using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Core.Metrics;
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
/// TKT-mdf-008 acceptance — the Metrics Engine's canonical core (Group E):
/// TC-MDF-026 (ALFA 18-concept golden values), TC-MDF-031 (not-meaningful NULL matrix),
/// TC-MDF-033 (CAGR from available history + actual window) and TC-MDF-034 (idempotent,
/// append-only recompute). Every test seeds the FU §5/§5.1/§5.2 facts into a migrated
/// Testcontainers PostgreSQL and runs the real <c>metrics-recompute</c> job through the
/// TKT-mdf-002 runner — the same job code the scheduler and admin trigger use.
///
/// Goldens are hand-derived in FU §11.1/§11.2 (never re-derived from the implementation).
/// The FU table rounds each golden to 6 decimal places; we compare with an absolute
/// tolerance of 1e-5 (the FU's own rounding granularity) — e.g. FU's <c>fcf_cagr_5y</c>
/// −0.089708 vs the exact (100/160)^(1/5)−1 = −0.089718 (a hand-arithmetic slip in the
/// table, reported in the ticket's flagged items).
/// </summary>
public sealed class MetricsEngineTests : IClassFixture<PostgresFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    private readonly PostgresFixture _postgres;

    public MetricsEngineTests(PostgresFixture postgres) => _postgres = postgres;

    /// <summary>TC-MDF-026 — 18 canonical metric golden values on ALFA.</summary>
    [Fact]
    public async Task TC_MDF_026_alfa_canonical_metric_golden_values()
    {
        await ResetAsync();
        await using var host = BuildHost();

        var result = await RunMetricsAsync(host, T);

        Assert.Equal("succeeded", result.Status);
        Assert.Equal(16 * 26, result.Written);

        await using var db = _postgres.CreateContext();
        var alfa = await db.Instruments.Where(i => i.Symbol == "ALFA").Select(i => i.Id).SingleAsync();
        var rows = await db.DerivedMetrics
            .Where(m => m.InstrumentId == alfa && m.AsOfDate == T)
            .ToListAsync();

        // One row per (instrument, metric, date), all 26 codes.
        Assert.Equal(26, rows.Count);
        Assert.Equal(26, rows.Select(r => r.MetricCode).Distinct().Count());
        Assert.All(rows, r => Assert.Equal(T, r.AsOfDate));
        var byCode = rows.ToDictionary(r => r.MetricCode, r => r, StringComparer.Ordinal);

        // FU §11.1 — the 14 non-growth concepts.
        AssertValue(byCode, MetricCodes.Pe, 10.0m);
        AssertValue(byCode, MetricCodes.Pb, 2.0m);
        AssertValue(byCode, MetricCodes.EvEbitda, 5.5m);
        AssertValue(byCode, MetricCodes.EvFcf, 22.0m);
        AssertValue(byCode, MetricCodes.FcfYield, 0.05m);
        AssertValue(byCode, MetricCodes.Roic, 0.20m);
        AssertValue(byCode, MetricCodes.Roe, 0.20m);
        AssertValue(byCode, MetricCodes.GrossMargin, 0.40m);
        AssertValue(byCode, MetricCodes.OperatingMargin, 0.30m);
        AssertValue(byCode, MetricCodes.NetDebtEbitda, 0.5m);
        AssertValue(byCode, MetricCodes.InterestCoverage, 6.0m);
        AssertValue(byCode, MetricCodes.CurrentRatio, 2.0m);
        AssertValue(byCode, MetricCodes.DivYield, 0.05m);
        AssertValue(byCode, MetricCodes.PayoutRatio, 0.50m);

        // FU §11.1 — the 4 growth concepts × 3 windows.
        AssertValue(byCode, MetricCodes.RevCagr3y, 0.160397m, window: 3);
        AssertValue(byCode, MetricCodes.RevCagr5y, 0.201124m, window: 5);
        AssertValue(byCode, MetricCodes.RevCagr10y, 0.174619m, window: 10);
        AssertValue(byCode, MetricCodes.EpsCagr3y, 0.035744m, window: 3);
        AssertValue(byCode, MetricCodes.EpsCagr5y, 0.059224m, window: 5);
        AssertValue(byCode, MetricCodes.EpsCagr10y, 0.071773m, window: 10);
        AssertValue(byCode, MetricCodes.FcfCagr3y, -0.206300m, window: 3);
        AssertValue(byCode, MetricCodes.FcfCagr5y, -0.089708m, window: 5);
        AssertValue(byCode, MetricCodes.FcfCagr10y, 0.022565m, window: 10);
        AssertValue(byCode, MetricCodes.DivCagr3y, 0.035744m, window: 3);
        AssertValue(byCode, MetricCodes.DivCagr5y, 0.045640m, window: 5);
        AssertValue(byCode, MetricCodes.DivCagr10y, 0.052409m, window: 10);

        // The non-growth concepts carry no window; ALFA is not restated / not adjusted.
        Assert.All(rows.Where(r => !MetricCodes.IsGrowth(r.MetricCode)), r => Assert.Null(r.WindowYears));
        Assert.All(rows, r => Assert.False(r.IsRested));
        Assert.All(rows, r => Assert.False(r.IsAdjusted));
    }

    /// <summary>TC-MDF-031 — not-meaningful rules produce NULL, never zero.</summary>
    [Fact]
    public async Task TC_MDF_031_not_meaningful_rules_produce_null_never_zero()
    {
        await ResetAsync();
        await using var host = BuildHost();

        var result = await RunMetricsAsync(host, T);
        Assert.Equal("succeeded", result.Status);

        await using var db = _postgres.CreateContext();
        var metrics = await LoadMetricsAsync(db);

        // BETA, GAMA — NI ≤ 0 → pe, payout_ratio NULL.
        foreach (var loss in new[] { "BETA", "GAMA" })
        {
            AssertNull(metrics, loss, MetricCodes.Pe);
            AssertNull(metrics, loss, MetricCodes.PayoutRatio);
        }

        // DELTA — equity ≤ 0 → pb, roe NULL; IC = 100 − 200 − 50 ≤ 0 → roic NULL.
        AssertNull(metrics, "DELTA", MetricCodes.Pb);
        AssertNull(metrics, "DELTA", MetricCodes.Roe);
        AssertNull(metrics, "DELTA", MetricCodes.Roic);

        // IOTA — EBITDA ≤ 0 → ev_ebitda, net_debt_ebitda NULL.
        AssertNull(metrics, "IOTA", MetricCodes.EvEbitda);
        AssertNull(metrics, "IOTA", MetricCodes.NetDebtEbitda);

        // KAPPA — FCF ≤ 0 → ev_fcf, fcf_yield NULL; FY sign change → fcf_cagr NULL.
        AssertNull(metrics, "KAPPA", MetricCodes.EvFcf);
        AssertNull(metrics, "KAPPA", MetricCodes.FcfYield);
        AssertNull(metrics, "KAPPA", MetricCodes.FcfCagr3y);
        AssertNull(metrics, "KAPPA", MetricCodes.FcfCagr5y);
        AssertNull(metrics, "KAPPA", MetricCodes.FcfCagr10y);

        // PART — no CF statement → FCF not computable → all FCF metrics NULL.
        AssertNull(metrics, "PART", MetricCodes.EvFcf);
        AssertNull(metrics, "PART", MetricCodes.FcfYield);
        AssertNull(metrics, "PART", MetricCodes.FcfCagr3y);
        AssertNull(metrics, "PART", MetricCodes.FcfCagr5y);
        AssertNull(metrics, "PART", MetricCodes.FcfCagr10y);

        // LAMDA — never paid → div_yield, div_cagr NULL.
        AssertNull(metrics, "LAMDA", MetricCodes.DivYield);
        AssertNull(metrics, "LAMDA", MetricCodes.DivCagr3y);
        AssertNull(metrics, "LAMDA", MetricCodes.DivCagr5y);
        AssertNull(metrics, "LAMDA", MetricCodes.DivCagr10y);

        // NEWP — one FY → every CAGR variant NULL.
        foreach (var window in MetricCodes.All.Where(MetricCodes.IsGrowth))
        {
            AssertNull(metrics, "NEWP", window);
        }

        // NU — payout is honest: > 1, not NULL.
        var nuPayout = metrics["NU"][MetricCodes.PayoutRatio];
        Assert.NotNull(nuPayout.Value);
        Assert.True(nuPayout.Value!.Value > 1m);
        Assert.Equal(1.5m, nuPayout.Value!.Value, 3);

        // ZETA — CAGRs computed over 1 interval, window_years = 1.
        AssertValue(metrics, "ZETA", MetricCodes.RevCagr3y, 0.25m, window: 1);
        AssertValue(metrics, "ZETA", MetricCodes.RevCagr5y, 0.25m, window: 1);
        AssertValue(metrics, "ZETA", MetricCodes.RevCagr10y, 0.25m, window: 1);
    }

    /// <summary>TC-MDF-033 — CAGR from available history with the actual window.</summary>
    [Fact]
    public async Task TC_MDF_033_cagr_uses_available_history_and_reports_actual_window()
    {
        await ResetAsync();
        await using var host = BuildHost();

        var result = await RunMetricsAsync(host, T);
        Assert.Equal("succeeded", result.Status);

        await using var db = _postgres.CreateContext();
        var metrics = await LoadMetricsAsync(db);

        // ZETA (2 FYs): nominal 3/5/10 windows all fall back to the 1 available interval.
        AssertValue(metrics, "ZETA", MetricCodes.RevCagr3y, 0.25m, window: 1);
        AssertValue(metrics, "ZETA", MetricCodes.RevCagr5y, 0.25m, window: 1);
        AssertValue(metrics, "ZETA", MetricCodes.RevCagr10y, 0.25m, window: 1);

        // ALFA (11 FYs): the full windows are used unchanged.
        AssertValue(metrics, "ALFA", MetricCodes.RevCagr3y, 0.160397m, window: 3);
        AssertValue(metrics, "ALFA", MetricCodes.RevCagr5y, 0.201124m, window: 5);
        AssertValue(metrics, "ALFA", MetricCodes.RevCagr10y, 0.174619m, window: 10);

        // NEWP (1 FY): all CAGRs NULL, and the honest no-data state names the coverage
        // boundary — the instrument's listing date (FU §3: 2025-08-01).
        foreach (var code in MetricCodes.All.Where(MetricCodes.IsGrowth))
        {
            AssertNull(metrics, "NEWP", code);
        }

        using var scope = host.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<MetricsEngine>();
        var computed = await engine.ComputeAsync(T);
        var newp = computed.Single(c => c.Symbol == "NEWP");
        Assert.Equal(new DateOnly(2025, 8, 1), newp.ListingDate);
        Assert.All(
            newp.Metrics.Where(m => MetricCodes.IsGrowth(m.Code)),
            m => Assert.Null(m.Value));
    }

    /// <summary>TC-MDF-034 — metrics recompute is idempotent and append-only.</summary>
    [Fact]
    public async Task TC_MDF_034_metrics_recompute_is_idempotent_and_append_only()
    {
        await ResetAsync();
        await using var host = BuildHost();

        var first = await RunMetricsAsync(host, T);
        Assert.Equal(16 * 26, first.Written);

        // A prior trading day is computed and must remain untouched by later runs.
        var prior = T.AddDays(-1);
        var priorRun = await RunMetricsAsync(host, prior);
        Assert.Equal(16 * 26, priorRun.Written);
        var priorSnapshot = await SnapshotAsync(prior);

        // Second run for the same date: no new rows, no duplicates.
        var second = await RunMetricsAsync(host, T);
        Assert.Equal(0, second.Written);
        Assert.Equal(16 * 26, second.Unchanged);

        await using var db = _postgres.CreateContext();
        Assert.Equal(16 * 26, await db.DerivedMetrics.CountAsync(m => m.AsOfDate == T));
        Assert.Equal(16 * 26 * 2, await db.DerivedMetrics.CountAsync());

        var duplicates = await db.DerivedMetrics
            .GroupBy(m => new { m.InstrumentId, m.MetricCode, m.AsOfDate })
            .Where(g => g.Count() > 1)
            .CountAsync();
        Assert.Equal(0, duplicates);

        // Prior date's rows are byte-for-byte untouched (append-only, NFR-MDF-002).
        var afterSnapshot = await SnapshotAsync(prior);
        Assert.Equal(priorSnapshot.Count, afterSnapshot.Count);
        Assert.All(priorSnapshot, pair => Assert.Equal(pair.Value, afterSnapshot[pair.Key]));
    }

    /// <summary>TC-MDF-058 — corporate-action-adjusted per-share CAGRs.</summary>
    [Fact]
    public async Task TC_MDF_058_corporate_action_adjusted_per_share_cagrs()
    {
        await ResetAsync();
        await using var host = BuildHost();

        var result = await RunMetricsAsync(host, T);
        Assert.Equal("succeeded", result.Status);

        await using var db = _postgres.CreateContext();
        var metrics = await LoadMetricsAsync(db);
        var epsl = metrics["EPSL"];

        // FU §5.3 — eps_cagr_3y / div_cagr_3y on the corporate-action-adjusted series.
        AssertValue(epsl, MetricCodes.EpsCagr3y, 0.062659m, window: 3);
        AssertValue(epsl, MetricCodes.DivCagr3y, 0.062659m, window: 3);

        // 4 FYs → only the 3Y window has enough compounding intervals.
        AssertNull(metrics, "EPSL", MetricCodes.EpsCagr5y);
        AssertNull(metrics, "EPSL", MetricCodes.EpsCagr10y);
        AssertNull(metrics, "EPSL", MetricCodes.DivCagr5y);
        AssertNull(metrics, "EPSL", MetricCodes.DivCagr10y);

        // Both computed CAGRs carry the corporate-action basis marker (FR-MDF-018).
        Assert.True(epsl[MetricCodes.EpsCagr3y].IsAdjusted);
        Assert.True(epsl[MetricCodes.DivCagr3y].IsAdjusted);

        // Negative guard: the unadjusted (0.75/3.125)^(1/3) − 1 = −0.378553 must not appear.
        Assert.True(
            Math.Abs(epsl[MetricCodes.EpsCagr3y].Value!.Value - (-0.378553m)) > 0.00001m,
            "the split-contaminated unadjusted CAGR was served");

        // The basis marker is per metric: non-per-share metrics stay unadjusted.
        Assert.False(epsl[MetricCodes.Pe].IsAdjusted);
        Assert.False(epsl[MetricCodes.RevCagr3y].IsAdjusted);
        Assert.False(epsl[MetricCodes.FcfCagr3y].IsAdjusted);
    }

    /// <summary>TC-MDF-059 — OCF-based FCF and the KAP sign convention.</summary>
    [Fact]
    public async Task TC_MDF_059_ocf_based_fcf_and_the_kap_sign_convention()
    {
        await ResetAsync();
        await using var host = BuildHost();

        // (a) ALFA FY2025 with the reported OCF 250 and CAPEX 150 (FU §5): FCF = 250 − |150|
        // = 100, so the EV/FCF-dependent §11.1 goldens are unchanged (ev_fcf 2200/100 = 22,
        // fcf_yield 100/2000 = 0.05).
        var first = await RunMetricsAsync(host, T);
        Assert.Equal("succeeded", first.Status);

        await using (var db = _postgres.CreateContext())
        {
            var metrics = await LoadMetricsAsync(db);
            AssertValue(metrics, "ALFA", MetricCodes.EvFcf, 22.0m);
            AssertValue(metrics, "ALFA", MetricCodes.FcfYield, 0.05m);

            // The seeded CF statement carries the authored OCF line (OCF = FCF + CAPEX).
            var fyLines = await FyCashFlowLinesAsync(db, "ALFA");
            Assert.Equal(250m, fyLines["OCF"]);
            Assert.Equal(150m, fyLines["CAPEX"]);
        }

        // (b) The sign-convention payload (FU §5): the same facts as the source reports
        // them — CAPEX −150 alongside OCF 250. |−150| is subtracted, so FCF stays 100.
        CannedSourcePayload signConvention = CannedSourceCatalog.Get(L2, CannedSourceCatalog.StatementsKapSignConvention);
        using (var document = JsonDocument.Parse(signConvention.Body))
        {
            var cf = document.RootElement.GetProperty("statements").EnumerateArray()
                .Single(s => s.GetProperty("statementType").GetString() == "CF"
                    && s.GetProperty("periodType").GetString() == "FY");
            Assert.Equal(-150m, cf.GetProperty("lines").GetProperty("CAPEX").GetDecimal());
            Assert.Equal(250m, cf.GetProperty("lines").GetProperty("OCF").GetDecimal());
        }

        await using (var db = _postgres.CreateContext())
        {
            await SetFyCapexAsync(db, "ALFA", -150m);
        }

        var second = await RunMetricsAsync(host, T);
        Assert.Equal("succeeded", second.Status);

        await using (var db = _postgres.CreateContext())
        {
            var metrics = await LoadMetricsAsync(db);
            // A naive OCF − CAPEX would give 400 (ev_fcf 5.5, fcf_yield 0.2); the canonical
            // absolute-value rule keeps the goldens at 22 and 0.05.
            AssertValue(metrics, "ALFA", MetricCodes.EvFcf, 22.0m);
            AssertValue(metrics, "ALFA", MetricCodes.FcfYield, 0.05m);
            Assert.True(
                Math.Abs(metrics["ALFA"][MetricCodes.EvFcf].Value!.Value - 5.5m) > 0.00001m,
                "the sign-unaware FCF (OCF − CAPEX = 400) was served");

            // (c) PART has no CF statement → no OCF → every FCF metric is honestly NULL.
            AssertNull(metrics, "PART", MetricCodes.EvFcf);
            AssertNull(metrics, "PART", MetricCodes.FcfYield);
            AssertNull(metrics, "PART", MetricCodes.FcfCagr3y);
            AssertNull(metrics, "PART", MetricCodes.FcfCagr5y);
            AssertNull(metrics, "PART", MetricCodes.FcfCagr10y);
        }
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    /// <summary>The ingestion composition retargeted at the Testcontainers database.</summary>
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

    private static async Task<IngestResult> RunMetricsAsync(IServiceProvider provider, DateOnly date)
    {
        using var scope = provider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IIngestionJobRunner>();
        return await runner.RunAsync(MetricsRecomputeJob.Code, new IngestionRequest(date));
    }

    private async Task ResetAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM derived_metrics");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
    }

    /// <summary>The serving FY cash-flow statement's lines for one instrument.</summary>
    private static async Task<Dictionary<string, decimal>> FyCashFlowLinesAsync(DegerliDbContext db, string symbol)
    {
        var statementId = await FyCashFlowStatementIdAsync(db, symbol);
        return await db.FinLineItems
            .Where(l => l.StatementId == statementId)
            .ToDictionaryAsync(l => l.ItemCode, l => l.Value, StringComparer.Ordinal);
    }

    /// <summary>Applies the KAP sign convention to the serving FY CAPEX line (as-reported negative).</summary>
    private static async Task SetFyCapexAsync(DegerliDbContext db, string symbol, decimal capex)
    {
        var statementId = await FyCashFlowStatementIdAsync(db, symbol);
        var line = await db.FinLineItems
            .SingleAsync(l => l.StatementId == statementId && l.ItemCode == "CAPEX");
        line.Value = capex;
        await db.SaveChangesAsync();
    }

    private static async Task<long> FyCashFlowStatementIdAsync(DegerliDbContext db, string symbol) =>
        await (
            from s in db.FinancialStatements
            join i in db.Instruments on s.InstrumentId equals i.Id
            where i.Symbol == symbol && s.PeriodType == "FY" && s.StatementType == "CF"
            orderby s.PeriodEndDate descending, s.Version descending
            select s.Id)
        .FirstAsync();

    private static async Task<Dictionary<string, Dictionary<string, DerivedMetric>>> LoadMetricsAsync(DegerliDbContext db)
    {
        var symbols = await db.Instruments.ToDictionaryAsync(i => i.Id, i => i.Symbol);
        var rows = await db.DerivedMetrics.Where(m => m.AsOfDate == T).ToListAsync();
        return rows
            .GroupBy(r => symbols[r.InstrumentId])
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(r => r.MetricCode, r => r, StringComparer.Ordinal),
                StringComparer.Ordinal);
    }

    private async Task<Dictionary<string, decimal?>> SnapshotAsync(DateOnly date)
    {
        await using var db = _postgres.CreateContext();
        var symbols = await db.Instruments.ToDictionaryAsync(i => i.Id, i => i.Symbol);
        var rows = await db.DerivedMetrics.Where(m => m.AsOfDate == date).ToListAsync();
        return rows.ToDictionary(
            r => $"{symbols[r.InstrumentId]}|{r.MetricCode}",
            r => r.Value,
            StringComparer.Ordinal);
    }

    private static void AssertValue(
        IReadOnlyDictionary<string, DerivedMetric> rows,
        string code,
        decimal expected,
        int? window = null)
    {
        var row = rows[code];
        Assert.NotNull(row.Value);
        // FU §11.1 quotes each golden to 6 decimal places; compare with that granularity
        // (the FU's own last-digit rounding is at most 1e-5 — e.g. fcf_cagr_5y).
        Assert.True(
            Math.Abs(row.Value!.Value - expected) <= 0.00001m,
            $"{code}: expected {expected} (±1e-5) but was {row.Value.Value}");
        Assert.Equal(window, row.WindowYears);
    }

    private static void AssertValue(
        IReadOnlyDictionary<string, Dictionary<string, DerivedMetric>> metrics,
        string symbol,
        string code,
        decimal expected,
        int? window = null) =>
        AssertValue(metrics[symbol], code, expected, window);

    private static void AssertNull(
        IReadOnlyDictionary<string, Dictionary<string, DerivedMetric>> metrics,
        string symbol,
        string code)
    {
        var row = metrics[symbol][code];
        Assert.Null(row.Value);
        Assert.Null(row.WindowYears);
    }
}
