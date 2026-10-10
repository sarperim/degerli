using Degerli.Ingestion.Validation;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Ingestion.Prices;

/// <summary>
/// Fact storage for <c>daily_prices</c> (FR-MDF-001, FR-MDF-015; 02 §1 principles 1–2).
/// One row per (instrument, date): a re-send of an identical fact is an idempotent no-op
/// (TC-MDF-002), while a *valid* value that conflicts with an already-stored fact is
/// refused and never overwrites the stored row (TC-MDF-052). Writes are append-only —
/// existing rows are never mutated. The same invariants hold for a backfill range
/// (TKT-mdf-007): only the rows the source returns are stored.
/// </summary>
public sealed class DailyPriceStore
{
    private readonly DegerliDbContext _db;

    public DailyPriceStore(DegerliDbContext db) => _db = db;

    public Task<PriceWriteResult> UpsertAsync(
        DateOnly date,
        string sourceRef,
        DateTimeOffset recordedAt,
        IReadOnlyList<PriceFact> facts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return UpsertBatchAsync(sourceRef, recordedAt, facts.Select(f => (date, f)), cancellationToken);
    }

    /// <summary>
    /// Append-only upsert of a dated history range (TKT-mdf-007, FR-MDF-010): each row is
    /// processed under the same idempotency/conflict rules as the incremental ingest.
    /// </summary>
    public Task<PriceWriteResult> UpsertHistoryAsync(
        string sourceRef,
        DateTimeOffset recordedAt,
        IReadOnlyList<DatedPriceFact> rows,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return UpsertBatchAsync(
            sourceRef,
            recordedAt,
            rows.Select(r => (r.Date, ToPriceFact(r))),
            cancellationToken);
    }

    private async Task<PriceWriteResult> UpsertBatchAsync(
        string sourceRef,
        DateTimeOffset recordedAt,
        IEnumerable<(DateOnly Date, PriceFact Fact)> facts,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRef);

        var materialized = facts.ToList();
        if (materialized.Count == 0)
        {
            return PriceWriteResult.Empty;
        }

        var symbols = materialized.Select(f => f.Fact.Symbol).Distinct(StringComparer.Ordinal).ToList();
        var instrumentIds = await _db.Instruments
            .Where(i => symbols.Contains(i.Symbol))
            .ToDictionaryAsync(i => i.Symbol, i => i.Id, cancellationToken)
            .ConfigureAwait(false);

        var inserted = 0;
        var unchanged = 0;
        var conflicts = new List<PriceFact>();
        var rejected = new List<RejectedPriceFact>();

        foreach (var group in materialized.GroupBy(f => f.Date))
        {
            var existing = await _db.DailyPrices
                .Where(p => p.PriceDate == group.Key)
                .ToDictionaryAsync(p => p.InstrumentId, cancellationToken)
                .ConfigureAwait(false);

            foreach (var (date, fact) in group)
            {
                if (!instrumentIds.TryGetValue(fact.Symbol, out var instrumentId) || fact.Close is null)
                {
                    rejected.Add(new RejectedPriceFact(fact, QuarantineReason.SchemaMismatch));
                    continue;
                }

                if (fact.Close.Value < 0m)
                {
                    // A negative price is never a real market fact (FR-MDF-012, FU §2).
                    rejected.Add(new RejectedPriceFact(fact, QuarantineReason.NegativePrice));
                    continue;
                }

                if (existing.TryGetValue(instrumentId, out var stored))
                {
                    if (Matches(stored, fact))
                    {
                        unchanged++;
                    }
                    else
                    {
                        // A conflicting valid value: never overwrite (FR-MDF-015, Q1).
                        conflicts.Add(fact);
                    }

                    continue;
                }

                var row = new DailyPrice
                {
                    InstrumentId = instrumentId,
                    PriceDate = date,
                    Open = fact.Open,
                    High = fact.High,
                    Low = fact.Low,
                    CloseRaw = fact.Close.Value,
                    CloseAdjusted = null,
                    Volume = fact.Volume,
                    SourceRef = sourceRef,
                    RecordedAt = recordedAt,
                };
                _db.DailyPrices.Add(row);
                // Track the staged insert so a later row for the same (instrument, date)
                // in the same batch is seen as a conflict/dup rather than a second insert.
                existing[instrumentId] = row;
                inserted++;
            }
        }

        if (inserted > 0)
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new PriceWriteResult(inserted, unchanged, conflicts, rejected);
    }

    private static PriceFact ToPriceFact(DatedPriceFact row) =>
        new(row.Symbol, null, row.Open, row.High, row.Low, row.Close, row.Volume);

    /// <summary>Value equality on the stored fact fields; provenance differences alone
    /// (same values, re-sent) remain idempotent no-ops.</summary>
    private static bool Matches(DailyPrice stored, PriceFact fact) =>
        stored.Open == fact.Open
        && stored.High == fact.High
        && stored.Low == fact.Low
        && stored.CloseRaw == fact.Close
        && stored.Volume == fact.Volume;
}
