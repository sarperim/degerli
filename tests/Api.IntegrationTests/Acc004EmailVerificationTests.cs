using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Degerli.Api.IntegrationTests.Harness;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-acc-004 acceptance — e-mail verification &amp; resend. Group C of
/// `.pipeline/testing/user-accounts.md` (TC-ACC-011..014). Each test runs against the
/// real, migrated Testcontainers PostgreSQL via the in-process host; the mail double
/// supplies the token and the fake clock drives the 48h expiry edge.
/// </summary>
public sealed class Acc004EmailVerificationTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _postgres;

    public Acc004EmailVerificationTests(PostgresFixture postgres) => _postgres = postgres;

    // TC-ACC-011 — verify happy; session gains verified state immediately (M-9).
    [Fact]
    public async Task Verify_consumes_the_token_and_the_same_session_becomes_verified()
    {
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);
        var email = UniqueEmail("acc011");

        using var registered = await RegisterAsync(client, email);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);

        var token = VerificationToken(factory);

        using var verified = await PostJsonAsync(client, "/api/v1/auth/verify-email", new { token });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);

        // Same session, no re-authentication: the very next session read is verified (M-9).
        using var session = await GetAsync(client, "/api/v1/auth/session");
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        using var sessionDocument = JsonDocument.Parse(await session.Content.ReadAsStringAsync());
        Assert.True(sessionDocument.RootElement.GetProperty("authenticated").GetBoolean());
        Assert.True(sessionDocument.RootElement.GetProperty("verified").GetBoolean());
        Assert.Equal(email, sessionDocument.RootElement.GetProperty("email").GetString());

        // The minimal account read reflects it too (the gate SCR/VAL persistence consume).
        using var me = await GetAsync(client, "/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var meDocument = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        Assert.True(meDocument.RootElement.GetProperty("verified").GetBoolean());

        await using var db = _postgres.CreateContext();
        var user = await db.Users.SingleAsync(u => u.NormalizedEmail == email.ToUpperInvariant());
        Assert.True(user.EmailConfirmed);
    }

    // TC-ACC-012 — expired verification token (BVA: 48h edge, fake clock).
    [Fact]
    public async Task Expired_verification_token_is_rejected_with_token_expired()
    {
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);
        var email = UniqueEmail("acc012");

        using var registered = await RegisterAsync(client, email);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var token = VerificationToken(factory);

        // Past the 48h single-use window.
        factory.Clock.Advance(TimeSpan.FromHours(48) + TimeSpan.FromMinutes(1));

        using var verified = await PostJsonAsync(client, "/api/v1/auth/verify-email", new { token });
        Assert.Equal(HttpStatusCode.Gone, verified.StatusCode);
        var problem = await ReadProblemAsync(verified);
        Assert.Equal("TOKEN_EXPIRED", problem.RootElement.GetProperty("code").GetString());

        // Account remains unverified — the persistence gate stays closed.
        using var session = await GetAsync(client, "/api/v1/auth/session");
        using var sessionDocument = JsonDocument.Parse(await session.Content.ReadAsStringAsync());
        Assert.False(sessionDocument.RootElement.GetProperty("verified").GetBoolean());

        await using var db = _postgres.CreateContext();
        var user = await db.Users.SingleAsync(u => u.NormalizedEmail == email.ToUpperInvariant());
        Assert.False(user.EmailConfirmed);
    }

    // TC-ACC-013 — single-use verification token (EP: reuse class).
    [Fact]
    public async Task Reusing_a_consumed_verification_token_is_rejected_with_token_expired()
    {
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);
        var email = UniqueEmail("acc013");

        using var registered = await RegisterAsync(client, email);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var token = VerificationToken(factory);

        using var first = await PostJsonAsync(client, "/api/v1/auth/verify-email", new { token });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var second = await PostJsonAsync(client, "/api/v1/auth/verify-email", new { token });
        Assert.Equal(HttpStatusCode.Gone, second.StatusCode);
        var problem = await ReadProblemAsync(second);
        Assert.Equal("TOKEN_EXPIRED", problem.RootElement.GetProperty("code").GetString());
    }

    // TC-ACC-014 — resend verification (EP: session-scoped resend classes).
    [Fact]
    public async Task Resend_dispatches_a_fresh_token_for_unverified_and_is_a_noop_for_verified()
    {
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);
        var email = UniqueEmail("acc014");

        using var registered = await RegisterAsync(client, email);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        Assert.Single(factory.Mail.Sent);

        // Signed-in-unverified → new e-mail dispatched.
        using var resent = await PostJsonAsync(client, "/api/v1/auth/resend-verification", null);
        Assert.Equal(HttpStatusCode.OK, resent.StatusCode);
        Assert.Equal(2, factory.Mail.Sent.Count);

        // The new token is valid.
        var freshToken = VerificationToken(factory);
        using var verified = await PostJsonAsync(client, "/api/v1/auth/verify-email", new { token = freshToken });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);

        // Verified account → benign confirmation, no new token issued.
        using var resentVerified = await PostJsonAsync(client, "/api/v1/auth/resend-verification", null);
        Assert.Equal(HttpStatusCode.OK, resentVerified.StatusCode);
        Assert.Equal(2, factory.Mail.Sent.Count);

        // Anonymous → 401 (session-scoped: no enumeration surface).
        using var anonymous = CreateClient(factory);
        using var anonymousResend = await PostJsonAsync(anonymous, "/api/v1/auth/resend-verification", null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResend.StatusCode);
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

    private static async Task<HttpResponseMessage> RegisterAsync(HttpClient client, string email)
        => await PostJsonAsync(client, "/api/v1/auth/register", new
        {
            email,
            password = "ValidPass123!",
            consent = new { noticeVersion = "2026-10" },
        });

    private static async Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string url, object? payload)
    {
        var csrf = await FetchCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = payload is null ? JsonContent.Create(new { }) : JsonContent.Create(payload),
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

    /// <summary>Extracts the verification token from the most recently dispatched mail.</summary>
    private static string VerificationToken(DegerliAppFactory factory)
    {
        var mail = factory.Mail.Sent[^1];
        var match = Regex.Match(mail.BodyTr, @"token=([^\s""&]+)");
        Assert.True(match.Success, "The verification e-mail must contain a token link.");
        return Uri.UnescapeDataString(match.Groups[1].Value);
    }

    private static async Task<JsonDocument> ReadProblemAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync());
}
