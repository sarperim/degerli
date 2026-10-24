using System.Net;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Metrics;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-foundation-037 acceptance — TC-MOV-032: the <c>GET /api/v1/market/overview</c>
/// display-freshness flag is <b>stored-prices-based</b>, the same doctrine §10.9.13
/// applies to the write path (stored prices are the truth, never a weekday rule).
/// <para>
/// The defect this pins: the old rule was
/// <c>stale = snapshot.SnapshotDate &lt; LatestTradingDayOnOrBefore(today)</c> — a pure
/// weekday-backing helper — so a Tuesday clock flagged Monday's row (the freshest
/// completed session, since Tuesday's EOD ingests at 20:30 Istanbul) as <c>Bayat</c>.
/// The honest direction must survive: <c>stale: true</c> only when a <b>newer
/// completed session's prices exist</b> in the store that the snapshot row lacks
/// (a genuinely missed <c>market-snapshot</c> run).
/// </para>
/// Snapshot rows are produced by the real <c>market-snapshot</c> job, never
/// hand-written; the stored-prices world is the L2 fixture universe (Tuesday anchor
/// T = 2026-10-06, so Monday = 2026-10-05 is the preceding completed session).
/// </summary>
public sealed class MarketOverviewFreshnessTests : IClassFixture<PostgresFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);

    /// <summary>The latest fixture trading day — Tuesday 2026-10-06.</summary>
    private static DateOnly Tuesday => L2.Anchor.T;

    /// <summary>The preceding completed session — Monday 2026-10-05.</summary>
    private static DateOnly Monday => L2.Anchor.TradingDay(-1);

    /// <summary>
    /// The Tuesday clock (21:00 Istanbul — the harness clock's fixed instant, which
    /// cannot be moved backwards). The calendar day is Tuesday and the store holds no
    /// Tuesday prices: Monday is the freshest completed session that exists, exactly the
    /// window the reported defect served as "Bayat".
    /// </summary>
    private static readonly DateTimeOffset TuesdayClock =
        new(2026, 10, 6, 18, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _postgres;

    public MarketOverviewFreshnessTests(PostgresFixture postgres) => _postgres = postgres;

    // ---------------------------------------------------------------------------------
    // TC-MOV-032 — stored-prices display freshness
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// TC-MOV-032 (i) — a Monday snapshot row + Monday-only stored prices served on a
    /// Tuesday clock is <b>fresh</b>: Monday is the freshest completed session that
    /// exists (Tuesday's EOD only lands at 20:30 Istanbul). Red on the weekday rule,
    /// which compared against Tuesday (a weekday, but not a completed session).
    /// </summary>
    [Fact]
    public async Task TC_MOV_032_monday_snapshot_over_monday_prices_served_tuesday_is_fresh()
    {
        await ResetAsync();
        await DeletePricesAfterAsync(Monday);

        // The real job resolves the requested Tuesday to the latest stored trading day
        // (Monday) — the write path's §10.9.13 rule.
        await RunSnapshotAsync(Tuesday);

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        factory.Clock.SetUtcNow(TuesdayClock);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/market/overview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal("available", root.GetProperty("state").GetString());
        Assert.Equal(Monday.ToString("yyyy-MM-dd"), root.GetProperty("asOf").GetString());
        // No newer completed session's data exists → the badge would be a lie.
        Assert.False(root.GetProperty("stale").GetBoolean());
    }

    /// <summary>
    /// TC-MOV-032 (ii) — the same world plus Tuesday's prices stored (a completed newer
    /// session the snapshot row lacks, i.e. the <c>market-snapshot</c> run was missed)
    /// serves <c>stale: true</c>: the honest missed-run signal must survive the fix.
    /// </summary>
    [Fact]
    public async Task TC_MOV_032_newer_stored_session_missing_from_snapshot_is_stale()
    {
        await ResetAsync();
        await DeletePricesAfterAsync(Monday);
        await RunSnapshotAsync(Tuesday);

        // Tuesday's EOD prices land but the snapshot run never follows: newer data exists.
        await using (var db = _postgres.CreateContext())
        {
            await FixtureSeeder.ApplyAsync(db, L2);
        }

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        factory.Clock.SetUtcNow(TuesdayClock);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/market/overview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        // Last-known-good is still served (FR-MOV-016) — marked stale honestly.
        Assert.Equal("available", root.GetProperty("state").GetString());
        Assert.Equal(Monday.ToString("yyyy-MM-dd"), root.GetProperty("asOf").GetString());
        Assert.True(root.GetProperty("stale").GetBoolean());
    }

    /// <summary>
    /// TC-MOV-032 (iii) — a Tuesday snapshot over Tuesday prices is fresh: the served
    /// row already covers the latest stored trading day (the ordinary trading-day case,
    /// unchanged by the fix).
    /// </summary>
    [Fact]
    public async Task TC_MOV_032_tuesday_snapshot_over_tuesday_prices_is_fresh()
    {
        await ResetAsync();
        await RunSnapshotAsync(Tuesday);

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        factory.Clock.SetUtcNow(TuesdayClock);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/market/overview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal("available", root.GetProperty("state").GetString());
        Assert.Equal(Tuesday.ToString("yyyy-MM-dd"), root.GetProperty("asOf").GetString());
        Assert.False(root.GetProperty("stale").GetBoolean());
    }

    // ---------------------------------------------------------------------------------
    // TC-MDF-085 — the intraday/close price-state contract on the market overview
    // (the frozen seam TKT-foundation-049 builds the Web half against)
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// The session the intraday cases describe: Wednesday 2026-10-07 — the fixture Tuesday
    /// (T) is the last stored session, so this is a fresh trading day whose EOD close has not
    /// landed. The fake clock only moves forward (the harness clock starts at 18:00 UTC on
    /// Tuesday), which is why the intraday world is anchored one day after T.
    /// </summary>
    private static DateOnly Session => Tuesday.AddDays(1);

    /// <summary>A mid-session instant: 12:30 Istanbul (09:30 UTC).</summary>
    private static readonly DateTimeOffset MidSessionClock = new(2026, 10, 7, 9, 30, 0, TimeSpan.Zero);

    /// <summary>The sweep's <c>fetched_at</c> (12:15 Istanbul).</summary>
    private static readonly DateTimeOffset MidSessionAsOf = new(2026, 10, 7, 9, 15, 0, TimeSpan.Zero);

    /// <summary>19:00 Istanbul — past the 18:00 close, before the ~20:30 EOD chain.</summary>
    private static readonly DateTimeOffset AfterCloseClock = new(2026, 10, 7, 16, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// TC-MDF-085 (i) — <b>before the session's EOD close lands</b>, a world whose only
    /// same-day prices are the intraday sweeps serves <c>state: "intraday"</c> with the
    /// sweep's <c>asOf</c> and the session date — the "last update HH:MM — not final" state.
    /// The numeric fields are NULL on the market overview: this lane ingests instrument
    /// quotes, not intraday index levels, and fabricating a market aggregate here would be
    /// computing (BR-MDF-009). <c>state</c>/<c>asOf</c>/<c>sessionDate</c> are the contract.
    /// </summary>
    [Fact]
    public async Task TC_MDF_085_overview_serves_intraday_state_before_the_eod_close_lands()
    {
        await ResetAsync();
        await DeletePricesAfterAsync(Monday);
        await RunSnapshotAsync(Tuesday);
        await InsertIntradayAsync(Session, MidSessionAsOf);

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        factory.Clock.SetUtcNow(MidSessionClock);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/market/overview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var priceState = document.RootElement.GetProperty("priceState");

        Assert.Equal("intraday", priceState.GetProperty("state").GetString());
        Assert.Equal(Session.ToString("yyyy-MM-dd"), priceState.GetProperty("sessionDate").GetString());
        Assert.Equal(MidSessionAsOf, priceState.GetProperty("asOf").GetDateTimeOffset());
        // No market-level intraday quote exists in this lane — honest NULLs, never a 0 reading.
        Assert.Equal(JsonValueKind.Null, priceState.GetProperty("lastPrice").ValueKind);
        Assert.Equal(JsonValueKind.Null, priceState.GetProperty("prevClose").ValueKind);
        Assert.Equal(JsonValueKind.Null, priceState.GetProperty("changePct").ValueKind);
    }

    /// <summary>
    /// TC-MDF-085 (ii) — <b>between the 18:00 Istanbul close and the ~20:30 EOD chain</b> the
    /// served state is still <c>intraday</c> with its last <c>asOf</c>: the close-in-formation
    /// must never be relabelled as final (the planner-pinned honest display, §10.9.18).
    /// </summary>
    [Fact]
    public async Task TC_MDF_085_overview_between_close_and_eod_keeps_intraday_with_its_last_asof()
    {
        await ResetAsync();
        await DeletePricesAfterAsync(Monday);
        await RunSnapshotAsync(Tuesday);
        await InsertIntradayAsync(Session, MidSessionAsOf);

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        // 19:00 Istanbul — the session has closed, the EOD chain has not published it yet.
        factory.Clock.SetUtcNow(AfterCloseClock);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/market/overview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var priceState = document.RootElement.GetProperty("priceState");

        Assert.Equal("intraday", priceState.GetProperty("state").GetString());
        Assert.Equal(Session.ToString("yyyy-MM-dd"), priceState.GetProperty("sessionDate").GetString());
        // Unchanged from the sweep: the last honest observation, never frozen into a close.
        Assert.Equal(MidSessionAsOf, priceState.GetProperty("asOf").GetDateTimeOffset());
    }

    /// <summary>
    /// TC-MDF-085 (iii) — <b>once the session's EOD close is stored</b> the state becomes
    /// <c>close</c>, sourced from <c>daily_prices</c> (an intraday value is never a close).
    /// </summary>
    [Fact]
    public async Task TC_MDF_085_overview_serves_close_once_the_eod_prices_land()
    {
        await ResetAsync();
        await RunSnapshotAsync(Tuesday);
        // The day's sweeps happened too; the EOD close supersedes them.
        await InsertIntradayAsync(Session, MidSessionAsOf);
        var recordedAt = await InsertSessionCloseAsync(Session, new DateTimeOffset(2026, 10, 7, 17, 45, 0, TimeSpan.Zero));

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        // After the 20:30 Istanbul EOD chain (21:00 Istanbul = 18:00 UTC).
        factory.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 7, 18, 0, 0, TimeSpan.Zero));
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/market/overview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var priceState = document.RootElement.GetProperty("priceState");

        Assert.Equal("close", priceState.GetProperty("state").GetString());
        Assert.Equal(Session.ToString("yyyy-MM-dd"), priceState.GetProperty("sessionDate").GetString());
        // The served close's own write timestamp — never the intraday sweep's asOf.
        Assert.Equal(recordedAt.ToUniversalTime(), priceState.GetProperty("asOf").GetDateTimeOffset().ToUniversalTime());
    }

    /// <summary>
    /// TC-MDF-085 (iv) — <b>weekends/holidays</b> serve the last completed session as
    /// <c>close</c>: a Saturday clock over a store whose newest session is Tuesday must not
    /// fabricate a Saturday session (stored prices are the truth, §10.9.13; 048 extends the
    /// calendar to holidays).
    /// </summary>
    [Fact]
    public async Task TC_MDF_085_overview_serves_the_last_completed_session_at_the_weekend()
    {
        await ResetAsync();
        await RunSnapshotAsync(Tuesday);

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        factory.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero)); // Saturday
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/market/overview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var priceState = document.RootElement.GetProperty("priceState");

        Assert.Equal("close", priceState.GetProperty("state").GetString());
        Assert.Equal(Tuesday.ToString("yyyy-MM-dd"), priceState.GetProperty("sessionDate").GetString());
    }

    // ---------------------------------------------------------------------------------
    // TC-MDF-087 (ii) — freshness honesty across a configured BIST holiday
    // (TKT-foundation-048: the calendar seam populated; 037's stored-trading-day rule
    // extended so a holiday Monday is not "a missing session we failed to fetch")
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// The configured holiday these cases describe: the fixture's Monday (2026-10-05) as a
    /// BIST holiday — the world of a holiday Monday followed by a Tuesday morning.
    /// </summary>
    private static DateOnly HolidayMonday => Monday;

    /// <summary>The preceding Friday — the last completed session a holiday Monday leaves.</summary>
    private static DateOnly Friday => Monday.AddDays(-3);

    /// <summary>
    /// TC-MDF-087 (ii) — <b>the market overview reads fresh on the morning after a holiday</b>:
    /// with Monday configured as a holiday and Friday the last completed session, Tuesday's
    /// overview serves Friday's snapshot <c>stale: false</c>. A holiday is a day the market did
    /// not trade, never a session the platform failed to fetch (037's stored-prices rule, the
    /// calendar extension of TKT-foundation-048).
    /// </summary>
    [Fact]
    public async Task TC_MDF_087_market_overview_reads_fresh_the_morning_after_a_holiday()
    {
        await ResetAsync();
        await DeletePricesAfterAsync(Friday);
        // The job resolves Tuesday to the latest stored trading day (Friday) — the write
        // path's §10.9.13 rule, unchanged by the calendar.
        await RunSnapshotAsync(Tuesday);

        await using var host = new DegerliAppFactory(_postgres.ConnectionString);
        host.Clock.SetUtcNow(TuesdayClock);
        await using var factory = WithHoliday(host, HolidayMonday);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/market/overview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal("available", root.GetProperty("state").GetString());
        Assert.Equal(Friday.ToString("yyyy-MM-dd"), root.GetProperty("asOf").GetString());
        // Friday is the last completed session — a holiday leaves nothing uncovered.
        Assert.False(root.GetProperty("stale").GetBoolean());

        // The price state agrees: the last completed stored session, never a fabricated
        // holiday-Monday session (01 §10.9.18).
        var priceState = root.GetProperty("priceState");
        Assert.Equal("close", priceState.GetProperty("state").GetString());
        Assert.Equal(Friday.ToString("yyyy-MM-dd"), priceState.GetProperty("sessionDate").GetString());
    }

    /// <summary>
    /// TC-MDF-087 (ii) — <b>the stock-page section freshness reads fresh on the morning after a
    /// holiday</b>. This is the surface the pre-ticket weekday rule broke: it compared the served
    /// as-of date against the latest <i>Mon–Fri</i> day on or before today, so a Tuesday after a
    /// holiday Monday demanded Monday's data and flagged Friday's honest last session as
    /// <c>stale: true</c>. The rule is now the calendar's last <b>completed</b> trading day
    /// (Monday is a holiday → Friday), the same doctrine the market overview already served.
    /// </summary>
    [Fact]
    public async Task TC_MDF_087_stock_page_reads_fresh_the_morning_after_a_holiday()
    {
        await ResetAsync();
        await DeletePricesAfterAsync(Friday);

        await using var host = new DegerliAppFactory(_postgres.ConnectionString);
        host.Clock.SetUtcNow(TuesdayClock);
        await using var factory = WithHoliday(host, HolidayMonday);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/stocks/ALFA/overview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(Friday.ToString("yyyy-MM-dd"), root.GetProperty("asOf").GetString());
        Assert.False(root.GetProperty("stale").GetBoolean());
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// The same host with a BIST holiday configured through the host's own configuration seam
    /// (`Ingestion:TradingCalendar:Holidays` — the list `appsettings.json` ships, bound to
    /// <c>TradingCalendarOptions</c>). The case's holiday is written at index 0 so it binds
    /// deterministically and survives the shipped list: the entry it displaces is a past-year
    /// date far outside this class's fixture world, and the shipped dates this world could see
    /// (late October 2026) never fall inside the 2026-10-02…06 window the cases serve.
    /// </summary>
    private static WebApplicationFactory<Program> WithHoliday(
        DegerliAppFactory host,
        params DateOnly[] holidays)
    {
        var settings = new Dictionary<string, string?>();
        for (var index = 0; index < holidays.Length; index++)
        {
            settings[$"Ingestion:TradingCalendar:Holidays:{index}"] = holidays[index].ToString("yyyy-MM-dd");
        }

        return host.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(
            (_, configuration) => configuration.AddInMemoryCollection(settings)));
    }

    /// <summary>
    /// Inserts the sweep rows a mid-session world would carry (the sweep itself is pinned by
    /// <c>IntradayQuotesTests</c> / TC-MDF-084; these cases read its storage).
    /// </summary>
    private async Task InsertIntradayAsync(DateOnly session, DateTimeOffset fetchedAt)
    {
        await using var db = _postgres.CreateContext();
        var ids = await db.Instruments
            .Where(instrument => instrument.Symbol == "ALFA" || instrument.Symbol == "BETA")
            .ToDictionaryAsync(instrument => instrument.Symbol, instrument => instrument.Id);

        db.IntradayQuotes.AddRange(
            new IntradayQuote
            {
                InstrumentId = ids["ALFA"],
                SessionDate = session,
                FetchedAt = fetchedAt,
                LastPrice = 21.50m,
                DayVolume = 1_200_000,
                SourceRef = "isyatirim",
            },
            new IntradayQuote
            {
                InstrumentId = ids["BETA"],
                SessionDate = session,
                FetchedAt = fetchedAt,
                LastPrice = 10.40m,
                DayVolume = 5_000_000,
                SourceRef = "isyatirim",
            });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Writes the session's stored EOD closes (the 20:30 chain's output) for the two sweep
    /// symbols, returning their <c>recorded_at</c> — the honest close-state <c>asOf</c>.
    /// </summary>
    private async Task<DateTimeOffset> InsertSessionCloseAsync(DateOnly session, DateTimeOffset recordedAt)
    {
        await using var db = _postgres.CreateContext();
        var ids = await db.Instruments
            .Where(instrument => instrument.Symbol == "ALFA" || instrument.Symbol == "BETA")
            .ToDictionaryAsync(instrument => instrument.Symbol, instrument => instrument.Id);

        foreach (var (symbol, close) in new[] { ("ALFA", 21.80m), ("BETA", 10.55m) })
        {
            db.DailyPrices.Add(new DailyPrice
            {
                InstrumentId = ids[symbol],
                PriceDate = session,
                Open = close,
                High = close,
                Low = close,
                CloseRaw = close,
                CloseAdjusted = null,
                Volume = 1_500_000,
                SourceRef = "isyatirim",
                RecordedAt = recordedAt,
            });
        }

        await db.SaveChangesAsync();
        return recordedAt;
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    /// <summary>
    /// Applies the fixture universe and clears the mutable state each case creates: the
    /// snapshot, the intraday rows, the ledger, and every stored price <b>after</b> the fixture
    /// anchor. The session rows the TC-MDF-085 EOD-close case writes belong to that case; a
    /// later case in the same class must not inherit them (xUnit gives no intra-class ordering),
    /// so the class returns to its documented L2 state (last stored session = Tuesday) every
    /// time — the same hygiene the stock-page class applies in its seed.
    /// </summary>
    private async Task ResetAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM market_snapshots");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM intraday_quotes");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM daily_prices WHERE price_date > {0}", Tuesday);
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

    /// <summary>Runs the real <c>market-snapshot</c> job for <paramref name="date"/>.</summary>
    private async Task RunSnapshotAsync(DateOnly date)
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
        var result = await runner.RunAsync(MarketSnapshotJob.Code, new IngestionRequest(date));
        Assert.Equal("succeeded", result.Status);
    }
}
