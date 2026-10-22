using Degerli.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Ingestion.Intraday;

/// <summary>
/// The served price state of one instrument — or of the market as a whole — at a given instant
/// (TKT-foundation-047, architecture §10.9.18; the frozen contract TKT-foundation-049's Web
/// half builds against).
/// <para>
/// <b>Semantics.</b> <see cref="State"/> is <c>intraday</c> while the described session has
/// intraday sweeps but no stored EOD close — including the window between the 18:00 Istanbul
/// close and the ~20:30 EOD chain, where the close in formation must never be relabelled as
/// final. It is <c>close</c> once the session's EOD close is stored in <c>daily_prices</c>;
/// weekends/holidays therefore serve the last completed session's close.
/// </para>
/// <para>
/// <see cref="LastPrice"/>/<see cref="PrevClose"/>/<see cref="ChangePct"/> come from the
/// described session's own store (the sweep's reading, or the EOD close); <see cref="PrevClose"/>
/// is the <b>previous stored session's</b> close — Mondays compare against Friday, holidays
/// against the last trading day, never against a fabricated calendar neighbour.
/// <see cref="ChangePct"/> is a <b>decimal fraction</b> (<c>0.05</c> = +5%), the platform-wide
/// contract (`03` §2/§4), rounded to the data model's numeric(18,6) granularity.
/// </para>
/// <see cref="AsOf"/> is the honest observation instant: the sweep's <c>fetched_at</c> in the
/// intraday state, the stored close's <c>recorded_at</c> in the close state.
/// </summary>
public sealed record PriceStateView(
    string State,
    decimal? LastPrice,
    decimal? PrevClose,
    decimal? ChangePct,
    DateTimeOffset AsOf,
    DateOnly SessionDate)
{
    /// <summary>The session is in progress (or its close is not yet published): not final.</summary>
    public const string Intraday = "intraday";

    /// <summary>The session's EOD close is stored: final.</summary>
    public const string Close = "close";
}

/// <summary>
/// Resolves the served price state (TKT-foundation-047, architecture §10.9.18) from the two
/// stores — <c>intraday_quotes</c> for the in-progress reading, <c>daily_prices</c> for the
/// final close. <b>Stored facts are the truth</b> (the §10.9.13 doctrine): the session is
/// whatever the stores hold, never a weekday guess, so a weekend or holiday serves the last
/// completed session and a failed EOD night keeps serving the intraday state honestly.
/// <para>
/// The numeric fields are NULL for the market-level state: this lane ingests instrument
/// quotes, not intraday index levels, and aggregating one here would be computing
/// (BR-MDF-009). The Web consumes <see cref="PriceStateView.State"/>,
/// <see cref="PriceStateView.AsOf"/> and <see cref="PriceStateView.SessionDate"/> there, and
/// the full numeric block on the stock page.
/// </para>
/// </summary>
public sealed class PriceStateService
{
    /// <summary>The index whose current constituents form the market's covered universe.</summary>
    private const string UniverseIndexCode = "XU100";

    /// <summary>The data model's numeric(18,6) granularity (the market aggregate's convention).</summary>
    private const int Scale = 6;

    private readonly DegerliDbContext _db;

    public PriceStateService(DegerliDbContext db) => _db = db;

