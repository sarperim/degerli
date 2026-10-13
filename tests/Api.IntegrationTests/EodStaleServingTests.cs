using System.Net;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Api.IntegrationTests.Harness.Mail;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Scheduling;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Serilog;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-int-001 acceptance — the exhausted-retries → last-known-good → stale-served →
/// recovery cycle observed through the public <c>GET /api/v1/market/overview</c> endpoint
/// (TC-MDF-013, TC-MDF-014; FR-MDF-016, UC-MDF-001 alternate a, NFR-MDF-001).
/// <para>
/// The state is established by the real EOD chain: a Monday 2026-10-05 cycle succeeds
/// (last-known-good, a 2026-10-05 snapshot), then the Tuesday 2026-10-06 cycle fails
/// because the price source is down and the 5/15/60 retry ladder is exhausted. The stale
/// serving is then read over HTTP from the real endpoint, not the DB directly.
/// </para>
/// </summary>
public sealed class EodStaleServingTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);

    private const string PricesPath = "/canned-sources/prices-ok-tminus1";
    private const string StatementsPath = "/canned-sources/statements-ok-alfa-fy2025";
    private const string DividendsPath = "/canned-sources/dividends-ok";
    private const string CorporateActionsPath = "/canned-sources/corporate-actions-ok";
    private const string DisclosuresPath = "/canned-sources/disclosures-ok";

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public EodStaleServingTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    /// <summary>TC-MDF-013 — exhausted retries alert, keep last-known-good, mark stale.</summary>
    [Fact]
    public async Task TC_MDF_013_Exhausted_retries_alert_and_serve_last_known_good_as_stale()
    {
        await ResetAsync();
        StubChainSources();
        await using var host = BuildScheduler();
        await EstablishStaleStateAsync(host);

        // The failed run is recorded with every retry attempt, all failing.
        var failed = (await LedgerAsync()).Where(run => run.Status == IngestResult.Failed).ToList();
        Assert.Equal(4, failed.Count);
        Assert.All(failed, run => Assert.Equal("prices", run.JobCode));
        Assert.Equal(new[] { 1, 2, 3, 4 }, failed.Select(Attempt).ToArray());

        // The builder is alerted by e-mail and by a structured Serilog alert event.
        var alert = Assert.Single(host.Mail.Sent);
        Assert.Contains("prices", alert.Subject);
        Assert.Contains("failed after 4 attempts", alert.Subject);
        Assert.True(host.Logs.Contains(evt =>
            evt.RenderMessage().Contains("RETRIES_EXHAUSTED", StringComparison.Ordinal) &&
            evt.RenderMessage().Contains("prices", StringComparison.Ordinal)));

        // The freshness view still carries the last-good prices success, with the failure recorded.
        await using (var db = _postgres.CreateContext())
        {
            var (success, failure) = await PricesFreshnessAsync(db);
            Assert.Equal("2026-10-05", success);
            Assert.Equal("2026-10-06", failure);
        }

        // The public endpoint serves T−1 (2026-10-05) last-known-good, marked stale.
        using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        factory.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 6, 18, 0, 0, TimeSpan.Zero));
        using var client = CreateClient(factory);
        using var response = await client.GetAsync("/api/v1/market/overview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("stale").GetBoolean());
        Assert.Equal("2026-10-05", document.RootElement.GetProperty("asOf").GetString());
    }

    /// <summary>TC-MDF-014 — recovery clears staleness.</summary>
    [Fact]
    public async Task TC_MDF_014_Recovery_clears_staleness()
    {
        await ResetAsync();
        StubChainSources();
        await using var host = BuildScheduler();
        await EstablishStaleStateAsync(host);

        // Restore the source stub and run the next scheduled cycle (Wednesday 2026-10-07).
        RestoreChainSources();
        await AdvanceToAsync(host.Scheduler, host.Clock, Trt(2026, 10, 7, 20, 30));

        await using (var db = _postgres.CreateContext())
        {
            // The recovered chain wrote the day's snapshot and refreshed the prices success.
            Assert.True(await db.MarketSnapshots.AnyAsync(s => s.SnapshotDate == new DateOnly(2026, 10, 7)));
            var (success, _) = await PricesFreshnessAsync(db);
            Assert.Equal("2026-10-07", success);
        }

        // The public endpoint now serves fresh T data.
        using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        factory.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 7, 18, 0, 0, TimeSpan.Zero));
        using var client = CreateClient(factory);
        using var response = await client.GetAsync("/api/v1/market/overview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(document.RootElement.GetProperty("stale").GetBoolean());
        Assert.Equal("2026-10-07", document.RootElement.GetProperty("asOf").GetString());
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Re-creates TC-MDF-013's state: a successful Monday 2026-10-05 chain (last-known-good),
    /// then a Tuesday 2026-10-06 chain whose price source is down until the retry ladder is
    /// exhausted (20:30, +5, +15, +60 minutes).
    /// </summary>
    private static async Task EstablishStaleStateAsync(IngestionSchedulerHost host)
    {
        // Monday: a clean cycle establishes the last-known-good snapshot.
        host.Clock.SetUtcNow(Trt(2026, 10, 5, 20, 29));
        await AdvanceToAsync(host.Scheduler, host.Clock, Trt(2026, 10, 5, 20, 30));

        // Tuesday: the price source goes down; the chain fails and exhausts its retries.
        host.SourceDown();
        await AdvanceToAsync(host.Scheduler, host.Clock, Trt(2026, 10, 6, 20, 30)); // attempt 1
        await AdvanceToAsync(host.Scheduler, host.Clock, Trt(2026, 10, 6, 20, 35)); // retry 1 (+5m)
        await AdvanceToAsync(host.Scheduler, host.Clock, Trt(2026, 10, 6, 20, 50)); // retry 2 (+15m)
        await AdvanceToAsync(host.Scheduler, host.Clock, Trt(2026, 10, 6, 21, 50)); // retry 3 (+60m) → exhausted
    }

    private void StubChainSources()
    {
        _wireMock.Server.ResetMappings();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesOkTminus1, path: PricesPath);
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.StatementsOkAlfaFy2025, path: StatementsPath);
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.DividendsOk, path: DividendsPath);
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.CorporateActionsOk, path: CorporateActionsPath);
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.DisclosuresOk, path: DisclosuresPath);
    }

    private void RestoreChainSources() => StubChainSources();

    private void StubDownSource()
    {
        _wireMock.Server.ResetMappings();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesSourceDown, path: PricesPath);
    }

    private static async Task AdvanceToAsync(IngestionScheduler scheduler, FakeTimeProvider clock, DateTimeOffset target)
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

    private IngestionSchedulerHost BuildScheduler()
    {
        var mail = new RecordingMailDispatcher();
        var logs = new InMemorySerilogSink();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _postgres.ConnectionString,
            ["Ingestion:Prices:BaseUrl"] = _wireMock.BaseUrl,
            ["Ingestion:Prices:Path"] = PricesPath,
            ["Ingestion:Kap:BaseUrl"] = _wireMock.BaseUrl,
            ["Ingestion:Statements:Path"] = StatementsPath,
            ["Ingestion:Dividends:Path"] = DividendsPath,
            ["Ingestion:CorporateActions:Path"] = CorporateActionsPath,
            ["Ingestion:Disclosures:Path"] = DisclosuresPath,
            ["Ingestion:Scheduler:Cron"] = "0 30 20 * * *",
            ["Ingestion:Scheduler:TimeZone"] = "Europe/Istanbul",
        };

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var logger = new LoggerConfiguration().MinimumLevel.Warning().WriteTo.Sink(logs).CreateLogger();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog(logger, dispose: false));
        services.AddSingleton<TimeProvider>(clock);
        // The production log alert plus the mail seam (the builder e-mail is dispatched
        // through the recording mail double), so one raised alert is observable in both.
        services.AddSingleton<LoggingIngestionAlerter>();
        services.AddSingleton<IIngestionAlerter>(provider => new AlertingMailAlerter(
            provider.GetRequiredService<LoggingIngestionAlerter>(),
            mail));
        services.AddMarketDataIngestion(configuration);

        var provider = services.BuildServiceProvider();
        var scheduler = provider.GetRequiredService<IngestionScheduler>();
        return new IngestionSchedulerHost(provider, clock, scheduler, mail, logs, StubDownSource);
    }

    private async Task ResetAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM quarantined_facts");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM derived_metrics");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM sector_metric_medians");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM market_snapshots");
    }

    private async Task<List<IngestRun>> LedgerAsync()
    {
        await using var db = _postgres.CreateContext();
        return await db.IngestRuns.OrderBy(r => r.StartedAt).ThenBy(r => r.Id).ToListAsync();
    }

    /// <summary>Reads the <c>v_data_freshness</c> last success/failure dates for <c>prices</c>
    /// (UTC calendar dates) — the freshness contract behind stale marking (FR-MDF-016).</summary>
    private static async Task<(string? Success, string? Failure)> PricesFreshnessAsync(DegerliDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT to_char(last_success_at AT TIME ZONE 'UTC', 'YYYY-MM-DD'), " +
            "to_char(last_failure_at AT TIME ZONE 'UTC', 'YYYY-MM-DD') " +
            "FROM v_data_freshness WHERE job_code = 'prices'";

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var success = reader.IsDBNull(0) ? null : reader.GetString(0);
        var failure = reader.IsDBNull(1) ? null : reader.GetString(1);
        return (success, failure);
    }

    private static int Attempt(IngestRun run)
    {
        using var document = JsonDocument.Parse(run.StatsJson!);
        return document.RootElement.GetProperty("attempt").GetInt32();
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    private static DateTimeOffset Trt(int year, int month, int day, int hour, int minute) =>
        new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.FromHours(3)).ToUniversalTime();

    /// <summary>Drives the real scheduler engine, the audit doubles and the source switch.</summary>
    private sealed class IngestionSchedulerHost : IAsyncDisposable
    {
        private readonly Action _sourceDown;

        public IngestionSchedulerHost(
            ServiceProvider provider,
            FakeTimeProvider clock,
            IngestionScheduler scheduler,
            RecordingMailDispatcher mail,
            InMemorySerilogSink logs,
            Action sourceDown)
        {
            Provider = provider;
            Clock = clock;
            Scheduler = scheduler;
            Mail = mail;
            Logs = logs;
            _sourceDown = sourceDown;
        }

        public ServiceProvider Provider { get; }

        public FakeTimeProvider Clock { get; }

        public IngestionScheduler Scheduler { get; }

        public RecordingMailDispatcher Mail { get; }

        public InMemorySerilogSink Logs { get; }

        public void SourceDown() => _sourceDown();

        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    /// <summary>
    /// The alerting seam wired as production does — the structured log alert — plus the
    /// builder e-mail through the mail double (C3d, FR-MDF-012).
    /// </summary>
    private sealed class AlertingMailAlerter : IIngestionAlerter
    {
        private readonly LoggingIngestionAlerter _logger;
        private readonly RecordingMailDispatcher _mail;

        public AlertingMailAlerter(LoggingIngestionAlerter logger, RecordingMailDispatcher mail)
        {
            _logger = logger;
            _mail = mail;
        }

        public async Task RaiseAsync(IngestionAlert alert, CancellationToken cancellationToken = default)
        {
            await _logger.RaiseAsync(alert, cancellationToken);
            await _mail.SendAsync(
                new OutboundMail("builder@degerli.test", alert.Subject, alert.BodyTr, alert.BodyEn),
                cancellationToken);
        }
    }
}
