using System.Text.Json;
using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Quarantine;
using Degerli.Ingestion.Sources;
using Microsoft.Extensions.Logging;

namespace Degerli.Ingestion.Prices;

/// <summary>
/// The <c>prices</c> EOD job (FR-MDF-001, UC-MDF-001 step 1): fetch the İşbank price
/// payload, enforce provenance and the append-only/idempotent fact-storage invariants,
/// quarantine anything refused, and report the outcome for the run ledger.
/// </summary>
public sealed class PricesJob : IIngestJob
{
    /// <summary>Stable job code used by the ledger, scheduler and admin trigger.</summary>
    public const string Code = "prices";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISourceAdapter<PricesPayload> _adapter;
    private readonly DailyPriceStore _store;
    private readonly IQuarantineWriter _quarantine;
    private readonly IIngestionAlerter _alerter;
    private readonly TimeProvider _clock;
    private readonly ILogger<PricesJob> _logger;

    public PricesJob(
        ISourceAdapter<PricesPayload> adapter,
        DailyPriceStore store,
        IQuarantineWriter quarantine,
        IIngestionAlerter alerter,
        TimeProvider clock,
        ILogger<PricesJob> logger)
    {
        _adapter = adapter;
        _store = store;
        _quarantine = quarantine;
        _alerter = alerter;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public string JobCode => Code;

    /// <inheritdoc />
    public async Task<IngestResult> RunAsync(
        IngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        SourcePayload<PricesPayload> fetched;
        try
        {
            fetched = await _adapter.FetchAsync(request.Date, cancellationToken).ConfigureAwait(false);
        }
        catch (SourcePayloadException exception)
        {
            await QuarantineAsync(request, null, exception.RawJson, "UNPARSEABLE_PAYLOAD", cancellationToken)
                .ConfigureAwait(false);
            return new IngestResult(Code, 0, 0, 1, IngestResult.Failed);
        }

        if (string.IsNullOrWhiteSpace(fetched.SourceRef))
        {
            // Provenance is required on every fact (FR-MDF-008): refuse the payload whole.
            _logger.LogWarning(
                "prices: payload for {Date} carries no provenance (MISSING_PROVENANCE)",
                request.Date);
            await QuarantineAsync(request, null, fetched.RawJson, "MISSING_PROVENANCE", cancellationToken)
                .ConfigureAwait(false);
            return new IngestResult(Code, 0, 0, 1, IngestResult.Failed);
        }

        var outcome = await _store
            .UpsertAsync(request.Date, fetched.SourceRef, _clock.GetUtcNow(), fetched.Payload.Items, cancellationToken)
            .ConfigureAwait(false);

        var quarantined = 0;

        foreach (var conflict in outcome.Conflicts)
        {
            _logger.LogWarning(
                "prices: conflicting value for {Symbol} on {Date}; stored fact kept (CONFLICTING_VALUE)",
                conflict.Symbol,
                request.Date);
            await QuarantineAsync(request, fetched.SourceRef, Serialize(conflict), "CONFLICTING_VALUE", cancellationToken)
                .ConfigureAwait(false);
            await RaiseAlertAsync(fetched.SourceRef, conflict, request, cancellationToken).ConfigureAwait(false);
            quarantined++;
        }

        foreach (var reject in outcome.Rejected)
        {
            _logger.LogWarning(
                "prices: rejected fact for {Symbol} on {Date} ({ReasonCode})",
                reject.Fact.Symbol,
                request.Date,
                reject.ReasonCode);
            await QuarantineAsync(request, fetched.SourceRef, Serialize(reject.Fact), reject.ReasonCode, cancellationToken)
                .ConfigureAwait(false);
            quarantined++;
        }

        var status = quarantined > 0 ? IngestResult.Partial : IngestResult.Succeeded;
        return new IngestResult(Code, outcome.Inserted, outcome.Unchanged, quarantined, status);
    }

    private Task QuarantineAsync(
        IngestionRequest request,
        string? sourceRef,
        string? payloadJson,
        string reasonCode,
        CancellationToken cancellationToken) =>
        _quarantine.WriteAsync(
            new QuarantineEntry(Code, sourceRef, payloadJson, reasonCode, _clock.GetUtcNow()),
            cancellationToken);

    private Task RaiseAlertAsync(
        string sourceRef,
        PriceFact conflict,
        IngestionRequest request,
        CancellationToken cancellationToken) =>
        _alerter.RaiseAsync(
            new IngestionAlert(
                Code,
                "CONFLICTING_VALUE",
                Subject: $"Degerli alert: prices CONFLICTING_VALUE for {conflict.Symbol}",
                BodyTr: $"prices işi {request.Date:yyyy-MM-dd} tarihinde {conflict.Symbol} için çelişen bir fiyat değeri buldu; saklanan değer korundu.",
                BodyEn: $"The prices job found a conflicting price value for {conflict.Symbol} on {request.Date:yyyy-MM-dd}; the stored value was kept.",
                SourceRef: sourceRef),
            cancellationToken);

    private static string Serialize(PriceFact fact) => JsonSerializer.Serialize(fact, JsonOptions);
}
