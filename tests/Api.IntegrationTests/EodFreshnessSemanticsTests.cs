using System.Net;
using System.Text;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Metrics;
using Degerli.Ingestion.Scheduling;
using Degerli.Ingestion.Valuation;
using Degerli.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// SLICE-002 acceptance — the EOD display-freshness rule shared by the stock/screener surfaces
/// (<see cref="Degerli.Api.Infrastructure.EodSessionFreshness"/>): a served as-of is stale only
/// when it trails the newest session that <b>should have completed</b>, resolved through the
/// configured calendar (weekend ∪ holiday), never a weekday assumption over <i>today</i>. Which
/// session that is follows the EOD chain's completion: the latest trading day strictly before
/// today while the chain is still legitimately running, today's own session at or after the
/// arbitrated <c>DefaultEodCompletionCutoff</c> (19:45 UTC).
/// <para>
/// Doctrine ancestry: TC-MOV-032 (TKT-foundation-037) — the market overview's stored-prices
/// freshness rule this slice extends from the snapshot lane to the store-backed stock/screener
/// surfaces. TC-MOV-032 is a precedent this rule mirrors, not a case re-verified here; the
/// overview's own cases (<c>MarketOverviewFreshnessTests</c>) keep it pinned.
/// </para>
/// <para>
/// The defect this pins (the user's 2026-10-31 report: "I always see something is stale"): the
/// pre-slice rule <c>asOf &lt; LatestTradingDayOnOrBefore(today)</c> compared against today, so
/// every trading day served <c>stale: true</c> from midnight until the 20:30 EOD chain landed —
/// even when Monday's close (the freshest session that exists during a Tuesday) was perfectly
/// healthy. Cases (i), (iii) and (v) are red on the old rule; case (ii) pins that the missed-run
/// signal survives the fix; case (iv) closes the v1.5.44 holiday follow-up.
/// </para>
/// <para>
/// <b>Dated supersession (2026-11-02, user arbitration on PR #174 review round 2 — G-1).</b> The
/// first cut expected the latest trading day strictly before today unconditionally and called the
/// missed-run signal "honestly late". The user arbitrated the completion-cutoff shape: once the
/// chain should have finished (19:45 UTC, derived from the 20:30 TRT start plus the ~100–120 min
/// healthy run — see the rule's own doc), the day's own session is expected, so a genuinely missed
/// same-day run is reported the same night. The cutoff cases below pin both halves; the existing
/// 18:00 UTC cases stay and must still pass, because that instant is mid-chain and fresh by design.
/// </para>
/// <para>
/// The world is the L2 fixture universe (anchor T = Tuesday 2026-10-06); the harness clock's
/// fixed instant is 18:00 UTC that Tuesday. Prices are trimmed with raw SQL exactly as
/// <c>MarketOverviewFreshnessTests</c> trims them — the store is the truth the rule reads.
/// </para>
/// </summary>
public sealed class EodFreshnessSemanticsTests : IClassFixture<PostgresFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);

    /// <summary>The latest fixture trading day — Tuesday 2026-10-06.</summary>
    private static DateOnly Tuesday => L2.Anchor.T;

    /// <summary>The preceding completed session — Monday 2026-10-05.</summary>
    private static DateOnly Monday => L2.Anchor.TradingDay(-1);

    /// <summary>The session before that — Friday 2026-10-02.</summary>
    private static DateOnly Friday => L2.Anchor.TradingDay(-2);

    /// <summary>The harness clock's fixed instant: Tuesday 2026-10-06 18:00 UTC (21:00 TRT).</summary>
    private static readonly DateTimeOffset TuesdayClock =
        new(2026, 10, 6, 18, 0, 0, TimeSpan.Zero);

    /// <summary>Saturday 2026-10-10 12:00 UTC — the weekend rule-level clock.</summary>
    private static readonly DateTimeOffset SaturdayClock =
        new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Tuesday 2026-10-06 20:30 UTC (23:30 TRT) — past the 19:45 UTC completion cutoff, so the
    /// day's own EOD chain should have finished.
    /// </summary>
    private static readonly DateTimeOffset TuesdayPostCutoffClock =
        new(2026, 10, 6, 20, 30, 0, TimeSpan.Zero);

    /// <summary>
    /// Tuesday 2026-10-06 exactly at the cutoff instant (19:45 UTC) — the rule is inclusive:
    /// at-or-after the cutoff the day's own session is expected.
    /// </summary>
    private static readonly DateTimeOffset TuesdayAtCutoffClock =
        new(2026, 10, 6, 19, 45, 0, TimeSpan.Zero);

    private readonly PostgresFixture _postgres;

    public EodFreshnessSemanticsTests(PostgresFixture postgres) => _postgres = postgres;

    // ---------------------------------------------------------------------------------
    // The rule itself (no database): the completed-session expectation
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// The rule's core: on a Tuesday clock the expected completed session is Monday, so
    /// Monday reads fresh (Tuesday's EOD lands ~20:30) and Friday reads stale (Monday's
    /// run was genuinely missed). On a Saturday clock Friday is the expected session, so
    /// Friday reads fresh — the weekend never demands a session that has not happened.
    /// </summary>
    [Fact]
    public void Eod_Freshness_rule_expects_the_last_completed_session_never_today()
    {
        var calendar = new ConfiguredTradingCalendar(
            Microsoft.Extensions.Options.Options.Create(new TradingCalendarOptions()));
        var tuesday = new FakeTimeProvider(TuesdayClock);
        var saturday = new FakeTimeProvider(SaturdayClock);

        // Tuesday: Monday is the newest session that should have completed.
        var (asOf, stale) = Degerli.Api.Infrastructure.EodSessionFreshness
            .AgainstCompletedSession(Monday, calendar, tuesday);
        Assert.Equal(Monday, asOf);
        Assert.False(stale);

        // Tuesday with Tuesday's EOD already landed: still fresh, trivially.
        (asOf, stale) = Degerli.Api.Infrastructure.EodSessionFreshness
            .AgainstCompletedSession(Tuesday, calendar, tuesday);
        Assert.Equal(Tuesday, asOf);
        Assert.False(stale);

        // Tuesday with only Friday stored: Monday's run was missed — the honest signal.
        (asOf, stale) = Degerli.Api.Infrastructure.EodSessionFreshness
            .AgainstCompletedSession(Friday, calendar, tuesday);
        Assert.Equal(Friday, asOf);
        Assert.True(stale);

        // Saturday: the preceding Friday is the newest completed session.
        var fridayBeforeTheSaturday = new DateOnly(2026, 10, 9);
        (asOf, stale) = Degerli.Api.Infrastructure.EodSessionFreshness
            .AgainstCompletedSession(fridayBeforeTheSaturday, calendar, saturday);
        Assert.Equal(fridayBeforeTheSaturday, asOf);
        Assert.False(stale);

        // An empty store keeps the caller's today-fallback and reads fresh — availability
        // is the `state` lane's question, never this boolean's.
        (asOf, stale) = Degerli.Api.Infrastructure.EodSessionFreshness
            .AgainstCompletedSession(null, calendar, tuesday);
        Assert.Equal(Tuesday, asOf);
        Assert.False(stale);
    }

    /// <summary>
    /// The calendar, never a weekday assumption, resolves the expectation: with Monday
    /// 2026-10-05 configured as a market holiday, a Tuesday clock expects Friday — so
    /// Friday's close reads fresh (the v1.5.44 follow-up, closed for these surfaces).
    /// </summary>
    [Fact]
    public void Eod_Freshness_rule_resolves_holidays_through_the_calendar()
    {
        var calendar = new ConfiguredTradingCalendar(
            Microsoft.Extensions.Options.Options.Create(new TradingCalendarOptions
            {
                Holidays = [Monday.ToString("yyyy-MM-dd")],
            }));
        var clock = new FakeTimeProvider(TuesdayClock);

        var (asOf, stale) = Degerli.Api.Infrastructure.EodSessionFreshness
            .AgainstCompletedSession(Friday, calendar, clock);
        Assert.Equal(Friday, asOf);
        Assert.False(stale);
    }

    /// <summary>
    /// The arbitrated completion cutoff (2026-11-02, user decision on PR #174 review round 2):
    /// at or after 19:45 UTC the day's own session is expected, so a missed same-day EOD is
    /// reported the same night rather than the next trading day. Past the cutoff, Monday's data
    /// on a Tuesday clock reads <b>stale</b> (Tuesday's chain should have finished and produced
    /// Tuesday's metrics) while Tuesday's data reads fresh. Before the cutoff the same Monday
    /// data reads fresh (see the core case), which is why the cutoff, not the calendar day alone,
    /// is what moved.
    /// </summary>
    [Fact]
    public void Eod_Freshness_rule_after_the_cutoff_expects_the_same_day_session()
    {
        var calendar = new ConfiguredTradingCalendar(
            Microsoft.Extensions.Options.Options.Create(new TradingCalendarOptions()));
        var postCutoff = new FakeTimeProvider(TuesdayPostCutoffClock);

        // Past the cutoff, the day's own EOD should have landed: only Monday stored is a miss.
        var (asOf, stale) = Degerli.Api.Infrastructure.EodSessionFreshness
            .AgainstCompletedSession(Monday, calendar, postCutoff);
        Assert.Equal(Monday, asOf);
        Assert.True(stale);

        // Past the cutoff with Tuesday's own session stored: fresh.
        (asOf, stale) = Degerli.Api.Infrastructure.EodSessionFreshness
            .AgainstCompletedSession(Tuesday, calendar, postCutoff);
        Assert.Equal(Tuesday, asOf);
        Assert.False(stale);
    }

    /// <summary>
    /// The cutoff's boundary is <b>inclusive</b>: at exactly 19:45 UTC (22:45 TRT) the day's own
    /// session is already the expected one — 19:44:59 is mid-chain, 19:45:00 is post-chain. Pinned
    /// as its own case so an off-by-one flip in the comparison cannot pass unnoticed.
    /// </summary>
    [Fact]
    public void Eod_Freshness_rule_cutoff_boundary_is_inclusive()
    {
        var calendar = new ConfiguredTradingCalendar(
            Microsoft.Extensions.Options.Options.Create(new TradingCalendarOptions()));

        // One second before the cutoff: still the mid-chain rule — Monday is the expected session.
        var justBefore = new FakeTimeProvider(TuesdayAtCutoffClock.AddSeconds(-1));
        var (asOf, stale) = Degerli.Api.Infrastructure.EodSessionFreshness
            .AgainstCompletedSession(Monday, calendar, justBefore);
        Assert.Equal(Monday, asOf);
        Assert.False(stale);

        // Exactly at the cutoff: the day's own session is expected — Monday now trails it.
        var atCutoff = new FakeTimeProvider(TuesdayAtCutoffClock);
        (asOf, stale) = Degerli.Api.Infrastructure.EodSessionFreshness
            .AgainstCompletedSession(Monday, calendar, atCutoff);
        Assert.Equal(Monday, asOf);
        Assert.True(stale);

        (asOf, stale) = Degerli.Api.Infrastructure.EodSessionFreshness
            .AgainstCompletedSession(Tuesday, calendar, atCutoff);
        Assert.Equal(Tuesday, asOf);
        Assert.False(stale);
    }

    /// <summary>
    /// The cutoff never invents a session. Past the cutoff on a normal Monday, the day's own
    /// session is expected — so with only Friday stored the rule reads <b>stale</b> (the same-night
    /// honesty the cutoff exists for). With that same Monday 2026-10-05 configured as a market
    /// holiday, the calendar resolves the expected session to Friday, so Friday's close still reads
    /// <b>fresh</b> after the cutoff: a holiday is never made "expected" merely because the clock
    /// passed 19:45 UTC.
    /// </summary>
    [Fact]
    public void Eod_Freshness_rule_cutoff_never_expects_a_holiday_session()
    {
        var weekdayCalendar = new ConfiguredTradingCalendar(
            Microsoft.Extensions.Options.Options.Create(new TradingCalendarOptions()));
        var mondayPostCutoff = new FakeTimeProvider(
            new DateTimeOffset(Monday.ToDateTime(new TimeOnly(20, 30)), TimeSpan.Zero));

        // Control: a normal Monday past the cutoff expects Monday itself, so Friday-only is stale.
        var (asOf, stale) = Degerli.Api.Infrastructure.EodSessionFreshness
            .AgainstCompletedSession(Friday, weekdayCalendar, mondayPostCutoff);
        Assert.Equal(Friday, asOf);
        Assert.True(stale);

        // Same clock, Monday declared a holiday: the expectation falls back to Friday — fresh.
        var holidayCalendar = new ConfiguredTradingCalendar(
            Microsoft.Extensions.Options.Options.Create(new TradingCalendarOptions
            {
                Holidays = [Monday.ToString("yyyy-MM-dd")],
            }));

        (asOf, stale) = Degerli.Api.Infrastructure.EodSessionFreshness
            .AgainstCompletedSession(Friday, holidayCalendar, mondayPostCutoff);
        Assert.Equal(Friday, asOf);
        Assert.False(stale);
    }

    // ---------------------------------------------------------------------------------
    // The surfaces (real endpoints, real store): the reported defect
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// (i) The reported defect, end to end: Monday's prices served on the Tuesday clock
    /// read <b>fresh</b> on <c>GET /api/v1/stocks</c> — Monday is the freshest completed
    /// session that exists (Tuesday's EOD only lands ~20:30). Red on the pre-slice rule,
    /// which compared against Tuesday and served the all-day stale badge.
    /// </summary>
    [Fact]
    public async Task Eod_Freshness_monday_prices_on_tuesday_clock_read_fresh()
    {
        await SeedAsync();
        await DeletePricesAfterAsync(Monday);

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/stocks");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(Monday.ToString("yyyy-MM-dd"), root.GetProperty("asOf").GetString());
        Assert.False(root.GetProperty("stale").GetBoolean());
    }

    /// <summary>
    /// (ii) The missed-run signal survives: with only Friday stored on the Tuesday 18:00 UTC
    /// clock (Monday's EOD genuinely missed, Tuesday's chain still mid-run) the list serves
    /// <c>stale: true</c>. Under the arbitrated completion-cutoff rule the badge also tells the
    /// same-night truth once Tuesday's chain should have finished — the cutoff cases above pin
    /// that half.
    /// </summary>
    [Fact]
    public async Task Eod_Freshness_missed_session_is_stale_on_the_next_trading_day()
    {
        await SeedAsync();
        await DeletePricesAfterAsync(Friday);

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/stocks");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(Friday.ToString("yyyy-MM-dd"), root.GetProperty("asOf").GetString());
        Assert.True(root.GetProperty("stale").GetBoolean());
    }

    /// <summary>
    /// (iii) The stock page's overview section carries the same rule (its own
    /// <c>FreshnessAsync</c> was one of the four weekday-rule copies). Red on the
    /// pre-slice rule.
    /// </summary>
    [Fact]
    public async Task Eod_Freshness_stock_overview_carries_the_completed_session_rule()
    {
        await SeedAsync();
        await DeletePricesAfterAsync(Monday);

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/stocks/ALFA/overview");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(Monday.ToString("yyyy-MM-dd"), root.GetProperty("asOf").GetString());
        Assert.False(root.GetProperty("stale").GetBoolean());
    }

    /// <summary>
    /// (iv) The holiday half of the v1.5.44 follow-up, end to end: with Monday configured
    /// as a market holiday and Friday the last stored session, the Tuesday clock serves
    /// Friday <b>fresh</b>. Red on the pre-slice rule (a weekday rule sees only Tuesday).
    /// </summary>
    [Fact]
    public async Task Eod_Freshness_holiday_serves_the_last_completed_session_fresh()
    {
        await SeedAsync();
        await DeletePricesAfterAsync(Friday);

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString)
            .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // Overrides the shipped list's first entry; the fixture world's only
                    // relevant closure is Monday 2026-10-05.
                    ["Ingestion:TradingCalendar:Holidays:0"] = Monday.ToString("yyyy-MM-dd"),
                });
            }));
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/stocks");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(Friday.ToString("yyyy-MM-dd"), root.GetProperty("asOf").GetString());
        Assert.False(root.GetProperty("stale").GetBoolean());
    }

    /// <summary>
    /// (v) The screener carries the same rule over its metrics as-of: metrics recomputed
    /// at Monday, served on the Tuesday clock, read fresh. Red on the pre-slice rule
    /// (the screener compared against Tuesday). The Monday metrics rows are produced by
    /// the real <c>metrics-recompute</c> job, never hand-written.
    /// </summary>
    [Fact]
    public async Task Eod_Freshness_screener_carries_the_completed_session_rule()
    {
        await SeedAsync();
        await DeletePricesAfterAsync(Monday);
        await TrimMetricsAfterAsync(Monday);
        await RunMetricsRecomputeAsync(Monday);

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await RunScreenerAsync(client, """
            { "criteria": [ { "metricCode": "pe", "bound": "max", "maxValue": 1000 } ] }
            """);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(Monday.ToString("yyyy-MM-dd"), root.GetProperty("asOf").GetString());
        Assert.False(root.GetProperty("stale").GetBoolean());
    }

    /// <summary>
    /// (vi) The unavailable world keeps its posture: an empty price store serves
    /// <c>asOf = today</c>, <c>stale: false</c> — availability is the state lane's
    /// question, never this boolean's (the pre-existing behaviour of these surfaces).
    /// </summary>
    [Fact]
    public async Task Eod_Freshness_empty_store_keeps_the_unavailable_posture()
    {
        await SeedAsync();
        await using (var db = _postgres.CreateContext())
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM daily_prices");
        }

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/stocks");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(Tuesday.ToString("yyyy-MM-dd"), root.GetProperty("asOf").GetString());
        Assert.False(root.GetProperty("stale").GetBoolean());
    }

    /// <summary>
    /// The slice's cadence half is shipped configuration: the two intraday KAP
    /// <c>JobSchedules</c> entries (<c>disclosures</c>, <c>statements</c> — every two hours
    /// 09:00–19:00 Europe/Istanbul, trading-day gated) and the hourly intraday sweep, all
    /// read from the shipped <c>appsettings.json</c> (the TC-MDF-084 shipped-config
    /// precedent). Configuration constants, not code: the cadence is user-adjustable.
    /// </summary>
    [Fact]
    public void Eod_Freshness_kap_cadence_entries_are_shipped_config_constants()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Api", "appsettings.json")));
        var schedules = document.RootElement
            .GetProperty("Ingestion")
            .GetProperty("Scheduler")
            .GetProperty("JobSchedules")
            .EnumerateArray()
            .ToList();

        void AssertKapSweep(string jobCode)
        {
            var schedule = schedules.Single(entry => entry.GetProperty("JobCode").GetString() == jobCode);
            // 09:00/11:00/13:00/15:00/17:00/19:00 Europe/Istanbul — the six-field Cronos form.
            Assert.Equal("0 0 9-19/2 * * *", schedule.GetProperty("Cron").GetString());
            // A market-publishing lane: a configured holiday skips the sweep and writes the
            // one `holiday-skip` ledger row (TKT-foundation-048 pin 3).
            Assert.True(schedule.GetProperty("TradingDaysOnly").GetBoolean());
        }

        AssertKapSweep("disclosures");
        AssertKapSweep("statements");

        // The hourly intraday sweep (the re-pinned TC-MDF-084 constant).
        var intraday = schedules.Single(entry => entry.GetProperty("JobCode").GetString() == "intraday-quotes");
        Assert.Equal("0 15 10-17 * * 1-5", intraday.GetProperty("Cron").GetString());
        Assert.True(intraday.GetProperty("TradingDaysOnly").GetBoolean());
    }

    /// <summary>
    /// (vii) Key metrics carries the completed-session rule over its derived-metrics as-of:
    /// Monday metrics served on the Tuesday 18:00 UTC clock read fresh; after the 19:45 UTC
    /// completion cutoff (20:30 UTC) they read stale.
    /// </summary>
    [Fact]
    public async Task Eod_Freshness_stock_key_metrics_carries_the_completed_session_rule()
    {
        await SeedAsync();
        await DeletePricesAfterAsync(Monday);
        await TrimMetricsAfterAsync(Monday);
        await RunMetricsRecomputeAsync(Monday);

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        // Mid-chain at 18:00 UTC Tuesday: Monday metrics are the latest completed session — fresh.
        using (var response = await client.GetAsync("/api/v1/stocks/ALFA/key-metrics"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = document.RootElement;
            Assert.Equal(Monday.ToString("yyyy-MM-dd"), root.GetProperty("asOf").GetString());
            Assert.False(root.GetProperty("stale").GetBoolean());
        }

        // Past the 19:45 UTC cutoff (20:30 UTC): Tuesday's chain should have finished — stale.
        factory.Clock.SetUtcNow(TuesdayPostCutoffClock);

        using (var response = await client.GetAsync("/api/v1/stocks/ALFA/key-metrics"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = document.RootElement;
            Assert.Equal(Monday.ToString("yyyy-MM-dd"), root.GetProperty("asOf").GetString());
            Assert.True(root.GetProperty("stale").GetBoolean());
        }
    }

    /// <summary>
    /// (viii) DCF baseline carries the completed-session rule over its canonical price as-of:
    /// Monday prices served on the Tuesday 18:00 UTC clock read fresh; after the 19:45 UTC
    /// completion cutoff (20:30 UTC) they read stale.
    /// </summary>
    [Fact]
    public async Task Eod_Freshness_dcf_baseline_carries_the_completed_session_rule()
    {
        await SeedAsync();
        await DeletePricesAfterAsync(Monday);

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        await RunBaselineJobAsync(factory, Monday);
        using var client = CreateClient(factory);

        // Mid-chain at 18:00 UTC Tuesday: Monday prices are the latest completed session — fresh.
        using (var response = await client.GetAsync("/api/v1/stocks/ALFA/dcf"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var price = document.RootElement.GetProperty("price");
            Assert.Equal(Monday.ToString("yyyy-MM-dd"), price.GetProperty("asOf").GetString());
            Assert.False(price.GetProperty("stale").GetBoolean());
        }

        // Past the 19:45 UTC cutoff (20:30 UTC): Tuesday's chain should have finished — stale.
        factory.Clock.SetUtcNow(TuesdayPostCutoffClock);

        using (var response = await client.GetAsync("/api/v1/stocks/ALFA/dcf"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var price = document.RootElement.GetProperty("price");
            Assert.Equal(Monday.ToString("yyyy-MM-dd"), price.GetProperty("asOf").GetString());
            Assert.True(price.GetProperty("stale").GetBoolean());
        }
    }

    /// <summary>
    /// (ix) DCF baseline holiday handling: with Monday configured as a holiday and Friday
    /// prices stored, the Tuesday clock serves Friday fresh.
    /// </summary>
    [Fact]
    public async Task Eod_Freshness_dcf_baseline_holiday_serves_the_last_completed_session_fresh()
    {
        await SeedAsync();
        await DeletePricesAfterAsync(Friday);

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString)
            .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Ingestion:TradingCalendar:Holidays:0"] = Monday.ToString("yyyy-MM-dd"),
                });
            }));
        await RunBaselineJobAsync(factory, Friday);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/stocks/ALFA/dcf");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var price = document.RootElement.GetProperty("price");
        Assert.Equal(Friday.ToString("yyyy-MM-dd"), price.GetProperty("asOf").GetString());
        Assert.False(price.GetProperty("stale").GetBoolean());
    }

    /// <summary>
    /// (x) DCF compute carries the completed-session rule over its canonical price as-of:
    /// Monday prices served on the Tuesday 18:00 UTC clock read fresh; after the 19:45 UTC
    /// completion cutoff (20:30 UTC) they read stale.
    /// </summary>
    [Fact]
    public async Task Eod_Freshness_dcf_compute_carries_the_completed_session_rule()
    {
        await SeedAsync();
        await DeletePricesAfterAsync(Monday);

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        // Mid-chain at 18:00 UTC Tuesday: Monday prices are the latest completed session — fresh.
        using (var response = await PostDcfComputeAsync(client, "ALFA"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var price = document.RootElement.GetProperty("price");
            Assert.Equal(Monday.ToString("yyyy-MM-dd"), price.GetProperty("asOf").GetString());
            Assert.False(price.GetProperty("stale").GetBoolean());
        }

        // Past the 19:45 UTC cutoff (20:30 UTC): Tuesday's chain should have finished — stale.
        factory.Clock.SetUtcNow(TuesdayPostCutoffClock);

        using (var response = await PostDcfComputeAsync(client, "ALFA"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var price = document.RootElement.GetProperty("price");
            Assert.Equal(Monday.ToString("yyyy-MM-dd"), price.GetProperty("asOf").GetString());
            Assert.True(price.GetProperty("stale").GetBoolean());
        }
    }

    /// <summary>
    /// (xi) DCF compute holiday handling: with Monday configured as a holiday and Friday
    /// prices stored, the Tuesday clock serves Friday fresh.
    /// </summary>
    [Fact]
    public async Task Eod_Freshness_dcf_compute_holiday_serves_the_last_completed_session_fresh()
    {
        await SeedAsync();
        await DeletePricesAfterAsync(Friday);

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString)
            .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Ingestion:TradingCalendar:Holidays:0"] = Monday.ToString("yyyy-MM-dd"),
                });
            }));
        using var client = CreateClient(factory);

        using var response = await PostDcfComputeAsync(client, "ALFA");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var price = document.RootElement.GetProperty("price");
        Assert.Equal(Friday.ToString("yyyy-MM-dd"), price.GetProperty("asOf").GetString());
        Assert.False(price.GetProperty("stale").GetBoolean());
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    private async Task SeedAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
    }

    /// <summary>
    /// Removes every stored price after <paramref name="day"/> — the world before that
    /// session's EOD landed (the store is the truth the freshness rule reads).
    /// </summary>
    private async Task DeletePricesAfterAsync(DateOnly day)
    {
        await using var db = _postgres.CreateContext();
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM daily_prices WHERE price_date > {0}",
            day);
    }

    /// <summary>
    /// Removes every derived-metric row after <paramref name="day"/> so the screener's
    /// metrics as-of is the day the case describes.
    /// </summary>
    private async Task TrimMetricsAfterAsync(DateOnly day)
    {
        await using var db = _postgres.CreateContext();
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM derived_metrics WHERE as_of_date > {0}",
            day);
    }

    /// <summary>Runs the real <c>metrics-recompute</c> job for <paramref name="date"/>.</summary>
    private async Task RunMetricsRecomputeAsync(DateOnly date)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _postgres.ConnectionString,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddMarketDataIngestion(configuration);

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IIngestionJobRunner>();
        var result = await runner.RunAsync(MetricsRecomputeJob.Code, new IngestionRequest(date));
        Assert.Equal("succeeded", result.Status);
    }

    /// <summary>The screener's POST needs the CSRF pair (the unsafe-method contract).</summary>
    private static async Task<HttpResponseMessage> RunScreenerAsync(HttpClient client, string json)
    {
        using var csrfRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/csrf-token");
        csrfRequest.Headers.Add("X-Forwarded-For", Guid.NewGuid().ToString("N"));
        using var csrfResponse = await client.SendAsync(csrfRequest);
        csrfResponse.EnsureSuccessStatusCode();
        using var csrfDocument = JsonDocument.Parse(await csrfResponse.Content.ReadAsStringAsync());
        var token = csrfDocument.RootElement.GetProperty("token").GetString();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/screener/run")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-CSRF-Token", token);
        request.Headers.Add("X-Forwarded-For", Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }

    private static async Task RunBaselineJobAsync(WebApplicationFactory<Program> factory, DateOnly buildDate)
    {
        using var scope = factory.Services.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IIngestionJobRunner>();
        var result = await runner.RunAsync(
            DcfBaselineGenerationJob.Code,
            new IngestionRequest(buildDate));
        Assert.Equal(IngestResult.Succeeded, result.Status);
    }

    private static async Task<HttpResponseMessage> PostDcfComputeAsync(HttpClient client, string symbol)
    {
        using var csrfRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/csrf-token");
        csrfRequest.Headers.Add("X-Forwarded-For", Guid.NewGuid().ToString("N"));
        using var csrfResponse = await client.SendAsync(csrfRequest);
        csrfResponse.EnsureSuccessStatusCode();
        using var csrfDocument = JsonDocument.Parse(await csrfResponse.Content.ReadAsStringAsync());
        var token = csrfDocument.RootElement.GetProperty("token").GetString();

        var payload = """
            {
                "base_fcf": 100,
                "growth_rate": 0.10,
                "horizon_years": 5,
                "terminal_growth": 0.03,
                "discount_rate": 0.12,
                "debt": 400,
                "cash": 200,
                "share_count": 100
            }
            """;

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/stocks/{symbol}/dcf/compute")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-CSRF-Token", token);
        request.Headers.Add("X-Forwarded-For", Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Degerli.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent!;
        }

        throw new InvalidOperationException("Could not locate the repository root (Degerli.sln).");
    }
}
