using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;
using Degerli.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-val-004 acceptance — the DCF compute endpoint
/// <c>POST /api/v1/stocks/{symbol}/dcf/compute</c> (Group B of
/// `.pipeline/testing/valuation-dcf.md`, TC-VAL-009..012).
///
/// Each test runs against the real, migrated Testcontainers PostgreSQL seeded with
/// the L2 fixture universe (ALFA canonical T price 20.00) via the in-process host.
/// The goldens are the hand-derived values from the test plan §1 / FU §11.5 — never
/// re-derived from the implementation.
/// </summary>
public sealed class DcfComputeEndpointTests : IClassFixture<PostgresFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    private readonly PostgresFixture _postgres;

    public DcfComputeEndpointTests(PostgresFixture postgres) => _postgres = postgres;

    // -- TC-VAL-009 — POST compute happy path ----------------------------------

    [Fact]
    public async Task TC_VAL_009_post_compute_returns_point_result_grid_and_canonical_price()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await PostComputeAsync(client, "ALFA", Baseline());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Anonymous, stateless, never cached (`03` §13): no auth was presented and the
        // dynamic compute surface carries Cache-Control: no-store.
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.False(response.Headers.Contains("Set-Cookie"));

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        // Point result (test plan §1 / FU §11.5).
        Assert.Equal(13.1969m, root.GetProperty("fairValuePerShare").GetDecimal(), 4);
        Assert.Equal(-0.5155m, root.GetProperty("marginOfSafety").GetDecimal(), 4);

        // Canonical price object.
        var price = root.GetProperty("price");
        Assert.Equal(20.00m, price.GetProperty("value").GetDecimal());
        Assert.Equal(T.ToString("yyyy-MM-dd"), price.GetProperty("asOf").GetString());
        Assert.False(price.GetProperty("stale").GetBoolean());

        // Full 5×5 sensitivity grid (FR-VAL-007 / TC-VAL-002).
        var sensitivity = root.GetProperty("sensitivity");
        Assert.Equal(
            new[] { 0.10m, 0.11m, 0.12m, 0.13m, 0.14m },
            sensitivity.GetProperty("discountRates").EnumerateArray().Select(e => e.GetDecimal()).ToArray());
        Assert.Equal(
            new[] { 0.01m, 0.02m, 0.03m, 0.04m, 0.05m },
            sensitivity.GetProperty("terminalGrowths").EnumerateArray().Select(e => e.GetDecimal()).ToArray());

        var grid = sensitivity.GetProperty("fairValues").EnumerateArray()
            .Select(row => row.EnumerateArray().Select(cell => cell.GetDecimal()).ToArray())
            .ToArray();
        Assert.Equal(5, grid.Length);
        Assert.All(grid, row => Assert.Equal(5, row.Length));
        Assert.Equal(13.1969m, grid[2][2], 4);
        Assert.Equal(24.0000m, grid[0][4], 4);
        Assert.Equal(8.99623m, grid[4][0], 5);
    }

    [Fact]
    public async Task TC_VAL_009_repeat_post_is_identical_and_persists_nothing()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        var before = await TableCountsAsync();

        using var first = await PostComputeAsync(client, "ALFA", Baseline());
        var firstBody = await first.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var second = await PostComputeAsync(client, "ALFA", Baseline());
        var secondBody = await second.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        // Stateless: identical request → byte-identical response.
        Assert.Equal(firstBody, secondBody);

        // No persistence anywhere: every table is exactly as it was before.
        var after = await TableCountsAsync();
        Assert.Equal(before, after);
    }

    // -- TC-VAL-010 — Compute not-computable → 422 -----------------------------

    [Fact]
    public async Task TC_VAL_010_discount_rate_not_above_terminal_growth_is_422()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        var parameters = Baseline();
        parameters["discount_rate"] = 0.03m;
        parameters["terminal_growth"] = 0.04m;

        using var response = await PostComputeAsync(client, "ALFA", parameters);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("DCF_NOT_COMPUTABLE", problem.RootElement.GetProperty("code").GetString());
        var parametersElement = problem.RootElement.GetProperty("params");
        Assert.Equal(
            "DISCOUNT_RATE_NOT_GREATER_THAN_TERMINAL_GROWTH",
            parametersElement.GetProperty("constraint").GetString());
        Assert.Equal("discount_rate", parametersElement.GetProperty("field").GetString());
    }

    [Fact]
    public async Task TC_VAL_010_horizon_outside_range_is_422()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        var parameters = Baseline();
        parameters["horizon_years"] = 11;

        using var response = await PostComputeAsync(client, "ALFA", parameters);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("DCF_NOT_COMPUTABLE", problem.RootElement.GetProperty("code").GetString());
        var parametersElement = problem.RootElement.GetProperty("params");
        Assert.Equal("HORIZON_OUT_OF_RANGE", parametersElement.GetProperty("constraint").GetString());
        Assert.Equal("horizon_years", parametersElement.GetProperty("field").GetString());
    }

    // -- TC-VAL-011 — Compute input validation ---------------------------------

    [Fact]
    public async Task TC_VAL_011_non_numeric_parameter_is_400_with_the_offending_field()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        var parameters = Baseline();
        parameters["growth_rate"] = "hızlı"; // a JSON string, not a number

        using var response = await PostComputeAsync(client, "ALFA", parameters);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal("VALIDATION_FAILED", problem.RootElement.GetProperty("code").GetString());
        var field = Assert.Single(ProblemFields(problem), f => f.Field == "growth_rate");
        Assert.Equal("not_a_number", field.Code);
    }

    [Fact]
    public async Task TC_VAL_011_missing_parameter_is_400_with_the_offending_field()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        var parameters = Baseline();
        parameters.Remove("cash");

        using var response = await PostComputeAsync(client, "ALFA", parameters);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal("VALIDATION_FAILED", problem.RootElement.GetProperty("code").GetString());
        var field = Assert.Single(ProblemFields(problem), f => f.Field == "cash");
        Assert.Equal("required", field.Code);
    }

    [Fact]
    public async Task TC_VAL_011_out_of_range_magnitude_is_400_but_finite_extremes_compute()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        // A magnitude beyond the JSON decimal representation range → 400.
        var beyondDecimal = Baseline();
        beyondDecimal["base_fcf"] = "1e30"; // raw numeric literal emitted below
        using (var outOfRange = await PostComputeRawAsync(client, "ALFA", RawWithExpected(beyondDecimal, "base_fcf")))
        {
            Assert.Equal(HttpStatusCode.BadRequest, outOfRange.StatusCode);
            var problem = await ReadProblemAsync(outOfRange);
            Assert.Equal("VALIDATION_FAILED", problem.RootElement.GetProperty("code").GetString());
            var field = Assert.Single(ProblemFields(problem), f => f.Field == "base_fcf");
            Assert.Equal("out_of_range", field.Code);
        }

        // Well-formed huge-but-finite values still compute (no correctness policing of
        // the user's assumptions — RISK-VAL-001).
        var huge = Baseline();
        huge["base_fcf"] = 1_000_000_000_000_000m; // 1e15, within decimal range
        huge["share_count"] = 1_000_000_000m;
        using var hugeResponse = await PostComputeAsync(client, "ALFA", huge);
        Assert.Equal(HttpStatusCode.OK, hugeResponse.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public async Task TC_VAL_011_horizon_0_and_11_are_422_not_400(int horizon)
    {
        // I-VAL-2: horizon outside 1..10 is a math constraint (422), not validation.
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        var parameters = Baseline();
        parameters["horizon_years"] = horizon;

        using var response = await PostComputeAsync(client, "ALFA", parameters);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("DCF_NOT_COMPUTABLE", problem.RootElement.GetProperty("code").GetString());
    }

    // -- TC-VAL-012 — Price consistency across surfaces ------------------------

    [Fact]
    public async Task TC_VAL_012_compute_price_equals_the_canonical_daily_prices_row()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await PostComputeAsync(client, "ALFA", Baseline());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var price = document.RootElement.GetProperty("price");

        await using var db = _postgres.CreateContext();
        var alfaId = await db.Instruments.Where(i => i.Symbol == "ALFA").Select(i => i.Id).SingleAsync();
        var canonical = await db.DailyPrices
            .Where(p => p.InstrumentId == alfaId && p.PriceDate == T)
            .SingleAsync();

        // The value the stock page and screener derive from — the single canonical row.
        Assert.Equal(canonical.CloseRaw, price.GetProperty("value").GetDecimal());
        Assert.Equal(canonical.PriceDate.ToString("yyyy-MM-dd"), price.GetProperty("asOf").GetString());
    }

    // ---------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------

    private async Task SeedAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
    }

    /// <summary>The canonical ALFA baseline parameter set (FU §11.5), snake_case wire names.</summary>
    private static Dictionary<string, object?> Baseline() => new()
    {
        ["base_fcf"] = 100m,
        ["growth_rate"] = 0.10m,
        ["horizon_years"] = 5,
        ["terminal_growth"] = 0.03m,
        ["discount_rate"] = 0.12m,
        ["debt"] = 400m,
        ["cash"] = 200m,
        ["share_count"] = 100m,
    };

    private static HttpClient CreateClient(DegerliAppFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

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

    /// <summary>POST a raw JSON body (used to emit literals outside the decimal range).</summary>
    private static async Task<HttpResponseMessage> PostComputeRawAsync(
        HttpClient client,
        string symbol,
        string json)
    {
        var csrf = await FetchCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/stocks/{symbol}/dcf/compute")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-CSRF-Token", csrf);
        request.Headers.Add("X-Forwarded-For", Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }

    /// <summary>Serializes the baseline, substituting one field with a raw numeric literal.</summary>
    private static string RawWithExpected(IDictionary<string, object?> payload, string rawField)
    {
        var parts = payload.Select(pair =>
            pair.Key == rawField
                ? $"\"{pair.Key}\": {pair.Value}"
                : $"\"{pair.Key}\": {JsonSerializer.Serialize(pair.Value)}");
        return "{" + string.Join(",", parts) + "}";
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

    private static async Task<JsonDocument> ReadProblemAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static IEnumerable<(string Field, string Code)> ProblemFields(JsonDocument problem)
        => problem.RootElement
            .GetProperty("params")
            .GetProperty("fields")
            .EnumerateArray()
            .Select(field => (
                field.GetProperty("field").GetString()!,
                field.GetProperty("code").GetString()!));

    /// <summary>Row counts of the tables a compute request must never touch.</summary>
    private async Task<Dictionary<string, long>> TableCountsAsync()
    {
        await using var db = _postgres.CreateContext();
        return new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["daily_prices"] = await db.DailyPrices.LongCountAsync(),
            ["instruments"] = await db.Instruments.LongCountAsync(),
            ["financial_statements"] = await db.FinancialStatements.LongCountAsync(),
            ["dcf_baselines"] = await db.DcfBaselines.LongCountAsync(),
            ["dcf_scenarios"] = await db.DcfScenarios.LongCountAsync(),
            ["users"] = await db.Users.LongCountAsync(),
            ["saved_screens"] = await db.SavedScreens.LongCountAsync(),
            ["derived_metrics"] = await db.DerivedMetrics.LongCountAsync(),
            ["consent_records"] = await db.ConsentRecords.LongCountAsync(),
        };
    }
}
