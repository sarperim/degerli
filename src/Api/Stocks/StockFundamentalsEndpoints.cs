using Degerli.Api.Infrastructure;
using Degerli.Core.Metrics;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Api.Stocks;

/// <summary>
/// The stocks module's Financials, Profitability, Growth, Balance-sheet and Dividends
/// sections (`03-api-design.md` §3; FR-RES-008..012/014..016/027, UC-RES-003):
/// <list type="bullet">
/// <item><c>GET /api/v1/stocks/{symbol}/financials</c> — per-period revenue → EBITDA →
/// EBIT → NI → FCF with per-figure <c>restated</c> flags; the latest restated statement
/// version serves and as-reported is retained (BR-MDF-011).</item>
/// <item><c>GET /api/v1/stocks/{symbol}/profitability</c> — canonical ROIC/ROE/margins
/// read from <c>derived_metrics</c> (BR-RES-004: the endpoint computes nothing).</item>
/// <item><c>GET /api/v1/stocks/{symbol}/growth</c> — 3/5/10Y CAGRs, each with the actual
/// <c>windowYears</c>; a short history falls back to the available span, and no history
/// at all is an explicit no-data state naming the listing-date coverage boundary
/// (FR-MDF-014, FR-RES-016/UXR-RES-016).</item>
/// <item><c>GET /api/v1/stocks/{symbol}/balance-sheet</c> — BS lines + book value +
/// BVPS = equity / shares (FR-RES-027).</item>
/// <item><c>GET /api/v1/stocks/{symbol}/dividends</c> — dated dividend history, or the
/// explicit never-paid state (SCR-005 §7).</item>
/// </list>
/// All five are anonymous, per-section reads with the honest-data envelope (M-1/`03`
/// §1.2): missing data is <c>200</c> + <c>state</c>/<c>coverage</c>, never blank-as-zero
/// (BR-RES-008). An instrument outside the covered universe is <c>404 NOT_FOUND</c>
/// (`03` §7).
/// </summary>
public static class StockFundamentalsEndpoints
{
    /// <summary>The index code whose current constituents form the covered universe.</summary>
    private const string UniverseIndexCode = "XU100";

    // ── Canonical chart-of-accounts item codes (02 §6.1) ─────────────────────
    private const string Rev = "REV";
    private const string Ebit = "EBIT";
    private const string DeprAmort = "DEPR_AMORT";
    private const string Ebitda = "EBITDA";
    private const string Ni = "NI";
    private const string Capex = "CAPEX";
    private const string Ocf = "OCF";
    private const string Cash = "CASH";
    private const string StDebt = "ST_DEBT";
    private const string LtDebt = "LT_DEBT";
    private const string TotalEquity = "TOTAL_EQUITY";
    private const string CurAssets = "CUR_ASSETS";
    private const string CurLiab = "CUR_LIAB";
    private const string SharesDiluted = "SHARES_DILUTED";
    private const string SharesOut = "SHARES_OUT";

    /// <summary>The four quality-metric codes the profitability section carries (02 §6.2).</summary>
    private static readonly string[] ProfitabilityMetricCodes =
    [
        MetricCodes.Roic,
        MetricCodes.Roe,
        MetricCodes.GrossMargin,
        MetricCodes.OperatingMargin,
    ];

    /// <summary>The growth families and their window-suffixed metric-code prefixes (Q5).</summary>
    private static readonly (string Family, string Prefix)[] GrowthFamilies =
    [
        ("rev", "rev_cagr"),
        ("eps", "eps_cagr"),
        ("fcf", "fcf_cagr"),
        ("div", "div_cagr"),
    ];

