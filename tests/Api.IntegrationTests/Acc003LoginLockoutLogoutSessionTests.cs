using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-acc-003 acceptance — Identity sign-in, lockout, sign-out &amp; session.
/// Group B of `.pipeline/testing/user-accounts.md` (TC-ACC-006..010). Runs against
/// the real migrated Testcontainers PostgreSQL via the in-process host with the
/// checked-in fixture-universe accounts (FU §9: user-a/user-b/user-c); the fake clock
/// drives the per-account lockout window with no wall-clock wait.
/// </summary>
public sealed class Acc003LoginLockoutLogoutSessionTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const string FixturePassword = "FixturePass1!";

    private readonly PostgresFixture _postgres;

    public Acc003LoginLockoutLogoutSessionTests(PostgresFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        // The fixture-universe accounts (FU §9) are the canonical user-a/b/c; seed the
        // shared dataset once per test so sign-in targets the real accounts.
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, FixtureUniverse.Build(FixtureAnchor.L2));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // TC-ACC-006 — sign-in happy; language preference returned.
    [Fact]
    public async Task Login_returns_session_cookie_and_the_account_language_preference()
    {
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);

        using var clientC = CreateClient(factory);
        using var responseC = await LoginAsync(clientC, "user-c@degerli.test", FixturePassword);

        Assert.Equal(HttpStatusCode.OK, responseC.StatusCode);
        using (var document = JsonDocument.Parse(await responseC.Content.ReadAsStringAsync()))
        {
            Assert.Equal("en", document.RootElement.GetProperty("languagePref").GetString());
        }

        var authCookie = Assert.Single(
            ResponseCookies(responseC),
            cookie => cookie.StartsWith("degerli.auth=", StringComparison.Ordinal));
        Assert.Contains("httponly", authCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", authCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", authCookie, StringComparison.OrdinalIgnoreCase);

        using var clientA = CreateClient(factory);
        using var responseA = await LoginAsync(clientA, "user-a@degerli.test", FixturePassword);

        Assert.Equal(HttpStatusCode.OK, responseA.StatusCode);
        using var documentA = JsonDocument.Parse(await responseA.Content.ReadAsStringAsync());
        Assert.Equal("tr", documentA.RootElement.GetProperty("languagePref").GetString());
    }

    // TC-ACC-007 — generic credential error (enumeration-neutral).
    [Fact]
    public async Task Wrong_password_and_unknown_email_return_identical_invalid_credentials()
    {
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);

        using var client = CreateClient(factory);
        using var wrongPassword = await LoginAsync(client, "user-a@degerli.test", "WrongPass123!");
        using var unknownEmail = await LoginAsync(client, $"nobody-{Guid.NewGuid():N}@degerli.test", FixturePassword);

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);

        Assert.Equal("INVALID_CREDENTIALS", await CodeAsync(wrongPassword));
        Assert.Equal("INVALID_CREDENTIALS", await CodeAsync(unknownEmail));

        // Byte-identical apart from the per-request support traceId: nothing reveals
        // which field (or which account) failed (NFR-ACC-002, UXR-ACC-008).
        Assert.Equal(await NeutralProblemAsync(wrongPassword), await NeutralProblemAsync(unknownEmail));
    }

    // TC-ACC-008 — lockout boundary (9th/10th failure; fake-clock window expiry; per-account).
    [Fact]
    public async Task Lockout_triggers_on_the_tenth_failure_and_expires_after_fifteen_minutes()
    {
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        for (var attempt = 1; attempt <= 9; attempt++)
        {
            using var failed = await LoginAsync(client, "user-b@degerli.test", "WrongPass123!");
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
            Assert.Equal("INVALID_CREDENTIALS", await CodeAsync(failed));
        }

        using (var tenth = await LoginAsync(client, "user-b@degerli.test", "WrongPass123!"))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, tenth.StatusCode);
            Assert.Equal("LOCKED_OUT", await CodeAsync(tenth));
        }

        using (var duringLockout = await LoginAsync(client, "user-b@degerli.test", FixturePassword))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, duringLockout.StatusCode);
            Assert.Equal("LOCKED_OUT", await CodeAsync(duringLockout));
        }

        // A different account is unaffected — lockout is per account, not global.
        using (var other = await LoginAsync(client, "user-c@degerli.test", FixturePassword))
        {
            Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        }

        // After the 15-minute window (fake clock) the correct password signs in again.
        factory.Clock.Advance(TimeSpan.FromMinutes(15));
        using (var unlocked = await LoginAsync(client, "user-b@degerli.test", FixturePassword))
        {
            Assert.Equal(HttpStatusCode.OK, unlocked.StatusCode);
        }
    }

    // TC-ACC-009 — session endpoint shapes (authenticated/anonymous; no-store).
    [Fact]
    public async Task Session_endpoint_reports_authenticated_and_anonymous_shapes_with_no_store()
    {
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);

        using var anonymous = CreateClient(factory);
        using (var session = await GetAsync(anonymous, "/api/v1/auth/session"))
        {
            Assert.Equal(HttpStatusCode.OK, session.StatusCode);
            Assert.Equal("no-store", session.Headers.CacheControl?.ToString());
            using var document = JsonDocument.Parse(await session.Content.ReadAsStringAsync());
            var keys = document.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
            Assert.Equal(new[] { "authenticated" }, keys);
            Assert.False(document.RootElement.GetProperty("authenticated").GetBoolean());
        }

        using var client = CreateClient(factory);
        using (var login = await LoginAsync(client, "user-a@degerli.test", FixturePassword))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }

        using (var session = await GetAsync(client, "/api/v1/auth/session"))
        {
            Assert.Equal(HttpStatusCode.OK, session.StatusCode);
            Assert.Equal("no-store", session.Headers.CacheControl?.ToString());
            using var document = JsonDocument.Parse(await session.Content.ReadAsStringAsync());
            var keys = document.RootElement.EnumerateObject()
                .Select(p => p.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(new[] { "authenticated", "email", "languagePref", "role", "verified" }, keys);
            Assert.True(document.RootElement.GetProperty("authenticated").GetBoolean());
            Assert.Equal("user-a@degerli.test", document.RootElement.GetProperty("email").GetString());
            Assert.True(document.RootElement.GetProperty("verified").GetBoolean());
            Assert.Equal("tr", document.RootElement.GetProperty("languagePref").GetString());
            Assert.Equal("user", document.RootElement.GetProperty("role").GetString());
        }
    }

    // TC-ACC-010 — sign-out revokes the session.
    [Fact]
    public async Task Logout_revokes_the_session_and_the_old_cookie_no_longer_authenticates()
    {
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        string issuedCookie;
        using (var login = await LoginAsync(client, "user-c@degerli.test", FixturePassword))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var setCookie = Assert.Single(
                ResponseCookies(login),
                cookie => cookie.StartsWith("degerli.auth=", StringComparison.Ordinal));
            issuedCookie = setCookie.Split(';')[0];
        }

        using (var logout = await LogoutAsync(client))
        {
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        }

        using (var session = await GetAsync(client, "/api/v1/auth/session"))
        {
            using var document = JsonDocument.Parse(await session.Content.ReadAsStringAsync());
            Assert.False(document.RootElement.GetProperty("authenticated").GetBoolean());
        }

        using (var me = await GetAsync(client, "/api/v1/me"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        }

        // The cookie that authenticated before sign-out no longer authenticates: the
        // session is revoked server-side, not merely cleared from the browser. The
        // clock moves past the (zero) security-stamp revalidation instant so the
        // revoked cookie is revalidated on this request.
        factory.Clock.Advance(TimeSpan.FromSeconds(5));
        using var replay = CreateCookieLessClient(factory);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/session");
        request.Headers.Add("Cookie", issuedCookie);
        request.Headers.Add("X-Forwarded-For", NextClientIp());
        using var replayed = await replay.SendAsync(request);
        using var replayedDocument = JsonDocument.Parse(await replayed.Content.ReadAsStringAsync());
        Assert.False(replayedDocument.RootElement.GetProperty("authenticated").GetBoolean());
    }

    private static HttpClient CreateClient(DegerliAppFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    private static HttpClient CreateCookieLessClient(DegerliAppFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    private static string NextClientIp() => Guid.NewGuid().ToString("N");

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password)
    {
        var csrf = await FetchCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { email, password }),
        };
        request.Headers.Add("X-CSRF-Token", csrf);
        request.Headers.Add("X-Forwarded-For", NextClientIp());
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> LogoutAsync(HttpClient client)
    {
        var csrf = await FetchCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
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

    private static IEnumerable<string> ResponseCookies(HttpResponseMessage response)
        => response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : Array.Empty<string>();

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("code").GetString();
    }

    /// <summary>The problem body with the per-request traceId removed, for neutrality comparison.</summary>
    private static async Task<string> NeutralProblemAsync(HttpResponseMessage response)
    {
        var node = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        node.Remove("traceId");
        return node.ToJsonString();
    }
}
