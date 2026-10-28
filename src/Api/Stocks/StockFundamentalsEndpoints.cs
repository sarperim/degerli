using System.Text.Json;
using Degerli.Api.Infrastructure;
using Degerli.Core.Metrics;
using Degerli.Core.Valuation;
using Degerli.Ingestion.Dividends;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Statements;
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
/// explicit empty state with the server's availability signal (SCR-005 §7).</item>
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

    // ── The statements view's [C] row inputs (02 §6.1, TKT-foundation-094) ───
    private const string GrossProfit = "GROSS_PROFIT";
    private const string PretaxInc = "PRETAX_INC";
    private const string TaxExp = "TAX_EXP";
    private const string NiParent = "NI_PARENT";
    private const string Goodwill = "GOODWILL";
    private const string IntangiblesNet = "INTANGIBLES_NET";
    private const string MinorityInt = "MINORITY_INT";

    /// <summary>
    /// The TMS 29 (inflation-accounting) regime transition the badge derives from
    /// (<c>02</c> §6.2): a period ending on or after this date is served with
    /// <c>tms29 = true</c>. It is a derivation from the served period end — never a claim
    /// about the filing's content beyond that pinned transition.
    /// </summary>
    private static readonly DateOnly Tms29From = new(2023, 12, 31);

    /// <summary>The evidence classes that make the served statement a KAP filing (`kap-public`
    /// public-site read, `kap` the VYK gateway) — the KAP Kaynak badge input.</summary>
    private static readonly HashSet<string> KapSourceRefs =
        new(StringComparer.Ordinal) { "kap-public", "kap" };

    /// <summary>
    /// The layout notice served for a financial-sector accounting profile (<c>02</c> §6.2
    /// posture). The profile set itself is the shared predicate
    /// <see cref="DcfSectorApplicability.IsFinancialSector"/> — one definition, not a third copy.
    /// </summary>
    private const string FinancialSectorLayoutNotice =
        "This company reports under a financial-sector accounting profile (bank/insurance): the "
        + "industrial statements layout does not apply. Bank-format statement labels are a later "
        + "slice, so no industrial row set is rendered for it.";

    /// <summary>The layout notice's state word: explicitly <b>not applicable</b>, never an
    /// error and never a no-data claim about the company's filings.</summary>
    private const string LayoutNotApplicableState = "not-applicable";

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
    /// The statements-view supply (FR-RES-008/014/015/016/027, TC-MDF-123): per period, the
    /// compat five figures (revenue → EBITDA → EBIT → NI → FCF, each with its own
    /// <c>restated</c> flag — unchanged) plus <c>lines</c> (the flat canonical-code →
    /// <c>{value, restated}</c> map per statement type, with the <c>OTHER_*</c> passthrough
    /// block beside it), <c>derived</c> (the server-computed [C] rows — margins, Y/Y growth,
    /// NWC, gross/net debt, TBV, the per-share rows), <c>span</c> (the filing's own declared
    /// cumulative range) and <c>consolidated</c>. The payload's filing envelope carries the
    /// badge inputs (consolidated from the stored flag, TMS 29 from the pinned <c>02</c> §6.2
    /// transition, KAP Kaynak from the served <c>source_ref</c>), the display-only EV header
    /// facts, and the financial-sector layout notice.
    /// <para>
    /// <b>A financial-sector profile serves no industrial row set.</b> For
    /// <c>accounting_profile ∈ {bank, insurance}</c> the period keeps its compat five figures,
    /// its <c>span</c> and its <c>consolidated</c> flag, but <c>lines</c>/<c>otherLines</c> serve
    /// the IS/BS/CF block with every map empty, <c>derived</c> serves empty and <c>evHeader</c>
    /// is served <c>null</c> — the layout notice is the signal, and no half-filled industrial
    /// table (nor an EV header read from the same industrial-mapped stored lines) is handed to a
    /// client (<c>reit</c>/<c>industrial</c> keep the natural row set).
    /// </para>
    /// <para>
    /// <b>Every [C] row is honest-null on a missing operand</b> (the <c>Line()</c>/<c>Sum()</c>
    /// seam's discipline): a ratio whose denominator is absent or zero, a sum with one absent
    /// addend and a per-share row without a share count serve <c>null</c> + the explicit
    /// <c>NO_DATA</c> state — never a guessed zero (BR-RES-008). The DR-2 gross-debt row
    /// consumes exactly the stored <c>ST_DEBT</c> + <c>LT_DEBT</c> (the fenced narrow
    /// definition, never widened to leases); TBV consumes the stored
    /// <c>TOTAL_EQUITY</c> − <c>GOODWILL</c> − <c>INTANGIBLES_NET</c>.
    /// </para>
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

        // The financial-sector layout verdict (`02` §6.2), from the one shared predicate the DCF
        // and the metrics gate already use: no industrial row set is served for these profiles.
        var financialSectorLayout = DcfSectorApplicability.IsFinancialSector(instrument.AccountingProfile);

        var statements = await db.FinancialStatements
            .Where(statement => statement.InstrumentId == instrument.Id)
            .ToListAsync(cancellationToken);
        var lines = await LoadLinesAsync(db, statements, cancellationToken);

        // The share-count precedence's second tier (086): the price lane's own count, used only
        // when the served statement declares no SHARES_DILUTED (USER DECISION D5 — a count, never
        // the paid-in-capital money figure).
        var derivedCount = await db.PriceDerivedShareCounts
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.InstrumentId == instrument.Id, cancellationToken);
        var priceCount = derivedCount is { SharesOutstanding: > 0 } ? derivedCount.SharesOutstanding : (long?)null;

        var entries = statements
            .GroupBy(statement => (statement.PeriodType, statement.PeriodEndDate))
            .OrderBy(group => group.Key.PeriodEndDate)
            .ThenBy(group => group.Key.PeriodType, StringComparer.Ordinal)
            .Select(group =>
            {
                var rows = group.ToList();
                return new PeriodEntry(
                    group.Key.PeriodType,
                    group.Key.PeriodEndDate,
                    rows[0].FiscalYear,
                    Pick(rows, "IS"),
                    Pick(rows, "BS"),
                    Pick(rows, "CF"));
            })
            .ToList();

        var byPeriod = entries.ToDictionary(
            entry => (entry.PeriodType, entry.PeriodEndDate));

        var periods = new List<Dictionary<string, object?>>();
        foreach (var entry in entries)
        {
            var is0 = entry.IncomeStatement;
            var bs0 = entry.BalanceSheet;
            var cf0 = entry.CashFlow;
            var isRestated = IsRestated(is0);
            var fcfRestated = IsRestated(is0) || IsRestated(cf0);
            var bsRestated = IsRestated(bs0);

            var ebit = Line(is0, Ebit, lines);
            var depr = Line(is0, DeprAmort, lines);
            var ebitda = Line(is0, Ebitda, lines) ?? Sum(ebit, depr);
            var ni = Line(is0, Ni, lines);
            var fcf = CanonicalMetrics.FreeCashFlow(Line(cf0, Ocf, lines), Line(cf0, Capex, lines));

            // ── The [C] rows ────────────────────────────────────────────────
            var revenue = Line(is0, Rev, lines);
            var grossProfit = Line(is0, GrossProfit, lines);
            var pretax = Line(is0, PretaxInc, lines);
            var tax = Line(is0, TaxExp, lines);
            var niParent = Line(is0, NiParent, lines);
            var curAssets = Line(bs0, CurAssets, lines);
            var curLiab = Line(bs0, CurLiab, lines);
            var stDebt = Line(bs0, StDebt, lines);
            var ltDebt = Line(bs0, LtDebt, lines);
            var cash = Line(bs0, Cash, lines);
            var equity = Line(bs0, TotalEquity, lines);

            var grossDebt = Sum(stDebt, ltDebt);              // DR-2 fenced: exactly ST + LT
            var netDebt = Subtract(grossDebt, cash);
            var nwc = Subtract(curAssets, curLiab);
            var tbv = Subtract(Subtract(equity, Line(bs0, Goodwill, lines)),
                Line(bs0, IntangiblesNet, lines));

            // Statement-reported diluted shares first, the price-derived count second; any other
            // route (a paid-in-capital-derived guess) is the user's exclusion #4 and never taken.
            var statementShares = Line(bs0, SharesDiluted, lines);
            var shareCount = statementShares is > 0m
                ? statementShares
                : priceCount is > 0 ? priceCount : (decimal?)null;
            var shareSourceRestated = statementShares is > 0m && bsRestated;

            var prior = byPeriod.TryGetValue((entry.PeriodType, entry.PeriodEndDate.AddYears(-1)), out var priorEntry)
                ? priorEntry
                : null;

            var derived = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["revenueGrowthYoy"] = Figure(
                    Subtract(
                        Ratio(Line(is0, Rev, lines), prior is null ? null : Line(prior.IncomeStatement, Rev, lines)),
                        1m),
                    isRestated || IsRestated(prior?.IncomeStatement)),
                ["grossMargin"] = Figure(Ratio(grossProfit, revenue), isRestated),
                ["operatingMargin"] = Figure(Ratio(ebit, revenue), isRestated),
                ["ebitdaMargin"] = Figure(Ratio(ebitda, revenue), isRestated),
                ["effectiveTaxRate"] = Figure(Ratio(tax, pretax), isRestated),
                ["netMarginParent"] = Figure(Ratio(niParent, revenue), isRestated),
                ["netWorkingCapital"] = Figure(nwc, bsRestated),
                ["grossDebt"] = Figure(grossDebt, bsRestated),
                ["netDebt"] = Figure(netDebt, bsRestated),
                ["tangibleBookValue"] = Figure(tbv, bsRestated),
                ["bvps"] = Figure(PerShare(equity, shareCount), bsRestated || shareSourceRestated),
                ["tbvPerShare"] = Figure(PerShare(tbv, shareCount), bsRestated || shareSourceRestated),
                ["fcfPerShare"] = Figure(PerShare(fcf, shareCount), fcfRestated || shareSourceRestated),
            };

            periods.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["periodType"] = entry.PeriodType,
                ["fiscalYear"] = entry.FiscalYear,
                ["periodEndDate"] = entry.PeriodEndDate,
                ["revenue"] = Figure(revenue, isRestated),
                ["ebitda"] = Figure(ebitda, isRestated),
                ["ebit"] = Figure(ebit, isRestated),
                ["netIncome"] = Figure(ni, isRestated),
                ["fcf"] = Figure(fcf, fcfRestated),
                ["span"] = SpanOf(is0, cf0),
                ["consolidated"] = ConsolidatedOf(entry),
                // A financial-sector profile serves the notice, never a half-filled industrial
                // table: the row-set keys keep their shape with every map empty.
                ["lines"] = financialSectorLayout ? EmptyRowSet() : StatementLines(is0, bs0, cf0, lines),
                ["otherLines"] = financialSectorLayout ? EmptyRowSet() : StatementOtherLines(is0, bs0, cf0, lines),
                ["derived"] = financialSectorLayout
                    ? new Dictionary<string, object?>(StringComparer.Ordinal)
                    : derived,
            });
        }

        var (asOf, stale) = await FreshnessAsync(db, clock, cancellationToken);

        // The EV header inputs are served as their own facts with their own as-of, display-only:
        // the DCF's own debt/cash/share-count inputs are untouched (02 §6.2, FR-RES-008).
        // A financial-sector layout is served no EV header at all: its inputs are the same
        // industrial-mapped stored lines the row set suppresses (the notice verdict is the signal,
        // and an EV reading beside an empty `derived` would be the asymmetry it exists to avoid).
        var latestPrice = await db.DailyPrices
            .AsNoTracking()
            .Where(price => price.InstrumentId == instrument.Id)
            .OrderByDescending(price => price.PriceDate)
            .Select(price => new { price.PriceDate, price.CloseAdjusted })
            .FirstOrDefaultAsync(cancellationToken);
        var latestSheet = entries
            .Where(entry => entry.BalanceSheet is not null)
            .OrderByDescending(entry => entry.PeriodEndDate)
            .Select(entry => entry.BalanceSheet)
            .FirstOrDefault();

        object? evHeader = financialSectorLayout
            ? null
            : new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["sharePrice"] = Fact(latestPrice?.CloseAdjusted, latestPrice?.PriceDate),
                ["marketCap"] = Fact(derivedCount?.MarketCapRaw, derivedCount?.PriceDate),
                ["grossDebt"] = Fact(
                    Sum(Line(latestSheet, StDebt, lines), Line(latestSheet, LtDebt, lines)),
                    latestSheet?.PeriodEndDate),
                // The stored `CASH` line only: the chart carries no short-term-investments code, so
                // the fact is named for exactly what it serves and never promises a sum.
                ["cash"] = Fact(Line(latestSheet, Cash, lines), latestSheet?.PeriodEndDate),
                ["minorityInterest"] = Fact(Line(latestSheet, MinorityInt, lines), latestSheet?.PeriodEndDate),
            };

        // The filing the controls bar describes: the latest served period's own evidence. Every
        // badge is derived from a served fact — an absent flag suppresses the badge, never a
        // guessed value (095 renders only what is served).
        var latest = entries.LastOrDefault();
        object? filing = null;
        if (latest is not null)
        {
            var latestStatements = new[] { latest.IncomeStatement, latest.BalanceSheet, latest.CashFlow }
                .Where(statement => statement is not null)
                .Select(statement => statement!)
                .ToList();
            var consolidated = ConsolidatedOf(latest);
            filing = new
            {
                periodEndDate = latest.PeriodEndDate.ToString("yyyy-MM-dd"),
                sourceRef = (latest.IncomeStatement ?? latest.BalanceSheet ?? latest.CashFlow)?.SourceRef,
                consolidated,
                tms29 = latest.PeriodEndDate >= Tms29From,
                kapSource = latestStatements.Any(statement => KapSourceRefs.Contains(statement.SourceRef)),
            };
        }

        object? layoutNotice = financialSectorLayout
            ? new
            {
                accountingProfile = instrument.AccountingProfile,
                applies = true,
                state = LayoutNotApplicableState,
                message = FinancialSectorLayoutNotice,
            }
            : null;

        return Results.Ok(new { asOf, stale, filing, layoutNotice, evHeader, periods });
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

        // The market-data facts (TKT-foundation-086, 01 v1.5.60 §10.9.11), two distinct stores
        // and two distinct units:
        //  • paidInCapital — the `share_counts` store's figure, <b>TRY</b> (USER DECISION D4:
        //    the `/memberSecurities` capital is money, never a share count), own as-of.
        //  • sharesOutstanding — the price-lane derived count (`PD ÷ close`, USER DECISION D5),
        //    own as-of (the price date), honest absence otherwise.
        // Neither is a replacement for the statement-reported `lines.sharesDiluted`/
        // `lines.sharesOutstanding` figures above; an absent fact serves null, never zero.
        var storedCapital = await db.ShareCounts
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.InstrumentId == instrument.Id, cancellationToken);
        var derivedCount = await db.PriceDerivedShareCounts
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.InstrumentId == instrument.Id, cancellationToken);
        var shareCount = new
        {
            paidInCapital = storedCapital?.PaidInCapital,
            paidInCapitalAsOf = storedCapital is null
                ? null
                : DateOnly.FromDateTime(storedCapital.RecordedAt.UtcDateTime).ToString("yyyy-MM-dd"),
            sharesOutstanding = derivedCount?.SharesOutstanding,
            sharesOutstandingAsOf = derivedCount is null
                ? null
                : derivedCount.PriceDate.ToString("yyyy-MM-dd"),
        };

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
            shareCount,
            bookValue = Figure(equity, restated),
            bookValuePerShare = Figure(bvps, restated),
        });
    }

    // ── Dividends (FR-RES-012) ───────────────────────────────────────────────

    /// <summary>
    /// The <c>coverage_metadata.data_type</c> of the dividends family's per-instrument
    /// cursor — written by the adapter only after a company walk completed (01 §10.9.11/§10.9.15).
    /// </summary>
    private const string DividendsCursorDataType = "kap-dividends-cursor";

    /// <summary>Instrument-scoped coverage rows (02 §3.1).</summary>
    private const string InstrumentCoverageScope = "instrument";

    /// <summary>
    /// The dated dividend history (ex-date, pay date, amount) with the trailing-twelve-
    /// month DPS, or the explicit empty state (TC-RES-013/037, SCR-005 §7) — never an
    /// error, never blanks.
    /// <para>
    /// A zero-row table is <b>not</b> evidence that the company never paid a dividend
    /// (TKT-foundation-052): it is equally the shape of "the dividends lane has not
    /// covered this instrument yet". The payload therefore carries
    /// <c>dividendsAvailable</c> — the server's coverage/run-state signal — so the three
    /// honest states are distinguishable: not processed (no signal backing), processed
    /// with zero rows (the backed never-paid state) and populated.
    /// </para>
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
            // Availability comes from the lane's coverage/run state, never from the row
            // count: the client must not read "never paid" out of an empty table.
            var available = await DividendsAvailableAsync(db, instrument, cancellationToken);
            return Results.Ok(new
            {
                asOf,
                stale,
                dividendsAvailable = available,
                state = DataStates.NoData,
                coverage = new DataCoverage(
                    instrument.ListingDate?.ToString("yyyy-MM-dd"),
                    available
                        ? "dividends data available; no dividend paid in the covered history"
                        : "dividends data not ingested for this instrument yet"),
            });
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
            dividendsAvailable = true,
            dividends,
            ttmPerShare,
        });
    }

    /// <summary>
    /// Whether the dividends lane has covered this instrument — the evidence that an empty
    /// dividends table means "no dividend paid" rather than "not ingested yet"
    /// (TKT-foundation-052).
    /// <para>
    /// Two sources, both the lane's own state: (1) the per-instrument family cursor, which
    /// the adapter persists only after a company's VYK walk completed; (2) the latest
    /// terminal <c>dividends</c> run in the ledger. A <c>partial</c> run isolates the
    /// constituents it could not walk (046's per-symbol stats, FR-MDF-012) — an instrument
    /// named failed there is <b>not</b> covered, so it keeps the neutral state. Rows always
    /// mean available (the caller only asks when there are none).
    /// </para>
    /// </summary>
    private static async Task<bool> DividendsAvailableAsync(
        DegerliDbContext db,
        CoveredInstrument instrument,
        CancellationToken cancellationToken)
    {
        var walked = await db.CoverageMetadata
            .AnyAsync(
                coverage => coverage.Scope == InstrumentCoverageScope
                    && coverage.InstrumentId == instrument.Id
                    && coverage.DataType == DividendsCursorDataType,
                cancellationToken);
        if (walked)
        {
            return true;
        }

        var run = await db.IngestRuns
            .Where(candidate => candidate.JobCode == DividendsJob.Code
                && (candidate.Status == IngestResult.Succeeded || candidate.Status == IngestResult.Partial))
            .OrderByDescending(candidate => candidate.FinishedAt ?? candidate.StartedAt)
            .ThenByDescending(candidate => candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (run is null)
        {
            return false;
        }

        // A succeeded pass walked every resolvable constituent. A partial pass walked all
        // but the ones it isolated — those are named in the run's per-symbol stats.
        return run.Status == IngestResult.Succeeded
            || !IsNamedFailedSymbol(run.StatsJson, instrument.Symbol);
    }

    /// <summary>
    /// True when the run's <c>stats_json.symbols</c> names this symbol as failed (the 046
    /// isolation channel). Unreadable stats are treated as "not covered": the signal is
    /// only ever raised on evidence, never assumed.
    /// </summary>
    private static bool IsNamedFailedSymbol(string? statsJson, string symbol)
    {
        if (string.IsNullOrWhiteSpace(statsJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(statsJson);
            if (!TryGetProperty(document.RootElement, "symbols", out var symbols)
                || symbols.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var entry in symbols.EnumerateArray())
            {
                if (TryGetProperty(entry, "symbol", out var named)
                    && named.ValueKind == JsonValueKind.String
                    && string.Equals(named.GetString(), symbol, StringComparison.OrdinalIgnoreCase)
                    && TryGetProperty(entry, "failed", out var failed)
                    && failed.ValueKind == JsonValueKind.True)
                {
                    return true;
                }
            }

            return false;
        }
        catch (JsonException)
        {
            // An unreadable breakdown cannot back a "no dividend" claim.
            return true;
        }
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    // ── Shared helpers ───────────────────────────────────────────────────────

    /// <summary>The projection the section endpoints need from the covered universe.</summary>
    private sealed record CoveredInstrument(
        long Id,
        string Symbol,
        DateOnly? ListingDate,
        string AccountingProfile);

    /// <summary>
    /// One served period: its key and the statement version serving each type (<c>null</c> when
    /// the period carries no statement of that type) — the statements view's unit of work.
    /// </summary>
    private sealed record PeriodEntry(
        string PeriodType,
        DateOnly PeriodEndDate,
        int? FiscalYear,
        FinancialStatement? IncomeStatement,
        FinancialStatement? BalanceSheet,
        FinancialStatement? CashFlow);

    /// <summary>Resolves a symbol to its current-universe instrument, or <c>null</c> when unknown/delisted.</summary>
    private static async Task<CoveredInstrument?> ResolveInstrumentAsync(
        DegerliDbContext db,
        string symbol,
        CancellationToken cancellationToken)
        => await Universe(db)
            .Where(instrument => instrument.Symbol == symbol.ToUpperInvariant())
            .Select(instrument => new CoveredInstrument(
                instrument.Id,
                instrument.Symbol,
                instrument.ListingDate,
                instrument.AccountingProfile))
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

    /// <summary>
    /// Picks the served statement of one type from a period group: the highest restatement
    /// version of the <b>preferred source</b> (<see cref="StatementSourcePreference"/>, <c>01</c>
    /// v1.5.57 §10.9.19, TC-MDF-111) — the public-site current fact wins for the periods it
    /// covers, the incumbent's retained rows serve otherwise, and the choice never depends on the
    /// rows' arrival order.
    /// </summary>
    private static FinancialStatement? Pick(IReadOnlyList<FinancialStatement> rows, string statementType)
        => StatementSourcePreference.Pick(rows, statementType);

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

    /// <summary>A difference whose absent operand makes the whole row null — the honest-null
    /// discipline the [C] rows share (never a difference from an assumed zero).</summary>
    private static decimal? Subtract(decimal? left, decimal? right) =>
        left is null || right is null ? null : left.Value - right.Value;

    /// <summary>A ratio, null when either side is absent or the denominator is zero — a
    /// division by a stored zero is not a meaningful row, it is no data.</summary>
    private static decimal? Ratio(decimal? numerator, decimal? denominator) =>
        numerator is null || denominator is null || denominator.Value == 0m
            ? null
            : numerator.Value / denominator.Value;

    /// <summary>A per-share row: null without a positive share count (the user's exclusion #4 —
    /// no paid-in-capital-derived guess), never zero.</summary>
    private static decimal? PerShare(decimal? numerator, decimal? shareCount) =>
        numerator is null || shareCount is null || shareCount.Value <= 0m
            ? null
            : numerator.Value / shareCount.Value;

    /// <summary>One served fact with its own as-of, or null when the fact is absent.</summary>
    private static object? Fact(decimal? value, DateOnly? asOf) =>
        value is null || asOf is null
            ? null
            : new { value = value.Value, asOf = asOf.Value.ToString("yyyy-MM-dd") };

    /// <summary>
    /// The period's declared span (TKT-foundation-094, closing 090's R-1 on the surface): the
    /// range-bearing statement's own header span, IS first then CF (a balance sheet is an
    /// instant and carries none). Both fields are served together or the span is null — a
    /// pre-migration row degrades to today's <c>Q</c> label, never a fabricated range.
    /// </summary>
    private static object? SpanOf(FinancialStatement? incomeStatement, FinancialStatement? cashFlow)
    {
        foreach (var statement in new[] { incomeStatement, cashFlow })
        {
            if (statement?.PeriodStartDate is DateOnly start && statement.DurationMonths is int months)
            {
                return new
                {
                    periodStartDate = start.ToString("yyyy-MM-dd"),
                    durationMonths = months,
                };
            }
        }

        return null;
    }

    /// <summary>
    /// The period's consolidation flag from the served statements: a single declared value
    /// serves, no declaration is null, and a disagreement between sources is null too — the
    /// badge is suppressed rather than chosen between two filings' claims.
    /// </summary>
    private static bool? ConsolidatedOf(PeriodEntry entry)
    {
        var flags = new[] { entry.IncomeStatement, entry.BalanceSheet, entry.CashFlow }
            .Where(statement => statement?.Consolidated is not null)
            .Select(statement => statement!.Consolidated!.Value)
            .Distinct()
            .ToList();
        return flags.Count == 1 ? flags[0] : null;
    }

    /// <summary>
    /// The served canonical line map per statement type (IS/BS/CF), each entry a figure with its
    /// statement's restated flag. An unserved type is an empty map — the <c>OTHER_*</c>
    /// passthroughs stay reachable in <see cref="StatementOtherLines"/> so an uncovered family
    /// is never silently dropped.
    /// </summary>
    private static Dictionary<string, object?> StatementLines(
        FinancialStatement? incomeStatement,
        FinancialStatement? balanceSheet,
        FinancialStatement? cashFlow,
        IReadOnlyDictionary<long, Dictionary<string, decimal>> lines) =>
        Split(incomeStatement, balanceSheet, cashFlow, lines, other: false);

    /// <summary>The <c>OTHER_*</c> passthrough block per statement type, same shape as
    /// <see cref="StatementLines"/>.</summary>
    private static Dictionary<string, object?> StatementOtherLines(
        FinancialStatement? incomeStatement,
        FinancialStatement? balanceSheet,
        FinancialStatement? cashFlow,
        IReadOnlyDictionary<long, Dictionary<string, decimal>> lines) =>
        Split(incomeStatement, balanceSheet, cashFlow, lines, other: true);

    /// <summary>
    /// The row-set block served when the period's layout is not applicable (a financial-sector
    /// profile, `02` §6.2): the same IS/BS/CF shape with every map empty. The layout notice is
    /// the signal; no industrial row is served, not even a half-filled one.
    /// </summary>
    private static Dictionary<string, object?> EmptyRowSet() =>
        new(StringComparer.Ordinal)
        {
            ["IS"] = new Dictionary<string, object?>(StringComparer.Ordinal),
            ["BS"] = new Dictionary<string, object?>(StringComparer.Ordinal),
            ["CF"] = new Dictionary<string, object?>(StringComparer.Ordinal),
        };

    private static Dictionary<string, object?> Split(
        FinancialStatement? incomeStatement,
        FinancialStatement? balanceSheet,
        FinancialStatement? cashFlow,
        IReadOnlyDictionary<long, Dictionary<string, decimal>> lines,
        bool other) =>
        new(StringComparer.Ordinal)
        {
            ["IS"] = CodeMap(incomeStatement, lines, other),
            ["BS"] = CodeMap(balanceSheet, lines, other),
            ["CF"] = CodeMap(cashFlow, lines, other),
        };

    private static Dictionary<string, object?> CodeMap(
        FinancialStatement? statement,
        IReadOnlyDictionary<long, Dictionary<string, decimal>> lines,
        bool other)
    {
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (statement is null || !lines.TryGetValue(statement.Id, out var stored))
        {
            return map;
        }

        var restated = IsRestated(statement);
        foreach (var (code, value) in stored)
        {
            var passthrough = code.StartsWith("OTHER_", StringComparison.Ordinal);
            if (passthrough == other)
            {
                map[code] = Figure(value, restated);
            }
        }

        return map;
    }

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
