using System.Net;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Valuation;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-val-009 acceptance — the builder-only DCF baseline regeneration endpoint
/// <c>POST /api/v1/admin/dcf-baselines/regenerate</c> (Group C of
/// <c>.pipeline/testing/valuation-dcf.md</c>, TC-VAL-015).
///
/// The endpoint triggers the C3b <c>dcf-baselines</c> job through the real runner, so
/// the run is recorded in the ledger and the baselines are re-derived from the seeded
/// canonical facts. Regeneration is a state transition: the active version bumps from
/// v_n to v_n+1, exactly one baseline stays active per stock, prior versions are
/// retained, and the next <c>GET …/dcf</c> serves the new active version.
/// </summary>
public sealed class DcfBaselineRegenerationTests : IClassFixture<PostgresFixture>
{
    private const string BuilderEmail = "builder@degerli.test";
    private const string UserAEmail = "user-a@degerli.test";
    private const string FixturePassword = "FixturePass1!";
    private const string RegenerateUrl = "/api/v1/admin/dcf-baselines/regenerate";

    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly BuildDate => L2.Anchor.Calendar(-6);

    private readonly PostgresFixture _postgres;

    public DcfBaselineRegenerationTests(PostgresFixture postgres) => _postgres = postgres;

    // -- TC-VAL-015 — regeneration bumps version and serves new active baseline ----

    [Fact]
    public async Task TC_VAL_015_regeneration_bumps_the_active_version_and_is_ledger_visible()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);

        // A baseline already exists: the C3b job has run once (v1).
        await RunBaselineJobAsync(factory, BuildDate);

        using var builder = await SignedInClientAsync(factory, BuilderEmail);
        var csrf = await CsrfTokenAsync(builder);

        using (var response = await PostAsync(builder, RegenerateUrl, csrf))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = document.RootElement;
            Assert.Equal(DcfBaselineGenerationJob.Code, root.GetProperty("job").GetString());
            Assert.Equal(IngestResult.Succeeded, root.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Number, root.GetProperty("runId").ValueKind);
        }

        // The regeneration run appears in the ledger (UC-VAL-003, UXR-MDF-025).
        using (var ledger = await GetAsync(builder, "/api/v1/admin/ingest-runs?job=dcf-baselines"))
        {
            Assert.Equal(HttpStatusCode.OK, ledger.StatusCode);
            using var document = JsonDocument.Parse(await ledger.Content.ReadAsStringAsync());
            var runs = document.RootElement.GetProperty("runs").EnumerateArray().ToList();
            Assert.Contains(runs, run => run.GetProperty("job").GetString() == DcfBaselineGenerationJob.Code);
        }

        // Exactly one active baseline per stock; the version bumped v1 → v2; v1 retained.
        await using (var db = _postgres.CreateContext())
        {
            var instrumentId = await db.Instruments
                .Where(i => i.Symbol == "ALFA")
                .Select(i => i.Id)
                .SingleAsync();
            var baselines = await db.DcfBaselines
                .Where(b => b.InstrumentId == instrumentId)
                .OrderBy(b => b.Version)
                .ToListAsync();

            Assert.Equal(new[] { 1, 2 }, baselines.Select(b => b.Version).ToArray());
            Assert.Single(baselines, b => b.IsActive);
            Assert.True(baselines.Single(b => b.Version == 2).IsActive);
            Assert.False(baselines.Single(b => b.Version == 1).IsActive);

            // One active per stock holds across the whole universe.
            var activePerInstrument = (await db.DcfBaselines
                    .Where(b => b.IsActive)
                    .ToListAsync())
                .GroupBy(b => b.InstrumentId);
            Assert.All(activePerInstrument, group => Assert.Single(group));
        }

        // The next GET serves the new active version (SCR-006 baseline on next load).
        using (var read = await GetAsync(builder, "/api/v1/stocks/ALFA/dcf"))
        {
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            using var document = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
            var baseline = document.RootElement.GetProperty("baseline");
            Assert.Equal(2, baseline.GetProperty("version").GetInt32());
        }
    }

    [Fact]
    public async Task TC_VAL_015_regeneration_is_builder_gated()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);

        // Anonymous unsafe call without a CSRF token → 400 (antiforgery runs first).
        using (var anonymous = CreateClient(factory))
        {
            using var noCsrf = await PostAsync(anonymous, RegenerateUrl, csrf: null);
            Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        }

        // Anonymous unsafe call with a CSRF token → 401 UNAUTHENTICATED.
        using (var anonymous = CreateClient(factory))
        {
            var csrf = await CsrfTokenAsync(anonymous);
            using var response = await PostAsync(anonymous, RegenerateUrl, csrf);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("UNAUTHENTICATED", await CodeAsync(response));
        }

        // A signed-in non-builder → 403 FORBIDDEN.
        using (var user = await SignedInClientAsync(factory, UserAEmail))
        {
            var csrf = await CsrfTokenAsync(user);
            using var response = await PostAsync(user, RegenerateUrl, csrf);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("FORBIDDEN", await CodeAsync(response));
        }

        // The gate held: no baseline rows were written by the rejected calls.
        await using var db = _postgres.CreateContext();
        Assert.Equal(0, await db.DcfBaselines.CountAsync());
    }

    // ---------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------

    private async Task SeedAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
        await db.DcfBaselines.ExecuteDeleteAsync();
        await db.IngestRuns.ExecuteDeleteAsync();
    }

    private static async Task RunBaselineJobAsync(DegerliAppFactory factory, DateOnly buildDate)
    {
        using var scope = factory.Services.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IIngestionJobRunner>();
        var result = await runner.RunAsync(DcfBaselineGenerationJob.Code, new IngestionRequest(buildDate));
        Assert.Equal(IngestResult.Succeeded, result.Status);
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    private static async Task<HttpClient> SignedInClientAsync(
        WebApplicationFactory<Program> factory,
        string email)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var signInManager = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();

        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);

        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        accessor.HttpContext = httpContext;

        var result = await signInManager.PasswordSignInAsync(
            user!, FixturePassword, isPersistent: false, lockoutOnFailure: true);
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

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, string? csrf)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("X-Forwarded-For", Guid.NewGuid().ToString("N"));
        if (csrf is not null)
        {
            request.Headers.Add("X-CSRF-Token", csrf);
        }

        return await client.SendAsync(request);
    }

    private static async Task<string> CsrfTokenAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/csrf-token");
        request.Headers.Add("X-Forwarded-For", Guid.NewGuid().ToString("N"));
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
}
