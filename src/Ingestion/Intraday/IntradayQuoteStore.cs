using Degerli.Ingestion.Validation;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Ingestion.Intraday;

/// <summary>
/// Fact storage for <c>intraday_quotes</c> (TKT-foundation-047, architecture §10.9.18).
/// One row per (instrument, session), <b>updated in place</b> by each sweep: the current
/// session's reading is the source's latest observation, so an identical re-observation is
/// counted <c>unchanged</c> (its <c>fetched_at</c> still advances — that is when the value
/// was last <i>seen</i>) and a changed value overwrites the row in place and is counted
/// <c>rewritten</c> (the TKT-foundation-042 input-aware-overwrite precedent).
/// <para>
/// <b>Never a close.</b> This store only writes <c>intraday_quotes</c>; <c>daily_prices</c>
/// stays the EOD lane's fact table untouched (an intraday reading must never be mistaken for
/// a session close). Sessions other than the one being swept are never addressed, so a prior
/// session's rows are immutable.
/// </para>
/// </summary>
public sealed class IntradayQuoteStore
{
    private readonly DegerliDbContext _db;

    public IntradayQuoteStore(DegerliDbContext db) => _db = db;

    /// <summary>
    /// Applies one symbol's intraday readings for <paramref name="sessionDate"/>. Readings for
    /// an unknown instrument, a missing/non-positive price, or a date that does not match the
    /// swept session are refused and reported (never silently dropped).
    /// </summary>
    public async Task<IntradayQuoteWriteResult> UpsertAsync(
        DateOnly sessionDate,
        string sourceRef,
        DateTimeOffset fetchedAt,
        IReadOnlyList<IntradayQuoteFact> facts,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRef);
        ArgumentNullException.ThrowIfNull(facts);

        if (facts.Count == 0)
        {
            return IntradayQuoteWriteResult.Empty;
        }

        var symbols = facts.Select(fact => fact.Symbol).Distinct(StringComparer.Ordinal).ToList();
        var instrumentIds = await _db.Instruments
            .Where(instrument => symbols.Contains(instrument.Symbol))
            .ToDictionaryAsync(instrument => instrument.Symbol, instrument => instrument.Id, cancellationToken)
            .ConfigureAwait(false);

        var existing = await _db.IntradayQuotes
            .Where(quote => quote.SessionDate == sessionDate)
            .ToDictionaryAsync(quote => quote.InstrumentId, cancellationToken)
            .ConfigureAwait(false);

        var inserted = 0;
        var unchanged = 0;
        var rewritten = 0;
        var rejected = new List<RejectedIntradayQuote>();

        foreach (var fact in facts)
        {
            if (!instrumentIds.TryGetValue(fact.Symbol, out var instrumentId)
                || fact.SessionDate != sessionDate)
            {
                rejected.Add(new RejectedIntradayQuote(fact, QuarantineReason.SchemaMismatch));
                continue;
            }

            if (fact.LastPrice is not { } lastPrice)
            {
                // A reading with no price is not a fact at all — the source's interval had no
                // trade (the adapter widens to the last available observation and yields
                // nothing when the whole session is empty, so this is a defence-in-depth
                // check). The honest class is a required field absent, never "negative".
                rejected.Add(new RejectedIntradayQuote(fact, QuarantineReason.SchemaMismatch));
                continue;
            }

            if (lastPrice <= 0m)
            {
                // A non-positive price is never a real market fact (FR-MDF-012, FU §2).
                rejected.Add(new RejectedIntradayQuote(fact, QuarantineReason.NegativePrice));
                continue;
            }

            if (existing.TryGetValue(instrumentId, out var stored))
            {
                var sameValue = stored.LastPrice == lastPrice && stored.DayVolume == fact.DayVolume;
                stored.LastPrice = lastPrice;
                stored.DayVolume = fact.DayVolume;
                stored.SourceRef = sourceRef;
                stored.FetchedAt = fetchedAt;
                if (sameValue)
                {
                    unchanged++;
                }
                else
                {
                    rewritten++;
                }

                continue;
            }

            var row = new IntradayQuote
            {
                InstrumentId = instrumentId,
                SessionDate = sessionDate,
                FetchedAt = fetchedAt,
                LastPrice = lastPrice,
                DayVolume = fact.DayVolume,
                SourceRef = sourceRef,
            };
            _db.IntradayQuotes.Add(row);
            existing[instrumentId] = row;
            inserted++;
        }

        if (inserted > 0 || rewritten > 0 || unchanged > 0)
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new IntradayQuoteWriteResult(inserted, unchanged, rewritten, rejected);
    }
}
