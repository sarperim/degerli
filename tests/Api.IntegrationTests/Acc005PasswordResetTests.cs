using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-acc-005 acceptance — password reset. Group D of
/// `.pipeline/testing/user-accounts.md` (TC-ACC-015..018). Runs against the real,
/// migrated Testcontainers PostgreSQL through the in-process host; the mail double
/// captures dispatch and the fake clock drives the 2h reset-token expiry.
/// </summary>
public sealed class Acc005PasswordResetTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _postgres;

    public Acc005PasswordResetTests(PostgresFixture postgres) => _postgres = postgres;

    // TC-ACC-015 — reset request is enumeration-neutral.
    [Fact]
    public async Task Forgot_password_is_enumeration_neutral_and_mails_only_registered_addresses()
    {
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);
        var registered = UniqueEmail("acc015-registered");
        var unknown = UniqueEmail("acc015-unknown");
        await CreateUserAsync(factory, registered, "ValidPass123!");

        using var registeredResponse = await PostAsync(client, "/api/v1/auth/forgot-password", new { email = registered });
        using var unknownResponse = await PostAsync(client, "/api/v1/auth/forgot-password", new { email = unknown });

        Assert.Equal(HttpStatusCode.OK, registeredResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unknownResponse.StatusCode);

        // Byte-identical neutral confirmations (no enumeration signal).
        Assert.Equal(
            await registeredResponse.Content.ReadAsStringAsync(),
            await unknownResponse.Content.ReadAsStringAsync());

        // Reset e-mail (2h single-use token) dispatched only for the registered address.
        var mail = Assert.Single(factory.Mail.Sent);
        Assert.Equal(registered, mail.To);
        Assert.Contains("token=", mail.BodyTr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("token=", mail.BodyEn, StringComparison.OrdinalIgnoreCase);
    }

    // TC-ACC-016 — reset happy path.
    [Fact]
    public async Task Reset_password_swaps_the_credential_and_keeps_the_account()
    {
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);
        var email = UniqueEmail("acc016");
        const string oldPassword = "ValidPass123!";
        const string newPassword = "BrandNewPass456!";
        await CreateUserAsync(factory, email, oldPassword);

        // TC-ACC-016 "saved work intact": seed a screen and a DCF scenario owned by
        // this user before the reset, so the post-reset assertion can actually detect a
        // reset regression that destroyed the account's work.
        long savedScreenId;
        long savedScenarioId;
        await using (var seedDb = _postgres.CreateContext())
        {
            var builder = new FixtureBuilder(seedDb);
            var seededUserId = (await seedDb.Users.SingleAsync(u => u.NormalizedEmail == email.ToUpperInvariant())).Id;
            var instrument = await builder.CreateInstrumentAsync($"ACC016-{Guid.NewGuid():N}");
            savedScreenId = (await builder.CreateScreenAsync(seededUserId, "ACC-016 saved screen")).Id;
            savedScenarioId = (await builder.CreateScenarioAsync(seededUserId, instrument.Id, "ACC-016 saved scenario")).Id;
        }

        using var forgot = await PostAsync(client, "/api/v1/auth/forgot-password", new { email });
        Assert.Equal(HttpStatusCode.OK, forgot.StatusCode);
        var token = ExtractToken(Assert.Single(factory.Mail.Sent));

        using var reset = await PostAsync(client, "/api/v1/auth/reset-password", new { token, newPassword });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        // Sign-in semantics at the Identity layer (the HTTP login endpoint is
        // TKT-acc-003, outside this ticket's scope): new password works, old fails.
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var signInManager = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);

        Assert.True((await signInManager.CheckPasswordSignInAsync(user!, newPassword, lockoutOnFailure: false)).Succeeded);
        Assert.False((await signInManager.CheckPasswordSignInAsync(user!, oldPassword, lockoutOnFailure: false)).Succeeded);

        // Saved work is intact: the user's screen and DCF scenario survive the reset.
        await using var db = _postgres.CreateContext();
        Assert.True(await db.Users.AnyAsync(u => u.Id == user!.Id));
        Assert.True(await db.SavedScreens.AnyAsync(s => s.Id == savedScreenId && s.UserId == user!.Id));
        Assert.True(await db.DcfScenarios.AnyAsync(s => s.Id == savedScenarioId && s.UserId == user!.Id));
    }

    // TC-ACC-017 — expired reset token; re-request path.
    [Fact]
    public async Task Expired_reset_token_is_rejected_and_a_fresh_request_works()
    {
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);
        var email = UniqueEmail("acc017");
        await CreateUserAsync(factory, email, "ValidPass123!");

        using var forgot = await PostAsync(client, "/api/v1/auth/forgot-password", new { email });
        Assert.Equal(HttpStatusCode.OK, forgot.StatusCode);
        var staleToken = ExtractToken(Assert.Single(factory.Mail.Sent));

        // Past the 2h reset-token expiry (fake clock).
        factory.Clock.Advance(TimeSpan.FromHours(2) + TimeSpan.FromMinutes(1));

        using var expired = await PostAsync(client, "/api/v1/auth/reset-password",
            new { token = staleToken, newPassword = "BrandNewPass456!" });
        Assert.Equal(HttpStatusCode.Gone, expired.StatusCode);
        var problem = await ReadProblemAsync(expired);
        Assert.Equal("TOKEN_EXPIRED", problem.RootElement.GetProperty("code").GetString());

        // The re-request path produces a working token.
        using var reRequest = await PostAsync(client, "/api/v1/auth/forgot-password", new { email });
        Assert.Equal(HttpStatusCode.OK, reRequest.StatusCode);
        var freshToken = ExtractToken(factory.Mail.Sent.Last(mail => mail.To == email));

        using var reset = await PostAsync(client, "/api/v1/auth/reset-password",
            new { token = freshToken, newPassword = "BrandNewPass456!" });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
    }

    // TC-ACC-018 — reset token single-use; policy applies.
    [Fact]
    public async Task Reset_token_is_single_use_and_password_policy_is_enforced()
    {
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);
        var email = UniqueEmail("acc018");
        await CreateUserAsync(factory, email, "ValidPass123!");

        using var forgot = await PostAsync(client, "/api/v1/auth/forgot-password", new { email });
        Assert.Equal(HttpStatusCode.OK, forgot.StatusCode);
        var token = ExtractToken(Assert.Single(factory.Mail.Sent));

        // Policy enforced on reset too: 9 chars rejected, field password.
        using var weak = await PostAsync(client, "/api/v1/auth/reset-password",
            new { token, newPassword = "abcde1234" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        var weakProblem = await ReadProblemAsync(weak);
        Assert.Equal("VALIDATION_FAILED", weakProblem.RootElement.GetProperty("code").GetString());
        Assert.Contains(ProblemFields(weakProblem), field => field == "password");

        // Exactly 10 chars accepted with the same, still-valid token.
        using var accepted = await PostAsync(client, "/api/v1/auth/reset-password",
            new { token, newPassword = "abcde12345" });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        // Single-use: consuming the token again is rejected as expired.
        using var reuse = await PostAsync(client, "/api/v1/auth/reset-password",
            new { token, newPassword = "AnotherPass123!" });
        Assert.Equal(HttpStatusCode.Gone, reuse.StatusCode);
        var reuseProblem = await ReadProblemAsync(reuse);
        Assert.Equal("TOKEN_EXPIRED", reuseProblem.RootElement.GetProperty("code").GetString());
    }

    private static HttpClient CreateClient(DegerliAppFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    private static string UniqueEmail(string prefix)
        => $"{prefix}-{Guid.NewGuid():N}@degerli.test";

    private static string NextClientIp() => Guid.NewGuid().ToString("N");

    private static async Task CreateUserAsync(DegerliAppFactory factory, string email, string password)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            LanguagePref = "tr",
        };
        var created = await userManager.CreateAsync(user, password);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
    }

    private static string ExtractToken(Degerli.Api.Mail.OutboundMail mail)
    {
        var match = Regex.Match(mail.BodyTr, @"token=([^&\s]+)");
        Assert.True(match.Success, "The reset e-mail must contain a token link.");
        return Uri.UnescapeDataString(match.Groups[1].Value);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, object payload)
    {
        var csrf = await FetchCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload),
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

    private static IEnumerable<string> ProblemFields(JsonDocument problem)
        => problem.RootElement
            .GetProperty("params")
            .GetProperty("fields")
            .EnumerateArray()
            .Select(field => field.GetProperty("field").GetString()!);
}
