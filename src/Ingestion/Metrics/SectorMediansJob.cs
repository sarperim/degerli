using Degerli.Ingestion.Jobs;

namespace Degerli.Ingestion.Metrics;

/// <summary>
/// The <c>medians</c> job (FR-RES-007/SD-002, `03` §9): computes the five valuation-family
/// sector medians for the target trading day into <c>sector_metric_medians</c>. Registered
/// like every other job so the scheduler chain, the run ledger and the admin trigger pick it
/// up by code (the TKT-mdf-008 convention); it reads the canonical <c>derived_metrics</c>
/// rows and computes nothing itself beyond the median (BR-RES-004).
/// </summary>
public sealed class SectorMediansJob : IIngestJob
{
    /// <summary>Stable job code used by the ledger, scheduler and admin trigger.</summary>
    public const string Code = "medians";

    private readonly SectorMedianEngine _engine;
    private readonly SectorMedianStore _store;
    private readonly TimeProvider _clock;

    public SectorMediansJob(SectorMedianEngine engine, SectorMedianStore store, TimeProvider clock)
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
