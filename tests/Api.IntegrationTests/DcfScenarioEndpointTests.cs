using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;
using Degerli.Persistence.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-val-005 acceptance — DCF scenario CRUD
/// (<c>/api/v1/me/scenarios</c>, Group B of `.pipeline/testing/valuation-dcf.md`,
/// TC-VAL-013 / TC-VAL-014).
///
/// Runs against the real, migrated Testcontainers PostgreSQL seeded with the L2
/// fixture universe (ALFA and REST are covered stocks) through the in-process host.
/// Sessions are established through the real Identity sign-in pipeline; the verified
/// gate is exercised with both an unverified and a verified account created by the
/// shared <see cref="FixtureBuilder"/>.
/// </summary>
public sealed class DcfScenarioEndpointTests : IClassFixture<PostgresFixture>
{
    private const string Password = "ValidPass123!";

    private static readonly FixtureSet Unused = FixtureUniverse.Build(FixtureAnchor.L2);

    private readonly PostgresFixture _postgres;

    public DcfScenarioEndpointTests(PostgresFixture postgres) => _postgres = postgres;

    // -- TC-VAL-013 — Scenario CRUD (auth + verified gates, duplicate, ownership) --

    [Fact]
    public async Task TC_VAL_013_post_requires_auth_then_verification_then_succeeds()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);

        // Anonymous → 401 UNAUTHENTICATED (BR-VAL-010: only saving needs an account).
        using var anonymous = CreateClient(factory);
        using (var anonResponse = await PostScenarioAsync(anonymous, "ALFA", "Planım", Baseline()))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonResponse.StatusCode);
            var problem = await ReadProblemAsync(anonResponse);
            Assert.Equal("UNAUTHENTICATED", problem.RootElement.GetProperty("code").GetString());
        }

        // Signed-in but unverified → 403 EMAIL_NOT_VERIFIED (UXR-VAL-021).
        var unverifiedEmail = UniqueEmail("val013-unverified");
        await CreateUserAsync(factory, unverifiedEmail, verified: false);
        using var unverified = await CreateSignedInClientAsync(factory, unverifiedEmail);
        using (var unverifiedResponse = await PostScenarioAsync(unverified, "ALFA", "Planım", Baseline()))
        {
            Assert.Equal(HttpStatusCode.Forbidden, unverifiedResponse.StatusCode);
            var problem = await ReadProblemAsync(unverifiedResponse);
            Assert.Equal("EMAIL_NOT_VERIFIED", problem.RootElement.GetProperty("code").GetString());
        }

        // Verified → 201 and the row is persisted for the owner.
        var verifiedEmail = UniqueEmail("val013-verified");
        var userId = await CreateUserAsync(factory, verifiedEmail, verified: true);
        using var verified = await CreateSignedInClientAsync(factory, verifiedEmail);
        using (var created = await PostScenarioAsync(verified, "ALFA", "Planım", Baseline()))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        await using var db = _postgres.CreateContext();
        var scenario = await db.DcfScenarios.SingleAsync(s => s.UserId == userId);
        Assert.Equal("Planım", scenario.Name);

        // The unverified account never persisted anything.
        Assert.False(await db.DcfScenarios.AnyAsync(s => s.Name == "Planım" && s.UserId != userId));
    }

    [Fact]
    public async Task TC_VAL_013_duplicate_name_is_rejected_per_stock_and_cross_stock_is_allowed()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        var email = UniqueEmail("val013-dup");
        await CreateUserAsync(factory, email, verified: true);
        using var client = await CreateSignedInClientAsync(factory, email);

        using (var first = await PostScenarioAsync(client, "ALFA", "Temel", Baseline()))
        {
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        }

        // Duplicate name for the same (user, stock) → 409 DUPLICATE_NAME.
        var overrideParams = Baseline();
        overrideParams["discount_rate"] = 0.15m;
        using (var duplicate = await PostScenarioAsync(client, "ALFA", "Temel", overrideParams))
        {
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            var problem = await ReadProblemAsync(duplicate);
            Assert.Equal("DUPLICATE_NAME", problem.RootElement.GetProperty("code").GetString());
        }

        // Nothing overwritten: the stored params are still the first save's.
        using (var list = await GetScenariosAsync(client, "ALFA"))
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            var scenario = await SingleScenarioForAsync(list, "Temel");
            Assert.Equal(0.12m, scenario.Params.DiscountRate);
        }

        // The same name for a different stock is allowed (unique is per stock).
        using (var crossStock = await PostScenarioAsync(client, "REST", "Temel", Baseline()))
        {
            Assert.Equal(HttpStatusCode.Created, crossStock.StatusCode);
        }
    }

    [Fact]
    public async Task TC_VAL_013_get_list_rename_delete_and_ownership_isolation()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        var email = UniqueEmail("val013-owner");
        await CreateUserAsync(factory, email, verified: true);
        using var client = await CreateSignedInClientAsync(factory, email);

        long idA;
        using (var a = await PostScenarioAsync(client, "ALFA", "A", Baseline()))
        {
            Assert.Equal(HttpStatusCode.Created, a.StatusCode);
            idA = (await ReadCreatedAsync(a)).Id;
        }

        long idB;
        using (var b = await PostScenarioAsync(client, "ALFA", "B", Baseline()))
        {
            Assert.Equal(HttpStatusCode.Created, b.StatusCode);
            idB = (await ReadCreatedAsync(b)).Id;
        }

        using (var c = await PostScenarioAsync(client, "REST", "C", Baseline()))
        {
            Assert.Equal(HttpStatusCode.Created, c.StatusCode);
        }

        // GET ?symbol=ALFA returns that stock's scenarios only.
        using (var list = await GetScenariosAsync(client, "ALFA"))
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            var names = await NamesAsync(list);
            Assert.Equal(new[] { "A", "B" }, names.OrderBy(n => n, StringComparer.Ordinal).ToArray());
        }

        // Without a symbol filter, every stock's scenarios are returned (grouped client-side).
        using (var all = await GetScenariosAsync(client, symbol: null))
        {
            Assert.Equal(HttpStatusCode.OK, all.StatusCode);
            Assert.Equal(3, (await NamesAsync(all)).Count);
        }

        // Anonymous read → 401.
        using (var anonymous = CreateClient(factory))
        using (var anonList = await GetScenariosAsync(anonymous, "ALFA"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonList.StatusCode);
        }

        // Another user's scenario ids are invisible → 404 (ownership isolation).
        var otherEmail = UniqueEmail("val013-other");
        await CreateUserAsync(factory, otherEmail, verified: true);
        using var other = await CreateSignedInClientAsync(factory, otherEmail);
        using (var wrongRename = await PatchScenarioAsync(other, idA, "Hacked"))
        {
            Assert.Equal(HttpStatusCode.NotFound, wrongRename.StatusCode);
        }

        using (var wrongDelete = await DeleteScenarioAsync(other, idA))
        {
            Assert.Equal(HttpStatusCode.NotFound, wrongDelete.StatusCode);
        }

        // Rename collision within the same stock → 409, original retained.
        using (var collision = await PatchScenarioAsync(client, idB, "A"))
        {
            Assert.Equal(HttpStatusCode.Conflict, collision.StatusCode);
            var problem = await ReadProblemAsync(collision);
            Assert.Equal("DUPLICATE_NAME", problem.RootElement.GetProperty("code").GetString());
        }

        using (var list = await GetScenariosAsync(client, "ALFA"))
        {
            Assert.Contains("B", await NamesAsync(list));
        }

        // Successful rename is reflected for the owner.
        using (var renamed = await PatchScenarioAsync(client, idB, "B2"))
        {
            Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
            Assert.Equal("B2", (await ReadScenarioAsync(renamed)).Name);
        }

        // Delete → gone.
        using (var deleted = await DeleteScenarioAsync(client, idA))
        {
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        using (var list = await GetScenariosAsync(client, "ALFA"))
        {
            Assert.Equal(new[] { "B2" }, (await NamesAsync(list)).ToArray());
        }
    }

    // -- TC-VAL-014 — Scenario params round-trip exactly -----------------------

    [Fact]
    public async Task TC_VAL_014_saved_params_round_trip_exactly()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        var email = UniqueEmail("val014");
        await CreateUserAsync(factory, email, verified: true);
        using var client = await CreateSignedInClientAsync(factory, email);

        // Baseline except the discount-rate override (test plan TC-VAL-014).
        var saved = Baseline();
        saved["discount_rate"] = 0.15m;

        using (var created = await PostScenarioAsync(client, "ALFA", "Kendi modelim", saved))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        using var list = await GetScenariosAsync(client, "ALFA");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var scenario = await SingleScenarioForAsync(list, "Kendi modelim");

        // All eight parameters are present and equal exactly — including 0.15.
        Assert.Equal(8, scenario.ParamCount);
        Assert.Equal(100m, scenario.Params.BaseFcf);
        Assert.Equal(0.10m, scenario.Params.GrowthRate);
        Assert.Equal(5, scenario.Params.HorizonYears);
        Assert.Equal(0.03m, scenario.Params.TerminalGrowth);
        Assert.Equal(0.15m, scenario.Params.DiscountRate);
        Assert.Equal(400m, scenario.Params.Debt);
        Assert.Equal(200m, scenario.Params.Cash);
        Assert.Equal(100m, scenario.Params.ShareCount);
    }

    // ---------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------

    private async Task SeedAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, Unused);
    }

    private static Dictionary<string, object?> Baseline() => new()
    {
        ["base_fcf"] = 100m,
        ["growth_rate"] = 0.10m,
        ["horizon_years"] = 5,
        ["terminal_growth"] = 0.03m,
        ["discount_rate"] = 0.12m,
        ["debt"] = 400m,
        ["cash"] = 200m,
        ["share_count"] = 100m,
    };

    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@degerli.test";

    private static string NextClientIp() => Guid.NewGuid().ToString("N");

    private static HttpClient CreateClient(DegerliAppFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    /// <summary>Creates a user owned by this test and returns its id.</summary>
    private async Task<long> CreateUserAsync(DegerliAppFactory factory, string email, bool verified)
    {
        await using var db = _postgres.CreateContext();
        var builder = new FixtureBuilder(db);
        var user = await builder.CreateUserAsync(email, Password, emailConfirmed: verified);
        return user.Id;
    }

    /// <summary>
    /// Signs the account in through the real Identity sign-in pipeline and returns a
    /// client carrying the resulting auth cookie (mirrors TKT-acc-006's harness).
    /// </summary>
    private static async Task<HttpClient> CreateSignedInClientAsync(DegerliAppFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var signInManager = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();

        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);

        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        accessor.HttpContext = httpContext;

        var result = await signInManager.PasswordSignInAsync(user!, Password, isPersistent: false, lockoutOnFailure: true);
        Assert.True(result.Succeeded, "expected a fresh sign-in to succeed");

        var cookiePair = httpContext.Response.Headers.SetCookie.ToString().Split(';', 2)[0];
        Assert.StartsWith("degerli.auth=", cookiePair, StringComparison.Ordinal);

        var client = CreateClient(factory);
        client.DefaultRequestHeaders.Add("Cookie", cookiePair);
        return client;
    }

    private static async Task<HttpResponseMessage> PostScenarioAsync(
        HttpClient client,
        string symbol,
        string name,
        IDictionary<string, object?> parameters)
    {
        var csrf = await FetchCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/me/scenarios")
        {
            Content = JsonContent.Create(new { symbol, name, @params = parameters }),
        };
        request.Headers.Add("X-CSRF-Token", csrf);
        request.Headers.Add("X-Forwarded-For", NextClientIp());
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> GetScenariosAsync(HttpClient client, string? symbol)
    {
        var url = symbol is null
            ? "/api/v1/me/scenarios"
            : $"/api/v1/me/scenarios?symbol={symbol}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Forwarded-For", NextClientIp());
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PatchScenarioAsync(HttpClient client, long id, string name)
    {
        var csrf = await FetchCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/me/scenarios/{id}")
        {
            Content = JsonContent.Create(new { name }),
        };
        request.Headers.Add("X-CSRF-Token", csrf);
        request.Headers.Add("X-Forwarded-For", NextClientIp());
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> DeleteScenarioAsync(HttpClient client, long id)
    {
        var csrf = await FetchCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/me/scenarios/{id}");
        request.Headers.Add("X-CSRF-Token", csrf);
        request.Headers.Add("X-Forwarded-For", NextClientIp());
        return await client.SendAsync(request);
    }

    private static async Task<string> FetchCsrfTokenAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/csrf-token");
        request.Headers.Add("X-Forwarded-For", NextClientIp());
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("token").GetString()!;
    }

    private static async Task<JsonDocument> ReadProblemAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static async Task<CreatedScenario> ReadCreatedAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return ReadScenario(document.RootElement);
    }

    private static async Task<CreatedScenario> ReadScenarioAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return ReadScenario(document.RootElement);
    }

    private static CreatedScenario ReadScenario(JsonElement element)
        => new(
            element.GetProperty("id").GetInt64(),
            element.GetProperty("name").GetString()!);

    private static async Task<ScenarioView> SingleScenarioForAsync(HttpResponseMessage list, string name)
    {
        var scenarios = await ScenariosAsync(list);
        return scenarios.Single(s => s.Name == name);
    }

    private static async Task<List<string>> NamesAsync(HttpResponseMessage list)
        => (await ScenariosAsync(list)).Select(s => s.Name).ToList();

    private static async Task<List<ScenarioView>> ScenariosAsync(HttpResponseMessage list)
    {
        using var document = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        return document.RootElement
            .GetProperty("scenarios")
            .EnumerateArray()
            .Select(ReadScenarioView)
            .ToList();
    }

    private static ScenarioView ReadScenarioView(JsonElement element)
    {
        var parameters = element.GetProperty("params");
        return new ScenarioView(
            element.GetProperty("id").GetInt64(),
            element.GetProperty("symbol").GetString()!,
            element.GetProperty("name").GetString()!,
            parameters.EnumerateObject().Count(),
            new ParameterView(
                parameters.GetProperty("base_fcf").GetDecimal(),
                parameters.GetProperty("growth_rate").GetDecimal(),
                parameters.GetProperty("horizon_years").GetInt32(),
                parameters.GetProperty("terminal_growth").GetDecimal(),
                parameters.GetProperty("discount_rate").GetDecimal(),
                parameters.GetProperty("debt").GetDecimal(),
                parameters.GetProperty("cash").GetDecimal(),
                parameters.GetProperty("share_count").GetDecimal()));
    }

    private sealed record CreatedScenario(long Id, string Name);
    private sealed record ScenarioView(long Id, string Symbol, string Name, int ParamCount, ParameterView Params);
    private sealed record ParameterView(
        decimal BaseFcf,
        decimal GrowthRate,
        int HorizonYears,
        decimal TerminalGrowth,
        decimal DiscountRate,
        decimal Debt,
        decimal Cash,
        decimal ShareCount);
}
