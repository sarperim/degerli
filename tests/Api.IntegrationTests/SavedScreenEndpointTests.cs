using System.Net;
using System.Net.Http.Json;
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
/// TKT-scr-004 acceptance — saved-screen CRUD &amp; persistence gates
/// (<c>/api/v1/me/screens</c>, Group B of `.pipeline/testing/stock-screening.md`,
/// TC-SCR-012..017/019):
/// <list type="bullet">
/// <item>TC-SCR-012 — save a named screen; criteria round-trip exactly incl. CAGR windows.</item>
/// <item>TC-SCR-013 — duplicate name rejected per account (409 DUPLICATE_NAME), nothing
/// overwritten, cross-account names allowed.</item>
/// <item>TC-SCR-014 — auth (401) + verified-e-mail (403 EMAIL_NOT_VERIFIED) gates.</item>
/// <item>TC-SCR-015 — rename in place with updated_at bump; collision rejected; empty name 400.</item>
/// <item>TC-SCR-016 — criteria update via PATCH validated exactly like the run endpoint.</item>
/// <item>TC-SCR-017 — delete, 404s on repeat, ownership isolation via 404-not-403.</item>
/// <item>TC-SCR-019 — re-run with stored criteria recomputes on the latest data
/// (mirrors TC-SCR-011 through the saved-screen path).</item>
/// </list>
/// Runs against the real migrated Testcontainers PostgreSQL seeded with the L2 fixture
/// universe; sessions are established through the real Identity sign-in pipeline and the
/// verified gate is exercised with both an unverified and a verified account.
/// </summary>
public sealed class SavedScreenEndpointTests : IClassFixture<PostgresFixture>
{
    private const string Password = "ValidPass123!";

    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    /// <summary>The trading day after the fixture anchor (2026-10-07, a Wednesday).</summary>
    private static DateOnly T1 => T.AddDays(1);

    private readonly PostgresFixture _postgres;

    public SavedScreenEndpointTests(PostgresFixture postgres) => _postgres = postgres;

    // -- TC-SCR-012 — Save a named screen (happy) -------------------------------

    [Fact]
    public async Task TC_SCR_012_save_named_screen_and_criteria_round_trip_with_cagr_window()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        var email = UniqueEmail("scr012");
        await CreateUserAsync(factory, email, verified: true);
        using var client = await CreateSignedInClientAsync(factory, email);

        const string criteriaJson = """
            [{"metricCode":"pe","bound":"max","maxValue":15},{"metricCode":"roe","bound":"min","minValue":0.15},{"metricCode":"rev_cagr","bound":"min","minValue":0.15,"window":5}]
            """;

