using Degerli.Core.Adjustments;

namespace Degerli.Core.UnitTests.Adjustments;

/// <summary>
/// L1 acceptance tests for the corporate-action adjustment-factor system (TKT-mdf-009;
/// FR-MDF-018, BR-MDF-010): the split/bonus factors, their multiplicative composition
/// (TC-MDF-029) and the TERP-based rights-issue factor with its boundaries and degenerate
/// inputs (TC-MDF-053). Goldens are hand-derived in FU §2/§4/§11.6 — never from the
/// implementation. Pure <c>/src/Core</c> logic: no DB, network or clock.
/// </summary>
public sealed class AdjustmentFactorTests
{
    // ── TC-MDF-029 — adjustment factors (split, bonus, composition) ───────────

    /// <summary>Split 1:5 → 0.2.</summary>
    [Fact]
    public void TC_MDF_029_split_one_for_five_is_point_two()
    {
        var factor = AdjustmentFactors.Split(5m);

        Assert.NotNull(factor);
        Assert.Equal(0.2m, factor!.Value, 9);
    }

    /// <summary>Bonus 1:10 → 10/11 = 0.909091.</summary>
    [Fact]
    public void TC_MDF_029_bonus_one_for_ten_is_ten_elevenths()
    {
        var factor = AdjustmentFactors.Bonus(0.1m);

        Assert.NotNull(factor);
        Assert.Equal(10m / 11m, factor!.Value, 9);
    }

    /// <summary>Factors compose multiplicatively over time (split then bonus).</summary>
    [Fact]
    public void TC_MDF_029_factors_compose_multiplicatively()
    {
        var split = AdjustmentFactors.Split(5m)!.Value;
        var bonus = AdjustmentFactors.Bonus(0.1m)!.Value;

        Assert.Equal(0.2m * (10m / 11m), AdjustmentFactors.Compose([split, bonus]), 9);
    }

    /// <summary>No actions → the no-adjustment factor 1 (raw price is already current).</summary>
    [Fact]
    public void TC_MDF_029_no_actions_is_factor_one()
    {
        Assert.Equal(1m, AdjustmentFactors.Compose([]));

        var adjusted = PriceAdjustment.AdjustedCloses(
            [new DatedClose(new DateOnly(2026, 10, 6), 20.00m)],
            []);

        Assert.Equal(20.00m, adjusted[0]);
    }

    // ── TC-MDF-053 — rights-issue adjustment factor (TERP) ────────────────────

    /// <summary>(a) Clean class — 1-for-4 rights at P_S 10.00, cum P_C 20.00 → 0.90.</summary>
    [Fact]
    public void TC_MDF_053_rights_clean_class_is_point_ninety()
    {
        var factor = AdjustmentFactors.Rights(q: 0.25m, subscriptionPrice: 10.00m, cumPrice: 20.00m);

        Assert.NotNull(factor);
        Assert.Equal(0.90m, factor!.Value, 9);
    }

    /// <summary>(b) Composition — rights 0.90 after a split 1:5 (0.2) → 0.180.</summary>
    [Fact]
    public void TC_MDF_053_rights_after_split_composes_to_point_one_eight()
    {
        var split = AdjustmentFactors.Split(5m)!.Value;
        var rights = AdjustmentFactors.Rights(0.25m, 10.00m, 20.00m)!.Value;

        Assert.Equal(0.180m, AdjustmentFactors.Compose([split, rights]), 9);
    }

    /// <summary>(c) Edge — P_S = P_C → factor 1 (price already at the TERP; no adjustment).</summary>
    [Fact]
    public void TC_MDF_053_subscription_equals_cum_is_no_adjustment()
    {
        var factor = AdjustmentFactors.Rights(0.25m, subscriptionPrice: 20.00m, cumPrice: 20.00m);

        Assert.NotNull(factor);
        Assert.Equal(1m, factor!.Value, 9);
    }

    /// <summary>(d) Edge — P_S = 0 → 0.80, equal to the bonus factor 1/(1+q).</summary>
    [Fact]
    public void TC_MDF_053_subscription_zero_reduces_to_bonus_factor()
    {
        var factor = AdjustmentFactors.Rights(0.25m, subscriptionPrice: 0m, cumPrice: 20.00m);

        Assert.NotNull(factor);
        Assert.Equal(0.80m, factor!.Value, 9);
        Assert.Equal(AdjustmentFactors.Bonus(0.25m)!.Value, factor!.Value, 9);
    }

