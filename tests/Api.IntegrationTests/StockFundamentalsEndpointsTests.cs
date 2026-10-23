using System.Net;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Dividends;
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
/// TKT-res-004 acceptance — the Financials, Profitability, Growth, Balance-sheet and
/// Dividends section endpoints (Group B of <c>.pipeline/testing/stock-research.md</c>,
/// TC-RES-009..015) plus the MDF-plan cross-domain case TC-MDF-036:
/// <list type="bullet">
/// <item><c>GET /api/v1/stocks/{symbol}/financials</c> — per-period revenue → EBITDA →
/// EBIT → NI → FCF with per-figure <c>restated</c> flags (FR-RES-008/014,
/// UC-RES-003).</item>
/// <item><c>GET /api/v1/stocks/{symbol}/profitability</c> — canonical ROIC/ROE/margins
/// read from <c>derived_metrics</c> (FR-RES-009, BR-RES-004).</item>
/// <item><c>GET /api/v1/stocks/{symbol}/growth</c> — 3/5/10Y CAGRs with the actual
/// <c>windowYears</c> and the honest no-data state for a short history (FR-RES-010,
/// FR-MDF-014, UXR-RES-016).</item>
/// <item><c>GET /api/v1/stocks/{symbol}/balance-sheet</c> — BS lines + book value +
/// BVPS (FR-RES-011/027).</item>
/// <item><c>GET /api/v1/stocks/{symbol}/dividends</c> — dated dividend history or the
/// explicit never-paid state (FR-RES-012).</item>
/// </list>
/// Every test runs against the real, migrated Testcontainers PostgreSQL seeded with the
/// L2 fixture universe, then computes the canonical metrics at T through the registered
/// job. Goldens are hand-derived in FU §5/§6/§11.1 — never re-derived from the
/// implementation.
/// </summary>
public sealed class StockFundamentalsEndpointsTests : IClassFixture<PostgresFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    private readonly PostgresFixture _postgres;

    public StockFundamentalsEndpointsTests(PostgresFixture postgres) => _postgres = postgres;

    // -- TC-RES-009 — Financials with restated flags ---------------------------

    [Fact]
    public async Task TC_RES_009_financials_alfa_serves_per_period_figures_and_quarters_sum_to_fy()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/stocks/ALFA/financials");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.True(response.Headers.CacheControl?.Public);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        // The honest-data envelope rides every section response (`03` §1.2, M-1).
        Assert.Equal(T.ToString("yyyy-MM-dd"), root.GetProperty("asOf").GetString());
        Assert.False(root.GetProperty("stale").GetBoolean());

        var periods = root.GetProperty("periods").EnumerateArray().ToList();

        // FY2025 golden row (FU §5): revenue → EBITDA → EBIT → NI → FCF.
        var fy2025 = periods.Single(p =>
            p.GetProperty("periodType").GetString() == "FY"
            && p.GetProperty("fiscalYear").GetInt32() == 2025);
        Assert.Equal(new DateOnly(2025, 12, 31).ToString("yyyy-MM-dd"), fy2025.GetProperty("periodEndDate").GetString());
        AssertFigure(fy2025, "revenue", 1000m, restated: false);
        AssertFigure(fy2025, "ebitda", 400m, restated: false);
        AssertFigure(fy2025, "ebit", 300m, restated: false);
        AssertFigure(fy2025, "netIncome", 200m, restated: false);
        AssertFigure(fy2025, "fcf", 100m, restated: false);

        // The four FY2025 quarterly rows sum exactly to the FY figures (FU §5.1 —
        // ΣQ TTM is a built-in consistency check).
        var quarters = periods
            .Where(p => p.GetProperty("periodType").GetString() == "Q"
                && p.GetProperty("fiscalYear").GetInt32() == 2025)
            .ToList();
        Assert.Equal(4, quarters.Count);
        Assert.Equal(1000m, SumFigure(quarters, "revenue"));
        Assert.Equal(400m, SumFigure(quarters, "ebitda"));
        Assert.Equal(300m, SumFigure(quarters, "ebit"));
        Assert.Equal(200m, SumFigure(quarters, "netIncome"));
        Assert.Equal(100m, SumFigure(quarters, "fcf"));
    }

    [Fact]
    public async Task TC_RES_009_financials_rest_serves_latest_restated_version_and_flags_it()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/stocks/REST/financials");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        var fy2025 = root.GetProperty("periods").EnumerateArray().Single(p =>
            p.GetProperty("periodType").GetString() == "FY"
            && p.GetProperty("fiscalYear").GetInt32() == 2025);

        // The latest restated version serves (NI 75); the as-reported 60 is retained in
        // storage but never served here (FU §5 footnote, BR-MDF-011).
        AssertFigure(fy2025, "netIncome", 75m, restated: true);
    }

    // -- TC-RES-010 — Profitability section ------------------------------------

    [Fact]
    public async Task TC_RES_010_profitability_alfa_serves_canonical_ratios()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/stocks/ALFA/profitability");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.True(response.Headers.CacheControl?.Public);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(T.ToString("yyyy-MM-dd"), root.GetProperty("asOf").GetString());
        Assert.False(root.GetProperty("stale").GetBoolean());

        // FU §11.1 — the canonical values the screener and DCF also assert (BR-RES-004).
        var metrics = root.GetProperty("metrics");
        AssertMetric(metrics, "roic", 0.20m);
        AssertMetric(metrics, "roe", 0.20m);
        AssertMetric(metrics, "gross_margin", 0.40m);
        AssertMetric(metrics, "operating_margin", 0.30m);
    }

    [Fact]
    public async Task TC_RES_010_profitability_delta_roe_is_not_meaningful_never_zero()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/stocks/DELTA/profitability");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        // DELTA has negative equity (FU §11.2): ROE is not meaningful — NULL, never 0.
        var roe = root.GetProperty("metrics").GetProperty("roe");
        Assert.True(roe.GetProperty("notMeaningful").GetBoolean());
        Assert.Equal(JsonValueKind.Null, roe.GetProperty("value").ValueKind);
    }

    // -- TC-RES-011 — Growth section with windows ------------------------------

    [Fact]
    public async Task TC_RES_011_growth_alfa_serves_3_5_10y_cagrs_with_actual_windows()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/stocks/ALFA/growth");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.True(response.Headers.CacheControl?.Public);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(T.ToString("yyyy-MM-dd"), root.GetProperty("asOf").GetString());
        Assert.False(root.GetProperty("stale").GetBoolean());

        // FU §11.1 goldens; windowYears echoes the nominal window for a full history.
        AssertGrowth(root, "rev", 3, 0.160397m, windowYears: 3);
        AssertGrowth(root, "rev", 5, 0.201124m, windowYears: 5);
        AssertGrowth(root, "rev", 10, 0.174619m, windowYears: 10);
        AssertGrowth(root, "eps", 3, 0.035744m, windowYears: 3);
        AssertGrowth(root, "eps", 5, 0.059224m, windowYears: 5);
        AssertGrowth(root, "eps", 10, 0.071773m, windowYears: 10);
        AssertGrowth(root, "fcf", 3, -0.206300m, windowYears: 3);
        AssertGrowth(root, "fcf", 5, -0.089708m, windowYears: 5);
        AssertGrowth(root, "fcf", 10, 0.022565m, windowYears: 10);
        AssertGrowth(root, "div", 3, 0.035744m, windowYears: 3);
        AssertGrowth(root, "div", 5, 0.045640m, windowYears: 5);
        AssertGrowth(root, "div", 10, 0.052409m, windowYears: 10);
    }

    [Fact]
    public async Task TC_RES_011_growth_zeta_indicates_actual_short_window()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/stocks/ZETA/growth");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        // ZETA has exactly 2 FYs (FU §5.2): every requested window falls back to the one
        // available interval and reports windowYears = 1 (FR-MDF-014).
        AssertGrowth(root, "rev", 3, 0.25m, windowYears: 1);
        AssertGrowth(root, "rev", 5, 0.25m, windowYears: 1);
        AssertGrowth(root, "rev", 10, 0.25m, windowYears: 1);
    }

    [Fact]
    public async Task TC_RES_011_growth_newp_serves_no_data_state_with_listing_boundary()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/stocks/NEWP/growth");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        // NEWP has 1 FY (FU §5.2): every CAGR is NULL → explicit no-data state naming the
        // listing-date coverage boundary (FR-RES-016, UXR-RES-022) — never fabricated.
        Assert.Equal("no-data", root.GetProperty("state").GetString());
        Assert.Equal(
            new DateOnly(2025, 8, 1).ToString("yyyy-MM-dd"),
            root.GetProperty("coverage").GetProperty("availableFrom").GetString());
    }

    // -- TC-RES-012 — Balance sheet incl. book value and BVPS ------------------

    [Fact]
    public async Task TC_RES_012_balance_sheet_alfa_serves_lines_book_value_and_bvps()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/stocks/ALFA/balance-sheet");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.True(response.Headers.CacheControl?.Public);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(T.ToString("yyyy-MM-dd"), root.GetProperty("asOf").GetString());
        Assert.False(root.GetProperty("stale").GetBoolean());
        Assert.Equal("FY", root.GetProperty("periodType").GetString());
        Assert.Equal(2025, root.GetProperty("fiscalYear").GetInt32());

        // FU §5 BS line items.
        var lines = root.GetProperty("lines");
        AssertFigure(lines, "cash", 200m, restated: false);
        AssertFigure(lines, "shortTermDebt", 100m, restated: false);
        AssertFigure(lines, "longTermDebt", 300m, restated: false);
        AssertFigure(lines, "totalEquity", 1000m, restated: false);
        AssertFigure(lines, "currentAssets", 600m, restated: false);
        AssertFigure(lines, "currentLiabilities", 300m, restated: false);
        AssertFigure(lines, "sharesDiluted", 100m, restated: false);
        AssertFigure(lines, "sharesOutstanding", 100m, restated: false);

        // Book value = total equity; BVPS = equity / shares = 1000 / 100 (FR-RES-027).
        AssertFigure(root, "bookValue", 1000m, restated: false);
        AssertFigure(root, "bookValuePerShare", 10.0m, restated: false);
    }

    // -- TC-MDF-093 — the market-data share count serves (TKT-foundation-058) ---

    [Fact]
    public async Task TC_MDF_093_balance_sheet_serves_market_share_count_and_honest_null_when_absent()
    {
        await SeedAsync();

        // The share_counts store is populated by the `shares` lane; the serving seam is
        // verified here against a directly seeded row (the ingest path is TC-MDF-092).
        var recordedAt = new DateTimeOffset(T.ToDateTime(new TimeOnly(6, 0)), TimeSpan.Zero);
        await using (var db = _postgres.CreateContext())
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM share_counts");
            var alfa = await db.Instruments.SingleAsync(instrument => instrument.Symbol == "ALFA");
            db.ShareCounts.Add(new ShareCount
            {
                InstrumentId = alfa.Id,
                SharesOutstanding = 4_560_000_000m,
                RecordedAt = recordedAt,
                SourceRef = "kap",
            });
            await db.SaveChangesAsync();
        }

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        // (1) A stored count serves as sharesOutstanding + asOf.
        using (var response = await client.GetAsync("/api/v1/stocks/ALFA/balance-sheet"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.Public);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = document.RootElement;

            // The honest-data envelope is unchanged by the additive block (`03` §1.2, M-1).
            Assert.Equal(T.ToString("yyyy-MM-dd"), root.GetProperty("asOf").GetString());
            Assert.False(root.GetProperty("stale").GetBoolean());

            var shareCount = root.GetProperty("shareCount");
            Assert.Equal(4_560_000_000m, shareCount.GetProperty("sharesOutstanding").GetDecimal());
            Assert.Equal(T.ToString("yyyy-MM-dd"), shareCount.GetProperty("asOf").GetString());

            // The statement-reported figures are untouched (golden stability): the market
            // count is a distinct field, not a replacement for the BS line.
            AssertFigure(root.GetProperty("lines"), "sharesOutstanding", 100m, restated: false);
            AssertFigure(root.GetProperty("lines"), "sharesDiluted", 100m, restated: false);
            Assert.Equal(10.0m, root.GetProperty("bookValuePerShare").GetProperty("value").GetDecimal());
        }

        // (2) An instrument with no stored count is honestly absent: nulls, never zeros —
        // while the balance-sheet section itself still serves.
        using (var response = await client.GetAsync("/api/v1/stocks/LAMDA/balance-sheet"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.Public);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = document.RootElement;

            Assert.Equal(T.ToString("yyyy-MM-dd"), root.GetProperty("asOf").GetString());

            var shareCount = root.GetProperty("shareCount");
            Assert.Equal(JsonValueKind.Null, shareCount.GetProperty("sharesOutstanding").ValueKind);
            Assert.Equal(JsonValueKind.Null, shareCount.GetProperty("asOf").ValueKind);
        }
    }

    // -- TC-RES-013 — Dividends section; never-paid state ----------------------

    [Fact]
    public async Task TC_RES_013_dividends_alfa_serves_dated_history()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/stocks/ALFA/dividends");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.True(response.Headers.CacheControl?.Public);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(T.ToString("yyyy-MM-dd"), root.GetProperty("asOf").GetString());
        Assert.False(root.GetProperty("stale").GetBoolean());

        var rows = root.GetProperty("dividends").EnumerateArray().ToList();
        // The four TTM quarterly rows plus the FY DPS series (FU §6 / §5.1).
        var ttm = rows.Where(r => r.GetProperty("exDate").GetString()!.StartsWith("2026-", StringComparison.Ordinal)).ToList();
        Assert.Equal(4, ttm.Count);
        Assert.All(ttm, r => Assert.Equal(0.25m, r.GetProperty("amountPerShare").GetDecimal()));

        // TTM DPS per share = Σ of the four 0.25 rows = 1.00 (FU §6).
        Assert.Equal(1.00m, root.GetProperty("ttmPerShare").GetDecimal());

        // Dated rows carry the ex-date (and pay date where present).
        var first = rows[0];
        Assert.Equal(new DateOnly(2015, 7, 15).ToString("yyyy-MM-dd"), first.GetProperty("exDate").GetString());
        Assert.Equal(0.60m, first.GetProperty("amountPerShare").GetDecimal());
    }

    [Fact]
    public async Task TC_RES_013_dividends_lamda_serves_never_paid_state()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/stocks/LAMDA/dividends");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        // LAMDA never paid a dividend (FU §6): an explicit 200-with-state, not an error,
        // not blanks-as-zero (SCR-005 §7, BR-RES-008).
        Assert.Equal("no-data", root.GetProperty("state").GetString());
        Assert.False(root.TryGetProperty("dividends", out _));
        Assert.Equal(
            new DateOnly(2016, 4, 12).ToString("yyyy-MM-dd"),
            root.GetProperty("coverage").GetProperty("availableFrom").GetString());
    }

    // -- TC-RES-037 — Dividends availability signal (TKT-foundation-052) --------
    //
    // A zero-row dividends table is not evidence that the company never paid a
    // dividend: it is equally the shape of "the dividends lane has not covered this
    // instrument yet". The payload carries the server-side availability signal so the
    // client can tell the three honest states apart (not-processed / processed-empty /
    // populated) instead of reading "never paid" out of an empty list.

    [Fact]
    public async Task TC_RES_037_dividends_zero_rows_with_no_run_is_not_available()
    {
        await SeedAsync();
        await SeedDividendsRunsAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/stocks/LAMDA/dividends");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        // No rows AND no completed dividends run: the data is simply not there yet —
        // the neutral state, never a "never paid" claim.
        Assert.False(root.GetProperty("dividendsAvailable").GetBoolean());
        Assert.Equal("no-data", root.GetProperty("state").GetString());
        Assert.False(root.TryGetProperty("dividends", out _));
        Assert.False(root.TryGetProperty("ttmPerShare", out _));
    }

    [Fact]
    public async Task TC_RES_037_dividends_zero_rows_after_a_succeeded_run_is_available()
    {
        await SeedAsync();
        // A completed dividends pass walked LAMDA's disclosures and found no dividend
        // notice: the empty table is now backed evidence.
        await SeedDividendsRunsAsync((IngestResult.Succeeded, StatsWithSymbols()));
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/stocks/LAMDA/dividends");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.True(root.GetProperty("dividendsAvailable").GetBoolean());
        Assert.Equal("no-data", root.GetProperty("state").GetString());
        Assert.False(root.TryGetProperty("dividends", out _));
        Assert.False(root.TryGetProperty("ttmPerShare", out _));
    }

    [Fact]
    public async Task TC_RES_037_dividends_zero_rows_after_a_partial_run_that_isolated_the_instrument_is_not_available()
    {
        await SeedAsync();
        // A partial pass (046's per-symbol isolation) never walked LAMDA — the symbol is
        // named failed in the run's stats, so "no dividends" is still unbacked for it.
        await SeedDividendsRunsAsync((IngestResult.Partial, StatsWithSymbols("LAMDA", failed: true)));
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/stocks/LAMDA/dividends");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.False(root.GetProperty("dividendsAvailable").GetBoolean());
        Assert.Equal("no-data", root.GetProperty("state").GetString());
    }

    [Fact]
    public async Task TC_RES_037_dividends_populated_payload_is_available()
    {
        await SeedAsync();
        await SeedDividendsRunsAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/stocks/ALFA/dividends");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        // Rows in hand: the data is available by definition, and the history still serves.
        Assert.True(root.GetProperty("dividendsAvailable").GetBoolean());
        Assert.True(root.GetProperty("dividends").GetArrayLength() > 0);
        Assert.Equal(1.00m, root.GetProperty("ttmPerShare").GetDecimal());
    }

    // -- TC-RES-014 — Unknown symbol across all section endpoints --------------

    [Fact]
    public async Task TC_RES_014_unknown_symbol_is_not_found_on_every_section_endpoint()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        string[] paths =
        [
            "/api/v1/stocks/NOPE/overview",
            "/api/v1/stocks/NOPE/valuation",
            "/api/v1/stocks/NOPE/financials",
            "/api/v1/stocks/NOPE/profitability",
            "/api/v1/stocks/NOPE/growth",
            "/api/v1/stocks/NOPE/balance-sheet",
            "/api/v1/stocks/NOPE/dividends",
            "/api/v1/stocks/NOPE/dcf",
        ];

        foreach (var path in paths)
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("NOT_FOUND", document.RootElement.GetProperty("code").GetString());
        }
    }

    // -- TC-RES-015 — Honest-data envelope across sections ---------------------

    [Fact]
    public async Task TC_RES_015_every_section_carries_as_of_and_never_blanks_as_zero()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        // Every data-bearing section carries asOf (+ stale) — M-1 / `03` §1.2.
        foreach (var section in new[] { "financials", "profitability", "growth", "balance-sheet", "dividends" })
        {
            using var response = await client.GetAsync($"/api/v1/stocks/ALFA/{section}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = document.RootElement;
            Assert.Equal(T.ToString("yyyy-MM-dd"), root.GetProperty("asOf").GetString());
            Assert.False(root.GetProperty("stale").GetBoolean());
        }

        // PART has no CF statement (FU §5): its FCF is absent with an explicit state —
        // never a blank served as zero (FR-RES-015/016, BR-RES-008).
        using (var response = await client.GetAsync("/api/v1/stocks/PART/financials"))
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var fy2025 = document.RootElement.GetProperty("periods").EnumerateArray().Single(p =>
                p.GetProperty("periodType").GetString() == "FY"
                && p.GetProperty("fiscalYear").GetInt32() == 2025);
            var fcf = fy2025.GetProperty("fcf");
            Assert.Equal(JsonValueKind.Null, fcf.GetProperty("value").ValueKind);
            Assert.Equal("no-data", fcf.GetProperty("state").GetString());
        }
    }

    // -- TC-MDF-036 — Downstream serving: canonical values or honest no-data ----

    [Fact]
    public async Task TC_MDF_036_downstream_serves_canonical_values_or_honest_no_data()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        // ALFA metrics present → canonical values with an asOf (FU §11.1).
        using (var response = await client.GetAsync("/api/v1/stocks/ALFA/profitability"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = document.RootElement;
            Assert.NotEqual(JsonValueKind.Null, root.GetProperty("asOf").ValueKind);
            AssertMetric(root.GetProperty("metrics"), "roe", 0.20m);
        }

        // NEWP growth absent → NULL + explicit no-data with the coverage boundary;
        // never fabricated, never blank-as-zero (`03` §1.2).
        using (var response = await client.GetAsync("/api/v1/stocks/NEWP/growth"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = document.RootElement;
            Assert.Equal("no-data", root.GetProperty("state").GetString());
            Assert.False(root.TryGetProperty("metrics", out _));
            Assert.True(root.TryGetProperty("coverage", out _));
        }
    }

    // ---------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Seeds the L2 fixture universe, then computes the canonical metrics at T through
    /// the registered job — the same canonical rows every surface serves (BR-RES-004).
    /// </summary>
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
        var metrics = await runner.RunAsync(MetricsRecomputeJob.Code, new IngestionRequest(T));
        Assert.Equal("succeeded", metrics.Status);
    }

    /// <summary>
    /// Replaces the run ledger's <c>dividends</c> rows with the supplied terminal runs
    /// (TKT-foundation-052). The ledger is shared across the class fixture, so the delete
    /// makes each availability case order-independent (the <c>derived_metrics</c> reset in
    /// <see cref="SeedAsync"/> is the same pattern). No args = the lane never ran.
    /// </summary>
    private async Task SeedDividendsRunsAsync(params (string Status, string? StatsJson)[] runs)
    {
        await using var db = _postgres.CreateContext();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs WHERE job_code = 'dividends'");
        foreach (var (status, statsJson) in runs)
        {
            db.IngestRuns.Add(new IngestRun
            {
                JobCode = DividendsJob.Code,
                StartedAt = new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                FinishedAt = new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddMinutes(5),
                Status = status,
                StatsJson = statsJson,
            });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>The run-ledger <c>stats_json</c> shape the runner writes (Jobs.cs).</summary>
    private static string StatsWithSymbols(string? symbol = null, bool failed = false) =>
        JsonSerializer.Serialize(new
        {
            written = 0,
            unchanged = 0,
            quarantined = 0,
            skipped = 0,
            symbols = symbol is null
                ? Array.Empty<object>()
                : [new { symbol, written = 0, unchanged = 0, quarantined = 0, failed }],
            mode = "incremental",
        });

    private static HttpClient CreateClient(DegerliAppFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    private static void AssertFigure(JsonElement parent, string name, decimal expected, bool restated)
    {
        var figure = parent.GetProperty(name);
        Assert.Equal(expected, figure.GetProperty("value").GetDecimal());
        Assert.Equal(restated, figure.GetProperty("restated").GetBoolean());
    }

    private static decimal SumFigure(IEnumerable<JsonElement> periods, string name) =>
        periods.Sum(p => p.GetProperty(name).GetProperty("value").GetDecimal());

    private static void AssertMetric(JsonElement metrics, string code, decimal expected)
    {
        var entry = metrics.GetProperty(code);
        Assert.False(entry.GetProperty("notMeaningful").GetBoolean());
        var value = entry.GetProperty("value").GetDecimal();
        Assert.True(
            Math.Abs(value - expected) <= 0.0001m,
            $"{code}: expected {expected} (±1e-4) but was {value}");
    }

    private static void AssertGrowth(JsonElement root, string family, int window, decimal expected, int windowYears)
    {
        var entry = root.GetProperty("metrics").GetProperty(family)
            .EnumerateArray()
            .Single(e => e.GetProperty("window").GetInt32() == window);
        Assert.Equal(windowYears, entry.GetProperty("windowYears").GetInt32());
        var value = entry.GetProperty("value").GetDecimal();
        Assert.True(
            Math.Abs(value - expected) <= 0.0001m,
            $"{family} {window}Y: expected {expected} (±1e-4) but was {value}");
    }
}
