using System.Net;
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
/// TKT-res-003 acceptance — the overview and valuation section endpoints plus the
/// sector-medians job (Group B of <c>.pipeline/testing/stock-research.md</c>,
/// TC-RES-005..008) and the MDF-plan cross-domain case TC-MDF-030:
/// <list type="bullet">
/// <item><c>GET /api/v1/stocks/{symbol}/overview</c> — the published bilingual
/// description (both TR + EN served; client renders the choice) with its last-reviewed
/// date, or the honest <c>preparing</c> state when nothing is published — drafts are
/// never leaked (FR-RES-005/022/026, UC-RES-002).</item>
/// <item><c>GET /api/v1/stocks/{symbol}/valuation</c> — current canonical multiples,
/// the corporate-action-adjusted historical price series (<c>adjusted: true</c>,
/// FR-RES-006/018), and the vs.-sector strip over <c>sector_metric_medians</c>
/// (<c>{value, notMeaningful, sectorMedian, peerCount, excludedCount}</c>) computed by
/// the <c>medians</c> job (FR-RES-007/SD-002; TC-MDF-030).</item>
/// </list>
/// Every test runs against the real, migrated Testcontainers PostgreSQL seeded with
/// the L2 fixture universe, then computes the canonical metrics and the sector medians
/// at T through the registered jobs. Goldens are hand-derived in FU §11.1/§11.4 — never
/// re-derived from the implementation.
/// </summary>
public sealed class StockOverviewAndValuationEndpointsTests : IClassFixture<PostgresFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    private readonly PostgresFixture _postgres;

    public StockOverviewAndValuationEndpointsTests(PostgresFixture postgres) => _postgres = postgres;

    // -- TC-RES-005 — Overview: published description --------------------------

    [Fact]
    public async Task TC_RES_005_overview_serves_the_published_description_in_both_languages()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/stocks/ALFA/overview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Anonymous research surface (BR-RES-001): no session is established.
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.True(response.Headers.CacheControl?.Public);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal("published", root.GetProperty("status").GetString());
        // Both language versions ride the payload; the client renders the choice (FR-RES-005).
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("textTr").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("textEn").GetString()));
        // The last-reviewed date is displayed (FR-RES-022); FU §9b ALFA = 2026-09-10.
        Assert.StartsWith("2026-09-10", root.GetProperty("lastReviewedAt").GetString()!, StringComparison.Ordinal);
        // The honest-data envelope rides every data response (`03` §1.2, M-1).
        Assert.Equal(T.ToString("yyyy-MM-dd"), root.GetProperty("asOf").GetString());
    }

    // -- TC-RES-006 — Overview: preparing state --------------------------------

    [Fact]
    public async Task TC_RES_006_overview_serves_preparing_state_and_never_leaks_drafts()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        // BETA has no description row at all → the honest "in preparation" state.
        using var beta = await client.GetAsync("/api/v1/stocks/BETA/overview");
        Assert.Equal(HttpStatusCode.OK, beta.StatusCode);
        using var betaDocument = JsonDocument.Parse(await beta.Content.ReadAsStringAsync());
        var betaRoot = betaDocument.RootElement;
        Assert.Equal("preparing", betaRoot.GetProperty("status").GetString());
        Assert.Null(TextField(betaRoot, "textTr"));
        Assert.Null(TextField(betaRoot, "textEn"));
        Assert.Equal(T.ToString("yyyy-MM-dd"), betaRoot.GetProperty("asOf").GetString());

        // REST has a *draft* in storage (FU §9b) — it must never be publicly served
        // (NFR-RES-006/BR-RES-002): the public page is still honestly "preparing".
        using var rest = await client.GetAsync("/api/v1/stocks/REST/overview");
        Assert.Equal(HttpStatusCode.OK, rest.StatusCode);
        using var restDocument = JsonDocument.Parse(await rest.Content.ReadAsStringAsync());
        var restRoot = restDocument.RootElement;
        Assert.Equal("preparing", restRoot.GetProperty("status").GetString());
        Assert.Null(TextField(restRoot, "textTr"));
        Assert.Null(TextField(restRoot, "textEn"));
    }

    // -- TKT-foundation-057 — Overview: the instrument's identity ---------------

    /// <summary>
    /// The overview payload names the stock the page is about (TKT-foundation-057,
    /// user-approved additive amendment): the canonical uppercase symbol and the
    /// language-neutral official name ride both the published and the preparing
    /// payload, and the route's casing never leaks into the header.
    /// </summary>
    [Fact]
    public async Task TKT_foundation_057_overview_serves_the_instrument_identity()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/stocks/ALFA/overview");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        // The official name is the L2 fixture's (FU §7): "Alfa Teknoloji A.Ş.".
        Assert.Equal("ALFA", TextField(root, "symbol"));
        Assert.Equal("Alfa Teknoloji A.Ş.", TextField(root, "name"));

        // The preparing state carries the same identity — the header must be able to
        // say which stock it is even before a description is published.
        using var preparing = await client.GetAsync("/api/v1/stocks/beta/overview");
        Assert.Equal(HttpStatusCode.OK, preparing.StatusCode);
        using var preparingDocument = JsonDocument.Parse(await preparing.Content.ReadAsStringAsync());
        var preparingRoot = preparingDocument.RootElement;
        Assert.Equal("preparing", preparingRoot.GetProperty("status").GetString());
        // A lowercase request symbol is served canonicalised, never echoed back raw.
        Assert.Equal("BETA", TextField(preparingRoot, "symbol"));
        Assert.False(string.IsNullOrWhiteSpace(TextField(preparingRoot, "name")));
    }

    /// <summary>
    /// A blank instrument name is served as `null`, never as an empty string dressed up
    /// as an identity (TKT-foundation-057): the client degrades to the symbol alone.
    /// </summary>
    [Fact]
    public async Task TKT_foundation_057_overview_serves_a_blank_name_as_null()
    {
        await SeedAsync();
        await using (var db = _postgres.CreateContext())
        {
            await db.Instruments
                .Where(instrument => instrument.Symbol == "BETA")
                .ExecuteUpdateAsync(setters => setters.SetProperty(instrument => instrument.Name, string.Empty));
        }

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/stocks/BETA/overview");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal("BETA", TextField(root, "symbol"));
        Assert.True(root.TryGetProperty("name", out var name));
        Assert.Equal(JsonValueKind.Null, name.ValueKind);
    }

    // -- TC-RES-007 — Valuation incl. vs.-sector strip (golden) -----------------

    [Fact]
    public async Task TC_RES_007_valuation_current_multiples_historical_series_and_strip_goldens()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/stocks/ALFA/valuation");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.True(response.Headers.CacheControl?.Public);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(T.ToString("yyyy-MM-dd"), root.GetProperty("asOf").GetString());
        Assert.False(root.GetProperty("stale").GetBoolean());

        // Current canonical multiples — FU §11.1 (BR-RES-004: the endpoint computes nothing).
        var current = root.GetProperty("current");
        AssertValue(current, "pe", 10.0m);
        AssertValue(current, "pb", 2.0m);
        AssertValue(current, "ev_ebitda", 5.5m);
        AssertValue(current, "ev_fcf", 22.0m);
        AssertValue(current, "fcf_yield", 0.05m);

        // Historical price series on the corporate-action-adjusted basis (FR-RES-006/018).
        var historical = root.GetProperty("historical");
        Assert.True(historical.GetProperty("adjusted").GetBoolean());
        Assert.True(historical.GetProperty("series").GetArrayLength() > 0);

        // The vs.-sector strip carries the stock value next to the sector median with
        // peer and loss-maker-exclusion counts (UXR-RES-013/024, FU §11.4).
        var strip = root.GetProperty("strip");
        AssertStrip(strip, "pe", value: 10.0m, sectorMedian: 10.0m, peerCount: 3, excludedCount: 2);
        // "same shape for the other four valuation metrics".
        foreach (var metric in new[] { "pb", "ev_ebitda", "ev_fcf", "fcf_yield" })
        {
            var entry = strip.GetProperty(metric);
            Assert.False(entry.GetProperty("notMeaningful").GetBoolean());
            Assert.NotNull(DecimalOrNull(entry, "value"));
            Assert.NotNull(DecimalOrNull(entry, "sectorMedian"));
            Assert.Equal(JsonValueKind.Number, entry.GetProperty("peerCount").ValueKind);
            Assert.Equal(JsonValueKind.Number, entry.GetProperty("excludedCount").ValueKind);
        }
    }

    [Fact]
    public async Task TC_RES_007_valuation_strip_epsl_variant_golden()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/stocks/EPSL/valuation");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        // EPSL's own current P/E (FU §11.4): 20 / (300/400) = 26.6667.
        AssertValue(root.GetProperty("current"), "pe", 26.6667m);

        // Sector B strip: value 26.6667, median 25.8333, peerCount 6, excludedCount 0.
        AssertStrip(root.GetProperty("strip"), "pe", value: 26.6667m, sectorMedian: 25.8333m, peerCount: 6, excludedCount: 0);
    }

    // -- TC-RES-008 — Strip not-meaningful state --------------------------------

    [Fact]
    public async Task TC_RES_008_strip_not_meaningful_for_loss_maker_keeps_median_and_counts()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/stocks/BETA/valuation");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        // BETA is a loss-maker (NI ≤ 0): its own P/E is not meaningful — never 0 (BR-RES-008).
        Assert.Null(DecimalOrNull(root.GetProperty("current"), "pe"));

        var pe = root.GetProperty("strip").GetProperty("pe");
        Assert.True(pe.GetProperty("notMeaningful").GetBoolean());
        Assert.Null(DecimalOrNull(pe, "value"));
        // The sector median and its basis are still served (UXR-RES-025): sector A median
        // 10.0 with 3 peers and 2 loss-makers excluded (FU §11.4).
        Assert.Equal(10.0m, DecimalOrNull(pe, "sectorMedian"));
        Assert.Equal(3, pe.GetProperty("peerCount").GetInt32());
        Assert.Equal(2, pe.GetProperty("excludedCount").GetInt32());
    }

    // -- TC-MDF-030 — Raw vs adjusted series diverge around actions -------------

    [Fact]
    public async Task TC_MDF_030_epsl_historical_series_carries_raw_and_adjusted_values()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/stocks/EPSL/valuation");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        var historical = root.GetProperty("historical");
        Assert.True(historical.GetProperty("adjusted").GetBoolean());

        var series = historical.GetProperty("series").EnumerateArray()
            .ToDictionary(row => row.GetProperty("date").GetString()!, StringComparer.Ordinal);

        // FU §4 EPSL rows exactly — raw vs adjusted diverge around the split/bonus.
        AssertRow(series["2025-05-30"], raw: 100.00m, adjusted: 18.1818m);
        AssertRow(series["2026-02-27"], raw: 22.00m, adjusted: 20.0000m);
        AssertRow(series["2026-06-02"], raw: 20.20m, adjusted: 20.2000m);
        AssertRow(series[T.ToString("yyyy-MM-dd")], raw: 20.00m, adjusted: 20.0000m);

        // Current display uses close_raw: EPSL's raw 20.00 gives P/E 20/(300/400) = 26.6667.
        AssertValue(root.GetProperty("current"), "pe", 26.6667m);
    }

    // ---------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Seeds the L2 fixture universe, then computes the canonical metrics and the sector
    /// medians at T through the registered jobs — the same canonical rows every
    /// surface serves (BR-RES-004 / NFR-RES-003).
    /// </summary>
    private async Task SeedAsync()
    {
        await using (var db = _postgres.CreateContext())
        {
            await FixtureSeeder.ApplyAsync(db, L2);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM derived_metrics");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM sector_metric_medians");
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
        var metrics = await runner.RunAsync(MetricsRecomputeJob.Code, new IngestionRequest(T));
        Assert.Equal("succeeded", metrics.Status);
        var medians = await runner.RunAsync(SectorMediansJob.Code, new IngestionRequest(T));
        Assert.Equal("succeeded", medians.Status);
    }

    private static HttpClient CreateClient(DegerliAppFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    private static string? TextField(JsonElement root, string name)
        => root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static decimal? DecimalOrNull(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return element.GetDecimal();
    }

    private static void AssertValue(JsonElement parent, string metricCode, decimal expected)
    {
        var value = DecimalOrNull(parent, metricCode);
        Assert.NotNull(value);
        Assert.True(
            Math.Abs(value!.Value - expected) <= 0.0001m,
            $"{metricCode}: expected {expected} (±1e-4) but was {value}");
    }

    private static void AssertStrip(
        JsonElement strip,
        string metricCode,
        decimal value,
        decimal sectorMedian,
        int peerCount,
        int excludedCount)
    {
        var entry = strip.GetProperty(metricCode);
        Assert.False(entry.GetProperty("notMeaningful").GetBoolean());
        AssertValue(entry, "value", value);
        AssertValue(entry, "sectorMedian", sectorMedian);
        Assert.Equal(peerCount, entry.GetProperty("peerCount").GetInt32());
        Assert.Equal(excludedCount, entry.GetProperty("excludedCount").GetInt32());
    }

    private static void AssertRow(JsonElement row, decimal raw, decimal adjusted)
    {
        var actualRaw = row.GetProperty("closeRaw").GetDecimal();
        var actualAdjusted = row.GetProperty("closeAdjusted").GetDecimal();
        Assert.True(Math.Abs(actualRaw - raw) <= 0.0001m, $"closeRaw: expected {raw} but was {actualRaw}");
        Assert.True(Math.Abs(actualAdjusted - adjusted) <= 0.0001m, $"closeAdjusted: expected {adjusted} but was {actualAdjusted}");
    }
}
