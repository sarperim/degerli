using System.Text.Json;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Quarantine;
using Degerli.Ingestion.Sources;
using Microsoft.Extensions.Logging;

namespace Degerli.Ingestion.Dividends;

/// <summary>
/// The <c>dividends</c> job (FR-MDF-003, UC-MDF-001 step 2): fetch the KAP dividend
/// payload, enforce provenance and store append-only dividend records.
/// </summary>
public sealed class DividendsJob : IIngestJob
{
    /// <summary>Stable job code used by the ledger, scheduler and admin trigger.</summary>
    public const string Code = "dividends";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISourceAdapter<DividendsPayload> _adapter;
    private readonly DividendStore _store;
    private readonly IQuarantineWriter _quarantine;
    private readonly TimeProvider _clock;
    private readonly ILogger<DividendsJob> _logger;

    public DividendsJob(
        ISourceAdapter<DividendsPayload> adapter,
        DividendStore store,
        IQuarantineWriter quarantine,
        TimeProvider clock,
        ILogger<DividendsJob> logger)
    {
        _adapter = adapter;
        _store = store;
        _quarantine = quarantine;
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
        SourcePayload<DividendsPayload> fetched;
        try
        {
            fetched = await _adapter.FetchAsync(request.Date, cancellationToken).ConfigureAwait(false);
        }
        catch (SourcePayloadException exception)
        {
            await QuarantineAsync(null, exception.RawJson, "UNPARSEABLE_PAYLOAD", cancellationToken).ConfigureAwait(false);
            return new IngestResult(Code, 0, 0, 1, IngestResult.Failed);
        }

        if (string.IsNullOrWhiteSpace(fetched.SourceRef))
        {
            _logger.LogWarning("dividends: payload carries no provenance (MISSING_PROVENANCE)");
            await QuarantineAsync(fetched, fetched.RawJson, "MISSING_PROVENANCE", cancellationToken).ConfigureAwait(false);
            return new IngestResult(Code, 0, 0, 1, IngestResult.Failed);
        }

        var result = await _store
            .UpsertAsync(fetched.SourceRef, _clock.GetUtcNow(), fetched.Payload, cancellationToken)
            .ConfigureAwait(false);

        var payloadJson = JsonSerializer.Serialize(fetched.Payload, JsonOptions);
        var quarantined = 0;
        foreach (var symbol in result.UnknownSymbols)
        {
            _logger.LogWarning("dividends: unknown instrument {Symbol} (SCHEMA_MISMATCH)", symbol);
            await QuarantineAsync(fetched, payloadJson, "SCHEMA_MISMATCH", cancellationToken).ConfigureAwait(false);
            quarantined++;
        }

        var status = quarantined > 0 ? IngestResult.Partial : IngestResult.Succeeded;
        return new IngestResult(Code, result.Inserted, result.Unchanged, quarantined, status);
    }

    private Task QuarantineAsync(
        SourcePayload<DividendsPayload>? fetched,
        string payloadJson,
        string reasonCode,
        CancellationToken cancellationToken) =>
        _quarantine.WriteAsync(
            new QuarantineEntry(Code, fetched?.SourceRef, payloadJson, reasonCode, _clock.GetUtcNow()),
            cancellationToken);
}
