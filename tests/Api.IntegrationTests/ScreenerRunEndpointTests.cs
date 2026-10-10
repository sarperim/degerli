using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Metrics;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-scr-002 acceptance — the screener metric catalog and the core run endpoint
/// (Group A of <c>.pipeline/testing/stock-screening.md</c>, TC-SCR-001..006/009):
/// <list type="bullet">
/// <item><c>GET /api/v1/screener/metrics</c> — the visible catalog (18 concepts, five
/// families, unit, TR/EN labels, <c>isCagr</c>, sort order; only <c>is_screenable</c>
/// rows; FR-SCR-001, BR-SCR-002, UXR-SCR-011).</item>
/// <item><c>POST /api/v1/screener/run</c> — AND logic over <c>derived_metrics</c> with
/// inclusive min/max/range bounds, per-criterion CAGR window, missing-data
/// exclude-and-count, zero-match as a normal 200 and <c>criteria: []</c> rejected
/// (FR-SCR-001..006/014/015, BR-SCR-003/009).</item>
/// </list>
/// Every test runs against the real migrated Testcontainers PostgreSQL seeded with the
/// L2 fixture universe, then computes the canonical metrics at T through the Metrics
/// Engine job — the run endpoint reads the same <c>derived_metrics</c> rows every other
/// surface serves (BR-SCR-001 / NFR-SCR-003). Goldens are hand-derived in the test plan
/// §1 / FU §11 — never re-derived from the implementation.
/// </summary>
public sealed class ScreenerRunEndpointTests : IClassFixture<PostgresFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    private readonly PostgresFixture _postgres;

    public ScreenerRunEndpointTests(PostgresFixture postgres) => _postgres = postgres;

    // -- TC-SCR-001 — Visible metric catalog -----------------------------------

    [Fact]
    public async Task TC_SCR_001_visible_metric_catalog_returns_18_grouped_by_five_families()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/screener/metrics");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Anonymous read surface (BR-SCR-004): no session is established.
        Assert.False(response.Headers.Contains("Set-Cookie"));

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var families = document.RootElement.GetProperty("families").EnumerateArray().ToList();

        // The five families, in catalog (sort) order.
        Assert.Equal(
            new[] { "valuation", "quality", "growth", "financial_health", "dividends" },
            families.Select(family => family.GetProperty("family").GetString()!).ToArray());

        var metrics = families
            .SelectMany(family => family.GetProperty("metrics").EnumerateArray())
            .ToList();
        Assert.Equal(18, metrics.Count);

        // The three growth-family metrics carry the isCagr flag; a valuation metric does not.
        var isCagr = metrics.ToDictionary(
            metric => metric.GetProperty("metricCode").GetString()!,
            metric => metric.GetProperty("isCagr").GetBoolean(),
            StringComparer.Ordinal);
        Assert.True(isCagr["rev_cagr"]);
        Assert.True(isCagr["eps_cagr"]);
        Assert.True(isCagr["fcf_cagr"]);
        Assert.False(isCagr["pe"]);

        // Plain-language TR/EN labels + units are served (NFR-SCR-002), ordered by sortOrder.
        var pe = metrics.Single(metric => metric.GetProperty("metricCode").GetString() == "pe");
        Assert.Equal("F/K", pe.GetProperty("labelTr").GetString());
        Assert.Equal("P/E", pe.GetProperty("labelEn").GetString());
        Assert.Equal("x", pe.GetProperty("unit").GetString());

        var orders = metrics.Select(metric => metric.GetProperty("sortOrder").GetInt32()).ToList();
        Assert.Equal(orders.OrderBy(order => order).ToList(), orders);

        // Only is_screenable rows are served (UXR-SCR-011): a hidden catalog row is absent.
        await using (var db = _postgres.CreateContext())
        {
            db.MetricCatalog.Add(new MetricCatalogEntry
            {
                MetricCode = "hidden_probe",
                Family = "valuation",
                Unit = "x",
                LabelTr = "Gizli",
                LabelEn = "Hidden",
                IsScreenable = false,
                IsGrowthCagr = false,
                SortOrder = 999,
            });
            await db.SaveChangesAsync();
        }

        using var hiddenResponse = await client.GetAsync("/api/v1/screener/metrics");
        using var hiddenDocument = JsonDocument.Parse(await hiddenResponse.Content.ReadAsStringAsync());
        var hiddenCodes = hiddenDocument.RootElement.GetProperty("families").EnumerateArray()
            .SelectMany(family => family.GetProperty("metrics").EnumerateArray())
            .Select(metric => metric.GetProperty("metricCode").GetString()!)
            .ToArray();
        Assert.DoesNotContain("hidden_probe", hiddenCodes);
        Assert.Equal(18, hiddenCodes.Length);
    }

    // -- TC-SCR-002 — AND logic with min/max bounds (golden) -------------------

    [Fact]
    public async Task TC_SCR_002_and_logic_min_max_bounds_golden()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await RunAsync(client, """
            { "criteria": [
                { "metricCode": "pe",  "bound": "max", "maxValue": 15 },
                { "metricCode": "roe", "bound": "min", "minValue": 0.15 }
            ] }
            """);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(3, root.GetProperty("matchCount").GetInt32());
        Assert.Equal(2, root.GetProperty("excludedCount").GetInt32());
        Assert.Equal(0, root.GetProperty("droppedCriteria").GetArrayLength());
        Assert.Equal(T.ToString("yyyy-MM-dd"), root.GetProperty("asOf").GetString());
        Assert.False(root.GetProperty("stale").GetBoolean());

        var rows = Rows(root).ToDictionary(row => Symbol(row), StringComparer.Ordinal);
        Assert.Equal(3, rows.Count);
        Assert.Equal("Alfa Teknoloji A.Ş.", Name(rows["ALFA"]));
        Assert.Equal("A", Sector(rows["ALFA"]));

        AssertValue(rows["ALFA"], "pe", 10.0m);
        AssertValue(rows["ALFA"], "roe", 0.20m);
        AssertValue(rows["REST"], "pe", 10.0m);
        AssertValue(rows["REST"], "roe", 0.15m);
        AssertValue(rows["UNSEC"], "pe", 14.0m);
        // UNSEC roe = 45/210 = 0.2142857… (the test plan quotes the 4-dp 0.2143).
        AssertValue(rows["UNSEC"], "roe", 0.214286m);
    }

    // -- TC-SCR-003 — Range bounds, inclusive edges (golden) -------------------

    [Fact]
    public async Task TC_SCR_003_range_bounds_are_inclusive_at_min_and_max()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        // Inclusive [10, 20]: values exactly at the min (ALFA, REST) and the max (DELTA)
        // are included — 5 rows.
        using var range = await RunAsync(client, """
            { "criteria": [ { "metricCode": "pe", "bound": "range", "minValue": 10, "maxValue": 20 } ] }
            """);
        Assert.Equal(HttpStatusCode.OK, range.StatusCode);
        using var rangeDocument = JsonDocument.Parse(await range.Content.ReadAsStringAsync());
        var rangeRows = Rows(rangeDocument.RootElement).ToDictionary(Symbol, StringComparer.Ordinal);
        Assert.Equal(
            new[] { "ALFA", "DELTA", "REST", "THETA", "UNSEC" },
            rangeRows.Keys.OrderBy(symbol => symbol, StringComparer.Ordinal).ToArray());
        Assert.Equal(5, rangeDocument.RootElement.GetProperty("matchCount").GetInt32());

        // Shifted min [10.0001, 20]: both values sitting exactly on the old min edge
        // (ALFA 10.0 and REST 10.0) drop — 3 rows. NOTE: the test plan text says
        // "ALFA … excluded → 4 rows", which overlooks REST (also exactly pe 10.0, listed
        // as a range match one line above); the arithmetically consistent expectation is
        // 3 rows. Flagged to the test-planner (see the ticket's flagged items).
        using var shifted = await RunAsync(client, """
            { "criteria": [ { "metricCode": "pe", "bound": "range", "minValue": 10.0001, "maxValue": 20 } ] }
            """);
        Assert.Equal(HttpStatusCode.OK, shifted.StatusCode);
        using var shiftedDocument = JsonDocument.Parse(await shifted.Content.ReadAsStringAsync());
        var shiftedRows = Rows(shiftedDocument.RootElement).ToDictionary(Symbol, StringComparer.Ordinal);
        Assert.Equal(
            new[] { "DELTA", "THETA", "UNSEC" },
            shiftedRows.Keys.OrderBy(symbol => symbol, StringComparer.Ordinal).ToArray());
        Assert.Equal(3, shiftedDocument.RootElement.GetProperty("matchCount").GetInt32());
    }

    // -- TC-SCR-004 — Growth criterion with window (golden) --------------------

    [Fact]
    public async Task TC_SCR_004_growth_criterion_honours_the_per_criterion_window()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var window3 = await RunAsync(client, """
            { "criteria": [ { "metricCode": "rev_cagr", "bound": "min", "minValue": 0.15, "window": 3 } ] }
            """);
        Assert.Equal(HttpStatusCode.OK, window3.StatusCode);
        using var window3Document = JsonDocument.Parse(await window3.Content.ReadAsStringAsync());
        var window3Root = window3Document.RootElement;
        var window3Rows = Rows(window3Root).ToDictionary(Symbol, StringComparer.Ordinal);

        Assert.Equal(2, window3Root.GetProperty("matchCount").GetInt32());
        Assert.Equal(14, window3Root.GetProperty("excludedCount").GetInt32());
        Assert.Equal(new[] { "ALFA", "ZETA" }, window3Rows.Keys.OrderBy(symbol => symbol, StringComparer.Ordinal).ToArray());
        AssertValue(window3Rows["ALFA"], "rev_cagr", 0.160397m);
        // ZETA: computed over its single available interval (window_years = 1).
        AssertValue(window3Rows["ZETA"], "rev_cagr", 0.25m);

        // The window is honoured per criterion: the same concept over 5Y yields ALFA's 5Y value.
        using var window5 = await RunAsync(client, """
            { "criteria": [ { "metricCode": "rev_cagr", "bound": "min", "minValue": 0.15, "window": 5 } ] }
            """);
        Assert.Equal(HttpStatusCode.OK, window5.StatusCode);
        using var window5Document = JsonDocument.Parse(await window5.Content.ReadAsStringAsync());
        var window5Rows = Rows(window5Document.RootElement).ToDictionary(Symbol, StringComparer.Ordinal);
        Assert.Equal(2, window5Rows.Count);
        AssertValue(window5Rows["ALFA"], "rev_cagr", 0.201124m);
        AssertValue(window5Rows["ZETA"], "rev_cagr", 0.25m);
    }

    // -- TC-SCR-005 — Dividend criterion with never-paid exclusions (golden) ----

    [Fact]
    public async Task TC_SCR_005_dividend_criterion_excludes_never_paid_stocks()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await RunAsync(client, """
            { "criteria": [ { "metricCode": "div_yield", "bound": "min", "minValue": 0.04 } ] }
            """);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var rows = Rows(root).ToDictionary(Symbol, StringComparer.Ordinal);

        Assert.Equal(2, root.GetProperty("matchCount").GetInt32());
        Assert.Equal(8, root.GetProperty("excludedCount").GetInt32());
        Assert.Equal(new[] { "ALFA", "REST" }, rows.Keys.OrderBy(symbol => symbol, StringComparer.Ordinal).ToArray());
        AssertValue(rows["ALFA"], "div_yield", 0.05m);
        // REST 0.04 sits exactly on the inclusive min edge.
        AssertValue(rows["REST"], "div_yield", 0.04m);

        // DELTA (0.02) has data and fails the bound — it is NOT counted as excluded.
        Assert.DoesNotContain("DELTA", rows.Keys);
        Assert.DoesNotContain("LAMDA", rows.Keys);
    }

    // -- TC-SCR-006 — Zero-match result is a normal 200 ------------------------

    [Fact]
    public async Task TC_SCR_006_zero_match_is_a_normal_200()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await RunAsync(client, """
            { "criteria": [ { "metricCode": "pe", "bound": "max", "maxValue": 5 } ] }
            """);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(0, root.GetProperty("matchCount").GetInt32());
        Assert.Empty(root.GetProperty("rows").EnumerateArray());
    }

    // -- TC-SCR-009 — Empty criteria array rejected ----------------------------

    [Fact]
    public async Task TC_SCR_009_empty_criteria_rejected_but_single_criterion_succeeds()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var empty = await RunAsync(client, """{ "criteria": [] }""");
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        using var emptyDocument = JsonDocument.Parse(await empty.Content.ReadAsStringAsync());
        Assert.Equal("VALIDATION_FAILED", emptyDocument.RootElement.GetProperty("code").GetString());

        // The count-1 boundary succeeds.
        using var single = await RunAsync(client, """
            { "criteria": [ { "metricCode": "pe", "bound": "max", "maxValue": 15 } ] }
            """);
        Assert.Equal(HttpStatusCode.OK, single.StatusCode);
        using var singleDocument = JsonDocument.Parse(await single.Content.ReadAsStringAsync());
        Assert.Equal(3, singleDocument.RootElement.GetProperty("matchCount").GetInt32());
    }

    // ---------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------

    /// <summary>Seeds the L2 fixture universe and computes the canonical metrics at T.</summary>
    private async Task SeedAsync()
    {
        await using (var db = _postgres.CreateContext())
        {
            await FixtureSeeder.ApplyAsync(db, L2);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM derived_metrics");
        }

        var clock = new FakeTimeProvider(new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _postgres.ConnectionString,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddMarketDataIngestion(configuration);
        await using var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IIngestionJobRunner>();
        var result = await runner.RunAsync(MetricsRecomputeJob.Code, new IngestionRequest(T));
        Assert.Equal("succeeded", result.Status);
    }

    private static HttpClient CreateClient(DegerliAppFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    private static async Task<HttpResponseMessage> RunAsync(HttpClient client, string json)
    {
        var csrf = await FetchCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/screener/run")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
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

    private static List<JsonElement> Rows(JsonElement root)
        => root.GetProperty("rows").EnumerateArray().ToList();

    private static string Symbol(JsonElement row) => row.GetProperty("symbol").GetString()!;

    private static string Name(JsonElement row) => row.GetProperty("name").GetString()!;

    private static string? Sector(JsonElement row)
    {
        var sector = row.GetProperty("sector");
        return sector.ValueKind == JsonValueKind.Null ? null : sector.GetString();
    }

    private static void AssertValue(JsonElement row, string metricCode, decimal expected)
    {
        var value = row.GetProperty("values").GetProperty(metricCode).GetDecimal();
        Assert.True(
            Math.Abs(value - expected) <= 0.00001m,
            $"{metricCode}: expected {expected} (±1e-5) but was {value}");
    }
}
