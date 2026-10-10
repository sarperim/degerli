using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Valuation;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-val-003 acceptance — the DCF baseline generation job + the baseline read
/// endpoint <c>GET /api/v1/stocks/{symbol}/dcf</c> (Group B of
/// <c>.pipeline/testing/valuation-dcf.md</c>, TC-VAL-005..008).
///
/// Every test runs against the real, migrated Testcontainers PostgreSQL seeded with
/// the L2 fixture universe. The <c>dcf-baselines</c> job (C3b) is then run through
/// the real job runner exactly as the scheduler/admin trigger would, so the
/// <c>dcf_baselines</c> rows the endpoint serves are derived from the seeded canonical
/// facts + the builder default rules (<c>02</c> §5.8). The goldens are the hand-derived
/// values from FU §11.5 / the VAL plan §1 — never re-derived from the implementation.
/// </summary>
public sealed class DcfBaselineEndpointTests : IClassFixture<PostgresFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    /// <summary>
    /// The canonical-fact as-of / baseline build date (FU §11.5: "asOf R−6d"). The job
    /// is date-parameterised; running it at R−6d reproduces the fixture contract.
    /// </summary>
    private static DateOnly BuildDate => L2.Anchor.Calendar(-6);

    private readonly PostgresFixture _postgres;

    public DcfBaselineEndpointTests(PostgresFixture postgres) => _postgres = postgres;

    // -- TC-VAL-005 — GET baseline payload -------------------------------------

    [Fact]
    public async Task TC_VAL_005_get_baseline_returns_version_params_refs_and_canonical_price()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        await RunBaselineJobAsync(factory, BuildDate);
        using var client = CreateClient(factory);

        using var response = await GetDcfAsync(client, "ALFA");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Anonymous read surface (FT-VAL-001): no session established, publicly cacheable
        // for five minutes per `03` §13.
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.True(response.Headers.CacheControl?.Public);
        Assert.Equal(300, response.Headers.CacheControl?.MaxAge?.TotalSeconds);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal("ALFA", root.GetProperty("symbol").GetString());

        var baseline = root.GetProperty("baseline");
        Assert.Equal(1, baseline.GetProperty("version").GetInt32());
        Assert.Equal(BuildDate.ToString("yyyy-MM-dd"), baseline.GetProperty("buildDate").GetString());

        // The confirmed 8-parameter set (FU §11.5 / OQ-UX-001), exactly.
        var parameters = baseline.GetProperty("params");
        Assert.Equal(8, parameters.EnumerateObject().Count());
        Assert.Equal(100m, parameters.GetProperty("base_fcf").GetDecimal());
        Assert.Equal(0.10m, parameters.GetProperty("growth_rate").GetDecimal());
        Assert.Equal(5, parameters.GetProperty("horizon_years").GetInt32());
        Assert.Equal(0.03m, parameters.GetProperty("terminal_growth").GetDecimal());
        Assert.Equal(0.12m, parameters.GetProperty("discount_rate").GetDecimal());
        Assert.Equal(400m, parameters.GetProperty("debt").GetDecimal());
        Assert.Equal(200m, parameters.GetProperty("cash").GetDecimal());
        Assert.Equal(100m, parameters.GetProperty("share_count").GetDecimal());

        // Canonical-fact refs: every fact-sourced default carries its as-of + restated flag.
        var refs = baseline.GetProperty("canonicalFactRefs");
        foreach (var fact in new[] { "base_fcf", "debt", "cash", "share_count" })
        {
            var reference = refs.GetProperty(fact);
            Assert.Equal(BuildDate.ToString("yyyy-MM-dd"), reference.GetProperty("asOf").GetString());
            Assert.False(reference.GetProperty("restated").GetBoolean());
        }

        // Canonical price from the same daily_prices row every surface derives from.
        var price = root.GetProperty("price");
        Assert.Equal(20.00m, price.GetProperty("value").GetDecimal());
        Assert.Equal(T.ToString("yyyy-MM-dd"), price.GetProperty("asOf").GetString());
        Assert.False(price.GetProperty("stale").GetBoolean());
    }

    // -- TC-VAL-006 — every covered stock has a calculator; unknown 404 ---------

    [Fact]
    public async Task TC_VAL_006_every_covered_stock_has_a_calculator_and_unknown_is_404()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        await RunBaselineJobAsync(factory, BuildDate);
        using var client = CreateClient(factory);

        // All 16 fixtures are covered; each has a usable calculator.
        foreach (var instrument in L2.Instruments)
        {
            using var response = await GetDcfAsync(client, instrument.Symbol);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = document.RootElement;
            Assert.Equal(instrument.Symbol, root.GetProperty("symbol").GetString());
            Assert.True(root.TryGetProperty("baseline", out _));
            Assert.True(root.TryGetProperty("price", out _));

            // PART is the only fixture without a cash-flow statement (TC-VAL-008);
            // every other stock opens complete.
            var missing = root.TryGetProperty("state", out var state)
                && state.GetString() == "missing_inputs";
            Assert.Equal(instrument.Symbol == "PART", missing);
        }

        using var unknown = await GetDcfAsync(client, "NOPE");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        using var problem = JsonDocument.Parse(await unknown.Content.ReadAsStringAsync());
        Assert.Equal("NOT_FOUND", problem.RootElement.GetProperty("code").GetString());
    }

    // -- TC-VAL-007 — baseline from restated facts carries markings ------------

    [Fact]
    public async Task TC_VAL_007_restated_facts_carry_the_restatement_marking()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        await RunBaselineJobAsync(factory, BuildDate);
        using var client = CreateClient(factory);

        using var response = await GetDcfAsync(client, "REST");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var baseline = document.RootElement.GetProperty("baseline");

        // REST's current facts serve the restated FY2025 statements (FU §5):
        // base_fcf = NI 75 + D&A 30 − ΔWC 5 − CAPEX 40 = 60.
        var parameters = baseline.GetProperty("params");
        Assert.Equal(60m, parameters.GetProperty("base_fcf").GetDecimal());
        Assert.Equal(120m, parameters.GetProperty("debt").GetDecimal());
        Assert.Equal(80m, parameters.GetProperty("cash").GetDecimal());
        Assert.Equal(50m, parameters.GetProperty("share_count").GetDecimal());

        var refs = baseline.GetProperty("canonicalFactRefs");
        foreach (var fact in new[] { "base_fcf", "debt", "cash", "share_count" })
        {
            Assert.True(
                refs.GetProperty(fact).GetProperty("restated").GetBoolean(),
                $"expected the '{fact}' canonical-fact ref to carry restated: true");
        }
    }

    // -- TC-VAL-008 — missing statement inputs ---------------------------------

    [Fact]
    public async Task TC_VAL_008_missing_statement_inputs_are_named_and_not_computed()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        await RunBaselineJobAsync(factory, BuildDate);
        using var client = CreateClient(factory);

        using var response = await GetDcfAsync(client, "PART");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        // PART has no cash-flow statement, so the CF-derived base FCF is missing.
        Assert.Equal("missing_inputs", root.GetProperty("state").GetString());
        var missing = root.GetProperty("missingInputs").EnumerateArray()
            .Select(e => e.GetString()!)
            .ToArray();
        Assert.Contains("base_fcf", missing);

        var baseline = root.GetProperty("baseline");
        var parameters = baseline.GetProperty("params");

        // No computation on partial data: the missing fact is not silently zero-filled,
        // and no fair value is returned.
        Assert.False(parameters.TryGetProperty("base_fcf", out _));
        Assert.False(baseline.TryGetProperty("fairValuePerShare", out _));
        Assert.False(root.TryGetProperty("fairValuePerShare", out _));

        // The remaining, derivable inputs and the builder defaults are still served so
        // the calculator is usable once the user supplies the missing value.
        Assert.Equal(350m, parameters.GetProperty("debt").GetDecimal());
        Assert.Equal(150m, parameters.GetProperty("cash").GetDecimal());
        Assert.Equal(120m, parameters.GetProperty("share_count").GetDecimal());
        Assert.Equal(0.10m, parameters.GetProperty("growth_rate").GetDecimal());

        // Supplying all eight values still computes — it is the user's model (RISK-VAL-001).
        var userModel = new Dictionary<string, object?>
        {
            ["base_fcf"] = 100m,
            ["growth_rate"] = 0.10m,
            ["horizon_years"] = 5,
            ["terminal_growth"] = 0.03m,
            ["discount_rate"] = 0.12m,
            ["debt"] = 350m,
            ["cash"] = 150m,
            ["share_count"] = 120m,
        };
        using var computed = await PostComputeAsync(client, "PART", userModel);
        Assert.Equal(HttpStatusCode.OK, computed.StatusCode);
    }

    // ---------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------

    private async Task SeedAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);

        // The four tests share one class-fixture database, and the generation job
        // versions on every run (`02` §5.8); start each test from a baseline-free slate
        // so the asserted version is deterministic rather than execution-order dependent.
        await db.DcfBaselines.ExecuteDeleteAsync();
    }

    /// <summary>Runs the real C3b baseline-generation job through the registered runner.</summary>
    private static async Task RunBaselineJobAsync(DegerliAppFactory factory, DateOnly buildDate)
    {
        using var scope = factory.Services.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IIngestionJobRunner>();
        var result = await runner.RunAsync(
            DcfBaselineGenerationJob.Code,
            new IngestionRequest(buildDate));
        Assert.Equal(IngestResult.Succeeded, result.Status);
    }

    private static HttpClient CreateClient(DegerliAppFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    private static Task<HttpResponseMessage> GetDcfAsync(HttpClient client, string symbol)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/stocks/{symbol}/dcf");
        request.Headers.Add("X-Forwarded-For", Guid.NewGuid().ToString("N"));
        return client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostComputeAsync(
        HttpClient client,
        string symbol,
        IDictionary<string, object?> payload)
    {
        var csrf = await FetchCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/stocks/{symbol}/dcf/compute")
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Add("X-CSRF-Token", csrf);
        request.Headers.Add("X-Forwarded-For", Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }

    private static async Task<string> FetchCsrfTokenAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/csrf-token");
        request.Headers.Add("X-Forwarded-For", Guid.NewGuid().ToString("N"));
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("token").GetString()!;
    }
}
