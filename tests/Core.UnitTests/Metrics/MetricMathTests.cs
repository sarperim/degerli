using Degerli.Core.Metrics;

namespace Degerli.Core.UnitTests.Metrics;

/// <summary>
/// L1 acceptance tests for the shared quantitative core (TKT-mdf-008; AD-06):
/// the CAGR definition (TC-MDF-027) and the effective-tax-rate fallback (TC-MDF-028),
/// per `.pipeline/testing/market-data-foundation.md` §2 Group E and FU §2/§6.
/// Pure `/src/Core` logic — no DB, network or clock.
/// </summary>
public sealed class MetricMathTests
{
    // ── TC-MDF-027 — CAGR function boundaries ────────────────────────────────

    /// <summary>2 points → (v2/v1) − 1 over 1 compounding interval, whatever the nominal window.</summary>
    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    public void TC_MDF_027_two_points_use_one_interval(int nominalWindow)
    {
        var series = new[]
        {
            new FiscalValue(2024, 80m),
            new FiscalValue(2025, 100m),
        };

        var outcome = MetricMath.Cagr(series, nominalWindow);

        Assert.Equal(0.25m, outcome.Value!.Value, 9);
        Assert.Equal(1, outcome.WindowYears);
    }

    /// <summary>n points → (last/first)^(1/(n−1)) − 1 over the full available span.</summary>
    [Fact]
    public void TC_MDF_027_n_points_use_available_span()
    {
        var series = new[]
        {
            new FiscalValue(2021, 100m),
            new FiscalValue(2022, 110m),
            new FiscalValue(2023, 121m),
        };

        // Nominal window 5 exceeds the 2 available intervals → use the whole span.
        var outcome = MetricMath.Cagr(series, 5);

        Assert.Equal(0.1m, outcome.Value!.Value, 9);
        Assert.Equal(2, outcome.WindowYears);
    }

    /// <summary>A deep series uses exactly the nominal window (distance back from the last point).</summary>
    [Fact]
    public void TC_MDF_027_deep_series_uses_nominal_window()
    {
        // FY2015..FY2025 with values 200,220,250,270,290,400,430,640,700,850,1000.
        var values = new decimal[] { 200, 220, 250, 270, 290, 400, 430, 640, 700, 850, 1000 };
        var series = values.Select((v, i) => new FiscalValue(2015 + i, v)).ToList();

        var threeYear = MetricMath.Cagr(series, 3);

        // FY2022 (640) → FY2025 (1000), 3 intervals.
        Assert.Equal((decimal)Math.Pow((double)(1000m / 640m), 1.0 / 3.0) - 1m, threeYear.Value!.Value, 9);
        Assert.Equal(3, threeYear.WindowYears);
    }

    /// <summary>0 or 1 point → NULL value and NULL window (not meaningful).</summary>
    [Fact]
    public void TC_MDF_027_zero_or_one_point_is_null()
    {
        var empty = MetricMath.Cagr([], 3);
        Assert.Null(empty.Value);
        Assert.Null(empty.WindowYears);

        var single = MetricMath.Cagr([new FiscalValue(2025, 100m)], 3);
        Assert.Null(single.Value);
        Assert.Null(single.WindowYears);
    }

    /// <summary>A declining series yields a negative CAGR — never NULL.</summary>
    [Fact]
    public void TC_MDF_027_declining_series_is_negative_not_null()
    {
        var outcome = MetricMath.Cagr(
            [new FiscalValue(2024, 100m), new FiscalValue(2025, 90m)], 3);

        Assert.NotNull(outcome.Value);
        Assert.True(outcome.Value!.Value < 0m);
        Assert.Equal(-0.1m, outcome.Value!.Value, 9);
    }

    /// <summary>A non-positive endpoint makes the ratio undefined → NULL (never 0).</summary>
    [Fact]
    public void TC_MDF_027_non_positive_endpoint_is_null()
    {
        var zeroLast = MetricMath.Cagr(
            [new FiscalValue(2024, 100m), new FiscalValue(2025, 0m)], 3);
        Assert.Null(zeroLast.Value);

        var negativeFirst = MetricMath.Cagr(
            [new FiscalValue(2024, -5m), new FiscalValue(2025, 100m)], 3);
        Assert.Null(negativeFirst.Value);
    }

    /// <summary>Multi-decade series: precision to 1e-9 relative.</summary>
    [Fact]
    public void TC_MDF_027_multi_decade_precision()
    {
        var series = new[] { new FiscalValue(2015, 80m), new FiscalValue(2025, 100m) };

        var outcome = MetricMath.Cagr(series, 10);

        var expected = (decimal)Math.Pow(100.0 / 80.0, 1.0 / 10.0) - 1m;
        Assert.Equal(10, outcome.WindowYears);
        Assert.True(Math.Abs(outcome.Value!.Value - expected) <= 1e-9m * Math.Abs(expected));
    }

    /// <summary>The honest FCF rule: a sign change in the used span → NULL.</summary>
    [Fact]
    public void TC_MDF_027_sign_change_is_null_when_requested()
    {
        // Ends positive, but the used span crosses zero in FY2023.
        var series = new[]
        {
            new FiscalValue(2021, 20m),
            new FiscalValue(2022, 25m),
            new FiscalValue(2023, -5m),
            new FiscalValue(2024, 10m),
            new FiscalValue(2025, 20m),
        };

        Assert.Null(MetricMath.Cagr(series, 3, nullOnSignChange: true).Value);

        // Without the FCF-only rule the same series is just a (negative) CAGR.
        Assert.NotNull(MetricMath.Cagr(series, 3, nullOnSignChange: false).Value);
    }

    // ── TC-MDF-028 — Effective tax rate fallback ─────────────────────────────

    [Fact]
    public void TC_MDF_028_positive_pretax_uses_tax_over_pretax()
    {
        Assert.Equal(0.2m, MetricMath.EffectiveTaxRate(50m, 250m), 9);
        Assert.Equal(14m / 66m, MetricMath.EffectiveTaxRate(14m, 66m), 9);
    }

    [Fact]
    public void TC_MDF_028_non_positive_pretax_falls_back()
    {
        Assert.Equal(0.25m, MetricMath.EffectiveTaxRate(0m, 0m));
        Assert.Equal(0.25m, MetricMath.EffectiveTaxRate(0m, -30m));
        Assert.Equal(0.25m, MetricMath.EffectiveTaxRate(3m, -5m));
    }

    [Fact]
    public void TC_MDF_028_missing_tax_falls_back()
    {
        Assert.Equal(0.25m, MetricMath.EffectiveTaxRate(null, 100m));
        Assert.Equal(0.25m, MetricMath.EffectiveTaxRate(null, -100m));
    }
}
