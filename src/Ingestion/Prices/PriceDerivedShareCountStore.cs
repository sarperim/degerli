using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Ingestion.Prices;

/// <summary>
/// What one derivation pass did (TKT-foundation-086, USER DECISION D5, 01 v1.5.60 §10.9.11).
/// <see cref="Written"/> is <see cref="Inserted"/> + <see cref="Updated"/> — the rows the pass
/// actually changed; <see cref="Unchanged"/> is the input-aware no-op (the 042/050 doctrine);
/// <see cref="Stale"/> is a fact older than the stored row (the monotonic as-of refusal).
/// <see cref="Withheld"/> counts, per <b>named reason</b>, the operand pairs that could not
/// honestly produce a count — a withheld fact writes nothing and never rounds.
/// </summary>
public sealed record PriceDerivedShareCountWriteResult(
    int Inserted,
    int Updated,
    int Unchanged,
    int Stale,
    IReadOnlyDictionary<string, int> Withheld)
{
    /// <summary>The rows this pass wrote (new + repaired in place).</summary>
    public int Written => Inserted + Updated;

    /// <summary>Every withheld fact of the pass, across all reasons.</summary>
    public int WithheldTotal => Withheld.Values.Sum();
}

/// <summary>
/// The per-instrument <b>price-derived share count</b> store (TKT-foundation-086, USER DECISION
/// D5, 01 v1.5.60 §10.9.11): the prices lane turns the payload it <b>already fetched</b> into an
/// honest share count by <c>count = PD ÷ close</c> — both operands are the source's own figures
/// for one instrument-date, and both are stored so the arithmetic is auditable from the row
/// (FR-MDF-008).
/// <para>
/// <b>The honesty rule.</b> A row is written only when <c>PD</c> is present and positive, the
/// close is positive, and <c>PD ÷ close</c> is integral within <see cref="IntegralTolerance"/>.
/// A missing/non-positive market cap is <see cref="MarketCapMissing"/>, a non-positive close is
/// <see cref="CloseInvalid"/>, and a non-integral quotient is <see cref="NonIntegralQuotient"/> —
/// three distinct <b>named, counted</b> skips. A non-integral quotient means the operands disagree
/// (GARAN's 2026-10-06/07 rows differ from a 4.2B count by ~100 shares), so it is withheld,
/// never rounded into a guess; the count is never zero. No nominal divisor and no
/// <c>/memberSecurities</c> division exists here — the 065 Option-B path stays forbidden.
/// </para>
/// <para>
/// <b>Input-aware and monotonic.</b> An identical re-run writes nothing and leaves
/// <c>recorded_at</c> (the served as-of of the write) unmoved; a changed fact repairs the row in
/// place and is counted. An <b>older</b> dated fact never regresses a row a newer one supplied —
/// it is counted as <see cref="PriceDerivedShareCountWriteResult.Stale"/>. A withheld fact leaves
/// any stored row alone: the row's own as-of is what makes a stale count honest, and deleting it
/// would be the same class of error as inventing one.
/// </para>
/// <para>
/// <b>No new fetch, no new source.</b> The store is called by the prices job with the decoded
/// facts it already holds; it reads/writes the database only. An instrument a fact's symbol does
/// not resolve to is out of scope (not a skip), exactly like the shares lane's unmatched lines.
/// </para>
/// </summary>
public sealed class PriceDerivedShareCountStore
{
    /// <summary>Reason code: the row carries no usable market cap (<c>PD</c> absent or ≤ 0).</summary>
    public const string MarketCapMissing = "PD_MISSING";

    /// <summary>Reason code: the row's close is absent or not positive.</summary>
    public const string CloseInvalid = "CLOSE_INVALID";

    /// <summary>Reason code: the operands disagree — <c>PD ÷ close</c> is not integral, so no count.</summary>
    public const string NonIntegralQuotient = "NON_INTEGRAL_QUOTIENT";

    /// <summary>
    /// The pinned integrality tolerance: a quotient within one ten-thousandth of an integer is
    /// that integer (the operands are published rounded, so an exact-equality rule would reject
    /// honest rows on a sub-share residue) — anything further off is a disagreement, never a
    /// rounding opportunity.
    /// </summary>
    public const decimal IntegralTolerance = 0.0001m;

    private readonly DegerliDbContext _db;

    public PriceDerivedShareCountStore(DegerliDbContext db) => _db = db;

