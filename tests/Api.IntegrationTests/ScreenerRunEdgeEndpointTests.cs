using System.Data;
using System.Data.Common;
using System.Net;
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
/// TKT-scr-003 acceptance — the screener run endpoint's edge behaviors (Group A of
/// <c>.pipeline/testing/stock-screening.md</c>, TC-SCR-008/010/011):
/// <list type="bullet">
/// <item>TC-SCR-008 — malformed-criterion classes (missing bound value, non-numeric
/// value, invalid CAGR window, range min &gt; max, window on a non-growth metric) each
/// reject the whole run with <c>400 VALIDATION_FAILED</c> and <c>params.fields[]</c>
/// naming the offending criterion field; no partial execution.</item>
/// <item>TC-SCR-010 — the run envelope always carries <c>asOf</c> + <c>stale</c>; a
/// forced freshness gap serves the older vintage and marks it stale
/// (FR-SCR-005, NFR-SCR-004).</item>
/// <item>TC-SCR-011 — results are computed on demand against the latest
/// <c>derived_metrics</c>, never persisted: a data-day advance changes the result
/// with no result-set table behind it (UC-SCR-003 step 2).</item>
/// </list>
/// Runs against the real migrated Testcontainers PostgreSQL seeded with the L2 fixture
/// universe, then computes the canonical metrics through the Metrics Engine job — the
/// same <c>derived_metrics</c> rows every other surface serves (BR-SCR-001/NFR-SCR-003).
/// </summary>
public sealed class ScreenerRunEdgeEndpointTests : IClassFixture<PostgresFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    /// <summary>The trading day after the fixture anchor (2026-10-07, a Wednesday).</summary>
    private static DateOnly T1 => T.AddDays(1);

    private readonly PostgresFixture _postgres;

    public ScreenerRunEdgeEndpointTests(PostgresFixture postgres) => _postgres = postgres;

    // -- TC-SCR-008 — Malformed criteria rejected --------------------------------

    [Fact]
    public async Task TC_SCR_008_malformed_criteria_are_rejected_with_field_details()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        // (a) missing bound value — a max bound with no maxValue.
        await AssertValidationFieldAsync(client,
            """{ "criteria": [ { "metricCode": "pe", "bound": "max" } ] }""",
            "maxValue");

        // (b) non-numeric bound value — a string where a number is required.
        await AssertValidationFieldAsync(client,
            """{ "criteria": [ { "metricCode": "pe", "bound": "max", "maxValue": "fifteen" } ] }""",
            "maxValue");

        // (c) window 7 is not one of the selectable 3/5/10 windows.
        await AssertValidationFieldAsync(client,
            """{ "criteria": [ { "metricCode": "rev_cagr", "bound": "min", "minValue": 0.15, "window": 7 } ] }""",
            "window");

        // (d) range with min greater than max.
        await AssertValidationFieldAsync(client,
            """{ "criteria": [ { "metricCode": "pe", "bound": "range", "minValue": 20, "maxValue": 10 } ] }""",
            "minValue");

        // (e) window on a non-growth metric (windows tie to growth metrics only, I-SCR-4).
        await AssertValidationFieldAsync(client,
            """{ "criteria": [ { "metricCode": "pe", "bound": "max", "maxValue": 15, "window": 3 } ] }""",
            "window");

        // No partial execution: a payload whose first criterion is perfectly valid and
        // whose second is malformed is rejected as a whole — no rows are evaluated.
        using var mixed = await RunAsync(client, """
            { "criteria": [
                { "metricCode": "pe", "bound": "max", "maxValue": 15 },
                { "metricCode": "roe", "bound": "min" }
            ] }
            """);
        Assert.Equal(HttpStatusCode.BadRequest, mixed.StatusCode);
        using var mixedDocument = JsonDocument.Parse(await mixed.Content.ReadAsStringAsync());
        var mixedRoot = mixedDocument.RootElement;
        Assert.Equal("VALIDATION_FAILED", mixedRoot.GetProperty("code").GetString());
        Assert.False(mixedRoot.TryGetProperty("rows", out _));
        Assert.False(mixedRoot.TryGetProperty("matchCount", out _));
    }

    // -- TC-SCR-010 — Run response envelope and staleness ------------------------

    [Fact]
    public async Task TC_SCR_010_run_envelope_serves_the_older_vintage_when_stale()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        const string criteria = """
            { "criteria": [ { "metricCode": "pe", "bound": "max", "maxValue": 15 } ] }
            """;

        // Fresh: the canonical metrics carry T and the clock is at T.
        factory.Clock.SetUtcNow(new DateTimeOffset(T.ToDateTime(new TimeOnly(18, 0)), TimeSpan.Zero));
        using var fresh = await RunAsync(client, criteria);
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
        using var freshDocument = JsonDocument.Parse(await fresh.Content.ReadAsStringAsync());
        var freshRoot = freshDocument.RootElement;
        Assert.Equal(T.ToString("yyyy-MM-dd"), freshRoot.GetProperty("asOf").GetString());
        Assert.False(freshRoot.GetProperty("stale").GetBoolean());

        // Forced freshness gap: the clock has advanced to the next trading day with no
        // new canonical metrics — the run serves the older asOf and marks it stale, never
        // presenting an unclear vintage (FR-SCR-005 / NFR-SCR-004).
        factory.Clock.SetUtcNow(new DateTimeOffset(T1.ToDateTime(new TimeOnly(18, 0)), TimeSpan.Zero));
        using var stale = await RunAsync(client, criteria);
        Assert.Equal(HttpStatusCode.OK, stale.StatusCode);
        using var staleDocument = JsonDocument.Parse(await stale.Content.ReadAsStringAsync());
        var staleRoot = staleDocument.RootElement;
        Assert.Equal(T.ToString("yyyy-MM-dd"), staleRoot.GetProperty("asOf").GetString());
        Assert.True(staleRoot.GetProperty("stale").GetBoolean());
    }

    // -- TC-SCR-011 — Results computed on demand, never persisted ----------------

    [Fact]
    public async Task TC_SCR_011_results_recompute_on_demand_when_the_data_day_advances()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        const string criteria = """
            { "criteria": [ { "metricCode": "pe", "bound": "min", "minValue": 0 } ] }
            """;

        // Run on day T: ALFA's P/E is 20.00 / 2.00 = 10.0, as-of T.
        using var first = await RunAsync(client, criteria);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var firstDocument = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var firstRoot = firstDocument.RootElement;
        Assert.Equal(T.ToString("yyyy-MM-dd"), firstRoot.GetProperty("asOf").GetString());
        var firstAlfa = Rows(firstRoot).Single(row => Symbol(row) == "ALFA");
        AssertValue(firstAlfa, "pe", 10.0m);

        // Ingest day T+1 prices: ALFA closes higher (30.00) so its P/E becomes 15.0;
        // the other rows are carried over unchanged.
        await IngestNextDayPricesAsync();

        // Re-run the identical criteria on T+1 — the result is recomputed from the
        // current derived_metrics, not replayed from a stored result set.
        await RunMetricsAsync(T1);
        factory.Clock.SetUtcNow(new DateTimeOffset(T1.ToDateTime(new TimeOnly(18, 0)), TimeSpan.Zero));

        using var second = await RunAsync(client, criteria);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var secondDocument = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        var secondRoot = secondDocument.RootElement;
        Assert.Equal(T1.ToString("yyyy-MM-dd"), secondRoot.GetProperty("asOf").GetString());
        var secondAlfa = Rows(secondRoot).Single(row => Symbol(row) == "ALFA");
        AssertValue(secondAlfa, "pe", 15.0m);

        // No persistence surface exists for run results (schema backstop TC-MDF-009):
        // nothing stores a computed run.
        await using var db = _postgres.CreateContext();
        var tables = (await QueryAsync(db,
                "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'"))
            .Select(row => (string)row[0]!)
            .ToArray();
        Assert.DoesNotContain(tables, table =>
            table.Contains("result", StringComparison.OrdinalIgnoreCase)
            || table.Contains("screener_run", StringComparison.OrdinalIgnoreCase));
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

        await RunMetricsAsync(T);
    }

    /// <summary>Runs the metrics-recompute job for <paramref name="date"/> through the real pipeline.</summary>
    private async Task RunMetricsAsync(DateOnly date)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
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
        var result = await runner.RunAsync(MetricsRecomputeJob.Code, new IngestionRequest(date));
        Assert.Equal("succeeded", result.Status);
    }

    /// <summary>
    /// Carries every T price row forward to T+1, raising ALFA's close so its P/E moves.
    /// </summary>
    private async Task IngestNextDayPricesAsync()
    {
        await using var db = _postgres.CreateContext();
        var tPrices = await db.DailyPrices
            .Where(price => price.PriceDate == T)
            .ToListAsync();
        var alfaId = await db.Instruments
            .Where(instrument => instrument.Symbol == "ALFA")
            .Select(instrument => instrument.Id)
            .SingleAsync();

        foreach (var price in tPrices)
        {
            var close = price.InstrumentId == alfaId ? 30.00m : price.CloseRaw;
            db.DailyPrices.Add(new DailyPrice
            {
                InstrumentId = price.InstrumentId,
                PriceDate = T1,
                Open = close,
                High = close,
                Low = close,
                CloseRaw = close,
                CloseAdjusted = close,
                Volume = price.Volume,
                SourceRef = "test:t-plus-1",
                RecordedAt = new DateTimeOffset(T1.ToDateTime(new TimeOnly(18, 0)), TimeSpan.Zero),
            });
        }

        await db.SaveChangesAsync();
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

    /// <summary>Asserts a run is rejected as <c>400 VALIDATION_FAILED</c> naming <paramref name="field"/>.</summary>
    private static async Task AssertValidationFieldAsync(HttpClient client, string json, string field)
    {
        using var response = await RunAsync(client, json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal("VALIDATION_FAILED", root.GetProperty("code").GetString());

        var fields = root.GetProperty("params").GetProperty("fields").EnumerateArray().ToList();
        Assert.Contains(fields, entry => entry.GetProperty("field").GetString() == field);
    }

    private static async Task<List<object?[]>> QueryAsync(DegerliDbContext db, string sql)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;

        var rows = new List<object?[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var values = new object[reader.FieldCount];
            reader.GetValues(values);
            rows.Add(values);
        }

        return rows;
    }

    private static List<JsonElement> Rows(JsonElement root)
        => root.GetProperty("rows").EnumerateArray().ToList();

    private static string Symbol(JsonElement row) => row.GetProperty("symbol").GetString()!;

    private static void AssertValue(JsonElement row, string metricCode, decimal expected)
    {
        var value = row.GetProperty("values").GetProperty(metricCode).GetDecimal();
        Assert.True(
            Math.Abs(value - expected) <= 0.00001m,
            $"{metricCode}: expected {expected} (±1e-5) but was {value}");
    }
}
