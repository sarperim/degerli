using Degerli.Fixtures;
using Degerli.Ingestion.Macro;
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

/// <summary>
/// TKT-foundation-030 acceptance (i): the L4 now-anchored seed must stay <em>fresh</em> on
/// the runtime clock on a <b>weekend</b> seed day, when <c>T</c> is backed to the preceding
/// Friday. The daily macro rows (<c>USD_TRY</c>/<c>EUR_TRY</c>) are "now" ingests — nothing
/// was missed — so their <c>recorded_at</c> sits on the real seed day (the Friday vintage
/// stays the value date/as-of); <c>GOLD</c> (T−10) stays stale by construction and still
/// drives TC-MOV-024's stale side. This is the deterministic, CI-runnable reproduction of
/// the Sunday E2E flake (dev run <c>39085551576</c>, 2026-10-18) — no wall-clock wait.
/// <para>
/// Its own <see cref="PostgresFixture"/> (hence its own class): the weekday class above
/// seeds a different anchor into the same container, and sharing it would make both
/// classes' row assertions order-dependent (FU §8.3 determinism rule).
/// </para>
/// </summary>
public sealed class FixtureUniverseNowAnchoredSundayTests : IClassFixture<PostgresFixture>
{
    // The 2026-10-18 precedent: a Sunday seed start (01:11Z, dev run 39085551576).
    private static readonly DateOnly SeedDay = new(2026, 10, 18);

    private readonly PostgresFixture _fixture;

    public FixtureUniverseNowAnchoredSundayTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Sunday_seed_day_keeps_daily_macro_rows_fresh_on_the_runtime_clock()
    {
        var clock = new FakeTimeProvider(
            new DateTimeOffset(SeedDay.ToDateTime(new TimeOnly(1, 11)), TimeSpan.Zero));
        var anchor = FixtureAnchor.NowAnchored(clock);

        // The trading-day anchor backs a weekend start to the preceding Friday (FU §2).
        Assert.Equal(DayOfWeek.Sunday, SeedDay.DayOfWeek);
        Assert.Equal(new DateOnly(2026, 10, 16), anchor.T);
        Assert.Equal(DayOfWeek.Friday, anchor.T.DayOfWeek);

        var set = FixtureUniverse.Build(anchor);

        await using var db = _fixture.CreateContext();
        await FixtureSeeder.ApplyAsync(db, set);

        // The real L4 read (MarketEndpoints → MacroFreshnessService) with the same clock.
        var freshness = new MacroFreshnessService(db, clock);

        foreach (var code in new[] { "USD_TRY", "EUR_TRY" })
        {
            // The latest daily ingest is the seed day itself — not the backed T (Friday),
            // which would read as ageDays 2 against the daily tolerance of 1.
            var row = await db.MacroValues.SingleAsync(v => v.SeriesCode == code && v.ValueDate == anchor.T);
            Assert.Equal(SeedDay, DateOnly.FromDateTime(row.RecordedAt.UtcDateTime));

            // The published vintage (value date/as-of) is untouched: Friday's value.
            Assert.Equal(anchor.T, row.ValueDate);

            var state = await freshness.GetStateAsync(code);
            Assert.False(state.Stale);
            Assert.Equal("fresh", state.State);
            Assert.Equal(anchor.T, state.AsOf);

            // The fixture's declared freshness agrees with the service read.
            var declared = set.MacroFreshness.Single(f => f.SeriesCode == code);
            Assert.False(declared.IsStale);
            Assert.Equal(0, declared.AgeDays);
        }

        // GOLD is the stale side by construction: last successful ingest T−10.
        var gold = await db.MacroValues.SingleAsync(v => v.SeriesCode == "GOLD");
        Assert.Equal(anchor.Calendar(-10), gold.ValueDate);

        var goldState = await freshness.GetStateAsync("GOLD");
        Assert.True(goldState.Stale);
        Assert.Equal("stale", goldState.State);

        var declaredGold = set.MacroFreshness.Single(f => f.SeriesCode == "GOLD");
        Assert.True(declaredGold.IsStale);
        Assert.Equal(SeedDay.DayNumber - anchor.Calendar(-10).DayNumber, declaredGold.AgeDays);
    }
}
