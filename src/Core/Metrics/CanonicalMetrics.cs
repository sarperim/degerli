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
    bool IsAdjusted);

/// <summary>One computed metric row: the value and, for CAGRs, the actual window used.</summary>
public sealed record ComputedMetric(string Code, decimal? Value, int? WindowYears, bool IsAdjusted, bool IsRested);

/// <summary>
/// The single canonical implementation of the 18 screener metric concepts / 26
/// window-suffixed codes (FR-MDF-013, 02 §6.2; BR-MDF-009, AD-06). Pure and
/// deterministic: identical inputs always produce identical outputs. Every
/// not-meaningful rule yields NULL — never <c>0</c>-for-missing.
/// </summary>
public static class CanonicalMetrics
{
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
        var fcf = FreeCashFlow(ni, depr, t.Dwc, t.Capex);
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

        return metrics;
    }

    /// <summary>FCF via the canonical indirect method: NI + D&amp;A − ΔWC − CAPEX (02 §6.2).</summary>
    public static decimal? FreeCashFlow(decimal? ni, decimal? depr, decimal? dwc, decimal? capex)
    {
        if (ni is null || depr is null || dwc is null || capex is null)
        {
            return null;
        }

        return ni.Value + depr.Value - dwc.Value - capex.Value;
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
        metrics.Add(Add(code, outcome.Value, outcome.WindowYears, input));
    }

    private static ComputedMetric Add(string code, decimal? value, CanonicalMetricsInput input) =>
        Add(code, value, null, input);

    private static ComputedMetric Add(string code, decimal? value, int? windowYears, CanonicalMetricsInput input) =>
        new(code, value, windowYears, input.IsAdjusted, input.IsRested);

    private static bool Positive(decimal? value) => value is > 0m;

    private static decimal? Sum(decimal? a, decimal? b) =>
        a is null || b is null ? null : a.Value + b.Value;

    private static decimal? Sum3(decimal? a, decimal? b, decimal? c) =>
        a is null || b is null || c is null ? null : a.Value + b.Value + c.Value;

    private static decimal? Difference(decimal? a, decimal? b) =>
        a is null || b is null ? null : a.Value - b.Value;

    private static decimal? Negate(decimal? value) => value is null ? null : -value.Value;
}
