using Degerli.Api.IntegrationTests.Harness;
using Degerli.Api.IntegrationTests.Harness.Mail;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.Jobs;
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
/// TKT-int-001 acceptance — the full cross-domain EOD chain pinned on the TKT-mdf-005
/// scheduler: prices → statements/dividends/corporate-actions/disclosures →
/// metrics-recompute → snapshot → medians, run in order on the daily 20:30 Europe/Istanbul
/// trigger with one run-ledger row per job and every artifact produced for the run day
/// (TC-MDF-010; FR-MDF-009, UC-MDF-001 main, NFR-MDF-001, `03` §9).
/// <para>
/// The scheduler is driven through the real engine over a <see cref="FakeTimeProvider"/>,
/// composing the same <c>AddMarketDataIngestion</c> registration the API host uses and no
/// explicit <c>JobCodes</c> override — the chain order asserted here <b>is</b> the production
/// wiring, not test configuration. Every source is served by the FU §10 WireMock double.
/// </para>
/// </summary>
public sealed class EodChainTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);

    /// <summary>Monday 2026-10-05 TRT — the EOD run day (the fixture's T−1 trading day).</summary>
    private static readonly DateOnly RunDay = new(2026, 10, 5);

    // The canned payloads the chain's source-reading jobs consume, wired at their
    // FU §10 default routes (`/canned-sources/{name}`).
    private const string PricesPath = "/canned-sources/prices-ok-tminus1";
    private const string StatementsPath = "/canned-sources/statements-ok-alfa-fy2025";
    private const string DividendsPath = "/canned-sources/dividends-ok";
    private const string CorporateActionsPath = "/canned-sources/corporate-actions-ok";
    private const string DisclosuresPath = "/canned-sources/disclosures-ok";

    /// <summary>The pinned chain order TC-MDF-010 asserts.</summary>
    private static readonly string[] EodChain =
    [
        "prices",
        "statements",
        "dividends",
        "corporate-actions",
        "disclosures",
        "metrics-recompute",
        "market-snapshot",
        "medians",
    ];

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public EodChainTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    /// <summary>TC-MDF-010 — the EOD chain triggers on schedule and runs in order.</summary>
    [Fact]
    public async Task TC_MDF_010_Eod_chain_triggers_on_schedule_and_runs_in_order()
    {
        await ResetAsync();
        StubChainSources();

        await using var host = BuildScheduler();
        var clock = host.Clock;

        // Scheduled-idle just before the trigger; advancing past 20:30 starts the chain.
        clock.SetUtcNow(Trt(2026, 10, 5, 20, 29));
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 5, 20, 30));

        // One succeeded run-ledger row per job, in the pinned chain order.
        var runs = await LedgerAsync();
        Assert.Equal(EodChain, runs.Select(run => run.JobCode).ToArray());
        Assert.All(runs, run =>
        {
            Assert.Equal(IngestResult.Succeeded, run.Status);
            Assert.NotNull(run.StartedAt);
            Assert.NotNull(run.FinishedAt);
            Assert.True(run.FinishedAt >= run.StartedAt);
            // stats_json carries the per-job counts.
            Assert.False(string.IsNullOrWhiteSpace(run.StatsJson));
        });

        // Every artifact exists for the run day: prices, metrics, snapshot, medians.
        await using var db = _postgres.CreateContext();
        Assert.Equal(16, await db.DailyPrices.CountAsync(p => p.PriceDate == RunDay));
        Assert.True(await db.DerivedMetrics.AnyAsync(m => m.AsOfDate == RunDay));
        Assert.True(await db.MarketSnapshots.AnyAsync(s => s.SnapshotDate == RunDay));
        Assert.True(await db.SectorMetricMedians.AnyAsync(m => m.AsOfDate == RunDay));
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    /// <summary>Points every source-reading job of the chain at its WireMock canned payload.</summary>
    private void StubChainSources()
    {
        _wireMock.Server.ResetMappings();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesOkTminus1, path: PricesPath);
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.StatementsOkAlfaFy2025, path: StatementsPath);
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.DividendsOk, path: DividendsPath);
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.CorporateActionsOk, path: CorporateActionsPath);
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.DisclosuresOk, path: DisclosuresPath);
    }

    /// <summary>
    /// Drives the same wake-then-tick loop the hosted service runs, deterministically over
    /// the fake clock (test strategy §7).
    /// </summary>
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

        // No `Ingestion:Scheduler:JobCodes` override: the production default pins the chain.
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
        await db.Database.ExecuteSqlRawAsync("DELETE FROM derived_metrics");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM sector_metric_medians");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM market_snapshots");
    }

    private async Task<List<IngestRun>> LedgerAsync()
    {
        await using var db = _postgres.CreateContext();
        return await db.IngestRuns.OrderBy(r => r.StartedAt).ThenBy(r => r.Id).ToListAsync();
    }

    /// <summary>An instant in Turkey time (Europe/Istanbul = UTC+3, no DST) as a UTC instant.</summary>
    private static DateTimeOffset Trt(int year, int month, int day, int hour, int minute) =>
        new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.FromHours(3)).ToUniversalTime();

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
