using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Quarantine;
using Degerli.Ingestion.Sources;
using Degerli.Ingestion.Universe;
using Degerli.Ingestion.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.Statements;

/// <summary>
/// The <c>statements</c> job (FR-MDF-002, UC-MDF-001 step 2). Per 01 v1.5 §10.9.2 the real
/// KAP financial-statement resource is <b>per-company</b> (the §10.9.5 envelope carries one
/// <c>companyCode</c> per payload): the job resolves the current constituents of the index
/// named by <see cref="StatementsSourceOptions.UniverseIndex"/> (<c>index_constituents</c>,
/// <c>effective_to IS NULL</c>) and iterates them <b>strictly sequentially</b>, paced by
/// <see cref="StatementsSourceOptions.RequestDelayMs"/>. The adapter is company-scoped;
/// persistence, validation, quarantine and pacing stay job-side, which is what makes
/// per-company failure isolation expressible (a failed company alerts/quarantines without
/// aborting the run; the run-ledger row carries per-company stats). Everything refused is
/// quarantined, never written.
/// <para>
/// Backfill mode (FR-MDF-010, UC-MDF-002) reuses the same per-company loop; the KAP resource
/// returns the periods the source holds, so the achieved depth is what was stored — never
/// fabricated beyond the source. The per-instrument statements coverage depth is derived from
/// the stored statements by <see cref="Degerli.Ingestion.Coverage.CoverageRecorder"/>'s
/// no-silent-gaps reconcile (FR-MDF-011), not re-invented here.
/// </para>
/// </summary>
public sealed class StatementsJob : IIngestJob
{
    /// <summary>Stable job code used by the ledger, scheduler and admin trigger.</summary>
    public const string Code = "statements";

    /// <summary>Reason code for a per-company transport failure (a source-down, not a parse event).</summary>
    public const string SourceFetchFailed = "SOURCE_FETCH_FAILED";

    private readonly StatementsSourceAdapter _adapter;
    private readonly FinancialStatementStore _store;
    private readonly IQuarantineWriter _quarantine;
    private readonly IIngestionAlerter _alerter;
    private readonly UniverseSyncStore _universe;
    private readonly StatementsSourceOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<StatementsJob> _logger;

    public StatementsJob(
        StatementsSourceAdapter adapter,
        FinancialStatementStore store,
        IQuarantineWriter quarantine,
        IIngestionAlerter alerter,
        UniverseSyncStore universe,
        IOptions<StatementsSourceOptions> options,
        TimeProvider clock,
        ILogger<StatementsJob> logger)
    {
        _adapter = adapter;
        _store = store;
        _quarantine = quarantine;
        _alerter = alerter;
        _universe = universe;
        _options = options.Value;
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
        // The fetch bound: the current constituents of the configured index (01 v1.5 §10.9.2).
        // A bound naming an unpublished index resolves to an empty set (degrade, never throw —
        // the instruments the bound needs may not exist yet).
        var constituents = await ResolveConstituentsAsync(cancellationToken).ConfigureAwait(false);
        if (constituents.Count == 0)
        {
            _logger.LogWarning(
                "statements: index {IndexCode} has no current constituents; nothing to fetch for {Date}",
                _options.UniverseIndex,
                request.Date);
            return EmptyBoundResult();
        }

        var written = 0;
        var unchanged = 0;
        var quarantined = 0;
        var failedSymbols = new List<string>();
        var parseFailed = 0;
        var symbolStats = new List<SymbolRunStat>(constituents.Count);

        for (var index = 0; index < constituents.Count; index++)
        {
            await PaceAsync(index, cancellationToken).ConfigureAwait(false);
            var symbol = constituents[index];

            try
            {
                var fetched = await _adapter
                    .FetchCompanyAsync(symbol, request.Date, cancellationToken)
                    .ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(fetched.SourceRef))
                {
                    // Provenance is required on every fact (FR-MDF-008): refuse this company's payload.
                    _logger.LogWarning(
                        "statements: company {Symbol} payload for {Date} carries no provenance (MISSING_PROVENANCE)",
                        symbol,
                        request.Date);
                    await QuarantineAsync(null, fetched.RawJson, QuarantineReason.MissingProvenance, cancellationToken)
                        .ConfigureAwait(false);
                    quarantined++;
                    symbolStats.Add(new SymbolRunStat(symbol, 0, 0, 1, false));
                    continue;
                }

                var outcome = await _store
                    .UpsertAsync(fetched.SourceRef, _clock.GetUtcNow(), fetched.Payload, cancellationToken)
                    .ConfigureAwait(false);

                var symbolQuarantined = 0;
                foreach (var reasonCode in outcome.RejectedReasonCodes)
                {
                    _logger.LogWarning(
                        "statements: refused payload for {Symbol} ({ReasonCode})", outcome.Symbol, reasonCode);
                    await QuarantineAsync(fetched.SourceRef, fetched.RawJson, reasonCode, cancellationToken)
                        .ConfigureAwait(false);
                    symbolQuarantined++;
                }

                written += outcome.Inserted;
                unchanged += outcome.Unchanged;
                quarantined += symbolQuarantined;

                symbolStats.Add(new SymbolRunStat(symbol, outcome.Inserted, outcome.Unchanged, symbolQuarantined, false));
            }
            catch (SourcePayloadException exception)
            {
                // A per-company parse failure is isolated: it quarantines and the loop continues.
                _logger.LogWarning(
                    "statements: company {Symbol} payload for {Date} unparseable (UNPARSEABLE_PAYLOAD)",
                    symbol,
                    request.Date);
                await QuarantineAsync(null, exception.RawJson, QuarantineReason.UnparseablePayload, cancellationToken)
                    .ConfigureAwait(false);
                quarantined++;
                parseFailed++;
                symbolStats.Add(new SymbolRunStat(symbol, 0, 0, 1, false));
            }
            catch (SourceFetchException exception)
            {
                // A transport failure is NOT a parse/validation event: no quarantine. The
                // company is marked failed; the run continues and is alerted once at the end.
                _logger.LogWarning(
                    exception,
                    "statements: company {Symbol} fetch failed for {Date} (SOURCE_FETCH_FAILED)",
                    symbol,
                    request.Date);
                failedSymbols.Add(symbol);
                symbolStats.Add(new SymbolRunStat(symbol, 0, 0, 0, true));
            }
        }