    /// <summary>
    /// The market-level price state: the session described by the covered universe's stored
    /// facts. <c>null</c> when neither store holds anything to describe (nothing is invented).
    /// </summary>
    public async Task<PriceStateView?> GetMarketStateAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var xu100Id = await _db.Indices
            .Where(index => index.Code == UniverseIndexCode)
            .Select(index => (long?)index.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (xu100Id is null)
        {
            return null;
        }

        var memberIds = _db.IndexConstituents
            .Where(constituent => constituent.IndexId == xu100Id.Value && constituent.EffectiveTo == null)
            .Select(constituent => constituent.InstrumentId);

        var closeSession = await _db.DailyPrices
            .Where(price => memberIds.Contains(price.InstrumentId) && price.PriceDate <= today)
            .MaxAsync(price => (DateOnly?)price.PriceDate, cancellationToken)
            .ConfigureAwait(false);

        var intradaySession = await _db.IntradayQuotes
            .Where(quote => memberIds.Contains(quote.InstrumentId) && quote.SessionDate <= today)
            .MaxAsync(quote => (DateOnly?)quote.SessionDate, cancellationToken)
            .ConfigureAwait(false);

        if (intradaySession is { } session
            && (closeSession is null || session > closeSession.Value))
        {
            // The session has sweeps but no stored EOD close yet: intraday, "not final".
            var asOf = await _db.IntradayQuotes
                .Where(quote => quote.SessionDate == session && memberIds.Contains(quote.InstrumentId))
                .MaxAsync(quote => (DateTimeOffset?)quote.FetchedAt, cancellationToken)
                .ConfigureAwait(false);
            if (asOf is { } observedAt)
            {
                return new PriceStateView(PriceStateView.Intraday, null, null, null, observedAt, session);
            }
        }

        if (closeSession is { } closeDay)
        {
            var asOf = await _db.DailyPrices
                .Where(price => price.PriceDate == closeDay && memberIds.Contains(price.InstrumentId))
                .MaxAsync(price => (DateTimeOffset?)price.RecordedAt, cancellationToken)
                .ConfigureAwait(false);

            return new PriceStateView(PriceStateView.Close, null, null, null, asOf ?? now, closeDay);
        }

        return null;
    }

    /// <summary>
    /// One instrument's price state: the intraday reading while the session is open, the stored
    /// EOD close once it lands. <c>null</c> when the instrument has neither.
    /// </summary>
    public async Task<PriceStateView?> GetInstrumentStateAsync(
        long instrumentId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        // The two most recent stored sessions: the described one and its predecessor (the
        // `prevClose` reference — Monday vs Friday, holiday vs the last trading day).
        var closeSessions = await _db.DailyPrices
            .Where(price => price.InstrumentId == instrumentId && price.PriceDate <= today)
            .OrderByDescending(price => price.PriceDate)
            .Take(2)
            .Select(price => new { price.PriceDate, price.CloseRaw, price.RecordedAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var closeSession = closeSessions.FirstOrDefault();

        var intradaySession = await _db.IntradayQuotes
            .Where(quote => quote.InstrumentId == instrumentId && quote.SessionDate <= today)
            .MaxAsync(quote => (DateOnly?)quote.SessionDate, cancellationToken)
            .ConfigureAwait(false);

        if (intradaySession is { } session
            && (closeSession is null || session > closeSession.PriceDate))
        {
            var reading = await _db.IntradayQuotes
                .SingleAsync(
                    quote => quote.InstrumentId == instrumentId && quote.SessionDate == session,
                    cancellationToken)
                .ConfigureAwait(false);

            var previousClose = closeSession?.CloseRaw;
            return new PriceStateView(
                PriceStateView.Intraday,
                reading.LastPrice,
                previousClose,
                ChangePct(reading.LastPrice, previousClose),
                reading.FetchedAt,
                session);
        }

        if (closeSession is { } close)
        {
            var previousClose = closeSessions.Count > 1 ? closeSessions[1].CloseRaw : (decimal?)null;
            return new PriceStateView(
                PriceStateView.Close,
                close.CloseRaw,
                previousClose,
                ChangePct(close.CloseRaw, previousClose),
                close.RecordedAt,
                close.PriceDate);
        }

        return null;
    }

    /// <summary>
    /// The change vs. the previous session's close as a decimal fraction, rounded to the
    /// data model's granularity; NULL when there is no usable previous close (cold start) —
    /// never a fabricated 0.
    /// </summary>
    private static decimal? ChangePct(decimal lastPrice, decimal? previousClose) =>
        previousClose is > 0m
            ? Math.Round((lastPrice - previousClose.Value) / previousClose.Value, Scale, MidpointRounding.AwayFromZero)
            : null;
}
