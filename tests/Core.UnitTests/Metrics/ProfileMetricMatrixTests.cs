using Degerli.Core.Metrics;

namespace Degerli.Core.UnitTests.Metrics;

/// <summary>
/// L1 acceptance for the accounting-profile metric gating (TKT-mdf-020; TC-MDF-057,
/// FR-MDF-013, <c>02</c> §6.2 v1.3): for <c>accounting_profile ∈ {bank, insurance}</c>
/// the ten industrial metric concepts are <em>not-meaningful by profile</em> and evaluate
/// to NULL — <b>even when the underlying lines happen to exist</b>, so a stray mapped
/// <c>REV</c>/<c>EBIT</c>/… can never produce a plausible-looking industrial number.
/// <c>pe</c>/<c>pb</c>/<c>roe</c>/<c>eps_cagr_*</c>/<c>div_*</c>/<c>payout_ratio</c>
/// compute normally (their inputs — NI, equity, shares, dividends — are meaningful for
/// financials), and <c>industrial</c>/<c>reit</c> keep the natural not-meaningful rules.
/// </summary>
public sealed class ProfileMetricMatrixTests
{
    /// <summary>The twelve profile-gated codes (ten industrial concepts, fcf_cagr ×3).</summary>
    private static readonly string[] GatedCodes =
    [
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
    ];

    /// <summary>
    /// TC-MDF-057 (L1) — a <c>bank</c> instrument with a <em>complete</em> stray industrial
    /// statement (REV/COGS/EBIT/D&amp;A/FIN_EXP/OCF/CAPEX/CUR_*) computes <c>pe</c>/<c>pb</c>/
    /// <c>roe</c> but every industrial concept is forced NULL — never a plausible-looking
    /// number, never <c>0</c>-for-missing.
    /// </summary>
    [Fact]
    public void TC_MDF_057_bank_profile_gates_industrial_metrics_despite_stray_lines()
    {
        var metrics = ByCode(CanonicalMetrics.Compute(Input("bank")));

        // The financial-meaningful metrics compute normally.
        Assert.Equal(30.0m, metrics[MetricCodes.Pe].Value);
        Assert.Equal(3.0m, metrics[MetricCodes.Pb].Value);
        Assert.Equal(0.10m, metrics[MetricCodes.Roe].Value);

        AssertGatedNull(metrics);
    }

    /// <summary>
    /// TC-MDF-057 (L1) — the <c>insurance</c> profile behaves exactly like the bank profile:
    /// the ten industrial concepts are NULL though the stray lines are present.
    /// </summary>
    [Fact]
    public void TC_MDF_057_insurance_profile_gates_industrial_metrics_despite_stray_lines()
    {
        var metrics = ByCode(CanonicalMetrics.Compute(Input("insurance")));

        Assert.Equal(30.0m, metrics[MetricCodes.Pe].Value);
        Assert.Equal(3.0m, metrics[MetricCodes.Pb].Value);
        Assert.Equal(0.10m, metrics[MetricCodes.Roe].Value);

        AssertGatedNull(metrics);
    }

    /// <summary>
    /// TC-MDF-057 (L1) — the control: the same stray-line statement under the
    /// <c>industrial</c> profile computes every industrial concept, proving the NULLs are
    /// caused by the profile gate, not by absent inputs.
    /// </summary>
    [Fact]
    public void TC_MDF_057_industrial_profile_keeps_computing_the_industrial_metrics()
    {
        var metrics = ByCode(CanonicalMetrics.Compute(Input("industrial")));

        Assert.NotNull(metrics[MetricCodes.EvEbitda].Value);       // 11500 / 250
        Assert.NotNull(metrics[MetricCodes.EvFcf].Value);          // 11500 / 150
        Assert.NotNull(metrics[MetricCodes.FcfYield].Value);       // 150 / 9000
        Assert.NotNull(metrics[MetricCodes.Roic].Value);
        Assert.NotNull(metrics[MetricCodes.GrossMargin].Value);    // 400 / 1000
        Assert.NotNull(metrics[MetricCodes.OperatingMargin].Value); // 200 / 1000
        Assert.NotNull(metrics[MetricCodes.NetDebtEbitda].Value);  // 2500 / 250
        Assert.NotNull(metrics[MetricCodes.InterestCoverage].Value); // 200 / 25
        Assert.NotNull(metrics[MetricCodes.CurrentRatio].Value);    // 800 / 400
        Assert.NotNull(metrics[MetricCodes.FcfCagr3y].Value);

        // The exact industrial goldens (hand-derived) confirm the gate did not silently
        // alter the natural rules.
        Assert.Equal(46m, metrics[MetricCodes.EvEbitda].Value);
        Assert.Equal(0.4m, metrics[MetricCodes.GrossMargin].Value);
        Assert.Equal(0.2m, metrics[MetricCodes.OperatingMargin].Value);
        Assert.Equal(10m, metrics[MetricCodes.NetDebtEbitda].Value);
        Assert.Equal(8m, metrics[MetricCodes.InterestCoverage].Value);
        Assert.Equal(2m, metrics[MetricCodes.CurrentRatio].Value);
    }

