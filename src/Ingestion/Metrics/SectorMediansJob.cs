using Degerli.Ingestion.Jobs;

namespace Degerli.Ingestion.Metrics;

/// <summary>
/// The <c>medians</c> job (FR-RES-007/SD-002, `03` §9): computes the five valuation-family
/// sector medians for the target trading day into <c>sector_metric_medians</c>. Registered
/// like every other job so the scheduler chain, the run ledger and the admin trigger pick it
/// up by code (the TKT-mdf-008 convention); it reads the canonical <c>derived_metrics</c>
/// rows and computes nothing itself beyond the median (BR-RES-004).
/// <para>
/// <b>Input-aware same-day recompute (<c>01</c> §10.9.13; TKT-foundation-050, the
/// TKT-foundation-042 pattern extended to this lane).</b> The day is recomputed from the
/// current <c>derived_metrics</c> rows on every run and written input-aware: a median already
/// identical to its stored value is reported <c>unchanged</c> and left untouched, while a
/// median whose upstream metrics changed is repaired in place and counted as written. One
/// row per <c>(sector, metric, as_of_date)</c> is preserved and days other than the target
/// one are never touched.
/// </para>
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

        // The engine classifies the recompute against the stored same-day rows: identical
        // rows are not rewritten, changed rows are (01 §10.9.13; TKT-foundation-050).
        var plan = await _engine
            .PlanWriteAsync(results, request.Date, cancellationToken)
            .ConfigureAwait(false);

        var outcome = await _store
            .UpsertAsync(plan.ToWrite, request.Date, _clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

        // A repair is a written row for this run too; the plan keeps the insert / repair /
        // unchanged distinction for the dedicated `rewritten` ledger stat
        // (`SectorMedianWritePlan.Rewritten`, TKT-foundation-044's fold-in).
        return new IngestResult(
            Code,
            outcome.Written + plan.Rewritten,
            plan.Unchanged,
            0,
            IngestResult.Succeeded);
    }
}
