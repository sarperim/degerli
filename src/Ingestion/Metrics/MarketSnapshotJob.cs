using Degerli.Ingestion.Jobs;

namespace Degerli.Ingestion.Metrics;

/// <summary>
/// The nightly <c>market-snapshot</c> job (FR-MOV-001..006, 021; BR-MOV-009/010/011,
/// NFR-MOV-001): compute the market overview snapshot for the target trading day and
/// write it to <c>market_snapshots</c> (one row per trading day, idempotent). Registered
/// like every other job (TKT-mdf-002 convention) so the scheduler chain, the run ledger
/// and the admin trigger pick it up by code.
/// </summary>
public sealed class MarketSnapshotJob : IIngestJob
{
    /// <summary>Stable job code used by the ledger, scheduler and admin trigger.</summary>
    public const string Code = "market-snapshot";

    private readonly MarketSnapshotEngine _engine;
    private readonly MarketSnapshotStore _store;
    private readonly TimeProvider _clock;

    public MarketSnapshotJob(MarketSnapshotEngine engine, MarketSnapshotStore store, TimeProvider clock)
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
        var computation = await _engine.ComputeAsync(request.Date, cancellationToken).ConfigureAwait(false);
        if (computation is null)
        {
            // No covered-universe price for the day — nothing to snapshot (no fabricated row).
            return new IngestResult(Code, 0, 0, 0, IngestResult.Succeeded, Skipped: 0);
        }

        var outcome = await _store
            .UpsertAsync(request.Date, computation, _clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

        return new IngestResult(Code, outcome.Written, outcome.Unchanged, 0, IngestResult.Succeeded);
    }
}
