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
    private const string UniversePath = "/canned-sources/universe";
    private const string IndexLevelsPath = "/canned-sources/index-levels";

    /// <summary>A dedicated index-levels route carrying the run day's own closes (TC-MDF-080).</summary>
    private const string RunDayLevelsPath = "/canned-sources/index-levels-eod";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The pinned chain order TC-MDF-010 asserts. Since TKT-foundation-041 (01 v1.5 §10.9.17)
    /// the chain <b>leads with <c>universe-sync</c></b>: index-level refresh has no schedule of
    /// its own, so a nightly chain that did not run it left the day's index levels to a manual
    /// admin trigger (TC-MDF-080).
    /// </summary>
    private static readonly string[] EodChain =
    [
        "universe-sync",
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

        // One run-ledger row per job, in the pinned chain order.
        var runs = await LedgerAsync();
        Assert.Equal(EodChain, runs.Select(run => run.JobCode).ToArray());
        Assert.All(runs, run =>
        {
            // The FU world carries one instrument with no sector classification (UNSEC), so
            // universe-sync reports the documented MISSING_CLASSIFICATION quarantine and lands
            // Partial — a non-fatal status that never stops the chain (fail-stop is Failed only).
            Assert.Equal(
                run.JobCode == "universe-sync" ? IngestResult.Partial : IngestResult.Succeeded,
                run.Status);
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

    /// <summary>
    /// TC-MDF-080 — <c>universe-sync</c> is the EOD chain's first job, so the day's index levels
    /// exist without a manual admin trigger: the levels the snapshot shows are written <i>by this
    /// run</i>, and a re-run over the same world is an idempotent no-op (one more ledger row, no
    /// duplicate level rows, membership unchanged).
    /// </summary>
    [Fact]
    public async Task TC_MDF_080_Universe_sync_leads_the_eod_chain_and_the_snapshot_has_real_levels()
    {
        await ResetAsync();
        StubChainSources();

        // The day's own closes, served on a dedicated route (the live gap's Tuesday numbers).
        _wireMock.StubJson(RunDayLevelsPath, JsonSerializer.Serialize(new
        {
            sourceRef = "isyatirim",
            levels = new[]
            {
                new { indexCode = "XU100", date = RunDay, close = 11797.70m },
                new { indexCode = "XU30", date = RunDay, close = 14676.53m },
            },
        }, JsonOptions));

        // The world the live gap produced: no index levels stored at all, so any level the
        // snapshot shows can only have come from this chain's own universe-sync step.
        await using (var reset = _postgres.CreateContext())
        {
            await reset.Database.ExecuteSqlRawAsync("DELETE FROM index_levels");
        }

        await using var host = BuildScheduler(indexLevelsPath: RunDayLevelsPath);
        var clock = host.Clock;
        clock.SetUtcNow(Trt(2026, 10, 5, 20, 29));
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 5, 20, 30));

        var runs = await LedgerAsync();

        // (1) universe-sync leads the chain: one ledger row, before prices.
        Assert.Equal(EodChain, runs.Select(run => run.JobCode).ToArray());
        Assert.Equal("universe-sync", runs[0].JobCode);
        Assert.True(runs[0].FinishedAt <= runs[1].StartedAt);

        // (2) The day's index levels came from the chain's own first step (FU §4: XU100 10200).
        int levelCount;
        int membershipCount;
        await using (var db = _postgres.CreateContext())
        {
            var indexIds = await db.Indices.ToDictionaryAsync(i => i.Code, i => i.Id);
            var level = await db.IndexLevels.SingleAsync(l => l.IndexId == indexIds["XU100"] && l.LevelDate == RunDay);
            Assert.Equal(11797.70m, level.Close);

            // (3) …and the snapshot serves them: no dependency on a manual universe-sync run.
            var snapshot = await db.MarketSnapshots.SingleAsync(s => s.SnapshotDate == RunDay);
            Assert.Equal(11797.70m, snapshot.Xu100Level);
            Assert.Equal(14676.53m, snapshot.Xu30Level);

            levelCount = await db.IndexLevels.CountAsync();
            membershipCount = await db.IndexConstituents.CountAsync();
        }

        // (4) Idempotent: the next night's universe-sync step is a no-op over the same world.
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 6, 20, 30));

        await using var after = _postgres.CreateContext();
        var universeRuns = await after.IngestRuns
            .Where(r => r.JobCode == "universe-sync")
            .OrderBy(r => r.Id)
            .ToListAsync();
        Assert.Equal(2, universeRuns.Count);
        Assert.Equal(IngestResult.Partial, universeRuns[1].Status);
        Assert.Equal(levelCount, await after.IndexLevels.CountAsync());
        Assert.Equal(membershipCount, await after.IndexConstituents.CountAsync());
    }

    // ---------------------------------------------------------------------------------
    // TKT-foundation-048 — the holiday gate (planner pin 3: never a silent no-run)
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// TC-MDF-087 (iii) — <b>a configured holiday gates the EOD chain, and the gate is never
    /// silent.</b> Monday 2026-10-05 — the harness's ordinary run day — is configured as a market
    /// holiday, so the 20:30 occurrence falls on a non-trading date (<c>weekend ∪ configured
    /// holiday</c>, planner pin 2). The chain therefore runs <b>no</b> job: zero source requests,
    /// no job row, no alert, and above all no <c>RUN_HUNG</c> — a holiday skip is intent, not a
    /// hang, so it must not trip 064's watchdog or leave a step timer armed.
    /// <para>
    /// What it does leave is <b>one short run-ledger row</b> naming the skip
    /// (<c>holiday-skip</c>) and dating it (the holiday it skipped, the stream it gated), so
    /// 041's watchdog and an operator see a decision where the pre-048 engine had silence
    /// (planner pin 3).
    /// </para>
    /// </summary>
    [Fact]
    public async Task TC_MDF_087_configured_holiday_skips_the_eod_chain_with_one_named_dated_row()
    {
        await ResetAsync();

        // The run day's prices are part of the fixture world; clear them so "nothing was
        // written" is a real assertion about this chain, not about the seed.
        await using (var reset = _postgres.CreateContext())
        {
            await reset.Database.ExecuteSqlRawAsync(
                "DELETE FROM daily_prices WHERE price_date = {0}",
                RunDay);
        }

        StubChainSources();
        var requestsBefore = _wireMock.Server.LogEntries.Count;

        await using var host = BuildScheduler(holiday: RunDay.ToString("yyyy-MM-dd"));
        var clock = host.Clock;

        // The same instant as TC-MDF-010: this is the trigger the holiday suppresses.
        clock.SetUtcNow(Trt(2026, 10, 5, 20, 29));
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 5, 20, 30));

        // Exactly one row — the skip itself. No chain job ran and no RUN_HUNG was declared.
        var runs = await LedgerAsync();
        var skip = Assert.Single(runs);
        Assert.Equal(IngestionScheduler.HolidaySkipJobCode, skip.JobCode);
        Assert.Equal(IngestResult.Succeeded, skip.Status);
        Assert.Null(skip.Error);
        Assert.Equal(Trt(2026, 10, 5, 20, 30), skip.StartedAt!.Value);
        Assert.Equal(skip.StartedAt, skip.FinishedAt);

        using var stats = JsonDocument.Parse(skip.StatsJson!);
        var root = stats.RootElement;
        Assert.Equal("trading-calendar", root.GetProperty("gate").GetString());
        Assert.Equal("skipped", root.GetProperty("gateOutcome").GetString());
        Assert.Equal(RunDay.ToString("yyyy-MM-dd"), root.GetProperty("holiday").GetString());
        Assert.Equal("eod-chain", root.GetProperty("stream").GetString());

        // Nothing was fetched: the gate is before the first source call.
        Assert.Equal(requestsBefore, _wireMock.Server.LogEntries.Count);

        // Nothing was produced for the day.
        await using var db = _postgres.CreateContext();
        Assert.Equal(0, await db.DailyPrices.CountAsync(p => p.PriceDate == RunDay));
        Assert.False(await db.MarketSnapshots.AnyAsync(s => s.SnapshotDate == RunDay));
        Assert.False(await db.DerivedMetrics.AnyAsync(m => m.AsOfDate == RunDay));

        // A skip is not a failure: no alert leaves the platform.
        Assert.Empty(host.Mail.Sent);
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
        // The chain's leading step (01 v1.5 §10.9.17): the universe snapshot and the day's
        // index levels, both served by the İş Yatırım source double.
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.UniverseMissingSector, path: UniversePath);
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.IndexLevelsOk, path: IndexLevelsPath);
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

    private IngestionSchedulerHost BuildScheduler(string indexLevelsPath = IndexLevelsPath, string? holiday = null)
        => BuildSchedulerCore(indexLevelsPath, holiday);

    private IngestionSchedulerHost BuildSchedulerCore(string indexLevelsPath, string? holiday = null)
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
            // The L2 harness binds the loop to the seeded FU XU100 (§8 preamble) and disables
            // pacing, so the EOD chain's 16-row price golden survives the per-ticker loop.
            ["Ingestion:Prices:UniverseIndex"] = "XU100",
            ["Ingestion:Prices:RequestDelayMs"] = "0",
            // The chain's leading universe-sync step (01 v1.5 §10.9.17) reads the İş Yatırım
            // universe + index-levels resources; both are the FU §10 source double here.
            ["Ingestion:Universe:Path"] = UniversePath,
            ["Ingestion:Universe:BaseUrl"] = _wireMock.BaseUrl,
            ["Ingestion:Universe:IndexLevelsPath"] = indexLevelsPath,
            ["Ingestion:Universe:IndexLevelsIndices:0"] = "XU100",
            ["Ingestion:Kap:BaseUrl"] = _wireMock.BaseUrl,
            ["Ingestion:Statements:Path"] = StatementsPath,
            // The company-scoped statements loop resolves its fetch bound from this index; the
            // test-local single-member bind keeps the ALFA statement golden intact
            // (TKT-foundation-024).
            ["Ingestion:Statements:UniverseIndex"] = StatementBound.IndexCode,
            ["Ingestion:Statements:RequestDelayMs"] = "0",
            ["Ingestion:Dividends:Path"] = DividendsPath,
            ["Ingestion:CorporateActions:Path"] = CorporateActionsPath,
            ["Ingestion:Disclosures:Path"] = DisclosuresPath,
            ["Ingestion:Scheduler:Cron"] = "0 30 20 * * *",
            ["Ingestion:Scheduler:TimeZone"] = "Europe/Istanbul",
        };

        // The 048 calendar is configuration (planner pin 1): a case can configure a holiday to
        // put the stream's own occurrence on a non-trading day.
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
        return new IngestionSchedulerHost(provider, clock, scheduler, mail);
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
        await StatementBound.ReplaceAsync(db, "ALFA");
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
        public IngestionSchedulerHost(
            ServiceProvider provider,
            FakeTimeProvider clock,
            IngestionScheduler scheduler,
            RecordingMailDispatcher mail)
        {
            Provider = provider;
            Clock = clock;
            Scheduler = scheduler;
            Mail = mail;
        }

        public ServiceProvider Provider { get; }

        public FakeTimeProvider Clock { get; }

        public IngestionScheduler Scheduler { get; }

        /// <summary>Everything the run raised through the alert seam (a skip raises nothing).</summary>
        public RecordingMailDispatcher Mail { get; }

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
