using Degerli.Api.Infrastructure;
using Degerli.Core.Metrics;
using Degerli.Ingestion.Intraday;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Api.Stocks;

/// <summary>
/// The stocks module's Overview &amp; Valuation sections (`03-api-design.md` §3;
/// FR-RES-005/006/007/026, UC-RES-002/003):
/// <list type="bullet">
/// <item><c>GET /api/v1/stocks/{symbol}/overview</c> — the latest <c>published</c>
/// business description with <b>both</b> language texts and its last-reviewed date
/// (the client renders the user's language choice, FR-RES-005/022); when nothing is
/// published the honest <c>preparing</c> state is served — <c>200</c>, never a
/// <c>404</c>, never a leaked draft (FR-RES-026, BR-RES-002/003). Since
/// TKT-foundation-057 the payload also carries the instrument's identity —
/// canonical <c>symbol</c> + language-neutral official <c>name</c> (a blank name is
/// served as <c>null</c>) — so the stock page header can say which stock it is.</item>
/// <item><c>GET /api/v1/stocks/{symbol}/valuation</c> — the current canonical multiples
/// (read from <c>derived_metrics</c>, never computed here — BR-RES-004), the
/// corporate-action-adjusted historical price series with an honest <c>adjusted</c> flag
/// (true only when the served rows carry <c>close_adjusted</c>; FR-RES-006/018), and the
/// vs.-sector strip over <c>sector_metric_medians</c>
/// (<c>{value, notMeaningful, sectorMedian, peerCount, excludedCount}</c>;
/// FR-RES-007/SD-002, UXR-RES-024/025).</item>
/// </list>
/// Both are anonymous, per-section reads with the honest-data envelope (M-1/`03` §1.2);
/// an instrument outside the covered universe is <c>404 NOT_FOUND</c> (`03` §7).
/// </summary>
public static class StockOverviewAndValuationEndpoints
{
    /// <summary>The index code whose current constituents form the covered universe.</summary>
    private const string UniverseIndexCode = "XU100";

    /// <summary>The five valuation-family metrics the strip carries (confirmed OQ-UX-002).</summary>
    private static readonly string[] ValuationMetricCodes =
    [
        MetricCodes.Pe,
        MetricCodes.Pb,
        MetricCodes.EvEbitda,
        MetricCodes.EvFcf,
        MetricCodes.FcfYield,
    ];

