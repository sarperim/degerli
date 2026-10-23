using System.Text.Json;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Kap;
using Degerli.Ingestion.Quarantine;
using Degerli.Ingestion.Sources;
using Microsoft.Extensions.Logging;

namespace Degerli.Ingestion.Dividends;

/// <summary>
/// The <c>dividends</c> job (FR-MDF-003, UC-MDF-001 step 2): fetch the KAP dividend
/// payload, enforce provenance and store append-only dividend records.
/// <para>
/// The company-scoped VYK walk lives in the adapter (01 v1.5 §10.9.15), so the job's part of the
/// §10.9.10/§10.9.11 per-company isolation doctrine (FR-MDF-012) is to carry the adapter's
/// isolated bound symbols into the run ledger: an unmapped symbol is never a whole-run failure —
/// the run ends <c>partial</c> with that symbol recorded (and named) in the per-symbol stats.
/// </para>
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

        // The adapter's per-constituent identity isolation (01 v1.5 §10.9.15, FR-MDF-012): the
        // symbols its loop skipped because the VYK directory could not map them. They ride the
        // run's per-symbol stats with the named reason logged, so the run reports what it could
        // not fetch instead of failing or silently looking complete.
        var unresolved = _adapter is IVykIdentityIsolationReporter reporter
            ? reporter.UnresolvedSymbols
            : [];

        if (unresolved.Count > 0)
        {
            _logger.LogWarning(
                "dividends: {Count} bound symbol(s) isolated ({ReasonCode}): {Symbols}",
                unresolved.Count,
                VykCompanyDirectory.CompanyIdentityUnresolved,
                string.Join(", ", unresolved));
        }

        // The adapter's per-disclosure isolation (01 v1.5.40 §10.9.15, TKT-foundation-061): the
        // disclosure details its walk could not fetch — an HTTP error, a fault envelope, a
        // transport failure or a per-item timeout (the 2026-10-22 500 on /disclosureDetail/1185709
        // and the 90-minute hang it bounded) — ride the run as quarantine rows carrying the
        // gateway's own error, counted with the named reason. One bad attachment is an honest
        // partial: never a failed run that fail-stops the chain, never a silent omission.
        var detailSkips = _adapter is IDisclosureIsolationReporter detailReporter
            ? detailReporter.DetailSkips
            : [];

        foreach (var skip in detailSkips)
        {
            _logger.LogWarning(
                "{Job}: disclosure detail {Index} for {Symbol} could not be fetched and is quarantined with evidence ({ReasonCode}, {FailureKind}, status {Status}): {Error}",
                Code,
                skip.DisclosureIndex,
                skip.Symbol,
                skip.ReasonCode,
                skip.FailureKind,
                skip.StatusCode,
                skip.Error);
            await QuarantineAsync(
                    fetched,
                    skip.ToEvidenceJson(),
                    VykDisclosureWalker.DisclosureDetailUnfetchable,
                    cancellationToken)
                .ConfigureAwait(false);
            quarantined++;
        }

        // Per-symbol stats carry both isolation classes additively: the 046 identity isolation
        // (failed) and this ticket's per-disclosure skips (quarantined) — the 046 shape plus the
        // skip count, so the pre-061 family-lane goldens only see new fields/rows.
        var symbolStats = unresolved.Select(symbol => new SymbolRunStat(symbol, 0, 0, 0, Failed: true)).ToList();
        foreach (var group in detailSkips.GroupBy(skip => skip.Symbol, StringComparer.Ordinal))
        {
            symbolStats.Add(new SymbolRunStat(group.Key, 0, 0, group.Count(), Failed: false));
        }

        var status = quarantined > 0 || unresolved.Count > 0 ? IngestResult.Partial : IngestResult.Succeeded;
        return new IngestResult(Code, result.Inserted, result.Unchanged, quarantined, status)
        {
            SymbolStats = symbolStats,
        };
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
