using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;
using Degerli.Ingestion.Coverage;
using Degerli.Ingestion.Prices;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-mdf-010 acceptance — the builder-only admin ops endpoints (`03` §8, AD-13):
/// summary (TC-MDF-040), coverage report (TC-MDF-017), run ledger (TC-MDF-041), job +
/// backfill triggers (TC-MDF-015/042/043), aggregate stats (TC-MDF-044) and the shared
/// role gate (TC-MDF-039). Every test runs through the real in-process API host against
/// a migrated, fixture-seeded Testcontainers PostgreSQL; the backfill tests point the
/// <c>prices</c> source at the WireMock double.
/// </summary>
public sealed class AdminEndpointsTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private const string BuilderEmail = "builder@degerli.test";
    private const string UserAEmail = "user-a@degerli.test";
    private const string FixturePassword = "FixturePass1!";
    private const string PricesBackfillPath = "/prices/history";
    private const string PricesEodPath = "/prices/eod";

    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public AdminEndpointsTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    // ---------------------------------------------------------------------------------
    // TC-MDF-039 — Admin role gating (API)
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task TC_MDF_039_Admin_endpoints_are_builder_only()
    {
        await ResetAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        const string trigger = "/api/v1/admin/ingest/metrics-recompute/run";

        // anonymous: safe read → 401 UNAUTHENTICATED.
        using (var anonymous = CreateClient(factory))
        {
            using var summary = await GetAsync(anonymous, "/api/v1/admin/summary");
            Assert.Equal(HttpStatusCode.Unauthorized, summary.StatusCode);
            Assert.Equal("UNAUTHENTICATED", await CodeAsync(summary));
        }

        // anonymous unsafe without a CSRF token → 400 (cross-tested in XC).
        using (var anonymous = CreateClient(factory))
        {
            using var noCsrf = await PostAsync(anonymous, trigger, body: null, csrf: null);
            Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        }

        // anonymous unsafe with a CSRF token → 401 (the gate runs after antiforgery).
        using (var anonymous = CreateClient(factory))
        {
            var csrf = await CsrfTokenAsync(anonymous);
            using var response = await PostAsync(anonymous, trigger, body: null, csrf);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("UNAUTHENTICATED", await CodeAsync(response));
        }

        // user-a: 403 FORBIDDEN on both the read and the unsafe call.
        using (var user = await SignedInClientAsync(factory, UserAEmail))
        {
            using var summary = await GetAsync(user, "/api/v1/admin/summary");
            Assert.Equal(HttpStatusCode.Forbidden, summary.StatusCode);
            Assert.Equal("FORBIDDEN", await CodeAsync(summary));

            var csrf = await CsrfTokenAsync(user);
            using var unsafeResponse = await PostAsync(user, trigger, body: null, csrf);
            Assert.Equal(HttpStatusCode.Forbidden, unsafeResponse.StatusCode);
            Assert.Equal("FORBIDDEN", await CodeAsync(unsafeResponse));
        }

        // builder: 200 on both.
        using (var builder = await SignedInClientAsync(factory, BuilderEmail))
        {
            using var summary = await GetAsync(builder, "/api/v1/admin/summary");
            Assert.Equal(HttpStatusCode.OK, summary.StatusCode);

            var csrf = await CsrfTokenAsync(builder);
            using var response = await PostAsync(builder, trigger, body: null, csrf);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    // ---------------------------------------------------------------------------------
    // TC-MDF-040 — Ops summary payload
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task TC_MDF_040_Ops_summary_payload()
    {
        await ResetAsync();

        var completedAt = new DateTimeOffset(T.ToDateTime(new TimeOnly(21, 14, 3)), TimeSpan.Zero);
        await using (var db = _postgres.CreateContext())
        {
            db.IngestRuns.Add(new IngestRun
            {
                JobCode = "prices",
                Status = "succeeded",
                StartedAt = completedAt.AddMinutes(-3),
                FinishedAt = completedAt,
                StatsJson = "{}",
            });

            for (var i = 0; i < 4; i++)
            {
                db.QuarantinedFacts.Add(new QuarantinedFact
                {
                    JobCode = "prices",
                    SourceRef = $"isbank://eod/{i}",
                    PayloadJson = "{\"symbol\":\"ALFA\"}",
                    ReasonCode = "NEGATIVE_PRICE",
                    QuarantinedAt = completedAt,
                    Status = "open",
                });
            }

            await db.SaveChangesAsync();
        }

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var builder = await SignedInClientAsync(factory, BuilderEmail);
        using var response = await GetAsync(builder, "/api/v1/admin/summary");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(T.ToString("yyyy-MM-dd"), root.GetProperty("asOf").GetString());

        // jobs[].lastRun.{status, finishedAt} (UXR-MDF-003).
        var pricesJob = root.GetProperty("jobs").EnumerateArray()
            .Single(job => job.GetProperty("job").GetString() == "prices");
        var lastRun = pricesJob.GetProperty("lastRun");
        Assert.Equal("succeeded", lastRun.GetProperty("status").GetString());
        Assert.StartsWith(completedAt.ToString("yyyy-MM-ddTHH:mm:ss"), lastRun.GetProperty("finishedAt").GetString());

        // freshness[] with GOLD stale and its T−10 last-success (UXR-MDF-004).
        var gold = root.GetProperty("freshness").EnumerateArray()
            .Single(entry => entry.GetProperty("dataType").GetString() == "macro-gold");
        Assert.True(gold.GetProperty("stale").GetBoolean());
        Assert.Equal(L2.Anchor.Calendar(-10).ToString("yyyy-MM-dd"), gold.GetProperty("lastSuccess").GetString());

        // contentCoverage counts match the RES fixtures (UXR-MDF-005).
        var content = root.GetProperty("contentCoverage");
        Assert.Equal(1, content.GetProperty("descriptionsPublished").GetInt32());
        Assert.Equal(2, content.GetProperty("descriptionsDraft").GetInt32());
        Assert.Equal(16, content.GetProperty("universe").GetInt32());

        // open-quarantine count matches the seeded rows (UXR-MDF-006).
        Assert.Equal(4, root.GetProperty("openQuarantineCount").GetInt32());
    }

    // ---------------------------------------------------------------------------------
    // TC-MDF-041 — Run ledger is filterable
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task TC_MDF_041_Run_ledger_is_filterable()
    {
        await ResetAsync();

        await using (var db = _postgres.CreateContext())
        {
            var baseTime = new DateTimeOffset(T.ToDateTime(new TimeOnly(20, 0, 0)), TimeSpan.Zero);
            db.IngestRuns.AddRange(
                Run("prices", "succeeded", baseTime.AddMinutes(1)),
                Run("prices", "failed", baseTime.AddMinutes(2)),
                Run("statements", "succeeded", baseTime.AddMinutes(3)));
            await db.SaveChangesAsync();
        }

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var builder = await SignedInClientAsync(factory, BuilderEmail);

        var all = await RunsAsync(builder, "/api/v1/admin/ingest-runs");
        Assert.Equal(3, all.Count);
        Assert.All(all, row =>
        {
            Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("job").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("status").GetString()));
            Assert.Equal(JsonValueKind.String, row.GetProperty("startedAt").ValueKind);
            Assert.Equal(JsonValueKind.String, row.GetProperty("finishedAt").ValueKind);
        });

        var byJob = await RunsAsync(builder, "/api/v1/admin/ingest-runs?job=prices");
        Assert.Equal(2, byJob.Count);
        Assert.All(byJob, row => Assert.Equal("prices", row.GetProperty("job").GetString()));

        var failed = await RunsAsync(builder, "/api/v1/admin/ingest-runs?status=failed");
        var failedRow = Assert.Single(failed);
        Assert.Equal("failed", failedRow.GetProperty("status").GetString());

        var conjunctive = await RunsAsync(builder, "/api/v1/admin/ingest-runs?job=prices&status=succeeded");
        var conjunctiveRow = Assert.Single(conjunctive);
        Assert.Equal("prices", conjunctiveRow.GetProperty("job").GetString());
        Assert.Equal("succeeded", conjunctiveRow.GetProperty("status").GetString());
    }

    // ---------------------------------------------------------------------------------
    // TC-MDF-042 — Job trigger (valid and unknown codes)
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task TC_MDF_042_Job_trigger_valid_and_unknown_codes()
    {
        await ResetAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var builder = await SignedInClientAsync(factory, BuilderEmail);

        var csrf = await CsrfTokenAsync(builder);
        using (var valid = await PostAsync(builder, "/api/v1/admin/ingest/metrics-recompute/run", body: null, csrf))
        {
            Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
            using var document = JsonDocument.Parse(await valid.Content.ReadAsStringAsync());
            Assert.Equal("metrics-recompute", document.RootElement.GetProperty("job").GetString());
            Assert.True(document.RootElement.GetProperty("runId").GetInt64() > 0, "a run reference is returned");
        }

        await using (var db = _postgres.CreateContext())
        {
            Assert.True(
                await db.IngestRuns.AnyAsync(run => run.JobCode == "metrics-recompute"),
                "the triggered run must be visible in the ledger");
        }

        // An unregistered job code is a path id that does not resolve → 404 NOT_FOUND.
        var csrf2 = await CsrfTokenAsync(builder);
        using (var unknown = await PostAsync(builder, "/api/v1/admin/ingest/not-a-job/run", body: null, csrf2))
        {
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
            Assert.Equal("NOT_FOUND", await CodeAsync(unknown));
        }

        await using (var db = _postgres.CreateContext())
        {
            Assert.False(await db.IngestRuns.AnyAsync(run => run.JobCode == "not-a-job"));
        }
    }

    // ---------------------------------------------------------------------------------
    // TC-MDF-043 — Backfill trigger carries the start date
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task TC_MDF_043_Backfill_trigger_carries_the_start_date()
    {
        await ResetAsync(clearPrices: true);
        StubPricesSources();

        await using var factory = PricesFactory();
        using var builder = await SignedInClientAsync(factory, BuilderEmail);

        var csrf = await CsrfTokenAsync(builder);
        using (var backfill = await PostAsync(
                   builder,
                   "/api/v1/admin/ingest/prices/run",
                   body: new { backfillFrom = "2016-01-01" },
                   csrf))
        {
            Assert.Equal(HttpStatusCode.OK, backfill.StatusCode);
        }

        var csrf2 = await CsrfTokenAsync(builder);
        using (var incremental = await PostAsync(builder, "/api/v1/admin/ingest/prices/run", body: null, csrf2))
        {
            Assert.Equal(HttpStatusCode.OK, incremental.StatusCode);
        }

        await using var db = _postgres.CreateContext();
        var runs = await db.IngestRuns
            .Where(run => run.JobCode == "prices")
            .OrderBy(run => run.Id)
            .ToListAsync();
        Assert.Equal(2, runs.Count);

        using (var backfillStats = JsonDocument.Parse(runs[0].StatsJson!))
        {
            Assert.Equal("backfill", backfillStats.RootElement.GetProperty("mode").GetString());
            Assert.Equal("2016-01-01", backfillStats.RootElement.GetProperty("backfillFrom").GetString());
        }

        using (var incrementalStats = JsonDocument.Parse(runs[1].StatsJson!))
        {
            Assert.Equal("incremental", incrementalStats.RootElement.GetProperty("mode").GetString());
            Assert.Equal(JsonValueKind.Null, incrementalStats.RootElement.GetProperty("backfillFrom").ValueKind);
        }
    }

    // ---------------------------------------------------------------------------------
    // TC-MDF-015 — Admin-triggered backfill ingests a historical range and records depth
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task TC_MDF_015_Admin_triggered_backfill_ingests_history_and_records_depth()
    {
        await ResetAsync(clearPrices: true);
        StubPricesSources();

        await using var factory = PricesFactory();
        using var builder = await SignedInClientAsync(factory, BuilderEmail);

        var csrf = await CsrfTokenAsync(builder);
        using (var response = await PostAsync(
                   builder,
                   "/api/v1/admin/ingest/prices/run",
                   body: new { backfillFrom = "2016-01-01" },
                   csrf))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await using var db = _postgres.CreateContext();

        // The run ledger row records the backfill mode and its start date.
        var run = await db.IngestRuns.SingleAsync(r => r.JobCode == "prices");
        using (var stats = JsonDocument.Parse(run.StatsJson!))
        {
            Assert.Equal("backfill", stats.RootElement.GetProperty("mode").GetString());
            Assert.Equal("2016-01-01", stats.RootElement.GetProperty("backfillFrom").GetString());
        }

        // History from the source's real limit forward is ingested; nothing before it.
        var sourceLimit = new DateOnly(2021, 1, 1);
        Assert.True(
            await db.DailyPrices.AnyAsync(price => price.PriceDate == sourceLimit),
            "rows at the source limit must be ingested");
        Assert.Equal(0, await db.DailyPrices.CountAsync(price => price.PriceDate < sourceLimit));

        // Coverage depth is recorded per instrument: available_from = earliest ingested date.
        var alfa = await db.Instruments.Where(i => i.Symbol == "ALFA").Select(i => i.Id).SingleAsync();
        var coverage = await db.CoverageMetadata.SingleAsync(row =>
            row.Scope == "instrument" && row.InstrumentId == alfa && row.DataType == CoverageDataTypes.Prices);
        Assert.Equal(sourceLimit, coverage.AvailableFrom);
        Assert.Equal(T, coverage.AvailableTo);
    }

    // ---------------------------------------------------------------------------------
    // TC-MDF-017 — Coverage report exposes per-instrument coverage and gaps
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task TC_MDF_017_Coverage_report_exposes_per_instrument_coverage_and_gaps()
    {
        await ResetAsync();

        await using (var db = _postgres.CreateContext())
        {
            // The no-silent-gaps recording side builds a row for every universe
            // instrument × data type (present depth or explicit gap).
            var recorder = new CoverageRecorder(db);
            await recorder.ReconcileUniverseAsync();

            // PART has no cash-flow statement → an explicit gap record.
            var part = await db.Instruments.Where(i => i.Symbol == "PART").Select(i => i.Id).SingleAsync();
            db.CoverageMetadata.Add(new CoverageMetadata
            {
                Scope = "instrument",
                InstrumentId = part,
                DataType = "cash_flow",
                AvailableFrom = null,
                AvailableTo = null,
                Notes = "Cash-flow statement absent; FCF-derived facts unavailable.",
            });
            await db.SaveChangesAsync();
        }

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var builder = await SignedInClientAsync(factory, BuilderEmail);
        using var response = await GetAsync(builder, "/api/v1/admin/coverage?scope=instrument");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var instruments = document.RootElement.GetProperty("instruments").EnumerateArray().ToList();

        // Every universe instrument is present (gaps shown, never omitted).
        Assert.Equal(16, instruments.Count);

        // Listing/IPO dates per FU §3.
        Assert.Equal("2010-01-04", Instrument(instruments, "ALFA").GetProperty("listingDate").GetString());
        Assert.Equal("2024-03-15", Instrument(instruments, "ZETA").GetProperty("listingDate").GetString());

        // Per-instrument data types carry availableFrom (seeded prices span T−9…T).
        var alfaPrices = DataType(Instrument(instruments, "ALFA"), "prices");
        Assert.True(alfaPrices.GetProperty("present").GetBoolean());
        Assert.Equal(L2.Anchor.TradingDay(-9).ToString("yyyy-MM-dd"), alfaPrices.GetProperty("availableFrom").GetString());

        // PART's missing CF statement is an explicit gap with a note.
        var partCashFlow = DataType(Instrument(instruments, "PART"), "cash_flow");
        Assert.False(partCashFlow.GetProperty("present").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(partCashFlow.GetProperty("notes").GetString()));

        // LAMDA pays no dividends → an explicit gap.
        var lamdaDividends = DataType(Instrument(instruments, "LAMDA"), "dividends");
        Assert.False(lamdaDividends.GetProperty("present").GetBoolean());

        // The unclassified instrument still appears with its coverage.
        Assert.True(Instrument(instruments, "UNSEC").GetProperty("dataTypes").GetArrayLength() > 0);
    }

    // ---------------------------------------------------------------------------------
    // TC-MDF-044 — Aggregate-only stats
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task TC_MDF_044_Aggregate_only_stats()
    {
        await ResetAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var builder = await SignedInClientAsync(factory, BuilderEmail);
        using var response = await GetAsync(builder, "/api/v1/admin/stats");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        // Exactly the aggregate shape — no per-user/per-account fields anywhere.
        Assert.Equal(
            new[] { "accounts", "dcfScenarios", "savedScreens" },
            root.EnumerateObject().Select(property => property.Name).OrderBy(name => name).ToArray());

        var accounts = root.GetProperty("accounts");
        Assert.Equal(
            new[] { "registered", "verified" },
            accounts.EnumerateObject().Select(property => property.Name).OrderBy(name => name).ToArray());

        // Values match the seed: 1 bootstrap builder + 4 fixture accounts, 4 verified.
        Assert.Equal(5, accounts.GetProperty("registered").GetInt32());
        Assert.Equal(4, accounts.GetProperty("verified").GetInt32());
        Assert.Equal(1, root.GetProperty("savedScreens").GetInt32());
        Assert.Equal(1, root.GetProperty("dcfScenarios").GetInt32());
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    /// <summary>Resets the shared container: re-applies the fixture universe and clears
    /// the mutable admin/ingestion tables each test owns.</summary>
    private async Task ResetAsync(bool clearPrices = false)
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM quarantined_facts");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM coverage_metadata");
        if (clearPrices)
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM daily_prices");
        }
    }

    private void StubPricesSources()
    {
        _wireMock.Server.ResetMappings();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesBackfillSourceLimited, path: PricesBackfillPath);
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesOk, path: PricesEodPath);
    }

    /// <summary>The API host with the <c>prices</c> source pointed at the WireMock double.</summary>
    private WebApplicationFactory<Program> PricesFactory() =>
        new DegerliAppFactory(_postgres.ConnectionString).WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.PostConfigure<PricesSourceOptions>(options =>
                {
                    options.BaseUrl = _wireMock.BaseUrl;
                    options.Path = PricesEodPath;
                    options.BackfillPath = PricesBackfillPath;
                })));

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    private static string NextClientIp() => Guid.NewGuid().ToString("N");

    /// <summary>Establishes a real session through the Identity sign-in pipeline (the
    /// path <c>POST /auth/login</c> uses) and returns a client carrying its cookie.</summary>
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
        request.Headers.Add("X-Forwarded-For", NextClientIp());
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, object? body, string? csrf)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("X-Forwarded-For", NextClientIp());
        if (csrf is not null)
        {
            request.Headers.Add("X-CSRF-Token", csrf);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request);
    }

    private static async Task<string> CsrfTokenAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/csrf-token");
        request.Headers.Add("X-Forwarded-For", NextClientIp());
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("token").GetString()!;
    }

    private static async Task<List<JsonElement>> RunsAsync(HttpClient client, string url)
    {
        using var response = await GetAsync(client, url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        // Clone so the elements outlive the disposed JsonDocument.
        return document.RootElement.GetProperty("runs").EnumerateArray()
            .Select(element => element.Clone())
            .ToList();
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static IngestRun Run(string jobCode, string status, DateTimeOffset finishedAt) => new()
    {
        JobCode = jobCode,
        Status = status,
        StartedAt = finishedAt.AddMinutes(-1),
        FinishedAt = finishedAt,
        StatsJson = "{}",
    };

    private static JsonElement Instrument(List<JsonElement> instruments, string symbol) =>
        instruments.Single(instrument => instrument.GetProperty("symbol").GetString() == symbol);

    private static JsonElement DataType(JsonElement instrument, string dataType) =>
        instrument.GetProperty("dataTypes").EnumerateArray()
            .Single(entry => entry.GetProperty("dataType").GetString() == dataType);
}
