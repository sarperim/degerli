using System.Net;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Core.Metrics;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Metrics;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Degerli.Persistence.Seed;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-foundation-096 acceptance — TC-MDF-124 (test plan §35): the key-metrics &amp; capital
/// tracker backend. The new canonical codes go through the real <c>metrics-recompute</c> job
/// (070's versioning and fast-path discipline intact), the sector medians job covers the
/// panel's set, and <c>GET /api/v1/stocks/{symbol}/key-metrics</c> serves current values,
/// the 5Y company median from stored history, sector medians with peer/exclusion counts, and
/// the capital &amp; dilution tracker block.
///
/// Goldens are hand-derived from the L2 fixture's own statement lines (FU §5/§5.1/§5.3) —
/// never re-derived from the implementation. Every case runs against a migrated
/// Testcontainers PostgreSQL; zero live requests.
/// </summary>
public sealed class StockKeyMetricsEndpointsTests : IClassFixture<PostgresFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    /// <summary>The L2 fixture's latest fiscal year (FU §5: FY2025 at R = 2026-10-06).</summary>
    private const int FixtureFiscalYear = 2025;

    /// <summary>The panel's code set — the key-value &amp; moat rows the endpoint serves.</summary>
    private static readonly string[] PanelCodes =
    [
        MetricCodes.Roic, MetricCodes.Croic, MetricCodes.Roe, MetricCodes.Roa,
        MetricCodes.FcfNi, MetricCodes.FcfNi3y, MetricCodes.CfoEbit, MetricCodes.MonetaryGainPretax,
        MetricCodes.EvEbit, MetricCodes.EvEbitda, MetricCodes.Pe, MetricCodes.PFcf, MetricCodes.FcfYield,
        MetricCodes.Pb, MetricCodes.PTbv, MetricCodes.DivYield,
        MetricCodes.NetDebtEbitda, MetricCodes.NetDebtFcf, MetricCodes.InterestCoverage,
        MetricCodes.CfoInterest, MetricCodes.CurrentRatio,
        MetricCodes.RevCagr3y, MetricCodes.RevCagr5y,
        MetricCodes.EbitCagr3y, MetricCodes.EbitCagr5y,
        MetricCodes.EpsCagr3y, MetricCodes.EpsCagr5y,
        MetricCodes.FcfPsCagr3y, MetricCodes.FcfPsCagr5y,
        MetricCodes.BvpsCagr3y, MetricCodes.BvpsCagr5y,
    ];

    /// <summary>The 17 codes v2 adds (11 quality/value + 6 windowed growth codes).</summary>
    private static readonly string[] NewCodes =
    [
        MetricCodes.Roa, MetricCodes.Croic, MetricCodes.EvEbit, MetricCodes.PFcf, MetricCodes.PTbv,
        MetricCodes.FcfNi, MetricCodes.FcfNi3y, MetricCodes.CfoEbit, MetricCodes.MonetaryGainPretax,
        MetricCodes.CfoInterest, MetricCodes.NetDebtFcf,
        MetricCodes.EbitCagr3y, MetricCodes.EbitCagr5y,
        MetricCodes.BvpsCagr3y, MetricCodes.BvpsCagr5y,
        MetricCodes.FcfPsCagr3y, MetricCodes.FcfPsCagr5y,
    ];

    /// <summary>The sector-median code set `02` §6.3 carries after this ticket (32 codes).</summary>
    private static readonly string[] MedianCodes =
    [
        MetricCodes.Pe, MetricCodes.Pb, MetricCodes.EvEbitda, MetricCodes.EvFcf, MetricCodes.FcfYield,
        MetricCodes.Roic, MetricCodes.Croic, MetricCodes.Roe, MetricCodes.Roa,
        MetricCodes.FcfNi, MetricCodes.FcfNi3y, MetricCodes.CfoEbit, MetricCodes.MonetaryGainPretax,
        MetricCodes.EvEbit, MetricCodes.PFcf, MetricCodes.PTbv, MetricCodes.DivYield,
        MetricCodes.NetDebtEbitda, MetricCodes.NetDebtFcf, MetricCodes.InterestCoverage,
        MetricCodes.CfoInterest, MetricCodes.CurrentRatio,
        MetricCodes.RevCagr3y, MetricCodes.RevCagr5y,
        MetricCodes.EbitCagr3y, MetricCodes.EbitCagr5y,
        MetricCodes.EpsCagr3y, MetricCodes.EpsCagr5y,
        MetricCodes.FcfPsCagr3y, MetricCodes.FcfPsCagr5y,
        MetricCodes.BvpsCagr3y, MetricCodes.BvpsCagr5y,
    ];

    private readonly PostgresFixture _postgres;

    public StockKeyMetricsEndpointsTests(PostgresFixture postgres) => _postgres = postgres;

    // -- (a) the engine: the new codes through the versioned engine --------------

    /// <summary>
    /// TC-MDF-124 (a) — every one of the 43 canonical codes is written per instrument and
    /// stamped with the <b>bumped</b> formula version (v1's 26 → v2's 43; 070's discipline:
    /// a row republished by a new definition carries the new stamp). The 17 new codes exist
    /// as rows for every instrument even where the fixture carries no input for them — an
    /// honest NULL row, never a missing row and never a <c>0</c>.
    /// </summary>
    [Fact]
    public async Task TC_MDF_124_engine_writes_the_panel_codes_stamped_with_the_bumped_formula_version()
    {
        await SeedAsync();

        // The definition version moved (v1 → v2): every new and re-stamped row carries it.
        Assert.Equal(2, CanonicalMetrics.Version);
        Assert.Equal(43, MetricCodes.All.Count);
        Assert.Equal(NewCodes.Length, NewCodes.Distinct(StringComparer.Ordinal).Count());

        await using var db = _postgres.CreateContext();
        var rows = await db.DerivedMetrics.Where(metric => metric.AsOfDate == T).ToListAsync();

        // 16 instruments × 43 codes.
        Assert.Equal(16 * 43, rows.Count);
        Assert.All(rows, row => Assert.Equal(CanonicalMetrics.Version, row.FormulaVersion));

        var byInstrument = rows.GroupBy(row => row.InstrumentId).ToList();
        Assert.Equal(16, byInstrument.Count);
        foreach (var instrumentRows in byInstrument)
        {
            var codes = instrumentRows.Select(row => row.MetricCode).ToHashSet(StringComparer.Ordinal);
            Assert.Equal(MetricCodes.All.ToHashSet(StringComparer.Ordinal), codes);
            // The 17 new codes are rows of their own, not an aggregate.
            Assert.All(NewCodes, code => Assert.Contains(code, codes));
        }

        // The values the fixture can support compute; the rest are honest NULLs (no BS total
        // assets, no TMS 29 monetary line, no cash-interest line, no multi-year EBIT/equity
        // history on the L2 world).
        var alfaId = await db.Instruments.Where(i => i.Symbol == "ALFA").Select(i => i.Id).SingleAsync();
        var alfa = rows.Where(row => row.InstrumentId == alfaId).ToDictionary(row => row.MetricCode, StringComparer.Ordinal);
        Assert.NotNull(alfa[MetricCodes.Croic].Value);
        Assert.NotNull(alfa[MetricCodes.FcfNi3y].Value);
        Assert.Null(alfa[MetricCodes.Roa].Value);
        Assert.Null(alfa[MetricCodes.EbitCagr3y].Value);
    }

    // -- (b) the endpoint: current values are the stored canonical values -------

    /// <summary>
    /// TC-MDF-124 (b) — the served current values are the stored <c>derived_metrics</c> rows
    /// (BR-RES-004: the endpoint computes no canonical value), cross-checked against the
    /// fixture lines' own arithmetic: ALFA FY2025 — price 20 TRY, 100 M shares (mcap 2,000),
    /// NI 200, EBIT 300, OCF 250, CAPEX 150 (FCF 100), debt 400, cash 200 (EV 2,200),
    /// invested capital 400 + 1,000 − 200 = 1,200, equity 1,000.
    /// </summary>
    [Fact]
    public async Task TC_MDF_124_key_metrics_endpoint_serves_the_stored_canonical_values()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/stocks/ALFA/key-metrics");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        // The honest-data envelope (`03` §1.2, M-1).
        Assert.Equal(T.ToString("yyyy-MM-dd"), root.GetProperty("asOf").GetString());
        Assert.Equal("ALFA", root.GetProperty("symbol").GetString());
        Assert.Equal(AccountingProfiles.Industrial, root.GetProperty("accountingProfile").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("financialSector").ValueKind);

        var metrics = root.GetProperty("metrics");
        Assert.Equal(PanelCodes.Length, metrics.EnumerateObject().Count());
        foreach (var code in PanelCodes)
        {
            Assert.True(metrics.TryGetProperty(code, out _), $"{code} is not served");
        }

        // The values the fixture's committed statement lines determine, hand-derived in FU §5.
        AssertServed(metrics, MetricCodes.Croic, 250m / 1200m);      // 0.208333
        AssertServed(metrics, MetricCodes.EvEbit, 2200m / 300m);      // 7.333333
        AssertServed(metrics, MetricCodes.PFcf, 2000m / 100m);        // 20
        AssertServed(metrics, MetricCodes.PTbv, 2000m / 1000m);       // 2 (no goodwill/intangibles)
        AssertServed(metrics, MetricCodes.FcfNi, 100m / 200m);        // 0.5
        AssertServed(metrics, MetricCodes.CfoEbit, 250m / 300m);      // 0.833333
        AssertServed(metrics, MetricCodes.NetDebtFcf, 200m / 100m);   // 2
        // fcf_ni_3y: the mean of FY2023/FY2024/FY2025 FCF/NI = (190/185 + 180/190 + 100/200) / 3.
        AssertServed(
            metrics,
            MetricCodes.FcfNi3y,
            ((190m / 185m) + (180m / 190m) + (100m / 200m)) / 3m);
        // FCF per share over a constant 100 M share count: the FCF CAGRs on a per-share basis.
        AssertServed(metrics, MetricCodes.FcfPsCagr3y, -0.206300m, window: 3);
        AssertServed(metrics, MetricCodes.FcfPsCagr5y, -0.089718m, window: 5);

        // Honest not-meaningful: inputs the committed fixture statements do not carry.
        foreach (var code in new[]
        {
            MetricCodes.Roa,            // no TOTAL_ASSETS line
            MetricCodes.MonetaryGainPretax, // no NET_MONETARY_GL line
            MetricCodes.CfoInterest,    // no INTEREST_PAID line
            MetricCodes.EbitCagr3y,     // one FY of EBIT only
            MetricCodes.EbitCagr5y,
            MetricCodes.BvpsCagr3y,     // one FY of equity only
            MetricCodes.BvpsCagr5y,
        })
        {
            AssertServedGap(metrics, code);
        }

        // Every served current value is the stored row, to the column's own precision.
        await using var db = _postgres.CreateContext();
        var instrumentId = await db.Instruments.Where(i => i.Symbol == "ALFA").Select(i => i.Id).SingleAsync();
        var stored = await db.DerivedMetrics
            .Where(metric => metric.InstrumentId == instrumentId && metric.AsOfDate == T)
            .ToDictionaryAsync(metric => metric.MetricCode, metric => metric, StringComparer.Ordinal);
        foreach (var code in PanelCodes)
        {
            Assert.Equal(stored[code].Value, DecimalOrNull(metrics.GetProperty(code), "value"));
            Assert.Equal(stored[code].WindowYears, IntOrNull(metrics.GetProperty(code), "windowYears"));
            Assert.Equal(stored[code].IsAdjusted, metrics.GetProperty(code).GetProperty("isAdjusted").GetBoolean());
            Assert.Equal(
                CanonicalMetrics.IsProfileGated(code),
                metrics.GetProperty(code).GetProperty("profileGated").GetBoolean());
        }
    }

    // -- (b cont.) the new formulas against stored lines the fixture omits -----

    /// <summary>
    /// TC-MDF-124 (b cont.) — the formulas the L2 world has no input for are exercised on
    /// stored lines injected into ALFA's own FY statements (the DR-1 falsifiability pattern):
    /// <c>roa</c> from <c>TOTAL_ASSETS</c>, <c>p_tbv</c> from equity less
    /// <c>GOODWILL</c>+<c>INTANGIBLES_NET</c>, <c>monetary_gain_pretax</c> from
    /// <c>NET_MONETARY_GL</c>/<c>PRETAX_INC</c>, <c>cfo_interest</c> from the CF statement's
    /// own (negative) interest-paid line, and the EBIT / BVPS CAGRs from multi-year series.
    /// </summary>
    [Fact]
    public async Task TC_MDF_124_the_new_formulas_compute_from_the_stored_lines()
    {
        await SeedAsync();
        await InjectAlfaInputsAsync();
        try
        {
            await RunMetricsAsync();

            await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
            using var client = CreateClient(factory);
            using var response = await client.GetAsync("/api/v1/stocks/ALFA/key-metrics");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var metrics = document.RootElement.GetProperty("metrics");

            // ROA = NI / TOTAL_ASSETS = 200 / 2,000.
            AssertServed(metrics, MetricCodes.Roa, 200m / 2000m);
            // P/TBV = mcap / (equity − goodwill − intangibles) = 2,000 / (1,000 − 100 − 50).
            AssertServed(metrics, MetricCodes.PTbv, 2000m / (1000m - 100m - 50m));
            // TMS 29 distortion alert = NET_MONETARY_GL / PRETAX_INC = 50 / 250.
            AssertServed(metrics, MetricCodes.MonetaryGainPretax, 50m / 250m);
            // CFO / |INTEREST_PAID| = 250 / |−50| — the KAP outflow sign is not the divisor.
            AssertServed(metrics, MetricCodes.CfoInterest, 250m / 50m);
            // EBIT 2020..2025 = 220, 230, 240, 250, 260, 300 → 3Y and 5Y CAGRs.
            AssertServed(metrics, MetricCodes.EbitCagr3y, 0.077217m, window: 3);
            AssertServed(metrics, MetricCodes.EbitCagr5y, 0.063995m, window: 5);
            // BVPS 2020..2025 = 7.0, 7.5, 8.0, 8.5, 9.0, 10.0 (equity ÷ 100 M shares).
            AssertServed(metrics, MetricCodes.BvpsCagr3y, 0.077217m, window: 3);
            AssertServed(metrics, MetricCodes.BvpsCagr5y, 0.073941m, window: 5);

            // The CAGRs that use a window spanning 2023-12-31 carry the TMS 29 marker.
            Assert.True(metrics.GetProperty(MetricCodes.EbitCagr3y).GetProperty("regimeBreak").GetBoolean());
            Assert.False(metrics.GetProperty(MetricCodes.EbitCagr3y).GetProperty("isAdjusted").GetBoolean());
        }
        finally
        {
            await RemoveAlfaInputsAsync();
        }
    }

    // -- (c) the 5Y company median: honest on thin depth, exact when deep ------

    /// <summary>
    /// TC-MDF-124 (c) — the 5Y company median is computed from the instrument's <b>stored</b>
    /// <c>derived_metrics</c> history at serve time. On the L2 world the history is one stored
    /// date, so the median is withheld (<c>notMeaningful</c>, <c>observations</c> 1) rather
    /// than dressed up; with the pinned floor (12 observations) reached it is the standard
    /// median of the stored values, and one observation below the floor it is withheld again.
    /// The nominal window and the actual depth are both disclosed.
    /// </summary>
    [Fact]
    public async Task TC_MDF_124_company_median_is_withheld_on_thin_history_and_exact_when_deep_enough()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        // (i) one stored observation → no median, and the disclosure says so.
        using (var thin = await client.GetAsync("/api/v1/stocks/ALFA/key-metrics"))
        {
            Assert.Equal(HttpStatusCode.OK, thin.StatusCode);
            using var document = JsonDocument.Parse(await thin.Content.ReadAsStringAsync());
            var block = document.RootElement.GetProperty("metrics").GetProperty(MetricCodes.Pe).GetProperty("companyMedian5y");
            Assert.Equal(5, block.GetProperty("windowYears").GetInt32());
            Assert.Equal(12, block.GetProperty("minimumObservations").GetInt32());
            Assert.Equal(1, block.GetProperty("observations").GetInt32());
            Assert.True(block.GetProperty("notMeaningful").GetBoolean());
            Assert.Equal(JsonValueKind.Null, block.GetProperty("value").ValueKind);
            Assert.Equal(0d, block.GetProperty("depthYears").GetDouble());
        }

        // (ii) twelve stored observations within the 5Y window → the stored values' median.
        await InsertPeHistoryAsync(11);
        using (var deep = await client.GetAsync("/api/v1/stocks/ALFA/key-metrics"))
        {
            Assert.Equal(HttpStatusCode.OK, deep.StatusCode);
            using var document = JsonDocument.Parse(await deep.Content.ReadAsStringAsync());
            var block = document.RootElement.GetProperty("metrics").GetProperty(MetricCodes.Pe).GetProperty("companyMedian5y");
            Assert.Equal(12, block.GetProperty("observations").GetInt32());
            Assert.False(block.GetProperty("notMeaningful").GetBoolean());
            // Stored values: 1..11 on the eleven earlier dates plus 10.0 at T → sorted
            // 1,2,3,4,5,6,7,8,9,10,10,11 → median (6 + 7) / 2 = 6.5 (FU §2: even count).
            Assert.Equal(6.5m, block.GetProperty("value").GetDecimal());
            Assert.True(block.GetProperty("depthYears").GetDouble() > 0d);
            // The current column is untouched by the history: pe stays the stored T value.
            Assert.Equal(10m, document.RootElement.GetProperty("metrics").GetProperty(MetricCodes.Pe).GetProperty("value").GetDecimal());
        }

        // (iii) one observation below the pinned floor → withheld again, never a 11-point median.
        await using (var db = _postgres.CreateContext())
        {
            var instrumentId = await db.Instruments.Where(i => i.Symbol == "ALFA").Select(i => i.Id).SingleAsync();
            await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM derived_metrics WHERE instrument_id = {0} AND metric_code = {1} AND as_of_date = {2}",
                instrumentId,
                MetricCodes.Pe,
                T.AddDays(-11));
        }

        using (var floor = await client.GetAsync("/api/v1/stocks/ALFA/key-metrics"))
        {
            Assert.Equal(HttpStatusCode.OK, floor.StatusCode);
            using var document = JsonDocument.Parse(await floor.Content.ReadAsStringAsync());
            var block = document.RootElement.GetProperty("metrics").GetProperty(MetricCodes.Pe).GetProperty("companyMedian5y");
            Assert.Equal(11, block.GetProperty("observations").GetInt32());
            Assert.True(block.GetProperty("notMeaningful").GetBoolean());
            Assert.Equal(JsonValueKind.Null, block.GetProperty("value").ValueKind);
        }

        await DeletePeHistoryAsync();
    }

    // -- (d) sector medians cover the panel, disclosed by peer/exclusion count --

    /// <summary>
    /// TC-MDF-124 (d) — the medians job covers the panel's code set (3 fixture sectors × 32
    /// codes) and the endpoint serves each code's stored median beside its peer and exclusion
    /// counts (UXR-RES-024/025). Sector A = ALFA (pe 10), BETA/GAMA (loss-makers, pe NULL),
    /// DELTA (pe 20), REST (pe 10) → median 10, 3 peers assessed, 2 excluded.
    /// </summary>
    [Fact]
    public async Task TC_MDF_124_sector_medians_cover_the_panel_set_with_peer_and_exclusion_counts()
    {
        await SeedAsync();

        await using var db = _postgres.CreateContext();
        var medians = await db.SectorMetricMedians.Where(median => median.AsOfDate == T).ToListAsync();
        Assert.Equal(3 * MedianCodes.Length, medians.Count);
        Assert.All(medians, median => Assert.Equal(CanonicalMetrics.Version, median.FormulaVersion));
        Assert.Equal(
            MedianCodes.ToHashSet(StringComparer.Ordinal),
            medians.Select(median => median.MetricCode).ToHashSet(StringComparer.Ordinal));

        var sectorId = await db.Instruments.Where(i => i.Symbol == "ALFA").Select(i => i.SectorId!.Value).SingleAsync();
        var sectorMedians = medians
            .Where(median => median.SectorId == sectorId)
            .ToDictionary(median => median.MetricCode, median => median, StringComparer.Ordinal);
        Assert.Equal(10m, sectorMedians[MetricCodes.Pe].MedianValue);
        Assert.Equal(3, sectorMedians[MetricCodes.Pe].PeerCount);
        Assert.Equal(2, sectorMedians[MetricCodes.Pe].ExcludedCount);

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);
        using var response = await client.GetAsync("/api/v1/stocks/ALFA/key-metrics");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var metrics = document.RootElement.GetProperty("metrics");

        // Every panel code is served with the stored sector median and its counts.
        foreach (var code in PanelCodes)
        {
            var entry = metrics.GetProperty(code);
            var expected = sectorMedians[code];
            Assert.Equal(expected.MedianValue, DecimalOrNull(entry, "sectorMedian"));
            Assert.Equal(expected.PeerCount, IntOrNull(entry, "peerCount"));
            Assert.Equal(expected.ExcludedCount, IntOrNull(entry, "excludedCount"));
        }

        // The cross-check above is load-bearing for a code the v1 job never covered: croic
        // is non-null for ALFA (IC 1,200), BETA (IC 450, OCF −50), GAMA (IC 350, OCF −50) and
        // REST (IC 770, OCF 100) — DELTA's invested capital is negative, so it is excluded.
        Assert.Equal(4, metrics.GetProperty(MetricCodes.Croic).GetProperty("peerCount").GetInt32());
        Assert.Equal(1, metrics.GetProperty(MetricCodes.Croic).GetProperty("excludedCount").GetInt32());
    }

    // -- (e) profile gating: a bank computes pe/roe, never ev_ebit -------------

    /// <summary>
    /// TC-MDF-124 (e) — a <c>bank</c> instrument carrying a <b>complete</b> industrial line
    /// set (the DR-1 falsifiability pattern) computes <c>pe</c>/<c>roe</c>/<c>roa</c> but never
    /// the industrial-shaped new codes: they are NULL by profile, and the endpoint names them
    /// <c>profileGated</c> and serves the <c>financialSector</c> not-applicable block. Its own
    /// sector's gated medians exclude it (disclosed by count) while the non-gated ones include
    /// it.
    /// </summary>
    [Fact]
    public async Task TC_MDF_124_a_bank_serves_pe_and_roe_but_never_ev_ebit_and_is_excluded_from_gated_medians()
    {
        await SeedAsync();
        try
        {
            var builder = new FixtureBuilder(_postgres.CreateContext());
            var sector = await builder.CreateSectorAsync("D096");
            var bank = await builder.CreateInstrumentAsync(
                "BANKA096",
                sectorId: sector.Id,
                accountingProfile: AccountingProfiles.Bank,
                listingDate: new DateOnly(2010, 1, 4));
            await builder.CreatePriceAsync(bank.Id, T, 30.00m);
            await builder.CreateCurrentConstituentAsync("XU100", bank.Id, new DateOnly(2010, 1, 4));

            var lines = IndustrialLines();
            foreach (var (type, periodEnd, values) in lines)
            {
                await builder.UpsertStatementLinesAsync(bank.Id, type, periodEnd, values);
            }

            await RunMetricsAsync();

            await using (var db = _postgres.CreateContext())
            {
                var rows = await db.DerivedMetrics
                    .Where(metric => metric.InstrumentId == bank.Id && metric.AsOfDate == T)
                    .ToDictionaryAsync(metric => metric.MetricCode, metric => metric, StringComparer.Ordinal);

                // Financial-meaningful concepts keep computing (the gate is not global).
                Assert.Equal(30m, rows[MetricCodes.Pe].Value);
                Assert.Equal(0.10m, rows[MetricCodes.Roe].Value);   // 300 / 3,000 equity
                Assert.Equal(0.06m, rows[MetricCodes.Roa].Value);   // 300 / 5,000 total assets

                // Every profile-gated code is NULL — the complete input set is present, so the
                // gate is what makes them so.
                var gated = MetricCodes.All.Where(CanonicalMetrics.IsProfileGated).ToList();
                Assert.Equal(26, gated.Count);
                Assert.Contains(MetricCodes.EvEbit, gated);
                Assert.Contains(MetricCodes.Croic, gated);
                Assert.Contains(MetricCodes.FcfNi3y, gated);
                Assert.All(gated, code =>
                {
                    Assert.Null(rows[code].Value);
                    Assert.Null(rows[code].WindowYears);
                });
            }

            await RunMediansAsync();

            await using (var db = _postgres.CreateContext())
            {
                var medians = await db.SectorMetricMedians
                    .Where(median => median.SectorId == sector.Id && median.AsOfDate == T)
                    .ToDictionaryAsync(median => median.MetricCode, median => median, StringComparer.Ordinal);

                // A gated code: the bank is the only peer and it is excluded, by count.
                Assert.Equal(0, medians[MetricCodes.EvEbit].PeerCount);
                Assert.Equal(1, medians[MetricCodes.EvEbit].ExcludedCount);
                Assert.Null(medians[MetricCodes.EvEbit].MedianValue);
                // A non-gated code: the same bank is a legitimate peer.
                Assert.Equal(1, medians[MetricCodes.Pe].PeerCount);
                Assert.Equal(0, medians[MetricCodes.Pe].ExcludedCount);
                Assert.Equal(30m, medians[MetricCodes.Pe].MedianValue);
            }

            await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
            using var client = CreateClient(factory);
            using var response = await client.GetAsync("/api/v1/stocks/BANKA096/key-metrics");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = document.RootElement;

            var financialSector = root.GetProperty("financialSector");
            Assert.Equal("sector_not_applicable", financialSector.GetProperty("state").GetString());
            Assert.Equal("financial_sector", financialSector.GetProperty("reason").GetString());
            Assert.Equal(AccountingProfiles.Bank, financialSector.GetProperty("profile").GetString());

            var metrics = root.GetProperty("metrics");
            AssertServed(metrics, MetricCodes.Pe, 30m);
            var evEbit = metrics.GetProperty(MetricCodes.EvEbit);
            Assert.True(evEbit.GetProperty("profileGated").GetBoolean());
            Assert.True(evEbit.GetProperty("notMeaningful").GetBoolean());
            Assert.Equal(JsonValueKind.Null, evEbit.GetProperty("value").ValueKind);
            Assert.Equal(0, evEbit.GetProperty("peerCount").GetInt32());
            Assert.Equal(1, evEbit.GetProperty("excludedCount").GetInt32());
        }
        finally
        {
            await DeleteInstrumentAsync("BANKA096");
        }
    }

    // -- (f)/(g) the capital & dilution tracker --------------------------------

    /// <summary>
    /// TC-MDF-124 (f)/(g) — the tracker block: the <b>bedelli/bedelsiz</b> history from the
    /// seeded <c>corporate_actions</c> (EPSL's split 1:5 of 2025-06-02 and bonus 1:10 of
    /// 2026-03-02, dated, on the corporate-action-adjusted basis), the paid-in-capital /
    /// share-count pair with its own as-of, the honest <c>no-count-history</c> N/M for the
    /// year-on-year share-count change (never guessed), and the <c>payout_ratio</c> reference
    /// read from <c>derived_metrics</c> (EPSL: 0.30 × 400 / 300 = 0.4).
    /// </summary>
    [Fact]
    public async Task TC_MDF_124_capital_tracker_serves_the_bedelli_bedelsiz_history_and_the_share_count_nm()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/stocks/EPSL/key-metrics");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var tracker = document.RootElement.GetProperty("capitalTracker");

        Assert.Equal("corporate-action-adjusted", tracker.GetProperty("corporateActionsBasis").GetString());
        var actions = tracker.GetProperty("corporateActions").EnumerateArray().ToList();
        Assert.Equal(2, actions.Count);

        // Newest first: the 2026-03-02 bonus (1:10 free shares).
        Assert.Equal("2026-03-02", actions[0].GetProperty("date").GetString());
        Assert.Equal("bonus_issue", actions[0].GetProperty("type").GetString());
        Assert.Equal("bedelsiz", actions[0].GetProperty("kind").GetString());
        Assert.Equal(0.1m, actions[0].GetProperty("ratio").GetDecimal());
        Assert.Equal(10m, actions[0].GetProperty("percent").GetDecimal());

        // The 2025-06-02 split (1:5).
        Assert.Equal("2025-06-02", actions[1].GetProperty("date").GetString());
        Assert.Equal("split", actions[1].GetProperty("type").GetString());
        Assert.Equal("split", actions[1].GetProperty("kind").GetString());
        Assert.Equal(5m, actions[1].GetProperty("ratio").GetDecimal());
        Assert.Equal(500m, actions[1].GetProperty("percent").GetDecimal());

        // The share-count change has no honest value: no count history is stored.
        var change = tracker.GetProperty("shareCountChangeYoy");
        Assert.Equal(JsonValueKind.Null, change.GetProperty("value").ValueKind);
        Assert.True(change.GetProperty("notMeaningful").GetBoolean());
        Assert.Equal("no-count-history", change.GetProperty("state").GetString());

        // Paid-in capital is money, and the L2 world stores none — honest nulls, not zeros.
        Assert.Equal(JsonValueKind.Null, tracker.GetProperty("paidInCapital").ValueKind);
        Assert.Equal(JsonValueKind.Null, tracker.GetProperty("paidInCapitalAsOf").ValueKind);
        Assert.Equal(JsonValueKind.Null, tracker.GetProperty("sharesOutstandingBasis").ValueKind);

        var payout = tracker.GetProperty("payoutRatio");
        Assert.False(payout.GetProperty("notMeaningful").GetBoolean());
        Assert.Equal(0.4m, payout.GetProperty("value").GetDecimal());
        Assert.Equal(T.ToString("yyyy-MM-dd"), payout.GetProperty("asOf").GetString());
    }

    // -- (h) the covered universe is the boundary ------------------------------

    /// <summary>
    /// TC-MDF-124 (h) — a symbol outside the covered XU100 universe is <c>404 NOT_FOUND</c>
    /// with the standard problem envelope (`03` §7), never an empty panel that looks like a
    /// data gap.
    /// </summary>
    [Fact]
    public async Task TC_MDF_124_a_symbol_outside_the_covered_universe_is_not_found()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/stocks/NOSUCH/key-metrics");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("NOT_FOUND", document.RootElement.GetProperty("code").GetString());
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Seeds the L2 fixture universe, then computes the canonical metrics and the sector
    /// medians at T through the registered jobs — the same canonical rows every surface
    /// serves (BR-RES-004 / NFR-RES-003).
    /// </summary>
    private async Task SeedAsync()
    {
        await using (var db = _postgres.CreateContext())
        {
            await FixtureSeeder.ApplyAsync(db, L2);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM derived_metrics");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM sector_metric_medians");
        }

        await RunMetricsAsync();
        await RunMediansAsync();
    }

    private async Task RunMetricsAsync()
    {
        await using var host = BuildIngestion();
        using var scope = host.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IIngestionJobRunner>();
        var result = await runner.RunAsync(MetricsRecomputeJob.Code, new IngestionRequest(T));
        Assert.Equal(IngestResult.Succeeded, result.Status);
    }

    private async Task RunMediansAsync()
    {
        await using var host = BuildIngestion();
        using var scope = host.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IIngestionJobRunner>();
        var result = await runner.RunAsync(SectorMediansJob.Code, new IngestionRequest(T));
        Assert.Equal(IngestResult.Succeeded, result.Status);
    }

    private ServiceProvider BuildIngestion()
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

    /// <summary>
    /// Injects the stored lines the L2 fixture does not carry onto ALFA's own FY statements:
    /// the total-assets and intangible block, the TMS 29 monetary line, the cash-interest line,
    /// and multi-year EBIT / equity series for the panel's CAGRs. Every line is removed again by
    /// <see cref="RemoveAlfaInputsAsync"/> so the shared fixture state is restored.
    /// </summary>
    private async Task InjectAlfaInputsAsync()
    {
        var builder = new FixtureBuilder(_postgres.CreateContext());
        long instrumentId;
        await using (var db = _postgres.CreateContext())
        {
            instrumentId = await db.Instruments.Where(i => i.Symbol == "ALFA").Select(i => i.Id).SingleAsync();
        }

        var fy = FixtureFiscalYear;
        await builder.UpsertStatementLinesAsync(instrumentId, "BS", new DateOnly(fy, 12, 31), new Dictionary<string, decimal>
        {
            ["TOTAL_ASSETS"] = 2000m,
            ["GOODWILL"] = 100m,
            ["INTANGIBLES_NET"] = 50m,
        });
        await builder.UpsertStatementLinesAsync(instrumentId, "IS", new DateOnly(fy, 12, 31), new Dictionary<string, decimal>
        {
            ["NET_MONETARY_GL"] = 50m,
        });
        await builder.UpsertStatementLinesAsync(instrumentId, "CF", new DateOnly(fy, 12, 31), new Dictionary<string, decimal>
        {
            ["INTEREST_PAID"] = -50m,
        });

        var ebit = new (int Year, decimal Value)[] { (2020, 220), (2021, 230), (2022, 240), (2023, 250), (2024, 260) };
        var equity = new (int Year, decimal Value)[] { (2020, 700), (2021, 750), (2022, 800), (2023, 850), (2024, 900) };
        foreach (var (year, value) in ebit)
        {
            await builder.UpsertStatementLinesAsync(
                instrumentId, "IS", new DateOnly(year, 12, 31), new Dictionary<string, decimal> { ["EBIT"] = value });
        }

        foreach (var (year, value) in equity)
        {
            await builder.UpsertStatementLinesAsync(
                instrumentId, "BS", new DateOnly(year, 12, 31), new Dictionary<string, decimal> { ["TOTAL_EQUITY"] = value });
        }
    }

    /// <summary>Removes the lines <see cref="InjectAlfaInputsAsync"/> added (they were absent before).</summary>
    private async Task RemoveAlfaInputsAsync()
    {
        await using var db = _postgres.CreateContext();
        var instrumentId = await db.Instruments.Where(i => i.Symbol == "ALFA").Select(i => i.Id).SingleAsync();
        var fy = FixtureFiscalYear;

        // The current-FY additions.
        await db.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM fin_line_items
            WHERE item_code IN ('TOTAL_ASSETS', 'GOODWILL', 'INTANGIBLES_NET', 'NET_MONETARY_GL', 'INTEREST_PAID')
              AND statement_id IN (SELECT id FROM financial_statements WHERE instrument_id = {0})
            """,
            instrumentId);

        // The prior-year EBIT / equity series.
        await db.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM fin_line_items
            WHERE item_code IN ('EBIT', 'TOTAL_EQUITY')
              AND statement_id IN (
                  SELECT id FROM financial_statements
                  WHERE instrument_id = {0} AND fiscal_year < {1})
            """,
            instrumentId,
            fy);
    }

    /// <summary>
    /// The complete industrial line set behind a financial profile (the DR-1 control). Five
    /// fiscal years of a growing series whose <b>latest</b> year carries the canonical values
    /// (NI 300, equity 3,000, total assets 5,000, EBIT 200, OCF 250) — so the gated CAGRs all
    /// have a real window and every gated metric <em>would</em> compute if the profile gate
    /// were absent.
    /// </summary>
    private static IReadOnlyList<(string Type, DateOnly PeriodEnd, IReadOnlyDictionary<string, decimal> Lines)> IndustrialLines()
    {
        var latest = FixtureFiscalYear;
        var years = new[] { latest - 4, latest - 3, latest - 2, latest - 1, latest };
        var result = new List<(string, DateOnly, IReadOnlyDictionary<string, decimal>)>();
        foreach (var year in years)
        {
            var end = new DateOnly(year, 12, 31);
            // delta is negative for the earlier years, 0 at the latest one.
            var delta = year - latest;
            result.Add(("IS", end, new Dictionary<string, decimal>
            {
                ["REV"] = 500m + (10m * delta),
                ["COGS"] = 300m + (5m * delta),
                ["GROSS_PROFIT"] = 200m + (5m * delta),
                ["EBIT"] = 200m + (10m * delta),
                ["DEPR_AMORT"] = 50m,
                ["EBITDA"] = 250m + (10m * delta),
                ["FIN_EXP"] = 25m,
                ["PRETAX_INC"] = 400m + (10m * delta),
                ["TAX_EXP"] = 80m,
                ["NET_MONETARY_GL"] = 60m + (2m * delta),
                ["NI"] = 300m + (5m * delta),
            }));
            result.Add(("BS", end, new Dictionary<string, decimal>
            {
                ["CASH"] = 500m,
                ["ST_DEBT"] = 2000m,
                ["LT_DEBT"] = 1000m,
                ["TOTAL_EQUITY"] = 3000m + (100m * delta),
                ["CUR_ASSETS"] = 800m,
                ["CUR_LIAB"] = 400m,
                ["TOTAL_ASSETS"] = 5000m,
                ["GOODWILL"] = 100m,
                ["INTANGIBLES_NET"] = 50m,
                ["SHARES_DILUTED"] = 300m,
                ["SHARES_OUT"] = 300m,
            }));
            result.Add(("CF", end, new Dictionary<string, decimal>
            {
                ["OCF"] = 250m + (5m * delta),
                ["CAPEX"] = 100m,
                ["INTEREST_PAID"] = -40m,
            }));
        }

        return result;
    }

    /// <summary>Eleven stored ALFA <c>pe</c> observations (values 1..11) before T, inside the 5Y window.</summary>
    private async Task InsertPeHistoryAsync(int count)
    {
        await using var db = _postgres.CreateContext();
        var instrumentId = await db.Instruments.Where(i => i.Symbol == "ALFA").Select(i => i.Id).SingleAsync();
        for (var offset = 1; offset <= count; offset++)
        {
            db.DerivedMetrics.Add(new DerivedMetric
            {
                InstrumentId = instrumentId,
                MetricCode = MetricCodes.Pe,
                AsOfDate = T.AddDays(-offset),
                Value = offset,
                FormulaVersion = CanonicalMetrics.Version,
                ComputedAt = new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task DeletePeHistoryAsync()
    {
        await using var db = _postgres.CreateContext();
        var instrumentId = await db.Instruments.Where(i => i.Symbol == "ALFA").Select(i => i.Id).SingleAsync();
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM derived_metrics WHERE instrument_id = {0} AND metric_code = {1} AND as_of_date < {2}",
            instrumentId,
            MetricCodes.Pe,
            T);
    }

    /// <summary>Removes an on-demand instrument and everything the case created for it.</summary>
    private async Task DeleteInstrumentAsync(string symbol)
    {
        await using var db = _postgres.CreateContext();
        var instrument = await db.Instruments.SingleOrDefaultAsync(i => i.Symbol == symbol);
        if (instrument is null)
        {
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM sector_metric_medians WHERE sector_id IN (SELECT id FROM sectors WHERE code = 'D096')");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM derived_metrics WHERE instrument_id = {0}", instrument.Id);
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM fin_line_items WHERE statement_id IN (SELECT id FROM financial_statements WHERE instrument_id = {0})",
            instrument.Id);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM financial_statements WHERE instrument_id = {0}", instrument.Id);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM daily_prices WHERE instrument_id = {0}", instrument.Id);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM index_constituents WHERE instrument_id = {0}", instrument.Id);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM instruments WHERE id = {0}", instrument.Id);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM sectors WHERE code = 'D096'");
    }

    private static HttpClient CreateClient(DegerliAppFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    /// <summary>A served metric value compared at the FU §11's 1e-5 granularity.</summary>
    private static void AssertServed(JsonElement metrics, string code, decimal expected, int? window = null)
    {
        var entry = metrics.GetProperty(code);
        var value = DecimalOrNull(entry, "value");
        Assert.NotNull(value);
        Assert.True(
            Math.Abs(value!.Value - expected) <= 0.00001m,
            $"{code}: expected {expected} (±1e-5) but was {value.Value}");
        Assert.Equal(window, IntOrNull(entry, "windowYears"));
        Assert.False(entry.GetProperty("notMeaningful").GetBoolean(), $"{code} served as not-meaningful");
    }

    private static void AssertServedGap(JsonElement metrics, string code)
    {
        var entry = metrics.GetProperty(code);
        Assert.True(entry.GetProperty("notMeaningful").GetBoolean(), $"{code} served as meaningful");
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("value").ValueKind);
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("windowYears").ValueKind);
    }

    private static decimal? DecimalOrNull(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return element.GetDecimal();
    }

    private static int? IntOrNull(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return element.GetInt32();
    }
}
