using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Persistence.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-acc-006 acceptance — Account settings endpoints. Group E of
/// `.pipeline/testing/user-accounts.md` (TC-ACC-019..021):
/// <list type="bullet">
/// <item>TC-ACC-019 — language preference persists and is applied at the next sign-in.</item>
/// <item>TC-ACC-020 — password change (correct/wrong current, weak new) keeps the session.</item>
/// <item>TC-ACC-021 — deletion cascades, consent retained anonymized, session revoked.</item>
/// </list>
/// Runs against the real, migrated Testcontainers PostgreSQL through the in-process
/// host. Sessions are established through the real registration flow, and the
/// "next sign-in" assertions re-authenticate against the real Identity pipeline
/// (the same path <c>POST /auth/login</c> uses — that endpoint is TKT-acc-003's).
/// </summary>
public sealed class Acc006AccountSettingsTests : IClassFixture<PostgresFixture>
{
    private const string Password = "ValidPass123!";

    private readonly PostgresFixture _postgres;

    public Acc006AccountSettingsTests(PostgresFixture postgres) => _postgres = postgres;

    // TC-ACC-019 — language preference persists and applies at sign-in.
    [Fact]
    public async Task Language_preference_is_persisted_and_applied_at_the_next_sign_in()
    {
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);
        var email = UniqueEmail("acc019");