    /// <summary>
    /// Derives and stores one row per resolvable instrument from the facts of one price date.
    /// </summary>
    /// <param name="sourceRef">Provenance of the operand pair (<c>isyatirim</c> — FR-MDF-008).</param>
    /// <param name="priceDate">The session date the operands belong to — the fact's as-of.</param>
    /// <param name="recordedAt">The instant of this write (unchanged re-runs do not move it).</param>
    /// <param name="facts">The decoded facts the lane already holds; none are fetched here.</param>
    public async Task<PriceDerivedShareCountWriteResult> UpsertAsync(
        string sourceRef,
        DateOnly priceDate,
        DateTimeOffset recordedAt,
        IReadOnlyList<PriceFact> facts,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRef);
        ArgumentNullException.ThrowIfNull(facts);

        var withheld = new Dictionary<string, int>(StringComparer.Ordinal);
        if (facts.Count == 0)
        {
            return new PriceDerivedShareCountWriteResult(0, 0, 0, 0, withheld);
        }

        var symbols = facts
            .Select(fact => fact.Symbol)
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var instruments = await _db.Instruments
            .Where(instrument => symbols.Contains(instrument.Symbol))
            .Select(instrument => new { instrument.Id, instrument.Symbol })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var bySymbol = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var instrument in instruments)
        {
            bySymbol[instrument.Symbol] = instrument.Id;
        }

        var instrumentIds = bySymbol.Values.ToList();
        var existing = await _db.PriceDerivedShareCounts
            .Where(row => instrumentIds.Contains(row.InstrumentId))
            .ToDictionaryAsync(row => row.InstrumentId, cancellationToken)
            .ConfigureAwait(false);

        var inserted = 0;
        var updated = 0;
        var unchanged = 0;
        var stale = 0;

        foreach (var fact in facts)
        {
            if (string.IsNullOrWhiteSpace(fact.Symbol)
                || !bySymbol.TryGetValue(fact.Symbol, out var instrumentId))
            {
                // Not one of our instruments — out of scope, not a skip.
                continue;
            }

            // The honesty core: three named refusals, each counted, none rounded.
            if (fact.MarketCap is not { } marketCap || marketCap <= 0m)
            {
                Count(withheld, MarketCapMissing);
                continue;
            }

            if (fact.Close is not { } close || close <= 0m)
            {
                Count(withheld, CloseInvalid);
                continue;
            }

            var quotient = marketCap / close;
            var rounded = decimal.Round(quotient, 0, MidpointRounding.ToZero);
            if (rounded < 1m || rounded > long.MaxValue || decimal.Abs(quotient - rounded) > IntegralTolerance)
            {
                // The operands disagree (or the quotient is not a usable count): withheld, never
                // rounded to the nearest billion.
                Count(withheld, NonIntegralQuotient);
                continue;
            }

            var count = (long)rounded;

            if (existing.TryGetValue(instrumentId, out var row))
            {
                if (priceDate < row.PriceDate)
                {
                    // Monotonic as-of: an older fact never regresses the row.
                    stale++;
                    continue;
                }

                if (priceDate == row.PriceDate
                    && row.SharesOutstanding == count
                    && row.MarketCapRaw == marketCap
                    && row.CloseRaw == close
                    && string.Equals(row.SourceRef, sourceRef, StringComparison.Ordinal))
                {
                    // Input-aware: identical operands → no write, `recorded_at` does not move.
                    unchanged++;
                    continue;
                }

                row.SharesOutstanding = count;
                row.MarketCapRaw = marketCap;
                row.CloseRaw = close;
                row.PriceDate = priceDate;
                row.RecordedAt = recordedAt;
                row.SourceRef = sourceRef;
                updated++;
                continue;
            }

            var created = new PriceDerivedShareCount
            {
                InstrumentId = instrumentId,
                SharesOutstanding = count,
                MarketCapRaw = marketCap,
                CloseRaw = close,
                PriceDate = priceDate,
                RecordedAt = recordedAt,
                SourceRef = sourceRef,
            };
            _db.PriceDerivedShareCounts.Add(created);
            // A second fact for the same instrument in this pass sees the row it just created
            // (one row per instrument, never a duplicate).
            existing[instrumentId] = created;
            inserted++;
        }

        if (inserted + updated > 0)
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new PriceDerivedShareCountWriteResult(inserted, updated, unchanged, stale, withheld);
    }

    private static void Count(IDictionary<string, int> counts, string reason) =>
        counts[reason] = counts.TryGetValue(reason, out var current) ? current + 1 : 1;
}
