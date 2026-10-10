using System.Text.Json;
using Degerli.Ingestion.Coverage;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Quarantine;
using Degerli.Ingestion.Sources;
using Degerli.Ingestion.Validation;
using Microsoft.Extensions.Logging;

namespace Degerli.Ingestion.Prices;

/// <summary>
/// The <c>prices</c> EOD job (FR-MDF-001, UC-MDF-001 step 1): fetch the İşbank price
/// payload, enforce provenance and the append-only/idempotent fact-storage invariants,
/// quarantine anything refused, and report the outcome for the run ledger. When the
/// request carries a <c>backfillFrom</c> the job runs in <b>backfill mode</b>
/// (FR-MDF-010, UC-MDF-002): it ingests the source's historical range and records the
/// achieved depth per instrument — never fabricating rows before the source limit.
/// </summary>
public sealed class PricesJob : IIngestJob
{
    /// <summary>Stable job code used by the ledger, scheduler and admin trigger.</summary>
    public const string Code = "prices";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISourceAdapter<PricesPayload> _adapter;
    private readonly IBackfillSourceAdapter<PricesHistoryPayload> _backfillAdapter;
    private readonly DailyPriceStore _store;
    private readonly CoverageRecorder _coverage;
    private readonly IQuarantineService _quarantine;
    private readonly TimeProvider _clock;
    private readonly ILogger<PricesJob> _logger;