    /// <summary>(e) Degenerate — q ≤ 0 or P_C ≤ 0 → invalid, never a factor, no division by zero.</summary>
    [Theory]
    [InlineData(0.25, 10.00, 0.00)]   // P_C = 0
    [InlineData(0.25, 10.00, -5.00)]  // P_C < 0
    [InlineData(0.00, 10.00, 20.00)]  // q = 0
    [InlineData(-0.10, 10.00, 20.00)] // q < 0
    public void TC_MDF_053_degenerate_rights_inputs_are_invalid(
        double q, double subscriptionPrice, double cumPrice)
    {
        var factor = AdjustmentFactors.Rights((decimal)q, (decimal)subscriptionPrice, (decimal)cumPrice);

        Assert.Null(factor);

        // The same rejection holds through the action-level entry point.
        Assert.Null(AdjustmentFactors.Factor(
            new AdjustmentAction(AdjustmentActionType.RightsIssue, (decimal)q, (decimal)subscriptionPrice),
            (decimal)cumPrice));
    }

    // ── close_adjusted derivation over the price series (FR-MDF-018, FU §4) ───

    /// <summary>
    /// EPSL — the FU §4 adjusted series around the 1:5 split (2025-06-02) and 1:10 bonus
    /// (2026-03-02): a price keeps every factor of the actions that follow it.
    /// </summary>
    [Fact]
    public void close_adjusted_multiplies_every_following_action_factor()
    {
        var closes = new[]
        {
            new DatedClose(new DateOnly(2025, 5, 30), 100.00m),
            new DatedClose(new DateOnly(2026, 2, 27), 22.00m),
            new DatedClose(new DateOnly(2026, 6, 2), 20.20m),
            new DatedClose(new DateOnly(2026, 10, 6), 20.00m),
        };
        var actions = new[]
        {
            new DatedAction(new DateOnly(2025, 6, 2), new AdjustmentAction(AdjustmentActionType.Split, 5m)),
            new DatedAction(new DateOnly(2026, 3, 2), new AdjustmentAction(AdjustmentActionType.BonusIssue, 0.1m)),
        };

        var adjusted = PriceAdjustment.AdjustedCloses(closes, actions);

        Assert.Equal(18.1818m, adjusted[0], 4); // 100 × 0.2 × 10/11
        Assert.Equal(20.0000m, adjusted[1], 4); // 22 × 10/11
        Assert.Equal(20.2000m, adjusted[2], 4); // after both actions → raw
        Assert.Equal(20.0000m, adjusted[3], 4);
    }

    /// <summary>
    /// A rights factor uses the last raw close on/before the action's effective date as its
    /// cum price; dates after the action are unadjusted.
    /// </summary>
    [Fact]
    public void rights_factor_uses_last_raw_close_on_or_before_the_action_date()
    {
        var closes = new[]
        {
            new DatedClose(new DateOnly(2026, 1, 2), 18.00m),
            new DatedClose(new DateOnly(2026, 1, 30), 20.00m), // cum price for the 2026-02-01 action
            new DatedClose(new DateOnly(2026, 2, 10), 18.20m),
        };
        var actions = new[]
        {
            new DatedAction(
                new DateOnly(2026, 2, 1),
                new AdjustmentAction(AdjustmentActionType.RightsIssue, 0.25m, 10.00m)),
        };

        var adjusted = PriceAdjustment.AdjustedCloses(closes, actions);

        Assert.Equal(18.00m * 0.90m, adjusted[0], 9); // both before → both adjusted
        Assert.Equal(20.00m * 0.90m, adjusted[1], 9);
        Assert.Equal(18.20m, adjusted[2]); // after the action → unadjusted
    }

    /// <summary>A degenerate action contributes no factor (it never divides by zero).</summary>
    [Fact]
    public void degenerate_action_never_becomes_a_factor()
    {
        var closes = new[] { new DatedClose(new DateOnly(2026, 1, 2), 20.00m) };
        var actions = new[]
        {
            new DatedAction(
                new DateOnly(2026, 2, 1),
                new AdjustmentAction(AdjustmentActionType.RightsIssue, 0m, 10.00m)), // q = 0 → invalid
        };

        var adjusted = PriceAdjustment.AdjustedCloses(closes, actions);

        Assert.Equal(20.00m, adjusted[0]); // invalid action → factor 1
    }
}
