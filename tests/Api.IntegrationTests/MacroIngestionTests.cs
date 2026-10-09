using System.Text.Json.Nodes;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Api.IntegrationTests.Harness.Mail;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Macro;
using Degerli.Ingestion.Scheduling;
using Degerli.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Serilog;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-mov-002 acceptance — the macro ingestion jobs on the TKT-mdf-002 adapter framework
/// and the TKT-mdf-005 scheduler (TC-MOV-006..011, TC-MDF-035). Every test runs the real
/// jobs against a migrated Testcontainers PostgreSQL and the FU §10 WireMock macro source
/// double, composing the same <c>AddMarketDataIngestion</c> registration the API host uses.
/// The macro revision path is asserted here for TC-MDF-035 (facts never overwritten,
/// canonical = latest recorded_at, both rows retained).
/// </summary>
public sealed class MacroIngestionTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static readonly DateOnly T = L2.Anchor.T;

    /// <summary>The route the macro adapter reads; tests override the stub here.</summary>
    private const string MacroPath = "/canned-sources/macro";

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public MacroIngestionTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    // ---------------------------------------------------------------------------------
    // TC-MOV-006 — daily macro series ingest
    // ---------------------------------------------------------------------------------

    /// <summary>TC-MOV-006 — the daily job stores dated, provenanced, unit-bearing values,
    /// one canonical value per series per date, idempotently.</summary>
    [Fact]
    public async Task TC_MOV_006_Daily_macro_series_ingest()
    {
        await ResetAsync(clearMacroValues: true);
        StubMacro(CannedSourceCatalog.MacroOk);
        await using var host = BuildIngestion();

        var first = await RunAsync(host, MacroDailyJob.Code);

        Assert.Equal("succeeded", first.Status);
        Assert.Equal(3, first.Written);
        Assert.Equal(0, first.Quarantined);

        await using (var db = _postgres.CreateContext())
        {
            var rows = await db.MacroValues
                .Where(v => v.SeriesCode == "USD_TRY" || v.SeriesCode == "EUR_TRY" || v.SeriesCode == "GOLD")
                .OrderBy(v => v.SeriesCode)
                .ToListAsync();

            Assert.Equal(3, rows.Count);

            // USD/TRY and EUR/TRY carry T; GOLD is the deliberately-stale fixture (FU §7:
            // last ingest T−10), so its value_date is T−10.
            var usd = rows.Single(r => r.SeriesCode == "USD_TRY");
            Assert.Equal(47.10m, usd.Value);
            Assert.Equal(T, usd.ValueDate);
            Assert.Equal("macro://series", usd.SourceRef);
            Assert.NotEqual(default, usd.RecordedAt);

            var eur = rows.Single(r => r.SeriesCode == "EUR_TRY");
            Assert.Equal(51.40m, eur.Value);
            Assert.Equal(T, eur.ValueDate);

            var gold = rows.Single(r => r.SeriesCode == "GOLD");
            Assert.Equal(5200.00m, gold.Value);
            Assert.Equal(T.AddDays(-10), gold.ValueDate);

            // Unit is a property of the series definition (02 §3.2); the fact joins to it.
            var units = await db.MacroSeries.ToDictionaryAsync(s => s.Code, s => s.Unit);
            Assert.Equal("TRY", units["USD_TRY"]);
            Assert.Equal("TRY", units["EUR_TRY"]);
            Assert.Equal("USD/oz", units["GOLD"]);

            // One canonical value per series per date.
            var duplicates = rows
                .GroupBy(r => new { r.SeriesCode, r.ValueDate, r.RecordedAt })
                .Count(g => g.Count() > 1);
            Assert.Equal(0, duplicates);
        }

        // Idempotent re-run: identical payload is a no-op.
        var second = await RunAsync(host, MacroDailyJob.Code);
        Assert.Equal(0, second.Written);
        Assert.Equal(3, second.Unchanged);

        await using (var db = _postgres.CreateContext())
        {
            Assert.Equal(3, await db.MacroValues.CountAsync(v =>
                v.SeriesCode == "USD_TRY" || v.SeriesCode == "EUR_TRY" || v.SeriesCode == "GOLD"));
        }
    }

    // ---------------------------------------------------------------------------------
    // TC-MOV-007 — CPI ingest with revision; canonical = latest
    // ---------------------------------------------------------------------------------

    /// <summary>TC-MOV-007 — a revised CPI value appends a second dated row; the canonical
    /// value is the latest recorded_at and neither row is overwritten.</summary>
    [Fact]
    public async Task TC_MOV_007_Cpi_ingest_with_revision_canonical_is_latest()
    {
        await ResetAsync(clearMacroValues: true);
        StubMacro(CannedSourceCatalog.MacroOk);
        await using var host = BuildIngestion();

        await RunAsync(host, MacroCpiJob.Code);

        var revisedDate = new DateOnly(2026, 8, 1);
        await using (var db = _postgres.CreateContext())
        {
            var rows = await db.MacroValues
                .Where(v => v.SeriesCode == "TUIK_CPI" && v.ValueDate == revisedDate)
                .OrderBy(v => v.RecordedAt)
                .ToListAsync();

            Assert.Equal(2, rows.Count);
            Assert.Equal(44.80m, rows[0].Value);
            Assert.Equal(new DateTimeOffset(2026, 9, 5, 10, 0, 0, TimeSpan.Zero), rows[0].RecordedAt);
            Assert.Equal(45.00m, rows[1].Value);
            Assert.Equal(new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero), rows[1].RecordedAt);
        }

        using var scope = host.Provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<MacroStore>();
        var canonical = await store.GetCanonicalAsync("TUIK_CPI", revisedDate);

        Assert.NotNull(canonical);
        Assert.Equal(45.00m, canonical!.Value);
    }

    // ---------------------------------------------------------------------------------
    // TC-MOV-008 — per-release series cadence
    // ---------------------------------------------------------------------------------

    /// <summary>TC-MOV-008 — the per-release policy rate is stored with its decision date;
    /// a new decision appends a new dated value and the previous one is retained.</summary>
    [Fact]
    public async Task TC_MOV_008_Per_release_series_cadence()
    {
        await ResetAsync(clearMacroValues: true);
        StubMacro(CannedSourceCatalog.MacroOk);
        await using var host = BuildIngestion();

        await RunAsync(host, MacroCpiJob.Code);

        var firstDecision = new DateOnly(2026, 9, 15);
        await using (var db = _postgres.CreateContext())
        {
            var first = await db.MacroValues.SingleAsync(v => v.SeriesCode == "CBRT_REPO" && v.ValueDate == firstDecision);
            Assert.Equal(42.50m, first.Value);
        }

        // A new CBRT decision (a new decision date) is published: append, never overwrite.
        var secondDecision = new DateOnly(2026, 10, 15);
        _wireMock.Server.ResetMappings();
        StubMacroBody(AppendRepoDecision(CannedSourceCatalog.MacroOk, secondDecision, 40.00m));

        await RunAsync(host, MacroCpiJob.Code);

        await using (var db = _postgres.CreateContext())
        {
            var repo = await db.MacroValues
                .Where(v => v.SeriesCode == "CBRT_REPO")
                .OrderBy(v => v.ValueDate)
                .ToListAsync();

            Assert.Equal(2, repo.Count);
            Assert.Equal(42.50m, repo[0].Value);
            Assert.Equal(firstDecision, repo[0].ValueDate);
            Assert.Equal(40.00m, repo[1].Value);
            Assert.Equal(secondDecision, repo[1].ValueDate);
        }
    }

    // ---------------------------------------------------------------------------------
    // TC-MOV-009 — source failure: last-known + stale + alert; recovery
    // ---------------------------------------------------------------------------------

    /// <summary>TC-MOV-009 — a failed series keeps serving its last-known value marked
    /// stale, alerts the builder, and clears stale on the next successful run.</summary>
    [Fact]
    public async Task TC_MOV_009_Macro_source_failure_last_known_stale_alert_and_recovery()
    {
        // Keep the seeded macro values (GOLD is stale by construction; USD_TRY is fresh).
        await ResetAsync(clearMacroValues: false);
        await using var host = BuildIngestion();

        using (var before = host.Provider.CreateScope())
        {
            var freshness = before.ServiceProvider.GetRequiredService<MacroFreshnessService>();
            var fresh = await freshness.GetStateAsync("USD_TRY");
            Assert.False(fresh.Stale);
            Assert.Equal(47.10m, fresh.Value);
        }

        // A fresh series' source fails (HTTP 500) for the daily job.
        _wireMock.Server.ResetMappings();
        _wireMock.StubJson(MacroPath, """{"error":"source unavailable"}""", statusCode: 500);

        var failed = await RunAsync(host, MacroDailyJob.Code);
        Assert.Equal("failed", failed.Status);

        // The last-known value is still served with its as-of date, now stale.
        using (var after = host.Provider.CreateScope())
        {
            var freshness = after.ServiceProvider.GetRequiredService<MacroFreshnessService>();
            var state = await freshness.GetStateAsync("USD_TRY");
            Assert.Equal(47.10m, state.Value);
            Assert.Equal(T, state.AsOf);
            Assert.True(state.Stale);
        }

        // The builder is alerted: a mail-double message and a log event name the series.
        Assert.Contains(host.Mail.Sent, m => m.Subject.Contains("USD_TRY", StringComparison.Ordinal));
        Assert.True(host.Logs.Contains(evt =>
            evt.RenderMessage().Contains("USD_TRY", StringComparison.Ordinal)));

        // The source recovers: the next successful run clears stale (stale → fresh).
        _wireMock.Server.ResetMappings();
        StubMacro(CannedSourceCatalog.MacroOk);

        var recovered = await RunAsync(host, MacroDailyJob.Code);
        Assert.Equal("succeeded", recovered.Status);

        using (var afterRecovery = host.Provider.CreateScope())
        {
            var freshness = afterRecovery.ServiceProvider.GetRequiredService<MacroFreshnessService>();
            var state = await freshness.GetStateAsync("USD_TRY");
            Assert.False(state.Stale);
            Assert.Equal("fresh", state.State);
        }
    }

    // ---------------------------------------------------------------------------------
    // TC-MOV-010 — macro freshness cadence targets
    // ---------------------------------------------------------------------------------

    /// <summary>TC-MOV-010 — with the fake clock the daily macro job completes by 09:00 on
    /// the day after the trading day, the CPI job runs within 24h of the canned release
    /// timestamp, and the run ledger records the timings.</summary>
    [Fact]
    public async Task TC_MOV_010_Macro_freshness_cadence_targets()
    {
        await ResetAsync(clearMacroValues: true);
        StubMacro(CannedSourceCatalog.MacroOk);

        var clock = new FakeTimeProvider(Trt(2026, 10, 2, 0, 0));
        await using var host = BuildScheduler(clock, dailyCron: "0 0 8 * * *", cpiCron: "0 30 9 * * *");

        // Sweep from the 02nd to the 07th; macro jobs fire daily on their own cadence.
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 7, 9, 30));

        var daily = await LedgerAsync(MacroDailyJob.Code);
        Assert.NotEmpty(daily);
        Assert.All(daily, run =>
        {
            Assert.Equal("succeeded", run.Status);
            Assert.NotNull(run.StartedAt);
            Assert.NotNull(run.FinishedAt);
        });

        // The daily job completed before 09:00 on the day after the fixture trading day T.
        var tradingDayRun = daily
            .Where(r => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(r.FinishedAt!.Value, Istanbul).DateTime) == new DateOnly(2026, 10, 7))
            .OrderByDescending(r => r.FinishedAt)
            .First();
        Assert.True(
            TimeZoneInfo.ConvertTime(tradingDayRun.FinishedAt!.Value, Istanbul) < Trt(2026, 10, 7, 9, 0),
            "Daily macro job must complete by 09:00 the day after the trading day.");

        // The CPI job ran within 24h of the fixture CPI release timestamp (2026-10-03 10:00 TRT).
        var release = new DateTimeOffset(2026, 10, 3, 10, 0, 0, TimeSpan.FromHours(3)).ToUniversalTime();
        var cpi = await LedgerAsync(MacroCpiJob.Code);
        Assert.Contains(cpi, run =>
            run.StartedAt >= release && run.StartedAt <= release.AddHours(24));
    }

    // ---------------------------------------------------------------------------------
    // TC-MOV-011 — macro alert content
    // ---------------------------------------------------------------------------------

    /// <summary>TC-MOV-011 — an unreachable source produces an alert naming the series, the
    /// failure class and the run.</summary>
    [Fact]
    public async Task TC_MOV_011_Unreachable_alert_content()
    {
        await ResetAsync(clearMacroValues: false);
        _wireMock.Server.ResetMappings();
        _wireMock.StubJson(MacroPath, """{"error":"source unavailable"}""", statusCode: 500);
        await using var host = BuildIngestion();

        await RunAsync(host, MacroCpiJob.Code);

        var alert = Assert.Single(host.Mail.Sent, m => m.Subject.Contains("TUIK_CPI", StringComparison.Ordinal));
        Assert.Contains("UNREACHABLE", alert.Subject);
        Assert.Contains(T.ToString("yyyy-MM-dd"), alert.Subject + alert.BodyTr + alert.BodyEn);
        Assert.True(host.Logs.Contains(evt =>
            evt.RenderMessage().Contains("TUIK_CPI", StringComparison.Ordinal)
            && evt.RenderMessage().Contains("UNREACHABLE", StringComparison.Ordinal)));
    }

    /// <summary>TC-MOV-011 — an invalid (unparseable) source body alerts with the invalid class.</summary>
    [Fact]
    public async Task TC_MOV_011_Invalid_alert_content()
    {
        await ResetAsync(clearMacroValues: false);
        _wireMock.Server.ResetMappings();
        _wireMock.StubJson(MacroPath, """{ this is not valid json""");
        await using var host = BuildIngestion();

        await RunAsync(host, MacroCpiJob.Code);

        var alert = Assert.Single(host.Mail.Sent, m => m.Subject.Contains("TUIK_CPI", StringComparison.Ordinal));
        Assert.Contains("INVALID", alert.Subject);
        Assert.True(host.Logs.Contains(evt =>
            evt.RenderMessage().Contains("TUIK_CPI", StringComparison.Ordinal)
            && evt.RenderMessage().Contains("INVALID", StringComparison.Ordinal)));
    }

    /// <summary>TC-MOV-011 — a series that is expected but absent (late) alerts with the late class.</summary>
    [Fact]
    public async Task TC_MOV_011_Late_alert_content()
    {
        await ResetAsync(clearMacroValues: false);
        StubMacro(CannedSourceCatalog.MacroIndepCpiAbsent);
        await using var host = BuildIngestion();

        await RunAsync(host, MacroCpiJob.Code);

        var alert = Assert.Single(host.Mail.Sent, m => m.Subject.Contains("INDEP_CPI", StringComparison.Ordinal));
        Assert.Contains("LATE", alert.Subject);
        Assert.True(host.Logs.Contains(evt =>
            evt.RenderMessage().Contains("INDEP_CPI", StringComparison.Ordinal)
            && evt.RenderMessage().Contains("LATE", StringComparison.Ordinal)));
    }

    // ---------------------------------------------------------------------------------
    // TC-MDF-035 — facts never overwritten; canonical = latest; as-of retrieval works
    // ---------------------------------------------------------------------------------

    /// <summary>TC-MDF-035 (asserted through the macro revision path) — both revision rows
    /// are retained, the canonical value is the latest recorded_at, and the earlier row is
    /// still retrievable as-of its own recorded_at.</summary>
    [Fact]
    public async Task TC_MDF_035_Facts_never_overwritten_as_of_retrieval()
    {
        await ResetAsync(clearMacroValues: true);
        StubMacro(CannedSourceCatalog.MacroOk);
        await using var host = BuildIngestion();

        await RunAsync(host, MacroCpiJob.Code);

        var revisedDate = new DateOnly(2026, 8, 1);
        using var scope = host.Provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<MacroStore>();

        var canonical = await store.GetCanonicalAsync("TUIK_CPI", revisedDate);
        Assert.NotNull(canonical);
        Assert.Equal(45.00m, canonical!.Value);
        Assert.Equal(new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero), canonical.RecordedAt);

        var history = await store.GetHistoryAsync("TUIK_CPI", revisedDate);
        Assert.Equal(2, history.Count);
        var earlier = Assert.Single(history, v => v.RecordedAt == new DateTimeOffset(2026, 9, 5, 10, 0, 0, TimeSpan.Zero));
        Assert.Equal(44.80m, earlier.Value);
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    private IngestionHost BuildIngestion()
    {
        var mail = new RecordingMailDispatcher();
        var logs = new InMemorySerilogSink();
        var clock = new FakeTimeProvider(new DateTimeOffset(T.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _postgres.ConnectionString,
                ["Ingestion:Macro:BaseUrl"] = _wireMock.BaseUrl,
                ["Ingestion:Macro:Path"] = MacroPath,
            })
            .Build();

        var provider = Compose(configuration, clock, mail, logs);
        return new IngestionHost(provider, mail, logs, clock, provider.GetRequiredService<IngestionScheduler>());
    }

    private SchedulerHost BuildScheduler(FakeTimeProvider clock, string dailyCron, string cpiCron)
    {
        var mail = new RecordingMailDispatcher();
        var logs = new InMemorySerilogSink();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _postgres.ConnectionString,
                ["Ingestion:Macro:BaseUrl"] = _wireMock.BaseUrl,
                ["Ingestion:Macro:Path"] = MacroPath,
                // The EOD chain is out of this ticket's scope: park its trigger outside
                // the test window so only the per-job macro schedules fire.
                ["Ingestion:Scheduler:Cron"] = "0 0 0 1 1 *",
                ["Ingestion:Scheduler:TimeZone"] = "Europe/Istanbul",
                ["Ingestion:Scheduler:JobSchedules:0:JobCode"] = MacroDailyJob.Code,
                ["Ingestion:Scheduler:JobSchedules:0:Cron"] = dailyCron,
                ["Ingestion:Scheduler:JobSchedules:1:JobCode"] = MacroCpiJob.Code,
                ["Ingestion:Scheduler:JobSchedules:1:Cron"] = cpiCron,
            })
            .Build();

        var provider = Compose(configuration, clock, mail, logs);
        return new SchedulerHost(provider, clock, provider.GetRequiredService<IngestionScheduler>());
    }

    private static ServiceProvider Compose(
        IConfiguration configuration,
        FakeTimeProvider clock,
        RecordingMailDispatcher mail,
        InMemorySerilogSink logs)
    {
        var logger = new LoggerConfiguration().MinimumLevel.Warning().WriteTo.Sink(logs).CreateLogger();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog(logger, dispose: false));
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IIngestionAlerter>(new HarnessMailAlerter(mail));
        services.AddMarketDataIngestion(configuration);

        return services.BuildServiceProvider();
    }

    private static async Task<IngestResult> RunAsync(IngestionHost host, string jobCode)
    {
        using var scope = host.Provider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IIngestionJobRunner>();
        return await runner.RunAsync(jobCode, new IngestionRequest(T));
    }

    private void StubMacro(string name)
    {
        _wireMock.Server.ResetMappings();
        _wireMock.StubCannedSource(L2, name, path: MacroPath);
    }

    private void StubMacroBody(string body)
    {
        _wireMock.Server.ResetMappings();
        _wireMock.StubJson(MacroPath, body);
    }

    /// <summary>Appends one new CBRT repo decision to the macro-ok payload.</summary>
    private static string AppendRepoDecision(string cannedName, DateOnly valueDate, decimal value)
    {
        var body = CannedSourceCatalog.Get(L2, cannedName).Body;
        var root = JsonNode.Parse(body)!;
        var series = root["series"]!.AsArray();
        series.Add(new JsonObject
        {
            ["code"] = "CBRT_REPO",
            ["valueDate"] = valueDate.ToString("yyyy-MM-dd"),
            ["value"] = value,
            ["unit"] = "%",
            ["recordedAt"] = new DateTimeOffset(valueDate.ToDateTime(new TimeOnly(14, 0)), TimeSpan.Zero).ToString("o"),
        });

        return root.ToJsonString();
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

    /// <summary>Applies the fixture universe, then clears the mutable ingestion state the
    /// test creates itself; optionally clears the seeded macro values.</summary>
    private async Task ResetAsync(bool clearMacroValues)
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM quarantined_facts");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
        if (clearMacroValues)
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM macro_values");
        }
    }

    private async Task<List<Degerli.Persistence.Entities.IngestRun>> LedgerAsync(string jobCode)
    {
        await using var db = _postgres.CreateContext();
        return await db.IngestRuns
            .Where(r => r.JobCode == jobCode)
            .OrderBy(r => r.StartedAt)
            .ThenBy(r => r.Id)
            .ToListAsync();
    }

    private static DateTimeOffset Trt(int year, int month, int day, int hour, int minute) =>
        new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.FromHours(3)).ToUniversalTime();

    private class IngestionHost : IAsyncDisposable
    {
        public IngestionHost(
            ServiceProvider provider,
            RecordingMailDispatcher mail,
            InMemorySerilogSink logs,
            FakeTimeProvider clock,
            IngestionScheduler scheduler)
        {
            Provider = provider;
            Mail = mail;
            Logs = logs;
            Clock = clock;
            Scheduler = scheduler;
        }

        public ServiceProvider Provider { get; }

        public RecordingMailDispatcher Mail { get; }

        public InMemorySerilogSink Logs { get; }

        public FakeTimeProvider Clock { get; }

        public IngestionScheduler Scheduler { get; }

        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    private sealed class SchedulerHost : IAsyncDisposable
    {
        public SchedulerHost(ServiceProvider provider, FakeTimeProvider clock, IngestionScheduler scheduler)
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
