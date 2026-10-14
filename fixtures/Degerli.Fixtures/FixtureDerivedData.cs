using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Metrics;
using Degerli.Ingestion.Valuation;
using Degerli.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Degerli.Fixtures;

/// <summary>
/// Produces the L4 stack's derived data at the fixture anchor (TKT-foundation-013).
/// After <see cref="FixtureSeeder.ApplyAsync"/> has written every fact, this pass runs
/// the product's own <c>metrics-recompute</c> (TKT-mdf-008) and <c>market-snapshot</c>
/// (TKT-mov-003) jobs for <see cref="FixtureAnchor.T"/> through the same
/// <see cref="IngestionJobRunner"/> the scheduler and admin trigger use, then the
/// <c>dcf-baselines</c> (TKT-val-003) job directly (see the inline note on why it is
/// not run through the ledger-writing runner).
/// <para>
/// It deliberately lives in the L4 entrypoint (<see cref="FixtureSeedEntry"/>) and not
/// in the shared <see cref="FixtureSeeder"/>, so the L2 absolute-date variant keeps its
/// observable behavior: L2 tests seed facts and run the jobs themselves. Running the
/// jobs — rather than re-encoding the FU §11.1–§11.3 goldens here — keeps one definition
/// per number (BR-MDF-009; the jobs are themselves verified against those goldens) and
/// leaves the run-ledger rows the admin ledger/staleness views read (02 §3.1).
/// </para>
/// <para>
/// Derived data is produced only after all facts are seeded, and in time order
/// (metrics-recompute, then the snapshot) at the anchor — never wall-clock dates
/// (FU §8.3). Both jobs are idempotent on their keys, so a re-run upserts in place.
/// </para>
/// </summary>
public static class FixtureDerivedData
{
    /// <summary>
    /// Runs <c>metrics-recompute</c> then <c>market-snapshot</c> for
    /// <see cref="FixtureAnchor.T"/> against an already-seeded database. Composes the
    /// two jobs' dependencies directly (mirroring the one-scoped-<see cref="DegerliDbContext"/>
    /// composition the API host and the L2 tests use) so the fixture module takes no
    /// host or extra package dependency.
    /// </summary>
    public static async Task ApplyAsync(
        string connectionString,
        FixtureAnchor anchor,
        TextWriter log,
        TimeProvider? clock = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(log);

        var timeProvider = clock ?? TimeProvider.System;

        // One context for the whole pass: the runner and both jobs share a single
        // scope in the product host, so their writes ride one unit of work here too.
        await using var db = new DegerliDbContext(DegerliDbContextOptions.Build(connectionString));

        var metricsJob = new MetricsRecomputeJob(
            new MetricsEngine(db),
            new DerivedMetricStore(db),
            timeProvider);

        var snapshotJob = new MarketSnapshotJob(
            new MarketSnapshotEngine(db, Options.Create(new MarketSnapshotOptions())),
            new MarketSnapshotStore(db),
            timeProvider);

        // DCF baselines are derived rows too (BR-VAL-001: "a covered calculator always
        // has an active baseline"). Without this pass the L4 stack serves
        // `404 NOT_FOUND` for `/stocks/{symbol}/dcf`, so the SCR-006/SCR-011 flows
        // (and the SC-002 DCF leg) cannot run — the same class of gap foundation-013
        // closed for the metrics/snapshot reads.
        var dcfBaselineJob = new DcfBaselineGenerationJob(
            db,
            Options.Create(new DcfBaselineOptions()));

        var runner = new IngestionJobRunner(
            [metricsJob, snapshotJob],
            db,
            timeProvider,
            NullLogger<IngestionJobRunner>.Instance);

        var request = new IngestionRequest(anchor.T);

        var metrics = await runner
            .RunAsync(MetricsRecomputeJob.Code, request, cancellationToken)
            .ConfigureAwait(false);
        var snapshot = await runner
            .RunAsync(MarketSnapshotJob.Code, request, cancellationToken)
            .ConfigureAwait(false);

        // Invoked directly, not through the runner: the fixture seeds the baseline
        // *data* the calculator serves, but deliberately records no `dcf-baselines`
        // ingest-ledger row. The admin regeneration contract (TC-VAL-016) asserts the
        // ledger delta of its own triggered run against a zero-row baseline, so a
        // fixture ledger row would make its ledger locator ambiguous.
        var dcfBaselines = await dcfBaselineJob
            .RunAsync(request, cancellationToken)
            .ConfigureAwait(false);

        await log.WriteLineAsync(
            $"fixture-universe: derived data at T={anchor.T:yyyy-MM-dd}; " +
            $"{metrics.JobCode}={metrics.Status} (written={metrics.Written}, unchanged={metrics.Unchanged}); " +
            $"{snapshot.JobCode}={snapshot.Status} (written={snapshot.Written}, unchanged={snapshot.Unchanged}); " +
            $"{dcfBaselines.JobCode}={dcfBaselines.Status} (written={dcfBaselines.Written}, unchanged={dcfBaselines.Unchanged}).")
            .ConfigureAwait(false);
    }
}
