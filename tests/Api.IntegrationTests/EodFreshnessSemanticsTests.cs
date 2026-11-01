using System.Net;
using System.Text;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Metrics;
using Degerli.Ingestion.Scheduling;
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
/// when it trails the newest session that <b>should have completed</b> — the latest trading day
/// strictly before today, resolved through the configured calendar (weekend ∪ holiday), never a
/// weekday assumption over <i>today</i>.
/// <para>
/// The defect this pins (the user's 2026-10-31 report: "I always see something is stale"): the
/// pre-slice rule <c>asOf &lt; LatestTradingDayOnOrBefore(today)</c> compared against today, so
/// every trading day served <c>stale: true</c> from midnight until the 20:30 EOD chain landed —
/// even when Monday's close (the freshest session that exists during a Tuesday) was perfectly
/// healthy. Cases (i), (iii) and (v) are red on the old rule; case (ii) pins that the missed-run
/// signal survives the fix, honestly late; case (iv) closes the v1.5.44 holiday follow-up.
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
    /// (ii) The missed-run signal survives, honestly late: with only Friday stored on the
    /// Tuesday clock (Monday's EOD genuinely missed) the list serves <c>stale: true</c> —
    /// the same-night detection stays with the watchdog/alerting lanes; the badge is the
    /// next-day truth, never a same-night lie.
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
