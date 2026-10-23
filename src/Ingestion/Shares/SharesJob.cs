using System.Text.Json;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Kap;
using Degerli.Ingestion.Quarantine;
using Degerli.Ingestion.Sources;
using Microsoft.Extensions.Logging;

namespace Degerli.Ingestion.Shares;

/// <summary>
/// The <c>shares</c> job (FR-MDF-002/003's lane, 01 v1.5.36 §10.9.11): one paced
/// <c>/memberSecurities</c> fetch, decode, and the per-instrument share-count store write.
/// Admin-triggerable now; a scheduled cadence is a later decision (share capital changes rarely),
/// deliberately not wired into either ingest chain by this ticket.
/// <para>
/// A malformed body quarantines <c>UNPARSEABLE_PAYLOAD</c>; a transport failure propagates
/// (<see cref="SourceFetchException"/>) so the runner records the run <c>failed</c> — never a
/// fabricated empty success. Lines the source carried without a <c>currentCapital</c> on one of
/// <b>our</b> matched instruments are skipped, <b>counted</b> in the run's <c>skipped</c> stat,
/// and never zero-filled.
/// </para>
/// </summary>
public sealed class SharesJob : IIngestJob
{
    /// <summary>Stable job code used by the ledger, the admin trigger and the summary.</summary>
    public const string Code = "shares";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISourceAdapter<SharesPayload> _adapter;
    private readonly ShareCountStore _store;
    private readonly IQuarantineWriter _quarantine;
    private readonly TimeProvider _clock;
    private readonly ILogger<SharesJob> _logger;

    public SharesJob(
        ISourceAdapter<SharesPayload> adapter,
        ShareCountStore store,
        IQuarantineWriter quarantine,
        TimeProvider clock,
        ILogger<SharesJob> logger)
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
        SourcePayload<SharesPayload> fetched;
        try
        {
            fetched = await _adapter.FetchAsync(request.Date, cancellationToken).ConfigureAwait(false);
        }
        catch (SourcePayloadException exception)
        {
            await QuarantineAsync(null, exception.RawJson, "UNPARSEABLE_PAYLOAD", cancellationToken).ConfigureAwait(false);
            return new IngestResult(Code, 0, 0, 1, IngestResult.Failed);
        }

        var result = await _store
            .UpsertAsync(fetched.SourceRef ?? _adapter.SourceName, _clock.GetUtcNow(), fetched.Payload, cancellationToken)
            .ConfigureAwait(false);

        if (result.SkippedLines > 0)
        {
            // Honest, counted gap: the line exists but carries no currentCapital. It is never
            // written as a zero, and a matched group containing it is withheld rather than
            // served as a partial tertip sum.
            _logger.LogWarning(
                "shares: {Skipped} security line(s) of our instruments carry no currentCapital and were skipped (never zero-filled)",
                result.SkippedLines);
        }

        if (result.Removed > 0)
        {
            // A group withheld this run loses its previously stored row: a stale count is never
            // left being served (the ticket's "a matched-but-unreadable group must yield no row").
            _logger.LogWarning(
                "shares: {Removed} previously stored row(s) withdrawn because this run withheld the instrument's group",
                result.Removed);
        }

        // The run succeeded when the source answered and the write completed: a skipped line is
        // a recorded data gap, not a failed run (the 052 doctrine — absence is stated, not
        // invented, and it does not make the lane's freshness signal lie).
        return new IngestResult(
            Code,
            result.Written,
            result.Unchanged,
            0,
            IngestResult.Succeeded,
            Skipped: result.SkippedLines);
    }

    private Task QuarantineAsync(
        SourcePayload<SharesPayload>? fetched,
        string payloadJson,
        string reasonCode,
        CancellationToken cancellationToken) =>
        _quarantine.WriteAsync(
            new QuarantineEntry(Code, fetched?.SourceRef, payloadJson, reasonCode, _clock.GetUtcNow()),
            cancellationToken);
}
