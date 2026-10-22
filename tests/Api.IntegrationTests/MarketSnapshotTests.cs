using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    /// <summary>
    /// TKT-foundation-025 (FLG-08; FR-MOV-001) — the BIST 30 index move resolves whichever
    /// spelling the stored world carries. The real chain materializes <c>XU030</c> (the
    /// <c>Xu030LaunchSeed</c> index row), while the FU world stores <c>XU30</c>; the engine
    /// must not silently degrade the BIST 30 move to (0, 0) when only the real spelling is
    /// present. Renaming the FU index to the real spelling (as a real-only DB has it) must
    /// still yield a non-degenerate BIST 30 level + daily change, with the XU100 move
    /// resolving alongside. The FU world's own <c>XU30</c> goldens (TC-MOV-001) are a
    /// separate path (no <c>XU030</c> row there, so the extra spelling is a no-op).
    /// </summary>
    [Fact]
    public async Task TKT_foundation_025_bist30_move_resolves_both_spellings()
    {
        await ResetAsync();
        await RenameIndexAsync("XU30", "XU030");
        try
        {
            await using var host = BuildHost();
            var result = await RunSnapshotAsync(host, T);
            Assert.Equal("succeeded", result.Status);

            await using var db = _postgres.CreateContext();
            var snapshot = await db.MarketSnapshots.SingleAsync(s => s.SnapshotDate == T);

            // The real BIST 30 spelling XU030 resolves: non-degenerate level + change
            // (a bare (0, 0) is exactly the regression this guards).
            Assert.NotEqual(0m, snapshot.Xu30Level);
            AssertClose(30600.00m, snapshot.Xu30Level);
            AssertClose(0.02m, snapshot.Xu30ChangePct);

            // The XU100 move still resolves alongside over the covered universe.
            Assert.NotEqual(0m, snapshot.Xu100Level);
            AssertClose(10200.00m, snapshot.Xu100Level);
            AssertClose(0.02m, snapshot.Xu100ChangePct);
        }
        finally
        {
            await RenameIndexAsync("XU030", "XU30");
        }
    }

    // ---------------------------------------------------------------------------------
    // TKT-foundation-028 — target-day selection = latest stored trading day (01 §10.9.13)
    //
    // Weekend UX / cold-start catch-up: the day the snapshot formulas run over is the
    // latest trading day <= request.Date that has stored prices for the covered universe
    // — stored prices are the truth, never request.Date and never a weekday rule. The
    // serving contract of GET /market/overview is unchanged (latest row + stale), so once
    // a real Friday row exists the weekend overview serves it with stale:false (TC-MOV-012).
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// TKT-foundation-028 — a Saturday request (2026-10-17) with stored prices through
    /// Friday (2026-10-16) writes exactly one row at <c>SnapshotDate = 2026-10-16</c>
    /// computed from the stored Friday prices, and the weekend overview then serves it
    /// (<c>asOf = 2026-10-16</c>, <c>stale: false</c>).
    /// </summary>
    [Fact]
    public async Task TKT_foundation_028_weekend_request_snapshots_latest_stored_trading_day()
    {
        await ResetAsync();
        var thursday = new DateOnly(2026, 10, 15);
        var friday = new DateOnly(2026, 10, 16);
        var saturday = new DateOnly(2026, 10, 17);
        await SeedStoredPriceWindowAsync(thursday, friday);

        IngestResult result;
        await using (var host = BuildHost())
        {
            result = await RunSnapshotAsync(host, saturday);
        }

        Assert.Equal("succeeded", result.Status);
        Assert.Equal(1, result.Written);
        Assert.Equal(0, result.Unchanged);

        await using (var db = _postgres.CreateContext())
        {
            var snapshot = await db.MarketSnapshots.SingleAsync();
            Assert.Equal(friday, snapshot.SnapshotDate);
            Assert.Equal(friday, snapshot.AsOfTradingDate);
        }

        // The weekend overview serves the real Friday row — Friday resolves as the latest
        // trading day on Saturday, so the row is not stale (the weekend UX).
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        factory.Clock.SetUtcNow(new DateTimeOffset(saturday.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

        using var response = await client.GetAsync("/api/v1/market/overview");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal("2026-10-16", root.GetProperty("asOf").GetString());
        Assert.False(root.GetProperty("stale").GetBoolean());
        Assert.Equal("available", root.GetProperty("state").GetString());
    }

    /// <summary>
    /// TKT-foundation-028 (TC-MOV-005 one-row-per-trading-day) — re-running the same weekend
    /// request after Friday is already snapshotted with **identical inputs** recomputes the
    /// day and reports the honest <c>unchanged: 1</c> (TKT-foundation-042 made the guard
    /// input-aware; before it the run reported 0/0 — a counting lie, since the day *was*
    /// checked). Nothing is rewritten and still exactly one row exists.
    /// </summary>
    [Fact]
    public async Task TKT_foundation_028_weekend_rerun_after_snapshotted_is_noop()
    {
        await ResetAsync();
        var thursday = new DateOnly(2026, 10, 15);
        var friday = new DateOnly(2026, 10, 16);
        var saturday = new DateOnly(2026, 10, 17);
        await SeedStoredPriceWindowAsync(thursday, friday);

        await using var host = BuildHost();
        var first = await RunSnapshotAsync(host, saturday);
        Assert.Equal(1, first.Written);

        var second = await RunSnapshotAsync(host, saturday);
        Assert.Equal("succeeded", second.Status);
        Assert.Equal(0, second.Written);
        Assert.Equal(1, second.Unchanged);

        await using var db = _postgres.CreateContext();
        Assert.Equal(1, await db.MarketSnapshots.CountAsync());
        Assert.Equal(1, await db.MarketSnapshots.CountAsync(s => s.SnapshotDate == friday));
    }

    /// <summary>
    /// TKT-foundation-028 — when <c>request.Date</c> is itself a trading day with stored
    /// prices the row lands on that day (behaviour unchanged: the day resolved from stored
    /// facts is the requested day).
    /// </summary>
    [Fact]
    public async Task TKT_foundation_028_trading_day_request_snapshots_that_day()
    {
        await ResetAsync();
        var thursday = new DateOnly(2026, 10, 15);
        var friday = new DateOnly(2026, 10, 16);
        await SeedStoredPriceWindowAsync(thursday, friday);

        IngestResult result;
        await using (var host = BuildHost())
        {
            result = await RunSnapshotAsync(host, friday);
        }

        Assert.Equal(1, result.Written);

        await using var db = _postgres.CreateContext();
        var snapshot = await db.MarketSnapshots.SingleAsync();
        Assert.Equal(friday, snapshot.SnapshotDate);
        Assert.Equal(friday, snapshot.AsOfTradingDate);
    }

    /// <summary>
    /// TKT-foundation-028 — a request with no stored price ≤ it (for the covered universe)
    /// writes no row: <c>Succeeded</c>, 0 written, and the overview keeps serving the honest
    /// <c>state: unavailable</c> (the empty rule, unchanged).
    /// </summary>
    [Fact]
    public async Task TKT_foundation_028_no_stored_price_writes_no_row_and_overview_unavailable()
    {
        await ResetAsync();
        // Earlier than every stored price (the FU world's earliest row is EPSL 2025-05-30).
        var beforeAnyPrice = new DateOnly(2024, 1, 1);

        IngestResult result;
        await using (var host = BuildHost())
        {
            result = await RunSnapshotAsync(host, beforeAnyPrice);
        }

        Assert.Equal("succeeded", result.Status);
        Assert.Equal(0, result.Written);

        await using (var db = _postgres.CreateContext())
        {
            Assert.Equal(0, await db.MarketSnapshots.CountAsync());
        }

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

        using var response = await client.GetAsync("/api/v1/market/overview");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("unavailable", document.RootElement.GetProperty("state").GetString());
    }

    // ---------------------------------------------------------------------------------
    // TKT-foundation-031 — price-derived vs share-derived aggregate decoupling (01 §10.9.14)
    //
    // A covered-universe member with no usable SHARES_DILUTED is no longer dropped from the
    // snapshot: it enters the price-derived blocks (breadth, movers, total volume) and is
    // excluded from the market-cap-derived blocks (market P/E, dividend yield, sector
    // weights), where the exclusion is disclosed via market_pe_excluded_count. The
    // share-derived blocks carry their honest null / empty state — never a fabricated 0.
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// (i) TKT-foundation-031 — zero share data (the real dev-stack shape): the job writes
    /// exactly one row and <c>GET /market/overview</c> serves real price-derived blocks with
    /// <c>marketPe</c>/<c>marketDivYield</c> null, <c>marketPeExcludedCount</c> = the
    /// constituent count and an empty <c>sectorPerformance</c>.
    /// </summary>
    [Fact]
    public async Task TKT_foundation_031_zero_shares_serves_price_derived_blocks_with_honest_nulls()
    {
        await ResetAsync();
        await DeleteSharesAsync(); // every constituent loses its balance-sheet share count

        IngestResult result;
        await using (var host = BuildHost())
        {
            result = await RunSnapshotAsync(host, T);
        }

        Assert.Equal("succeeded", result.Status);
        Assert.Equal(1, result.Written);

        // Price-derived blocks are computable from the stored prices alone; the FU world's
        // price facts are unchanged by the missing share counts.
        await using (var db = _postgres.CreateContext())
        {
            var snapshot = await db.MarketSnapshots.SingleAsync();
            Assert.Equal(T, snapshot.SnapshotDate);
            Assert.Equal(T, snapshot.AsOfTradingDate);
            AssertClose(10200.00m, snapshot.Xu100Level);
            AssertClose(0.02m, snapshot.Xu100ChangePct);
            AssertClose(30600.00m, snapshot.Xu30Level);
            AssertClose(0.02m, snapshot.Xu30ChangePct);
            Assert.Equal(10, snapshot.BreadthAdvancing);
            Assert.Equal(5, snapshot.BreadthDeclining);
            Assert.Equal(1, snapshot.BreadthUnchanged);
            AssertClose(174_500_000m, snapshot.VolumeTotal);
            Assert.Equal(
                new[] { "ETA", "NEWP", "ZETA", "KAPPA", "EPSL", "PART", "REST", "ALFA", "DELTA" },
                ReadMovers(snapshot.GainersJson).Select(g => g.Symbol).ToArray());
            Assert.Equal(
                new[] { "THETA", "IOTA", "BETA", "GAMA", "UNSEC" },
                ReadMovers(snapshot.LosersJson).Select(l => l.Symbol).ToArray());

            // Share-derived blocks: honest nulls, the whole universe disclosed as excluded.
            Assert.Null(snapshot.MarketPe);
            Assert.Equal(16, snapshot.MarketPeExcludedCount);
            Assert.Null(snapshot.MarketDivYield);
            Assert.Empty(ReadSectors(snapshot.SectorPerfJson));
        }

        // The served payload is available (not unavailable) and carries the same honest state.
        // The harness clock is already pinned to T (18:00 UTC), so the served row is not stale.
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

        using var response = await client.GetAsync("/api/v1/market/overview");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(T, DateOnly.Parse(root.GetProperty("asOf").GetString()!));
        Assert.False(root.GetProperty("stale").GetBoolean());
        Assert.Equal("available", root.GetProperty("state").GetString());
        Assert.Equal(10200.00m, root.GetProperty("indices").GetProperty("xu100").GetProperty("level").GetDecimal());
        Assert.Equal(30600.00m, root.GetProperty("indices").GetProperty("xu30").GetProperty("level").GetDecimal());
        Assert.Equal(10, root.GetProperty("breadth").GetProperty("advancing").GetInt32());
        Assert.Equal(5, root.GetProperty("breadth").GetProperty("declining").GetInt32());
        Assert.Equal(1, root.GetProperty("breadth").GetProperty("unchanged").GetInt32());
        Assert.Equal(174_500_000m, root.GetProperty("volumeTotal").GetDecimal());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("marketPe").ValueKind);
        Assert.Equal(16, root.GetProperty("marketPeExcludedCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("marketDivYield").ValueKind);
        Assert.Empty(root.GetProperty("sectorPerformance").EnumerateObject());
        Assert.Equal(9, root.GetProperty("gainers").GetArrayLength());
        Assert.Equal(5, root.GetProperty("losers").GetArrayLength());
    }

    /// <summary>
    /// (ii) TKT-foundation-031 — partial share data: only sector A keeps its share counts.
    /// P/E, dividend yield and sector weights are computed over the share-bearing subset
    /// (ALFA/DELTA/REST profitable: cap 3,750, NI 325, DIV_TTM 150), the sector dictionary
    /// omits every sector without a share-bearing member, and the no-share members still
    /// count in breadth/movers/volume and in the disclosed exclusion count.
    /// </summary>
    [Fact]
    public async Task TKT_foundation_031_partial_shares_aggregate_over_share_bearing_subset()
    {
        await ResetAsync();
        // Keep the share counts of the sector-A members only (ALFA, BETA, GAMA, DELTA, REST).
        await DeleteSharesAsync("EPSL", "ZETA", "ETA", "THETA", "PART", "NEWP",
            "IOTA", "KAPPA", "LAMDA", "NU", "UNSEC");

        IngestResult result;
        await using (var host = BuildHost())
        {
            result = await RunSnapshotAsync(host, T);
        }

        Assert.Equal("succeeded", result.Status);
        Assert.Equal(1, result.Written);

        await using var db = _postgres.CreateContext();
        var snapshot = await db.MarketSnapshots.SingleAsync();

        // Price-derived blocks are unchanged: every no-share member still participates.
        Assert.Equal(10, snapshot.BreadthAdvancing);
        Assert.Equal(5, snapshot.BreadthDeclining);
        Assert.Equal(1, snapshot.BreadthUnchanged);
        AssertClose(174_500_000m, snapshot.VolumeTotal);
        Assert.Equal(
            new[] { "ETA", "NEWP", "ZETA", "KAPPA", "EPSL", "PART", "REST", "ALFA", "DELTA" },
            ReadMovers(snapshot.GainersJson).Select(g => g.Symbol).ToArray());
        Assert.Equal(
            new[] { "THETA", "IOTA", "BETA", "GAMA", "UNSEC" },
            ReadMovers(snapshot.LosersJson).Select(l => l.Symbol).ToArray());

        // P/E over share-bearing profitable members only: 3,750 mcap / 325 NI.
        Assert.NotNull(snapshot.MarketPe);
        AssertClose(11.5385m, snapshot.MarketPe);

        // Every member not in the P/E sum is disclosed: 11 no-share + BETA/GAMA (loss-makers).
        Assert.Equal(13, snapshot.MarketPeExcludedCount);

        // Dividend yield over every share-bearing member (BETA/GAMA keep their share counts):
        // 150 DIV_TTM / 5,030 mcap.
        Assert.NotNull(snapshot.MarketDivYield);
        AssertClose(0.029821m, snapshot.MarketDivYield);

        // Sector weights only over share-bearing members; B and C have none → omitted.
        var sectors = ReadSectors(snapshot.SectorPerfJson);
        Assert.Equal(new[] { "A" }, sectors.Keys.ToArray());
        AssertClose(0.002530m, sectors["A"]);

        // The served payload stays available and mirrors the honest partial state.
        // The harness clock is already pinned to T (18:00 UTC), so the served row is not stale.
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

        using var response = await client.GetAsync("/api/v1/market/overview");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal("available", root.GetProperty("state").GetString());
        Assert.Equal(13, root.GetProperty("marketPeExcludedCount").GetInt32());
        Assert.Equal(
            new[] { "A" },
            root.GetProperty("sectorPerformance").EnumerateObject().Select(p => p.Name).ToArray());
    }

    // ---------------------------------------------------------------------------------
    // TKT-foundation-042 — honest index levels + input-aware same-day repair (01 §10.9.13/14)
    //
    // (C) A resolved day whose index facts have not landed yet must not fabricate
    //     `xu100_level = 0.0000`: the level/change columns are honestly NULL, the
    //     price-derived blocks still materialise (the 031 doctrine) and the overview
    //     serves `state: available` with `indices: null` (the Web's Unavailable block).
    // (B) The resolved-day guard is input-aware: identical facts → honest `unchanged: 1`;
    //     changed facts → the row is repaired in place and the ledger counts the repair.
    //     Historical days stay immutable — the store only ever touches the resolved day.
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// TC-MOV-034 (TKT-foundation-042) — prices exist but the day's index levels do not:
    /// the row is written with NULL level/change columns (never 0.0000), the
    /// price-derived blocks are real, and <c>GET /market/overview</c> serves
    /// <c>state: available</c> with an honest <c>indices: null</c>. When the levels land,
    /// the next run repairs the same row with the real figures.
    /// </summary>
    [Fact]
    public async Task TC_MOV_034_missing_index_levels_write_null_and_serve_honest_empty_indices()
    {
        await ResetAsync();
        var removed = await RemoveIndexLevelsAsync(T);
        try
        {
            IngestResult result;
            await using (var host = BuildHost())
            {
                result = await RunSnapshotAsync(host, T);
            }

            Assert.Equal("succeeded", result.Status);
            Assert.Equal(1, result.Written);

            await using (var db = _postgres.CreateContext())
            {
                var snapshot = await db.MarketSnapshots.SingleAsync(s => s.SnapshotDate == T);

                // FR-MOV-001 honest empty: a missing index fact is NULL, not a 0.0000 level
                // with a 0.00% change (the 031/M-1 honesty class).
                Assert.Null(snapshot.Xu100Level);
                Assert.Null(snapshot.Xu100ChangePct);
                Assert.Null(snapshot.Xu30Level);
                Assert.Null(snapshot.Xu30ChangePct);

                // The price-derived blocks are untouched by the missing index facts: a
                // missing index level never takes the row down (01 §10.9.14).
                Assert.Equal(10, snapshot.BreadthAdvancing);
                Assert.Equal(5, snapshot.BreadthDeclining);
                Assert.Equal(1, snapshot.BreadthUnchanged);
                AssertClose(174_500_000m, snapshot.VolumeTotal);
                Assert.Equal(
                    new[] { "ETA", "NEWP", "ZETA", "KAPPA", "EPSL", "PART", "REST", "ALFA", "DELTA" },
                    ReadMovers(snapshot.GainersJson).Select(g => g.Symbol).ToArray());
                Assert.Equal(
                    new[] { "THETA", "IOTA", "BETA", "GAMA", "UNSEC" },
                    ReadMovers(snapshot.LosersJson).Select(l => l.Symbol).ToArray());
            }

            // The overview serves the honest empty indices block — never a fabricated zero.
            await using (var factory = new DegerliAppFactory(_postgres.ConnectionString))
            {
                using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
                {
                    BaseAddress = new Uri("https://localhost"),
                    AllowAutoRedirect = false,
                });

                using var response = await client.GetAsync("/api/v1/market/overview");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var root = document.RootElement;

                Assert.Equal(T, DateOnly.Parse(root.GetProperty("asOf").GetString()!));
                Assert.Equal("available", root.GetProperty("state").GetString());
                Assert.Equal(JsonValueKind.Null, root.GetProperty("indices").ValueKind);

                // The rest of the payload is real, not blanked by the missing indices.
                Assert.Equal(10, root.GetProperty("breadth").GetProperty("advancing").GetInt32());
                Assert.Equal(5, root.GetProperty("breadth").GetProperty("declining").GetInt32());
                Assert.Equal(174_500_000m, root.GetProperty("volumeTotal").GetDecimal());
                Assert.Equal(9, root.GetProperty("gainers").GetArrayLength());
                Assert.Equal(5, root.GetProperty("losers").GetArrayLength());
            }

            // The day's levels land afterwards: the next run repairs this very row
            // (one row per trading day stays true — TC-MOV-035 pins the counting).
            await RestoreIndexLevelsAsync(removed);

            IngestResult repair;
            await using (var host = BuildHost())
            {
                repair = await RunSnapshotAsync(host, T);
            }

            Assert.Equal("succeeded", repair.Status);

            // The repair is a written row for this run (the pre-ticket behaviour reported
            // 0/0 and left the broken row in place): the resolved day was recomputed from
            // the changed facts and the row was overwritten in place, never duplicated.
            Assert.Equal(1, repair.Written);
            Assert.Equal(0, repair.Unchanged);

            await using (var db = _postgres.CreateContext())
            {
                var snapshot = await db.MarketSnapshots.SingleAsync(s => s.SnapshotDate == T);
                AssertClose(10200.00m, snapshot.Xu100Level);
                AssertClose(0.02m, snapshot.Xu100ChangePct);
                AssertClose(30600.00m, snapshot.Xu30Level);
                AssertClose(0.02m, snapshot.Xu30ChangePct);
            }

            await using (var factory = new DegerliAppFactory(_postgres.ConnectionString))
            {
                using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
                {
                    BaseAddress = new Uri("https://localhost"),
                    AllowAutoRedirect = false,
                });

                using var response = await client.GetAsync("/api/v1/market/overview");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var indices = document.RootElement.GetProperty("indices");
                Assert.Equal(
                    10200.00m,
                    indices.GetProperty("xu100").GetProperty("level").GetDecimal());
                Assert.Equal(
                    30600.00m,
                    indices.GetProperty("xu30").GetProperty("level").GetDecimal());
            }
        }
        finally
        {
            await RestoreIndexLevelsAsync(removed);
        }
    }

    /// <summary>
    /// TC-MOV-035 (TKT-foundation-042) — the same-day recompute is input-aware: an
    /// identical re-run reports the honest <c>unchanged: 1</c> (the pre-ticket
    /// behaviour counted 0), and a re-run after the day's index levels land repairs the
    /// existing row in place with the corrected values, counted in the run ledger instead
    /// of the pre-ticket silent <c>written: 0, unchanged: 0</c>. Exactly one row per
    /// trading day in both cases, and a historical day's row is never touched (it is not
    /// the resolved day).
    /// </summary>
    [Fact]
    public async Task TC_MOV_035_same_day_rerun_is_input_aware_and_repairs_in_place()
    {
        await ResetAsync();

        // A completed earlier day: under the input-aware guard it must stay untouched.
        var prior = L2.Anchor.TradingDay(-1);
        IngestResult priorRun;
        await using (var host = BuildHost())
        {
            priorRun = await RunSnapshotAsync(host, prior);
        }

        Assert.Equal("succeeded", priorRun.Status);
        Assert.Equal(1, priorRun.Written);
        var priorBefore = await ReadSnapshotRowAsync(prior);

        var removed = await RemoveIndexLevelsAsync(T);
        try
        {
            // The first compute of the day, before its index levels landed.
            await using (var host = BuildHost())
            {
                var first = await RunSnapshotAsync(host, T);
                Assert.Equal("succeeded", first.Status);
                Assert.Equal(1, first.Written);
            }

            // Identical inputs: an honest unchanged count, not the pre-ticket 0/0.
            IngestResult unchangedRun;
            await using (var host = BuildHost())
            {
                unchangedRun = await RunSnapshotAsync(host, T);
            }

            Assert.Equal("succeeded", unchangedRun.Status);
            Assert.Equal(0, unchangedRun.Written);
            Assert.Equal(1, unchangedRun.Unchanged);

            // The honest count reaches the run ledger too (it serialises the run's stats).
            Assert.Equal(0, await ReadLatestLedgerStatAsync("written"));
            Assert.Equal(1, await ReadLatestLedgerStatAsync("unchanged"));

            // Changed inputs (the day's index levels land after the first compute): the
            // re-run repairs the row in place instead of no-op'ing on a broken row.
            await RestoreIndexLevelsAsync(removed);

            IngestResult repairRun;
            await using (var host = BuildHost())
            {
                repairRun = await RunSnapshotAsync(host, T);
            }

            Assert.Equal("succeeded", repairRun.Status);
            Assert.Equal(1, repairRun.Written);
            Assert.Equal(0, repairRun.Unchanged);

            // The repair is visible in the run ledger (pre-ticket it was the silent
            // `written: 0, unchanged: 0`), so operational repair needs no SQL surgery.
            Assert.Equal(1, await ReadLatestLedgerStatAsync("written"));
            Assert.Equal(0, await ReadLatestLedgerStatAsync("unchanged"));

            await using (var db = _postgres.CreateContext())
            {
                // Exactly one row per trading day — the repair overwrote, never appended.
                Assert.Equal(1, await db.MarketSnapshots.CountAsync(s => s.SnapshotDate == T));
                Assert.Equal(2, await db.MarketSnapshots.CountAsync());

                var snapshot = await db.MarketSnapshots.SingleAsync(s => s.SnapshotDate == T);
                AssertClose(10200.00m, snapshot.Xu100Level);
                AssertClose(0.02m, snapshot.Xu100ChangePct);
                AssertClose(30600.00m, snapshot.Xu30Level);
                AssertClose(0.02m, snapshot.Xu30ChangePct);
                Assert.Equal(T, snapshot.AsOfTradingDate);
            }

            // The served payload carries the corrected values, not the broken ones.
            await using (var factory = new DegerliAppFactory(_postgres.ConnectionString))
            {
                using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
                {
                    BaseAddress = new Uri("https://localhost"),
                    AllowAutoRedirect = false,
                });

                using var response = await client.GetAsync("/api/v1/market/overview");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var root = document.RootElement;
                Assert.Equal(T, DateOnly.Parse(root.GetProperty("asOf").GetString()!));
                var indices = root.GetProperty("indices");
                Assert.Equal(
                    10200.00m,
                    indices.GetProperty("xu100").GetProperty("level").GetDecimal());
                Assert.Equal(
                    0.02m,
                    indices.GetProperty("xu100").GetProperty("changePct").GetDecimal());
            }

            // A historical day's row is never touched by a later day's recompute.
            var priorAfter = await ReadSnapshotRowAsync(prior);
            Assert.Equal(priorBefore, priorAfter);
        }
        finally
        {
            await RestoreIndexLevelsAsync(removed);
        }
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

    /// <summary>Renames an index code in place (same row id, so its levels stay bound) to
    /// model the real-chain spelling of the BIST 30 index (<c>XU030</c>).</summary>
    private async Task RenameIndexAsync(string from, string to)
    {
        await using var db = _postgres.CreateContext();
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE indices SET code = {0} WHERE code = {1}", to, from);
    }

    /// <summary>
    /// Seeds a stored-price window on each of <paramref name="dates"/> for the covered
    /// universe by copying the anchor day's rows (TKT-foundation-028 weekend/cold-start
    /// scenario: real prices exist through a Friday and nothing after). Idempotent across
    /// the shared class fixture.
    /// </summary>
    private async Task SeedStoredPriceWindowAsync(params DateOnly[] dates)
    {
        await using var db = _postgres.CreateContext();
        var anchorPrices = await db.DailyPrices.Where(p => p.PriceDate == T).ToListAsync();
        var existing = (await db.DailyPrices
                .Where(p => dates.Contains(p.PriceDate))
                .Select(p => new { p.InstrumentId, p.PriceDate })
                .ToListAsync())
            .Select(p => (p.InstrumentId, p.PriceDate))
            .ToHashSet();

        foreach (var date in dates)
        {
            foreach (var price in anchorPrices)
            {
                if (!existing.Add((price.InstrumentId, date)))
                {
                    continue;
                }

                db.DailyPrices.Add(new DailyPrice
                {
                    InstrumentId = price.InstrumentId,
                    PriceDate = date,
                    Open = price.Open,
                    High = price.High,
                    Low = price.Low,
                    CloseRaw = price.CloseRaw,
                    CloseAdjusted = price.CloseAdjusted,
                    Volume = price.Volume,
                    SourceRef = "test:tkt-foundation-028-window",
                    RecordedAt = price.RecordedAt,
                });
            }
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Removes the balance-sheet <c>SHARES_DILUTED</c> line item from the given instruments'
    /// statements (models the real dev-stack shape, where no share-count facts exist at all:
    /// the parameterless call drops every constituent's share count). The rest of the FU
    /// fact world — prices, previous closes, index levels, income, dividends — is untouched.
    /// </summary>
    private async Task DeleteSharesAsync(params string[] symbols)
    {
        await using var db = _postgres.CreateContext();
        if (symbols.Length == 0)
        {
            await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM fin_line_items WHERE item_code = 'SHARES_DILUTED'");
            return;
        }

        var ids = await db.Instruments
            .Where(i => symbols.Contains(i.Symbol))
            .Select(i => i.Id)
            .ToListAsync();

        foreach (var id in ids)
        {
            await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM fin_line_items WHERE item_code = 'SHARES_DILUTED' AND statement_id IN "
                + "(SELECT id FROM financial_statements WHERE instrument_id = {0})",
                id);
        }
    }

    /// <summary>
    /// Removes the stored index levels for one day (models the real defect world: the
    /// session's prices exist but its index close has not landed yet) and returns the
    /// removed rows so the test can restore them.
    /// </summary>
    private async Task<List<IndexLevel>> RemoveIndexLevelsAsync(DateOnly date)
    {
        await using var db = _postgres.CreateContext();
        var rows = await db.IndexLevels.Where(l => l.LevelDate == date).ToListAsync();
        db.IndexLevels.RemoveRange(rows);
        await db.SaveChangesAsync();
        return rows;
    }

    /// <summary>Re-inserts previously removed index levels (idempotent: existing rows are skipped).</summary>
    private async Task RestoreIndexLevelsAsync(IReadOnlyList<IndexLevel> rows)
    {
        await using var db = _postgres.CreateContext();
        var existing = (await db.IndexLevels
                .Where(l => rows.Select(r => r.LevelDate).Contains(l.LevelDate))
                .Select(l => new { l.IndexId, l.LevelDate })
                .ToListAsync())
            .Select(l => (l.IndexId, l.LevelDate))
            .ToHashSet();

        foreach (var row in rows)
        {
            if (existing.Contains((row.IndexId, row.LevelDate)))
            {
                continue;
            }

            db.IndexLevels.Add(new IndexLevel
            {
                IndexId = row.IndexId,
                LevelDate = row.LevelDate,
                Close = row.Close,
                SourceRef = row.SourceRef,
                RecordedAt = row.RecordedAt,
            });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>Captures a stored snapshot row's full contents for byte-level comparison.</summary>
    private async Task<SnapshotRow> ReadSnapshotRowAsync(DateOnly date)
    {
        await using var db = _postgres.CreateContext();
        var row = await db.MarketSnapshots.SingleAsync(s => s.SnapshotDate == date);
        return new SnapshotRow(
            row.SnapshotDate,
            row.Xu100Level,
            row.Xu100ChangePct,
            row.Xu30Level,
            row.Xu30ChangePct,
            row.BreadthAdvancing,
            row.BreadthDeclining,
            row.BreadthUnchanged,
            row.VolumeTotal,
            row.MarketPe,
            row.MarketPeExcludedCount,
            row.MarketDivYield,
            row.GainersJson,
            row.LosersJson,
            row.SectorPerfJson,
            row.AsOfTradingDate,
            row.ComputedAt);
    }

    /// <summary>
    /// Reads one stat off the latest <c>market-snapshot</c> run-ledger row (the stats the
    /// runner writes to <c>ingest_runs.stats_json</c>); an absent stat reads 0.
    /// </summary>
    private async Task<int> ReadLatestLedgerStatAsync(string name)
    {
        await using var db = _postgres.CreateContext();
        var statsJson = await db.IngestRuns
            .Where(r => r.JobCode == MarketSnapshotJob.Code)
            .OrderByDescending(r => r.Id)
            .Select(r => r.StatsJson)
            .FirstAsync();

        using var document = JsonDocument.Parse(statsJson!);
        return document.RootElement.TryGetProperty(name, out var value)
            ? value.GetInt32()
            : 0;
    }

    private sealed record SnapshotRow(
        DateOnly SnapshotDate,
        decimal? Xu100Level,
        decimal? Xu100ChangePct,
        decimal? Xu30Level,
        decimal? Xu30ChangePct,
        int? BreadthAdvancing,
        int? BreadthDeclining,
        int? BreadthUnchanged,
        decimal? VolumeTotal,
        decimal? MarketPe,
        int? MarketPeExcludedCount,
        decimal? MarketDivYield,
        string? GainersJson,
        string? LosersJson,
        string? SectorPerfJson,
        DateOnly AsOfTradingDate,
        DateTimeOffset ComputedAt);

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
