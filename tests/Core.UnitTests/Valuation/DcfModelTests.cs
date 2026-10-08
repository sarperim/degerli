using Degerli.Core.Valuation;

namespace Degerli.Core.UnitTests.Valuation;

/// <summary>
/// L1 acceptance tests for the pure DCF fair-value model (TKT-val-002; AD-08).
///
/// TC-VAL-001..004 as specified in `.pipeline/testing/valuation-dcf.md` §2 Group A.
/// The goldens are hand-derived in FU §11.5 and the test plan §1; they are asserted
/// here against the canonical ALFA baseline and are never re-derived from the
/// implementation:
///   base_fcf 100, growth_rate 0.10, horizon_years 5, terminal_growth 0.03,
///   discount_rate 0.12, debt 400 (total, ST+LT — F-VAL-1 ruling), cash 200,
///   share_count 100; price 20.00
///   → PV_explicit 473.8436 · TV 1843.1392 · TV_pv 1045.8467 · EquityValue 1319.6903
///     FVPS 13.1969 · MoS −0.5155
/// Sensitivity (5×5, step 0.01 centered): center 13.1969 · (0.10, 0.05) 24.0000 ·
/// (0.14, 0.01) 8.99623 (corrected v1.1/v1.3 corner).
/// </summary>
public sealed class DcfModelTests
{
    /// <summary>The canonical ALFA baseline parameter set (FU §11.5).</summary>
    private static DcfParameters AlfaBaseline() => new(
        BaseFcf: 100m,
        GrowthRate: 0.10m,
        HorizonYears: 5,
        TerminalGrowth: 0.03m,
        DiscountRate: 0.12m,
        Debt: 400m,
        Cash: 200m,
        ShareCount: 100m);

    private const decimal AlfaPrice = 20.00m;

    // -- TC-VAL-001 — DCF golden values ---------------------------------------

    [Fact]
    public void TC_VAL_001_dcf_golden_values()
    {
        var computation = DcfModel.Compute(AlfaBaseline(), AlfaPrice);

        Assert.True(computation.IsComputable);
        var result = Assert.IsType<DcfPointResult>(computation.Result);

        // Intermediate values (test plan §1 / FU §11.5), 6 sig. digits → 4 d.p.
        Assert.Equal(473.8436m, result.PvExplicit, 4);
        Assert.Equal(1843.1392m, result.TerminalValue, 4);
        Assert.Equal(1045.8467m, result.TerminalValuePresentValue, 4);
        Assert.Equal(1319.6903m, result.EquityValue, 4);

        // F-VAL-1: EquityValue = PV + TV_pv + Cash − total Debt (both cash and
        // debt affect the result; the net-debt reading 15.1969 is rejected).
        Assert.Equal(13.1969m, result.FairValuePerShare, 4);
        Assert.Equal(-0.5155m, result.MarginOfSafety, 4);
    }

    // -- TC-VAL-002 — Sensitivity grid golden values ---------------------------

    [Fact]
    public void TC_VAL_002_sensitivity_grid_golden_values()
    {
        var grid = DcfModel.ComputeSensitivity(AlfaBaseline());

        Assert.Equal(new[] { 0.10m, 0.11m, 0.12m, 0.13m, 0.14m }, grid.DiscountRates);
        Assert.Equal(new[] { 0.01m, 0.02m, 0.03m, 0.04m, 0.05m }, grid.TerminalGrowths);
        Assert.Equal(5, grid.FairValues.Count);
        Assert.All(grid.FairValues, row => Assert.Equal(5, row.Count));

        // Center cell = the single-point result.
        Assert.Equal(13.1969m, grid.FairValues[2][2]!.Value, 4);
        // (discount_rate 0.10, terminal_growth 0.05) corner.
        Assert.Equal(24.0000m, grid.FairValues[0][4]!.Value, 4);
        // (discount_rate 0.14, terminal_growth 0.01) corner — corrected v1.1 8.99623.
        Assert.Equal(8.99623m, grid.FairValues[4][0]!.Value, 5);

        // No r ≤ g_t cell exists in this centered grid (max g_t 0.05 < min r 0.10).
        Assert.All(grid.FairValues, row => Assert.All(row, cell => Assert.NotNull(cell)));
    }

