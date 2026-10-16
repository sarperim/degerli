using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Api.IntegrationTests.Harness.Mail;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.Coverage;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Scheduling;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Serilog;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-fdf-002 acceptance — fund scheduling, coverage and failure handling
/// (TC-FDF-005, TC-FDF-006, TC-FDF-007, TC-FDF-008; TC-FDF-010 is covered by
/// <c>Xc018OpenApiTests</c>). Every test runs against a migrated Testcontainers
/// PostgreSQL and the FU §10 WireMock TEFAS source double, composing the same
/// <c>AddMarketDataIngestion</c> registration the API host uses.
/// </summary>
public sealed class FundSchedulingCoverageTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private const string UniverseRoute = "/canned-sources/tefas-universe";
    private const string BuilderEmail = "builder@degerli.test";
    private const string FixturePassword = "FixturePass1!";

    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static readonly DateOnly T = L2.Anchor.T;

    /// <summary>Earliest NAV publication in the FU universe (T−6) — 2026-09-28.</summary>
    private static readonly DateOnly NavEarliest = L2.Anchor.TradingDay(-6);

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public FundSchedulingCoverageTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    // ---------------------------------------------------------------------------------
    // TC-FDF-005 — Per-fund coverage metadata with holdings gaps
    // ---------------------------------------------------------------------------------

    /// <summary>TC-FDF-005 (job side) — the three fund jobs record per-fund coverage;
    /// an absent data type is an explicit gap, never a silent omission.</summary>
    [Fact]
    public async Task TC_FDF_005_Per_fund_coverage_metadata_with_holdings_gaps()
    {
        await ResetAsync(clearFunds: true);
        StubUniverse(Universe("TEF0001", "TEF0002", "TEF0003"));
        await using var host = BuildIngestion();

        await RunAsync(host, "fund-nav");
        await RunAsync(host, "fund-performance");
        await RunAsync(host, "fund-holdings");

        await using var db = _postgres.CreateContext();

        // TEF0001 — NAV + performance present; holdings descoped to a recorded gap (TC-FDF-013).
        var tef1 = await FundCoverageAsync(db, "TEF0001");
        var tef1Nav = CoverageRow(tef1, CoverageDataTypes.Nav);
        Assert.Equal(NavEarliest, tef1Nav.AvailableFrom);
        Assert.Equal(T, tef1Nav.AvailableTo);
        Assert.Equal(T, CoverageRow(tef1, CoverageDataTypes.Performance).AvailableFrom);
        var tef1Holdings = CoverageRow(tef1, CoverageDataTypes.Holdings);
        Assert.Null(tef1Holdings.AvailableFrom);
        Assert.False(
            string.IsNullOrWhiteSpace(tef1Holdings.Notes),
            "the descoped holdings gap must be recorded, never silent");

        // TEF0002 — NAV + performance present; holdings absent recorded as a gap.
        var tef2 = await FundCoverageAsync(db, "TEF0002");
        Assert.Equal(NavEarliest, CoverageRow(tef2, CoverageDataTypes.Nav).AvailableFrom);
        Assert.Equal(T, CoverageRow(tef2, CoverageDataTypes.Performance).AvailableFrom);
        var tef2Holdings = CoverageRow(tef2, CoverageDataTypes.Holdings);
        Assert.Null(tef2Holdings.AvailableFrom);
        Assert.False(string.IsNullOrWhiteSpace(tef2Holdings.Notes), "the holdings gap must be recorded, never silent");

        // TEF0003 — NAV present; the absent performance and the descoped holdings are recorded gaps.
        var tef3 = await FundCoverageAsync(db, "TEF0003");
        Assert.Equal(NavEarliest, CoverageRow(tef3, CoverageDataTypes.Nav).AvailableFrom);
        Assert.Null(CoverageRow(tef3, CoverageDataTypes.Holdings).AvailableFrom);
        var tef3Performance = CoverageRow(tef3, CoverageDataTypes.Performance);
        Assert.Null(tef3Performance.AvailableFrom);
        Assert.False(string.IsNullOrWhiteSpace(tef3Performance.Notes));
    }

    /// <summary>TC-FDF-005 (report side) — the admin coverage report serves the fund
    /// scope with from-when dates and holdings availability.</summary>
    [Fact]
    public async Task TC_FDF_005_Fund_scope_coverage_report_serves_from_when_dates_and_holdings_availability()
    {
        await ResetAsync();
        await using (var db = _postgres.CreateContext())
        {
            await new FundCoverageRecorder(db).ReconcileAsync();
        }

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var builder = await SignedInClientAsync(factory, BuilderEmail);
        using var response = await GetAsync(builder, "/api/v1/admin/coverage?scope=fund");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal("fund", root.GetProperty("scope").GetString());

        var funds = root.GetProperty("funds").EnumerateArray().Select(element => element.Clone()).ToList();
        Assert.Equal(3, funds.Count);

        // The fund scope renders exactly the fund data types (no instrument types leak in).
        var tef1 = Fund(funds, "TEF0001");
        Assert.Equal(
            new[] { CoverageDataTypes.Holdings, CoverageDataTypes.Nav, CoverageDataTypes.Performance },
            DataTypeNames(tef1).OrderBy(name => name).ToArray());

        var tef1Nav = FundDataType(tef1, CoverageDataTypes.Nav);
        Assert.True(tef1Nav.GetProperty("present").GetBoolean());
        Assert.Equal(NavEarliest.ToString("yyyy-MM-dd"), tef1Nav.GetProperty("availableFrom").GetString());
        Assert.True(FundDataType(tef1, CoverageDataTypes.Holdings).GetProperty("present").GetBoolean());

        // TEF0002's holdings gap is shown explicitly.
        var tef2Holdings = FundDataType(Fund(funds, "TEF0002"), CoverageDataTypes.Holdings);
        Assert.False(tef2Holdings.GetProperty("present").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(tef2Holdings.GetProperty("notes").GetString()));
    }

    // ---------------------------------------------------------------------------------
    // TC-FDF-006 — Daily refresh unattended
    // ---------------------------------------------------------------------------------

    /// <summary>TC-FDF-006 — at the TEFAS cadence the fund jobs trigger without any
    /// manual intervention, each writing its own ledger row with stats.</summary>
    [Fact]
    public async Task TC_FDF_006_Daily_refresh_unattended()
    {
        await ResetAsync(clearFunds: true);
        _wireMock.Server.ResetMappings();
        StubUniverse(Universe("TEF0001", "TEF0002", "TEF0003"));

        await using var host = BuildSchedulerHost(new Dictionary<string, string?>
        {
            ["Ingestion:Scheduler:JobSchedules:0:JobCode"] = "fund-nav",
            ["Ingestion:Scheduler:JobSchedules:0:Cron"] = "0 0 19 * * *",
            ["Ingestion:Scheduler:JobSchedules:1:JobCode"] = "fund-performance",
            ["Ingestion:Scheduler:JobSchedules:1:Cron"] = "0 5 19 * * *",
            ["Ingestion:Scheduler:JobSchedules:2:JobCode"] = "fund-holdings",
            ["Ingestion:Scheduler:JobSchedules:2:Cron"] = "0 10 19 * * *",
        });

        host.Clock.SetUtcNow(Trt(2026, 10, 6, 18, 0));
        await AdvanceToAsync(host.Scheduler, host.Clock, Trt(2026, 10, 6, 19, 15));

        await using var db = _postgres.CreateContext();
        var runs = await db.IngestRuns.OrderBy(run => run.StartedAt).ThenBy(run => run.Id).ToListAsync();

        Assert.Equal(
            new[] { "fund-nav", "fund-performance", "fund-holdings" },
            runs.Select(run => run.JobCode).ToArray());
        Assert.All(runs, run => Assert.Equal(IngestResult.Succeeded, run.Status));
        Assert.All(runs, run =>
        {
            using var stats = JsonDocument.Parse(run.StatsJson!);
            Assert.Equal("incremental", stats.RootElement.GetProperty("mode").GetString());
        });

        // The unattended cycle wrote the day's fund facts at each job's cadence. Holdings are
        // descoped (TC-FDF-013): the fund-holdings job runs on schedule but writes no
        // ticker-level rows — the per-fund gap is recorded in coverage_metadata instead.
        Assert.Equal(15, await db.FundNavs.CountAsync());
        Assert.Equal(5, await db.FundPerformances.CountAsync());
        Assert.Equal(0, await db.FundHoldings.CountAsync());
    }

    // ---------------------------------------------------------------------------------
    // TC-FDF-007 — Fund backfill records achieved depth
    // ---------------------------------------------------------------------------------

    /// <summary>TC-FDF-007 — backfill ingests the history the source holds and records
    /// the achieved depth; a source limit is recorded as the actual depth, never
    /// fabricated over.</summary>
    [Fact]
    public async Task TC_FDF_007_Fund_backfill_records_achieved_depth()
    {
        await ResetAsync(clearFunds: true);
        StubUniverse(Universe("TEF0001", "TEF0002", "TEF0003"));
        await using var host = BuildIngestion();

        // Full-range variant: the requested target is the earliest row the source has,
        // so the achieved depth reaches it and no limitation is recorded.
        var full = await RunAsync(host, "fund-nav", new IngestionRequest(T, NavEarliest));
        Assert.Equal(IngestResult.Succeeded, full.Status);
        await using (var db = _postgres.CreateContext())
        {
            var nav = await CoverageRowAsync(db, "TEF0001", CoverageDataTypes.Nav);
            Assert.Equal(NavEarliest, nav.AvailableFrom);
            Assert.Null(nav.Notes);
        }

        // Source-limited variant: a 10Y target the source cannot reach — the achieved
        // depth (T−6) is recorded, the limitation noted, nothing fabricated before it.
        var requested = new DateOnly(2016, 1, 1);
        await RunAsync(host, "fund-nav", new IngestionRequest(T, requested));
        await RunAsync(host, "fund-performance", new IngestionRequest(T, requested));
        await RunAsync(host, "fund-holdings", new IngestionRequest(T, requested));

        await using (var db = _postgres.CreateContext())
        {
            Assert.Equal(15, await db.FundNavs.CountAsync());
            Assert.Equal(0, await db.FundNavs.CountAsync(n => n.NavDate < NavEarliest));
            Assert.Equal(5, await db.FundPerformances.CountAsync());
            // Holdings are descoped (TC-FDF-013): no ticker-level rows, only the recorded gap.
            Assert.Equal(0, await db.FundHoldings.CountAsync());

            var nav = await CoverageRowAsync(db, "TEF0001", CoverageDataTypes.Nav);
            Assert.Equal(NavEarliest, nav.AvailableFrom);
            Assert.Equal(T, nav.AvailableTo);
            Assert.False(string.IsNullOrWhiteSpace(nav.Notes), "the source limitation must be recorded");
            Assert.Contains("2016-01-01", nav.Notes);
            Assert.Contains(NavEarliest.ToString("yyyy-MM-dd"), nav.Notes);

            // The ledger records the backfill mode and its start date.
            var run = await db.IngestRuns
                .Where(row => row.JobCode == "fund-nav")
                .OrderByDescending(row => row.Id)
                .FirstAsync();
            using var stats = JsonDocument.Parse(run.StatsJson!);
            Assert.Equal("backfill", stats.RootElement.GetProperty("mode").GetString());
            Assert.Equal("2016-01-01", stats.RootElement.GetProperty("backfillFrom").GetString());
        }
    }

    // ---------------------------------------------------------------------------------
    // TC-FDF-008 — Source failure: last-known retained, anomaly alerted
    // ---------------------------------------------------------------------------------

    /// <summary>TC-FDF-008 (failure side) — a TEFAS failure fails the run after the
    /// 5/15/60 retry ladder, alerts the builder naming the fund job, and leaves the
    /// last-known NAV rows untouched.</summary>
    [Fact]
    public async Task TC_FDF_008_Source_failure_retries_alerts_and_retains_last_known()
    {
        await ResetAsync();
        var knownBefore = await KnownNavsAsync();
        Assert.Equal(15, knownBefore.Count);

        _wireMock.Server.ResetMappings();
        _wireMock.StubJson(UniverseRoute, "{\"sourceRef\":\"tefas://funds\",\"funds\":[]}", statusCode: 500);

        await using var host = BuildSchedulerHost(new Dictionary<string, string?>
        {
            ["Ingestion:Scheduler:JobSchedules:0:JobCode"] = "fund-nav",
            ["Ingestion:Scheduler:JobSchedules:0:Cron"] = "0 0 19 * * *",
        });

        host.Clock.SetUtcNow(Trt(2026, 10, 6, 18, 0));
        await AdvanceToAsync(host.Scheduler, host.Clock, Trt(2026, 10, 6, 19, 0));   // attempt 1
        await AdvanceToAsync(host.Scheduler, host.Clock, Trt(2026, 10, 6, 19, 5));   // retry 1 (+5m)
        await AdvanceToAsync(host.Scheduler, host.Clock, Trt(2026, 10, 6, 19, 20));  // retry 2 (+15m)
        await AdvanceToAsync(host.Scheduler, host.Clock, Trt(2026, 10, 6, 20, 20));  // retry 3 (+60m) → exhausted

        await using var db = _postgres.CreateContext();
        var runs = await db.IngestRuns
            .Where(row => row.JobCode == "fund-nav")
            .OrderBy(row => row.Id)
            .ToListAsync();
        Assert.Equal(4, runs.Count);
        Assert.All(runs, run => Assert.Equal(IngestResult.Failed, run.Status));
        Assert.Equal(1, Attempt(runs[0]));
        Assert.Equal(4, Attempt(runs[^1]));

        // The builder is alerted (mail double) with the failing fund job named.
        var alert = Assert.Single(host.Mail.Sent);
        Assert.Contains("fund-nav", alert.Subject);

        // Last-known-good NAV rows are retained, unchanged.
        var knownAfter = await KnownNavsAsync();
        Assert.Equal(knownBefore, knownAfter);
    }

    /// <summary>TC-FDF-008 (freshness side) — a failed fund run surfaces as stale in the
    /// admin summary's freshness (FR-FDF-005, v_data_freshness).</summary>
    [Fact]
    public async Task TC_FDF_008_Fund_staleness_visible_in_admin_summary()
    {
        await ResetAsync();
        await using (var db = _postgres.CreateContext())
        {
            var finishedAt = new DateTimeOffset(2026, 10, 6, 18, 30, 0, TimeSpan.Zero);
            db.IngestRuns.Add(new IngestRun
            {
                JobCode = "fund-nav",
                Status = IngestResult.Failed,
                StartedAt = finishedAt.AddMinutes(-1),
                FinishedAt = finishedAt,
                StatsJson = "{}",
            });
            await db.SaveChangesAsync();
        }

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var builder = await SignedInClientAsync(factory, BuilderEmail);
        using var response = await GetAsync(builder, "/api/v1/admin/summary");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var fundNav = document.RootElement
            .GetProperty("freshness")
            .EnumerateArray()
            .Single(entry => entry.GetProperty("dataType").GetString() == "fund-nav");
        Assert.True(fundNav.GetProperty("stale").GetBoolean(), "the failed fund run's data type must read as stale");
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    private IngestionHost BuildIngestion()
    {
        var mail = new RecordingMailDispatcher();
        var logs = new InMemorySerilogSink();
        var clock = new FakeTimeProvider(new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _postgres.ConnectionString,
                ["Ingestion:Funds:BaseUrl"] = _wireMock.BaseUrl,
                ["Ingestion:Funds:UniversePath"] = UniverseRoute,
            })
            .Build();

        return BuildHost(configuration, clock, mail, logs);
    }

    private IngestionHost BuildSchedulerHost(IDictionary<string, string?> schedulerSettings)
    {
        var mail = new RecordingMailDispatcher();
        var logs = new InMemorySerilogSink();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero));

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _postgres.ConnectionString,
            ["Ingestion:Funds:BaseUrl"] = _wireMock.BaseUrl,
            ["Ingestion:Funds:UniversePath"] = UniverseRoute,
        };
        foreach (var (key, value) in schedulerSettings)
        {
            settings[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return BuildHost(configuration, clock, mail, logs);
    }

    private static IngestionHost BuildHost(
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

        return new IngestionHost(services.BuildServiceProvider(), clock, mail, logs);
    }

    private static async Task<IngestResult> RunAsync(IngestionHost host, string jobCode, IngestionRequest? request = null)
    {
        using var scope = host.Provider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IIngestionJobRunner>();
        return await runner.RunAsync(jobCode, request ?? new IngestionRequest(T));
    }

    /// <summary>Drives the same wake-then-tick loop the hosted service runs, but
    /// deterministically over the fake clock (mirrors SchedulerTests).</summary>
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

    private async Task<List<(string FundId, DateOnly NavDate, decimal NavValue)>> KnownNavsAsync()
    {
        await using var db = _postgres.CreateContext();
        return (await db.FundNavs.AsNoTracking().ToListAsync())
            .Select(nav => (nav.FundId, nav.NavDate, nav.NavValue))
            .OrderBy(nav => nav.FundId)
            .ThenBy(nav => nav.NavDate)
            .ToList();
    }

    private static async Task<Dictionary<string, CoverageMetadata>> FundCoverageAsync(DegerliDbContext db, string fundCode) =>
        (await db.CoverageMetadata
                .Where(row => row.Scope == "fund" && row.FundId == fundCode)
                .ToListAsync())
            .ToDictionary(row => row.DataType, StringComparer.Ordinal);

    private static async Task<CoverageMetadata> CoverageRowAsync(DegerliDbContext db, string fundCode, string dataType) =>
        await db.CoverageMetadata.SingleAsync(row =>
            row.Scope == "fund" && row.FundId == fundCode && row.DataType == dataType);

    private static CoverageMetadata CoverageRow(Dictionary<string, CoverageMetadata> coverage, string dataType) =>
        coverage[dataType];

    private async Task ResetAsync(bool clearFunds = false)
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM coverage_metadata");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM quarantined_facts");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
        if (clearFunds)
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM fund_holdings");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM fund_performances");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM fund_navs");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM funds");
        }
    }

    private static JsonObject Universe(params string[] codes)
    {
        var funds = new JsonArray();
        foreach (var code in codes)
        {
            funds.Add(JsonNode.Parse(CannedSourceCatalog.Get(L2, $"tefas-{code.ToLowerInvariant()}").Body)!);
        }

        return new JsonObject
        {
            ["sourceRef"] = "tefas://funds",
            ["funds"] = funds,
        };
    }

    private void StubUniverse(JsonObject universe) =>
        _wireMock.StubJson(UniverseRoute, universe.ToJsonString());

    private static int Attempt(IngestRun run)
    {
        using var stats = JsonDocument.Parse(run.StatsJson!);
        return stats.RootElement.GetProperty("attempt").GetInt32();
    }

    private static DateTimeOffset Trt(int year, int month, int day, int hour, int minute) =>
        new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.FromHours(3)).ToUniversalTime();

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    private static async Task<HttpClient> SignedInClientAsync(WebApplicationFactory<Program> factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var signInManager = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();

        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);

        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        accessor.HttpContext = httpContext;

        var result = await signInManager.PasswordSignInAsync(user!, FixturePassword, isPersistent: false, lockoutOnFailure: true);
        Assert.True(result.Succeeded, $"expected a sign-in for {email} to succeed");

        var setCookie = httpContext.Response.Headers.SetCookie.ToString();
        var cookiePair = setCookie.Split(';', 2)[0];
        Assert.StartsWith("degerli.auth=", cookiePair, StringComparison.Ordinal);

        var client = CreateClient(factory);
        client.DefaultRequestHeaders.Add("Cookie", cookiePair);
        return client;
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Forwarded-For", Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }

    private static JsonElement Fund(List<JsonElement> funds, string code) =>
        funds.Single(fund => fund.GetProperty("code").GetString() == code);

    private static JsonElement FundDataType(JsonElement fund, string dataType) =>
        fund.GetProperty("dataTypes").EnumerateArray()
            .Single(entry => entry.GetProperty("dataType").GetString() == dataType);

    private static IEnumerable<string> DataTypeNames(JsonElement fund) =>
        fund.GetProperty("dataTypes").EnumerateArray()
            .Select(entry => entry.GetProperty("dataType").GetString()!);

    private sealed class IngestionHost : IAsyncDisposable
    {
        public IngestionHost(
            ServiceProvider provider,
            FakeTimeProvider clock,
            RecordingMailDispatcher mail,
            InMemorySerilogSink logs)
        {
            Provider = provider;
            Clock = clock;
            Mail = mail;
            Logs = logs;
        }

        public ServiceProvider Provider { get; }

        public FakeTimeProvider Clock { get; }

        public IngestionScheduler Scheduler => Provider.GetRequiredService<IngestionScheduler>();

        public RecordingMailDispatcher Mail { get; }

        public InMemorySerilogSink Logs { get; }

        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    private sealed class HarnessMailAlerter : IIngestionAlerter
    {
        private readonly RecordingMailDispatcher _mail;

        public HarnessMailAlerter(RecordingMailDispatcher mail) => _mail = mail;

        public Task RaiseAsync(IngestionAlert alert, CancellationToken cancellationToken = default) =>
            _mail.SendAsync(
                new Degerli.Api.Mail.OutboundMail("builder@degerli.test", alert.Subject, alert.BodyTr, alert.BodyEn),
                cancellationToken);
    }
}
