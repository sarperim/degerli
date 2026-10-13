using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Metrics;
using Degerli.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Degerli.Fixtures;

/// <summary>
/// Produces the L4 stack's derived data at the fixture anchor (TKT-foundation-013).
/// After <see cref="FixtureSeeder.ApplyAsync"/> has written every fact, this pass runs
/// the product's own <c>metrics-recompute</c> (TKT-mdf-008) and <c>market-snapshot</c>
/// (TKT-mov-003) jobs for <see cref="FixtureAnchor.T"/> through the same
/// <see cref="IngestionJobRunner"/> the scheduler and admin trigger use.
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

        await log.WriteLineAsync(
            $"fixture-universe: derived data at T={anchor.T:yyyy-MM-dd}; " +
            $"{metrics.JobCode}={metrics.Status} (written={metrics.Written}, unchanged={metrics.Unchanged}); " +
            $"{snapshot.JobCode}={snapshot.Status} (written={snapshot.Written}, unchanged={snapshot.Unchanged}).")
            .ConfigureAwait(false);
    }
}