    [Fact]
    public void TC_VAL_002_sensitivity_r_le_gt_cells_are_null()
    {
        // Center r 0.10 > g_t 0.06 is computable, but the centered grid's axes cross:
        // r ∈ [0.08..0.12] × g_t ∈ [0.04..0.08] — cell (r 0.08, g_t 0.08) has r ≤ g_t.
        var grid = DcfModel.ComputeSensitivity(
            AlfaBaseline() with { DiscountRate = 0.10m, TerminalGrowth = 0.06m });

        Assert.Equal(new[] { 0.08m, 0.09m, 0.10m, 0.11m, 0.12m }, grid.DiscountRates);
        Assert.Equal(new[] { 0.04m, 0.05m, 0.06m, 0.07m, 0.08m }, grid.TerminalGrowths);

        // I-VAL-1: r ≤ g_t → NULL (0.08 ≤ 0.08).
        Assert.Null(grid.FairValues[0][4]);
        // r just above g_t (0.09 > 0.08) is computable → not null.
        Assert.NotNull(grid.FairValues[1][4]);
    }

    // -- TC-VAL-003 — Not-computable constraints -------------------------------

    [Fact]
    public void TC_VAL_003_discount_rate_not_above_terminal_growth_is_not_computable()
    {
        var equal = DcfModel.Compute(
            AlfaBaseline() with { DiscountRate = 0.04m, TerminalGrowth = 0.04m }, AlfaPrice);
        Assert.False(equal.IsComputable);
        Assert.Equal(DcfConstraint.DiscountRateNotGreaterThanTerminalGrowth, equal.Constraint);
        Assert.Null(equal.Result);

        var below = DcfModel.Compute(
            AlfaBaseline() with { DiscountRate = 0.03m, TerminalGrowth = 0.04m }, AlfaPrice);
        Assert.False(below.IsComputable);
        Assert.Equal(DcfConstraint.DiscountRateNotGreaterThanTerminalGrowth, below.Constraint);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void TC_VAL_003_horizon_outside_1_to_10_is_not_computable(int horizon)
    {
        var computation = DcfModel.Compute(AlfaBaseline() with { HorizonYears = horizon }, AlfaPrice);

        Assert.False(computation.IsComputable);
        Assert.Equal(DcfConstraint.HorizonOutOfRange, computation.Constraint);
        Assert.Null(computation.Result);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public void TC_VAL_003_horizon_boundaries_are_computable(int horizon)
    {
        var computation = DcfModel.Compute(AlfaBaseline() with { HorizonYears = horizon }, AlfaPrice);

        Assert.True(computation.IsComputable);
        Assert.NotNull(computation.Result);
    }

    [Fact]
    public void TC_VAL_003_negative_base_fcf_is_computable()
    {
        // RISK-VAL-001: it is the user's model — assumptions are not "correctness"-
        // validated; a negative base FCF still computes.
        var computation = DcfModel.Compute(AlfaBaseline() with { BaseFcf = -100m }, AlfaPrice);

        Assert.True(computation.IsComputable);
        Assert.NotNull(computation.Result);
    }

    // -- TC-VAL-004 — Pure function determinism --------------------------------

    [Fact]
    public void TC_VAL_004_identical_inputs_yield_identical_outputs()
    {
        var first = DcfModel.Compute(AlfaBaseline(), AlfaPrice);
        var second = DcfModel.Compute(AlfaBaseline(), AlfaPrice);

        Assert.Equal(first, second);

        var firstGrid = DcfModel.ComputeSensitivity(AlfaBaseline());
        var secondGrid = DcfModel.ComputeSensitivity(AlfaBaseline());

        Assert.Equal(firstGrid.DiscountRates, secondGrid.DiscountRates);
        Assert.Equal(firstGrid.TerminalGrowths, secondGrid.TerminalGrowths);
        for (var i = 0; i < firstGrid.DiscountRates.Count; i++)
        {
            Assert.Equal(firstGrid.FairValues[i], secondGrid.FairValues[i]);
        }
    }
}
