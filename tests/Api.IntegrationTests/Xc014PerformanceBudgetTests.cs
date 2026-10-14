using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Metrics;
using Degerli.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TC-XC-014 — performance budget smoke (`01` §9 targets, strategy decision A).
/// Seeded data, warm cache, Testcontainers, single user. Boundary-value analysis with
/// the documented ×2 tolerance: cached public GETs p95 ≤ 600 ms (target 300 × 2);
/// <c>POST /screener/run</c> and <c>POST …/dcf/compute</c> p95 ≤ 1600 ms (target
/// 800 × 2), each measured over ≥ 30 calls. The Playwright ≤ 5-minute suite gate is
/// the e2e half of this budget (tests/e2e budget.spec.ts). Traces: `01` §9.
/// </summary>
public sealed class Xc014PerformanceBudgetTests : IClassFixture<PostgresFixture>
{
    private const int CallCount = 30;
    private const double CachedGetBudgetMs = 600;
    private const double DynamicPostBudgetMs = 1600;

    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    private readonly PostgresFixture _postgres;

    public Xc014PerformanceBudgetTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task Cached_public_gets_p95_within_600ms()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        await AssertWithinBudgetAsync(client, "/api/v1/stocks", CachedGetBudgetMs);
        await AssertWithinBudgetAsync(client, "/api/v1/screener/metrics", CachedGetBudgetMs);
    }

    [Fact]
    public async Task Dynamic_posts_p95_within_1600ms()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        var token = await FetchCsrfTokenAsync(client);
        const string criteria = """
            { "criteria": [ { "metricCode": "pe", "bound": "max", "maxValue": 15 } ] }
            """;

        await AssertWithinBudgetAsync(
            client,
            "/api/v1/screener/run",
            DynamicPostBudgetMs,
            () => PostAsync(client, "/api/v1/screener/run", token, criteria));

        var computePayload = Baseline();
        await AssertWithinBudgetAsync(
            client,
            "/api/v1/stocks/ALFA/dcf/compute",
            DynamicPostBudgetMs,
            () => PostAsync(client, "/api/v1/stocks/ALFA/dcf/compute", token, JsonSerializer.Serialize(computePayload)));
    }

    private static async Task AssertWithinBudgetAsync(
        HttpClient client,
        string label,
        double budgetMs,
        Func<Task<HttpResponseMessage>>? invoke = null)
    {
        invoke ??= () => client.GetAsync(label);

        // Warm-up: JIT, EF model build, first-touch caches. Not measured.
        for (var i = 0; i < 3; i++)
        {
            using var warmup = await invoke();
            Assert.Equal(HttpStatusCode.OK, warmup.StatusCode);
        }

        var samples = new List<double>(CallCount);
        var stopwatch = new Stopwatch();
        for (var i = 0; i < CallCount; i++)
        {
            stopwatch.Restart();
            using var response = await invoke();
            stopwatch.Stop();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            samples.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        var p95 = Percentile(samples, 0.95);
        Assert.True(
            p95 <= budgetMs,
            $"{label}: p95 {p95:F1} ms exceeded the {budgetMs:F0} ms budget " +
            $"(max {samples.Max():F1} ms over {CallCount} calls)");
    }

    private static double Percentile(List<double> values, double percentile)
    {
        var sorted = values.OrderBy(value => value).ToList();
        var rank = (int)Math.Ceiling(percentile * sorted.Count) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Count - 1)];
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string path,
        string token,
        string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-CSRF-Token", token);
        request.Headers.Add("X-Forwarded-For", "198.51.100.201");
        return await client.SendAsync(request);
    }

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

    private static async Task<string> FetchCsrfTokenAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/csrf-token");
        request.Headers.Add("X-Forwarded-For", Guid.NewGuid().ToString("N"));
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("token").GetString()!;
    }

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
}
