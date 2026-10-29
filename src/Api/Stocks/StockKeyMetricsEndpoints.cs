using System.Text.Json;
using Degerli.Api.Infrastructure;
using Degerli.Core.Metrics;
using Degerli.Core.Valuation;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Api.Stocks;

/// <summary>
/// The stocks module's Key-metrics &amp; capital-tracker section (`03-api-design.md` §3;
/// FR-MDF-013/014/018, FR-RES-006/007/009/010/027; the user's v0 row set recorded verbatim
/// on TKT-foundation-095 — "KEY VALUE METRICS &amp; MOAT INDICATORS" and
/// "CAPITAL &amp; DILUTION TRACKER"; TKT-foundation-096):
/// <list type="bullet">
/// <item><c>GET /api/v1/stocks/{symbol}/key-metrics</c> — per panel metric the
/// <b>current</b> canonical value read from <c>derived_metrics</c> (never computed here,
/// BR-RES-004/AD-06), the <b>5Y company median</b> from that same stored history, and the
/// <b>sector median</b> with its peer/exclusion counts from <c>sector_metric_medians</c>
/// (UXR-RES-024/025).</item>
/// <item>the <b>capital &amp; dilution tracker</b> block: paid-in capital (money, D4) and the
/// split-adjusted share count (the 086 price-derived store), each with its own as-of; the
/// year-on-year share-count change (honest N/M — no count history exists); the
/// <b>bedelli/bedelsiz</b> ratio history from <c>corporate_actions</c>; and the
/// <c>payout_ratio</c> reference.</item>
/// </list>
/// <para>
/// <b>The 5Y company median and the user's own honesty (FR-MDF-014 window honesty).</b> The stored
/// <c>derived_metrics</c> history is ~3.5 years deep today, so a nominal 5Y window is served
/// with its <b>actual depth</b> beside it (<c>depthYears</c>) and an observation count. Below
/// <see cref="MinimumCompanyMedianObservations"/> non-null observations the median is
/// <b>not served</b> (<c>null</c> + <c>notMeaningful</c>) — a two-observation "median" dressed
/// as a five-year statistic is exactly the fabrication the spec forbids. Nothing is ever
/// interpolated or extrapolated, and no window is lengthened to look deeper. The user's own
/// "5Y is marginal" caveat is why the window's actual depth is disclosed rather than assumed;
/// this is window honesty (FR-MDF-014), not one of the five pinned exclusions (no 10Y window
/// is exclusion #5, and this family does not offer one).
/// </para>
/// <para>
/// <b>Financial-sector companies are served honestly, never half-computed.</b> A
/// <c>bank</c>/<c>insurance</c> instrument gets the <c>sector_not_applicable</c> block
/// (<see cref="DcfSectorApplicability.SectorNotApplicableState"/>, the same verdict the DCF
/// lane records) naming the profile, and every profile-gated metric is served as
/// not-meaningful with <c>profileGated: true</c> — a bank's <c>pe</c>/<c>roe</c>/<c>roa</c>
/// still compute (their inputs are meaningful), while <c>ev_ebit</c>/<c>p_fcf</c>/
/// <c>cfo_ebit</c> and the rest of the industrial-shaped family are NULL by profile, never a
/// plausible number from a stray mapped line (<c>02</c> §6.2).
/// </para>
/// Anonymous, per-section read with the honest-data envelope (M-1/`03` §1.2); an instrument
/// outside the covered universe is <c>404 NOT_FOUND</c> (`03` §7).
/// </summary>
public static class StockKeyMetricsEndpoints
{
    /// <summary>The index code whose current constituents form the covered universe.</summary>
    private const string UniverseIndexCode = "XU100";

    /// <summary>The nominal window of the company-median column (`03` §3).</summary>
    public const int CompanyMedianWindowYears = 5;

    /// <summary>
    /// The pinned minimum number of non-null observations a 5Y company median needs
    /// (`02` §6.2, v2). One observation is one stored <c>derived_metrics</c> date, so this is
    /// a floor on how thin a history may be before the median is withheld: fewer than a
    /// dozen sessions of values is not a historical median, it is a handful of points. Below
    /// it the field is <c>null</c> with <c>notMeaningful: true</c> — never a fabricated
    /// figure and never a silently shortened window.
    /// </summary>
    public const int MinimumCompanyMedianObservations = 12;