        return await CompleteAsync(
            request,
            constituents.Count,
            written,
            unchanged,
            quarantined,
            failedSymbols,
            parseFailed,
            symbolStats,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The run's terminal outcome: aggregate the per-company counts, emit the run summary and
    /// raise a single aggregate alert naming the failed companies (the TC-MDF-073 posture). A
    /// run with <b>no</b> writable company — every current constituent failed to fetch or to
    /// parse — is a failed run (so the retry ladder applies); some failed or some facts
    /// quarantined → <see cref="IngestResult.Partial"/>; otherwise <see cref="IngestResult.Succeeded"/>.
    /// </summary>
    private async Task<IngestResult> CompleteAsync(
        IngestionRequest request,
        int constituentCount,
        int written,
        int unchanged,
        int quarantined,
        List<string> failedSymbols,
        int parseFailed,
        IReadOnlyList<SymbolRunStat> symbolStats,
        CancellationToken cancellationToken)
    {
        if (failedSymbols.Count > 0)
        {
            await _alerter.RaiseAsync(
                new IngestionAlert(
                    JobCode: Code,
                    ReasonCode: SourceFetchFailed,
                    Subject: $"Degerli alert: {Code} source fetch failed for {string.Join(", ", failedSymbols)}",
                    BodyTr: $"Finansal tablo işi {request.Date:yyyy-MM-dd} için şu şirketlerin kaynağından veri çekemedi: {string.Join(", ", failedSymbols)}. Diğer şirketler işlendi; kaynak hatası karantinaya alınmaz (kaynak hatası, ayrıştırma olayı değildir).",
                    BodyEn: $"The statements job could not fetch data for {string.Join(", ", failedSymbols)} on {request.Date:yyyy-MM-dd}. The remaining companies were processed; a source failure is not quarantined (it is a source-down, not a parse event).",
                    SourceRef: null),
                cancellationToken).ConfigureAwait(false);
        }

        var status = failedSymbols.Count + parseFailed == constituentCount
            ? IngestResult.Failed
            : failedSymbols.Count > 0 || quarantined > 0
                ? IngestResult.Partial
                : IngestResult.Succeeded;

        _logger.LogInformation(
            "statements: run for {Date} complete — constituents {Constituents}, written {Written}, unchanged {Unchanged}, quarantined {Quarantined}, failed {Failed} ({Status})",
            request.Date,
            constituentCount,
            written,
            unchanged,
            quarantined,
            failedSymbols.Count,
            status);

        return new IngestResult(Code, written, unchanged, quarantined, status) { SymbolStats = symbolStats };
    }

    /// <summary>
    /// The result of a run whose configured bound resolved to no current constituents. A bound
    /// naming an unpublished index degrades to an empty set (never throws). Under the production
    /// launch default <see cref="Xu030LaunchSeed.IndexCode"/> however, an empty bound is a real
    /// cold-start/misconfiguration gap — the universe sync materialised no members — so it is
    /// reported as a failure rather than a silent success that would look healthy in the run
    /// ledger while fetching nothing (01 v1.5 §10.9.2, the prices-job posture).
    /// </summary>
    private IngestResult EmptyBoundResult() =>
        string.Equals(_options.UniverseIndex, Xu030LaunchSeed.IndexCode, StringComparison.Ordinal)
            ? new IngestResult(Code, 0, 0, 0, IngestResult.Failed)
            : new IngestResult(Code, 0, 0, 0, IngestResult.Succeeded);

    private Task<IReadOnlyList<string>> ResolveConstituentsAsync(CancellationToken cancellationToken) =>
        _universe.GetCurrentConstituentSymbolsAsync(_options.UniverseIndex, cancellationToken);

    /// <summary>
    /// Paces the strictly sequential loop (01 v1.5 §10.9.2): waits
    /// <see cref="StatementsSourceOptions.RequestDelayMs"/> between requests, never before the
    /// first. A non-positive delay disables the wait (deterministic L2 runs). The wait is a real
    /// wall-clock timer — a politeness measure, not a scheduling decision — so the TC-MDF-074
    /// lower-bound timing assertion reflects the live posture.
    /// </summary>
    private async Task PaceAsync(int index, CancellationToken cancellationToken)
    {
        if (index == 0 || _options.RequestDelayMs <= 0)
        {
            return;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(_options.RequestDelayMs), cancellationToken).ConfigureAwait(false);
    }

    private Task QuarantineAsync(
        string? sourceRef,
        string? payloadJson,
        string reasonCode,
        CancellationToken cancellationToken) =>
        _quarantine.WriteAsync(
            new QuarantineEntry(Code, sourceRef, payloadJson, reasonCode, _clock.GetUtcNow()),
            cancellationToken);
}
