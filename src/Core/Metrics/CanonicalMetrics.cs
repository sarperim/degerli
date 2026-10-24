namespace Degerli.Core.Metrics;

/// <summary>
/// The canonical TTM (trailing-twelve-month) financial facts for one instrument,
/// expressed in the fixture/data-model units (TRY millions, share counts in millions).
/// NULL means the fact is honestly missing (e.g. no CF statement), never zero-for-missing.
/// Chart-of-accounts codes per 02 §6.1.
/// </summary>
public sealed record CanonicalStatement(
    decimal? Rev,
    decimal? Cogs,
    decimal? GrossProfit,
    decimal? Ebit,
    decimal? DeprAmort,
    decimal? Ebitda,
    decimal? FinExp,
    decimal? PretaxInc,
    decimal? TaxExp,
    decimal? Ni,
    decimal? Capex,
    decimal? Dwc,
    decimal? Ocf,
    decimal? Cash,
    decimal? StDebt,
    decimal? LtDebt,
    decimal? TotalEquity,
    decimal? CurAssets,
    decimal? CurLiab);

/// <summary>
/// Everything the 18 canonical metric concepts need for one instrument at one date
/// (02 §6.2). Pure input — no clock, no DB, no hidden state.
/// </summary>
/// <param name="AccountingProfile">
/// The instrument's builder-curated accounting classification (<c>industrial</c>/
/// <c>bank</c>/<c>insurance</c>/<c>reit</c>; 02 §3.1). For <c>bank</c>/<c>insurance</c>
/// the industrial metrics are not-meaningful by profile and are forced NULL (§6.2);
/// <c>industrial</c> and <c>reit</c> keep the natural not-meaningful rules.
/// </param>
public sealed record CanonicalMetricsInput(
    decimal Price,
    decimal? SharesDiluted,
    CanonicalStatement Ttm,
    IReadOnlyList<FiscalValue> RevenueByFiscalYear,
    IReadOnlyList<FiscalValue> EpsByFiscalYear,
    IReadOnlyList<FiscalValue> FcfByFiscalYear,
    IReadOnlyList<FiscalValue> DpsByFiscalYear,
    decimal? DividendTtmPerShare,
    bool HasEverPaidDividend,
    bool IsRested,
    bool IsAdjusted,
    string AccountingProfile = CanonicalMetrics.IndustrialProfile);

/// <summary>One computed metric row: the value and, for CAGRs, the actual window used.</summary>
/// <param name="Code">Window-suffixed metric code (02 §6.2, Q5).</param>
/// <param name="Value">The value; NULL = not meaningful, never <c>0</c>-for-missing.</param>
/// <param name="WindowYears">The actual compounding window used by a CAGR, else NULL.</param>
/// <param name="IsAdjusted">Corporate-action-adjusted basis marker (FR-MDF-018).</param>
/// <param name="IsRested">Computed from <c>version &gt; 0</c> statement rows (FR-MDF-019).</param>
/// <param name="RegimeBreak">
/// TMS 29 regime-break marker (FR-MDF-013/014, 02 §3.1/§6.2): CAGRs only, TRUE when the
/// window spans the 2023-12-31 transition; NULL for non-CAGRs and for CAGRs with no
/// computed window.
/// </param>
public sealed record ComputedMetric(
    string Code,
    decimal? Value,
    int? WindowYears,
    bool IsAdjusted,
    bool IsRested,
    bool? RegimeBreak = null);

/// <summary>
/// The single canonical implementation of the 18 screener metric concepts / 26
/// window-suffixed codes (FR-MDF-013, 02 §6.2; BR-MDF-009, AD-06). Pure and
/// deterministic: identical inputs always produce identical outputs. Every
/// not-meaningful rule yields NULL — never <c>0</c>-for-missing.
/// </summary>
public static class CanonicalMetrics
{
    /// <summary>
    /// The default accounting profile (<c>02</c> §3.1): an instrument not classified as a
    /// financial-sector member keeps the natural not-meaningful metric rules.
    /// </summary>
    public const string IndustrialProfile = "industrial";