    public static IEndpointRouteBuilder MapDegerliStockFundamentalsEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/stocks/{symbol}/financials", FinancialsAsync).AllowAnonymous();
        api.MapGet("/stocks/{symbol}/profitability", ProfitabilityAsync).AllowAnonymous();
        api.MapGet("/stocks/{symbol}/growth", GrowthAsync).AllowAnonymous();
        api.MapGet("/stocks/{symbol}/balance-sheet", BalanceSheetAsync).AllowAnonymous();
        api.MapGet("/stocks/{symbol}/dividends", DividendsAsync).AllowAnonymous();
        return api;
    }

    // ── Financials (FR-RES-008/014) ──────────────────────────────────────────

    /// <summary>
    /// Per-period revenue → EBITDA → EBIT → NI → FCF (TC-RES-009). The latest statement
    /// version serves per statement type — the restated row when one exists — and each
    /// figure carries its own <c>restated</c> flag (FR-RES-014, BR-MDF-011). Derivations
    /// use the canonical definitions (02 §6.1/§6.2): EBITDA = EBIT + D&amp;A when not
    /// reported; FCF = OCF − |CAPEX| (the KAP-reported operating cash flow less the
    /// absolute capital expenditure — the same definition the metrics engine serves).
    /// </summary>
    private static async Task<IResult> FinancialsAsync(
        string symbol,
        DegerliDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var instrument = await ResolveInstrumentAsync(db, symbol, cancellationToken);
        if (instrument is null)
        {
            return NotFound(symbol);
        }

        var statements = await db.FinancialStatements
            .Where(statement => statement.InstrumentId == instrument.Id)
            .ToListAsync(cancellationToken);
        var lines = await LoadLinesAsync(db, statements, cancellationToken);

        var periods = new List<Dictionary<string, object?>>();
        foreach (var group in statements
            .GroupBy(statement => (statement.PeriodType, statement.PeriodEndDate))
            .OrderBy(group => group.Key.PeriodEndDate)
            .ThenBy(group => group.Key.PeriodType, StringComparer.Ordinal))
        {
            var rows = group.ToList();
            var is0 = Pick(rows, "IS");
            var cf0 = Pick(rows, "CF");
            var isRestated = IsRestated(is0);
            var fcfRestated = IsRestated(is0) || IsRestated(cf0);

            var ebit = Line(is0, Ebit, lines);
            var depr = Line(is0, DeprAmort, lines);
            var ebitda = Line(is0, Ebitda, lines) ?? Sum(ebit, depr);
            var ni = Line(is0, Ni, lines);
            var fcf = CanonicalMetrics.FreeCashFlow(Line(cf0, Ocf, lines), Line(cf0, Capex, lines));

            periods.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["periodType"] = group.Key.PeriodType,
                ["fiscalYear"] = rows[0].FiscalYear,
                ["periodEndDate"] = group.Key.PeriodEndDate,
                ["revenue"] = Figure(Line(is0, Rev, lines), isRestated),
                ["ebitda"] = Figure(ebitda, isRestated),
                ["ebit"] = Figure(ebit, isRestated),
                ["netIncome"] = Figure(ni, isRestated),
                ["fcf"] = Figure(fcf, fcfRestated),
            });
        }

        var (asOf, stale) = await FreshnessAsync(db, clock, cancellationToken);
        return Results.Ok(new { asOf, stale, periods });
    }

    // ── Profitability (FR-RES-009) ───────────────────────────────────────────

    /// <summary>
    /// The canonical ROIC/ROE/gross- and operating-margin values (TC-RES-010), read from
    /// <c>derived_metrics</c> — the same rows the screener and DCF serve (BR-RES-004). A
    /// NULL is explicitly not-meaningful, never 0 (BR-RES-008).
    /// </summary>
    private static async Task<IResult> ProfitabilityAsync(
        string symbol,
        DegerliDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var instrument = await ResolveInstrumentAsync(db, symbol, cancellationToken);
        if (instrument is null)
        {
            return NotFound(symbol);
        }

        var (asOf, stale) = await FreshnessAsync(db, clock, cancellationToken);
        var current = await CurrentMetricsAsync(db, instrument.Id, ProfitabilityMetricCodes, cancellationToken);

        var metrics = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var code in ProfitabilityMetricCodes)
        {
            var value = current.TryGetValue(code, out var metricValue) ? metricValue : null;
            metrics[code] = new { value, notMeaningful = value is null };
        }

        return Results.Ok(new { asOf, stale, metrics });
    }

    // ── Growth (FR-RES-010/016) ──────────────────────────────────────────────

    /// <summary>
    /// The 3/5/10Y CAGRs for revenue/EPS/FCF/DPS, each with the interval actually used
    /// (TC-RES-011). A short history reports <c>windowYears &lt; window</c>; an instrument
    /// with no CAGR at all is an explicit no-data state naming the listing-date coverage
    /// boundary — never fabricated (FR-RES-016, UXR-RES-022).
    /// </summary>
    private static async Task<IResult> GrowthAsync(
        string symbol,
        DegerliDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var instrument = await ResolveInstrumentAsync(db, symbol, cancellationToken);
        if (instrument is null)
        {
            return NotFound(symbol);
        }

        var (asOf, stale) = await FreshnessAsync(db, clock, cancellationToken);

        var codes = GrowthFamilies
            .SelectMany(family => MetricCodes.CagrWindows.Select(window => $"{family.Prefix}_{window}y"))
            .ToList();
        var metricAsOf = await db.DerivedMetrics
            .Where(metric => metric.InstrumentId == instrument.Id)
            .MaxAsync(metric => (DateOnly?)metric.AsOfDate, cancellationToken);
        var rows = metricAsOf is null
            ? []
            : await db.DerivedMetrics
                .Where(metric => metric.InstrumentId == instrument.Id
                    && metric.AsOfDate == metricAsOf
                    && codes.Contains(metric.MetricCode))
                .ToDictionaryAsync(metric => metric.MetricCode, cancellationToken);

        var hasData = rows.Values.Any(row => row.Value is not null);
        if (!hasData)
        {
            return Results.Ok(DataEnvelope.Missing(
                asOf,
                DataStates.NoData,
                new DataCoverage(instrument.ListingDate?.ToString("yyyy-MM-dd"), "insufficient financial history"),
                stale));
        }

        var metrics = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (family, prefix) in GrowthFamilies)
        {
            var windows = MetricCodes.CagrWindows.Select(window =>
            {
                rows.TryGetValue($"{prefix}_{window}y", out var row);
                return new
                {
                    window,
                    windowYears = row?.WindowYears,
                    value = row?.Value,
                };
            }).ToList();
            metrics[family] = windows;
        }

        // The per-metric basis flags (03 §4, v1.4): the TMS 29 regime-break marker
        // (FR-MDF-013/014), the actual CAGR window, and the adjusted/restated basis
        // markers — keyed by the window-suffixed code so the Growth-section markers
        // (UXR-RES-026/027) read the server's answer rather than recomputing it.
        var metricFlags = rows.ToDictionary(
            pair => pair.Key,
            pair => (object)new
            {
                windowYears = pair.Value.WindowYears,
                regimeBreak = pair.Value.RegimeBreak,
                isAdjusted = pair.Value.IsAdjusted,
                isRestated = pair.Value.IsRested,
            },
            StringComparer.Ordinal);

        return Results.Ok(new { asOf, stale, metrics, metricFlags });
    }

    // ── Balance sheet (FR-RES-011/027) ───────────────────────────────────────

    /// <summary>
    /// The latest FY balance-sheet lines plus book value (total equity) and BVPS
    /// (equity / shares) — TC-RES-012. Book value per share reads the same equity/shares
    /// ratio the canonical P/B metric is defined over (02 §6.2).
    /// </summary>
    private static async Task<IResult> BalanceSheetAsync(
        string symbol,
        DegerliDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var instrument = await ResolveInstrumentAsync(db, symbol, cancellationToken);
        if (instrument is null)
        {
            return NotFound(symbol);
        }

        var (asOf, stale) = await FreshnessAsync(db, clock, cancellationToken);

        var statements = await db.FinancialStatements
            .Where(statement => statement.InstrumentId == instrument.Id
                && statement.StatementType == "BS"
                && statement.PeriodType == "FY")
            .ToListAsync(cancellationToken);
        var lines = await LoadLinesAsync(db, statements, cancellationToken);

        var latest = statements
            .OrderByDescending(statement => statement.PeriodEndDate)
            .FirstOrDefault();
        if (latest is null)
        {
            return Results.Ok(DataEnvelope.Missing(
                asOf,
                DataStates.NoData,
                new DataCoverage(instrument.ListingDate?.ToString("yyyy-MM-dd"), "no balance sheet reported"),
                stale));
        }

        // The restated version serves when present (BR-MDF-011).
        var candidates = statements.Where(statement => statement.PeriodEndDate == latest.PeriodEndDate).ToList();
        var sheet = Pick(candidates, "BS") ?? latest;
        var restated = IsRestated(sheet);

        var equity = Line(sheet, TotalEquity, lines);
        var shares = Line(sheet, SharesDiluted, lines);
        var bvps = equity is not null && shares is > 0m ? equity.Value / shares.Value : (decimal?)null;

        var bsLines = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["cash"] = Figure(Line(sheet, Cash, lines), restated),
            ["shortTermDebt"] = Figure(Line(sheet, StDebt, lines), restated),
            ["longTermDebt"] = Figure(Line(sheet, LtDebt, lines), restated),
            ["totalEquity"] = Figure(equity, restated),
            ["currentAssets"] = Figure(Line(sheet, CurAssets, lines), restated),
            ["currentLiabilities"] = Figure(Line(sheet, CurLiab, lines), restated),
            ["sharesDiluted"] = Figure(shares, restated),
            ["sharesOutstanding"] = Figure(Line(sheet, SharesOut, lines), restated),
        };

        return Results.Ok(new
        {
            asOf,
            stale,
            periodType = sheet.PeriodType,
            fiscalYear = sheet.FiscalYear,
            periodEndDate = sheet.PeriodEndDate,
            restated,
            lines = bsLines,
            bookValue = Figure(equity, restated),
            bookValuePerShare = Figure(bvps, restated),
        });
    }

    // ── Dividends (FR-RES-012) ───────────────────────────────────────────────

    /// <summary>
    /// The dated dividend history (ex-date, pay date, amount) with the trailing-twelve-
    /// month DPS, or the explicit never-paid state when no dividend row exists
    /// (TC-RES-013, SCR-005 §7) — never an error, never blanks.
    /// </summary>
    private static async Task<IResult> DividendsAsync(
        string symbol,
        DegerliDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var instrument = await ResolveInstrumentAsync(db, symbol, cancellationToken);
        if (instrument is null)
        {
            return NotFound(symbol);
        }

        var (asOf, stale) = await FreshnessAsync(db, clock, cancellationToken);

        var dividends = await db.Dividends
            .Where(dividend => dividend.InstrumentId == instrument.Id)
            .OrderBy(dividend => dividend.ExDate)
            .Select(dividend => new
            {
                dividend.ExDate,
                dividend.PayDate,
                dividend.AmountPerShare,
                dividend.Currency,
            })
            .ToListAsync(cancellationToken);

        if (dividends.Count == 0)
        {
            return Results.Ok(DataEnvelope.Missing(
                asOf,
                DataStates.NoData,
                new DataCoverage(instrument.ListingDate?.ToString("yyyy-MM-dd"), "no dividends paid"),
                stale));
        }

        // TTM DPS uses the same trailing-year ex-date window the Metrics Engine applies
        // to div_yield (02 §6.2); it is an aggregation of facts, not a stored metric.
        var ttmFrom = asOf.AddDays(-365);
        var ttmPerShare = dividends
            .Where(dividend => dividend.ExDate >= ttmFrom && dividend.ExDate <= asOf)
            .Sum(dividend => dividend.AmountPerShare);

        return Results.Ok(new
        {
            asOf,
            stale,
            dividends,
            ttmPerShare,
        });
    }

    // ── Shared helpers ───────────────────────────────────────────────────────

    /// <summary>The projection the section endpoints need from the covered universe.</summary>
    private sealed record CoveredInstrument(long Id, string Symbol, DateOnly? ListingDate);

    /// <summary>Resolves a symbol to its current-universe instrument, or <c>null</c> when unknown/delisted.</summary>
    private static async Task<CoveredInstrument?> ResolveInstrumentAsync(
        DegerliDbContext db,
        string symbol,
        CancellationToken cancellationToken)
        => await Universe(db)
            .Where(instrument => instrument.Symbol == symbol.ToUpperInvariant())
            .Select(instrument => new CoveredInstrument(instrument.Id, instrument.Symbol, instrument.ListingDate))
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Instruments with a current XU100 membership — the covered universe.</summary>
    private static IQueryable<Instrument> Universe(DegerliDbContext db) =>
        from instrument in db.Instruments
        join constituent in db.IndexConstituents on instrument.Id equals constituent.InstrumentId
        join index in db.Indices on constituent.IndexId equals index.Id
        where index.Code == UniverseIndexCode && constituent.EffectiveTo == null
        select instrument;

    /// <summary>Loads the line-item maps for the supplied statements, keyed by statement id then item code.</summary>
    private static async Task<IReadOnlyDictionary<long, Dictionary<string, decimal>>> LoadLinesAsync(
        DegerliDbContext db,
        IReadOnlyList<FinancialStatement> statements,
        CancellationToken cancellationToken)
    {
        var statementIds = statements.Select(statement => statement.Id).ToList();
        var lines = await db.FinLineItems
            .Where(line => statementIds.Contains(line.StatementId))
            .ToListAsync(cancellationToken);
        return lines
            .GroupBy(line => line.StatementId)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(line => line.ItemCode, line => line.Value, StringComparer.Ordinal));
    }

    /// <summary>Picks the latest served version of a statement type: the highest version.</summary>
    private static FinancialStatement? Pick(IReadOnlyList<FinancialStatement> rows, string statementType)
        => rows
            .Where(row => row.StatementType == statementType)
            .OrderByDescending(row => row.Version)
            .FirstOrDefault();

    private static bool IsRestated(FinancialStatement? statement) => statement is not null && statement.Version > 0;

    private static decimal? Line(
        FinancialStatement? statement,
        string code,
        IReadOnlyDictionary<long, Dictionary<string, decimal>> lines)
        => statement is not null
            && lines.TryGetValue(statement.Id, out var map)
            && map.TryGetValue(code, out var value)
                ? value
                : null;

    private static decimal? Sum(decimal? left, decimal? right) =>
        left is null || right is null ? null : left.Value + right.Value;

    /// <summary>A figure with its restatement marker; a NULL carries an explicit no-data state.</summary>
    private static object Figure(decimal? value, bool restated) =>
        value is null
            ? new { value = (decimal?)null, restated, state = DataStates.NoData }
            : new { value = (decimal?)value, restated };

    /// <summary>The current canonical metric values at the latest computed date.</summary>
    private static async Task<Dictionary<string, decimal?>> CurrentMetricsAsync(
        DegerliDbContext db,
        long instrumentId,
        IReadOnlyList<string> codes,
        CancellationToken cancellationToken)
    {
        var metricAsOf = await db.DerivedMetrics
            .Where(metric => metric.InstrumentId == instrumentId)
            .MaxAsync(metric => (DateOnly?)metric.AsOfDate, cancellationToken);
        if (metricAsOf is null)
        {
            return new Dictionary<string, decimal?>(StringComparer.Ordinal);
        }

        var rows = await db.DerivedMetrics
            .Where(metric => metric.InstrumentId == instrumentId
                && metric.AsOfDate == metricAsOf
                && codes.Contains(metric.MetricCode))
            .Select(metric => new { metric.MetricCode, metric.Value })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.MetricCode, row => row.Value, StringComparer.Ordinal);
    }

    /// <summary>
    /// The section freshness: the latest canonical daily-price date serves as the as-of
    /// date; it is stale when that date trails the latest trading day (`03` §1.2, M-1).
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
