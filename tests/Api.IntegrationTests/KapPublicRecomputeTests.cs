using System.Net;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Core.Metrics;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Coverage;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Metrics;
using Degerli.Ingestion.Statements;
using Degerli.Ingestion.Valuation;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-foundation-083 acceptance — <b>TC-MDF-111</b> (test plan §24; <c>01</c> v1.5.57
/// §10.9.13 + §10.9.19): the mixed-source world's recompute honesty, its per-source coverage
/// depth, the honest-null market P/E, and the cross-source <b>read preference</b> that
/// TKT-foundation-082's overlap skip deferred.
/// <para>
/// <b>The world this class seeds, stated plainly.</b> The FU §3–§6 fixture world is the
/// incumbent half; four symbols' statements are then replaced with a hand-seeded mixed world so
/// each behaviour has an instrument to be observed on:
/// <list type="bullet">
/// <item><c>ALFA</c> — both sources hold FY2025 (the incumbent <c>kap</c> filing <b>and</b> the
/// public-site <c>kap-public</c> filing), plus an incumbent-only FY2023 period. This is the
/// coexistence case: the public-site fact is served, the incumbent is retained, and the
/// incumbent's share count is never borrowed for the newer filing.</item>
/// <item><c>BETA</c> — the incumbent only, at FY2025 (the "public-site absent → VYK served"
/// fallback).</item>
/// <item><c>GAMA</c> — the public site only, at FY2025 (current fundamentals computed from the new
/// lane's facts with no incumbent row anywhere).</item>
/// <item><c>UNSEC</c> — FY2023 <c>kap</c> facts only: the shape that must read as <i>2023-deep</i>
/// in coverage, never as "current".</item>
/// </list>
/// Values are stored canonical (the same shape the FU fixture stores), so the metric goldens
/// below are hand-derived from them — never re-derived from the implementation.
/// </para>
/// <para>
/// <b>Doubles only, zero live requests.</b> Every run goes through the production job runner and
/// the production composition; the served payloads come from the real in-process API host. No
/// test in this file touches the network beyond the Testcontainers PostgreSQL, and none calls
/// kap.org.tr. Nothing here fabricates source data: the public-site facts are test-seeded store
/// rows (exactly as §23's coexistence cases seed the incumbent half), not a manufactured capture.
/// </para>
/// </summary>
public sealed class KapPublicRecomputeTests : IClassFixture<PostgresFixture>
{
    /// <summary>The FU universe × the 43 canonical metric codes (TC-MDF-026's written count;
    /// 26 through v1, 43 since the key-value/moat panel — TKT-foundation-096).</summary>
    private const int MetricRows = 16 * 43;

    private const string Kap = StatementSourcePreference.IncumbentSource;
    private const string KapPublic = StatementSourcePreference.PublicSiteSource;

    /// <summary>The as-reported / restated version vocabulary the store accepts.</summary>
    private const string AsReported = "as_reported";
    private const string Restated = "restated";

    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;
    private static DateTimeOffset RecordedAt => new(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    /// <summary>The current fiscal year end both sources can hold (§23's FY2025/2026 world).</summary>
    private static readonly DateOnly FyCurrent = new(2025, 12, 31);

    /// <summary>The incumbent-only historical period (an as-of date before the public lane existed).</summary>
    private static readonly DateOnly FyIncipient = new(2023, 12, 31);

    private static readonly DateOnly KapCurrentPublished = new(2026, 3, 1);
    private static readonly DateOnly KapPublicCurrentPublished = new(2026, 8, 20);

    /// <summary>The symbols whose fixture statements this class replaces with the mixed world.</summary>
    private static readonly string[] MixedWorldSymbols = ["ALFA", "BETA", "GAMA", "UNSEC"];

    private readonly PostgresFixture _postgres;

    public KapPublicRecomputeTests(PostgresFixture postgres) => _postgres = postgres;

    // =============================================================================================
    // TC-MDF-111 — the recompute over the mixed world, stamped by the formula version
    // =============================================================================================

    /// <summary>
    /// TC-MDF-111 — current-period metrics are computed from the facts the coexistence rule
    /// makes current, and every written row carries TKT-foundation-070's formula version.
    /// <para>
    /// The preference is observable in the numbers: ALFA's FY2025 exists twice (incumbent EBIT
    /// 100 on revenue 1,000 → 10%; public-site EBIT 400 on revenue 2,000 → 20%), so an
    /// operating margin of <c>0.20</c> is the public-site fact being served and a <c>0.10</c>
    /// would be the incumbent's row winning (or both rows being mixed). Per-share metrics stay
    /// honestly NULL because the current filing carries no share count — even though the
    /// retained incumbent row for the <i>same</i> period does.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TC_MDF_111_current_period_metrics_compute_from_the_current_facts_and_carry_the_formula_version()
    {
        await ResetAsync();
        await SeedMixedWorldAsync();

        var run = await ComputeDerivedMetricsAsync(T);
        Assert.Equal("succeeded", run.Status);
        Assert.Equal(MetricRows, run.Written);

        // ALFA — both sources hold FY2025: the public-site fact serves (20%), and the retained
        // incumbent row (10%) is not mixed into it.
        Assert.Equal(0.20m, await ReadMetricAsync("ALFA", MetricCodes.OperatingMargin));
        Assert.Equal(0.20m, await ReadMetricAsync("ALFA", MetricCodes.Roe)); // 400 NI / 2,000 equity

        // Growth is a value, not just a row: ALFA's FY2023 incumbent revenue (900) to its FY2025
        // public-site revenue (2,000) is a two-interval fallback of the 3Y window —
        // √(2,000 / 900) − 1 = 0.490712. The incumbent's 1,000 for the same period would give
        // √(1,000 / 900) − 1 ≈ 0.054093, so the figure also proves which source's fact served.
        Assert.Equal(0.490712m, await ReadMetricAsync("ALFA", MetricCodes.RevCagr3y));

        // The current filing carries no share count, so every share-derived figure is honestly
        // NULL — the incumbent's 100 M shares for the same period are never borrowed.
        Assert.Null(await ReadMetricAsync("ALFA", MetricCodes.Pe));
        Assert.Null(await ReadMetricAsync("ALFA", MetricCodes.Pb));
        Assert.Null(await ReadMetricAsync("ALFA", MetricCodes.EvEbitda));

        // BETA — the public site holds nothing for this period: the incumbent's fact serves.
        Assert.Equal(0.10m, await ReadMetricAsync("BETA", MetricCodes.OperatingMargin)); // 50 / 500
        Assert.Equal(4m, await ReadMetricAsync("BETA", MetricCodes.Pe)); // 10.00 / (200 NI ÷ 80 shares)

        // GAMA — the public site is the only source: current metrics come from the new lane.
        Assert.Equal(0.30m, await ReadMetricAsync("GAMA", MetricCodes.OperatingMargin)); // 180 / 600
        Assert.Equal(0.25m, await ReadMetricAsync("GAMA", MetricCodes.Roe)); // 150 / 600

        // UNSEC — 2023 is the deepest fact this instrument has; the engine serves what exists
        // (and the coverage depth below is what says how old it is), never a fabricated current.
        Assert.Equal(0.10m, await ReadMetricAsync("UNSEC", MetricCodes.OperatingMargin)); // 30 / 300
        Assert.Equal(0.15m, await ReadMetricAsync("UNSEC", MetricCodes.Roe)); // 30 / 200

        // 070's provenance: every written row is stamped with the current definition version —
        // the recompute's provenance is written, never inferred from a timestamp.
        await using (var db = _postgres.CreateContext())
        {
            var versions = await db.DerivedMetrics
                .Where(metric => metric.AsOfDate == T)
                .Select(metric => metric.FormulaVersion)
                .ToListAsync();

            Assert.Equal(MetricRows, versions.Count);
            Assert.All(versions, version => Assert.Equal(CanonicalMetrics.Version, version));
        }

        // The same contract for the sector-median lane over the same mixed world.
        var medians = await RunMediansAsync(T);
        Assert.Equal("succeeded", medians.Status);

        await using (var db = _postgres.CreateContext())
        {
            var medianVersions = await db.SectorMetricMedians
                .Where(median => median.AsOfDate == T)
                .Select(median => median.FormulaVersion)
                .ToListAsync();

            Assert.NotEmpty(medianVersions);
            Assert.All(medianVersions, version => Assert.Equal(CanonicalMetrics.Version, version));
        }
    }

    // =============================================================================================
    // TC-MDF-111 — the overlap read preference (i): the public-site current fact is served
    // =============================================================================================

    /// <summary>
    /// TC-MDF-111 (overlap read preference, i) — with both sources holding the same period, the
    /// served financials payload returns <b>one</b> period built from the public-site current fact
    /// (revenue 2,000, FCF 300 − |100| = 200, not the incumbent's 1,000 / 100), raises no false
    /// <c>restated</c> flag, and the incumbent's row and lines are still in the store untouched —
    /// the preference masks one row on read; it deletes nothing.
    /// </summary>
    [Fact]
    public async Task TC_MDF_111_i_the_served_payload_selects_the_public_site_fact_and_the_incumbent_row_survives()
    {
        await ResetAsync();
        await SeedMixedWorldAsync();

        await using (var factory = new DegerliAppFactory(_postgres.ConnectionString))
        {
            var client = factory.CreateClient();
            var response = await client.GetAsync(new Uri("/api/v1/stocks/ALFA/financials", UriKind.Relative));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var periods = document.RootElement.GetProperty("periods").EnumerateArray().ToList();

            // One entry per (periodType, periodEndDate): two sources holding one period is one
            // period on the served surface, never a duplicated row. The distinct key is the whole
            // period coordinate — a Q and an FY legitimately share a period end date.
            Assert.Equal(
                periods.Count,
                periods
                    .Select(period => (
                        period.GetProperty("periodType").GetString(),
                        period.GetProperty("periodEndDate").GetString()))
                    .Distinct()
                    .Count());

            var current = Assert.Single(
                periods,
                period => period.GetProperty("periodEndDate").GetString() == "2025-12-31");
            Assert.Equal("FY", current.GetProperty("periodType").GetString());

            // The public-site current fact, line by line: REV 2,000, NI 400, FCF = OCF 300 − |CAPEX 100|.
            Assert.Equal(2000m, current.GetProperty("revenue").GetProperty("value").GetDecimal());
            Assert.Equal(400m, current.GetProperty("netIncome").GetProperty("value").GetDecimal());
            Assert.Equal(200m, current.GetProperty("fcf").GetProperty("value").GetDecimal());

            // A cross-source fact is never recorded as a restatement of the other source's filing.
            Assert.False(current.GetProperty("revenue").GetProperty("restated").GetBoolean());

            // The incumbent-only period is still served — the preference is per period, not a
            // global switch away from the incumbent source.
            var incipient = Assert.Single(
                periods,
                period => period.GetProperty("periodEndDate").GetString() == "2023-12-31");
            Assert.Equal(900m, incipient.GetProperty("revenue").GetProperty("value").GetDecimal());

            // SNA (shareholders' net assets) comes from the same preferred statement: the served
            // FY2025 balance sheet is the public-site one, equity 2,000 — and it carries no share
            // count, so the count is honestly absent rather than the incumbent's 100 M borrowed.
            var sheetResponse = await client.GetAsync(new Uri("/api/v1/stocks/ALFA/balance-sheet", UriKind.Relative));
            Assert.Equal(HttpStatusCode.OK, sheetResponse.StatusCode);

            using var sheet = JsonDocument.Parse(await sheetResponse.Content.ReadAsStringAsync());
            var equityFigure = sheet.RootElement.GetProperty("lines").GetProperty("totalEquity");
            Assert.Equal(2000m, equityFigure.GetProperty("value").GetDecimal());
            Assert.False(equityFigure.GetProperty("restated").GetBoolean());
            Assert.Equal(
                JsonValueKind.Null,
                sheet.RootElement.GetProperty("lines").GetProperty("sharesDiluted").GetProperty("value").ValueKind);
        }

        // Nothing was masked in the store: both sources' rows and their own lines are there, the
        // incumbent carrying its own values.
        await using (var db = _postgres.CreateContext())
        {
            var alfaId = await InstrumentIdAsync(db, "ALFA");
            var rows = await db.FinancialStatements
                .Where(statement => statement.InstrumentId == alfaId
                    && statement.PeriodType == "FY"
                    && statement.PeriodEndDate == FyCurrent
                    && statement.StatementType == "IS")
                .ToListAsync();

            Assert.Equal(2, rows.Count);
            var incumbent = Assert.Single(rows, row => row.SourceRef == Kap);
            var current = Assert.Single(rows, row => row.SourceRef == KapPublic);

            Assert.Equal(0, incumbent.Version);
            Assert.Null(incumbent.RestatementDate);
            Assert.Equal(0, current.Version);
            Assert.Null(current.RestatementDate);

            var incumbentRevenue = await db.FinLineItems
                .SingleAsync(line => line.StatementId == incumbent.Id && line.ItemCode == "REV");
            var currentRevenue = await db.FinLineItems
                .SingleAsync(line => line.StatementId == current.Id && line.ItemCode == "REV");
            Assert.Equal(1000m, incumbentRevenue.Value);
            Assert.Equal(2000m, currentRevenue.Value);
        }

        // The same pick, proven by behaviour in another consumer rather than by inspection: the
        // DCF job's active ALFA baseline carries base FCF 200 — the public-site cash flow
        // (OCF 300 − |CAPEX 100|), not the incumbent's 100 (250 − |150|) — so the baseline was
        // built from the served statement's facts, not from whichever row arrived last.
        await using (var host = BuildHost())
        {
            var dcf = await RunAsync(host, DcfBaselineGenerationJob.Code, T);
            Assert.Equal("succeeded", dcf.Status);
        }

        await using (var db = _postgres.CreateContext())
        {
            var alfaId = await InstrumentIdAsync(db, "ALFA");
            var baseline = await db.DcfBaselines
                .SingleAsync(row => row.InstrumentId == alfaId && row.IsActive);

            using var baselineParameters = JsonDocument.Parse(baseline.ParamsJson);
            Assert.Equal(
                200m,
                baselineParameters.RootElement.GetProperty("params").GetProperty("base_fcf").GetDecimal());
        }
    }

    // =============================================================================================
    // TC-MDF-111 — the overlap read preference (ii) and (iii): the incumbent fallback and as-of
    // =============================================================================================

    /// <summary>
    /// TC-MDF-111 (overlap read preference, ii and iii) — the preference is a fallback, not a
    /// filter: where the public site holds no fact for a period the incumbent's fact is served
    /// (BETA's FY2025, ALFA's FY2023), and an as-of query still reaches <b>both</b> sources' rows
    /// at the overlapped period — the incumbent is retained and as-of queryable, so the newer
    /// filing never erases what an earlier as-of read would have answered.
    /// </summary>
    [Fact]
    public async Task TC_MDF_111_ii_iii_the_incumbent_is_served_when_the_public_site_has_no_fact_and_as_of_reaches_both()
    {
        await ResetAsync();
        await SeedMixedWorldAsync();

        await using (var factory = new DegerliAppFactory(_postgres.ConnectionString))
        {
            var client = factory.CreateClient();

            // (ii) public-site absent for the period → the incumbent's fact is served.
            var beta = await ReadFinancialsAsync(client, "BETA");
            var betaCurrent = Assert.Single(
                beta,
                period => period.GetProperty("periodEndDate").GetString() == "2025-12-31");
            Assert.Equal(500m, betaCurrent.GetProperty("revenue").GetProperty("value").GetDecimal());
            Assert.Equal(50m, betaCurrent.GetProperty("ebit").GetProperty("value").GetDecimal());

            // (ii) the same for a period the public lane never reached at all (ALFA's FY2023).
            var alfa = await ReadFinancialsAsync(client, "ALFA");
            var alfaIncipient = Assert.Single(
                alfa,
                period => period.GetProperty("periodEndDate").GetString() == "2023-12-31");
            Assert.Equal(900m, alfaIncipient.GetProperty("revenue").GetProperty("value").GetDecimal());

            // A company whose deepest fact is 2023 is served exactly that period — no 2025/2026
            // row is invented for it (the "reads as such, never as current" pin).
            var unsec = await ReadFinancialsAsync(client, "UNSEC");
            var unsecPeriods = unsec
                .Select(period => period.GetProperty("periodEndDate").GetString())
                .ToList();
            Assert.Equal(["2023-12-31"], unsecPeriods);
        }

        // (iii) the as-of read. Statements carry no as-of endpoint of their own; the as-of
        // coordinate a reader uses is the filing's publication date, so the query below is the
        // one an as-of read makes. The incumbent answers an as-of before the public-site filing
        // existed, and both sources answer it afterwards — the preference never deletes the
        // incumbent row it does not serve.
        await using (var db = _postgres.CreateContext())
        {
            var alfaId = await InstrumentIdAsync(db, "ALFA");
            var rows = await db.FinancialStatements
                .Where(statement => statement.InstrumentId == alfaId
                    && statement.PeriodType == "FY"
                    && statement.PeriodEndDate == FyCurrent
                    && statement.StatementType == "IS")
                .ToListAsync();

            Assert.Equal(2, rows.Count);

            var asOfIncumbentOnly = rows
                .Where(row => row.PublishedAt <= KapCurrentPublished)
                .ToList();
            var incumbent = Assert.Single(asOfIncumbentOnly);
            Assert.Equal(Kap, incumbent.SourceRef);
            Assert.Equal(KapCurrentPublished, incumbent.PublishedAt);

            var asOfPublic = rows
                .Where(row => row.PublishedAt <= KapPublicCurrentPublished)
                .ToList();
            Assert.Equal(2, asOfPublic.Count);
            Assert.Equal(
                [Kap, KapPublic],
                asOfPublic.Select(row => row.SourceRef).OrderBy(source => source, StringComparer.Ordinal).ToArray());
        }
    }

    // =============================================================================================
    // TC-MDF-111 — market P/E honesty: no share count is borrowed across sources
    // =============================================================================================

    /// <summary>
    /// TC-MDF-111 — market P/E stays honestly NULL where the <b>current</b> filing carries no
    /// share count (FR-MOV-006 / BR-MOV-010, the 065 Option-A posture), and the exclusion is
    /// counted. The proof is a differential over the same day: with the public-site balance sheet
    /// serving, ALFA has no market cap (the incumbent row's 100 M shares for the same period are
    /// never borrowed); drop that one public-site row and the incumbent's balance sheet serves,
    /// the cap appears, and the snapshot's exclusion count falls by exactly one.
    /// </summary>
    [Fact]
    public async Task TC_MDF_111_market_pe_stays_honestly_null_and_never_borrows_the_incumbents_share_count()
    {
        await ResetAsync();
        await SeedMixedWorldAsync();

        // The per-share posture at the metric level: ROE is computable (equity is reported), P/E
        // is not (no share count in the current filing) — nothing is fabricated to fill the gap.
        await ComputeDerivedMetricsAsync(T);
        Assert.NotNull(await ReadMetricAsync("ALFA", MetricCodes.Roe));
        Assert.Null(await ReadMetricAsync("ALFA", MetricCodes.Pe));

        await using (var host = BuildHost())
        {
            var first = await RunAsync(host, MarketSnapshotJob.Code, T);
            Assert.Equal("succeeded", first.Status);
        }

        MarketSnapshot before;
        await using (var db = _postgres.CreateContext())
        {
            before = await db.MarketSnapshots.SingleAsync(snapshot => snapshot.SnapshotDate == T);
        }

        // PF/BR-MOV-010: a member whose current filing has no share count is excluded from the
        // cap-weighted aggregate and counted — not silently dropped and not given a cap. The
        // pre-state is pinned exactly: in this mixed world ALFA is the one excluded constituent
        // (the served public-site FY2025 balance sheet carries no SHARES_DILUTED, while BETA,
        // GAMA and UNSEC each seed one and the other twelve keep their fixture balance sheets),
        // and the differential below holds the repair to exactly one fewer.
        Assert.NotNull(before.MarketPe);
        Assert.Equal(1, before.MarketPeExcludedCount);

        // Now the public-site balance sheet is gone for that period: the incumbent's balance
        // sheet (which does carry SHARES_DILUTED) becomes the served fact under preference (ii).
        await using (var db = _postgres.CreateContext())
        {
            var alfaId = await InstrumentIdAsync(db, "ALFA");
            await db.FinancialStatements
                .Where(statement => statement.InstrumentId == alfaId
                    && statement.SourceRef == KapPublic
                    && statement.PeriodType == "FY"
                    && statement.PeriodEndDate == FyCurrent
                    && statement.StatementType == "BS")
                .ExecuteDeleteAsync();
        }

        await using (var host = BuildHost(TimeSpan.FromHours(2)))
        {
            var second = await RunAsync(host, MarketSnapshotJob.Code, T);
            Assert.Equal("succeeded", second.Status);
        }

        await using (var db = _postgres.CreateContext())
        {
            var after = await db.MarketSnapshots.SingleAsync(snapshot => snapshot.SnapshotDate == T);

            // One snapshot row per trading day — the recompute repaired it in place.
            Assert.Equal(1, await db.MarketSnapshots.CountAsync(snapshot => snapshot.SnapshotDate == T));

            Assert.Equal(before.MarketPeExcludedCount - 1, after.MarketPeExcludedCount);
            Assert.NotNull(after.MarketPe);
            Assert.NotEqual(before.MarketPe, after.MarketPe);
        }
    }

    // =============================================================================================
    // TC-MDF-111 — per-source coverage depth
    // =============================================================================================

    /// <summary>
    /// TC-MDF-111 — coverage metadata reports the achieved depth <b>per source</b>: ALFA shows both
    /// evidence classes with each one's own range; a source that wrote nothing for an instrument
    /// has <b>no</b> row (honest absence, never a zero or a gap claim); and the aggregate
    /// <c>statements</c> row stays the union. A 2023-only company reads as 2023-deep.
    /// </summary>
    [Fact]
    public async Task TC_MDF_111_coverage_reports_per_source_achieved_depth_and_absence_is_not_a_zero()
    {
        await ResetAsync();
        await SeedMixedWorldAsync();

        await using (var db = _postgres.CreateContext())
        {
            var recorder = new CoverageRecorder(db);
            await recorder.ReconcileUniverseAsync();
        }

        await using (var db = _postgres.CreateContext())
        {
            var alfaId = await InstrumentIdAsync(db, "ALFA");

            // ALFA — both sources, each with its own achieved range.
            var kap = await ReadCoverageAsync(db, alfaId, CoverageDataTypes.StatementsBySource(Kap));
            Assert.NotNull(kap);
            Assert.Equal(FyIncipient, kap!.AvailableFrom);
            Assert.Equal(FyCurrent, kap.AvailableTo);

            var kapPublic = await ReadCoverageAsync(db, alfaId, CoverageDataTypes.StatementsBySource(KapPublic));
            Assert.NotNull(kapPublic);
            Assert.Equal(FyCurrent, kapPublic!.AvailableFrom);
            Assert.Equal(FyCurrent, kapPublic.AvailableTo);

            // The aggregate row is still the union across sources.
            var aggregate = await ReadCoverageAsync(db, alfaId, CoverageDataTypes.Statements);
            Assert.NotNull(aggregate);
            Assert.Equal(FyIncipient, aggregate!.AvailableFrom);
            Assert.Equal(FyCurrent, aggregate.AvailableTo);

            // BETA — the incumbent wrote here, the public site did not: no public row at all.
            var betaId = await InstrumentIdAsync(db, "BETA");
            Assert.NotNull(await ReadCoverageAsync(db, betaId, CoverageDataTypes.StatementsBySource(Kap)));
            Assert.Null(await ReadCoverageAsync(db, betaId, CoverageDataTypes.StatementsBySource(KapPublic)));

            // GAMA — the mirror image: the public site is the only writer.
            var gamaId = await InstrumentIdAsync(db, "GAMA");
            Assert.NotNull(await ReadCoverageAsync(db, gamaId, CoverageDataTypes.StatementsBySource(KapPublic)));
            Assert.Null(await ReadCoverageAsync(db, gamaId, CoverageDataTypes.StatementsBySource(Kap)));

            // UNSEC — 2023-deep, and the row says so; nothing claims a current period.
            var unsecId = await InstrumentIdAsync(db, "UNSEC");
            var unsec = await ReadCoverageAsync(db, unsecId, CoverageDataTypes.StatementsBySource(Kap));
            Assert.NotNull(unsec);
            Assert.Equal(FyIncipient, unsec!.AvailableFrom);
            Assert.Equal(FyIncipient, unsec.AvailableTo);

            // The per-source family is provenance, not an instrument × data-type invariant: it
            // stays outside the no-silent-gaps set (a missing per-source row means "this source
            // wrote nothing here", which is not a coverage gap).
            Assert.DoesNotContain(CoverageDataTypes.StatementsBySource(Kap), CoverageDataTypes.InstrumentScoped);
            Assert.DoesNotContain(CoverageDataTypes.StatementsBySource(KapPublic), CoverageDataTypes.InstrumentScoped);
        }
    }

    // =============================================================================================
    // TC-MDF-111 — the store key carries the source (the migration's behavioural delta)
    // =============================================================================================

    /// <summary>
    /// TC-MDF-111 — the store's natural key includes the source, so the <b>same</b> period from a
    /// <b>different</b> source is a second fact (version 0), never a restatement of the first and
    /// never an idempotent swallow; and <c>version</c> stays the restatement sequence <b>within one
    /// source</b> — the incumbent's restatement takes version 1 while the public-site row stays 0.
    /// This is the behaviour the pinned read preference needs, and the one TKT-foundation-082's
    /// overlap skip deferred (TC-MDF-110's write-side rule is unchanged: the lane still withholds
    /// an overlapped fact and names it).
    /// </summary>
    [Fact]
    public async Task TC_MDF_111_the_store_keys_facts_by_source_so_two_sources_coexist_without_a_restatement()
    {
        await ResetAsync();

        var incumbent = Payload("UNSEC", "REV", 1_000m);
        var publicSite = Payload("UNSEC", "REV", 2_000m);

        await using (var db = _postgres.CreateContext())
        {
            var store = new FinancialStatementStore(db);

            var first = await store.UpsertAsync(Kap, RecordedAt, incumbent);
            Assert.Equal(1, first.Inserted);

            // The same period, a different source: a distinct fact — not a restatement of the
            // first, not a silent `unchanged`.
            var second = await store.UpsertAsync(KapPublic, RecordedAt, publicSite);
            Assert.Equal(1, second.Inserted);
            Assert.Empty(second.RejectedReasonCodes);

            // Re-sending an identical public-site payload is the idempotent no-op, within its
            // own source.
            var replayed = await store.UpsertAsync(KapPublic, RecordedAt, publicSite);
            Assert.Equal(0, replayed.Inserted);
            Assert.Equal(1, replayed.Unchanged);

            // A restatement is a sequence within one source's own filing series.
            var restatement = await store.UpsertAsync(
                Kap,
                RecordedAt,
                incumbent with
                {
                    Statements =
                    [
                        Fact("REV", 1_100m) with
                        {
                            Version = Restated,
                            RestatementDate = new DateOnly(2026, 9, 1),
                            PublishedAt = new DateOnly(2026, 9, 1),
                        },
                    ],
                });
            Assert.Equal(1, restatement.Inserted);
        }

        await using (var db = _postgres.CreateContext())
        {
            var unsecId = await InstrumentIdAsync(db, "UNSEC");
            var rows = await db.FinancialStatements
                .Where(statement => statement.InstrumentId == unsecId
                    && statement.PeriodType == "FY"
                    && statement.PeriodEndDate == FyCurrent
                    && statement.StatementType == "IS")
                .ToListAsync();

            // Two sources × (as-reported + the incumbent's one restatement).
            Assert.Equal(3, rows.Count);

            var publicRow = Assert.Single(rows, row => row.SourceRef == KapPublic);
            Assert.Equal(0, publicRow.Version);
            Assert.Null(publicRow.RestatementDate);

            var incumbentAsReported = Assert.Single(
                rows,
                row => row.SourceRef == Kap && row.Version == 0);
            Assert.Equal(Kap, incumbentAsReported.SourceRef);

            var incumbentRestated = Assert.Single(
                rows,
                row => row.SourceRef == Kap && row.Version == 1);
            Assert.Equal(new DateOnly(2026, 9, 1), incumbentRestated.RestatementDate);

            // The served read preference over the three rows still answers one statement: the
            // public-site row is preferred to the incumbent's newer restatement — the preference
            // is the source's, never the write order's or the version's.
            var lines = await db.FinLineItems
                .Where(line => rows.Select(row => row.Id).Contains(line.StatementId))
                .ToListAsync();
            var picked = StatementSourcePreference.Pick(rows, "IS");
            Assert.NotNull(picked);
            Assert.Equal(KapPublic, picked!.SourceRef);
            Assert.Equal(
                2_000m,
                lines.Single(line => line.StatementId == picked.Id && line.ItemCode == "REV").Value);
        }
    }

    /// <summary>
    /// TC-MDF-118 (b) — <b>the derived lanes describe the latest stored session, never a date
    /// that owns no prices</b> (<c>01</c> §10.9.13's stored-prices-as-truth rule; the gap that
    /// kept 083's open live criterion open). The 2026-10-26 live run asked
    /// <c>metrics-recompute</c> for a day whose universe had no stored prices and the job wrote
    /// <b>nothing</b>, reporting a green <c>written: 0, unchanged: 0</c> — indistinguishable
    /// from "everything was already current". The repair resolves the day through the one
    /// implementation of the rule (the snapshot lane's own
    /// <c>MarketSnapshotStore.ResolveLatestStoredTradingDayAsync</c>), so both
    /// <c>metrics-recompute</c> and <c>medians</c> asked for a priceless date compute the
    /// latest <b>real</b> session — and stamp their rows with that day, not the request's.
    /// <para>
    /// The world is the plain L2 fixture (no mixed-source statements), so the written count is
    /// the fixture contract's <see cref="MetricRows"/>; the requested date is a Saturday well
    /// after the last stored price, so a weekday-backing rule would have fabricated a day the
    /// market never traded and a blind <c>request.Date</c> would have written nothing.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TC_MDF_118_the_derived_lanes_describe_the_latest_stored_session_not_a_priceless_date()
    {
        await ResetAsync();

        DateOnly latestStored;
        await using (var db = _postgres.CreateContext())
        {
            latestStored = await db.DailyPrices.MaxAsync(price => price.PriceDate);
        }

        // A Saturday with no stored price anywhere in the universe — the cold-start / missed-run
        // shape the live run hit.
        var requested = new DateOnly(2027, 1, 2);
        Assert.Equal(DayOfWeek.Saturday, requested.DayOfWeek);
        Assert.True(requested > latestStored);
        await using (var db = _postgres.CreateContext())
        {
            Assert.False(await db.DailyPrices.AnyAsync(price => price.PriceDate == requested));
        }

        var metrics = await ComputeDerivedMetricsAsync(requested);
        Assert.Equal("succeeded", metrics.Status);

        // The run is not a silent zero: it recomputed the latest real session in full.
        Assert.Equal(MetricRows, metrics.Written);
        Assert.Equal(0, metrics.Unchanged);

        await using (var db = _postgres.CreateContext())
        {
            // Every written row describes the resolved session; the requested date owns none.
            Assert.Equal(0, await db.DerivedMetrics.CountAsync(metric => metric.AsOfDate == requested));
            Assert.Equal(
                MetricRows,
                await db.DerivedMetrics.CountAsync(metric => metric.AsOfDate == latestStored));
            Assert.All(
                await db.DerivedMetrics
                    .Where(metric => metric.AsOfDate == latestStored)
                    .Select(metric => metric.FormulaVersion)
                    .ToListAsync(),
                version => Assert.Equal(CanonicalMetrics.Version, version));
        }

        // The medians lane rides the same rule: it describes the same session.
        var medians = await RunMediansAsync(requested);
        Assert.Equal("succeeded", medians.Status);
        Assert.True(medians.Written > 0);

        await using (var db = _postgres.CreateContext())
        {
            Assert.Equal(
                0,
                await db.SectorMetricMedians.CountAsync(median => median.AsOfDate == requested));
            Assert.True(
                await db.SectorMetricMedians.AnyAsync(median => median.AsOfDate == latestStored));
        }
    }

    // =============================================================================================
    // Harness — the world
    // =============================================================================================

    /// <summary>The production ingestion composition over the Testcontainers database.</summary>
    private ServiceProvider BuildHost(TimeSpan? after = null)
    {
        var clock = new FakeTimeProvider(
            new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) + (after ?? TimeSpan.Zero));

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

    private static async Task<IngestResult> RunAsync(IServiceProvider provider, string jobCode, DateOnly date)
    {
        using var scope = provider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IIngestionJobRunner>();
        return await runner.RunAsync(jobCode, new IngestionRequest(date));
    }

    private async Task<IngestResult> ComputeDerivedMetricsAsync(DateOnly date, TimeSpan? after = null)
    {
        await using var host = BuildHost(after);
        return await RunAsync(host, MetricsRecomputeJob.Code, date);
    }

    private async Task<IngestResult> RunMediansAsync(DateOnly date, TimeSpan? after = null)
    {
        await using var host = BuildHost(after);
        return await RunAsync(host, SectorMediansJob.Code, date);
    }

    private async Task ResetAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);

        await db.Database.ExecuteSqlRawAsync("DELETE FROM derived_metrics");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM sector_metric_medians");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM market_snapshots");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM coverage_metadata");

        // The four mixed-world symbols' fixture statements are replaced by hand-seeded rows
        // (their line items cascade with them). EF-translated, so the symbol list is a bound
        // parameter — never string-built SQL.
        var instrumentIds = await db.Instruments
            .Where(instrument => MixedWorldSymbols.Contains(instrument.Symbol))
            .Select(instrument => instrument.Id)
            .ToListAsync();
        await db.FinancialStatements
            .Where(statement => instrumentIds.Contains(statement.InstrumentId))
            .ExecuteDeleteAsync();
    }

    /// <summary>
    /// The mixed world (test plan §23/§24): four symbols whose statements are hand-seeded so each
    /// behaviour has an instrument to be observed on. Stored canonical values, exactly the shape
    /// the FU fixture stores, so the metric goldens are hand-derived and never re-derived.
    /// </summary>
    private async Task SeedMixedWorldAsync()
    {
        await using var db = _postgres.CreateContext();

        // ALFA — both sources at FY2025, plus an incumbent-only FY2023.
        await SeedStatementAsync(
            db, "ALFA", Kap, FyIncipient, "IS",
            new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["REV"] = 900m,
                ["EBIT"] = 90m,
                ["NI"] = 45m,
            },
            publishedAt: new DateOnly(2024, 3, 1));
        await SeedStatementAsync(
            db, "ALFA", Kap, FyIncipient, "CF",
            new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["OCF"] = 60m,
                ["CAPEX"] = 20m,
            },
            publishedAt: new DateOnly(2024, 3, 1));
        await SeedStatementAsync(
            db, "ALFA", Kap, FyIncipient, "BS",
            new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["TOTAL_EQUITY"] = 500m,
                ["SHARES_DILUTED"] = 100m,
            },
            publishedAt: new DateOnly(2024, 3, 1));

        await SeedStatementAsync(
            db, "ALFA", Kap, FyCurrent, "IS",
            new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["REV"] = 1000m,
                ["EBIT"] = 100m,
                ["NI"] = 200m,
            },
            publishedAt: KapCurrentPublished);
        await SeedStatementAsync(
            db, "ALFA", Kap, FyCurrent, "CF",
            new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["OCF"] = 250m,
                ["CAPEX"] = 150m,
            },
            publishedAt: KapCurrentPublished);
        await SeedStatementAsync(
            db, "ALFA", Kap, FyCurrent, "BS",
            new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["TOTAL_EQUITY"] = 1000m,
                ["SHARES_DILUTED"] = 100m,
            },
            publishedAt: KapCurrentPublished);

        await SeedStatementAsync(
            db, "ALFA", KapPublic, FyCurrent, "IS",
            new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["REV"] = 2000m,
                ["EBIT"] = 400m,
                ["NI"] = 400m,
            },
            publishedAt: KapPublicCurrentPublished);
        await SeedStatementAsync(
            db, "ALFA", KapPublic, FyCurrent, "CF",
            new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["OCF"] = 300m,
                ["CAPEX"] = 100m,
            },
            publishedAt: KapPublicCurrentPublished);

        // No share count in the public-site balance sheet: the 065 Option-A posture this whole
        // class pins (a current filing without a count is honestly NULL, never a guess).
        await SeedStatementAsync(
            db, "ALFA", KapPublic, FyCurrent, "BS",
            new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["TOTAL_EQUITY"] = 2000m,
            },
            publishedAt: KapPublicCurrentPublished);

        // BETA — the incumbent only.
        await SeedStatementAsync(
            db, "BETA", Kap, FyCurrent, "IS",
            new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["REV"] = 500m,
                ["EBIT"] = 50m,
                ["NI"] = 200m,
            },
            publishedAt: KapCurrentPublished);
        await SeedStatementAsync(
            db, "BETA", Kap, FyCurrent, "CF",
            new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["OCF"] = 120m,
                ["CAPEX"] = 20m,
            },
            publishedAt: KapCurrentPublished);
        await SeedStatementAsync(
            db, "BETA", Kap, FyCurrent, "BS",
            new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["TOTAL_EQUITY"] = 800m,
                ["SHARES_DILUTED"] = 80m,
            },
            publishedAt: KapCurrentPublished);

        // GAMA — the public site only.
        await SeedStatementAsync(
            db, "GAMA", KapPublic, FyCurrent, "IS",
            new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["REV"] = 600m,
                ["EBIT"] = 180m,
                ["NI"] = 150m,
            },
            publishedAt: KapPublicCurrentPublished);
        await SeedStatementAsync(
            db, "GAMA", KapPublic, FyCurrent, "CF",
            new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["OCF"] = 90m,
                ["CAPEX"] = 30m,
            },
            publishedAt: KapPublicCurrentPublished);
        await SeedStatementAsync(
            db, "GAMA", KapPublic, FyCurrent, "BS",
            new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["TOTAL_EQUITY"] = 600m,
                ["SHARES_DILUTED"] = 60m,
            },
            publishedAt: KapPublicCurrentPublished);

        // UNSEC — the incumbent's 2023 facts only.
        await SeedStatementAsync(
            db, "UNSEC", Kap, FyIncipient, "IS",
            new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["REV"] = 300m,
                ["EBIT"] = 30m,
                ["NI"] = 30m,
            },
            publishedAt: new DateOnly(2024, 3, 15));
        await SeedStatementAsync(
            db, "UNSEC", Kap, FyIncipient, "CF",
            new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["OCF"] = 40m,
                ["CAPEX"] = 10m,
            },
            publishedAt: new DateOnly(2024, 3, 15));
        await SeedStatementAsync(
            db, "UNSEC", Kap, FyIncipient, "BS",
            new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["TOTAL_EQUITY"] = 200m,
                ["SHARES_DILUTED"] = 70m,
            },
            publishedAt: new DateOnly(2024, 3, 15));
    }

    /// <summary>Seeds one stored statement with its lines, under the given evidence class.</summary>
    private static async Task SeedStatementAsync(
        DegerliDbContext db,
        string symbol,
        string sourceRef,
        DateOnly periodEnd,
        string statementType,
        IReadOnlyDictionary<string, decimal> lines,
        DateOnly? publishedAt)
    {
        var instrumentId = await InstrumentIdAsync(db, symbol);
        var statement = new FinancialStatement
        {
            InstrumentId = instrumentId,
            PeriodType = "FY",
            PeriodEndDate = periodEnd,
            FiscalYear = periodEnd.Year,
            StatementType = statementType,
            Version = 0,
            PublishedAt = publishedAt,
            SourceUnitScale = "units",
            SourceCurrency = "TRY",
            SourceRef = sourceRef,
            RecordedAt = RecordedAt,
        };
        db.FinancialStatements.Add(statement);
        await db.SaveChangesAsync();

        foreach (var (itemCode, value) in lines)
        {
            db.FinLineItems.Add(new FinLineItem
            {
                StatementId = statement.Id,
                ItemCode = itemCode,
                Value = value,
            });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>One single-fact payload for the store-level coexistence case.</summary>
    private static StatementsPayload Payload(string symbol, string itemCode, decimal value) =>
        new(
            SourceRef: null,
            Symbol: symbol,
            UnitScale: "units",
            Currency: "TRY",
            Statements: [Fact(itemCode, value)]);

    private static StatementFact Fact(string itemCode, decimal value) =>
        new(
            PeriodType: "FY",
            PeriodEndDate: FyCurrent,
            FiscalYear: FyCurrent.Year,
            StatementType: "IS",
            Version: AsReported,
            RestatementDate: null,
            PublishedAt: KapCurrentPublished,
            Lines: new Dictionary<string, decimal>(StringComparer.Ordinal) { [itemCode] = value });

    // =============================================================================================
    // Harness — reads
    // =============================================================================================

    private static async Task<long> InstrumentIdAsync(DegerliDbContext db, string symbol) =>
        await db.Instruments
            .Where(instrument => instrument.Symbol == symbol)
            .Select(instrument => instrument.Id)
            .SingleAsync();

    /// <summary>One stored metric value for <paramref name="symbol"/> at <see cref="T"/>.</summary>
    private async Task<decimal?> ReadMetricAsync(string symbol, string metricCode)
    {
        await using var db = _postgres.CreateContext();
        var instrumentId = await InstrumentIdAsync(db, symbol);
        return await db.DerivedMetrics
            .Where(metric => metric.InstrumentId == instrumentId
                && metric.MetricCode == metricCode
                && metric.AsOfDate == T)
            .Select(metric => metric.Value)
            .SingleAsync();
    }

    private static async Task<CoverageMetadata?> ReadCoverageAsync(
        DegerliDbContext db,
        long instrumentId,
        string dataType) =>
        await db.CoverageMetadata.SingleOrDefaultAsync(row =>
            row.Scope == "instrument"
            && row.InstrumentId == instrumentId
            && row.DataType == dataType);

    private static async Task<List<JsonElement>> ReadFinancialsAsync(HttpClient client, string symbol)
    {
        var response = await client.GetAsync(new Uri($"/api/v1/stocks/{symbol}/financials", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Cloned: the document is disposed when this method returns, and the assertions read the
        // elements afterwards.
        return document.RootElement.GetProperty("periods").EnumerateArray()
            .Select(period => period.Clone())
            .ToList();
    }
}
