using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Ingestion.Prices;

/// <summary>
/// Fact storage for <c>daily_prices</c> (FR-MDF-001, FR-MDF-015; 02 §1 principles 1–2).
/// One row per (instrument, date): a re-send of an identical fact is an idempotent no-op
/// (TC-MDF-002), while a *valid* value that conflicts with an already-stored fact is
/// refused and never overwrites the stored row (TC-MDF-052). Writes are append-only —
/// existing rows are never mutated.
/// </summary>
public sealed class DailyPriceStore
{
    private readonly DegerliDbContext _db;

    public DailyPriceStore(DegerliDbContext db) => _db = db;

    public async Task<PriceWriteResult> UpsertAsync(
        DateOnly date,
        string sourceRef,
        DateTimeOffset recordedAt,
        IReadOnlyList<PriceFact> facts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRef);

        var symbols = facts.Select(f => f.Symbol).Distinct(StringComparer.Ordinal).ToList();
        var instrumentIds = await _db.Instruments
            .Where(i => symbols.Contains(i.Symbol))
            .ToDictionaryAsync(i => i.Symbol, i => i.Id, cancellationToken)
            .ConfigureAwait(false);

        var existing = await _db.DailyPrices
            .Where(p => p.PriceDate == date)
            .ToDictionaryAsync(p => p.InstrumentId, cancellationToken)
            .ConfigureAwait(false);

        var inserted = 0;
        var unchanged = 0;
        var conflicts = new List<PriceFact>();
        var rejected = new List<RejectedPriceFact>();

        foreach (var fact in facts)
        {
            if (!instrumentIds.TryGetValue(fact.Symbol, out var instrumentId) || fact.Close is null)
            {
                rejected.Add(new RejectedPriceFact(fact, "SCHEMA_MISMATCH"));
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

            _db.DailyPrices.Add(new DailyPrice
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
            });
            inserted++;
        }

        if (inserted > 0)
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new PriceWriteResult(inserted, unchanged, conflicts, rejected);
    }

    /// <summary>Value equality on the stored fact fields; provenance differences alone
    /// (same values, re-sent) remain idempotent no-ops.</summary>
    private static bool Matches(DailyPrice stored, PriceFact fact) =>
        stored.Open == fact.Open
        && stored.High == fact.High
        && stored.Low == fact.Low
        && stored.CloseRaw == fact.Close
        && stored.Volume == fact.Volume;
}
