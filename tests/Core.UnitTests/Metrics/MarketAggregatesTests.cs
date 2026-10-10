using Degerli.Core.Metrics;

namespace Degerli.Core.UnitTests.Metrics;

/// <summary>
/// L1 acceptance tests for the shared market-aggregate core (TKT-mov-003; AD-06):
/// the mover volume-eligibility boundary (TC-MOV-003, BR-MOV-011) and the market P/E
/// loss-maker exclusion (TC-MOV-004, BR-MOV-010/D-05). Pure `/src/Core` logic.
/// </summary>
public sealed class MarketAggregatesTests
{
    private static readonly IndexMove Flat = new(1000m, 0m);

    /// <summary>
    /// TC-MOV-003 (boundary) — a stock whose volume equals the threshold exactly IS
    /// eligible (inclusive ≥), while one just under it is not.
    /// </summary>
    [Fact]
    public void TC_MOV_003_exact_threshold_is_inclusive()
    {
        var constituents = new List<MarketConstituent>
        {
            // The single largest change sits *below* the threshold → never eligible.
            Constituent("BIG", change: 0.20m, volume: 999_999, ni: 100m),
        };

        // Nine clearly-eligible gainers (ranks 1..9 by change).
        for (var i = 1; i <= 9; i++)
        {
            constituents.Add(Constituent($"G{i}", change: 0.01m * i, volume: 2_000_000, ni: 100m));
        }

        // Exactly at the threshold; its change slots it into the top ten, so it must
        // appear if and only if the boundary is inclusive.
        constituents.Add(Constituent("EDGE", change: 0.05m, volume: 1_000_000, ni: 100m));

        var result = MarketAggregates.Compute(Flat, Flat, constituents, volumeEligibilityThreshold: 1_000_000m);

        Assert.Equal(10, result.Gainers.Count);
        Assert.Contains("EDGE", result.Gainers.Select(g => g.Symbol));
        Assert.DoesNotContain("BIG", result.Gainers.Select(g => g.Symbol));
    }

    /// <summary>TC-MOV-004 (degenerate) — every constituent a loss-maker → NULL P/E, all counted.</summary>
    [Fact]
    public void TC_MOV_004_all_loss_makers_yield_null_pe_with_full_exclusion_count()
    {
        var constituents = new List<MarketConstituent>
        {
            Constituent("A", change: 0.01m, volume: 2_000_000, ni: -10m),
            Constituent("B", change: -0.01m, volume: 2_000_000, ni: 0m),
            Constituent("C", change: 0.02m, volume: 2_000_000, ni: null),
        };

        var result = MarketAggregates.Compute(Flat, Flat, constituents, volumeEligibilityThreshold: 1_000_000m);

        Assert.Null(result.MarketPe);
        Assert.Equal(3, result.MarketPeExcludedCount);
    }

    /// <summary>TC-MOV-004 — loss-makers are excluded from the cap-weighted P/E and counted.</summary>
    [Fact]
    public void TC_MOV_004_loss_makers_excluded_and_counted()
    {
        var constituents = new List<MarketConstituent>
        {
            Constituent("A", change: 0.01m, volume: 2_000_000, ni: 100m, mcap: 1000m),
            Constituent("B", change: 0.01m, volume: 2_000_000, ni: -50m, mcap: 500m),
        };

        var result = MarketAggregates.Compute(Flat, Flat, constituents, volumeEligibilityThreshold: 1_000_000m);

        Assert.Equal(10m, result.MarketPe);
        Assert.Equal(1, result.MarketPeExcludedCount);
    }

    private static MarketConstituent Constituent(
        string symbol,
        decimal change,
        long volume,
        decimal? ni,
        decimal mcap = 1000m,
        string? sector = "A") =>
        new(symbol, sector, mcap, change, volume, ni, DividendTtm: 0m);
}
