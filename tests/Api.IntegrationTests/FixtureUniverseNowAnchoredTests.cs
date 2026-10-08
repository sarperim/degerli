using Degerli.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-foundation-007 acceptance: the L4 now-anchored variant anchors <c>T</c> to the
/// seed (container-start) day and emits flags/offsets — tests never assert wall-clock
/// dates. It loads through the same <see cref="FixtureSeeder"/> path as the L2 variant.
/// </summary>
public sealed class FixtureUniverseNowAnchoredTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public FixtureUniverseNowAnchoredTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Now_anchored_variant_anchors_T_and_emits_flags_and_offsets()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2027, 3, 15, 6, 0, 0, TimeSpan.Zero));
        var anchor = FixtureAnchor.NowAnchored(clock);
        var set = FixtureUniverse.Build(anchor);

        await using var db = _fixture.CreateContext();
        await FixtureSeeder.ApplyAsync(db, set);

        // The latest price row sits on T, and the whole T−9…T window follows the anchor.
        var alfa = await db.Instruments.Where(i => i.Symbol == "ALFA").Select(i => i.Id).SingleAsync();
        var priceDates = await db.DailyPrices
            .Where(p => p.InstrumentId == alfa)
            .Select(p => p.PriceDate)
            .OrderBy(d => d)
            .ToListAsync();
        Assert.Equal(anchor.TradingDays(10), priceDates);
        Assert.Equal(anchor.T, priceDates[^1]);
        Assert.Equal(new DateOnly(2027, 3, 15), anchor.T);

        // GOLD is stale by exactly 10 days, expressed as flag + offset (no wall-clock date).
        var gold = await db.MacroValues.SingleAsync(v => v.SeriesCode == "GOLD");
        Assert.Equal(anchor.Calendar(-10), gold.ValueDate);

        var goldFreshness = set.MacroFreshness.Single(f => f.SeriesCode == "GOLD");
        Assert.True(goldFreshness.IsStale);
        Assert.Equal(10, goldFreshness.AgeDays);

        var usdFreshness = set.MacroFreshness.Single(f => f.SeriesCode == "USD_TRY");
        Assert.False(usdFreshness.IsStale);
        Assert.Equal(0, usdFreshness.AgeDays);

        // Macro revision pair still has two rows for the prior-month value.
        var priorMonth = anchor.MonthStart(-2);
        Assert.Equal(2, await db.MacroValues.CountAsync(v => v.SeriesCode == "TUIK_CPI" && v.ValueDate == priorMonth));
    }

    [Fact]
    public void Both_variants_are_built_by_the_same_module()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2030, 1, 2, 0, 0, 0, TimeSpan.Zero));
        var now = FixtureUniverse.Build(FixtureAnchor.NowAnchored(clock));
        var absolute = FixtureUniverse.Build(FixtureAnchor.L2);

        // Same universe, different anchor: identical row counts, shifted dates.
        Assert.Equal(absolute.Instruments.Count, now.Instruments.Count);
        Assert.Equal(absolute.Prices.Count, now.Prices.Count);
        Assert.Equal(absolute.FundHoldings.Count, now.FundHoldings.Count);
        Assert.NotEqual(absolute.Prices[0].Date, now.Prices[0].Date);
        Assert.True(now.Anchor.IsNowAnchored);
        Assert.False(absolute.Anchor.IsNowAnchored);
    }
}