    /// <summary>
    /// The definition version of the canonical metric set — the single source of truth for
    /// "which formula produced this number" (<c>01</c> §10.9.13, TKT-foundation-070;
    /// BR-MDF-009). Every row this definition produces is stamped with it
    /// (<c>derived_metrics.formula_version</c> / <c>sector_metric_medians.formula_version</c>),
    /// so a stored value can answer the provenance question after the fact.
    /// <para>
    /// <b>Bump it in the same change as any formula, input-assembly or not-meaningful-rule
    /// change in this file</b> (or in the engines that assemble its inputs). A bump makes the
    /// next recompute rewrite every row of the target day even when the values are identical,
    /// because the stored rows were produced by a different definition — that rewrite is the
    /// honest provenance update, not a repair of a changed fact. A stamp that is absent
    /// (<c>NULL</c> — rows written before versioning existed) is treated the same way: the
    /// next recompute of that day stamps it. No stamp is ever guessed or back-filled.
    /// </para>
    /// </summary>
    public const int Version = 1;

    /// <summary>
    /// The accounting profiles whose industrial metric concepts are not meaningful
    /// (<c>02</c> §6.2, v1.3): banks and insurers report under BDDK/insurance rules with
    /// no <c>REV</c>/<c>COGS</c>/<c>EBITDA</c>/<c>FCF</c>/<c>CUR_ASSETS</c> semantics, so a
    /// stray mapped line must never produce a plausible-looking industrial number.
    /// </summary>
    private static readonly IReadOnlySet<string> ProfileGatingProfiles =
        new HashSet<string>(StringComparer.Ordinal) { "bank", "insurance" };

