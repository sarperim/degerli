using System.Text.Json;
using System.Text.Json.Serialization;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Metrics;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-mov-003 acceptance — the <c>market-snapshot</c> job (MOV Group A):
/// TC-MOV-001 (FU §11.3 golden values), TC-MOV-002 (mover volume eligibility),
/// TC-MOV-003 (top-10 cap + exact-threshold eligibility), TC-MOV-004 (market P/E
/// loss-maker exclusion + degenerate all-loss case) and TC-MOV-005 (one snapshot per
/// trading day; the latest completed snapshot is the one served).
///
/// Every test seeds the FU §3–§6 facts into a migrated Testcontainers PostgreSQL and
/// runs the real <c>market-snapshot</c> job through the TKT-mdf-002 runner — the same
/// job code the nightly chain and admin trigger use. Goldens are hand-derived in FU
/// §11.3 (never re-derived from the implementation).
/// </summary>
public sealed class MarketSnapshotTests : IClassFixture<PostgresFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    // FU §11.3 value goldens are quoted at mixed granularities (market_pe at 4 dp,
    // market_div_yield at 6 dp, sector performance at a 4-dp percentage). Compare at
    // the coarsest printed granularity so a correct implementation matches regardless
    // of the doc's per-value rounding — a strictly finer absolute tie (1e-5) would
    // reject market_pe = 33465/1190 = 28.121849 against the doc's 28.1218.
    private const decimal GoldenTolerance = 0.0001m;

    private readonly PostgresFixture _postgres;

    public MarketSnapshotTests(PostgresFixture postgres) => _postgres = postgres;

    /// <summary>TC-MOV-001 — market snapshot golden values (FU §11.3).</summary>
    [Fact]
    public async Task TC_MOV_001_market_snapshot_golden_values()
    {
        await ResetAsync();
        await using var host = BuildHost();

        var result = await RunSnapshotAsync(host, T);
        Assert.Equal("succeeded", result.Status);

        await using var db = _postgres.CreateContext();
        var snapshot = await db.MarketSnapshots.SingleAsync(s => s.SnapshotDate == T);

        Assert.Equal(T, snapshot.SnapshotDate);
        Assert.Equal(T, snapshot.AsOfTradingDate);

        // FR-MOV-001 — index levels + daily change.
        AssertClose(10200.00m, snapshot.Xu100Level);
        AssertClose(0.02m, snapshot.Xu100ChangePct);
        AssertClose(30600.00m, snapshot.Xu30Level);
        AssertClose(0.02m, snapshot.Xu30ChangePct);

        // FR-MOV-003 — breadth.
        Assert.Equal(10, snapshot.BreadthAdvancing);
        Assert.Equal(5, snapshot.BreadthDeclining);
        Assert.Equal(1, snapshot.BreadthUnchanged);

        // FR-MOV-005 — total volume.
        AssertClose(174_500_000m, snapshot.VolumeTotal);

        // FR-MOV-006 / BR-MOV-010 / D-05 — market P/E (loss-makers excluded + counted)
        // and market dividend yield.
        AssertClose(28.1218m, snapshot.MarketPe);
        Assert.Equal(2, snapshot.MarketPeExcludedCount);
        AssertClose(0.011225m, snapshot.MarketDivYield);

        // FR-MOV-004 / FR-MOV-021 / BR-MOV-011 — gainers (9 eligible, LAMDA excluded).
        var gainers = ReadMovers(snapshot.GainersJson);
        Assert.Equal(
            new[] { "ETA", "NEWP", "ZETA", "KAPPA", "EPSL", "PART", "REST", "ALFA", "DELTA" },
            gainers.Select(g => g.Symbol).ToArray());
        AssertClose(0.10m, gainers[0].ChangePct);
        Assert.Equal(50_000_000, gainers[0].Volume);
        AssertClose(0.005025m, gainers[^1].ChangePct);
        Assert.Equal(6_000_000, gainers[^1].Volume);

        // FR-MOV-004 — losers (5 rows).
        var losers = ReadMovers(snapshot.LosersJson);
        Assert.Equal(
            new[] { "THETA", "IOTA", "BETA", "GAMA", "UNSEC" },
            losers.Select(l => l.Symbol).ToArray());
        AssertClose(-0.10m, losers[0].ChangePct);
        Assert.Equal(40_000_000, losers[0].Volume);

        // FR-MOV-002 — sector daily performance, cap-weighted (locked formula Q2).
        var sectors = ReadSectors(snapshot.SectorPerfJson);
        AssertClose(0.002530m, sectors["A"]);
        AssertClose(0.022409m, sectors["B"]);
        AssertClose(0.041726m, sectors["C"]);
        Assert.False(sectors.ContainsKey(string.Empty));
    }

    /// <summary>TC-MOV-002 — mover eligibility excludes below-threshold volume.</summary>
    [Fact]
    public async Task TC_MOV_002_movers_eligibility_excludes_ineligible_volume()
    {
        await ResetAsync();
        await using var host = BuildHost();

        await RunSnapshotAsync(host, T);

        await using var db = _postgres.CreateContext();
        var snapshot = await db.MarketSnapshots.SingleAsync(s => s.SnapshotDate == T);
        var gainers = ReadMovers(snapshot.GainersJson);

        // LAMDA (+25%) has the largest change but 500,000 shares < the 1,000,000
        // eligibility threshold — it must be absent.
        Assert.DoesNotContain("LAMDA", gainers.Select(g => g.Symbol));

        // Every listed entry carries its traded volume (FR-MOV-021) and is a
        // current-universe stock (UC-MOV-004 alt a invariant, I-MOV-3).
        var universeSymbols = (await db.Instruments.Select(i => i.Symbol).ToListAsync())
            .ToHashSet(StringComparer.Ordinal);
        Assert.All(gainers, g => Assert.True(g.Volume > 0));
        Assert.All(gainers, g => Assert.Contains(g.Symbol, universeSymbols));
        Assert.All(ReadMovers(snapshot.LosersJson), l => Assert.True(l.Volume > 0));
    }

    /// <summary>TC-MOV-003 — top-10 cap and exact-threshold eligibility (BVA).</summary>
    [Fact]
    public async Task TC_MOV_003_top_ten_cap_and_exact_threshold_eligibility()
    {
        await ResetAsync();
        await SeedVariantThresholdAsync();
        await using var host = BuildHost();

        await RunSnapshotAsync(host, T);

        await using var db = _postgres.CreateContext();
        var snapshot = await db.MarketSnapshots.SingleAsync(s => s.SnapshotDate == T);
        var gainers = ReadMovers(snapshot.GainersJson);

        // 12 eligible gainers (the standard 9 plus BETA/GAMA/IOTA forced positive);
        // the list caps at 10, ranked by % change descending.
        Assert.Equal(10, gainers.Count);
        Assert.Equal(
            new[] { "ETA", "IOTA", "GAMA", "NEWP", "ZETA", "BETA", "KAPPA", "EPSL", "PART", "REST" },
            gainers.Select(g => g.Symbol).ToArray());

        // The 11th/12th candidates (ALFA, DELTA) are excluded by the cap — not by
        // eligibility: ALFA sits exactly on the 1,000,000 boundary (inclusive) and is
        // the first candidate the cap drops. The inclusive boundary itself is pinned
        // by MarketAggregatesTests.TC_MOV_003_exact_threshold_is_inclusive.
        Assert.DoesNotContain("ALFA", gainers.Select(g => g.Symbol));
        Assert.DoesNotContain("DELTA", gainers.Select(g => g.Symbol));
        Assert.DoesNotContain("LAMDA", gainers.Select(g => g.Symbol));

        // The last admitted entry is REST at +1.2151%.
        Assert.Equal("REST", gainers[^1].Symbol);
        AssertClose(0.012151m, gainers[^1].ChangePct);
    }

    /// <summary>TC-MOV-004 — market P/E exclusion rule and degenerate case.</summary>
    [Fact]
    public async Task TC_MOV_004_market_pe_exclusion_and_degenerate_case()
    {
        // (a) The TC-MOV-001 state discloses the two loss-makers (BETA, GAMA).
        await ResetAsync();
        await using (var host = BuildHost())
        {
            await RunSnapshotAsync(host, T);
        }

        await using (var db = _postgres.CreateContext())
        {
            var snapshot = await db.MarketSnapshots.SingleAsync(s => s.SnapshotDate == T);
            Assert.Equal(2, snapshot.MarketPeExcludedCount);
            Assert.NotNull(snapshot.MarketPe);
            AssertClose(28.1218m, snapshot.MarketPe);
        }

        // (b) Every constituent a loss-maker → market_pe is NULL (not 0, not negative)
        // with excluded_count = the universe size (I-MOV-2, honest unavailable aggregate).
        await ResetAsync();
        await using (var db = _postgres.CreateContext())
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE fin_line_items SET value = -ABS(value) WHERE item_code = 'NI'");
        }

        await using (var host = BuildHost())
        {
            await RunSnapshotAsync(host, T);
        }

        await using (var db = _postgres.CreateContext())
        {
            var snapshot = await db.MarketSnapshots.SingleAsync(s => s.SnapshotDate == T);
            Assert.Null(snapshot.MarketPe);
            Assert.Equal(16, snapshot.MarketPeExcludedCount);
        }
    }

    /// <summary>TC-MOV-005 — one snapshot per trading day; latest completed is served.</summary>
    [Fact]
    public async Task TC_MOV_005_one_snapshot_per_trading_day_latest_served()
    {
        await ResetAsync();
        await using var host = BuildHost();

        await RunSnapshotAsync(host, T);
        await RunSnapshotAsync(host, T); // idempotent re-run

        var prior = L2.Anchor.TradingDay(-1);
        await RunSnapshotAsync(host, prior);

        await using var db = _postgres.CreateContext();

        // Exactly one row per trading day (idempotent by snapshot_date).
        Assert.Equal(1, await db.MarketSnapshots.CountAsync(s => s.SnapshotDate == T));
        Assert.Equal(2, await db.MarketSnapshots.CountAsync());

        // The row carries its own as-of trading date (honesty stamp) — the datum the
        // read path (GET /market/overview, TKT-mov-004) serves as "latest completed".
        var latest = await db.MarketSnapshots
            .OrderByDescending(s => s.SnapshotDate)
            .FirstAsync();
        Assert.Equal(T, latest.SnapshotDate);
        Assert.Equal(T, latest.AsOfTradingDate);
        Assert.Equal(prior, (await db.MarketSnapshots.OrderBy(s => s.SnapshotDate).FirstAsync()).SnapshotDate);
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    private ServiceProvider BuildHost()
    {
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

        return services.BuildServiceProvider();
    }

    private static async Task<IngestResult> RunSnapshotAsync(IServiceProvider provider, DateOnly date)
    {
        using var scope = provider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IIngestionJobRunner>();
        return await runner.RunAsync(MarketSnapshotJob.Code, new IngestionRequest(date));
    }

    private async Task ResetAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM market_snapshots");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
    }

    /// <summary>
    /// FU §10 <c>prices-variant-threshold</c> state: BETA/GAMA/IOTA forced into gainers
    /// and ALFA's volume pinned to exactly the 1,000,000 eligibility threshold, with
    /// LAMDA retained as the illiquid big gainer to exclude. The snapshot job consumes
    /// canonical facts, so the variant is applied to the stored daily prices directly.
    /// </summary>
    private async Task SeedVariantThresholdAsync()
    {
        await using var db = _postgres.CreateContext();
        var ids = await db.Instruments.ToDictionaryAsync(i => i.Symbol, i => i.Id);

        foreach (var symbol in new[] { "BETA", "GAMA", "IOTA" })
        {
            var row = await db.DailyPrices
                .SingleAsync(p => p.InstrumentId == ids[symbol] && p.PriceDate == T);
            row.CloseRaw += 0.50m;
        }

        var alfa = await db.DailyPrices
            .SingleAsync(p => p.InstrumentId == ids["ALFA"] && p.PriceDate == T);
        alfa.Volume = 1_000_000;

        await db.SaveChangesAsync();
    }

    private static IReadOnlyList<MoverEntryDto> ReadMovers(string? json)
    {
        Assert.False(string.IsNullOrWhiteSpace(json));
        return JsonSerializer.Deserialize<List<MoverEntryDto>>(json!)!;
    }

    private static Dictionary<string, decimal> ReadSectors(string? json)
    {
        Assert.False(string.IsNullOrWhiteSpace(json));
        return JsonSerializer.Deserialize<Dictionary<string, decimal>>(json!)!;
    }

    private static void AssertClose(decimal expected, decimal? actual)
    {
        Assert.NotNull(actual);
        Assert.True(
            Math.Abs(actual!.Value - expected) <= GoldenTolerance,
            $"expected {expected} (±{GoldenTolerance}) but was {actual.Value}");
    }

    private static void AssertClose(decimal expected, decimal actual) =>
        Assert.True(
            Math.Abs(actual - expected) <= GoldenTolerance,
            $"expected {expected} (±{GoldenTolerance}) but was {actual}");

    private sealed record MoverEntryDto(
        [property: JsonPropertyName("symbol")] string Symbol,
        [property: JsonPropertyName("change_pct")] decimal ChangePct,
        [property: JsonPropertyName("volume")] long Volume);
}
