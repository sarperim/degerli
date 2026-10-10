using Degerli.Ingestion.Jobs;

namespace Degerli.Ingestion.Metrics;

/// <summary>
/// The <c>metrics-recompute</c> job (FR-MDF-013/014, UC-MDF-001 step 4): recompute the
/// canonical metrics for the target trading day and write them to <c>derived_metrics</c>.
/// Registered like every other job so the scheduler chain, the run ledger and the admin
/// trigger (TKT-mdf-010) pick it up by code; the snapshot/medians/baseline jobs that
/// TKT-mov-003/TKT-res-003/TKT-val-003 add follow this same convention.
/// </summary>
public sealed class MetricsRecomputeJob : IIngestJob
{
    /// <summary>Stable job code used by the ledger, scheduler and admin trigger.</summary>
    public const string Code = "metrics-recompute";

    private readonly MetricsEngine _engine;
    private readonly DerivedMetricStore _store;
    private readonly TimeProvider _clock;

    public MetricsRecomputeJob(MetricsEngine engine, DerivedMetricStore store, TimeProvider clock)
    {
        _engine = engine;
        _store = store;
        _clock = clock;
    }

    /// <inheritdoc />
    public string JobCode => Code;

    /// <inheritdoc />
    public async Task<IngestResult> RunAsync(
        IngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        var results = await _engine.ComputeAsync(request.Date, cancellationToken).ConfigureAwait(false);
        var outcome = await _store
            .UpsertAsync(results, request.Date, _clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

        return new IngestResult(Code, outcome.Written, outcome.Unchanged, 0, IngestResult.Succeeded);
    }
}
