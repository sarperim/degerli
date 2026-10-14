using System.Net;
using System.Text;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Metrics;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-scr-005 acceptance — metric visibility: the builder-only admin PATCH that
/// hides/unhides a screener metric without a code change, and the behaviors the
/// hidden state drives (Group C of <c>.pipeline/testing/stock-screening.md</c>,
/// TC-SCR-007/018/020; FR-SCR-017, UC-SCR-004, UXR-MDF-023):
/// <list type="bullet">
/// <item><b>TC-SCR-020</b> — as builder, <c>PATCH /api/v1/admin/screener-metrics/roe</c>
/// <c>{isScreenable:false}</c> makes the anonymous catalog exclude ROE; an affected
/// saved screen re-run drops it; a non-builder PATCH is <c>403 FORBIDDEN</c>; toggling
/// back restores everything.</item>
/// <item><b>TC-SCR-007</b> — with ROE hidden, an <i>ad-hoc</i> run naming it (or an
/// unknown metric) is <c>400 METRIC_NOT_AVAILABLE</c>; the catalog excludes ROE.</item>
/// <item><b>TC-SCR-018</b> — a saved screen referencing a since-hidden metric re-runs
/// <i>without</i> it, naming it in <c>droppedCriteria</c>; after un-hiding, the re-run
/// includes it again with an empty <c>droppedCriteria</c>.</item>
/// </list>
/// Runs against the real migrated Testcontainers PostgreSQL seeded with the L2 fixture
/// universe and the canonical metrics at T; sessions are established through the real
/// Identity sign-in pipeline. Goldens are the FU-derived values the test plan §1 pins.
/// </summary>
public sealed class ScreenerMetricVisibilityTests : IClassFixture<PostgresFixture>
{
    private const string BuilderEmail = "builder@degerli.test";
    private const string BuilderPassword = "FixturePass1!";
    private const string UserAEmail = "user-a@degerli.test";
    private const string UserAPassword = "FixturePass1!";

    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    private readonly PostgresFixture _postgres;

    public ScreenerMetricVisibilityTests(PostgresFixture postgres) => _postgres = postgres;

    // ---------------------------------------------------------------------------
    // TC-SCR-020 — Hide/unhide metric without code change (API)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task TC_SCR_020_hide_unhide_metric_without_code_change()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var builder = await SignedInClientAsync(factory, BuilderEmail, BuilderPassword);
        using var user = await SignedInClientAsync(factory, UserAEmail, UserAPassword);
        using var anonymous = CreateClient(factory);

        // The affected saved screen: user-a's fixture screen ("Ekranım") is pe≤15 AND roe≥0.15.
        var screenId = await FixtureScreenIdAsync(user, "Ekranım");
        var criteriaJson = await FixtureScreenCriteriaJsonAsync(user, "Ekranım");

        // --- builder hides ROE → 200, the served state flips.
        using (var hidden = await PatchMetricAsync(builder, "roe", isScreenable: false))
        {
            Assert.Equal(HttpStatusCode.OK, hidden.StatusCode);
            using var document = JsonDocument.Parse(await hidden.Content.ReadAsStringAsync());
            Assert.Equal("roe", document.RootElement.GetProperty("metricCode").GetString());
            Assert.False(document.RootElement.GetProperty("isScreenable").GetBoolean());
        }

        // --- the anonymous catalog excludes ROE (UXR-SCR-011) — 17 of the 18 concepts.
        var visible = await CatalogCodesAsync(anonymous);
        Assert.DoesNotContain("roe", visible);
        Assert.Equal(17, visible.Count);

        // --- the affected saved screen re-run drops ROE and proceeds (TC-SCR-018 mechanism).
        using (var rerun = await RerunAsync(user, screenId, criteriaJson))
        {
            Assert.Equal(HttpStatusCode.OK, rerun.StatusCode);
            using var document = JsonDocument.Parse(await rerun.Content.ReadAsStringAsync());
            var root = document.RootElement;
            Assert.Equal(new[] { "roe" }, DroppedCriteria(root));
            Assert.Equal(new[] { "ALFA", "REST", "UNSEC" }, Symbols(root));
        }

