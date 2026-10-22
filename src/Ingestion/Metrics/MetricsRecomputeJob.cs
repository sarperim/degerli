using Degerli.Ingestion.Jobs;

namespace Degerli.Ingestion.Metrics;

/// <summary>
/// The <c>metrics-recompute</c> job (FR-MDF-013/014, UC-MDF-001 step 4): recompute the
/// canonical metrics for the target trading day and write them to <c>derived_metrics</c>.
/// Registered like every other job so the scheduler chain, the run ledger and the admin
/// trigger (TKT-mdf-010) pick it up by code; the snapshot/medians/baseline jobs that
/// TKT-mov-003/TKT-res-003/TKT-val-003 add follow this same convention.
/// <para>
/// <b>Input-aware same-day recompute (<c>01</c> §10.9.13; TKT-foundation-050, the
/// TKT-foundation-042 pattern extended to this lane).</b> The day is recomputed from the
/// current facts on every run and written input-aware: a row already identical to its
/// stored value is reported <c>unchanged</c> and left untouched (its <c>computed_at</c>
/// does not move), while a row whose upstream facts changed (a restatement, a corrected
/// line, a late dividend) is repaired in place and counted as written — never the pre-050
/// blind overwrite that reported "unchanged" for rows it rewrote and "written: 0" for a
/// real repair. Exactly one row per <c>(instrument, metric, as_of_date)</c> is preserved
/// (FR-MDF-015, NFR-MDF-002) and days other than the target one are never touched, so
/// historical days stay immutable.
/// </para>
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

        // The engine classifies the recompute against the stored same-day rows: identical
        // rows are not rewritten, changed rows are (01 §10.9.13; TKT-foundation-050).
        var plan = await _engine
            .PlanWriteAsync(results, request.Date, cancellationToken)
            .ConfigureAwait(false);

        var outcome = await _store
            .UpsertAsync(plan.ToWrite, request.Date, _clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

        // A repair is a written row for this run too: the plan keeps the insert / repair /
        // unchanged distinction so a dedicated `rewritten` ledger stat (the
        // `MetricWritePlan.Rewritten` counter, TKT-foundation-044's fold-in) can report the
        // repair on its own.
        return new IngestResult(
            Code,
            outcome.Written + plan.Rewritten,
            plan.Unchanged,
            0,
            IngestResult.Succeeded);
    }
}
