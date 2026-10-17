using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;
using Degerli.Ingestion.Prices;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-mdf-011 acceptance — the builder-only quarantine review surface (`03` §8, AD-13):
/// the open/dismissed review queue (TC-MDF-021), note-required dismissal that records the
/// accepted gap and never deletes (TC-MDF-022), dismissed items remaining reviewable
/// (TC-MDF-023) and the quarantine → run ledger → re-ingestion path (TC-MDF-024). Every
/// test runs through the real in-process API host against a migrated, fixture-seeded
/// Testcontainers PostgreSQL; the re-ingestion test points the <c>prices</c> source at the
/// WireMock double.
/// </summary>
public sealed class AdminQuarantineEndpointsTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private const string BuilderEmail = "builder@degerli.test";
    private const string FixturePassword = "FixturePass1!";
    private const string QueuePath = "/api/v1/admin/quarantine";
    private const string PricesEodPath = "/prices/eod";
    private const string PricesBackfillPath = "/prices/history";

    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public AdminQuarantineEndpointsTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    // ---------------------------------------------------------------------------------
    // TC-MDF-021 — Quarantine queue payload serves review fields
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task TC_MDF_021_Quarantine_queue_serves_review_fields()
    {
        await ResetAsync();

        var quarantinedAt = new DateTimeOffset(T.ToDateTime(new TimeOnly(21, 14, 3)), TimeSpan.Zero);
        await using (var db = _postgres.CreateContext())
        {
            db.QuarantinedFacts.AddRange(
                new QuarantinedFact
                {
                    JobCode = "prices",
                    SourceRef = "isbank://eod/ALFA",
                    PayloadJson = "{\"symbol\":\"ALFA\",\"close\":-5}",
                    ReasonCode = "NEGATIVE_PRICE",
                    QuarantinedAt = quarantinedAt,
                    Status = "open",
                },
                new QuarantinedFact
                {
                    JobCode = "statements",
                    SourceRef = "kap://statements/PART",
                    PayloadJson = "{\"symbol\":\"PART\",\"netIncome\":\"N/A\"}",
                    ReasonCode = "SCHEMA_MISMATCH",
                    QuarantinedAt = quarantinedAt.AddMinutes(5),
                    Status = "open",
                },
                new QuarantinedFact
                {
                    JobCode = "prices",
                    SourceRef = "isbank://eod/OLDM",
                    PayloadJson = "{\"symbol\":\"OLDM\"}",
                    ReasonCode = "MISSING_PROVENANCE",
                    QuarantinedAt = quarantinedAt.AddMinutes(9),
                    Status = "dismissed",
                    ResolutionNote = "source error accepted",
                });
            await db.SaveChangesAsync();
        }

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var builder = await SignedInClientAsync(factory, BuilderEmail);
        using var response = await GetAsync(builder, $"{QueuePath}?status=open");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = document.RootElement.GetProperty("items").EnumerateArray().ToList();

        // Only the open items are listed (the dismissed one belongs to the dismissed queue).
        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.Equal("open", item.GetProperty("status").GetString()));

        // Each item exposes its review fields (UXR-MDF-007): job, source, reason, date and
        // the rejected payload, retained and inspectable.
        var prices = items.Single(item => item.GetProperty("jobCode").GetString() == "prices");
        Assert.Equal("isbank://eod/ALFA", prices.GetProperty("sourceRef").GetString());
        Assert.Equal("NEGATIVE_PRICE", prices.GetProperty("reasonCode").GetString());
        Assert.True(prices.GetProperty("id").GetInt64() > 0);
        Assert.StartsWith(quarantinedAt.ToString("yyyy-MM-ddTHH:mm:ss"), prices.GetProperty("quarantinedAt").GetString());

        var payload = prices.GetProperty("payloadJson");
        Assert.Equal(JsonValueKind.Object, payload.ValueKind);
        Assert.Equal("ALFA", payload.GetProperty("symbol").GetString());
        Assert.Equal(-5, payload.GetProperty("close").GetInt32());

        var statements = items.Single(item => item.GetProperty("jobCode").GetString() == "statements");
        Assert.Equal("kap://statements/PART", statements.GetProperty("sourceRef").GetString());
        Assert.Equal("SCHEMA_MISMATCH", statements.GetProperty("reasonCode").GetString());
        Assert.Equal("PART", statements.GetProperty("payloadJson").GetProperty("symbol").GetString());
    }

    // ---------------------------------------------------------------------------------
    // TC-MDF-022 — Dismissal requires a note and records the accepted gap
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task TC_MDF_022_Dismissal_requires_a_note_and_records_the_accepted_gap()
    {
        await ResetAsync();

        long toDismiss;
        long untouched;
        await using (var db = _postgres.CreateContext())
        {
            var quarantinedAt = new DateTimeOffset(T.ToDateTime(new TimeOnly(21, 14, 3)), TimeSpan.Zero);
            var first = new QuarantinedFact
            {
                JobCode = "prices",
                SourceRef = "isbank://eod/ALFA",
                PayloadJson = "{\"symbol\":\"ALFA\",\"close\":-5}",
                ReasonCode = "NEGATIVE_PRICE",
                QuarantinedAt = quarantinedAt,
                Status = "open",
            };
            var second = new QuarantinedFact
            {
                JobCode = "prices",
                SourceRef = "isbank://eod/BETA",
                PayloadJson = "{\"symbol\":\"BETA\",\"close\":-7}",
                ReasonCode = "NEGATIVE_PRICE",
                QuarantinedAt = quarantinedAt.AddMinutes(1),
                Status = "open",
            };
            db.QuarantinedFacts.AddRange(first, second);
            await db.SaveChangesAsync();
            toDismiss = first.Id;
            untouched = second.Id;
        }

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var builder = await SignedInClientAsync(factory, BuilderEmail);

        // --- non-empty note: accepted gap recorded, item moved to dismissed.
        var csrf = await CsrfTokenAsync(builder);
        using (var accepted = await PostAsync(
                   builder,
                   $"{QueuePath}/{toDismiss}/dismiss",
                   body: new { note = "source error accepted" },
                   csrf))
        {
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            using var document = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync());
            Assert.Equal("dismissed", document.RootElement.GetProperty("status").GetString());
            Assert.Equal("source error accepted", document.RootElement.GetProperty("resolutionNote").GetString());
        }

        await using (var db = _postgres.CreateContext())
        {
            var dismissed = await db.QuarantinedFacts.SingleAsync(fact => fact.Id == toDismiss);
            Assert.Equal("dismissed", dismissed.Status);
            Assert.Equal("source error accepted", dismissed.ResolutionNote);
            // Nothing is ever deleted (BR-MDF-007).
            Assert.Contains("ALFA", dismissed.PayloadJson);
        }

        // --- empty note: inline validation failure naming the field; nothing changed.
        var csrf2 = await CsrfTokenAsync(builder);
        using (var rejected = await PostAsync(
                   builder,
                   $"{QueuePath}/{untouched}/dismiss",
                   body: new { note = "" },
                   csrf2))
        {
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            using var document = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
            Assert.Equal("VALIDATION_FAILED", document.RootElement.GetProperty("code").GetString());
            var fields = document.RootElement.GetProperty("params").GetProperty("fields").EnumerateArray().ToList();
            Assert.Contains(fields, field => field.GetProperty("field").GetString() == "note");
        }

        await using (var db = _postgres.CreateContext())
        {
            var stillOpen = await db.QuarantinedFacts.SingleAsync(fact => fact.Id == untouched);
            Assert.Equal("open", stillOpen.Status);
            Assert.Null(stillOpen.ResolutionNote);

            // Nothing deleted either way — both rows survive.
            Assert.Equal(2, await db.QuarantinedFacts.CountAsync());
        }
    }

    // ---------------------------------------------------------------------------------
    // TC-MDF-023 — Dismissed items remain reviewable
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task TC_MDF_023_Dismissed_items_remain_reviewable()
    {
        await ResetAsync();

        var quarantinedAt = new DateTimeOffset(T.ToDateTime(new TimeOnly(21, 14, 3)), TimeSpan.Zero);
        await using (var db = _postgres.CreateContext())
        {
            db.QuarantinedFacts.Add(new QuarantinedFact
            {
                JobCode = "prices",
                SourceRef = "isbank://eod/OLDM",
                PayloadJson = "{\"symbol\":\"OLDM\",\"close\":-3}",
                ReasonCode = "MISSING_PROVENANCE",
                QuarantinedAt = quarantinedAt,
                Status = "dismissed",
                ResolutionNote = "upstream feed glitch — gap accepted",
            });
            await db.SaveChangesAsync();
        }

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var builder = await SignedInClientAsync(factory, BuilderEmail);
        using var response = await GetAsync(builder, $"{QueuePath}?status=dismissed");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray());

        Assert.Equal("dismissed", item.GetProperty("status").GetString());
        Assert.Equal("upstream feed glitch — gap accepted", item.GetProperty("resolutionNote").GetString());

        // The original rejected payload is still inspectable alongside the note.
        var payload = item.GetProperty("payloadJson");
        Assert.Equal("OLDM", payload.GetProperty("symbol").GetString());
        Assert.Equal(-3, payload.GetProperty("close").GetInt32());
    }

    // ---------------------------------------------------------------------------------
    // TC-MDF-024 — Quarantine → run ledger → re-ingestion path
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task TC_MDF_024_Quarantine_run_ledger_re_ingestion_path()
    {
        await ResetAsync(clearPrices: true);

        await using (var db = _postgres.CreateContext())
        {
            // The failed run that produced the quarantine item.
            db.IngestRuns.Add(new IngestRun
            {
                JobCode = "prices",
                Status = "failed",
                StartedAt = new DateTimeOffset(T.ToDateTime(new TimeOnly(20, 30, 0)), TimeSpan.Zero),
                FinishedAt = new DateTimeOffset(T.ToDateTime(new TimeOnly(20, 30, 5)), TimeSpan.Zero),
                StatsJson = "{}",
            });

            // The open quarantine item the builder navigates from (UXR-MDF-010).
            db.QuarantinedFacts.Add(new QuarantinedFact
            {
                JobCode = "prices",
                SourceRef = "isbank://eod/ALFA",
                PayloadJson = "{\"symbol\":\"ALFA\",\"close\":-5}",
                ReasonCode = "NEGATIVE_PRICE",
                QuarantinedAt = new DateTimeOffset(T.ToDateTime(new TimeOnly(20, 30, 5)), TimeSpan.Zero),
                Status = "open",
            });
            await db.SaveChangesAsync();
        }

        // The source is fixed (the stub is restored/serving good data).
        StubPricesSources();

        await using var factory = PricesFactory();
        using var builder = await SignedInClientAsync(factory, BuilderEmail);

        // The builder starts from the quarantine item (UXR-MDF-010) and navigates to the
        // run-ledger view filtered to that item's job.
        string job;
        using (var queue = await GetAsync(builder, $"{QueuePath}?status=open"))
        {
            Assert.Equal(HttpStatusCode.OK, queue.StatusCode);
            using var document = JsonDocument.Parse(await queue.Content.ReadAsStringAsync());
            var item = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray());
            job = item.GetProperty("jobCode").GetString()!;
        }

        Assert.Equal("prices", job);

        // The filtered ledger view for the quarantined item's job returns the failed run.
        using (var ledger = await GetAsync(builder, $"/api/v1/admin/ingest-runs?job={job}"))
        {
            Assert.Equal(HttpStatusCode.OK, ledger.StatusCode);
            using var document = JsonDocument.Parse(await ledger.Content.ReadAsStringAsync());
            var runs = document.RootElement.GetProperty("runs").EnumerateArray().ToList();
            var failed = Assert.Single(runs);
            Assert.Equal("prices", failed.GetProperty("job").GetString());
            Assert.Equal("failed", failed.GetProperty("status").GetString());
        }

        // Re-trigger the job after the fix — the re-ingestion path.
        var csrf = await CsrfTokenAsync(builder);
        using (var retrigger = await PostAsync(builder, "/api/v1/admin/ingest/prices/run", body: null, csrf))
        {
            Assert.Equal(HttpStatusCode.OK, retrigger.StatusCode);
        }

        await using (var db = _postgres.CreateContext())
        {
            // A new run appears and succeeds.
            var runs = await db.IngestRuns
                .Where(run => run.JobCode == "prices")
                .OrderBy(run => run.Id)
                .ToListAsync();
            Assert.Equal(2, runs.Count);
            Assert.Equal("succeeded", runs[1].Status);

            // Re-ingestion does not auto-dismiss: the quarantine item remains until the
            // builder dismisses it (BR-MDF-007).
            var item = Assert.Single(await db.QuarantinedFacts.ToListAsync());
            Assert.Equal("open", item.Status);
            Assert.Null(item.ResolutionNote);
        }
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    /// <summary>Resets the shared container: re-applies the fixture universe and clears
    /// the mutable tables each test owns.</summary>
    private async Task ResetAsync(bool clearPrices = false)
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM quarantined_facts");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM coverage_metadata");
        if (clearPrices)
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM daily_prices");
        }
    }

    private void StubPricesSources()
    {
        _wireMock.Server.ResetMappings();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesBackfillSourceLimited, path: PricesBackfillPath);
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesOk, path: PricesEodPath);
    }

    /// <summary>The API host with the <c>prices</c> source pointed at the WireMock double.</summary>
    private WebApplicationFactory<Program> PricesFactory() =>
        new DegerliAppFactory(_postgres.ConnectionString).WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.PostConfigure<PricesSourceOptions>(options =>
                {
                    options.BaseUrl = _wireMock.BaseUrl;
                    options.Path = PricesEodPath;
                    options.BackfillPath = PricesBackfillPath;
                    // The L2 harness binds the loop to the seeded FU XU100 (§8 preamble) and
                    // disables pacing, so the admin-triggered run resolves 16 constituents.
                    options.UniverseIndex = "XU100";
                    options.RequestDelayMs = 0;
                })));

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    private static string NextClientIp() => Guid.NewGuid().ToString("N");

    /// <summary>Establishes a real session through the Identity sign-in pipeline (the
    /// path <c>POST /auth/login</c> uses) and returns a client carrying its cookie.</summary>
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

    private static async Task<string> CsrfTokenAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/csrf-token");
        request.Headers.Add("X-Forwarded-For", NextClientIp());
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("token").GetString()!;
    }
}
