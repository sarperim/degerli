using System.Net;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;
using Degerli.Ingestion.Statements;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-foundation-094 acceptance — the statements-view serving surface
/// (<c>.pipeline/testing/market-data-foundation.md</c> §34, TC-MDF-123): the extended
/// <c>GET /api/v1/stocks/{symbol}/financials</c> payload.
/// <para>
/// <b>L2 doubles only, zero live requests.</b> The five committed KAP public-site captures are the
/// entire input: each case runs the production decoder over a committed body, writes it through
/// the production <see cref="FinancialStatementStore"/>, and reads the production endpoint against
/// the real migrated Testcontainers PostgreSQL. Negative cases are in-memory mutations of a
/// committed body or of the seeded world (a hand-built pre-migration-shaped fact, an accounting
/// profile flip, a stored line added), never a live read.
/// </para>
/// <para>
/// The [C] row assertions follow the TC-MDF-118 (a) precedent: each served derived value is
/// cross-checked against <b>the served lines' own arithmetic</b> (the payload's <c>lines</c> map),
/// with the capture's raw figures pinned as the goldens where they matter (gross debt, NWC, TBV,
/// net debt). Goldens are the committed bodies' values, never re-derived from the implementation.
/// </para>
/// </summary>
public sealed class FinancialStatementsViewTests : IClassFixture<PostgresFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    private const string Tuprs = "TUPRS";
    private const string Mgros = "MGROS";
    private const string Asels = "ASELS";
    private const string Thyao = "THYAO";

    /// <summary>The test world's only provenance for the committed captures.</summary>
    private const string KapPublic = "kap-public";

    /// <summary>The capture's own filing stamp used for the seeded rows (its submit date).</summary>
    private static readonly DateTimeOffset RecordedAt =
        new(2026, 2, 6, 18, 34, 29, TimeSpan.Zero);

    private readonly PostgresFixture _postgres;

    public FinancialStatementsViewTests(PostgresFixture postgres) => _postgres = postgres;

    // -- (a) the full line set + the [C] rows, TUPRS's committed FY2025 filing ----------------

    [Fact]
    public async Task TC_MDF_123_a_financials_serves_the_full_line_set_and_the_derived_rows_from_the_stored_lines()
    {
        await SeedUniverseAsync();
        await SeedCapturedCompanyAsync(Tuprs, RealSourceCatalog.KapPublicReportTuprs);
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync($"/api/v1/stocks/{Tuprs}/financials");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        var fy2025 = Period(root, "FY", "2025-12-31");
        var lines = fy2025.GetProperty("lines");

        // The capture's own stored figures (thousands TL × the store's scale = single TL). The
        // capture declares `1.000 TL`, so every value is the attribute × 1,000.
        Assert.Equal(830_356_131_000m, lines.GetProperty("IS").GetProperty("REV").GetProperty("value").GetDecimal());
        Assert.Equal(49_464_734_000m, lines.GetProperty("CF").GetProperty("OCF").GetProperty("value").GetDecimal());
        Assert.Equal(-19_118_339_000m, lines.GetProperty("CF").GetProperty("CAPEX").GetProperty("value").GetDecimal());
        Assert.Equal(8_957_100_000m, lines.GetProperty("BS").GetProperty("ST_DEBT").GetProperty("value").GetDecimal());
        Assert.Equal(28_428_775_000m, lines.GetProperty("BS").GetProperty("LT_DEBT").GetProperty("value").GetDecimal());
        Assert.Equal(107_237_211_000m, lines.GetProperty("BS").GetProperty("CASH").GetProperty("value").GetDecimal());
        Assert.Equal(236_931_136_000m, lines.GetProperty("BS").GetProperty("CUR_ASSETS").GetProperty("value").GetDecimal());
        Assert.Equal(169_748_201_000m, lines.GetProperty("BS").GetProperty("CUR_LIAB").GetProperty("value").GetDecimal());
        Assert.Equal(369_827_994_000m, lines.GetProperty("BS").GetProperty("TOTAL_EQUITY").GetProperty("value").GetDecimal());
        Assert.Equal(4_064_000m, lines.GetProperty("BS").GetProperty("GOODWILL").GetProperty("value").GetDecimal());
        Assert.Equal(10_129_896_000m, lines.GetProperty("BS").GetProperty("INTANGIBLES_NET").GetProperty("value").GetDecimal());
        Assert.Equal(5_728_736_000m, lines.GetProperty("BS").GetProperty("MINORITY_INT").GetProperty("value").GetDecimal());

        // Every served line carries its statement's restated flag (as-reported here).
        Assert.False(lines.GetProperty("BS").GetProperty("ST_DEBT").GetProperty("restated").GetBoolean());

        // The compat five figures are unchanged beside the new blocks.
        Assert.Equal(830_356_131_000m, fy2025.GetProperty("revenue").GetProperty("value").GetDecimal());
        Assert.Equal(41_646_897_000m, fy2025.GetProperty("ebit").GetProperty("value").GetDecimal());
        Assert.Equal(29_872_672_000m, fy2025.GetProperty("netIncome").GetProperty("value").GetDecimal());

        // The OTHER_* passthrough stays reachable beside the canonical map (never dropped): the
        // sheet's own ARA TOPLAM line is the deepest passthrough this body carries.
        var otherBs = fy2025.GetProperty("otherLines").GetProperty("BS");
        Assert.Equal(
            236_931_136_000m,
            otherBs.GetProperty("OTHER_BS_ARA_TOPLAM").GetProperty("value").GetDecimal());
        var otherCodes = otherBs.EnumerateObject().Select(property => property.Name).ToList();
        Assert.NotEmpty(otherCodes);
        Assert.All(otherCodes, code => Assert.StartsWith("OTHER_", code, StringComparison.Ordinal));
        Assert.DoesNotContain(
            lines.GetProperty("BS").EnumerateObject().ToList(),
            property => property.Name.StartsWith("OTHER_", StringComparison.Ordinal));

        // ── The [C] rows, cross-checked against the served lines' own arithmetic ──
        var derived = fy2025.GetProperty("derived");
        var stDebt = lines.GetProperty("BS").GetProperty("ST_DEBT").GetProperty("value").GetDecimal();
        var ltDebt = lines.GetProperty("BS").GetProperty("LT_DEBT").GetProperty("value").GetDecimal();
        var cash = lines.GetProperty("BS").GetProperty("CASH").GetProperty("value").GetDecimal();
        var curAssets = lines.GetProperty("BS").GetProperty("CUR_ASSETS").GetProperty("value").GetDecimal();
        var curLiab = lines.GetProperty("BS").GetProperty("CUR_LIAB").GetProperty("value").GetDecimal();
        var equity = lines.GetProperty("BS").GetProperty("TOTAL_EQUITY").GetProperty("value").GetDecimal();
        var goodwill = lines.GetProperty("BS").GetProperty("GOODWILL").GetProperty("value").GetDecimal();
        var intangibles = lines.GetProperty("BS").GetProperty("INTANGIBLES_NET").GetProperty("value").GetDecimal();
        var revenue = lines.GetProperty("IS").GetProperty("REV").GetProperty("value").GetDecimal();
        var grossProfit = lines.GetProperty("IS").GetProperty("GROSS_PROFIT").GetProperty("value").GetDecimal();
        var ebit = lines.GetProperty("IS").GetProperty("EBIT").GetProperty("value").GetDecimal();
        var pretax = lines.GetProperty("IS").GetProperty("PRETAX_INC").GetProperty("value").GetDecimal();
        var tax = lines.GetProperty("IS").GetProperty("TAX_EXP").GetProperty("value").GetDecimal();
        var niParent = lines.GetProperty("IS").GetProperty("NI_PARENT").GetProperty("value").GetDecimal();
        var ocf = lines.GetProperty("CF").GetProperty("OCF").GetProperty("value").GetDecimal();
        var capex = lines.GetProperty("CF").GetProperty("CAPEX").GetProperty("value").GetDecimal();

        // The pinned goldens: the fenced DR-2 sum, net debt, NWC and TBV — from the capture.
        Assert.Equal(37_385_875_000m, derived.GetProperty("grossDebt").GetProperty("value").GetDecimal());
        Assert.Equal(37_385_875_000m, stDebt + ltDebt);
        Assert.Equal(-69_851_336_000m, derived.GetProperty("netDebt").GetProperty("value").GetDecimal());
        Assert.Equal(67_182_935_000m, derived.GetProperty("netWorkingCapital").GetProperty("value").GetDecimal());
        Assert.Equal(359_694_034_000m, derived.GetProperty("tangibleBookValue").GetProperty("value").GetDecimal());

        // …and every ratio is exactly the stored lines' own quotient.
        Assert.Equal(grossProfit / revenue, derived.GetProperty("grossMargin").GetProperty("value").GetDecimal());
        Assert.Equal(ebit / revenue, derived.GetProperty("operatingMargin").GetProperty("value").GetDecimal());
        Assert.Equal(tax / pretax, derived.GetProperty("effectiveTaxRate").GetProperty("value").GetDecimal());
        Assert.Equal(niParent / revenue, derived.GetProperty("netMarginParent").GetProperty("value").GetDecimal());
        Assert.Equal(curAssets - curLiab, derived.GetProperty("netWorkingCapital").GetProperty("value").GetDecimal());
        Assert.Equal(stDebt + ltDebt - cash, derived.GetProperty("netDebt").GetProperty("value").GetDecimal());

        // Y/Y growth against the prior same-type period served in this payload (FY2024).
        var fy2024Revenue = Period(root, "FY", "2024-12-31")
            .GetProperty("lines").GetProperty("IS").GetProperty("REV").GetProperty("value").GetDecimal();
        Assert.Equal(revenue / fy2024Revenue - 1m, derived.GetProperty("revenueGrowthYoy").GetProperty("value").GetDecimal());

        // The per-share rows use the pinned precedence: the statement declares no SHARES_DILUTED,
        // so the price lane's own count (2.5bn, seeded at T) is the only divisor.
        const decimal shares = 2_500_000_000m;
        Assert.Equal(equity / shares, derived.GetProperty("bvps").GetProperty("value").GetDecimal());
        Assert.Equal((equity - goodwill - intangibles) / shares, derived.GetProperty("tbvPerShare").GetProperty("value").GetDecimal());
        Assert.Equal((ocf - Math.Abs(capex)) / shares, derived.GetProperty("fcfPerShare").GetProperty("value").GetDecimal());

        // The EV header inputs are their own facts with their own as-of (display-only).
        var ev = root.GetProperty("evHeader");
        Assert.Equal(150m, ev.GetProperty("sharePrice").GetProperty("value").GetDecimal());
        Assert.Equal(T.ToString("yyyy-MM-dd"), ev.GetProperty("sharePrice").GetProperty("asOf").GetString());
        Assert.Equal(375_000_000_000m, ev.GetProperty("marketCap").GetProperty("value").GetDecimal());
        Assert.Equal(T.ToString("yyyy-MM-dd"), ev.GetProperty("marketCap").GetProperty("asOf").GetString());
        Assert.Equal(37_385_875_000m, ev.GetProperty("grossDebt").GetProperty("value").GetDecimal());
        Assert.Equal("2025-12-31", ev.GetProperty("grossDebt").GetProperty("asOf").GetString());
        Assert.Equal(107_237_211_000m, ev.GetProperty("cashAndShortTermInvestments").GetProperty("value").GetDecimal());
        Assert.Equal(5_728_736_000m, ev.GetProperty("minorityInterest").GetProperty("value").GetDecimal());
    }

    // -- (b) span + filing badges from stored evidence ---------------------------------------

    [Fact]
    public async Task TC_MDF_123_b_served_span_and_badges_come_from_the_stored_filing_evidence()
    {
        await SeedUniverseAsync();
        await SeedCapturedCompanyAsync(Tuprs, RealSourceCatalog.KapPublicReportTuprs);
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync($"/api/v1/stocks/{Tuprs}/financials");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        // The FY2025 statement's declared span (its own 01.01–31.12 range) is served verbatim.
        var span = Period(root, "FY", "2025-12-31").GetProperty("span");
        Assert.Equal("2025-01-01", span.GetProperty("periodStartDate").GetString());
        Assert.Equal(12, span.GetProperty("durationMonths").GetInt32());

        // The filing flag is stored per statement and served per period (`Konsolide` → true).
        Assert.True(Period(root, "FY", "2025-12-31").GetProperty("consolidated").GetBoolean());
        Assert.True(Period(root, "FY", "2024-12-31").GetProperty("consolidated").GetBoolean());

        // The filing envelope describes the latest served period's filing, every badge derived
        // from a served fact: the flag, the pinned TMS 29 transition (`02` §6.2), the source class.
        var filing = root.GetProperty("filing");
        Assert.Equal("2025-12-31", filing.GetProperty("periodEndDate").GetString());
        Assert.Equal(KapPublic, filing.GetProperty("sourceRef").GetString());
        Assert.True(filing.GetProperty("consolidated").GetBoolean());
        Assert.True(filing.GetProperty("tms29").GetBoolean());
        Assert.True(filing.GetProperty("kapSource").GetBoolean());

        // A period ending before the pinned transition is served with `tms29 = false` — the
        // derivation, not a stored claim (the legacy-shaped fact seeded in case (g)).
        await SeedLegacyInterimAsync();
        using var legacy = await client.GetAsync("/api/v1/stocks/LEGACY/financials");
        using var legacyDocument = JsonDocument.Parse(await legacy.Content.ReadAsStringAsync());
        Assert.False(legacyDocument.RootElement.GetProperty("filing").GetProperty("tms29").GetBoolean());
    }

    // -- (c) honest period labelling: the interim's declared cumulative span ------------------

    [Fact]
    public async Task TC_MDF_123_c_a_quarter_typed_interim_serves_its_declared_cumulative_span()
    {
        await SeedUniverseAsync();
        await SeedCapturedCompanyAsync(Mgros, RealSourceCatalog.KapPublicReportMgros);
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync($"/api/v1/stocks/{Mgros}/financials");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        // The 2026 interim is `Q`-typed, and it is honestly cumulative: the filing declared a
        // six-month range, so the payload serves that range (090's R-1 closed on the surface).
        var interim = Period(root, "Q", "2026-06-30");
        Assert.Equal("Q", interim.GetProperty("periodType").GetString());
        var span = interim.GetProperty("span");
        Assert.Equal("2026-01-01", span.GetProperty("periodStartDate").GetString());
        Assert.Equal(6, span.GetProperty("durationMonths").GetInt32());

        // The prior interim in the same filing carries its own declared six-month span.
        var prior = Period(root, "Q", "2025-06-30").GetProperty("span");
        Assert.Equal("2025-01-01", prior.GetProperty("periodStartDate").GetString());
        Assert.Equal(6, prior.GetProperty("durationMonths").GetInt32());

        // Y/Y growth is anchored to the same period type one year earlier, from served lines.
        var lines = interim.GetProperty("lines");
        var revenue = lines.GetProperty("IS").GetProperty("REV").GetProperty("value").GetDecimal();
        var priorRevenue = Period(root, "Q", "2025-06-30")
            .GetProperty("lines").GetProperty("IS").GetProperty("REV").GetProperty("value").GetDecimal();
        Assert.Equal(241_298_039_000m, revenue);
        Assert.Equal(revenue / priorRevenue - 1m,
            interim.GetProperty("derived").GetProperty("revenueGrowthYoy").GetProperty("value").GetDecimal());

        // The interim's own balance sheet drives the [C] rows (gross debt from the fenced pair).
        Assert.Equal(
            (4_974_529_000m + 34_053_178_000m),
            interim.GetProperty("derived").GetProperty("grossDebt").GetProperty("value").GetDecimal());

        // A balance-sheet-only period (the filing's comparative sheet) serves its rows, an empty
        // IS map, and no span — a balance sheet is an instant, never a range.
        var comparative = Period(root, "Q", "2025-12-31");
        Assert.Equal(JsonValueKind.Null, comparative.GetProperty("span").ValueKind);
        Assert.Empty(comparative.GetProperty("lines").GetProperty("IS").EnumerateObject().ToList());
        Assert.NotEmpty(comparative.GetProperty("lines").GetProperty("BS").EnumerateObject().ToList());
        Assert.Equal(JsonValueKind.Null,
            comparative.GetProperty("derived").GetProperty("revenueGrowthYoy").GetProperty("value").ValueKind);
    }

    // -- (d) honest-null on a missing operand (TBV, margins, growth) -------------------------

    [Fact]
    public async Task TC_MDF_123_d_a_missing_operand_makes_its_derived_row_null_never_a_guess()
    {
        await SeedUniverseAsync();
        await SeedCapturedCompanyAsync(Asels, RealSourceCatalog.KapPublicReportAselsAnnual);
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync($"/api/v1/stocks/{Asels}/financials");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        var fy2025 = Period(root, "FY", "2025-12-31");
        var derived = fy2025.GetProperty("derived");

        // This body's balance sheet carries no GOODWILL line: TBV and its per-share row are
        // honest-null with the explicit no-data state — not `equity − intangibles`.
        Assert.Equal(JsonValueKind.Null, derived.GetProperty("tangibleBookValue").GetProperty("value").ValueKind);
        Assert.Equal("no-data", derived.GetProperty("tangibleBookValue").GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, derived.GetProperty("tbvPerShare").GetProperty("value").ValueKind);

        // EBITDA is not reported and the statement carries no D&A line, so the margin is null;
        // the gross margin (its operands present) is served.
        Assert.Equal(JsonValueKind.Null, derived.GetProperty("ebitdaMargin").GetProperty("value").ValueKind);
        var lines = fy2025.GetProperty("lines");
        Assert.NotEqual(JsonValueKind.Null, derived.GetProperty("grossMargin").GetProperty("value").ValueKind);
        Assert.Equal(
            lines.GetProperty("IS").GetProperty("GROSS_PROFIT").GetProperty("value").GetDecimal()
                / lines.GetProperty("IS").GetProperty("REV").GetProperty("value").GetDecimal(),
            derived.GetProperty("grossMargin").GetProperty("value").GetDecimal());

        // The earliest served period has no prior same-type period: growth is null, not 0.
        var fy2024 = Period(root, "FY", "2024-12-31").GetProperty("derived");
        Assert.Equal(JsonValueKind.Null, fy2024.GetProperty("revenueGrowthYoy").GetProperty("value").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, derived.GetProperty("revenueGrowthYoy").GetProperty("value").ValueKind);
    }

    // -- (e) per-share rows: statement count first, price count second, null otherwise -------

    [Fact]
    public async Task TC_MDF_123_e_per_share_rows_use_the_statement_count_then_the_price_count_then_null()
    {
        await SeedUniverseAsync();
        var instrumentId = await SeedCapturedCompanyAsync(
            Asels, RealSourceCatalog.KapPublicReportAselsAnnual, priceDerivedCount: false);
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        // No statement count and no price-derived count: the per-share rows are null (the user's
        // exclusion #4 — no paid-in-capital-derived guess), never zero.
        using (var response = await client.GetAsync($"/api/v1/stocks/{Asels}/financials"))
        using (var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            var derived = Period(document.RootElement, "FY", "2025-12-31").GetProperty("derived");
            Assert.Equal(JsonValueKind.Null, derived.GetProperty("bvps").GetProperty("value").ValueKind);
            Assert.Equal(JsonValueKind.Null, derived.GetProperty("tbvPerShare").GetProperty("value").ValueKind);
            Assert.Equal(JsonValueKind.Null, derived.GetProperty("fcfPerShare").GetProperty("value").ValueKind);
            Assert.Equal("no-data", derived.GetProperty("bvps").GetProperty("state").GetString());
        }

        // Add the price lane's count (2.5bn) and a conflicting statement count (100m): the
        // statement's own SHARES_DILUTED wins (the pinned precedence).
        await using (var db = _postgres.CreateContext())
        {
            var sheet = await db.FinancialStatements.SingleAsync(statement =>
                statement.InstrumentId == instrumentId
                && statement.StatementType == "BS"
                && statement.PeriodEndDate == new DateOnly(2025, 12, 31));
            db.FinLineItems.Add(new FinLineItem
            {
                StatementId = sheet.Id,
                ItemCode = "SHARES_DILUTED",
                Value = 100_000_000m,
            });
            db.PriceDerivedShareCounts.Add(new PriceDerivedShareCount
            {
                InstrumentId = instrumentId,
                SharesOutstanding = 2_500_000_000L,
                MarketCapRaw = 375_000_000_000m,
                CloseRaw = 150m,
                PriceDate = T,
                RecordedAt = new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                SourceRef = "isyatirim",
            });
            await db.SaveChangesAsync();
        }

        using (var response = await client.GetAsync($"/api/v1/stocks/{Asels}/financials"))
        using (var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            var fy2025 = Period(document.RootElement, "FY", "2025-12-31");
            var lines = fy2025.GetProperty("lines");
            var equity = lines.GetProperty("BS").GetProperty("TOTAL_EQUITY").GetProperty("value").GetDecimal();
            var derived = fy2025.GetProperty("derived");

            // 100m is the statement's count: the served BVPS is equity / 100m, not / 2.5bn.
            Assert.Equal(equity / 100_000_000m, derived.GetProperty("bvps").GetProperty("value").GetDecimal());
            Assert.NotEqual(equity / 2_500_000_000m, derived.GetProperty("bvps").GetProperty("value").GetDecimal());

            var ocf = lines.GetProperty("CF").GetProperty("OCF").GetProperty("value").GetDecimal();
            var capex = lines.GetProperty("CF").GetProperty("CAPEX").GetProperty("value").GetDecimal();
            Assert.Equal((ocf - Math.Abs(capex)) / 100_000_000m,
                derived.GetProperty("fcfPerShare").GetProperty("value").GetDecimal());
        }
    }

    // -- (f) the financial-sector layout notice ---------------------------------------------

    [Fact]
    public async Task TC_MDF_123_f_bank_and_insurance_profiles_serve_the_layout_notice_instead_of_a_row_set()
    {
        await SeedUniverseAsync();
        var instrumentId = await SeedCapturedCompanyAsync(Tuprs, RealSourceCatalog.KapPublicReportTuprs);
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        // The industrial filing keeps the natural row set and carries no notice.
        using (var response = await client.GetAsync($"/api/v1/stocks/{Tuprs}/financials"))
        using (var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            var root = document.RootElement;
            Assert.Equal(JsonValueKind.Null, root.GetProperty("layoutNotice").ValueKind);
            Assert.NotEmpty(Period(root, "FY", "2025-12-31").GetProperty("lines").GetProperty("BS").EnumerateObject().ToList());
        }

        // A bank profile serves the explicit notice: not applicable, never an error and never a
        // half-filled industrial table claim.
        foreach (var profile in new[] { "bank", "insurance" })
        {
            await SetProfileAsync(instrumentId, profile);
            using var response = await client.GetAsync($"/api/v1/stocks/{Tuprs}/financials");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var notice = document.RootElement.GetProperty("layoutNotice");
            Assert.Equal(profile, notice.GetProperty("accountingProfile").GetString());
            Assert.True(notice.GetProperty("applies").GetBoolean());
            Assert.Equal("not-applicable", notice.GetProperty("state").GetString());
            Assert.Contains("industrial", notice.GetProperty("message").GetString(), StringComparison.Ordinal);
        }

        // A REIT is not a financial-sector profile: it keeps the natural row set.
        await SetProfileAsync(instrumentId, "reit");
        using (var response = await client.GetAsync($"/api/v1/stocks/{Tuprs}/financials"))
        using (var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("layoutNotice").ValueKind);
        }
    }

    // -- (g) a pre-migration row: no span, no flag, badges suppressed ------------------------

    [Fact]
    public async Task TC_MDF_123_g_a_pre_migration_row_serves_no_span_and_suppresses_the_badges()
    {
        await SeedUniverseAsync();
        await SeedLegacyInterimAsync("manual-test");
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/stocks/LEGACY/financials");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        // A Q period with no stored span degrades to today's Q label: `periodType` stays `Q`
        // and the span is honestly absent — never a fabricated range.
        var period = Period(root, "Q", "2022-06-30");
        Assert.Equal("Q", period.GetProperty("periodType").GetString());
        Assert.Equal(JsonValueKind.Null, period.GetProperty("span").ValueKind);
        Assert.Equal(JsonValueKind.Null, period.GetProperty("consolidated").ValueKind);

        // The [C] rows still compute from whatever the stored lines hold.
        var derived = period.GetProperty("derived");
        Assert.Equal(300m, derived.GetProperty("grossDebt").GetProperty("value").GetDecimal());
        Assert.Equal(250m, derived.GetProperty("netWorkingCapital").GetProperty("value").GetDecimal());
        Assert.Equal(250m, derived.GetProperty("netDebt").GetProperty("value").GetDecimal());

        // Every badge is suppressed rather than guessed: no flag, a pre-transition period and a
        // non-KAP evidence class all read false/null on the filing envelope.
        var filing = root.GetProperty("filing");
        Assert.Equal(JsonValueKind.Null, filing.GetProperty("consolidated").ValueKind);
        Assert.False(filing.GetProperty("tms29").GetBoolean());
        Assert.False(filing.GetProperty("kapSource").GetBoolean());
    }

    // -- (h) the DR-2 fence: gross debt is exactly ST_DEBT + LT_DEBT ------------------------

    [Fact]
    public async Task TC_MDF_123_h_gross_debt_is_the_fenced_st_plus_lt_pair_never_widened_to_leases()
    {
        await SeedUniverseAsync();
        await SeedCapturedCompanyAsync(Thyao, RealSourceCatalog.KapPublicReportThyao);
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync($"/api/v1/stocks/{Thyao}/financials");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        var fy2025 = Period(root, "FY", "2025-12-31");
        var lines = fy2025.GetProperty("lines").GetProperty("BS");
        var stDebt = lines.GetProperty("ST_DEBT").GetProperty("value").GetDecimal();
        var ltDebt = lines.GetProperty("LT_DEBT").GetProperty("value").GetDecimal();
        var stLease = lines.GetProperty("ST_LEASE_LIAB").GetProperty("value").GetDecimal();
        var ltLease = lines.GetProperty("LT_LEASE_LIAB").GetProperty("value").GetDecimal();

        // This body carries the lease-liability families beside the borrowings pair (millions
        // scale), so the fence is genuinely visible: the DR-2 sum excludes them.
        Assert.Equal(70_069_000_000m, stDebt);
        Assert.Equal(601_817_000_000m, ltDebt);
        Assert.Equal(86_152_000_000m, stLease);
        Assert.Equal(552_824_000_000m, ltLease);

        var grossDebt = fy2025.GetProperty("derived").GetProperty("grossDebt").GetProperty("value").GetDecimal();
        Assert.Equal(671_886_000_000m, grossDebt);
        Assert.Equal(stDebt + ltDebt, grossDebt);
        Assert.NotEqual(stDebt + ltDebt + stLease + ltLease, grossDebt);

        Assert.Equal(
            671_886_000_000m - 86_035_000_000m,
            fy2025.GetProperty("derived").GetProperty("netDebt").GetProperty("value").GetDecimal());
        Assert.Equal(
            911_256_000_000m - 1_145_000_000m - 8_867_000_000m,
            fy2025.GetProperty("derived").GetProperty("tangibleBookValue").GetProperty("value").GetDecimal());
    }

    // ── Harness ───────────────────────────────────────────────────────────────

    /// <summary>Seeds the fixture universe's reference world (sectors, indices, prices).</summary>
    private async Task SeedUniverseAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM derived_metrics");
    }

    /// <summary>
    /// The committed capture's whole statement world for one test company: the body is decoded by
    /// the production decoder and written by the production store (zero live requests), with the
    /// price lane's own count seeded so the EV header and per-share second tier are exercisable.
    /// </summary>
    private async Task<long> SeedCapturedCompanyAsync(
        string symbol,
        string captureKey,
        bool priceDerivedCount = true,
        string accountingProfile = "industrial")
    {
        await using var db = _postgres.CreateContext();

        var xu100 = await db.Indices.SingleAsync(index => index.Code == "XU100");
        var recordedAt = new DateTimeOffset(T.ToDateTime(new TimeOnly(18, 30)), TimeSpan.Zero);

        var instrument = await db.Instruments.SingleOrDefaultAsync(candidate => candidate.Symbol == symbol);
        if (instrument is null)
        {
            var alfa = await db.Instruments.AsNoTracking().SingleAsync(candidate => candidate.Symbol == "ALFA");
            instrument = new Instrument
            {
                Symbol = symbol,
                Name = symbol,
                SectorId = alfa.SectorId,
                AccountingProfile = accountingProfile,
                ListingDate = new DateOnly(1990, 1, 1),
                Status = "active",
                SourceRef = "fixture:universe",
                RecordedAt = new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            };
            db.Instruments.Add(instrument);
            await db.SaveChangesAsync();
        }

        instrument.AccountingProfile = accountingProfile;
        await db.SaveChangesAsync();

        // Each case states this company's whole statement world: a sibling case's rows are not
        // part of it (line items cascade with their statement).
        await db.FinancialStatements
            .Where(statement => statement.InstrumentId == instrument.Id)
            .ExecuteDeleteAsync();
        await db.PriceDerivedShareCounts
            .Where(count => count.InstrumentId == instrument.Id)
            .ExecuteDeleteAsync();

        if (!await db.IndexConstituents.AnyAsync(constituent =>
                constituent.IndexId == xu100.Id && constituent.InstrumentId == instrument.Id
                && constituent.EffectiveTo == null))
        {
            db.IndexConstituents.Add(new IndexConstituent
            {
                IndexId = xu100.Id,
                InstrumentId = instrument.Id,
                EffectiveFrom = instrument.ListingDate!.Value,
                EffectiveTo = null,
                SourceRef = "fixture:universe",
                RecordedAt = recordedAt,
            });
        }

        if (!await db.DailyPrices.AnyAsync(price => price.InstrumentId == instrument.Id && price.PriceDate == T))
        {
            db.DailyPrices.Add(new DailyPrice
            {
                InstrumentId = instrument.Id,
                PriceDate = T,
                CloseRaw = 150m,
                CloseAdjusted = 150m,
                Volume = 1_000_000,
                SourceRef = "fixture:universe",
                RecordedAt = recordedAt,
            });
        }

        if (priceDerivedCount)
        {
            db.PriceDerivedShareCounts.Add(new PriceDerivedShareCount
            {
                InstrumentId = instrument.Id,
                SharesOutstanding = 2_500_000_000L,
                MarketCapRaw = 375_000_000_000m,
                CloseRaw = 150m,
                PriceDate = T,
                RecordedAt = recordedAt,
                SourceRef = "isyatirim",
            });
        }

        await db.SaveChangesAsync();

        var report = KapPublicReportDecoder.Decode(RealSourceCatalog.Get(captureKey).Body, symbol);
        var write = await new FinancialStatementStore(db).UpsertAsync(KapPublic, RecordedAt, report.Payload);
        Assert.Empty(write.RejectedReasonCodes);
        Assert.Equal(report.Payload.Statements.Count, write.Inserted);

        return instrument.Id;
    }

    /// <summary>
    /// A pre-migration-shaped statement world (TKT-foundation-094 case (g)): a hand-built
    /// in-memory payload whose facts carry no span and no filing flag — exactly what a row
    /// written before the migration's columns existed looks like — under a non-KAP evidence
    /// class, at a period ending before the TMS 29 transition.
    /// </summary>
    private async Task SeedLegacyInterimAsync(string sourceRef = "kap-public")
    {
        await using var db = _postgres.CreateContext();

        var xu100 = await db.Indices.SingleAsync(index => index.Code == "XU100");
        var alfa = await db.Instruments.AsNoTracking().SingleAsync(candidate => candidate.Symbol == "ALFA");
        var recordedAt = new DateTimeOffset(T.ToDateTime(new TimeOnly(18, 30)), TimeSpan.Zero);

        var instrument = await db.Instruments.SingleOrDefaultAsync(candidate => candidate.Symbol == "LEGACY");
        if (instrument is null)
        {
            instrument = new Instrument
            {
                Symbol = "LEGACY",
                Name = "Legacy Interim",
                SectorId = alfa.SectorId,
                AccountingProfile = "industrial",
                ListingDate = new DateOnly(2005, 1, 3),
                Status = "active",
                SourceRef = "fixture:universe",
                RecordedAt = new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            };
            db.Instruments.Add(instrument);
            await db.SaveChangesAsync();
        }

        await db.FinancialStatements
            .Where(statement => statement.InstrumentId == instrument.Id)
            .ExecuteDeleteAsync();

        if (!await db.IndexConstituents.AnyAsync(constituent =>
                constituent.IndexId == xu100.Id && constituent.InstrumentId == instrument.Id
                && constituent.EffectiveTo == null))
        {
            db.IndexConstituents.Add(new IndexConstituent
            {
                IndexId = xu100.Id,
                InstrumentId = instrument.Id,
                EffectiveFrom = instrument.ListingDate!.Value,
                EffectiveTo = null,
                SourceRef = "fixture:universe",
                RecordedAt = recordedAt,
            });
            await db.SaveChangesAsync();
        }

        var period = new DateOnly(2022, 6, 30);
        var payload = new StatementsPayload(sourceRef, "LEGACY", "units", "TRY",
        [
            new StatementFact(
                "Q",
                period,
                2022,
                "IS",
                "as_reported",
                null,
                null,
                new Dictionary<string, decimal>(StringComparer.Ordinal)
                {
                    ["REV"] = 1_000m,
                    ["COGS"] = -600m,
                    ["EBIT"] = 100m,
                    ["PRETAX_INC"] = 80m,
                    ["TAX_EXP"] = 20m,
                    ["NI"] = 50m,
                    ["NI_PARENT"] = 50m,
                }),
            new StatementFact(
                "Q",
                period,
                2022,
                "BS",
                "as_reported",
                null,
                null,
                new Dictionary<string, decimal>(StringComparer.Ordinal)
                {
                    ["CUR_ASSETS"] = 400m,
                    ["CUR_LIAB"] = 150m,
                    ["ST_DEBT"] = 100m,
                    ["LT_DEBT"] = 200m,
                    ["CASH"] = 50m,
                    ["TOTAL_EQUITY"] = 250m,
                }),
        ]);

        var write = await new FinancialStatementStore(db).UpsertAsync(sourceRef, RecordedAt, payload);
        Assert.Empty(write.RejectedReasonCodes);
        Assert.Equal(2, write.Inserted);
    }

    private async Task SetProfileAsync(long instrumentId, string profile)
    {
        await using var db = _postgres.CreateContext();
        await db.Instruments
            .Where(instrument => instrument.Id == instrumentId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(instrument => instrument.AccountingProfile, profile));
    }

    private static HttpClient CreateClient(DegerliAppFactory factory)
        => factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    private static JsonElement Period(JsonElement root, string periodType, string periodEndDate) =>
        root.GetProperty("periods").EnumerateArray().Single(period =>
            period.GetProperty("periodType").GetString() == periodType
            && period.GetProperty("periodEndDate").GetString() == periodEndDate);
}
