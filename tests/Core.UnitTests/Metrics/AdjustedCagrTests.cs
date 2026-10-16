using Degerli.Core.Adjustments;
using Degerli.Core.Metrics;

namespace Degerli.Core.UnitTests.Metrics;

/// <summary>
/// L1 acceptance for the corporate-action-adjusted per-share series (TKT-mdf-016;
/// TC-MDF-058, FR-MDF-013/018, 02 §6.2): the historical EPS/DPS facts are multiplied by
/// the cumulative adjustment factor Π of every corporate action that follows the fact's
/// basis date (a fiscal period end for EPS, the ex-date for DPS), exactly as
/// <c>close_adjusted</c> is derived (02 §3.1) — then the CAGR is taken on the adjusted
/// series. Goldens are hand-derived in FU §5.3; the unadjusted value is carried as a
/// negative guard.
/// </summary>
public sealed class AdjustedCagrTests
{
    // EPSL's corporate actions (FU §4/§5.3): a 1:5 split effective 2025-06-02
    // (factor 0.2) and a 1:10 bonus effective 2026-03-02 (factor 10/11).
    private static readonly DatedAction[] EpslActions =
    [
        new(new DateOnly(2025, 6, 2), new AdjustmentAction(AdjustmentActionType.Split, 5m)),
        new(new DateOnly(2026, 3, 2), new AdjustmentAction(AdjustmentActionType.BonusIssue, 0.1m)),
    ];

    // EPSL FY series (FU §5.3) — as-reported EPS and DPS with their basis dates.
    private static readonly (int Year, DateOnly Basis, decimal Eps, decimal Dps)[] EpslFacts =
    [
        (2022, new DateOnly(2022, 12, 31), 3.125m, 1.25m),
        (2023, new DateOnly(2023, 12, 31), 3.30m, 1.30m),
        (2024, new DateOnly(2024, 12, 31), 3.50m, 1.375m),
        (2025, new DateOnly(2025, 12, 31), 0.75m, 0.30m),
    ];

    /// <summary>TC-MDF-058 (L1) — the adjusted EPS CAGR golden and the unadjusted guard.</summary>
    [Fact]
    public void TC_MDF_058_adjusted_eps_cagr_matches_golden_and_rejects_the_unadjusted_value()
    {
        var adjusted = EpslFacts
            .Select(f => new FiscalValue(f.Year, f.Eps * PriceAdjustment.FactorAfter(f.Basis, EpslActions)))
            .ToList();

        var outcome = MetricMath.Cagr(adjusted, 3);

        Assert.NotNull(outcome.Value);
        Assert.Equal(0.062659m, outcome.Value!.Value, 5);
        Assert.Equal(3, outcome.WindowYears);

        // Negative guard: the unadjusted (0.75/3.125)^(1/3) − 1 = −0.378553 must not appear.
        var unadjusted = MetricMath.Cagr(
            EpslFacts.Select(f => new FiscalValue(f.Year, f.Eps)).ToList(),
            3);
        Assert.Equal(-0.378553m, unadjusted.Value!.Value, 5);
        Assert.NotEqual(unadjusted.Value!.Value, outcome.Value!.Value);
    }

    /// <summary>TC-MDF-058 (L1) — the adjusted DPS CAGR golden (same shape as EPS).</summary>
    [Fact]
    public void TC_MDF_058_adjusted_div_cagr_matches_golden()
    {
        var adjusted = EpslFacts
            .Select(f => new FiscalValue(f.Year, f.Dps * PriceAdjustment.FactorAfter(f.Basis, EpslActions)))
            .ToList();

        var outcome = MetricMath.Cagr(adjusted, 3);

        Assert.NotNull(outcome.Value);
        Assert.Equal(0.062659m, outcome.Value!.Value, 5);
        Assert.Equal(3, outcome.WindowYears);
    }

    /// <summary>
    /// TC-MDF-058 (L1) — only the actions <em>after</em> a fact's basis date apply: a
    /// pre-2025 basis takes both factors (2/11); a FY2025 basis takes only the bonus (10/11).
    /// </summary>
    [Fact]
    public void TC_MDF_058_factor_applies_only_actions_after_the_fact_date()
    {
        Assert.Equal(2m / 11m, PriceAdjustment.FactorAfter(new DateOnly(2024, 12, 31), EpslActions), 9);
        Assert.Equal(10m / 11m, PriceAdjustment.FactorAfter(new DateOnly(2025, 12, 31), EpslActions), 9);
        Assert.Equal(1m, PriceAdjustment.FactorAfter(new DateOnly(2026, 6, 2), EpslActions));
    }

    /// <summary>TC-MDF-058 (L1) — no actions means no adjustment (factor exactly 1).</summary>
    [Fact]
    public void TC_MDF_058_no_actions_leaves_the_series_unadjusted()
    {
        Assert.Equal(1m, PriceAdjustment.FactorAfter(new DateOnly(2022, 12, 31), []));
    }
}
