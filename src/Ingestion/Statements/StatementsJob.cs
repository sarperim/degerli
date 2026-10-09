using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Quarantine;
using Degerli.Ingestion.Sources;
using Microsoft.Extensions.Logging;

namespace Degerli.Ingestion.Statements;

/// <summary>
/// The <c>statements</c> job (FR-MDF-002, UC-MDF-001 step 2): fetch the KAP statement
/// payload, enforce provenance, map to the canonical chart of accounts and store versioned
/// statements idempotently. Anything refused is quarantined, never written.
/// </summary>
public sealed class StatementsJob : IIngestJob
{
    /// <summary>Stable job code used by the ledger, scheduler and admin trigger.</summary>
    public const string Code = "statements";

    private readonly ISourceAdapter<StatementsPayload> _adapter;
    private readonly FinancialStatementStore _store;
    private readonly IQuarantineWriter _quarantine;
    private readonly TimeProvider _clock;
    private readonly ILogger<StatementsJob> _logger;

    public StatementsJob(
        ISourceAdapter<StatementsPayload> adapter,
        FinancialStatementStore store,
        IQuarantineWriter quarantine,
        TimeProvider clock,
        ILogger<StatementsJob> logger)
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
        SourcePayload<StatementsPayload> fetched;
        try
        {
            fetched = await _adapter.FetchAsync(request.Date, cancellationToken).ConfigureAwait(false);
        }
        catch (SourcePayloadException exception)
        {
            await QuarantineAsync(fetched: null, exception.RawJson, "UNPARSEABLE_PAYLOAD", cancellationToken)
                .ConfigureAwait(false);
            return new IngestResult(Code, 0, 0, 1, IngestResult.Failed);
        }

        if (string.IsNullOrWhiteSpace(fetched.SourceRef))
        {
            _logger.LogWarning("statements: payload carries no provenance (MISSING_PROVENANCE)");
            await QuarantineAsync(fetched, fetched.RawJson, "MISSING_PROVENANCE", cancellationToken)
                .ConfigureAwait(false);
            return new IngestResult(Code, 0, 0, 1, IngestResult.Failed);
        }

        var result = await _store
            .UpsertAsync(fetched.SourceRef, _clock.GetUtcNow(), fetched.Payload, cancellationToken)
            .ConfigureAwait(false);

        var quarantined = 0;
        foreach (var reasonCode in result.RejectedReasonCodes)
        {
            _logger.LogWarning("statements: refused payload for {Symbol} ({ReasonCode})", result.Symbol, reasonCode);
            await QuarantineAsync(fetched, fetched.RawJson, reasonCode, cancellationToken).ConfigureAwait(false);
            quarantined++;
        }

        var status = quarantined > 0 ? IngestResult.Partial : IngestResult.Succeeded;
        return new IngestResult(Code, result.Inserted, result.Unchanged, quarantined, status);
    }

    private Task QuarantineAsync(
        SourcePayload<StatementsPayload>? fetched,
        string payloadJson,
        string reasonCode,
        CancellationToken cancellationToken) =>
        _quarantine.WriteAsync(
            new QuarantineEntry(Code, fetched?.SourceRef, payloadJson, reasonCode, _clock.GetUtcNow()),
            cancellationToken);
}