    public PricesJob(
        ISourceAdapter<PricesPayload> adapter,
        IBackfillSourceAdapter<PricesHistoryPayload> backfillAdapter,
        DailyPriceStore store,
        CoverageRecorder coverage,
        IQuarantineService quarantine,
        TimeProvider clock,
        ILogger<PricesJob> logger)
    {
        _adapter = adapter;
        _backfillAdapter = backfillAdapter;
        _store = store;
        _coverage = coverage;
        _quarantine = quarantine;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public string JobCode => Code;

    /// <inheritdoc />
    public Task<IngestResult> RunAsync(
        IngestionRequest request,
        CancellationToken cancellationToken = default) =>
        request.IsBackfill
            ? RunBackfillAsync(request, cancellationToken)
            : RunIncrementalAsync(request, cancellationToken);

    private async Task<IngestResult> RunIncrementalAsync(
        IngestionRequest request,
        CancellationToken cancellationToken)
    {
        SourcePayload<PricesPayload> fetched;
        try
        {
            fetched = await _adapter.FetchAsync(request.Date, cancellationToken).ConfigureAwait(false);
        }
        catch (SourcePayloadException exception)
        {
            await QuarantineAsync(request, null, exception.RawJson, QuarantineReason.UnparseablePayload, cancellationToken)
                .ConfigureAwait(false);
            return new IngestResult(Code, 0, 0, 1, IngestResult.Failed);
        }

        if (string.IsNullOrWhiteSpace(fetched.SourceRef))
        {
            // Provenance is required on every fact (FR-MDF-008): refuse the payload whole.
            _logger.LogWarning(
                "prices: payload for {Date} carries no provenance (MISSING_PROVENANCE)",
                request.Date);
            await QuarantineAsync(request, null, fetched.RawJson, QuarantineReason.MissingProvenance, cancellationToken)
                .ConfigureAwait(false);
            return new IngestResult(Code, 0, 0, 1, IngestResult.Failed);
        }

        var outcome = await _store
            .UpsertAsync(request.Date, fetched.SourceRef, _clock.GetUtcNow(), fetched.Payload.Items, cancellationToken)
            .ConfigureAwait(false);

        var quarantined = await QuarantineOutcomeAsync(request, fetched.SourceRef, outcome, cancellationToken)
            .ConfigureAwait(false);

        var status = quarantined > 0 ? IngestResult.Partial : IngestResult.Succeeded;
        return new IngestResult(Code, outcome.Inserted, outcome.Unchanged, quarantined, status);
    }

    /// <summary>
    /// Backfill mode (FR-MDF-010, UC-MDF-002): ingest the source's historical range and
    /// record the actual achieved depth per instrument. The earliest row the source
    /// returns is the real limit; rows before it are never fabricated (BR-MDF-006).
    /// </summary>
    private async Task<IngestResult> RunBackfillAsync(
        IngestionRequest request,
        CancellationToken cancellationToken)
    {
        var requestedFrom = request.BackfillFrom!.Value;

        SourcePayload<PricesHistoryPayload> fetched;
        try
        {
            fetched = await _backfillAdapter.FetchBackfillAsync(requestedFrom, cancellationToken).ConfigureAwait(false);
        }
        catch (SourcePayloadException exception)
        {
            await QuarantineAsync(request, null, exception.RawJson, QuarantineReason.UnparseablePayload, cancellationToken)
                .ConfigureAwait(false);
            return new IngestResult(Code, 0, 0, 1, IngestResult.Failed);
        }

        if (string.IsNullOrWhiteSpace(fetched.SourceRef))
        {
            _logger.LogWarning(
                "prices: backfill payload from {From} carries no provenance (MISSING_PROVENANCE)",
                requestedFrom);
            await QuarantineAsync(request, null, fetched.RawJson, QuarantineReason.MissingProvenance, cancellationToken)
                .ConfigureAwait(false);
            return new IngestResult(Code, 0, 0, 1, IngestResult.Failed);
        }

        var rows = fetched.Payload.Rows;
        if (rows.Count == 0)
        {
            _logger.LogWarning("prices: backfill from {From} returned no history", requestedFrom);
            return new IngestResult(Code, 0, 0, 0, IngestResult.Succeeded);
        }

        var outcome = await _store
            .UpsertHistoryAsync(fetched.SourceRef, _clock.GetUtcNow(), rows, cancellationToken)
            .ConfigureAwait(false);

        var quarantined = await QuarantineOutcomeAsync(request, fetched.SourceRef, outcome, cancellationToken)
            .ConfigureAwait(false);

        // Record the achieved depth per instrument, with a source-limit note when the
        // requested target was not reached (UC-MDF-002 alternate a).
        var symbols = rows.Select(r => r.Symbol).Distinct(StringComparer.Ordinal).ToList();
        var achievedFrom = rows.Min(r => r.Date);
        await _coverage
            .RecordPricesCoverageAsync(symbols, requestedFrom, achievedFrom, cancellationToken)
            .ConfigureAwait(false);

        var status = quarantined > 0 ? IngestResult.Partial : IngestResult.Succeeded;
        return new IngestResult(Code, outcome.Inserted, outcome.Unchanged, quarantined, status);
    }

    private async Task<int> QuarantineOutcomeAsync(
        IngestionRequest request,
        string sourceRef,
        PriceWriteResult outcome,
        CancellationToken cancellationToken)
    {
        var quarantined = 0;

        foreach (var conflict in outcome.Conflicts)
        {
            _logger.LogWarning(
                "prices: conflicting value for {Symbol} on {Date}; stored fact kept (CONFLICTING_VALUE)",
                conflict.Symbol,
                request.Date);
            await QuarantineAsync(request, sourceRef, Serialize(conflict), QuarantineReason.ConflictingValue, cancellationToken)
                .ConfigureAwait(false);
            quarantined++;
        }

        foreach (var reject in outcome.Rejected)
        {
            _logger.LogWarning(
                "prices: rejected fact for {Symbol} on {Date} ({ReasonCode})",
                reject.Fact.Symbol,
                request.Date,
                reject.ReasonCode);
            await QuarantineAsync(request, sourceRef, Serialize(reject.Fact), reject.ReasonCode, cancellationToken)
                .ConfigureAwait(false);
            quarantined++;
        }

        return quarantined;
    }

    private Task QuarantineAsync(
        IngestionRequest request,
        string? sourceRef,
        string? payloadJson,
        string reasonCode,
        CancellationToken cancellationToken) =>
        _quarantine.QuarantineAsync(
            new QuarantineEntry(Code, sourceRef, payloadJson, reasonCode, _clock.GetUtcNow()),
            cancellationToken);

    private static string Serialize(PriceFact fact) => JsonSerializer.Serialize(fact, JsonOptions);
}