        // --- a non-builder PATCH is rejected by the admin role gate.
        using (var forbidden = await PatchMetricAsync(user, "roe", isScreenable: false))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            Assert.Equal("FORBIDDEN", await CodeAsync(forbidden));
        }

        // --- the anonymous catalog is public prose only (no session is established).
        Assert.False((await CatalogResponseAsync(anonymous)).Headers.Contains("Set-Cookie"));

        // --- toggle back → catalog restores, the re-run includes ROE with no drops.
        using (var restored = await PatchMetricAsync(builder, "roe", isScreenable: true))
        {
            Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
            Assert.True(
                JsonDocument.Parse(await restored.Content.ReadAsStringAsync())
                    .RootElement.GetProperty("isScreenable").GetBoolean());
        }

        var restoredCodes = await CatalogCodesAsync(anonymous);
        Assert.Contains("roe", restoredCodes);
        Assert.Equal(18, restoredCodes.Count);

        using (var rerun = await RerunAsync(user, screenId, criteriaJson))
        {
            Assert.Equal(HttpStatusCode.OK, rerun.StatusCode);
            using var document = JsonDocument.Parse(await rerun.Content.ReadAsStringAsync());
            var root = document.RootElement;
            Assert.Empty(DroppedCriteria(root));
            Assert.Equal(new[] { "ALFA", "REST", "UNSEC" }, Symbols(root));
        }
    }

    // ---------------------------------------------------------------------------
    // TC-SCR-007 — Hidden or unknown metric rejected (ad-hoc)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task TC_SCR_007_hidden_or_unknown_metric_rejected_ad_hoc()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var builder = await SignedInClientAsync(factory, BuilderEmail, BuilderPassword);
        using var anonymous = CreateClient(factory);

        // Precondition: ROE hidden via the admin toggle (TC-SCR-020 state).
        using (var hidden = await PatchMetricAsync(builder, "roe", isScreenable: false))
        {
            Assert.Equal(HttpStatusCode.OK, hidden.StatusCode);
        }

        // Ad-hoc run naming the hidden metric → the dedicated availability error.
        using (var hiddenRun = await RunAsync(anonymous,
                   """{ "criteria": [ { "metricCode": "roe", "bound": "min", "minValue": 0.15 } ] }"""))
        {
            Assert.Equal(HttpStatusCode.BadRequest, hiddenRun.StatusCode);
            Assert.Equal("METRIC_NOT_AVAILABLE", await CodeAsync(hiddenRun));
        }

        // Ad-hoc run naming an unknown metric → the same error.
        using (var unknownRun = await RunAsync(anonymous,
                   """{ "criteria": [ { "metricCode": "not_a_metric", "bound": "max", "maxValue": 1 } ] }"""))
        {
            Assert.Equal(HttpStatusCode.BadRequest, unknownRun.StatusCode);
            Assert.Equal("METRIC_NOT_AVAILABLE", await CodeAsync(unknownRun));
        }

        // The catalog also excludes ROE in this state.
        Assert.DoesNotContain("roe", await CatalogCodesAsync(anonymous));

        // A still-visible metric keeps working ad-hoc (the error is availability, not the surface).
        using (var ok = await RunAsync(anonymous,
                   """{ "criteria": [ { "metricCode": "pe", "bound": "max", "maxValue": 15 } ] }"""))
        {
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }
    }

    // ---------------------------------------------------------------------------
    // TC-SCR-018 — Re-run with stored criteria; dropped criterion on hidden metric
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task TC_SCR_018_rerun_drops_criterion_for_hidden_metric_and_restores_it()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var builder = await SignedInClientAsync(factory, BuilderEmail, BuilderPassword);
        using var user = await SignedInClientAsync(factory, UserAEmail, UserAPassword);

        // Precondition: a saved screen containing ROE. roe≥0.21 keeps only UNSEC when
        // ROE is applied, while the pe-only bound keeps ALFA/REST/UNSEC — so the drop
        // is observable in the results, not only in droppedCriteria.
        const string criteriaJson =
            """[ { "metricCode": "pe", "bound": "max", "maxValue": 15 }, { "metricCode": "roe", "bound": "min", "minValue": 0.21 } ]""";
        long screenId;
        using (var created = await PostScreenAsync(user, "Gizli ölçüt", criteriaJson))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            using var document = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
            screenId = document.RootElement.GetProperty("id").GetInt64();
        }

        // ROE is visible → the re-run applies it (UNSEC only) with no drops.
        using (var before = await RerunAsync(user, screenId, criteriaJson))
        {
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);
            using var document = JsonDocument.Parse(await before.Content.ReadAsStringAsync());
            Assert.Empty(DroppedCriteria(document.RootElement));
            Assert.Equal(new[] { "UNSEC" }, Symbols(document.RootElement));
        }

        // ROE then hidden via the admin toggle.
        using (var hidden = await PatchMetricAsync(builder, "roe", isScreenable: false))
        {
            Assert.Equal(HttpStatusCode.OK, hidden.StatusCode);
        }

        // Re-run proceeds without ROE: droppedCriteria names it; results match pe-only.
        using (var dropped = await RerunAsync(user, screenId, criteriaJson))
        {
            Assert.Equal(HttpStatusCode.OK, dropped.StatusCode);
            using var document = JsonDocument.Parse(await dropped.Content.ReadAsStringAsync());
            var root = document.RootElement;
            Assert.Equal(new[] { "roe" }, DroppedCriteria(root));
            Assert.Equal(new[] { "ALFA", "REST", "UNSEC" }, Symbols(root));
        }

        // After un-hiding, the re-run includes ROE again with an empty droppedCriteria.
        using (var restored = await PatchMetricAsync(builder, "roe", isScreenable: true))
        {
            Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        }

        using (var after = await RerunAsync(user, screenId, criteriaJson))
        {
            Assert.Equal(HttpStatusCode.OK, after.StatusCode);
            using var document = JsonDocument.Parse(await after.Content.ReadAsStringAsync());
            var root = document.RootElement;
            Assert.Empty(DroppedCriteria(root));
            Assert.Equal(new[] { "UNSEC" }, Symbols(root));
        }
    }

    // ---------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Seeds the L2 fixture universe (which creates the builder and user accounts),
    /// restores the seeded metric catalog to fully visible, then computes the canonical
    /// metrics at T through the real Metrics Engine job. The shared container is reused
    /// across the class's tests, so the mutable visibility flag is reset each time.
    /// </summary>
    private async Task SeedAsync()
    {
        await using (var db = _postgres.CreateContext())
        {
            await FixtureSeeder.ApplyAsync(db, L2);
            await db.Database.ExecuteSqlRawAsync("UPDATE metric_catalog SET is_screenable = true");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM derived_metrics");
        }

        var clock = new FakeTimeProvider(new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
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
        var result = await runner.RunAsync(MetricsRecomputeJob.Code, new IngestionRequest(T));
        Assert.Equal("succeeded", result.Status);
    }

    private async Task<long> FixtureScreenIdAsync(HttpClient user, string name)
    {
        using var document = await ReadScreensAsync(user);
        return document.RootElement.GetProperty("screens").EnumerateArray()
            .Single(screen => screen.GetProperty("name").GetString() == name)
            .GetProperty("id").GetInt64();
    }

    private async Task<string> FixtureScreenCriteriaJsonAsync(HttpClient user, string name)
    {
        using var document = await ReadScreensAsync(user);
        return document.RootElement.GetProperty("screens").EnumerateArray()
            .Single(screen => screen.GetProperty("name").GetString() == name)
            .GetProperty("criteria").GetRawText();
    }

    private static async Task<JsonDocument> ReadScreensAsync(HttpClient user)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me/screens");
        request.Headers.Add("X-Forwarded-For", NextClientIp());
        using var response = await user.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// A saved-screen re-run: names the screen (so the server drops a since-hidden
    /// criterion) and ships the stored criteria, exactly as SCR-004's re-run does.
    /// </summary>
    private static Task<HttpResponseMessage> RerunAsync(HttpClient client, long screenId, string criteriaJson)
        => RunAsync(client, $$"""{ "savedScreenId": {{screenId}}, "criteria": {{criteriaJson}} }""");

    private static async Task<HttpResponseMessage> PostScreenAsync(HttpClient client, string name, string criteriaJson)
    {
        var csrf = await FetchCsrfTokenAsync(client);
        var body = $$"""{"name":{{JsonSerializer.Serialize(name)}},"criteria":{{criteriaJson}}}""";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/me/screens")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-CSRF-Token", csrf);
        request.Headers.Add("X-Forwarded-For", NextClientIp());
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> RunAsync(HttpClient client, string json)
    {
        var csrf = await FetchCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/screener/run")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-CSRF-Token", csrf);
        request.Headers.Add("X-Forwarded-For", NextClientIp());
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PatchMetricAsync(HttpClient client, string code, bool isScreenable)
    {
        var csrf = await FetchCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"/api/v1/admin/screener-metrics/{code}")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { isScreenable }),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Add("X-CSRF-Token", csrf);
        request.Headers.Add("X-Forwarded-For", NextClientIp());
        return await client.SendAsync(request);
    }

    private static async Task<List<string>> CatalogCodesAsync(HttpClient client)
    {
        using var response = await CatalogResponseAsync(client);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("families").EnumerateArray()
            .SelectMany(family => family.GetProperty("metrics").EnumerateArray())
            .Select(metric => metric.GetProperty("metricCode").GetString()!)
            .ToList();
    }

    private static async Task<HttpResponseMessage> CatalogResponseAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/screener/metrics");
        request.Headers.Add("X-Forwarded-For", NextClientIp());
        return await client.SendAsync(request);
    }

    private static string[] DroppedCriteria(JsonElement root)
        => root.GetProperty("droppedCriteria").EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();

    private static string[] Symbols(JsonElement root)
        => root.GetProperty("rows").EnumerateArray()
            .Select(row => row.GetProperty("symbol").GetString()!)
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();

    private static async Task<string> FetchCsrfTokenAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/csrf-token");
        request.Headers.Add("X-Forwarded-For", NextClientIp());
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("token").GetString()!;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static string NextClientIp() => Guid.NewGuid().ToString("N");

    private static HttpClient CreateClient(DegerliAppFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    /// <summary>Signs in through the real Identity pipeline and returns a client carrying
    /// the resulting auth cookie (mirrors the other screener/admin suites).</summary>
    private static async Task<HttpClient> SignedInClientAsync(
        DegerliAppFactory factory,
        string email,
        string password)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var signInManager = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();

        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);

        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        accessor.HttpContext = httpContext;

        var result = await signInManager.PasswordSignInAsync(user!, password, isPersistent: false, lockoutOnFailure: true);
        Assert.True(result.Succeeded, $"expected a sign-in for {email} to succeed");

        var cookiePair = httpContext.Response.Headers.SetCookie.ToString().Split(';', 2)[0];
        Assert.StartsWith("degerli.auth=", cookiePair, StringComparison.Ordinal);

        var client = CreateClient(factory);
        client.DefaultRequestHeaders.Add("Cookie", cookiePair);
        return client;
    }
}
