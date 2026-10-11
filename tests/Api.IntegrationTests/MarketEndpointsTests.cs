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
/// TKT-mov-004 acceptance — the public market endpoints (`03` §3, MOV Group C):
/// <list type="bullet">
/// <item><c>GET /api/v1/market/overview</c> (TC-MOV-012) — the latest completed
/// <c>market_snapshots</c> row served anonymously under the honest-data envelope;
/// the equity pipeline is its failure domain.</item>
/// <item><c>GET /api/v1/market/macro</c> (TC-MOV-013) — six series, each
/// <c>{code, value, unit, source, asOf, stale, state}</c>; the inflation pair is
/// always both entries (BR-MOV-002) and an absent series is an explicit
/// <c>state: "unavailable"</c> entry, never silently dropped (UXR-MOV-009).</item>
/// <item>TC-MOV-014 — equity and macro have separate failure domains: one outage
/// must not blank the other (SCR-001 §5).</item>
/// </list>
/// Each test runs against a migrated Testcontainers PostgreSQL seeded with the L2
/// fixture universe; snapshot rows are produced by the real <c>market-snapshot</c>
/// job, never hand-written.
/// </summary>
public sealed class MarketEndpointsTests : IClassFixture<PostgresFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    /// <summary>FU §11.3 goldens are quoted at mixed granularities; compare coarsely.</summary>
    private const decimal GoldenTolerance = 0.0001m;

    private readonly PostgresFixture _postgres;

    public MarketEndpointsTests(PostgresFixture postgres) => _postgres = postgres;

    // ---------------------------------------------------------------------------------
    // TC-MOV-012 — GET /market/overview payload
    // ---------------------------------------------------------------------------------

    /// <summary>TC-MOV-012 — anonymous 200; the TC-MOV-001 values under the honest-data
    /// envelope (<c>asOf</c> = T, <c>stale</c> = false); all equity blocks in one payload.</summary>
    [Fact]
    public async Task TC_MOV_012_overview_payload()
    {
        await ResetAsync();
        await RunSnapshotAsync(T);

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/market/overview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Public research surface (BR-MOV-007): anonymous, no session cookie.
        Assert.False(response.Headers.Contains("Set-Cookie"));
        // Daily public data caching policy (`03` §13).
        Assert.True(response.Headers.CacheControl?.Public);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(T.ToString("yyyy-MM-dd"), root.GetProperty("asOf").GetString());
        Assert.False(root.GetProperty("stale").GetBoolean());

        // FR-MOV-001 — index levels + daily change.
        var xu100 = root.GetProperty("indices").GetProperty("xu100");
        AssertClose(10200.00m, xu100.GetProperty("level").GetDecimal());
        AssertClose(0.02m, xu100.GetProperty("changePct").GetDecimal());
        var xu30 = root.GetProperty("indices").GetProperty("xu30");
        AssertClose(30600.00m, xu30.GetProperty("level").GetDecimal());
        AssertClose(0.02m, xu30.GetProperty("changePct").GetDecimal());

        // FR-MOV-003 — breadth.
        var breadth = root.GetProperty("breadth");
        Assert.Equal(10, breadth.GetProperty("advancing").GetInt32());
        Assert.Equal(5, breadth.GetProperty("declining").GetInt32());
        Assert.Equal(1, breadth.GetProperty("unchanged").GetInt32());

        // FR-MOV-005 — total volume.
        AssertClose(174_500_000m, root.GetProperty("volumeTotal").GetDecimal());

        // FR-MOV-006 / BR-MOV-010 / D-05 — market P/E (loss-makers excluded + counted)
        // and market dividend yield.
        AssertClose(28.1218m, root.GetProperty("marketPe").GetDecimal());
        Assert.Equal(2, root.GetProperty("marketPeExcludedCount").GetInt32());
        AssertClose(0.011225m, root.GetProperty("marketDivYield").GetDecimal());

        // FR-MOV-004 / FR-MOV-021 / BR-MOV-011 — gainers (9 eligible, LAMDA excluded)
        // each carrying symbol, change and volume.
        var gainers = MoverList(root, "gainers");
        Assert.Equal(
            new[] { "ETA", "NEWP", "ZETA", "KAPPA", "EPSL", "PART", "REST", "ALFA", "DELTA" },
            gainers.Select(entry => entry.GetProperty("symbol").GetString()!).ToArray());
        AssertClose(0.10m, gainers[0].GetProperty("changePct").GetDecimal());
        Assert.Equal(50_000_000, gainers[0].GetProperty("volume").GetInt64());
        AssertClose(0.005025m, gainers[^1].GetProperty("changePct").GetDecimal());

        // FR-MOV-004 — losers (5 rows).
        var losers = MoverList(root, "losers");
        Assert.Equal(
            new[] { "THETA", "IOTA", "BETA", "GAMA", "UNSEC" },
            losers.Select(entry => entry.GetProperty("symbol").GetString()!).ToArray());
        AssertClose(-0.10m, losers[0].GetProperty("changePct").GetDecimal());

        // FR-MOV-002 — sector daily performance, cap-weighted (locked formula Q2).
        var sectors = root.GetProperty("sectorPerformance");
        AssertClose(0.002530m, sectors.GetProperty("A").GetDecimal());
        AssertClose(0.022409m, sectors.GetProperty("B").GetDecimal());
        AssertClose(0.041726m, sectors.GetProperty("C").GetDecimal());
    }

    // ---------------------------------------------------------------------------------
    // TC-MOV-013 — GET /market/macro payload
    // ---------------------------------------------------------------------------------

    /// <summary>TC-MOV-013 — six entries each <c>{code, value, unit, source, asOf, stale,
    /// state}</c>; inflation pair always both entries; GOLD stale with its T−10 asOf.</summary>
    [Fact]
    public async Task TC_MOV_013_macro_payload()
    {
        await ResetAsync();

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/market/macro");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(T.ToString("yyyy-MM-dd"), root.GetProperty("asOf").GetString());

        var series = Series(root);
        // Exactly the six V1 series are present (`02` §3.2), order-independent.
        Assert.Equal(6, series.Count);
        Assert.Equal(
            new[] { "TUIK_CPI", "INDEP_CPI", "CBRT_REPO", "USD_TRY", "EUR_TRY", "GOLD" }
                .OrderBy(code => code, StringComparer.Ordinal),
            series.Select(entry => entry.GetProperty("code").GetString()!).OrderBy(code => code, StringComparer.Ordinal));

        // BR-MOV-002 — the inflation pair is always both entries.
        Assert.Contains(series, entry => entry.GetProperty("code").GetString() == "TUIK_CPI");
        Assert.Contains(series, entry => entry.GetProperty("code").GetString() == "INDEP_CPI");

        // FR-MOV-009 — every value carries unit, source and as-of.
        Assert.All(series, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("unit").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("source").GetString()));
            Assert.Equal(JsonValueKind.String, entry.GetProperty("asOf").ValueKind);
        });

        var usd = SeriesByCode(series, "USD_TRY");
        AssertClose(47.10m, usd.GetProperty("value").GetDecimal());
        Assert.Equal(T.ToString("yyyy-MM-dd"), usd.GetProperty("asOf").GetString());
        Assert.False(usd.GetProperty("stale").GetBoolean());
        Assert.Equal("available", usd.GetProperty("state").GetString());

        // GOLD is the deliberately-stale fixture (FU §7: last ingest T−10).
        var gold = SeriesByCode(series, "GOLD");
        AssertClose(5200.00m, gold.GetProperty("value").GetDecimal());
        Assert.True(gold.GetProperty("stale").GetBoolean());
        Assert.Equal(T.AddDays(-10).ToString("yyyy-MM-dd"), gold.GetProperty("asOf").GetString());

        // TUIK CPI is the canonical (latest recorded_at) value for its latest date.
        var tuik = SeriesByCode(series, "TUIK_CPI");
        AssertClose(45.20m, tuik.GetProperty("value").GetDecimal());
        Assert.Equal("TÜİK", tuik.GetProperty("source").GetString());
    }

    /// <summary>TC-MOV-013 — in the degraded profile (INDEP_CPI absent) the independent
    /// entry is still present with <c>state: "unavailable"</c> and no value.</summary>
    [Fact]
    public async Task TC_MOV_013_degraded_profile_independent_cpi_present_unavailable()
    {
        await ResetAsync();
        await using (var db = _postgres.CreateContext())
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM macro_values WHERE series_code = 'INDEP_CPI'");
        }

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/market/macro");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var series = Series(document.RootElement);

        // Never silently dropped — the entry survives the outage (UXR-MOV-009, BR-MOV-002).
        var indep = SeriesByCode(series, "INDEP_CPI");
        Assert.Equal(JsonValueKind.Null, indep.GetProperty("value").ValueKind);
        Assert.Equal("unavailable", indep.GetProperty("state").GetString());
        // The label is retained (unit/source still resolved from the series definition).
        Assert.Equal("ENAG", indep.GetProperty("source").GetString());

        // The official measure is unaffected.
        var tuik = SeriesByCode(series, "TUIK_CPI");
        AssertClose(45.20m, tuik.GetProperty("value").GetDecimal());
    }

    // ---------------------------------------------------------------------------------
    // TC-MOV-014 — Equity and macro failure domains are separate
    // ---------------------------------------------------------------------------------

    /// <summary>TC-MOV-014 — prices source down + macro fresh: overview serves last-known
    /// with <c>stale: true</c> while macro still serves its values 200.</summary>
    [Fact]
    public async Task TC_MOV_014_prices_down_macro_fresh_no_cross_blanking()
    {
        await ResetAsync();
        // The equity pipeline is down: the latest completed snapshot is the prior day.
        await RunSnapshotAsync(L2.Anchor.TradingDay(-1));

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var overview = await client.GetAsync("/api/v1/market/overview");
        Assert.Equal(HttpStatusCode.OK, overview.StatusCode);
        using var overviewDocument = JsonDocument.Parse(await overview.Content.ReadAsStringAsync());
        var overviewRoot = overviewDocument.RootElement;

        Assert.True(overviewRoot.GetProperty("stale").GetBoolean());
        Assert.Equal(
            L2.Anchor.TradingDay(-1).ToString("yyyy-MM-dd"),
            overviewRoot.GetProperty("asOf").GetString());
        // Last-known equity data is still served, not blanked.
        Assert.Equal(JsonValueKind.Number, overviewRoot.GetProperty("marketPe").ValueKind);

        using var macro = await client.GetAsync("/api/v1/market/macro");
        Assert.Equal(HttpStatusCode.OK, macro.StatusCode);
        using var macroDocument = JsonDocument.Parse(await macro.Content.ReadAsStringAsync());
        var usd = SeriesByCode(Series(macroDocument.RootElement), "USD_TRY");
        AssertClose(47.10m, usd.GetProperty("value").GetDecimal());
        Assert.Equal("available", usd.GetProperty("state").GetString());
    }

    /// <summary>TC-MOV-014 — equity fresh + macro degraded: overview clean while the macro
    /// entry reports unavailable 200 (the inverse).</summary>
    [Fact]
    public async Task TC_MOV_014_macro_degraded_equity_fresh_no_cross_blanking()
    {
        await ResetAsync();
        await RunSnapshotAsync(T);
        await using (var db = _postgres.CreateContext())
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM macro_values WHERE series_code = 'INDEP_CPI'");
        }

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var overview = await client.GetAsync("/api/v1/market/overview");
        Assert.Equal(HttpStatusCode.OK, overview.StatusCode);
        using var overviewDocument = JsonDocument.Parse(await overview.Content.ReadAsStringAsync());
        Assert.False(overviewDocument.RootElement.GetProperty("stale").GetBoolean());

        using var macro = await client.GetAsync("/api/v1/market/macro");
        Assert.Equal(HttpStatusCode.OK, macro.StatusCode);
        using var macroDocument = JsonDocument.Parse(await macro.Content.ReadAsStringAsync());
        var indep = SeriesByCode(Series(macroDocument.RootElement), "INDEP_CPI");
        Assert.Equal(JsonValueKind.Null, indep.GetProperty("value").ValueKind);
        Assert.Equal("unavailable", indep.GetProperty("state").GetString());
    }

    /// <summary>TC-MOV-014 — both fresh: both clean.</summary>
    [Fact]
    public async Task TC_MOV_014_both_fresh_both_clean()
    {
        await ResetAsync();
        await RunSnapshotAsync(T);

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var overview = await client.GetAsync("/api/v1/market/overview");
        Assert.Equal(HttpStatusCode.OK, overview.StatusCode);
        using var overviewDocument = JsonDocument.Parse(await overview.Content.ReadAsStringAsync());
        Assert.False(overviewDocument.RootElement.GetProperty("stale").GetBoolean());

        using var macro = await client.GetAsync("/api/v1/market/macro");
        Assert.Equal(HttpStatusCode.OK, macro.StatusCode);
        using var macroDocument = JsonDocument.Parse(await macro.Content.ReadAsStringAsync());
        var series = Series(macroDocument.RootElement);
        // No series is unavailable when every value is present.
        Assert.All(series, entry => Assert.NotEqual("unavailable", entry.GetProperty("state").GetString()));
    }

    /// <summary>TC-MOV-014 — both down: both degrade independently (no cross-blanking).</summary>
    [Fact]
    public async Task TC_MOV_014_both_down_degrade_independently()
    {
        await ResetAsync();
        // Equity: no completed snapshot at all. Macro: every value absent.
        await using (var db = _postgres.CreateContext())
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM macro_values");
        }

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        // Overview degrades honestly — 200 with an unavailable state, not an error.
        using var overview = await client.GetAsync("/api/v1/market/overview");
        Assert.Equal(HttpStatusCode.OK, overview.StatusCode);
        using var overviewDocument = JsonDocument.Parse(await overview.Content.ReadAsStringAsync());
        Assert.Equal("unavailable", overviewDocument.RootElement.GetProperty("state").GetString());

        // Macro degrades on its own terms — every series explicitly unavailable.
        using var macro = await client.GetAsync("/api/v1/market/macro");
        Assert.Equal(HttpStatusCode.OK, macro.StatusCode);
        using var macroDocument = JsonDocument.Parse(await macro.Content.ReadAsStringAsync());
        var series = Series(macroDocument.RootElement);
        Assert.Equal(6, series.Count);
        Assert.All(series, entry =>
        {
            Assert.Equal("unavailable", entry.GetProperty("state").GetString());
            Assert.Equal(JsonValueKind.Null, entry.GetProperty("value").ValueKind);
        });
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    private static HttpClient CreateClient(DegerliAppFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    private static List<JsonElement> MoverList(JsonElement root, string property)
        => root.GetProperty(property).EnumerateArray().ToList();

    private static List<JsonElement> Series(JsonElement root)
        => root.GetProperty("series").EnumerateArray().ToList();

    private static JsonElement SeriesByCode(List<JsonElement> series, string code)
        => series.Single(entry => entry.GetProperty("code").GetString() == code);

    private static void AssertClose(decimal expected, decimal actual) =>
        Assert.True(
            Math.Abs(actual - expected) <= GoldenTolerance,
            $"expected {expected} (±{GoldenTolerance}) but was {actual}");

    /// <summary>Applies the fixture universe and clears the mutable snapshot state.</summary>
    private async Task ResetAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM market_snapshots");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
    }

    /// <summary>Runs the real <c>market-snapshot</c> job for <paramref name="date"/>.</summary>
    private async Task RunSnapshotAsync(DateOnly date)
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
        var result = await runner.RunAsync(MarketSnapshotJob.Code, new IngestionRequest(date));
        Assert.Equal("succeeded", result.Status);
    }
}
