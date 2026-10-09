using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Api.IntegrationTests.Harness.Mail;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.Scheduling;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Serilog;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-mdf-005 acceptance — the C3c scheduler: the trading-day gate (TC-MDF-011) and the
/// 5/15/60-minute retry ladder (TC-MDF-012). Every test drives the real scheduler engine
/// over a <see cref="FakeTimeProvider"/> so schedule/retry edges are exact, against a
/// migrated Testcontainers PostgreSQL and the FU §10 WireMock source double, composing
/// the same <c>AddMarketDataIngestion</c> registration the API host uses.
/// </summary>
public sealed class SchedulerTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);

    /// <summary>The route the prices adapter reads; both the down and the ok stubs are
    /// bound here so the test can switch the source mid-run without touching config.</summary>
    private const string PricesPath = "/prices/eod";

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public SchedulerTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    /// <summary>TC-MDF-011 — No trigger on non-trading days.</summary>
    [Fact]
    public async Task TC_MDF_011_No_trigger_on_non_trading_days()
    {
        await ResetAsync();
        _wireMock.Server.ResetMappings();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesOkTminus1, path: PricesPath);

        await using var host = BuildIngestion();
        var clock = host.Clock;

        // The clock starts on Saturday 2026-10-03 (TRT) and sweeps the weekend.
        clock.SetUtcNow(Trt(2026, 10, 3, 0, 0));

        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 3, 23, 59));
        Assert.Equal(0, await LedgerCountAsync());

        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 4, 23, 59));
        Assert.Equal(0, await LedgerCountAsync());

        // Monday 20:30 TRT triggers normally (the fixture's T−1 trading day).
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 5, 20, 30));

        var runs = await LedgerAsync();
        var run = Assert.Single(runs);
        Assert.Equal("prices", run.JobCode);
        Assert.Equal("succeeded", run.Status);
        Assert.Equal(Trt(2026, 10, 5, 20, 30), run.StartedAt!.Value);

        await using var db = _postgres.CreateContext();
        Assert.Equal(16, await db.DailyPrices.CountAsync(p => p.PriceDate == new DateOnly(2026, 10, 5)));
    }

    /// <summary>
    /// TC-MDF-011 (supporting) — the holiday calendar is configuration: a weekday listed
    /// as a holiday is a non-trading day, and its 20:30 occurrence is skipped.
    /// </summary>
    [Fact]
    public async Task TC_MDF_011_Holiday_calendar_is_config()
    {
        await ResetAsync();
        _wireMock.Server.ResetMappings();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesOkTminus1, path: PricesPath);

        await using var host = BuildIngestion(holiday: "2026-10-05");
        var clock = host.Clock;
        clock.SetUtcNow(Trt(2026, 10, 5, 0, 0));

        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 5, 23, 59));

        Assert.Equal(0, await LedgerCountAsync());
    }

    /// <summary>TC-MDF-012 — Source failure retries per the 5/15/60 ladder.</summary>
    [Fact]
    public async Task TC_MDF_012_Source_failure_retries_per_ladder()
    {
        await ResetAsync();
        _wireMock.Server.ResetMappings();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesSourceDown, path: PricesPath);

        await using var host = BuildIngestion();
        var clock = host.Clock;
        clock.SetUtcNow(Trt(2026, 10, 5, 20, 29));

        // Attempt 1 at 20:30 — the source is down (HTTP 500).
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 5, 20, 30));
        var afterFirst = await LedgerAsync();
        var first = Assert.Single(afterFirst);
        Assert.Equal("failed", first.Status);
        Assert.Equal(Trt(2026, 10, 5, 20, 30), first.StartedAt!.Value);
        Assert.Equal(1, Attempt(first));

        // Retry 1 at +5 min — still down.
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 5, 20, 35));
        var afterSecond = await LedgerAsync();
        Assert.Equal(2, afterSecond.Count);
        Assert.Equal("failed", afterSecond[1].Status);
        Assert.Equal(Trt(2026, 10, 5, 20, 35), afterSecond[1].StartedAt!.Value);
        Assert.Equal(2, Attempt(afterSecond[1]));

        // The source recovers before retry 2 (+15 min later).
        _wireMock.Server.ResetMappings();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesOkTminus1, path: PricesPath);

        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 5, 20, 50));
        var afterThird = await LedgerAsync();
        Assert.Equal(3, afterThird.Count);
        Assert.Equal("succeeded", afterThird[2].Status);
        Assert.Equal(Trt(2026, 10, 5, 20, 50), afterThird[2].StartedAt!.Value);
        Assert.Equal(3, Attempt(afterThird[2]));
        Assert.Equal(2, Retries(afterThird[2]));

        // The successful retry-2 attempt wrote the day's facts exactly once — the partial
        // attempts left no partial rows behind.
        await using (var db = _postgres.CreateContext())
        {
            Assert.Equal(16, await db.DailyPrices.CountAsync(p => p.PriceDate == new DateOnly(2026, 10, 5)));
            var duplicates = await db.DailyPrices
                .Where(p => p.PriceDate == new DateOnly(2026, 10, 5))
                .GroupBy(p => new { p.InstrumentId, p.PriceDate })
                .Where(g => g.Count() > 1)
                .CountAsync();
            Assert.Equal(0, duplicates);
        }

        // No further attempt fires on the +60 ladder boundary; the run already succeeded.
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 5, 21, 50));
        Assert.Equal(3, await LedgerCountAsync());
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Drives the same wake-then-tick loop the hosted service runs, but deterministically
    /// over the fake clock: it advances to each due instant up to and including
    /// <paramref name="target"/> and processes it, then settles on the target.
    /// </summary>
    private static async Task AdvanceToAsync(
        IngestionScheduler scheduler,
        FakeTimeProvider clock,
        DateTimeOffset target)
    {
        while (true)
        {
            var now = clock.GetUtcNow();
            var next = scheduler.NextWake(now);
            if (next is null || next.Value > target)
            {
                break;
            }

            var instant = next.Value.ToUniversalTime();
            clock.SetUtcNow(instant);
            await scheduler.TickAsync(instant);
        }

        clock.SetUtcNow(target.ToUniversalTime());
    }

    private IngestionSchedulerHost BuildIngestion(string? holiday = null)
    {
        var mail = new RecordingMailDispatcher();
        var logs = new InMemorySerilogSink();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _postgres.ConnectionString,
            ["Ingestion:Prices:BaseUrl"] = _wireMock.BaseUrl,
            ["Ingestion:Prices:Path"] = PricesPath,
            ["Ingestion:Scheduler:Cron"] = "0 30 20 * * *",
            ["Ingestion:Scheduler:TimeZone"] = "Europe/Istanbul",
            ["Ingestion:Scheduler:JobCodes:0"] = "prices",
        };
        if (holiday is not null)
        {
            settings["Ingestion:TradingCalendar:Holidays:0"] = holiday;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var logger = new LoggerConfiguration().MinimumLevel.Warning().WriteTo.Sink(logs).CreateLogger();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog(logger, dispose: false));
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IIngestionAlerter>(new HarnessMailAlerter(mail));
        services.AddMarketDataIngestion(configuration);

        var provider = services.BuildServiceProvider();
        var scheduler = provider.GetRequiredService<IngestionScheduler>();
        return new IngestionSchedulerHost(provider, clock, scheduler);
    }

    private async Task ResetAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM quarantined_facts");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM daily_prices");
    }

    private async Task<List<IngestRun>> LedgerAsync()
    {
        await using var db = _postgres.CreateContext();
        return await db.IngestRuns.OrderBy(r => r.StartedAt).ThenBy(r => r.Id).ToListAsync();
    }

    private async Task<int> LedgerCountAsync()
    {
        await using var db = _postgres.CreateContext();
        return await db.IngestRuns.CountAsync();
    }

    private static int Attempt(IngestRun run) => Stat(run, "attempt");

    private static int Retries(IngestRun run) => Stat(run, "retries");

    private static int Stat(IngestRun run, string name)
    {
        using var document = JsonDocument.Parse(run.StatsJson!);
        return document.RootElement.GetProperty(name).GetInt32();
    }

    /// <summary>An instant expressed in Turkey time (Europe/Istanbul = UTC+3, no DST
    /// since 2016) as a UTC <see cref="DateTimeOffset"/> for the fake clock.</summary>
    private static DateTimeOffset Trt(int year, int month, int day, int hour, int minute) =>
        new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.FromHours(3)).ToUniversalTime();

    /// <summary>The active engine provider plus its clock and the mail/log doubles.</summary>
    private sealed class IngestionSchedulerHost : IAsyncDisposable
    {
        public IngestionSchedulerHost(ServiceProvider provider, FakeTimeProvider clock, IngestionScheduler scheduler)
        {
            Provider = provider;
            Clock = clock;
            Scheduler = scheduler;
        }

        public ServiceProvider Provider { get; }

        public FakeTimeProvider Clock { get; }

        public IngestionScheduler Scheduler { get; }

        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    /// <summary>Funnels the ingestion alert seam into the shared recording mail double.</summary>
    private sealed class HarnessMailAlerter : IIngestionAlerter
    {
        private readonly RecordingMailDispatcher _mail;

        public HarnessMailAlerter(RecordingMailDispatcher mail) => _mail = mail;

        public Task RaiseAsync(IngestionAlert alert, CancellationToken cancellationToken = default) =>
            _mail.SendAsync(
                new OutboundMail("builder@degerli.test", alert.Subject, alert.BodyTr, alert.BodyEn),
                cancellationToken);
    }
}