        using var registered = await RegisterAsync(client, email, Password);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);

        using var patched = await PatchAsync(client, "/api/v1/me", new { languagePref = "en" });
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);

        // Persisted (BR-ACC-004): the stored value login returns.
        await using (var db = _postgres.CreateContext())
        {
            var user = await db.Users.SingleAsync(u => u.NormalizedEmail == email.ToUpperInvariant());
            Assert.Equal("en", user.LanguagePref);
        }

        // The change is reflected on the current session's /me read.
        using var me = await GetAsync(client, "/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using (var document = JsonDocument.Parse(await me.Content.ReadAsStringAsync()))
        {
            Assert.Equal("en", document.RootElement.GetProperty("languagePref").GetString());
        }

        // "next login returns en" — a fresh authentication through the Identity
        // sign-in pipeline (the login endpoint's path) reports the persisted value.
        using var freshClient = await CreateSignedInClientAsync(factory, email, Password);
        using var session = await GetAsync(freshClient, "/api/v1/auth/session");
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        using (var document = JsonDocument.Parse(await session.Content.ReadAsStringAsync()))
        {
            Assert.True(document.RootElement.GetProperty("authenticated").GetBoolean());
            Assert.Equal("en", document.RootElement.GetProperty("languagePref").GetString());
        }

        // Invalid value → 400 VALIDATION_FAILED, persisted value unchanged.
        using var invalid = await PatchAsync(client, "/api/v1/me", new { languagePref = "de" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var problem = await ReadProblemAsync(invalid);
        Assert.Equal("VALIDATION_FAILED", problem.RootElement.GetProperty("code").GetString());
        Assert.Contains(ProblemFields(problem), field => field == "languagePref");

        await using (var db = _postgres.CreateContext())
        {
            var user = await db.Users.SingleAsync(u => u.NormalizedEmail == email.ToUpperInvariant());
            Assert.Equal("en", user.LanguagePref);
        }
    }

    // TC-ACC-020 — password change keeps the session; wrong current/weak new rejected.
    [Fact]
    public async Task Password_change_keeps_the_session_and_enforces_current_and_policy()
    {
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);
        var email = UniqueEmail("acc020");
        const string newPassword = "NewValidPass456!";

        using var registered = await RegisterAsync(client, email, Password);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);

        // correct current + valid new → success, session still authenticated.
        using var changed = await PatchAsync(
            client,
            "/api/v1/me/password",
            new { current = Password, newPassword });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

        using var me = await GetAsync(client, "/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        // New password works at next sign-in; the old one fails.
        Assert.True(await CheckPasswordAsync(factory, email, newPassword));
        Assert.False(await CheckPasswordAsync(factory, email, Password));

        // wrong current password → 400 field `current` (I-ACC-1), session intact.
        using var wrongCurrent = await PatchAsync(
            client,
            "/api/v1/me/password",
            new { current = "WrongPass123!", newPassword = "AnotherPass789!" });
        Assert.Equal(HttpStatusCode.BadRequest, wrongCurrent.StatusCode);
        var wrongProblem = await ReadProblemAsync(wrongCurrent);
        Assert.Equal("VALIDATION_FAILED", wrongProblem.RootElement.GetProperty("code").GetString());
        Assert.Contains(ProblemFields(wrongProblem), field => field == "current");

        // weak new (9 chars) → 400 field `newPassword`; nothing changed.
        using var weak = await PatchAsync(
            client,
            "/api/v1/me/password",
            new { current = newPassword, newPassword = "abcde1234" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        var weakProblem = await ReadProblemAsync(weak);
        Assert.Equal("VALIDATION_FAILED", weakProblem.RootElement.GetProperty("code").GetString());
        Assert.Contains(ProblemFields(weakProblem), field => field == "newPassword");

        Assert.True(await CheckPasswordAsync(factory, email, newPassword));
        using var stillAuthenticated = await GetAsync(client, "/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, stillAuthenticated.StatusCode);
    }

    // TC-ACC-021 — deletion cascades; consent retained anonymized; session revoked.
    [Fact]
    public async Task Deletion_cascades_retains_anonymized_consent_and_revokes_the_session()
    {
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);
        var email = UniqueEmail("acc021");

        using var registered = await RegisterAsync(client, email, Password);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);

        long userId;
        long consentId;
        long retainedConsentId;
        string consentHash;
        await using (var db = _postgres.CreateContext())
        {
            var user = await db.Users.SingleAsync(u => u.NormalizedEmail == email.ToUpperInvariant());
            userId = user.Id;

            var consent = await db.ConsentRecords.SingleAsync(c => c.UserId == user.Id);
            consentId = consent.Id;
            consentHash = consent.UserRefHash;
            Assert.False(string.IsNullOrWhiteSpace(consentHash));

            // Precondition (created in-test): a saved screen and a DCF scenario.
            var builder = new FixtureBuilder(db);
            await builder.CreateScreenAsync(user.Id, "Ekranım");
            var instrument = await builder.CreateInstrumentAsync($"ACC{Guid.NewGuid():N}"[..12]);
            await builder.CreateScenarioAsync(user.Id, instrument.Id, "Temel");
        }

        // A second, unrelated account is untouched by the deletion.
        var otherEmail = UniqueEmail("acc021-other");
        using (var otherClient = CreateClient(factory))
        {
            using var otherRegistered = await RegisterAsync(otherClient, otherEmail, Password);
            Assert.Equal(HttpStatusCode.Created, otherRegistered.StatusCode);
        }

        using var deleted = await DeleteAsync(client, "/api/v1/me");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        await using (var db = _postgres.CreateContext())
        {
            Assert.False(await db.Users.AnyAsync(u => u.Id == userId));
            Assert.False(await db.SavedScreens.AnyAsync(s => s.UserId == userId));
            Assert.False(await db.DcfScenarios.AnyAsync(s => s.UserId == userId));

            // Consent evidence survives, anonymized (02 §5.4).
            retainedConsentId = await db.ConsentRecords
                .Where(c => c.Id == consentId)
                .Select(c => c.Id)
                .SingleAsync();
            var retained = await db.ConsentRecords.SingleAsync(c => c.Id == retainedConsentId);
            Assert.Null(retained.UserId);
            Assert.Equal(consentHash, retained.UserRefHash);
            Assert.False(string.IsNullOrWhiteSpace(retained.UserRefHash));

            // Sign-in with the old credentials is impossible: the account row is gone.
            Assert.Null(await db.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == email.ToUpperInvariant()));

            // Other users are unaffected.
            Assert.True(await db.Users.AnyAsync(u => u.NormalizedEmail == otherEmail.ToUpperInvariant()));
        }

        // Session revoked (UC-ACC-003 step 5).
        using var me = await GetAsync(client, "/api/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        var problem = await ReadProblemAsync(me);
        Assert.Equal("UNAUTHENTICATED", problem.RootElement.GetProperty("code").GetString());
    }

    private static HttpClient CreateClient(DegerliAppFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    private static string UniqueEmail(string prefix)
        => $"{prefix}-{Guid.NewGuid():N}@degerli.test";

    // Each request carries a unique forwarded-for value so the auth rate limiter
    // (5/min/IP) never interferes with assertion count.
    private static string NextClientIp() => Guid.NewGuid().ToString("N");

    private static async Task<HttpResponseMessage> RegisterAsync(HttpClient client, string email, string password)
    {
        var csrf = await FetchCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/register")
        {
            Content = JsonContent.Create(new
            {
                email,
                password,
                consent = new { noticeVersion = "2026-10" },
            }),
        };
        request.Headers.Add("X-CSRF-Token", csrf);
        request.Headers.Add("X-Forwarded-For", NextClientIp());
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PatchAsync(HttpClient client, string url, object payload)
    {
        // The token is (re)minted while authenticated so it validates against the
        // signed-in principal.
        var csrf = await FetchCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Add("X-CSRF-Token", csrf);
        request.Headers.Add("X-Forwarded-For", NextClientIp());
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> DeleteAsync(HttpClient client, string url)
    {
        var csrf = await FetchCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.Add("X-CSRF-Token", csrf);
        request.Headers.Add("X-Forwarded-For", NextClientIp());
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
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

    /// <summary>
    /// Re-authenticates through the real Identity sign-in pipeline (the path the
    /// login endpoint uses) and returns a client carrying the resulting auth cookie.
    /// </summary>
    private static async Task<HttpClient> CreateSignedInClientAsync(
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
        Assert.True(result.Succeeded, "expected a fresh sign-in to succeed");

        var setCookie = httpContext.Response.Headers.SetCookie.ToString();
        var cookiePair = setCookie.Split(';', 2)[0];
        Assert.StartsWith("degerli.auth=", cookiePair, StringComparison.Ordinal);

        var client = CreateClient(factory);
        client.DefaultRequestHeaders.Add("Cookie", cookiePair);
        return client;
    }

    private static async Task<bool> CheckPasswordAsync(DegerliAppFactory factory, string email, string password)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);
        return await userManager.CheckPasswordAsync(user!, password);
    }

    private static async Task<JsonDocument> ReadProblemAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(payload);
    }

    private static IEnumerable<string> ProblemFields(JsonDocument problem)
        => problem.RootElement
            .GetProperty("params")
            .GetProperty("fields")
            .EnumerateArray()
            .Select(field => field.GetProperty("field").GetString()!);
}