    /// <summary>
    /// TC-MDF-057 (L1) — <c>reit</c> is <em>not</em> a gating profile: it keeps the natural
    /// not-meaningful rules (<c>02</c> §6.2). Only <c>bank</c>/<c>insurance</c> gate.
    /// </summary>
    [Fact]
    public void TC_MDF_057_reit_profile_does_not_gate_industrial_metrics()
    {
        var metrics = ByCode(CanonicalMetrics.Compute(Input("reit")));

        Assert.All(GatedCodes, code => Assert.NotNull(metrics[code].Value));
    }

    /// <summary>
    /// A gated metric never disguises itself as <c>0</c>-for-missing: the value, the CAGR
    /// window and the regime-break marker are all NULL.
    /// </summary>
    private static void AssertGatedNull(IReadOnlyDictionary<string, ComputedMetric> metrics)
    {
        foreach (var code in GatedCodes)
        {
            Assert.Null(metrics[code].Value);
            Assert.Null(metrics[code].WindowYears);
            Assert.Null(metrics[code].RegimeBreak);
        }
    }

    /// <summary>
    /// A deliberately complete industrial statement under the given profile (price 30,
    /// shares 300, NI 300, equity 3000) — the stray lines the bank/insurance gate must
    /// override. Goldens: pe 30, pb 3, roe 0.10; ev_ebitda 46, gross_margin 0.4, …
    /// </summary>
    private static CanonicalMetricsInput Input(string profile) => new(
        Price: 30m,
        SharesDiluted: 300m,
        Ttm: new CanonicalStatement(
            Rev: 1000m,
            Cogs: 600m,
            GrossProfit: null,
            Ebit: 200m,
            DeprAmort: 50m,
            Ebitda: null,
            FinExp: 25m,
            PretaxInc: 175m,
            TaxExp: 35m,
            Ni: 300m,
            Capex: 100m,
            Dwc: 0m,
            Ocf: 250m,
            Cash: 500m,
            StDebt: 2000m,
            LtDebt: 1000m,
            TotalEquity: 3000m,
            CurAssets: 800m,
            CurLiab: 400m),
        RevenueByFiscalYear: Fy(1000m, 1100m, 1210m, 1331m),
        EpsByFiscalYear: Fy(1m, 1.1m, 1.21m, 1.331m),
        FcfByFiscalYear: Fy(100m, 110m, 121m, 133.1m),
        DpsByFiscalYear: Fy(0.5m, 0.55m, 0.605m, 0.6655m),
        DividendTtmPerShare: 0.6655m,
        HasEverPaidDividend: true,
        IsRested: false,
        IsAdjusted: false,
        AccountingProfile: profile);

    /// <summary>Four FYs (2022…2025) of a series, enough for the 3-year compound window.</summary>
    private static IReadOnlyList<FiscalValue> Fy(decimal y2022, decimal y2023, decimal y2024, decimal y2025) =>
    [
        new FiscalValue(2022, y2022, new DateOnly(2022, 12, 31)),
        new FiscalValue(2023, y2023, new DateOnly(2023, 12, 31)),
        new FiscalValue(2024, y2024, new DateOnly(2024, 12, 31)),
        new FiscalValue(2025, y2025, new DateOnly(2025, 12, 31)),
    ];

    private static Dictionary<string, ComputedMetric> ByCode(IReadOnlyList<ComputedMetric> metrics) =>
        metrics.ToDictionary(metric => metric.Code, metric => metric, StringComparer.Ordinal);
}
