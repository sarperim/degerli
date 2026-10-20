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
using WireMock.Logging;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-foundation-040 acceptance — the <b>morning chain</b> (01 v1.5 §10.9.16): a second chain
/// stream on the TKT-mdf-005 scheduler, 08:30 Europe/Istanbul on trading days, running the
/// pinned <see cref="MorningIngestChain"/> for the <b>previous trading day</b> with the
/// stored-prices repair gate.
/// <para>
/// TC-MDF-077 — scheduling: the morning stream fires at 08:30 on a trading day for the
/// previous trading day, skips a non-trading occurrence silently, and retries/exhausts on the
/// unchanged 5/15/60 ladder with the <c>RETRIES_EXHAUSTED</c> alert.
/// </para>
/// <para>
/// TC-MDF-078 — the repair gate: (a) a fully covered day is never re-fetched (zero İş Yatırım
/// calls, zero quarantine noise) while statements/metrics/medians run and the snapshot no-ops;
/// (b) a failed night is repaired by the next 08:30 pass, which writes the day's overview row
/// end-to-end and serves it from <c>GET /api/v1/market/overview</c>.
/// </para>
/// <para>
/// The scheduler is driven through the real engine over a <see cref="FakeTimeProvider"/> with
/// the same <c>AddMarketDataIngestion</c> registration the API host uses; every source is the
/// FU §10 WireMock double (L2 doubles only — never a live source).
/// </para>
/// </summary>
public sealed class MorningChainTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);

    /// <summary>The morning pass's target: the previous trading day (Monday 2026-10-05).</summary>
    private static readonly DateOnly PreviousTradingDay = new(2026, 10, 5);

    /// <summary>The 08:30 occurrence day (Tuesday 2026-10-06) — its session does not exist yet.</summary>
    private static readonly DateOnly MorningOccurrenceDay = new(2026, 10, 6);

    private const string PricesPath = "/canned-sources/prices-ok-tminus1";
    private const string StatementsPath = "/canned-sources/statements-ok-alfa-fy2025";
    private const string DividendsPath = "/canned-sources/dividends-ok";
    private const string CorporateActionsPath = "/canned-sources/corporate-actions-ok";
    private const string DisclosuresPath = "/canned-sources/disclosures-ok";

    /// <summary>The morning composition as shipped (phase 1 — no event families yet).</summary>
    private static readonly string[] MorningChain =
    [
        "prices",
        "statements",
        "metrics-recompute",
        "market-snapshot",
        "medians",
    ];

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public MorningChainTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    // ---------------------------------------------------------------------------------
    // TC-MDF-077 — scheduling
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// TC-MDF-077 (i) — advancing the fake clock to 08:30 Istanbul on a trading day starts the
    /// morning chain, and its <see cref="IngestionRequest.TradingDate"/> is the previous trading
    /// day: the run's artifacts land on 2026-10-05, never on the 08:30 occurrence's own date
    /// (BIST prices are EOD — a Tuesday morning can only repair Monday's session).
    /// </summary>
    [Fact]
    public async Task TC_MDF_077_morning_chain_runs_for_the_previous_trading_day()
    {
        await ResetAsync();
        await DeleteStoredPricesAsync(PreviousTradingDay);
        StubChainSources();

        await using var host = BuildScheduler();
        var clock = host.Clock;

        clock.SetUtcNow(Trt(2026, 10, 6, 8, 29));
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 6, 8, 30));

        var runs = await LedgerAsync();
        Assert.Equal(MorningChain, runs.Select(run => run.JobCode).ToArray());
        Assert.All(runs, run => Assert.Equal(IngestResult.Succeeded, run.Status));

        // The prices step ran for real (the day was uncovered) and wrote the target day.
        var pricesRun = runs[0];
        Assert.Equal(16, Stat(pricesRun, "written"));
        Assert.False(pricesRun.StatsJson!.Contains("gateOutcome", StringComparison.Ordinal));

        // Every artifact is dated the previous trading day — nothing claims the 08:30 day.
        await using var db = _postgres.CreateContext();
        Assert.Equal(16, await db.DailyPrices.CountAsync(p => p.PriceDate == PreviousTradingDay));
        Assert.True(await db.DerivedMetrics.AnyAsync(m => m.AsOfDate == PreviousTradingDay));
        Assert.False(await db.DerivedMetrics.AnyAsync(m => m.AsOfDate == MorningOccurrenceDay));
        var snapshot = await db.MarketSnapshots.SingleAsync();
        Assert.Equal(PreviousTradingDay, snapshot.SnapshotDate);
        Assert.True(await db.SectorMetricMedians.AnyAsync(m => m.AsOfDate == PreviousTradingDay));
    }

    /// <summary>
    /// TC-MDF-077 (i) — a morning occurrence on a non-trading day is skipped silently: no run,
    /// no ledger rows (the EOD stream's <c>TradingDaysOnly</c> posture mirrored).
    /// </summary>
    [Fact]
    public async Task TC_MDF_077_non_trading_day_occurrence_is_skipped_silently()
    {
        await ResetAsync();
        StubChainSources();

        await using var host = BuildScheduler();
        var clock = host.Clock;

        // Saturday 2026-10-10 — the 08:30 (and the 20:30 EOD) occurrences are both non-trading.
        clock.SetUtcNow(Trt(2026, 10, 10, 8, 29));
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 10, 23, 59));

        Assert.Equal(0, await LedgerCountAsync());
    }

    /// <summary>
    /// TC-MDF-077 (iii) — a failed morning attempt retries on the unchanged 5/15/60 ladder and
    /// raises <c>RETRIES_EXHAUSTED</c> after <c>MaxAttempts</c> (the existing ladder + alert
    /// machinery, applied to the morning stream).
    /// </summary>
    [Fact]
    public async Task TC_MDF_077_morning_failure_retries_on_ladder_and_alerts_exhausted()
    {
        await ResetAsync();
        await DeleteStoredPricesAsync(PreviousTradingDay);
        _wireMock.Server.ResetMappings();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesSourceDown, path: PricesPath);

        await using var host = BuildScheduler(new RecordingMailDispatcher());
        var clock = host.Clock;
        clock.SetUtcNow(Trt(2026, 10, 6, 8, 29));

        // Attempt 1 at 08:30 — the prices step fails and fail-stops the chain.
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 6, 8, 30));
        var afterFirst = await LedgerAsync();
        var first = Assert.Single(afterFirst);
        Assert.Equal("prices", first.JobCode);
        Assert.Equal(IngestResult.Failed, first.Status);
        Assert.Equal(Trt(2026, 10, 6, 8, 30), first.StartedAt!.Value);
        Assert.Equal(1, Stat(first, "attempt"));

        // Retry 1 at +5, retry 2 at +15, retry 3 at +60 — the ladder is unchanged.
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 6, 8, 35));
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 6, 8, 50));
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 6, 9, 50));

        var runs = await LedgerAsync();
        Assert.Equal(4, runs.Count);
        Assert.All(runs, run => Assert.Equal(IngestResult.Failed, run.Status));
        Assert.Equal(
            new[]
            {
                Trt(2026, 10, 6, 8, 30),
                Trt(2026, 10, 6, 8, 35),
                Trt(2026, 10, 6, 8, 50),
                Trt(2026, 10, 6, 9, 50),
            },
            runs.Select(run => run.StartedAt!.Value).ToArray());
        Assert.Equal(4, Stat(runs[^1], "attempt"));

        // RETRIES_EXHAUSTED after MaxAttempts, naming the morning run's target day.
        var exhausted = Assert.Single(
            host.Mail.Sent,
            mail => mail.Subject.Contains("failed after 4 attempts", StringComparison.Ordinal));
        Assert.Contains("2026-10-05", exhausted.Subject, StringComparison.Ordinal);

        // No further attempt fires past exhaustion.
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 6, 10, 30));
        Assert.Equal(4, await LedgerCountAsync());
    }

    // ---------------------------------------------------------------------------------
    // TC-MDF-078 — the stored-prices repair gate
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// TC-MDF-078 (a) — a fully covered day is never re-fetched. After a fully succeeded night
    /// the 08:30 pass invokes <b>no</b> İş Yatırım call (the gate skips the prices job and its
    /// ledger row records the skip), while statements/metrics/medians run and the snapshot
    /// no-ops. Zero <c>CONFLICTING_VALUE</c> quarantine rows (the TKT-foundation-035 lesson).
    /// </summary>
    [Fact]
    public async Task TC_MDF_078_covered_day_skips_prices_and_runs_the_rest()
    {
        await ResetAsync();
        // The L2 fixture already stores 2026-10-05's prices (T−1 of its anchor T). Clear them
        // so the night genuinely writes the day — otherwise the gate would be "covered" by the
        // seed and a night→morning regression could hide behind it.
        await DeleteStoredPricesAsync(PreviousTradingDay);
        StubChainSources();

        await using var host = BuildScheduler();
        var clock = host.Clock;

        // A fully succeeded night: the EOD chain at 20:30 on Monday writes the day end to end.
        clock.SetUtcNow(Trt(2026, 10, 5, 20, 29));
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 5, 20, 30));

        var nightRuns = await LedgerAsync();
        Assert.Equal(8, nightRuns.Count);
        Assert.All(nightRuns, run => Assert.Equal(IngestResult.Succeeded, run.Status));

        // The night is the writer: the day starts uncovered and ends with 16 stored rows.
        Assert.Equal("prices", nightRuns[0].JobCode);
        Assert.Equal(16, Stat(nightRuns[0], "written"));

        DateTimeOffset nightComputedAt;
        await using (var before = _postgres.CreateContext())
        {
            var nightSnapshot = await before.MarketSnapshots.SingleAsync();
            Assert.Equal(PreviousTradingDay, nightSnapshot.SnapshotDate);
            nightComputedAt = nightSnapshot.ComputedAt;
        }

        // From here on, every source call is the morning pass's.
        _wireMock.Server.ResetLogEntries();

        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 6, 8, 30));

        var runs = await LedgerAsync();
        var morning = runs.Skip(nightRuns.Count).ToList();
        Assert.Equal(MorningChain, morning.Select(run => run.JobCode).ToArray());
        Assert.All(morning, run => Assert.Equal(IngestResult.Succeeded, run.Status));

        // The gate: prices recorded as SKIPPED, no source call at all.
        var pricesRun = morning[0];
        Assert.Equal("skipped", StatString(pricesRun, "gateOutcome"));
        Assert.Equal("prices-coverage", StatString(pricesRun, "gate"));
        Assert.Equal(16, Stat(pricesRun, "constituents"));
        Assert.Equal(16, Stat(pricesRun, "covered"));
        Assert.Equal(0, Stat(pricesRun, "written"));
        Assert.Empty(PriceRequests());

        await using var db = _postgres.CreateContext();

        // The closed loop: metrics recomputed, snapshot untouched (one row per day), medians
        // recomputed — all for the previous trading day.
        Assert.True(await db.DerivedMetrics.AnyAsync(m => m.AsOfDate == PreviousTradingDay));
        Assert.True(await db.SectorMetricMedians.AnyAsync(m => m.AsOfDate == PreviousTradingDay));
        var snapshot = await db.MarketSnapshots.SingleAsync();
        Assert.Equal(PreviousTradingDay, snapshot.SnapshotDate);
        // No-op: the completed day's row was neither recomputed nor rewritten.
        Assert.Equal(nightComputedAt, snapshot.ComputedAt);

        // Zero quarantine noise on a normal morning.
        Assert.Equal(0, await db.QuarantinedFacts.CountAsync());
    }

    /// <summary>
    /// TC-MDF-078 (b) — a failed night is repaired by the next 08:30 pass: prices run for the
    /// previous trading day, metrics + snapshot + medians are written, and
    /// <c>GET /api/v1/market/overview</c> serves the repaired day (<c>state: available</c>,
    /// <c>asOf</c> = the repaired day).
    /// </summary>
    [Fact]
    public async Task TC_MDF_078_failed_night_is_repaired_by_the_morning_pass()
    {
        await ResetAsync();
        // A faithful failed-night world: neither the previous session (the repair target) nor
        // the 08:30 occurrence day's own session is in the store. The L2 anchor T = 2026-10-06
        // leaves T's prices seeded, so the occurrence day must be cleared too — otherwise the
        // served row would be "stale" only because of a session that a Tuesday morning cannot
        // have ingested yet.
        await DeleteStoredPricesAsync(PreviousTradingDay, MorningOccurrenceDay);
        StubChainSources();

        await using var host = BuildScheduler();
        var clock = host.Clock;

        // No EOD run at all: the previous session's prices are missing (a failed night).
        clock.SetUtcNow(Trt(2026, 10, 6, 8, 29));
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 6, 8, 30));

        var runs = await LedgerAsync();
        Assert.Equal(MorningChain, runs.Select(run => run.JobCode).ToArray());
        Assert.All(runs, run => Assert.Equal(IngestResult.Succeeded, run.Status));

        // The uncovered day ran the prices job at the existing pacing (one call per current
        // constituent — the XU100 fixture bound) and wrote the repaired day.
        Assert.Equal(16, Stat(runs[0], "written"));
        Assert.Equal(16, PriceRequests().Count);

        await using (var db = _postgres.CreateContext())
        {
            Assert.Equal(16, await db.DailyPrices.CountAsync(p => p.PriceDate == PreviousTradingDay));
            Assert.True(await db.DerivedMetrics.AnyAsync(m => m.AsOfDate == PreviousTradingDay));
            var snapshot = await db.MarketSnapshots.SingleAsync();
            Assert.Equal(PreviousTradingDay, snapshot.SnapshotDate);
            Assert.True(await db.SectorMetricMedians.AnyAsync(m => m.AsOfDate == PreviousTradingDay));
        }

        // The user-facing point: the overview serves the repaired day by the morning.
        // The harness host's clock is 2026-10-06 (Tuesday), the morning pass's own day.
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

        using var response = await client.GetAsync("/api/v1/market/overview");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal("2026-10-05", root.GetProperty("asOf").GetString());
        Assert.Equal("available", root.GetProperty("state").GetString());
        // Honest freshness (01 §10.9.13, v1.5.13): on a Tuesday morning Monday is the latest
        // stored trading day, so serving Monday's repaired session is FRESH. `stale: true`
        // means a newer *stored* session exists that the served row lacks — never "the clock
        // has moved past a weekday".
        Assert.False(root.GetProperty("stale").GetBoolean());
    }

    /// <summary>
    /// TC-MDF-078 (c) — partial coverage is not "covered": the decision table's third
    /// missing/<b>partial</b> prices row (the ticket's criterion (ii)(b)). With one of the
    /// sixteen XU100 constituents missing from an otherwise completed day, the morning gate
    /// must run the prices job (the ledger row is the runner's, carrying no <c>gateOutcome</c>)
    /// and the day must end complete.
    /// </summary>
    [Fact]
    public async Task TC_MDF_078_partially_covered_day_runs_prices_and_completes()
    {
        await ResetAsync();
        // As in 078(a): the night writes the day, so coverage comes from this run, not the seed.
        await DeleteStoredPricesAsync(PreviousTradingDay);
        StubChainSources();

        await using var host = BuildScheduler();
        var clock = host.Clock;

        // A fully succeeded night, then one constituent's row is lost: 15 of 16 stored.
        clock.SetUtcNow(Trt(2026, 10, 5, 20, 29));
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 5, 20, 30));
        var nightRuns = await LedgerAsync();
        Assert.Equal(8, nightRuns.Count);

        await DeleteOneConstituentsPricesAsync(PreviousTradingDay);

        await using (var before = _postgres.CreateContext())
        {
            Assert.Equal(15, await before.DailyPrices.CountAsync(p => p.PriceDate == PreviousTradingDay));
        }

        // From here on, every source call is the morning pass's.
        _wireMock.Server.ResetLogEntries();

        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 6, 8, 30));

        var runs = await LedgerAsync();
        var morning = runs.Skip(nightRuns.Count).ToList();
        Assert.Equal(MorningChain, morning.Select(run => run.JobCode).ToArray());

        // The gate did NOT skip: the prices job ran for real. Its row is the job runner's —
        // no `gate`/`gateOutcome` keys — and it reached the source. The current
        // implementation re-fetches the whole bound at the existing pacing and writes only
        // the missing name, so the exact request count is deliberately not pinned here.
        var pricesRun = morning[0];
        Assert.Equal(IngestResult.Succeeded, pricesRun.Status);
        Assert.False(pricesRun.StatsJson!.Contains("gateOutcome", StringComparison.Ordinal));
        Assert.NotEmpty(PriceRequests());

        // The boundary the partial case exists to catch: the day ends complete, not "mostly".
        await using var db = _postgres.CreateContext();
        Assert.Equal(16, await db.DailyPrices.CountAsync(p => p.PriceDate == PreviousTradingDay));
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    /// <summary>Points every source-reading job of the chain at its WireMock canned payload.</summary>
    private void StubChainSources()
    {
        _wireMock.Server.ResetMappings();
        // Each test owns the request log: the gate's cost is measured from a clean slate.
        _wireMock.Server.ResetLogEntries();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesOkTminus1, path: PricesPath);
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.StatementsOkAlfaFy2025, path: StatementsPath);
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.DividendsOk, path: DividendsPath);
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.CorporateActionsOk, path: CorporateActionsPath);
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.DisclosuresOk, path: DisclosuresPath);
    }

    /// <summary>The İş Yatırım price-route request log entries (the morning gate's cost meter).</summary>
    private List<ILogEntry> PriceRequests() =>
        _wireMock.Server.LogEntries
            .Where(entry => string.Equals(entry.RequestMessage?.Path, PricesPath, StringComparison.Ordinal))
            .ToList();

    /// <summary>
    /// Drives the same wake-then-tick loop the hosted service runs, deterministically over the
    /// fake clock (test strategy §7).
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

    private IngestionSchedulerHost BuildScheduler(RecordingMailDispatcher? mail = null)
    {
        mail ??= new RecordingMailDispatcher();
        var logs = new InMemorySerilogSink();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

        // The shipped morning cron (`0 30 8 * * *`) alongside the unchanged EOD cron: both
        // chain streams on the same engine, composing the production registration.
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _postgres.ConnectionString,
            ["Ingestion:Prices:BaseUrl"] = _wireMock.BaseUrl,
            ["Ingestion:Prices:Path"] = PricesPath,
            ["Ingestion:Prices:UniverseIndex"] = "XU100",
            ["Ingestion:Prices:RequestDelayMs"] = "0",
            ["Ingestion:Kap:BaseUrl"] = _wireMock.BaseUrl,
            ["Ingestion:Statements:Path"] = StatementsPath,
            ["Ingestion:Statements:UniverseIndex"] = StatementBound.IndexCode,
            ["Ingestion:Statements:RequestDelayMs"] = "0",
            ["Ingestion:Dividends:Path"] = DividendsPath,
            ["Ingestion:CorporateActions:Path"] = CorporateActionsPath,
            ["Ingestion:Disclosures:Path"] = DisclosuresPath,
            ["Ingestion:Scheduler:Cron"] = "0 30 20 * * *",
            ["Ingestion:Scheduler:MorningCron"] = "0 30 8 * * *",
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

    /// <summary>Models an uncovered/failed session: the stored prices for the given day(s) are absent.</summary>
    private async Task DeleteStoredPricesAsync(params DateOnly[] dates)
    {
        await using var db = _postgres.CreateContext();
        foreach (var date in dates)
        {
            await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM daily_prices WHERE price_date = {0}", date);
        }
    }

    /// <summary>
    /// Models a <b>partially</b> covered day: one current XU100 constituent's stored row is
    /// absent while the other fifteen remain.
    /// </summary>
    private async Task DeleteOneConstituentsPricesAsync(DateOnly date)
    {
        await using var db = _postgres.CreateContext();
        var xu100Id = await db.Indices
            .Where(index => index.Code == "XU100")
            .Select(index => index.Id)
            .SingleAsync();
        var instrumentId = await db.IndexConstituents
            .Where(c => c.IndexId == xu100Id && c.EffectiveTo == null)
            .Select(c => c.InstrumentId)
            .OrderBy(id => id)
            .FirstAsync();

        await db.DailyPrices
            .Where(p => p.PriceDate == date && p.InstrumentId == instrumentId)
            .ExecuteDeleteAsync();
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

    private static int Stat(IngestRun run, string name)
    {
        using var document = JsonDocument.Parse(run.StatsJson!);
        return document.RootElement.GetProperty(name).GetInt32();
    }

    private static string StatString(IngestRun run, string name)
    {
        using var document = JsonDocument.Parse(run.StatsJson!);
        return document.RootElement.GetProperty(name).GetString()!;
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
