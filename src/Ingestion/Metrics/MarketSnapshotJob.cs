using Degerli.Ingestion.Jobs;

namespace Degerli.Ingestion.Metrics;

/// <summary>
/// The nightly <c>market-snapshot</c> job (FR-MOV-001..006, 021; BR-MOV-009/010/011,
/// NFR-MOV-001): compute the market overview snapshot for the target trading day and
/// write it to <c>market_snapshots</c> (one row per trading day, idempotent). Registered
/// like every other job (TKT-mdf-002 convention) so the scheduler chain, the run ledger
/// and the admin trigger pick it up by code.
/// <para>
/// <b>Target-day selection (<c>01</c> §10.9.13; TKT-foundation-028).</b> The day the
/// formulas run over is the <b>latest trading day ≤ <c>request.Date</c> that has stored
/// prices for the covered universe</b> — never blindly <c>request.Date</c>. Stored prices
/// are the truth (no weekday/holiday rule): a Saturday/Sunday request resolves the
/// preceding Friday, so the weekend overview serves real Friday data, and a missed-run or
/// cold-start self-heals. When <c>request.Date</c> is itself a trading day with prices the
/// resolution is a no-op and the row lands on that day (behaviour unchanged).
/// </para>
/// <para>
/// <b>Input-aware same-day recompute (<c>01</c> §10.9.13; TKT-foundation-042).</b> The
/// resolved day is recomputed from the current facts on every run and written
/// input-aware: a completed day is never *blindly* rewritten — identical inputs report an
/// honest <c>unchanged: 1</c>, while changed inputs (index levels or prices landing after
/// the first compute) repair the existing row in place and count as a repair, so
/// operational repair never needs SQL surgery. Exactly one row per trading day is
/// preserved (TC-MOV-005), and days other than the resolved one are never touched. With no
/// stored price ≤ <c>request.Date</c> the honest empty rule stands: <c>Succeeded</c>, 0
/// written, no fabricated row and the overview keeps serving <c>state: unavailable</c>.
/// </para>
/// </summary>
public sealed class MarketSnapshotJob : IIngestJob
{
    /// <summary>Stable job code used by the ledger, scheduler and admin trigger.</summary>
    public const string Code = "market-snapshot";

    private readonly MarketSnapshotEngine _engine;
    private readonly MarketSnapshotStore _store;
    private readonly TimeProvider _clock;

    public MarketSnapshotJob(MarketSnapshotEngine engine, MarketSnapshotStore store, TimeProvider clock)
    {
        _engine = engine;
        _store = store;
        _clock = clock;
    }

    /// <inheritdoc />
    public string JobCode => Code;

    /// <inheritdoc />
    public async Task<IngestResult> RunAsync(
        IngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        // The day the formulas run over is the latest trading day at or before the
        // requested date that actually has stored prices for the covered universe
        // (01 §10.9.13) — stored prices are the truth, not request.Date and not a
        // weekday/holiday assumption.
        var target = await _store
            .ResolveLatestStoredTradingDayAsync(request.Date, cancellationToken)
            .ConfigureAwait(false);
        if (target is null)
        {
            // No covered-universe price ≤ the requested date — nothing to snapshot
            // (no fabricated row; the honest empty rule, unchanged).
            return new IngestResult(Code, 0, 0, 0, IngestResult.Succeeded, Skipped: 0);
        }

        // The resolved day is recomputed from the current facts and the store decides:
        // identical → unchanged, changed → in-place repair (01 §10.9.13; TKT-foundation-042).
        // The guard is input-aware, never a blind no-op: a broken row is repaired by a
        // plain re-run, not by SQL surgery.
        var computation = await _engine.ComputeAsync(target.Value, cancellationToken).ConfigureAwait(false);
        if (computation is null)
        {
            // A resolved day whose facts still do not yield a covered constituent (e.g. a
            // member with a price but no shares) — no fabricated row.
            return new IngestResult(Code, 0, 0, 0, IngestResult.Succeeded, Skipped: 0);
        }

        var outcome = await _store
            .UpsertAsync(target.Value, computation, _clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

        // An in-place repair is a written row for this run too; the store keeps the insert /
        // repair / unchanged distinction so a dedicated `rewritten` ledger stat can report
        // the repair on its own.
        return new IngestResult(
            Code,
            outcome.Written + outcome.Rewritten,
            outcome.Unchanged,
            0,
            IngestResult.Succeeded);
    }
}
