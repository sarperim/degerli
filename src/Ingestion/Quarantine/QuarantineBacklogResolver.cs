using System.Text.Json;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Validation;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Ingestion.Quarantine;

/// <summary>
/// One admin-triggered bulk-resolution run over the <c>CONFLICTING_VALUE</c> quarantine backlog
/// (TKT-foundation-035, policies settled 2026-10-29 — **keep-first**, no fact-semantics change):
/// how many rows this batch acknowledged, how many rows the backlog still retains, and the
/// ledger run that records it.
/// </summary>
/// <param name="RunId">The <c>ingest_runs</c> row this batch wrote — the audit trail.</param>
/// <param name="ReasonCode">The reason class this lane is allowed to touch (always
/// <see cref="QuarantineReason.ConflictingValue"/>; nothing else is ever acknowledged here).</param>
/// <param name="BatchSize">The bound that was applied to this run.</param>
/// <param name="Resolved">Rows acknowledged as accepted drift in this batch.</param>
/// <param name="Retained">Rows of the same class the backlog still retains, quarantined and
/// untouched (they keep their payloads and their <c>open</c> status until a later batch).</param>
/// <param name="ResolvedIds">The ids acknowledged in this batch, oldest first.</param>
public sealed record BulkQuarantineResolution(
    long RunId,
    string ReasonCode,
    int BatchSize,
    int Resolved,
    int Retained,
    IReadOnlyList<long> ResolvedIds);

/// <summary>
/// The paced, admin-triggered bulk-resolve/acknowledge lane over <c>CONFLICTING_VALUE</c>
/// quarantine rows (TKT-foundation-035, user-ratified policy **OPTION 1** of 2026-10-29).
/// <para>
/// <b>What it is for.</b> İş Yatırım EOD values drift between fetches (revision/rounding churn),
/// and the duplicate guard — correctly — quarantines the re-fetched conflicting value instead of
/// overwriting the stored fact (first-write wins, both values retained). The 2026-10 Monday
/// catch-up produced 74,008 such rows: operational noise, not a product defect, and nothing
/// fabricated or dropped. This lane lets the builder acknowledge a <b>bounded batch</b> of that
/// drift at a time.
/// </para>
/// <para>
/// <b>What it deliberately does not do.</b> It never deletes a row (BR-MDF-007), it never
/// modifies a stored fact (FR-MDF-015/NFR-MDF-002 — it does not even read a fact table), it
/// never promotes a quarantined payload into a fact, and it never touches another reason class.
/// Acknowledged rows take the store's existing terminal transition <c>open → dismissed</c> with
/// the operator's note as <c>resolution_note</c> (02 §3.1) — no new status, no schema change.
/// </para>
/// <para>
/// <b>Honesty.</b> Every run writes exactly one <c>ingest_runs</c> row under
/// <see cref="RunJobCode"/> — deliberately not a registered job, so no freshness query can
/// mistake this admin action for an ingestion run (the <c>holiday-skip</c> precedent, §10.9.16
/// TKT-foundation-048) — carrying the class, the bound, the resolved count, the retained count,
/// the operator's note and the acknowledged ids. Every batch commits its rows and its ledger
/// row in one transaction, so a crash never leaves an unrecorded acknowledgement.
/// </para>
/// <para>
/// <b>Bounded and paced.</b> A run acknowledges at most <see cref="MaxBatchSize"/> rows and takes
/// them oldest-first (<c>quarantined_at, id</c>), so repeated admin runs drain the backlog
/// deterministically and each run's cost is known in advance. Selection takes
/// <c>FOR UPDATE SKIP LOCKED</c>, so two concurrent admin runs can never acknowledge the same row
/// twice nor block each other.
/// </para>
/// </summary>
public static class QuarantineBacklogResolver
{
    /// <summary>The ledger job code for a bulk-resolution run (not a registered job).</summary>
    public const string RunJobCode = "quarantine-resolve";

    /// <summary>The batch bound applied when the request names none.</summary>
    public const int DefaultBatchSize = 500;

    /// <summary>The largest batch a single run may acknowledge.</summary>
    public const int MaxBatchSize = 1000;

    private const string OpenStatus = "open";
    private const string DismissedStatus = "dismissed";
    private const string AdminTrigger = "admin";

    private static readonly JsonSerializerOptions StatsJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Acknowledges the oldest <paramref name="batchSize"/> open <c>CONFLICTING_VALUE</c> rows as
    /// accepted drift and records the run in the ledger.
    /// </summary>
    public static async Task<BulkQuarantineResolution> ResolveConflictingValuesAsync(
        DegerliDbContext db,
        TimeProvider clock,
        int batchSize,
        string note,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrWhiteSpace(note);

        // The bound is validated by the endpoint before it reaches here; this guard makes the
        // class's own invariant explicit rather than trusting its caller.
        if (batchSize < 1 || batchSize > MaxBatchSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(batchSize),
                batchSize,
                $"A bulk-resolution batch must be between 1 and {MaxBatchSize} rows.");
        }

        var startedAt = clock.GetUtcNow();

        await using var transaction = await db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        // Oldest-first and bounded; SKIP LOCKED keeps two concurrent admin runs honest without
        // serializing them. This reads quarantine rows only — no fact table is involved.
        var batch = await db.QuarantinedFacts
            .FromSqlInterpolated($"""
                SELECT * FROM quarantined_facts
                WHERE status = {OpenStatus} AND reason_code = {QuarantineReason.ConflictingValue}
                ORDER BY quarantined_at, id
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var fact in batch)
        {
            // The existing terminal transition, never a delete and never a new status: the
            // accepted gap is recorded with the operator's note (BR-MDF-007, UXR-MDF-008).
            fact.Status = DismissedStatus;
            fact.ResolutionNote = note;
        }

        var resolvedIds = batch.Select(fact => fact.Id).ToList();

        // The rows this batch changed are only in the tracked state, so counting after the save
        // reports the backlog the operator will actually see next.
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var retained = await db.QuarantinedFacts
            .CountAsync(
                fact => fact.Status == OpenStatus && fact.ReasonCode == QuarantineReason.ConflictingValue,
                cancellationToken)
            .ConfigureAwait(false);

        var run = new IngestRun
        {
            JobCode = RunJobCode,
            StartedAt = startedAt,
            FinishedAt = clock.GetUtcNow(),
            Status = IngestResult.Succeeded,
            StatsJson = JsonSerializer.Serialize(new
            {
                // The runner's stats shape, so a ledger reader sees the same keys it always does
                // (the gate-skip / holiday-skip row precedent).
                written = 0,
                unchanged = 0,
                quarantined = 0,
                skipped = 0,
                reasonCode = QuarantineReason.ConflictingValue,
                batchSize,
                resolved = resolvedIds.Count,
                retained,
                note,
                trigger = AdminTrigger,
                resolvedIds,
            }, StatsJsonOptions),
        };

        db.IngestRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new BulkQuarantineResolution(
            run.Id,
            QuarantineReason.ConflictingValue,
            batchSize,
            resolvedIds.Count,
            retained,
            resolvedIds);
    }
}
