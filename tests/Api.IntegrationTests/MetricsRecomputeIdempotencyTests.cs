using System.Text.Json;
using Degerli.Core.Metrics;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Metrics;
using Degerli.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-foundation-050 acceptance — <b>TC-MDF-088</b>, the input-aware same-day recompute for
/// the two derived-fact jobs, extending the TKT-foundation-042 pattern (<c>01</c> §10.9.13)
/// from <c>market-snapshot</c> to <c>metrics-recompute</c> and <c>medians</c>.
/// <para>
/// The pre-ticket write path treated a same-day re-run as an unconditional in-place
/// overwrite: identical inputs reported the honest-looking but blind <c>unchanged</c> count
/// while the rows were rewritten anyway, and a re-run over <i>changed</i> upstream facts
/// reported <c>written: 0</c> — the repair was invisible in the run's own stats (the
/// <c>0/0</c> silence class 042 fixed for the snapshot). Both jobs are driven through the
/// real runner (the job codes the scheduler and the admin trigger use) over the FU §5
/// canonical fixture universe in a migrated Testcontainers PostgreSQL — doubles only, no
/// live source.
/// </para>
/// </summary>
public sealed class MetricsRecomputeIdempotencyTests : IClassFixture<PostgresFixture>
{
    /// <summary>The FU universe × the 26 canonical metric codes (TC-MDF-026's written count).</summary>
    private const int MetricRows = 16 * 26;