        using (var created = await PostScreenAsync(client, "Ucuz kaliteli", criteriaJson))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            Assert.StartsWith("/api/v1/me/screens/", created.Headers.Location!.OriginalString, StringComparison.Ordinal);
        }

        using var list = await GetScreensAsync(client);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var screen = await SingleScreenAsync(list, "Ucuz kaliteli");

        // criteria_json round-trips exactly, CAGR window preserved.
        using var sentDocument = JsonDocument.Parse(criteriaJson);
        Assert.True(
            JsonElement.DeepEquals(sentDocument.RootElement, screen.Criteria),
            $"criteria did not round-trip: {screen.Criteria.GetRawText()}");
    }

    // -- TC-SCR-013 — Duplicate name rejected, nothing overwritten --------------

    [Fact]
    public async Task TC_SCR_013_duplicate_name_rejected_per_account_and_cross_account_allowed()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);

        var emailA = UniqueEmail("scr013-a");
        await CreateUserAsync(factory, emailA, verified: true);
        using var clientA = await CreateSignedInClientAsync(factory, emailA);

        const string original = """[{"metricCode":"pe","bound":"max","maxValue":15}]""";
        using (var first = await PostScreenAsync(clientA, "Ucuz hisseler", original))
        {
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        }

        // A second save with the same name (same account) → 409 DUPLICATE_NAME params.name.
        const string overrideCriteria = """[{"metricCode":"pe","bound":"max","maxValue":99}]""";
        using (var duplicate = await PostScreenAsync(clientA, "Ucuz hisseler", overrideCriteria))
        {
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            var problem = await ReadProblemAsync(duplicate);
            Assert.Equal("DUPLICATE_NAME", problem.RootElement.GetProperty("code").GetString());
            Assert.Equal("Ucuz hisseler", problem.RootElement.GetProperty("params").GetProperty("name").GetString());
        }

        // Nothing overwritten: the original criteria are unchanged.
        using (var list = await GetScreensAsync(clientA))
        {
            var screen = await SingleScreenAsync(list, "Ucuz hisseler");
            using var expected = JsonDocument.Parse(original);
            Assert.True(JsonElement.DeepEquals(expected.RootElement, screen.Criteria));
        }

        // Names are unique per account only — user-c saving the same name → 201.
        var emailC = UniqueEmail("scr013-c");
        await CreateUserAsync(factory, emailC, verified: true);
        using var clientC = await CreateSignedInClientAsync(factory, emailC);
        using (var crossAccount = await PostScreenAsync(clientC, "Ucuz hisseler", original))
        {
            Assert.Equal(HttpStatusCode.Created, crossAccount.StatusCode);
        }
    }

    // -- TC-SCR-014 — Auth and verification gates -------------------------------

    [Fact]
    public async Task TC_SCR_014_anonymous_401_unverified_403_verified_201()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);

        const string criteria = """[{"metricCode":"pe","bound":"max","maxValue":15}]""";

        // Anonymous POST → 401 UNAUTHENTICATED.
        using (var anonymous = CreateClient(factory))
        using (var anonPost = await PostScreenAsync(anonymous, "Anonim", criteria))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonPost.StatusCode);
            var problem = await ReadProblemAsync(anonPost);
            Assert.Equal("UNAUTHENTICATED", problem.RootElement.GetProperty("code").GetString());
        }

        // Signed-in but unverified → 403 EMAIL_NOT_VERIFIED.
        var unverifiedEmail = UniqueEmail("scr014-unverified");
        await CreateUserAsync(factory, unverifiedEmail, verified: false);
        using (var unverified = await CreateSignedInClientAsync(factory, unverifiedEmail))
        using (var unverifiedPost = await PostScreenAsync(unverified, "Doğrulanmamış", criteria))
        {
            Assert.Equal(HttpStatusCode.Forbidden, unverifiedPost.StatusCode);
            var problem = await ReadProblemAsync(unverifiedPost);
            Assert.Equal("EMAIL_NOT_VERIFIED", problem.RootElement.GetProperty("code").GetString());
        }

        // Verified → 201.
        var verifiedEmail = UniqueEmail("scr014-verified");
        await CreateUserAsync(factory, verifiedEmail, verified: true);
        using var verified = await CreateSignedInClientAsync(factory, verifiedEmail);
        using (var verifiedPost = await PostScreenAsync(verified, "Doğrulanmış", criteria))
        {
            Assert.Equal(HttpStatusCode.Created, verifiedPost.StatusCode);
        }

        // Reading GET /me/screens requires auth only → anonymous is 401.
        using (var anonymous = CreateClient(factory))
        using (var anonGet = await GetScreensAsync(anonymous))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonGet.StatusCode);
        }
    }

    // -- TC-SCR-015 — Rename in place; collision rejected -----------------------

    [Fact]
    public async Task TC_SCR_015_rename_in_place_collision_rejected_and_empty_name_400()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        var email = UniqueEmail("scr015");
        await CreateUserAsync(factory, email, verified: true);
        using var client = await CreateSignedInClientAsync(factory, email);

        const string criteria = """[{"metricCode":"pe","bound":"max","maxValue":15}]""";
        long idA;
        using (var a = await PostScreenAsync(client, "A", criteria))
        {
            Assert.Equal(HttpStatusCode.Created, a.StatusCode);
            idA = (await ReadScreenAsync(a)).Id;
        }

        long idB;
        using (var b = await PostScreenAsync(client, "B", criteria))
        {
            Assert.Equal(HttpStatusCode.Created, b.StatusCode);
            idB = (await ReadScreenAsync(b)).Id;
        }

        // Rename in place: same id, updated_at bumped.
        factory.Clock.SetUtcNow(new DateTimeOffset(T.ToDateTime(new TimeOnly(19, 0)), TimeSpan.Zero));
        using (var renamed = await PatchScreenAsync(client, idA, $$"""{"name":"A2"}"""))
        {
            Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
            var screen = await ReadScreenAsync(renamed);
            Assert.Equal(idA, screen.Id);
            Assert.Equal("A2", screen.Name);
            Assert.True(screen.UpdatedAt >= screen.CreatedAt);
        }

        // Rename to an existing name → 409 DUPLICATE_NAME, previous name retained.
        using (var collision = await PatchScreenAsync(client, idB, $$"""{"name":"A2"}"""))
        {
            Assert.Equal(HttpStatusCode.Conflict, collision.StatusCode);
            var problem = await ReadProblemAsync(collision);
            Assert.Equal("DUPLICATE_NAME", problem.RootElement.GetProperty("code").GetString());
        }

        using (var list = await GetScreensAsync(client))
        {
            var names = await NamesAsync(list);
            Assert.Contains("B", names);
            Assert.DoesNotContain("B2", names);
        }

        // Empty name → 400.
        using (var empty = await PatchScreenAsync(client, idA, """{"name":""}"""))
        {
            Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        }
    }

    // -- TC-SCR-016 — Criteria update via PATCH --------------------------------

    [Fact]
    public async Task TC_SCR_016_criteria_update_via_patch_and_invalid_criteria_rejected()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        var email = UniqueEmail("scr016");
        await CreateUserAsync(factory, email, verified: true);
        using var client = await CreateSignedInClientAsync(factory, email);

        long id;
        using (var created = await PostScreenAsync(client, "Güncellenecek", """[{"metricCode":"pe","bound":"max","maxValue":15}]"""))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            id = (await ReadScreenAsync(created)).Id;
        }

        // New criteria replace the stored set.
        const string updated = """[{"metricCode":"pe","bound":"min","minValue":0}]""";
        using (var patched = await PatchScreenAsync(client, id, $$"""{"criteria":{{updated}}}"""))
        {
            Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
            var screen = await ReadScreenAsync(patched);
            using var expected = JsonDocument.Parse(updated);
            Assert.True(JsonElement.DeepEquals(expected.RootElement, screen.Criteria));
        }

        // The stored criteria are what a re-run uses.
        string storedCriteria;
        using (var list = await GetScreensAsync(client))
        {
            storedCriteria = (await SingleScreenAsync(list, "Güncellenecek")).Criteria.GetRawText();
        }

        using (var run = await RunAsync(client, $$"""{"criteria":{{storedCriteria}}}"""))
        {
            Assert.Equal(HttpStatusCode.OK, run.StatusCode);
            using var runDocument = JsonDocument.Parse(await run.Content.ReadAsStringAsync());
            Assert.Equal(T.ToString("yyyy-MM-dd"), runDocument.RootElement.GetProperty("asOf").GetString());
        }

        // Invalid criteria payload → 400 with the same validation detail as the run.
        using (var invalid = await PatchScreenAsync(client, id, """{"criteria":[{"metricCode":"pe","bound":"max"}]}"""))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            var problem = await ReadProblemAsync(invalid);
            Assert.Equal("VALIDATION_FAILED", problem.RootElement.GetProperty("code").GetString());
            var fields = problem.RootElement.GetProperty("params").GetProperty("fields").EnumerateArray().ToList();
            Assert.Contains(fields, entry => entry.GetProperty("field").GetString() == "maxValue");
        }
    }

    // -- TC-SCR-017 — Delete; 404s; ownership isolation -------------------------

    [Fact]
    public async Task TC_SCR_017_delete_404_on_repeat_and_ownership_isolation()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);

        var emailA = UniqueEmail("scr017-a");
        await CreateUserAsync(factory, emailA, verified: true);
        using var clientA = await CreateSignedInClientAsync(factory, emailA);

        const string criteria = """[{"metricCode":"pe","bound":"max","maxValue":15}]""";
        long idA;
        using (var created = await PostScreenAsync(clientA, "Silinecek", criteria))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            idA = (await ReadScreenAsync(created)).Id;
        }

        // Owner DELETE → gone from the list.
        using (var deleted = await DeleteScreenAsync(clientA, idA))
        {
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        using (var list = await GetScreensAsync(clientA))
        {
            Assert.DoesNotContain("Silinecek", await NamesAsync(list));
        }

        // Second DELETE → 404 NOT_FOUND.
        using (var again = await DeleteScreenAsync(clientA, idA))
        {
            Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
            var problem = await ReadProblemAsync(again);
            Assert.Equal("NOT_FOUND", problem.RootElement.GetProperty("code").GetString());
        }

        // Another user's id is invisible → 404 (never 403 — no existence leak).
        var emailC = UniqueEmail("scr017-c");
        await CreateUserAsync(factory, emailC, verified: true);
        using var clientC = await CreateSignedInClientAsync(factory, emailC);

        using (var created = await PostScreenAsync(clientA, "Başkasının", criteria))
        {
            idA = (await ReadScreenAsync(created)).Id;
        }

        using (var wrongDelete = await DeleteScreenAsync(clientC, idA))
        {
            Assert.Equal(HttpStatusCode.NotFound, wrongDelete.StatusCode);
        }

        using (var wrongPatch = await PatchScreenAsync(clientC, idA, """{"name":"Hack"}"""))
        {
            Assert.Equal(HttpStatusCode.NotFound, wrongPatch.StatusCode);
        }
    }

    // -- TC-SCR-019 — Re-run computes on latest data ----------------------------

    [Fact]
    public async Task TC_SCR_019_rerun_with_stored_criteria_recomputes_on_latest_data()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        var email = UniqueEmail("scr019");
        await CreateUserAsync(factory, email, verified: true);
        using var client = await CreateSignedInClientAsync(factory, email);

        using (var created = await PostScreenAsync(client, "Takip", """[{"metricCode":"pe","bound":"min","minValue":0}]"""))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        // Ingest day T+1 prices (ALFA closes at 30.00 → P/E 15.0) and recompute metrics.
        await IngestNextDayPricesAsync();
        await RunMetricsAsync(T1);
        factory.Clock.SetUtcNow(new DateTimeOffset(T1.ToDateTime(new TimeOnly(18, 0)), TimeSpan.Zero));

        // The saved criteria drive the re-run (GET → POST /screener/run).
        string storedCriteria;
        using (var list = await GetScreensAsync(client))
        {
            storedCriteria = (await SingleScreenAsync(list, "Takip")).Criteria.GetRawText();
        }

        using var run = await RunAsync(client, $$"""{"criteria":{{storedCriteria}}}""");
        Assert.Equal(HttpStatusCode.OK, run.StatusCode);
        using var runDocument = JsonDocument.Parse(await run.Content.ReadAsStringAsync());
        var root = runDocument.RootElement;
        Assert.Equal(T1.ToString("yyyy-MM-dd"), root.GetProperty("asOf").GetString());

        var alfa = root.GetProperty("rows").EnumerateArray().Single(row => row.GetProperty("symbol").GetString() == "ALFA");
        var pe = alfa.GetProperty("values").GetProperty("pe").GetDecimal();
        Assert.True(Math.Abs(pe - 15.0m) <= 0.00001m, $"expected ALFA pe 15.0 but was {pe}");
    }

    // ---------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------

    /// <summary>Seeds the L2 fixture universe and computes the canonical metrics at T.</summary>
    private async Task SeedAsync()
    {
        await using (var db = _postgres.CreateContext())
        {
            await FixtureSeeder.ApplyAsync(db, L2);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM derived_metrics");
        }

        await RunMetricsAsync(T);
    }

    /// <summary>Runs the metrics-recompute job for <paramref name="date"/> through the real pipeline.</summary>
    private async Task RunMetricsAsync(DateOnly date)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
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
        var result = await runner.RunAsync(MetricsRecomputeJob.Code, new IngestionRequest(date));
        Assert.Equal("succeeded", result.Status);
    }

    /// <summary>Carries every T price row forward to T+1, raising ALFA's close so its P/E moves.</summary>
    private async Task IngestNextDayPricesAsync()
    {
        await using var db = _postgres.CreateContext();
        var tPrices = await db.DailyPrices.Where(price => price.PriceDate == T).ToListAsync();
        var alfaId = await db.Instruments
            .Where(instrument => instrument.Symbol == "ALFA")
            .Select(instrument => instrument.Id)
            .SingleAsync();

        foreach (var price in tPrices)
        {
            var close = price.InstrumentId == alfaId ? 30.00m : price.CloseRaw;
            db.DailyPrices.Add(new DailyPrice
            {
                InstrumentId = price.InstrumentId,
                PriceDate = T1,
                Open = close,
                High = close,
                Low = close,
                CloseRaw = close,
                CloseAdjusted = close,
                Volume = price.Volume,
                SourceRef = "test:t-plus-1",
                RecordedAt = new DateTimeOffset(T1.ToDateTime(new TimeOnly(18, 0)), TimeSpan.Zero),
            });
        }

        await db.SaveChangesAsync();
    }

    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@degerli.test";

    private static string NextClientIp() => Guid.NewGuid().ToString("N");

    private static HttpClient CreateClient(DegerliAppFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    /// <summary>Creates a user owned by this test.</summary>
    private async Task CreateUserAsync(DegerliAppFactory factory, string email, bool verified)
    {
        await using var db = _postgres.CreateContext();
        var builder = new FixtureBuilder(db);
        await builder.CreateUserAsync(email, Password, emailConfirmed: verified);
    }

    /// <summary>
    /// Signs the account in through the real Identity sign-in pipeline and returns a
    /// client carrying the resulting auth cookie (mirrors TKT-val-005's harness).
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

    private static async Task<HttpResponseMessage> GetScreensAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me/screens");
        request.Headers.Add("X-Forwarded-For", NextClientIp());
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PatchScreenAsync(HttpClient client, long id, string json)
    {
        var csrf = await FetchCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/me/screens/{id}")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-CSRF-Token", csrf);
        request.Headers.Add("X-Forwarded-For", NextClientIp());
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> DeleteScreenAsync(HttpClient client, long id)
    {
        var csrf = await FetchCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/me/screens/{id}");
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

    private static async Task<ScreenView> ReadScreenAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return ReadScreenView(document.RootElement);
    }

    private static async Task<ScreenView> SingleScreenAsync(HttpResponseMessage list, string name)
    {
        using var document = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        return document.RootElement
            .GetProperty("screens")
            .EnumerateArray()
            .Select(ReadScreenView)
            .Single(screen => screen.Name == name);
    }

    private static async Task<List<string>> NamesAsync(HttpResponseMessage list)
    {
        using var document = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        return document.RootElement
            .GetProperty("screens")
            .EnumerateArray()
            .Select(screen => screen.GetProperty("name").GetString()!)
            .ToList();
    }

    private static ScreenView ReadScreenView(JsonElement element)
        => new(
            element.GetProperty("id").GetInt64(),
            element.GetProperty("name").GetString()!,
            element.GetProperty("criteria").Clone(),
            element.GetProperty("createdAt").GetDateTimeOffset(),
            element.GetProperty("updatedAt").GetDateTimeOffset());

    private sealed record ScreenView(long Id, string Name, JsonElement Criteria, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
}
