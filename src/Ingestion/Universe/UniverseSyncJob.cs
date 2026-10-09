using System.Text.Json;
using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Quarantine;
using Degerli.Ingestion.Sources;
using Microsoft.Extensions.Logging;

namespace Degerli.Ingestion.Universe;

/// <summary>
/// The <c>universe-sync</c> job (FR-MDF-006, FR-MDF-007, UC-MDF-003): syncs the
/// instrument set and sector classifications, applies effective-dated membership
/// changes (append-only) and ingests BIST index levels. An instrument with no (or
/// unknown) sector classification is stored as unclassified and flagged — never
/// dropped (UC-MDF-003 alternate a, TC-MDF-025).
/// </summary>
public sealed class UniverseSyncJob : IIngestJob
{
    /// <summary>Stable job code used by the ledger, scheduler and admin trigger.</summary>
    public const string Code = "universe-sync";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISourceAdapter<UniversePayload> _universeAdapter;
    private readonly ISourceAdapter<IndexLevelsPayload> _indexLevelsAdapter;
    private readonly UniverseSyncStore _store;
    private readonly IQuarantineWriter _quarantine;
    private readonly IIngestionAlerter _alerter;
    private readonly TimeProvider _clock;
    private readonly ILogger<UniverseSyncJob> _logger;

    public UniverseSyncJob(
        ISourceAdapter<UniversePayload> universeAdapter,
        ISourceAdapter<IndexLevelsPayload> indexLevelsAdapter,
        UniverseSyncStore store,
        IQuarantineWriter quarantine,
        IIngestionAlerter alerter,
        TimeProvider clock,
        ILogger<UniverseSyncJob> logger)
    {
        _universeAdapter = universeAdapter;
        _indexLevelsAdapter = indexLevelsAdapter;
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
        SourcePayload<UniversePayload> fetched;
        try
        {
            fetched = await _universeAdapter.FetchAsync(request.Date, cancellationToken).ConfigureAwait(false);
        }
        catch (SourcePayloadException exception)
        {
            await QuarantineAsync(null, exception.RawJson, "UNPARSEABLE_PAYLOAD", cancellationToken)
                .ConfigureAwait(false);
            return new IngestResult(Code, 0, 0, 1, IngestResult.Failed);
        }

        if (string.IsNullOrWhiteSpace(fetched.SourceRef))
        {
            _logger.LogWarning("universe-sync: payload carries no provenance (MISSING_PROVENANCE)");
            await QuarantineAsync(null, fetched.RawJson, "MISSING_PROVENANCE", cancellationToken)
                .ConfigureAwait(false);
            return new IngestResult(Code, 0, 0, 1, IngestResult.Failed);
        }

        var now = _clock.GetUtcNow();
        var universe = await _store
            .SyncUniverseAsync(fetched.SourceRef, now, fetched.Payload, cancellationToken)
            .ConfigureAwait(false);

        var quarantined = 0;
        foreach (var unclassified in universe.Unclassified)
        {
            _logger.LogWarning(
                "universe-sync: instrument {Symbol} has no sector classification (MISSING_CLASSIFICATION)",
                unclassified.Symbol);
            await QuarantineAsync(
                    fetched.SourceRef,
                    Serialize(unclassified),
                    "MISSING_CLASSIFICATION",
                    cancellationToken)
                .ConfigureAwait(false);
            await _alerter.RaiseAsync(
                    new IngestionAlert(
                        Code,
                        "MISSING_CLASSIFICATION",
                        Subject: $"Degerli alert: universe-sync MISSING_CLASSIFICATION for {unclassified.Symbol}",
                        BodyTr: $"universe-sync işi {unclassified.Symbol} enstrümanında sektör sınıflandırması bulamadı; enstrüman sınıflandırılmamış olarak kaydedildi ve işaretlendi.",
                        BodyEn: $"The universe-sync job found no sector classification for {unclassified.Symbol}; the instrument was stored as unclassified and flagged.",
                        SourceRef: fetched.SourceRef),
                    cancellationToken)
                .ConfigureAwait(false);
            quarantined++;
        }

        var indexWritten = 0;
        var indexUnchanged = 0;
        try
        {
            var levels = await _indexLevelsAdapter.FetchAsync(request.Date, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(levels.SourceRef))
            {
                _logger.LogWarning("universe-sync: index-levels payload carries no provenance (MISSING_PROVENANCE)");
                await QuarantineAsync(null, levels.RawJson, "MISSING_PROVENANCE", cancellationToken)
                    .ConfigureAwait(false);
                quarantined++;
            }
            else
            {
                var result = await _store
                    .SyncIndexLevelsAsync(levels.SourceRef, now, levels.Payload, cancellationToken)
                    .ConfigureAwait(false);
                indexWritten = result.Written;
                indexUnchanged = result.Unchanged;
            }
        }
        catch (SourcePayloadException exception)
        {
            _logger.LogWarning("universe-sync: index-levels payload unparseable (UNPARSEABLE_PAYLOAD)");
            await QuarantineAsync(null, exception.RawJson, "UNPARSEABLE_PAYLOAD", cancellationToken)
                .ConfigureAwait(false);
            quarantined++;
        }

        var written = universe.InstrumentsWritten + universe.MembershipsAdded + indexWritten;
        var unchanged = universe.InstrumentsUnchanged + indexUnchanged;
        var status = quarantined > 0 ? IngestResult.Partial : IngestResult.Succeeded;

        return new IngestResult(Code, written, unchanged, quarantined, status);
    }

    private Task QuarantineAsync(
        string? sourceRef,
        string? payloadJson,
        string reasonCode,
        CancellationToken cancellationToken) =>
        _quarantine.WriteAsync(
            new QuarantineEntry(Code, sourceRef, payloadJson, reasonCode, _clock.GetUtcNow()),
            cancellationToken);

    private static string Serialize(UniverseInstrument instrument) =>
        JsonSerializer.Serialize(instrument, JsonOptions);
}