    /// <summary>The FU's three classified sectors × the five valuation-family metrics.</summary>
    private const int MedianRows = 3 * 5;

    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);

    private static DateOnly T => L2.Anchor.T;

    private static DateOnly Prior => L2.Anchor.TradingDay(-1);

    private readonly PostgresFixture _postgres;

    public MetricsRecomputeIdempotencyTests(PostgresFixture postgres) => _postgres = postgres;

    // =====================================================================================
    // TC-MDF-088 (i) — the identical same-day re-run is an honest `unchanged` count
    // =====================================================================================

    /// <summary>
    /// TC-MDF-088 — <c>metrics-recompute</c>: a value-identical same-day re-run reports
    /// <c>written: 0, unchanged: N</c> (never a silent <c>0/0</c>), leaves every row — including
    /// its <c>computed_at</c> — untouched, keeps exactly one row per
    /// <c>(instrument, metric, as_of_date)</c>, and never touches a historical day.
    /// </summary>
    [Fact]
    public async Task TC_MDF_088_metrics_recompute_identical_same_day_rerun_reports_unchanged()
    {
        await ResetAsync();

        // A completed earlier day: it must be immutable throughout.
        IngestResult priorRun;
        await using (var host = BuildHost())
        {
            priorRun = await RunAsync(host, MetricsRecomputeJob.Code, Prior);
        }

        Assert.Equal("succeeded", priorRun.Status);
        Assert.Equal(MetricRows, priorRun.Written);
        var priorBefore = await ReadMetricsAsync(Prior);

        IngestResult first;
        await using (var host = BuildHost())
        {
            first = await RunAsync(host, MetricsRecomputeJob.Code, T);
        }

        Assert.Equal("succeeded", first.Status);
        Assert.Equal(MetricRows, first.Written);
        Assert.Equal(0, first.Unchanged);
        var before = await ReadMetricsAsync(T);
        Assert.Equal(MetricRows, before.Count);

        // A later clock instant, so "nothing was written" is observable — a blind overwrite
        // would move computed_at forward.
        IngestResult second;
        await using (var host = BuildHost(TimeSpan.FromHours(2)))
        {
            second = await RunAsync(host, MetricsRecomputeJob.Code, T);
        }

        Assert.Equal("succeeded", second.Status);
        Assert.Equal(0, second.Written);
        Assert.Equal(MetricRows, second.Unchanged);

        // The honest count reaches the run ledger too (the runner serialises the run stats).
        Assert.Equal(0, await ReadLatestLedgerStatAsync(MetricsRecomputeJob.Code, "written"));
        Assert.Equal(MetricRows, await ReadLatestLedgerStatAsync(MetricsRecomputeJob.Code, "unchanged"));

        var after = await ReadMetricsAsync(T);
        Assert.Equal(before, after); // values, windows, markers and computed_at all untouched

        await using (var db = _postgres.CreateContext())
        {
            // Exactly one row per (instrument, metric, as_of_date) — never an append.
            Assert.Equal(MetricRows, await db.DerivedMetrics.CountAsync(m => m.AsOfDate == T));
            Assert.Equal(MetricRows * 2, await db.DerivedMetrics.CountAsync());
            Assert.Equal(
                0,
                await db.DerivedMetrics
                    .GroupBy(m => new { m.InstrumentId, m.MetricCode, m.AsOfDate })
                    .Where(g => g.Count() > 1)
                    .CountAsync());
        }

        // The historical day is byte-identical (values *and* computed_at).
        Assert.Equal(priorBefore, await ReadMetricsAsync(Prior));
    }

    /// <summary>
    /// TC-MDF-088 — <c>medians</c>: the same contract for the sector-median lane — an
    /// identical same-day re-run reports <c>unchanged: N</c>, writes nothing, keeps one row
    /// per <c>(sector, metric, as_of_date)</c> and leaves the historical day alone.
    /// </summary>
    [Fact]
    public async Task TC_MDF_088_medians_identical_same_day_rerun_reports_unchanged()
    {
        await ResetAsync();
        await ComputeDerivedMetricsAsync(Prior);
        await ComputeDerivedMetricsAsync(T);
        await RunMediansAsync(Prior);

        var priorBefore = await ReadMediansAsync(Prior);

        var first = await RunMediansAsync(T);
        Assert.Equal("succeeded", first.Status);
        Assert.Equal(MedianRows, first.Written);
        Assert.Equal(0, first.Unchanged);
        var before = await ReadMediansAsync(T);
        Assert.Equal(MedianRows, before.Count);

        var second = await RunMediansAsync(T, TimeSpan.FromHours(2));
        Assert.Equal("succeeded", second.Status);
        Assert.Equal(0, second.Written);
        Assert.Equal(MedianRows, second.Unchanged);

        Assert.Equal(0, await ReadLatestLedgerStatAsync(SectorMediansJob.Code, "written"));
        Assert.Equal(MedianRows, await ReadLatestLedgerStatAsync(SectorMediansJob.Code, "unchanged"));

        Assert.Equal(before, await ReadMediansAsync(T));

        await using (var db = _postgres.CreateContext())
        {
            Assert.Equal(MedianRows, await db.SectorMetricMedians.CountAsync(m => m.AsOfDate == T));
            Assert.Equal(
                0,
                await db.SectorMetricMedians
                    .GroupBy(m => new { m.SectorId, m.MetricCode, m.AsOfDate })
                    .Where(g => g.Count() > 1)
                    .CountAsync());
        }

        Assert.Equal(priorBefore, await ReadMediansAsync(Prior));
    }

    // =====================================================================================
    // TC-MDF-088 (ii) — changed inputs repair the row in place and the run counts it
    // =====================================================================================

    /// <summary>
    /// TC-MDF-088 — <c>metrics-recompute</c>: when an upstream canonical fact changes after the
    /// day was computed, the same-day re-run overwrites the affected rows in place and counts
    /// the repair as written (pre-ticket: <c>written: 0, unchanged: 416</c> — a repair reported
    /// as "nothing changed" while the rows were rewritten). The rows the changed fact does not
    /// reach stay untouched (their <c>computed_at</c> does not move), one row per key is
    /// preserved, and the historical day stays byte-identical.
    /// </summary>
    [Fact]
    public async Task TC_MDF_088_metrics_recompute_changed_inputs_repair_in_place_and_are_counted()
    {
        await ResetAsync();
        await ComputeDerivedMetricsAsync(Prior);
        var priorBefore = await ReadMetricsAsync(Prior);

        var first = await ComputeDerivedMetricsAsync(T);
        Assert.Equal(MetricRows, first.Written);
        var before = await ReadMetricsAsync(T);
        Assert.Equal(MetricRows, before.Count);

        // An upstream canonical fact changes: ALFA's serving FY permanent CAPEX line
        // (FU §5: OCF 250, CAPEX 150 → FCF 100). CAPEX 150 → 100 raises FCF to 150, so the
        // FCF-family rows are no longer the computed values for the current facts.
        await SetAlfaFyCapexAsync(100m);

        var repair = await ComputeDerivedMetricsAsync(T, TimeSpan.FromHours(2));
        Assert.Equal("succeeded", repair.Status);

        var after = await ReadMetricsAsync(T);

        // Change detection is over the input-derived values only — `computed_at` stamps the
        // write, not the facts (a blind overwrite would otherwise look like a value change).
        var changed = before
            .Where(pair => pair.Value.Facts() != after[pair.Key].Facts())
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.Ordinal);

        // The repair is a real write count, not the pre-ticket silent 0/0: exactly the rows
        // whose stored values no longer match the recompute are counted, the rest are honestly
        // reported unchanged.
        Assert.NotEmpty(changed);
        Assert.Equal(changed.Count, repair.Written);
        Assert.Equal(MetricRows - changed.Count, repair.Unchanged);
        Assert.Equal(repair.Written, await ReadLatestLedgerStatAsync(MetricsRecomputeJob.Code, "written"));
        Assert.Equal(repair.Unchanged, await ReadLatestLedgerStatAsync(MetricsRecomputeJob.Code, "unchanged"));

        // The FCF family is what the changed CAPEX line reaches (ev_fcf / fcf_yield / the three
        // FCF CAGRs); the repair wrote them and nothing appended.
        Assert.All(
            new[]
            {
                $"ALFA|{MetricCodes.EvFcf}",
                $"ALFA|{MetricCodes.FcfYield}",
                $"ALFA|{MetricCodes.FcfCagr3y}",
                $"ALFA|{MetricCodes.FcfCagr5y}",
                $"ALFA|{MetricCodes.FcfCagr10y}",
            },
            key => Assert.Contains(key, changed));

        // The repaired values are the current-fact values: EV 2200 / FCF 150, and
        // FCF 150 / market cap 2000.
        Assert.Equal(2200m / 150m, after[$"ALFA|{MetricCodes.EvFcf}"].Value!.Value, 6);
        Assert.Equal(0.075m, after[$"ALFA|{MetricCodes.FcfYield}"].Value!.Value, 6);

        // Only the repaired rows carry the later clock: the untouched rows were never rewritten.
        Assert.All(
            after.Where(pair => !changed.Contains(pair.Key)),
            pair => Assert.Equal(before[pair.Key].ComputedAt, pair.Value.ComputedAt));
        Assert.All(
            changed,
            key => Assert.Equal(
                new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddHours(2),
                after[key].ComputedAt));

        await using (var db = _postgres.CreateContext())
        {
            // In-place overwrite: still exactly one row per (instrument, metric, as_of_date).
            Assert.Equal(MetricRows, await db.DerivedMetrics.CountAsync(m => m.AsOfDate == T));
            Assert.Equal(
                0,
                await db.DerivedMetrics
                    .GroupBy(m => new { m.InstrumentId, m.MetricCode, m.AsOfDate })
                    .Where(g => g.Count() > 1)
                    .CountAsync());
        }

        // Historical days are immutable — the repair addressed only the recomputed day.
        Assert.Equal(priorBefore, await ReadMetricsAsync(Prior));
    }

    /// <summary>
    /// TC-MDF-088 — <c>medians</c>: a same-day re-run after an upstream <c>derived_metrics</c>
    /// row changed (the median lane's input) overwrites the affected median row in place and
    /// counts the repair, leaving the rest and the historical day untouched.
    /// </summary>
    [Fact]
    public async Task TC_MDF_088_medians_changed_inputs_repair_in_place_and_are_counted()
    {
        await ResetAsync();
        await ComputeDerivedMetricsAsync(Prior);
        await ComputeDerivedMetricsAsync(T);
        await RunMediansAsync(Prior);
        var priorBefore = await ReadMediansAsync(Prior);

        var first = await RunMediansAsync(T);
        Assert.Equal(MedianRows, first.Written);
        var before = await ReadMediansAsync(T);
        Assert.Equal(MedianRows, before.Count);

        // An upstream fact changes: ALFA's stored P/E is corrected from 10 to 20 (sector A's
        // meaningful P/E peer set is ALFA 10, REST 10, DELTA 20 — BETA/GAMA are loss-makers,
        // excluded; FU §5), so its sorted set moves from [10, 10, 20] to [10, 20, 20] and the
        // median moves from 10 to 20 while the peer/exclusion counts stay put.
        var alphaSectorPeBefore = await ReadMedianAsync(T, "A", MetricCodes.Pe);
        Assert.Equal(10m, alphaSectorPeBefore.MedianValue);
        await SetDerivedMetricValueAsync("ALFA", MetricCodes.Pe, 20m);

        var repair = await RunMediansAsync(T, TimeSpan.FromHours(2));
        Assert.Equal("succeeded", repair.Status);
        Assert.Equal(1, repair.Written);
        Assert.Equal(MedianRows - 1, repair.Unchanged);
        Assert.Equal(1, await ReadLatestLedgerStatAsync(SectorMediansJob.Code, "written"));
        Assert.Equal(MedianRows - 1, await ReadLatestLedgerStatAsync(SectorMediansJob.Code, "unchanged"));

        var repaired = await ReadMedianAsync(T, "A", MetricCodes.Pe);
        Assert.Equal(20m, repaired.MedianValue);
        Assert.Equal(alphaSectorPeBefore.PeerCount, repaired.PeerCount);
        Assert.Equal(alphaSectorPeBefore.ExcludedCount, repaired.ExcludedCount);
        Assert.Equal(
            new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddHours(2),
            repaired.ComputedAt);

        // Every other median row is untouched (a re-run over unchanged inputs writes nothing).
        var after = await ReadMediansAsync(T);
        Assert.All(
            after.Where(pair => pair.Key != $"A|{MetricCodes.Pe}"),
            pair => Assert.Equal(before[pair.Key], pair.Value));

        await using (var db = _postgres.CreateContext())
        {
            Assert.Equal(MedianRows, await db.SectorMetricMedians.CountAsync(m => m.AsOfDate == T));
            Assert.Equal(
                0,
                await db.SectorMetricMedians
                    .GroupBy(m => new { m.SectorId, m.MetricCode, m.AsOfDate })
                    .Where(g => g.Count() > 1)
                    .CountAsync());
        }

        Assert.Equal(priorBefore, await ReadMediansAsync(Prior));
    }

    // =====================================================================================
    // TC-MDF-102 — definition versioning: per-row formula provenance (TKT-foundation-070)
    // =====================================================================================

    /// <summary>
    /// TC-MDF-102 — <c>metrics-recompute</c>: every row the engine writes carries the current
    /// definition version (<see cref="CanonicalMetrics.Version"/>) — the single source of truth
    /// for "which formula produced this number". A fresh day is entirely current, so a
    /// value-identical re-run stays honestly <c>unchanged</c> (no perpetual pseudo-repair).
    /// </summary>
    [Fact]
    public async Task TC_MDF_102_metrics_recompute_stamps_the_definition_version_on_every_row()
    {
        await ResetAsync();

        var first = await ComputeDerivedMetricsAsync(T);
        Assert.Equal("succeeded", first.Status);
        Assert.Equal(MetricRows, first.Written);

        // The stamp is the definition's own constant — not a literal the store invents.
        var versions = await ReadMetricFormulaVersionsAsync(T);
        Assert.Equal(MetricRows, versions.Count);
        Assert.All(versions.Values, version => Assert.Equal(CanonicalMetrics.Version, version));

        // Same inputs + same version = honestly unchanged, and nothing is a version rewrite.
        var plan = await PlanMetricsAsync(T);
        Assert.Equal(MetricRows, plan.Unchanged);
        Assert.Equal(0, plan.Rewritten);
        Assert.Equal(0, plan.FormulaVersionRewritten);
    }

    /// <summary>
    /// TC-MDF-102 — <c>metrics-recompute</c>: rows carrying an <b>older/absent</b> definition
    /// stamp are rewritten even though their values are identical, the current version is
    /// stamped, and the rewrite is reported distinctly from a fact repair
    /// (<see cref="MetricWritePlan.FormulaVersionRewritten"/>). Under the same version a second
    /// run is <c>unchanged</c>. The historical day keeps its absent stamp — never guessed, never
    /// silently reinterpreted.
    /// </summary>
    [Fact]
    public async Task TC_MDF_102_metrics_recompute_rewrites_an_older_definition_and_stamps_the_current_one()
    {
        await ResetAsync();
        await ComputeDerivedMetricsAsync(Prior);
        var first = await ComputeDerivedMetricsAsync(T);
        Assert.Equal(MetricRows, first.Written);

        var before = await ReadMetricsAsync(T);

        // Rows produced before versioning existed: the honest pre-migration shape is NULL, and
        // only a recompute may stamp them.
        await SetMetricFormulaVersionAsync(T, null);
        await SetMetricFormulaVersionAsync(Prior, null);
        var priorBefore = await ReadMetricsAsync(Prior);

        // The classification is distinct: identical values, stale definition → a formula-version
        // rewrite, never an `unchanged` row and never a fact repair.
        var plan = await PlanMetricsAsync(T);
        Assert.Equal(0, plan.Unchanged);
        Assert.Equal(MetricRows, plan.Rewritten);
        Assert.Equal(MetricRows, plan.FormulaVersionRewritten);

        var rewrite = await ComputeDerivedMetricsAsync(T, TimeSpan.FromHours(2));
        Assert.Equal("succeeded", rewrite.Status);
        Assert.Equal(MetricRows, rewrite.Written);
        Assert.Equal(0, rewrite.Unchanged);
        Assert.Equal(MetricRows, await ReadLatestLedgerStatAsync(MetricsRecomputeJob.Code, "written"));

        var after = await ReadMetricsAsync(T);

        // The values are untouched (no formula changed — only its version stamp) ...
        Assert.All(after, pair => Assert.Equal(before[pair.Key].Facts(), pair.Value.Facts()));

        // ... every row now carries the current definition, and only the rewritten rows moved
        // their computed_at (a rewrite is a real write).
        var versions = await ReadMetricFormulaVersionsAsync(T);
        Assert.Equal(MetricRows, versions.Count);
        Assert.All(versions.Values, version => Assert.Equal(CanonicalMetrics.Version, version));
        Assert.All(after, pair => Assert.Equal(
            new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddHours(2),
            pair.Value.ComputedAt));

        // The historical day is not silently reinterpreted: its absent stamp stays absent and
        // its rows are byte-identical (only an explicit recompute of that day may stamp it).
        var priorVersions = await ReadMetricFormulaVersionsAsync(Prior);
        Assert.Equal(MetricRows, priorVersions.Count);
        Assert.All(priorVersions.Values, version => Assert.Null(version));

        // ... and the historical day's rows are byte-identical (values *and* computed_at).
        Assert.Equal(priorBefore, await ReadMetricsAsync(Prior));

        // ... and a second run under the same definition is honestly unchanged again.
        var settled = await ComputeDerivedMetricsAsync(T, TimeSpan.FromHours(4));
        Assert.Equal(0, settled.Written);
        Assert.Equal(MetricRows, settled.Unchanged);
    }

    /// <summary>
    /// TC-MDF-102 — <c>metrics-recompute</c>: the same rule for any stamp the current definition
    /// did not produce (a bumped-then-changed formula version), i.e. the rule is
    /// "stamp ≠ current version", not "stamp is absent". The rows are rewritten, restamped and
    /// reported distinctly, and the row set stays one-per-key.
    /// </summary>
    [Fact]
    public async Task TC_MDF_102_metrics_recompute_rewrites_a_row_stamped_with_another_definition()
    {
        await ResetAsync();
        await ComputeDerivedMetricsAsync(T);
        var before = await ReadMetricsAsync(T);

        await SetMetricFormulaVersionAsync(T, CanonicalMetrics.Version + 1);

        var plan = await PlanMetricsAsync(T);
        Assert.Equal(MetricRows, plan.FormulaVersionRewritten);
        Assert.Equal(0, plan.Rewritten - plan.FormulaVersionRewritten);

        var rewrite = await ComputeDerivedMetricsAsync(T, TimeSpan.FromHours(2));
        Assert.Equal(MetricRows, rewrite.Written);
        Assert.Equal(0, rewrite.Unchanged);

        Assert.All(
            (await ReadMetricFormulaVersionsAsync(T)).Values,
            version => Assert.Equal(CanonicalMetrics.Version, version));
        Assert.All((await ReadMetricsAsync(T)), pair => Assert.Equal(before[pair.Key].Facts(), pair.Value.Facts()));

        await using var db = _postgres.CreateContext();
        Assert.Equal(MetricRows, await db.DerivedMetrics.CountAsync(m => m.AsOfDate == T));
    }

    /// <summary>
    /// TC-MDF-102 — <c>medians</c>: the sector-median lane carries the same discipline — every
    /// row is stamped with the current definition version, a stale/absent stamp is rewritten
    /// identically-valued and reported distinctly, and the settled re-run is <c>unchanged</c>.
    /// </summary>
    [Fact]
    public async Task TC_MDF_102_medians_carry_the_same_definition_version_discipline()
    {
        await ResetAsync();
        await ComputeDerivedMetricsAsync(T);

        var first = await RunMediansAsync(T);
        Assert.Equal(MedianRows, first.Written);
        Assert.All(
            (await ReadMedianFormulaVersionsAsync(T)).Values,
            version => Assert.Equal(CanonicalMetrics.Version, version));

        var before = await ReadMediansAsync(T);
        var settledPlan = await PlanMediansAsync(T);
        Assert.Equal(MedianRows, settledPlan.Unchanged);
        Assert.Equal(0, settledPlan.FormulaVersionRewritten);

        // The honest pre-migration shape, then the distinct classification.
        await SetMedianFormulaVersionAsync(T, null);

        var plan = await PlanMediansAsync(T);
        Assert.Equal(0, plan.Unchanged);
        Assert.Equal(MedianRows, plan.Rewritten);
        Assert.Equal(MedianRows, plan.FormulaVersionRewritten);

        var rewrite = await RunMediansAsync(T, TimeSpan.FromHours(2));
        Assert.Equal(MedianRows, rewrite.Written);
        Assert.Equal(0, rewrite.Unchanged);
        Assert.Equal(MedianRows, await ReadLatestLedgerStatAsync(SectorMediansJob.Code, "written"));

        Assert.All(
            (await ReadMedianFormulaVersionsAsync(T)).Values,
            version => Assert.Equal(CanonicalMetrics.Version, version));
        Assert.All(
            (await ReadMediansAsync(T)),
            pair => Assert.Equal(before[pair.Key] with { ComputedAt = default }, pair.Value with { ComputedAt = default }));

        var settled = await RunMediansAsync(T, TimeSpan.FromHours(4));
        Assert.Equal(0, settled.Written);
        Assert.Equal(MedianRows, settled.Unchanged);
    }

    /// <summary>
    /// TC-MDF-102 — the run ledger records <b>who</b> asked for a run, after the fact: a run
    /// carrying the scheduler's retry context reads <c>scheduled</c>, a run through the bare
    /// overload the admin trigger uses reads <c>admin</c>, and an explicit request-level trigger
    /// wins over the context's default. Purely additive provenance: the counts and status of the
    /// run are untouched by it.
    /// </summary>
    [Fact]
    public async Task TC_MDF_102_run_ledger_records_the_trigger_that_asked_for_the_run()
    {
        await ResetAsync();

        // The scheduler path: it always supplies the retry-ladder context.
        await using (var host = BuildHost())
        {
            using var scope = host.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<IIngestionJobRunner>();
            await runner.RunAsync(MetricsRecomputeJob.Code, new IngestionRequest(T), new IngestionRunContext(1, 3));
        }

        Assert.Equal(
            IngestionTrigger.Scheduled,
            await ReadLatestLedgerStringStatAsync(MetricsRecomputeJob.Code, "trigger"));
        Assert.Equal(MetricRows, await ReadLatestLedgerStatAsync(MetricsRecomputeJob.Code, "written"));

        // The admin path: the operator trigger omits the context entirely.
        await using (var host = BuildHost(TimeSpan.FromHours(2)))
        {
            using var scope = host.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<IIngestionJobRunner>();
            await runner.RunAsync(MetricsRecomputeJob.Code, new IngestionRequest(T));
        }

        Assert.Equal(
            IngestionTrigger.Admin,
            await ReadLatestLedgerStringStatAsync(MetricsRecomputeJob.Code, "trigger"));
        Assert.Equal(MetricRows, await ReadLatestLedgerStatAsync(MetricsRecomputeJob.Code, "unchanged"));

        // An explicit trigger on the request wins over the context's default.
        await using (var host = BuildHost(TimeSpan.FromHours(3)))
        {
            using var scope = host.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<IIngestionJobRunner>();
            await runner.RunAsync(
                MetricsRecomputeJob.Code,
                new IngestionRequest(T, Trigger: IngestionTrigger.Admin),
                new IngestionRunContext(1, 3));
        }

        Assert.Equal(
            IngestionTrigger.Admin,
            await ReadLatestLedgerStringStatAsync(MetricsRecomputeJob.Code, "trigger"));
    }

    // =====================================================================================
    // Harness
    // =====================================================================================

    /// <summary>The ingestion composition retargeted at the Testcontainers database.</summary>
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

    private static async Task<IngestResult> RunAsync(
        IServiceProvider provider,
        string jobCode,
        DateOnly date)
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
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
    }

    /// <summary>Every stored metric row at <paramref name="date"/>, keyed <c>SYMBOL|CODE</c>.</summary>
    private async Task<Dictionary<string, MetricMarker>> ReadMetricsAsync(DateOnly date)
    {
        await using var db = _postgres.CreateContext();
        var symbols = await db.Instruments.ToDictionaryAsync(i => i.Id, i => i.Symbol);
        var rows = await db.DerivedMetrics.Where(m => m.AsOfDate == date).ToListAsync();
        return rows.ToDictionary(
            r => $"{symbols[r.InstrumentId]}|{r.MetricCode}",
            r => new MetricMarker(
                r.Value,
                r.WindowYears,
                r.IsAdjusted,
                r.IsRested,
                r.RegimeBreak,
                r.ComputedAt),
            StringComparer.Ordinal);
    }

    /// <summary>Every stored median row at <paramref name="date"/>, keyed <c>SECTOR|CODE</c>.</summary>
    private async Task<Dictionary<string, MedianMarker>> ReadMediansAsync(DateOnly date)
    {
        await using var db = _postgres.CreateContext();
        var sectors = await db.Sectors.ToDictionaryAsync(s => s.Id, s => s.Code);
        var rows = await db.SectorMetricMedians.Where(m => m.AsOfDate == date).ToListAsync();
        return rows.ToDictionary(
            r => $"{sectors[r.SectorId]}|{r.MetricCode}",
            r => new MedianMarker(
                r.MedianValue,
                r.PeerCount,
                r.ExcludedCount,
                r.IsAdjusted,
                r.ComputedAt),
            StringComparer.Ordinal);
    }

    private async Task<MedianMarker> ReadMedianAsync(DateOnly date, string sectorCode, string metricCode)
    {
        await using var db = _postgres.CreateContext();
        var row = await (
                from median in db.SectorMetricMedians
                join sector in db.Sectors on median.SectorId equals sector.Id
                where median.AsOfDate == date
                    && sector.Code == sectorCode
                    && median.MetricCode == metricCode
                select median)
            .SingleAsync();

        return new MedianMarker(
            row.MedianValue,
            row.PeerCount,
            row.ExcludedCount,
            row.IsAdjusted,
            row.ComputedAt);
    }

    /// <summary>Applies the KAP sign convention to ALFA's serving FY CAPEX line value.</summary>
    private async Task SetAlfaFyCapexAsync(decimal capex)
    {
        await using var db = _postgres.CreateContext();
        var statementId = await (
                from statement in db.FinancialStatements
                join instrument in db.Instruments on statement.InstrumentId equals instrument.Id
                where instrument.Symbol == "ALFA"
                    && statement.PeriodType == "FY"
                    && statement.StatementType == "CF"
                orderby statement.PeriodEndDate descending, statement.Version descending
                select statement.Id)
            .FirstAsync();

        var line = await db.FinLineItems
            .SingleAsync(l => l.StatementId == statementId && l.ItemCode == "CAPEX");
        line.Value = capex;
        await db.SaveChangesAsync();
    }

    /// <summary>Corrects one instrument's stored metric value — the median lane's upstream fact.</summary>
    private async Task SetDerivedMetricValueAsync(string symbol, string metricCode, decimal value)
    {
        await using var db = _postgres.CreateContext();
        var instrumentId = await db.Instruments
            .Where(i => i.Symbol == symbol)
            .Select(i => i.Id)
            .SingleAsync();

        var row = await db.DerivedMetrics.SingleAsync(
            m => m.InstrumentId == instrumentId && m.MetricCode == metricCode && m.AsOfDate == T);
        row.Value = value;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Reads one stat off the latest run-ledger row for <paramref name="jobCode"/> (the stats
    /// the runner writes to <c>ingest_runs.stats_json</c>); an absent stat reads 0.
    /// </summary>
    private async Task<int> ReadLatestLedgerStatAsync(string jobCode, string name)
    {
        await using var db = _postgres.CreateContext();
        var statsJson = await db.IngestRuns
            .Where(r => r.JobCode == jobCode)
            .OrderByDescending(r => r.Id)
            .Select(r => r.StatsJson)
            .FirstAsync();

        using var document = JsonDocument.Parse(statsJson!);
        return document.RootElement.TryGetProperty(name, out var value)
            ? value.GetInt32()
            : 0;
    }

    /// <summary>
    /// Reads one string stat off the latest run-ledger row for <paramref name="jobCode"/>
    /// (e.g. <c>trigger</c>, TKT-foundation-070); an absent stat reads <c>null</c>.
    /// </summary>
    private async Task<string?> ReadLatestLedgerStringStatAsync(string jobCode, string name)
    {
        await using var db = _postgres.CreateContext();
        var statsJson = await db.IngestRuns
            .Where(r => r.JobCode == jobCode)
            .OrderByDescending(r => r.Id)
            .Select(r => r.StatsJson)
            .FirstAsync();

        using var document = JsonDocument.Parse(statsJson!);
        return document.RootElement.TryGetProperty(name, out var value)
            ? value.GetString()
            : null;
    }

    /// <summary>The engine's classification of the recompute for <paramref name="date"/> (write plan).</summary>
    private async Task<MetricWritePlan> PlanMetricsAsync(DateOnly date)
    {
        await using var db = _postgres.CreateContext();
        var engine = new MetricsEngine(db);
        var computed = await engine.ComputeAsync(date);
        return await engine.PlanWriteAsync(computed, date);
    }

    /// <summary>The median engine's classification of the recompute for <paramref name="date"/>.</summary>
    private async Task<SectorMedianWritePlan> PlanMediansAsync(DateOnly date)
    {
        await using var db = _postgres.CreateContext();
        var engine = new SectorMedianEngine(db);
        var computed = await engine.ComputeAsync(date);
        return await engine.PlanWriteAsync(computed, date);
    }

    /// <summary>
    /// Every stored metric row's <c>formula_version</c> at <paramref name="date"/>, keyed
    /// <c>SYMBOL|CODE</c> (<c>null</c> = produced before the definition was versioned).
    /// </summary>
    private Task<Dictionary<string, int?>> ReadMetricFormulaVersionsAsync(DateOnly date) =>
        ReadFormulaVersionsAsync(
            """
            SELECT i.symbol || '|' || m.metric_code, m.formula_version
            FROM derived_metrics m
            JOIN instruments i ON i.id = m.instrument_id
            WHERE m.as_of_date = @as_of_date
            """,
            date);

    /// <summary>Every stored median row's <c>formula_version</c> at <paramref name="date"/>, keyed <c>SECTOR|CODE</c>.</summary>
    private Task<Dictionary<string, int?>> ReadMedianFormulaVersionsAsync(DateOnly date) =>
        ReadFormulaVersionsAsync(
            """
            SELECT s.code || '|' || m.metric_code, m.formula_version
            FROM sector_metric_medians m
            JOIN sectors s ON s.id = m.sector_id
            WHERE m.as_of_date = @as_of_date
            """,
            date);

    private async Task<Dictionary<string, int?>> ReadFormulaVersionsAsync(string sql, DateOnly date)
    {
        await using var db = _postgres.CreateContext();
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "as_of_date";
            parameter.Value = date;
            command.Parameters.Add(parameter);

            await using var reader = await command.ExecuteReaderAsync();
            var versions = new Dictionary<string, int?>(StringComparer.Ordinal);
            while (await reader.ReadAsync())
            {
                versions[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetInt32(1);
            }

            return versions;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// Sets every stored metric row's <c>formula_version</c> at <paramref name="date"/> — the
    /// DB-level exercise of "rows produced by a different definition", which no production path
    /// can produce on demand (the definition version is a compile-time constant).
    /// </summary>
    private async Task SetMetricFormulaVersionAsync(DateOnly date, int? version)
    {
        await using var db = _postgres.CreateContext();
        if (version is int stamped)
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE derived_metrics SET formula_version = {0} WHERE as_of_date = {1}", stamped, date);
        }
        else
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE derived_metrics SET formula_version = NULL WHERE as_of_date = {0}", date);
        }
    }

    /// <summary>Sets every stored median row's <c>formula_version</c> at <paramref name="date"/>.</summary>
    private async Task SetMedianFormulaVersionAsync(DateOnly date, int? version)
    {
        await using var db = _postgres.CreateContext();
        if (version is int stamped)
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE sector_metric_medians SET formula_version = {0} WHERE as_of_date = {1}", stamped, date);
        }
        else
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE sector_metric_medians SET formula_version = NULL WHERE as_of_date = {0}", date);
        }
    }

    private sealed record MetricMarker(
        decimal? Value,
        int? WindowYears,
        bool IsAdjusted,
        bool IsRested,
        bool? RegimeBreak,
        DateTimeOffset ComputedAt)
    {
        /// <summary>The input-derived half of the row — what an input-aware recompute compares.</summary>
        public MetricMarker Facts() => this with { ComputedAt = default };
    }

    private sealed record MedianMarker(
        decimal? MedianValue,
        int? PeerCount,
        int? ExcludedCount,
        bool IsAdjusted,
        DateTimeOffset ComputedAt);
}
