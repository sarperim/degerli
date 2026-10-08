using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-acc-002 acceptance — Identity core: registration, consent &amp; session.
/// Group A of `.pipeline/testing/user-accounts.md` (TC-ACC-001..005). Each test runs
/// against the real, migrated Testcontainers PostgreSQL via the in-process host and
/// asserts through the API and the database; the mail double captures dispatch and
/// the fake clock fixes consent time.
/// </summary>
public sealed class Acc002RegistrationConsentSessionTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _postgres;

    public Acc002RegistrationConsentSessionTests(PostgresFixture postgres) => _postgres = postgres;

    // TC-ACC-001 — register: account, consent, verification e-mail, session.
    [Fact]
    public async Task Register_creates_account_consent_verification_mail_and_unverified_session()
    {
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);
        var email = UniqueEmail("acc001");

        using var response = await RegisterAsync(client, new
        {
            email,
            password = "ValidPass123!",
            consent = new { noticeVersion = "2026-10" },
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var authCookie = Assert.Single(
            ResponseCookies(response),
            cookie => cookie.StartsWith("degerli.auth=", StringComparison.Ordinal));
        Assert.Contains("httponly", authCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", authCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", authCookie, StringComparison.OrdinalIgnoreCase);

        await using (var db = _postgres.CreateContext())
        {
            var user = await db.Users.SingleAsync(u => u.NormalizedEmail == email.ToUpperInvariant());
            Assert.False(user.EmailConfirmed);

            var consent = await db.ConsentRecords.SingleAsync(c => c.UserId == user.Id);
            Assert.Equal("2026-10", consent.NoticeVersion);
            Assert.Equal("register", consent.Action);
            Assert.False(string.IsNullOrWhiteSpace(consent.UserRefHash));
            Assert.Equal(factory.Clock.GetUtcNow(), consent.ConsentedAt);
        }

        var mail = Assert.Single(factory.Mail.Sent);
        Assert.Equal(email, mail.To);
        Assert.Contains("token=", mail.BodyTr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("token=", mail.BodyEn, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("http", mail.BodyTr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("http", mail.BodyEn, StringComparison.OrdinalIgnoreCase);

        using var session = await GetAsync(client, "/api/v1/auth/session");
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        using var document = JsonDocument.Parse(await session.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("authenticated").GetBoolean());
        Assert.False(document.RootElement.GetProperty("verified").GetBoolean());
        Assert.Equal(email, document.RootElement.GetProperty("email").GetString());
    }

    // TC-ACC-002 — duplicate e-mail rejected with sign-in path data.
    [Fact]
    public async Task Duplicate_email_is_rejected_with_email_taken_and_writes_nothing()
    {
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);
        var email = UniqueEmail("acc002");
        var payload = new
        {
            email,
            password = "ValidPass123!",
            consent = new { noticeVersion = "2026-10" },
        };

        using var first = await RegisterAsync(client, payload);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var second = await RegisterAsync(client, payload);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var problem = await ReadProblemAsync(second);
        Assert.Equal("EMAIL_TAKEN", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(email, problem.RootElement.GetProperty("params").GetProperty("email").GetString());

        await using var db = _postgres.CreateContext();
        Assert.Equal(1, await db.Users.CountAsync(u => u.NormalizedEmail == email.ToUpperInvariant()));
        var user = await db.Users.SingleAsync(u => u.NormalizedEmail == email.ToUpperInvariant());
        Assert.Equal(1, await db.ConsentRecords.CountAsync(c => c.UserId == user.Id));
        Assert.Single(factory.Mail.Sent);
    }

    // TC-ACC-003 — password policy boundary (9/10 chars; no composition rules).
    [Fact]
    public async Task Password_policy_requires_ten_chars_and_no_composition_rules()
    {
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        var tooShortEmail = UniqueEmail("acc003-short");
        using var tooShort = await RegisterAsync(client, new
        {
            email = tooShortEmail,
            password = "abcde1234", // 9 chars
            consent = new { noticeVersion = "2026-10" },
        });

        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        var problem = await ReadProblemAsync(tooShort);
        Assert.Equal("VALIDATION_FAILED", problem.RootElement.GetProperty("code").GetString());
        Assert.Contains(ProblemFields(problem), field => field == "password");

        var exactEmail = UniqueEmail("acc003-exact");
        using var exact = await RegisterAsync(client, new
        {
            email = exactEmail,
            password = "abcde12345", // exactly 10, all lowercase — no composition requirement
            consent = new { noticeVersion = "2026-10" },
        });
        Assert.Equal(HttpStatusCode.Created, exact.StatusCode);

        await using var db = _postgres.CreateContext();
        Assert.False(await db.Users.AnyAsync(u => u.NormalizedEmail == tooShortEmail.ToUpperInvariant()));
        Assert.True(await db.Users.AnyAsync(u => u.NormalizedEmail == exactEmail.ToUpperInvariant()));
    }

    // TC-ACC-004 — consent required (absent / stale noticeVersion / valid).
    [Fact]
    public async Task Consent_is_required_and_notice_version_must_be_known()
    {
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        var missingEmail = UniqueEmail("acc004-missing");
        using var missing = await RegisterAsync(client, new
        {
            email = missingEmail,
            password = "ValidPass123!",
        });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var missingProblem = await ReadProblemAsync(missing);
        Assert.Equal("VALIDATION_FAILED", missingProblem.RootElement.GetProperty("code").GetString());
        Assert.Contains(ProblemFields(missingProblem), field => field == "consent");

        var staleEmail = UniqueEmail("acc004-stale");
        using var stale = await RegisterAsync(client, new
        {
            email = staleEmail,
            password = "ValidPass123!",
            consent = new { noticeVersion = "1999-01" },
        });
        Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);
        var staleProblem = await ReadProblemAsync(stale);
        Assert.Equal("VALIDATION_FAILED", staleProblem.RootElement.GetProperty("code").GetString());
        Assert.Contains(ProblemFields(staleProblem), field => field == "consent.noticeVersion");

        await using var db = _postgres.CreateContext();
        Assert.False(await db.Users.AnyAsync(u => u.NormalizedEmail == missingEmail.ToUpperInvariant()));
        Assert.False(await db.Users.AnyAsync(u => u.NormalizedEmail == staleEmail.ToUpperInvariant()));
    }

    // TC-ACC-005 — minimal data collection.
    [Fact]
    public async Task Registration_accepts_only_the_documented_fields_and_me_exposes_only_them()
    {
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);
        var email = UniqueEmail("acc005");

        using var response = await RegisterAsync(client, new
        {
            email,
            password = "ValidPass123!",
            consent = new { noticeVersion = "2026-10" },
            // Unknown fields must be ignored, never stored (payload evolution, BR-ACC-001).
            name = "Ada Lovelace",
            profile = new { age = 36, city = "İstanbul" },
            demographics = new { gender = "x" },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var me = await GetAsync(client, "/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        using var document = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        var keys = document.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "email", "languagePref", "verified" }, keys);
        Assert.Equal(email, document.RootElement.GetProperty("email").GetString());
        Assert.Equal("tr", document.RootElement.GetProperty("languagePref").GetString());
        Assert.False(document.RootElement.GetProperty("verified").GetBoolean());
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

    private static async Task<HttpResponseMessage> RegisterAsync(HttpClient client, object payload)
    {
        var csrf = await FetchCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/register")
        {
            Content = JsonContent.Create(payload),
        };
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