    /// <summary>
    /// The twelve metric codes a financial profile forces to NULL (<c>02</c> §6.2):
    /// the ten industrial concepts — <c>ev_ebitda</c>, <c>ev_fcf</c>, <c>fcf_yield</c>,
    /// <c>roic</c>, <c>gross_margin</c>, <c>operating_margin</c>,
    /// <c>fcf_cagr_*</c> (×3 windows), <c>net_debt_ebitda</c>, <c>interest_coverage</c>,
    /// <c>current_ratio</c>. <c>pe</c>/<c>pb</c>/<c>roe</c>/<c>eps_cagr_*</c>/
    /// <c>div_*</c>/<c>payout_ratio</c> keep computing — their inputs are meaningful for
    /// financials.
    /// </summary>
    private static readonly IReadOnlySet<string> ProfileGatedCodes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            MetricCodes.EvEbitda,
            MetricCodes.EvFcf,
            MetricCodes.FcfYield,
            MetricCodes.Roic,
            MetricCodes.GrossMargin,
            MetricCodes.OperatingMargin,
            MetricCodes.NetDebtEbitda,
            MetricCodes.InterestCoverage,
            MetricCodes.CurrentRatio,
            MetricCodes.FcfCagr3y,
            MetricCodes.FcfCagr5y,
            MetricCodes.FcfCagr10y,
        };

    /// <summary>
    /// Whether a metric code is not-meaningful by accounting profile (<c>02</c> §6.2) —
    /// forced to NULL for <c>bank</c>/<c>insurance</c> regardless of stray mapped lines.
    /// </summary>
    private static bool IsProfileGated(string code) => ProfileGatedCodes.Contains(code);

    /// <summary>Computes all 26 metric codes for one instrument.</summary>
    public static IReadOnlyList<ComputedMetric> Compute(CanonicalMetricsInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var t = input.Ttm;
        var shares = input.SharesDiluted;
        var price = input.Price;

        decimal? mcap = Positive(shares) ? price * shares!.Value : null;
        var ni = t.Ni;
        var equity = t.TotalEquity;
        var rev = t.Rev;
        var ebit = t.Ebit;
        var depr = t.DeprAmort;
        var ebitda = t.Ebitda ?? Sum(ebit, depr);
        var grossProfit = t.GrossProfit ?? Difference(rev, t.Cogs);
        var fcf = FreeCashFlow(t.Ocf, t.Capex);
        var debt = Sum(t.StDebt, t.LtDebt);
        var ev = Sum3(mcap, debt, Negate(t.Cash));
        var investedCapital = Sum3(debt, equity, Negate(t.Cash));
        var taxRate = MetricMath.EffectiveTaxRate(t.TaxExp, t.PretaxInc);

        var metrics = new List<ComputedMetric>(MetricCodes.All.Count)
        {
            Add(MetricCodes.Pe, (ni is > 0m && Positive(shares)) ? price / (ni.Value / shares!.Value) : null, input),
            Add(MetricCodes.Pb, (equity is > 0m && Positive(shares)) ? price / (equity.Value / shares!.Value) : null, input),
            Add(MetricCodes.EvEbitda, (ebitda is > 0m && ev is not null) ? ev.Value / ebitda.Value : null, input),
            Add(MetricCodes.EvFcf, (fcf is > 0m && ev is not null) ? ev.Value / fcf.Value : null, input),
            Add(MetricCodes.FcfYield, (fcf is > 0m && Positive(mcap)) ? fcf.Value / mcap!.Value : null, input),

            Add(MetricCodes.Roic, (investedCapital is > 0m && ebit is not null) ? ebit.Value * (1m - taxRate) / investedCapital.Value : null, input),
            Add(MetricCodes.Roe, (equity is > 0m && ni is not null) ? ni.Value / equity.Value : null, input),
            Add(MetricCodes.GrossMargin, (rev is > 0m && grossProfit is not null) ? grossProfit.Value / rev.Value : null, input),
            Add(MetricCodes.OperatingMargin, (rev is > 0m && ebit is not null) ? ebit.Value / rev.Value : null, input),

            Add(MetricCodes.NetDebtEbitda, (ebitda is > 0m && debt is not null && t.Cash is not null) ? (debt.Value - t.Cash.Value) / ebitda.Value : null, input),
            Add(MetricCodes.InterestCoverage, (t.FinExp is > 0m && ebit is not null) ? ebit.Value / t.FinExp.Value : null, input),
            Add(MetricCodes.CurrentRatio, (t.CurLiab is > 0m && t.CurAssets is not null) ? t.CurAssets.Value / t.CurLiab.Value : null, input),

            Add(MetricCodes.DivYield, (input.HasEverPaidDividend && price > 0m && input.DividendTtmPerShare is not null) ? input.DividendTtmPerShare.Value / price : null, input),
            Add(MetricCodes.PayoutRatio, (ni is > 0m && input.DividendTtmPerShare is not null && Positive(shares)) ? input.DividendTtmPerShare.Value * shares!.Value / ni.Value : null, input),
        };

        AddCagr(metrics, MetricCodes.RevCagr3y, input.RevenueByFiscalYear, 3, nullOnSignChange: false, input);
        AddCagr(metrics, MetricCodes.RevCagr5y, input.RevenueByFiscalYear, 5, nullOnSignChange: false, input);
        AddCagr(metrics, MetricCodes.RevCagr10y, input.RevenueByFiscalYear, 10, nullOnSignChange: false, input);
        AddCagr(metrics, MetricCodes.EpsCagr3y, input.EpsByFiscalYear, 3, nullOnSignChange: false, input);
        AddCagr(metrics, MetricCodes.EpsCagr5y, input.EpsByFiscalYear, 5, nullOnSignChange: false, input);
        AddCagr(metrics, MetricCodes.EpsCagr10y, input.EpsByFiscalYear, 10, nullOnSignChange: false, input);
        AddCagr(metrics, MetricCodes.FcfCagr3y, input.FcfByFiscalYear, 3, nullOnSignChange: true, input);
        AddCagr(metrics, MetricCodes.FcfCagr5y, input.FcfByFiscalYear, 5, nullOnSignChange: true, input);
        AddCagr(metrics, MetricCodes.FcfCagr10y, input.FcfByFiscalYear, 10, nullOnSignChange: true, input);
        AddCagr(metrics, MetricCodes.DivCagr3y, input.DpsByFiscalYear, 3, nullOnSignChange: false, input);
        AddCagr(metrics, MetricCodes.DivCagr5y, input.DpsByFiscalYear, 5, nullOnSignChange: false, input);
        AddCagr(metrics, MetricCodes.DivCagr10y, input.DpsByFiscalYear, 10, nullOnSignChange: false, input);

        // Accounting-profile gating (02 §6.2, v1.3): for `bank`/`insurance` the ten
        // industrial concepts evaluate to NULL even when the underlying lines happen to
        // exist — a stray mapped REV/EBIT/... must never produce a plausible-looking
        // number. `industrial` and `reit` keep the natural not-meaningful rules.
        if (ProfileGatingProfiles.Contains(input.AccountingProfile))
        {
            metrics = metrics
                .Select(metric => IsProfileGated(metric.Code)
                    ? metric with { Value = null, WindowYears = null, RegimeBreak = null }
                    : metric)
                .ToList();
        }

        return metrics;
    }

    /// <summary>
    /// Canonical FCF (<c>02</c> §6.2, v1.3): <c>OCF − |CAPEX|</c>. Operating cash flow is
    /// taken as the KAP cash-flow statement reports it (already reflecting the TMS 29
    /// non-cash items the rejected indirect reconstruction missed); the absolute CAPEX
    /// guards the KAP sign convention where investing outflows are negative — a reported
    /// −150 deductible is <em>subtracted</em>, not added. NULL when <c>OCF</c> or
    /// <c>CAPEX</c> is missing (no CF statement → no FCF, never zero-for-missing).
    /// </summary>
    public static decimal? FreeCashFlow(decimal? ocf, decimal? capex)
    {
        if (ocf is null || capex is null)
        {
            return null;
        }

        return ocf.Value - Math.Abs(capex.Value);
    }

    private static void AddCagr(
        List<ComputedMetric> metrics,
        string code,
        IReadOnlyList<FiscalValue> series,
        int window,
        bool nullOnSignChange,
        CanonicalMetricsInput input)
    {
        var outcome = MetricMath.Cagr(series, window, nullOnSignChange);
        metrics.Add(Add(code, outcome.Value, outcome.WindowYears, input, RegimeBreak(series, outcome)));
    }

    /// <summary>
    /// The TMS 29 regime-break marker for one CAGR (02 §6.2): TRUE when the compounding
    /// window actually used spans the 2023-12-31 inflation-accounting transition, i.e.
    /// min FY <c>period_end_date</c> &lt; 2023-12-31 ≤ max FY <c>period_end_date</c>. NULL
    /// when no window was computed (fewer than two FYs) or the period ends are unknown —
    /// never fabricated. A window entirely before or entirely after the boundary is FALSE.
    /// </summary>
    private static bool? RegimeBreak(IReadOnlyList<FiscalValue> series, CagrOutcome outcome)
    {
        if (outcome.WindowYears is not int intervals || series.Count == 0)
        {
            return null;
        }

        var endYear = series.Max(point => point.FiscalYear);
        var startYear = endYear - intervals;
        var start = series.FirstOrDefault(point => point.FiscalYear == startYear)?.PeriodEnd;
        var end = series.FirstOrDefault(point => point.FiscalYear == endYear)?.PeriodEnd;
        if (start is null || end is null)
        {
            return null;
        }

        return start.Value < RegimeBoundary && RegimeBoundary <= end.Value;
    }

    /// <summary>The TMS 29 inflation-accounting transition date (02 §6.2).</summary>
    private static readonly DateOnly RegimeBoundary = new(2023, 12, 31);

    private static ComputedMetric Add(string code, decimal? value, CanonicalMetricsInput input) =>
        Add(code, value, null, input);

    private static ComputedMetric Add(string code, decimal? value, int? windowYears, CanonicalMetricsInput input) =>
        Add(code, value, windowYears, input, null);

    private static ComputedMetric Add(
        string code,
        decimal? value,
        int? windowYears,
        CanonicalMetricsInput input,
        bool? regimeBreak) =>
        new(code, value, windowYears, input.IsAdjusted, input.IsRested, regimeBreak);

    private static bool Positive(decimal? value) => value is > 0m;

    private static decimal? Sum(decimal? a, decimal? b) =>
        a is null || b is null ? null : a.Value + b.Value;

    private static decimal? Sum3(decimal? a, decimal? b, decimal? c) =>
        a is null || b is null || c is null ? null : a.Value + b.Value + c.Value;

    private static decimal? Difference(decimal? a, decimal? b) =>
        a is null || b is null ? null : a.Value - b.Value;

    private static decimal? Negate(decimal? value) => value is null ? null : -value.Value;
}
