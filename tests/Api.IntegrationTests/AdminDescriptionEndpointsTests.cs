using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-res-006 acceptance — the builder-only admin descriptions surface (`03` §8 v1.2,
/// FR-RES-020/021, UC-RES-004 steps 2–4, UXR-MDF-018..022 API side):
/// <list type="bullet">
/// <item><c>GET /api/v1/admin/descriptions?status=</c> — the review queue that *is* the
/// editor payload: stock, version, both texts and the KAP <c>sourceRefs</c> resolved
/// against <c>kap_disclosures</c>, with <c>{disclosureId}</c>-only stubs for
/// unresolvable ids (never silently dropped) under a wrapped root (TC-RES-018).</item>
/// <item><c>PATCH /api/v1/admin/descriptions/{id}</c> <c>{textTr?, textEn?}</c> — saves
/// both languages; provenance is not editable, a <c>sourceRefs</c> write is
/// <c>400 VALIDATION_FAILED</c> (I-RES-2) (TC-RES-019).</item>
/// <item><c>POST /api/v1/admin/descriptions/{id}/publish</c> — the publication gate:
/// both languages required, a missing one is <c>400 VALIDATION_FAILED</c> naming it
/// (I-RES-1); the DB CHECK is the backstop (TC-RES-020), and publishing flips the public
/// page from <c>preparing</c> to <c>published</c> (TC-RES-021).</item>
/// </list>
/// Every test runs through the real in-process API host against a migrated,
/// fixture-seeded Testcontainers PostgreSQL.
/// </summary>
public sealed class AdminDescriptionEndpointsTests : IClassFixture<PostgresFixture>
{
    private const string BuilderEmail = "builder@degerli.test";
    private const string UserAEmail = "user-a@degerli.test";
    private const string FixturePassword = "FixturePass1!";
    private const string QueuePath = "/api/v1/admin/descriptions";

    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    private readonly PostgresFixture _postgres;

    public AdminDescriptionEndpointsTests(PostgresFixture postgres) => _postgres = postgres;

    // ---------------------------------------------------------------------------------
    // TC-RES-018 — Review queue with status filter and KAP source refs
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task TC_RES_018_review_queue_with_status_filter_and_kap_source_refs()
    {
        await ResetAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var builder = await SignedInClientAsync(factory, BuilderEmail);

        // --- status=draft → the two drafts (REST, ZETA), each with its resolved refs.
        using (var draftResponse = await GetAsync(builder, $"{QueuePath}?status=draft"))
        {
            Assert.Equal(HttpStatusCode.OK, draftResponse.StatusCode);
            using var document = JsonDocument.Parse(await draftResponse.Content.ReadAsStringAsync());
            var drafts = document.RootElement.GetProperty("descriptions").EnumerateArray().ToList();
            Assert.Equal(2, drafts.Count);
            Assert.All(drafts, entry => Assert.Equal("draft", entry.GetProperty("status").GetString()));

            var rest = drafts.Single(entry => entry.GetProperty("symbol").GetString() == "REST");
            var zeta = drafts.Single(entry => entry.GetProperty("symbol").GetString() == "ZETA");

            // The queue payload *is* the editor payload: both texts ride along (FR-RES-021).
            Assert.Equal(1, rest.GetProperty("version").GetInt32());
            Assert.False(string.IsNullOrWhiteSpace(rest.GetProperty("textTr").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(rest.GetProperty("textEn").GetString()));
            // ZETA's EN text is empty (FU §9b) — the key is served, still empty.
            Assert.Equal(string.Empty, zeta.GetProperty("textEn").GetString());

            // sourceRefs resolved against kap_disclosures (id, type, publish date, title, URL).
            var restRef = Assert.Single(rest.GetProperty("sourceRefs").EnumerateArray());
            Assert.Equal(8842, restRef.GetProperty("disclosureId").GetInt64());
            Assert.Equal("financial_report", restRef.GetProperty("disclosureType").GetString());
            Assert.StartsWith(
                L2.Anchor.Calendar(-300).ToString("yyyy-MM-dd"),
                restRef.GetProperty("publishDate").GetString());
            Assert.Equal("Rest Telekom Finansal Rapor", restRef.GetProperty("title").GetString());
            Assert.Equal("https://www.kap.org.tr/tr/Bildirim/8842", restRef.GetProperty("sourceUrl").GetString());
        }

        // --- status=published → ALFA, whose refs include an unresolvable id (9999).
        using (var publishedResponse = await GetAsync(builder, $"{QueuePath}?status=published"))
        {
            Assert.Equal(HttpStatusCode.OK, publishedResponse.StatusCode);
            using var document = JsonDocument.Parse(await publishedResponse.Content.ReadAsStringAsync());
            var alfa = Assert.Single(document.RootElement.GetProperty("descriptions").EnumerateArray());
            Assert.Equal("ALFA", alfa.GetProperty("symbol").GetString());
            Assert.Equal("published", alfa.GetProperty("status").GetString());

            var refs = alfa.GetProperty("sourceRefs").EnumerateArray().ToList();
            Assert.Equal(2, refs.Count);

            var resolved = refs.Single(ref_ => ref_.GetProperty("disclosureId").GetInt64() == 8841);
            Assert.Equal("annual_report", resolved.GetProperty("disclosureType").GetString());
            Assert.Equal("2025 Faaliyet Raporu", resolved.GetProperty("title").GetString());

            // The unresolvable id is served as a {disclosureId}-only stub — never dropped.
            var stub = refs.Single(ref_ => ref_.GetProperty("disclosureId").GetInt64() == 9999);
            Assert.Equal(
                new[] { "disclosureId" },
                stub.EnumerateObject().Select(property => property.Name).ToArray());
        }

        // --- non-builder → 403 FORBIDDEN (AD-13 role gate).
        using var user = await SignedInClientAsync(factory, UserAEmail);
        using var forbidden = await GetAsync(user, $"{QueuePath}?status=draft");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal("FORBIDDEN", await CodeAsync(forbidden));
    }

    // ---------------------------------------------------------------------------------
    // TC-RES-019 — Editor saves both languages; provenance not editable
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task TC_RES_019_editor_saves_both_languages_and_provenance_is_not_editable()
    {
        await ResetAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var builder = await SignedInClientAsync(factory, BuilderEmail);

        var restId = await DescriptionIdAsync("REST");

        // --- PATCH both languages → saved, reflected on the next queue read.
        var csrf = await CsrfTokenAsync(builder);
        using (var patched = await PatchAsync(
                   builder,
                   $"{QueuePath}/{restId}",
                   new { textTr = "Rest Telekom güncellendi.", textEn = "Rest Telekom updated." },
                   csrf))
        {
            Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        }

        using (var queue = await GetAsync(builder, $"{QueuePath}?status=draft"))
        {
            Assert.Equal(HttpStatusCode.OK, queue.StatusCode);
            using var document = JsonDocument.Parse(await queue.Content.ReadAsStringAsync());
            var rest = document.RootElement.GetProperty("descriptions").EnumerateArray()
                .Single(entry => entry.GetProperty("id").GetInt64() == restId);
            Assert.Equal("Rest Telekom güncellendi.", rest.GetProperty("textTr").GetString());
            Assert.Equal("Rest Telekom updated.", rest.GetProperty("textEn").GetString());
        }

        // --- a sourceRefs write is rejected outright (strict contract, I-RES-2); the
        //     accompanying text change is NOT applied.
        var csrf2 = await CsrfTokenAsync(builder);
        using (var rejected = await PatchAsync(
                   builder,
                   $"{QueuePath}/{restId}",
                   new { textTr = "must not persist", sourceRefs = new[] { 8842 } },
                   csrf2))
        {
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            using var document = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
            Assert.Equal("VALIDATION_FAILED", document.RootElement.GetProperty("code").GetString());
            var fields = document.RootElement.GetProperty("params").GetProperty("fields").EnumerateArray().ToList();
            Assert.Contains(fields, field => field.GetProperty("field").GetString() == "sourceRefs");
        }

        await using (var db = _postgres.CreateContext())
        {
            var rest = await db.BusinessDescriptions.SingleAsync(description => description.Id == restId);
            Assert.Equal("Rest Telekom güncellendi.", rest.TextTr);
            // Provenance stays exactly as the pipeline wrote it.
            Assert.Equal("[8842]", rest.SourceRefsJson);
        }
    }

    // ---------------------------------------------------------------------------------
    // Negative path — explicit JSON null text is rejected, never written (I-RES-2)
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task Patch_rejects_json_null_text_as_invalid()
    {
        await ResetAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var builder = await SignedInClientAsync(factory, BuilderEmail);

        var restId = await DescriptionIdAsync("REST");
        var originalTr = await TextTrAsync(restId);

        var csrf = await CsrfTokenAsync(builder);
        using (var rejected = await PatchAsync(
                   builder,
                   $"{QueuePath}/{restId}",
                   new { textTr = (string?)null },
                   csrf))
        {
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            using var document = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
            Assert.Equal("VALIDATION_FAILED", document.RootElement.GetProperty("code").GetString());
            var fields = document.RootElement.GetProperty("params").GetProperty("fields").EnumerateArray().ToList();
            Assert.Contains(fields, field => field.GetProperty("field").GetString() == "textTr");
        }

        // The rejected request leaves the stored text untouched.
        Assert.Equal(originalTr, await TextTrAsync(restId));
    }

    // ---------------------------------------------------------------------------------
    // TC-RES-020 — Publication gate
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task TC_RES_020_publication_gate_requires_both_languages()
    {
        await ResetAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var builder = await SignedInClientAsync(factory, BuilderEmail);

        var restId = await DescriptionIdAsync("REST");
        var zetaId = await DescriptionIdAsync("ZETA");

        // --- REST has both texts → publish succeeds and stamps review/publication.
        var csrf = await CsrfTokenAsync(builder);
        using (var published = await PostAsync(builder, $"{QueuePath}/{restId}/publish", body: null, csrf))
        {
            Assert.Equal(HttpStatusCode.OK, published.StatusCode);
            using var document = JsonDocument.Parse(await published.Content.ReadAsStringAsync());
            var root = document.RootElement;
            Assert.Equal("published", root.GetProperty("status").GetString());
            Assert.StartsWith(
                factory.Clock.GetUtcNow().ToString("yyyy-MM-ddTHH:mm:ss"),
                root.GetProperty("publishedAt").GetString());
            Assert.StartsWith(
                factory.Clock.GetUtcNow().ToString("yyyy-MM-ddTHH:mm:ss"),
                root.GetProperty("lastReviewedAt").GetString());
        }

        // --- ZETA's EN text is empty → rejected, naming the missing language; both texts
        //     and the status are preserved.
        var csrf2 = await CsrfTokenAsync(builder);
        using (var rejected = await PostAsync(builder, $"{QueuePath}/{zetaId}/publish", body: null, csrf2))
        {
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            using var document = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
            Assert.Equal("VALIDATION_FAILED", document.RootElement.GetProperty("code").GetString());
            var fields = document.RootElement.GetProperty("params").GetProperty("fields").EnumerateArray().ToList();
            Assert.Contains(fields, field => field.GetProperty("field").GetString() == "textEn");
        }

        await using (var db = _postgres.CreateContext())
        {
            var rest = await db.BusinessDescriptions.SingleAsync(description => description.Id == restId);
            Assert.Equal("published", rest.Status);
            Assert.NotNull(rest.PublishedAt);
            Assert.NotNull(rest.LastReviewedAt);

            var zeta = await db.BusinessDescriptions.SingleAsync(description => description.Id == zetaId);
            Assert.Equal("draft", zeta.Status);
            Assert.Null(zeta.PublishedAt);
            Assert.False(string.IsNullOrWhiteSpace(zeta.TextTr));
            Assert.Equal(string.Empty, zeta.TextEn);
        }
    }

    // ---------------------------------------------------------------------------------
    // TC-RES-021 — Publication effect on the public page
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task TC_RES_021_publishing_flips_the_public_page_to_published()
    {
        await ResetAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var builder = await SignedInClientAsync(factory, BuilderEmail);

        // Before publishing, REST's public page is honestly "preparing" (its row is a draft).
        using (var before = await GetAsync(builder, "/api/v1/stocks/REST/overview"))
        {
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);
            using var document = JsonDocument.Parse(await before.Content.ReadAsStringAsync());
            Assert.Equal("preparing", document.RootElement.GetProperty("status").GetString());
        }

        var restId = await DescriptionIdAsync("REST");
        var csrf = await CsrfTokenAsync(builder);
        using (var published = await PostAsync(builder, $"{QueuePath}/{restId}/publish", body: null, csrf))
        {
            Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        }

        // The public page flips to published with its last-reviewed date — no cache clearing.
        using var after = await GetAsync(builder, "/api/v1/stocks/REST/overview");
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        using var afterDocument = JsonDocument.Parse(await after.Content.ReadAsStringAsync());
        var root = afterDocument.RootElement;
        Assert.Equal("published", root.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("textTr").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("textEn").GetString()));
        Assert.StartsWith(
            factory.Clock.GetUtcNow().ToString("yyyy-MM-ddTHH:mm:ss"),
            root.GetProperty("lastReviewedAt").GetString());
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    /// <summary>Restores the seeded description state: the shared container is reused
    /// across tests, so the mutable <c>business_descriptions</c> rows are dropped and
    /// re-seeded to the FU §9b baseline (draft/published statuses and texts).</summary>
    private async Task ResetAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM business_descriptions");
        await FixtureSeeder.ApplyAsync(db, L2);
    }

    private async Task<long> DescriptionIdAsync(string symbol)
    {
        await using var db = _postgres.CreateContext();
        var instrumentId = await db.Instruments
            .Where(instrument => instrument.Symbol == symbol)
            .Select(instrument => instrument.Id)
            .SingleAsync();
        return await db.BusinessDescriptions
            .Where(description => description.InstrumentId == instrumentId)
            .Select(description => description.Id)
            .SingleAsync();
    }

    private async Task<string?> TextTrAsync(long descriptionId)
    {
        await using var db = _postgres.CreateContext();
        return await db.BusinessDescriptions
            .Where(description => description.Id == descriptionId)
            .Select(description => description.TextTr)
            .SingleAsync();
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    private static string NextClientIp() => Guid.NewGuid().ToString("N");

    /// <summary>Establishes a real session through the Identity sign-in pipeline and
    /// returns a client carrying its cookie (mirrors the other admin suites).</summary>
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

    private static async Task<HttpResponseMessage> PatchAsync(HttpClient client, string url, object? body, string? csrf)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, url);
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

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