    /// <summary>The metric code the capital tracker's payout reference reads (`02` §6.2).</summary>
    private const string PayoutRatio = MetricCodes.PayoutRatio;

    public static IEndpointRouteBuilder MapDegerliStockKeyMetricsEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/stocks/{symbol}/key-metrics", KeyMetricsAsync).AllowAnonymous();
        return api;
    }

    private static async Task<IResult> KeyMetricsAsync(
        string symbol,
        DegerliDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var instrument = await Universe(db)
            .Where(candidate => candidate.Symbol == symbol.ToUpperInvariant())
            .Select(candidate => new { candidate.Id, candidate.Symbol, candidate.SectorId, candidate.AccountingProfile })
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

        // ── current canonical values (read, never computed — BR-RES-004) ──────────────
        var panelCodes = MetricCodes.KeyMetricPanel;
        var currentRows = await db.DerivedMetrics
            .Where(metric => metric.InstrumentId == instrument.Id
                && metric.AsOfDate == asOf
                && panelCodes.Contains(metric.MetricCode))
            .Select(metric => new { metric.MetricCode, metric.Value, metric.WindowYears, metric.RegimeBreak, metric.IsAdjusted, metric.IsRested })
            .ToListAsync(cancellationToken);
        var current = currentRows.ToDictionary(row => row.MetricCode, row => row, StringComparer.Ordinal);

        // ── the 5Y company median, from the stored history of the same codes ──────────
        var historyFrom = asOf.AddYears(-CompanyMedianWindowYears);
        var historyRows = await db.DerivedMetrics
            .Where(metric => metric.InstrumentId == instrument.Id
                && metric.AsOfDate >= historyFrom
                && metric.AsOfDate <= asOf
                && panelCodes.Contains(metric.MetricCode)
                && metric.Value != null)
            .Select(metric => new { metric.MetricCode, metric.AsOfDate, metric.Value })
            .ToListAsync(cancellationToken);
        var companyMedians = CompanyMedians(historyRows
            .GroupBy(row => row.MetricCode, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => (row.AsOfDate, row.Value!.Value)).ToList(),
                StringComparer.Ordinal));

        // ── the sector median + its disclosed peer/exclusion counts ───────────────────
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

        var metrics = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var code in panelCodes)
        {
            if (!current.TryGetValue(code, out var row))
            {
                // No stored row for this instrument/date: the honest gap, not a zero.
                metrics[code] = new
                {
                    value = (decimal?)null,
                    notMeaningful = true,
                    asOf,
                    profileGated = CanonicalMetrics.IsProfileGated(code),
                    windowYears = (int?)null,
                    regimeBreak = (bool?)null,
                    isAdjusted = (bool?)null,
                    isRestated = (bool?)null,
                    companyMedian5y = CompanyMedianPayload(null, CompanyMedianWindowYears),
                    sectorMedian = (decimal?)null,
                    peerCount = (int?)null,
                    excludedCount = (int?)null,
                };
                continue;
            }

            mediansByMetric.TryGetValue(code, out var median);
            metrics[code] = new
            {
                value = row.Value,
                // A NULL value is explicitly not-meaningful, never 0 (BR-RES-008).
                notMeaningful = row.Value is null,
                asOf,
                profileGated = CanonicalMetrics.IsProfileGated(code),
                windowYears = row.WindowYears,
                regimeBreak = row.RegimeBreak,
                isAdjusted = row.IsAdjusted,
                isRestated = row.IsRested,
                companyMedian5y = CompanyMedianPayload(
                    companyMedians.TryGetValue(code, out var companyMedian) ? companyMedian : null,
                    CompanyMedianWindowYears),
                sectorMedian = median?.MedianValue,
                peerCount = median?.PeerCount,
                excludedCount = median?.ExcludedCount,
            };
        }

        // ── the capital & dilution tracker ───────────────────────────────────────────
        var capital = await CapitalTrackerAsync(db, instrument.Id, asOf, cancellationToken);

        return Results.Ok(new
        {
            asOf,
            stale,
            symbol = instrument.Symbol,
            accountingProfile = instrument.AccountingProfile,
            // A financial-sector instrument serves the same not-applicable verdict the DCF
            // lane records (02 §6.4): the industrial-shaped panel does not describe it, and
            // the client renders the bank notice instead of half-filled rows.
            financialSector = DcfSectorApplicability.IsFinancialSector(instrument.AccountingProfile)
                ? new
                {
                    state = DcfSectorApplicability.SectorNotApplicableState,
                    reason = DcfSectorApplicability.FinancialSectorReason,
                    profile = instrument.AccountingProfile,
                }
                : null,
            metrics,
            capitalTracker = capital,
        });
    }

    // ── the 5Y company median ────────────────────────────────────────────────────

    /// <summary>One code's company-median computation: the values, the observation count and the depth.</summary>
    private sealed record CompanyMedian(decimal? Value, int Observations, double DepthYears);

    /// <summary>
    /// The 5Y company median per code over the stored history (<c>02</c> §6.2, v2). The window
    /// is the caller's (<see cref="CompanyMedianWindowYears"/>), the observations are the
    /// non-null stored dates in it, and the median is the standard one
    /// (<see cref="MetricMath.Median"/>; an even count yields the mean of the two middle
    /// values). Withheld — <c>null</c> — when the observation count is below
    /// <see cref="MinimumCompanyMedianObservations"/>: the honest N/M the ~3.5-year depth
    /// demands, never a fabricated longer window.
    /// </summary>
    private static Dictionary<string, CompanyMedian> CompanyMedians(
        IReadOnlyDictionary<string, List<(DateOnly AsOfDate, decimal Value)>> history)
    {
        var medians = new Dictionary<string, CompanyMedian>(StringComparer.Ordinal);
        foreach (var (code, points) in history)
        {
            if (points.Count == 0)
            {
                medians[code] = new CompanyMedian(null, 0, 0d);
                continue;
            }

            var depth = (points.Max(point => point.AsOfDate).DayNumber
                - points.Min(point => point.AsOfDate).DayNumber) / 365.25d;
            var value = points.Count >= MinimumCompanyMedianObservations
                ? MetricMath.Median(points.Select(point => point.Value).ToList())
                : null;
            medians[code] = new CompanyMedian(value, points.Count, Math.Round(depth, 2));
        }

        return medians;
    }

    /// <summary>
    /// The served <c>companyMedian5y</c> block: the median (or the honest N/M), the window it
    /// nominally covers, the observations behind it and the depth the history actually reaches
    /// (the user's own "5Y is marginal" caveat — window honesty under FR-MDF-014, not one of the
    /// five pinned exclusions).
    /// </summary>
    private static object CompanyMedianPayload(CompanyMedian? median, int windowYears) => new
    {
        windowYears, // the nominal window the column is labelled with
        value = median?.Value,
        notMeaningful = median?.Value is null,
        observations = median?.Observations ?? 0,
        depthYears = median?.DepthYears ?? 0d, // the depth the stored history actually reaches
        minimumObservations = MinimumCompanyMedianObservations,
    };

    // ── the capital & dilution tracker ───────────────────────────────────────────

    /// <summary>
    /// The tracker block (the user's CAPITAL &amp; DILUTION TRACKER row set): paid-in capital
    /// (money — D4; never a share count), the split-adjusted share count from the 086
    /// price-derived store, each with its own as-of; the Y/Y share-count change (honest N/M:
    /// no count history is stored, so it is never guessed); the bedelli/bedelsiz ratio history
    /// read from the ingested <c>corporate_actions.terms_json</c>; and the <c>payout_ratio</c>
    /// reference from the canonical metrics.
    /// </summary>
    private static async Task<object> CapitalTrackerAsync(
        DegerliDbContext db,
        long instrumentId,
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        var paidIn = await db.ShareCounts
            .Where(row => row.InstrumentId == instrumentId)
            .Select(row => new { row.PaidInCapital, row.RecordedAt })
            .FirstOrDefaultAsync(cancellationToken);
        var derived = await db.PriceDerivedShareCounts
            .Where(row => row.InstrumentId == instrumentId)
            .Select(row => new { row.SharesOutstanding, row.PriceDate, row.CloseRaw, row.MarketCapRaw })
            .FirstOrDefaultAsync(cancellationToken);

        var actions = await db.CorporateActions
            .Where(action => action.InstrumentId == instrumentId
                && (action.ActionType == "rights_issue" || action.ActionType == "bonus_issue" || action.ActionType == "split"))
            .OrderByDescending(action => action.ActionDate)
            .Select(action => new { action.ActionType, action.ActionDate, action.TermsJson })
            .ToListAsync(cancellationToken);

        var payout = await db.DerivedMetrics
            .Where(metric => metric.InstrumentId == instrumentId
                && metric.MetricCode == PayoutRatio
                && metric.AsOfDate == asOf)
            .Select(metric => metric.Value)
            .FirstOrDefaultAsync(cancellationToken);

        var history = new List<object>(actions.Count);
        foreach (var action in actions)
        {
            var (kind, ratioKey) = action.ActionType switch
            {
                "rights_issue" => ("bedelli", "q"),
                "bonus_issue" => ("bedelsiz", "b"),
                _ => ("split", "n"),
            };
            var ratio = ReadRatio(action.TermsJson, ratioKey);
            if (ratio is null)
            {
                // Unreadable terms: the action is dated but its ratio is not invented.
                history.Add(new { date = action.ActionDate, type = action.ActionType, kind, ratio = (decimal?)null, percent = (decimal?)null });
                continue;
            }

            // The served percentage is the ingested ratio as-is: a bedelsiz `b` of 0.10 is
            // "+10% free shares"; a bedelli `q` of 0.5 is a 50% rights issue. Corporate-action
            // adjusted: these are the ratios the price/EPS series were adjusted by
            // (FR-MDF-004/018, UXR-RES-020).
            history.Add(new
            {
                date = action.ActionDate,
                type = action.ActionType,
                kind,
                ratio,
                percent = ratio.Value * 100m,
            });
        }

        return new
        {
            paidInCapital = paidIn?.PaidInCapital,
            paidInCapitalAsOf = paidIn is null ? (DateOnly?)null : DateOnly.FromDateTime(paidIn.RecordedAt.UtcDateTime),
            sharesOutstanding = derived?.SharesOutstanding,
            sharesOutstandingAsOf = derived?.PriceDate,
            // The count is the price-lane derivation (PD ÷ close), stated as what it is.
            sharesOutstandingBasis = derived is null ? null : "price-derived",
            shareCountChangeYoy = new
            {
                value = (decimal?)null,
                notMeaningful = true,
                // Never guessed: no share-count history is stored, so the year-on-year change
                // has no honest value today (the user's exclusion #4 posture).
                state = "no-count-history",
            },
            corporateActions = history,
            corporateActionsBasis = "corporate-action-adjusted",
            payoutRatio = new
            {
                value = payout,
                notMeaningful = payout is null,
                asOf,
            },
        };
    }

    /// <summary>
    /// Reads one ratio out of a stored <c>terms_json</c> document (the same canonical keys the
    /// adjustment system uses: <c>n</c> split, <c>b</c> bonus, <c>q</c> rights). Unreadable or
    /// absent terms yield <c>null</c> — the ratio is never guessed.
    /// </summary>
    private static decimal? ReadRatio(string? termsJson, string ratioKey)
    {
        if (string.IsNullOrWhiteSpace(termsJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(termsJson);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty(ratioKey, out var raw)
                && raw.ValueKind == JsonValueKind.Number
                && raw.TryGetDecimal(out var ratio)
                    ? ratio
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ── Shared helpers ───────────────────────────────────────────────────────────

    /// <summary>Instruments with a current XU100 membership — the covered universe.</summary>
    private static IQueryable<Instrument> Universe(DegerliDbContext db) =>
        from instrument in db.Instruments
        join constituent in db.IndexConstituents on instrument.Id equals constituent.InstrumentId
        join index in db.Indices on constituent.IndexId equals index.Id
        where index.Code == UniverseIndexCode && constituent.EffectiveTo == null
        select instrument;

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