    public static IEndpointRouteBuilder MapDegerliStockOverviewAndValuationEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/stocks/{symbol}/overview", OverviewAsync).AllowAnonymous();
        api.MapGet("/stocks/{symbol}/valuation", ValuationAsync).AllowAnonymous();
        return api;
    }

    // ── Overview ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The Overview section (TC-RES-005/006): the latest published description, or the
    /// honest "description in preparation" state (FR-RES-026). Drafts and reviewed-only
    /// rows are never served — only <c>published</c> (NFR-RES-006).
    /// </summary>
    private static async Task<IResult> OverviewAsync(
        string symbol,
        DegerliDbContext db,
        PriceStateService priceStates,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        // The instrument's identity rides the overview payload (TKT-foundation-057, user-approved
        // additive amendment): the stock page's header names the stock the user is looking at —
        // the canonical uppercase symbol and the language-neutral official name (`Instrument.Name`,
        // `02` §3.1). A blank/missing name is served as `null` — the client degrades to the symbol
        // alone rather than inventing one.
        var instrument = await Universe(db)
            .Where(candidate => candidate.Symbol == symbol.ToUpperInvariant())
            .Select(candidate => new { candidate.Id, candidate.Symbol, candidate.Name })
            .FirstOrDefaultAsync(cancellationToken);
        if (instrument is null)
        {
            return NotFound(symbol);
        }

        var identityName = string.IsNullOrWhiteSpace(instrument.Name) ? null : instrument.Name;
        var instrumentId = instrument.Id;

        var description = await db.BusinessDescriptions
            .Where(row => row.InstrumentId == instrumentId && row.Status == "published")
            .OrderByDescending(row => row.Version)
            .Select(row => new { row.TextTr, row.TextEn, row.LastReviewedAt })
            .FirstOrDefaultAsync(cancellationToken);

        var (asOf, stale) = await FreshnessAsync(db, clock, cancellationToken);

        // The intraday/close price state (01 §10.9.18, TKT-foundation-047 — the frozen
        // contract TKT-foundation-049's Web half consumes): the sweep's last price while the
        // session's EOD close is not stored, the stored close once it lands, the change vs.
        // the previous stored session's close, and the honest asOf of whichever store served.
        var priceState = await priceStates.GetInstrumentStateAsync(instrumentId, clock.GetUtcNow(), cancellationToken);

        if (description is null)
        {
            return Results.Ok(new
            {
                asOf,
                stale,
                priceState,
                status = "preparing",
                symbol = instrument.Symbol,
                name = identityName,
            });
        }

        return Results.Ok(new
        {
            asOf,
            stale,
            priceState,
            status = "published",
            textTr = description.TextTr,
            textEn = description.TextEn,
            lastReviewedAt = description.LastReviewedAt,
            symbol = instrument.Symbol,
            name = identityName,
        });
    }

    // ── Valuation ────────────────────────────────────────────────────────────

    /// <summary>
    /// The Valuation section (TC-RES-007/008, TC-MDF-030): current multiples, the historical
    /// price series with its honest <c>adjusted</c> basis flag, and the vs.-sector strip.
    /// </summary>
    private static async Task<IResult> ValuationAsync(
        string symbol,
        DegerliDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var instrument = await Universe(db)
            .Where(candidate => candidate.Symbol == symbol.ToUpperInvariant())
            .Select(candidate => new { candidate.Id, candidate.SectorId })
            .FirstOrDefaultAsync(cancellationToken);
        if (instrument is null)
        {
            return NotFound(symbol);
        }

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var asOf = await db.DerivedMetrics
            .MaxAsync(metric => (DateOnly?)metric.AsOfDate, cancellationToken)
            ?? today;
        var stale = asOf < LatestTradingDayOnOrBefore(today);

        // Current multiples — the canonical rows every other surface serves (BR-RES-004).
        var metricRows = await db.DerivedMetrics
            .Where(metric => metric.InstrumentId == instrument.Id
                && metric.AsOfDate == asOf
                && ValuationMetricCodes.Contains(metric.MetricCode))
            .Select(metric => new { metric.MetricCode, metric.Value })
            .ToListAsync(cancellationToken);
        var current = metricRows.ToDictionary(row => row.MetricCode, row => row.Value, StringComparer.Ordinal);

        // Historical price series (BR-MDF-010); raw and adjusted coexist so a later
        // action never fabricates a cliff. `closeAdjusted` is served as stored — never
        // coalesced to raw — so the payload does not present a raw close as an adjusted one.
        var series = await db.DailyPrices
            .Where(price => price.InstrumentId == instrument.Id)
            .OrderBy(price => price.PriceDate)
            .Select(price => new
            {
                date = price.PriceDate,
                closeRaw = price.CloseRaw,
                closeAdjusted = price.CloseAdjusted,
            })
            .ToListAsync(cancellationToken);

        // The served payload states which basis it carries (`02` §1.4, M-1/`03` §1.2): the
        // series is on the adjusted basis only when every served row actually carries
        // close_adjusted. When the adjusted column is absent (as today's ingestion writes
        // raw only), serve the rows as-is and flag `adjusted: false` — never assert an
        // adjusted basis over raw data (BR-MDF-010).
        var adjusted = series.Count > 0 && series.All(row => row.closeAdjusted is not null);

        // The vs.-sector strip: the stock's own current value next to its sector median,
        // with peer/exclusion counts (UXR-RES-024/025, FU §11.4).
        var mediansByMetric = new Dictionary<string, SectorMetricMedian>(StringComparer.Ordinal);
        if (instrument.SectorId is long sectorId)
        {
            var medians = await db.SectorMetricMedians
                .Where(median => median.SectorId == sectorId && median.AsOfDate == asOf)
                .ToListAsync(cancellationToken);
            foreach (var median in medians)
            {
                mediansByMetric[median.MetricCode] = median;
            }
        }

        var strip = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var code in ValuationMetricCodes)
        {
            var value = current.TryGetValue(code, out var metricValue) ? metricValue : null;
            mediansByMetric.TryGetValue(code, out var median);
            strip[code] = new
            {
                value,
                // An own value that is NULL is explicitly not-meaningful, never 0 (BR-RES-008).
                notMeaningful = value is null,
                sectorMedian = median?.MedianValue,
                peerCount = median?.PeerCount,
                excludedCount = median?.ExcludedCount,
            };
        }

        return Results.Ok(new
        {
            asOf,
            stale,
            current = ValuationMetricCodes.ToDictionary(
                code => code,
                code => current.TryGetValue(code, out var metricValue) ? metricValue : (decimal?)null,
                StringComparer.Ordinal),
            historical = new { adjusted, series },
            strip,
        });
    }

    // ── Shared helpers ───────────────────────────────────────────────────────

    /// <summary>Instruments with a current XU100 membership — the covered universe.</summary>
    private static IQueryable<Instrument> Universe(DegerliDbContext db) =>
        from instrument in db.Instruments
        join constituent in db.IndexConstituents on instrument.Id equals constituent.InstrumentId
        join index in db.Indices on constituent.IndexId equals index.Id
        where index.Code == UniverseIndexCode && constituent.EffectiveTo == null
        select instrument;

    /// <summary>
    /// The section freshness: the latest canonical daily-price date serves as the as-of date;
    /// it is stale when that date trails the latest trading day (`03` §1.2, FR-MDF-016).
    /// </summary>
    private static async Task<(DateOnly AsOf, bool Stale)> FreshnessAsync(
        DegerliDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var asOf = await db.DailyPrices
            .MaxAsync(price => (DateOnly?)price.PriceDate, cancellationToken)
            ?? today;

        return (asOf, asOf < LatestTradingDayOnOrBefore(today));
    }

    /// <summary>The latest Mon–Fri trading day at or before <paramref name="date"/>.</summary>
    private static DateOnly LatestTradingDayOnOrBefore(DateOnly date)
    {
        while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            date = date.AddDays(-1);
        }

        return date;
    }

    private static IResult NotFound(string symbol)
        => Results.Problem(ApiProblem.Create(
            StatusCodes.Status404NotFound,
            ApiErrorCodes.NotFound,
            new Dictionary<string, object?> { ["symbol"] = symbol.ToUpperInvariant() }));
}
