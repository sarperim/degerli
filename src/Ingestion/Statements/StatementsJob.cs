using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Kap;
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

    /// <summary>
    /// Reason code for a bound symbol the VYK directory could not map to a
    /// <c>companyId</c> (FR-MDF-008/FR-MDF-012, 01 v1.5 §10.9.11): the company is marked
    /// failed and reported, never dropped and never written under a fabricated identity.
    /// </summary>
    public const string CompanyIdentityUnresolved = "COMPANY_IDENTITY_UNRESOLVED";

    private readonly StatementsSourceAdapter _adapter;
    private readonly FinancialStatementStore _store;
    private readonly IQuarantineWriter _quarantine;
    private readonly IIngestionAlerter _alerter;
    private readonly UniverseSyncStore _universe;
    private readonly KapDisclosureCursorStore _cursorStore;
    private readonly StatementsSourceOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<StatementsJob> _logger;

    public StatementsJob(
        StatementsSourceAdapter adapter,
        FinancialStatementStore store,
        IQuarantineWriter quarantine,
        IIngestionAlerter alerter,
        UniverseSyncStore universe,
        KapDisclosureCursorStore cursorStore,
        IOptions<StatementsSourceOptions> options,
        TimeProvider clock,
        ILogger<StatementsJob> logger)
    {
        _adapter = adapter;
        _store = store;
        _quarantine = quarantine;
        _alerter = alerter;
        _universe = universe;
        _cursorStore = cursorStore;
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
        var identityIsolated = 0;
        var cursorResets = 0;
        var symbolStats = new List<SymbolRunStat>(constituents.Count);

        for (var index = 0; index < constituents.Count; index++)
        {
            await PaceAsync(index, cancellationToken).ConfigureAwait(false);
            var symbol = constituents[index];

            try
            {
                // The per-company incremental cursor (01 v1.5 §10.9.11): only the VYK source
                // is cursor-driven; the single-resource canonical/XBRL path ignores it (and
                // consults no cursor row), so its L2 goldens are byte-for-byte unchanged.
                var cursor = _adapter.VykEnabled
                    ? await _cursorStore.GetAsync(symbol, cancellationToken).ConfigureAwait(false)
                    : null;

                var fetched = await _adapter
                    .FetchCompanyAsync(symbol, cursor, cancellationToken)
                    .ConfigureAwait(false);
                var source = fetched.Source;

                if (string.IsNullOrWhiteSpace(source.SourceRef))
                {
                    // Provenance is required on every fact (FR-MDF-008): refuse this company's payload.
                    _logger.LogWarning(
                        "statements: company {Symbol} payload for {Date} carries no provenance (MISSING_PROVENANCE)",
                        symbol,
                        request.Date);
                    await QuarantineAsync(null, source.RawJson, QuarantineReason.MissingProvenance, cancellationToken)
                        .ConfigureAwait(false);
                    quarantined++;
                    symbolStats.Add(new SymbolRunStat(symbol, 0, 0, 1, false));
                    continue;
                }

                var outcome = await _store
                    .UpsertAsync(source.SourceRef, _clock.GetUtcNow(), source.Payload, cancellationToken)
                    .ConfigureAwait(false);

                var symbolQuarantined = 0;
                foreach (var reasonCode in outcome.RejectedReasonCodes)
                {
                    _logger.LogWarning(
                        "statements: refused payload for {Symbol} ({ReasonCode})", outcome.Symbol, reasonCode);
                    await QuarantineAsync(source.SourceRef, source.RawJson, reasonCode, cancellationToken)
                        .ConfigureAwait(false);
                    symbolQuarantined++;
                }

                // The per-disclosure isolation evidence (01 v1.5.40/v1.5.42 §10.9.15,
                // TKT-foundation-063): each detail the shared walker could not fetch — an HTTP
                // error, a fault envelope, a transport failure or the bounded timeout — is
                // quarantined with the gateway's own answer verbatim and counted, never a failed
                // company and never a silent omission. The other disclosures' facts were written
                // above; the company's cursor already advanced past the recorded index.
                var skips = fetched.DetailSkips;
                foreach (var skip in skips)
                {
                    _logger.LogWarning(
                        "statements: disclosure detail {Index} for {Symbol} could not be fetched and is quarantined with evidence ({ReasonCode}, {FailureKind}, status {Status}): {Error}",
                        skip.DisclosureIndex,
                        skip.Symbol,
                        skip.ReasonCode,
                        skip.FailureKind,
                        skip.StatusCode,
                        skip.Error);
                    await QuarantineAsync(
                            source.SourceRef,
                            skip.ToEvidenceJson(),
                            VykDisclosureWalker.DisclosureDetailUnfetchable,
                            cancellationToken)
                        .ConfigureAwait(false);
                    symbolQuarantined++;
                }

                written += outcome.Inserted;
                unchanged += outcome.Unchanged;
                quarantined += symbolQuarantined;

                // Advance the company's cursor to the last-processed disclosure index (the
                // disclosure was decoded and persisted — or idempotently collapsed — so the
                // next run resumes after it; FR-MDF-010/011). A stale-cursor reset (01 v1.5.42
                // §10.9.15, TKT-foundation-063) is recorded verbatim and its outcome persisted:
                // the corrected cursor when the re-walk emitted facts, or a cleared row when the
                // re-walk was itself honest-empty — so the next run never repeats the same ER005.
                if (fetched.CursorReset is { } cursorReset)
                {
                    cursorResets++;
                    _logger.LogWarning(
                        "statements: stale disclosure cursor reset for {Symbol}: {From} -> {To} ({Code}); gateway body: {Body}. {Error}",
                        cursorReset.Symbol,
                        cursorReset.FromCursor,
                        cursorReset.ToIndex,
                        cursorReset.Code,
                        cursorReset.Body ?? string.Empty,
                        cursorReset.Error);
                }

                if (fetched.Cursor is not null)
                {
                    await _cursorStore.SetAsync(symbol, fetched.Cursor, cancellationToken).ConfigureAwait(false);
                }
                else if (fetched.CursorReset is not null)
                {
                    // The re-walk was honest-empty; keep the stale value would repeat ER005 forever.
                    await _cursorStore.ClearAsync(symbol, cancellationToken).ConfigureAwait(false);
                }

                symbolStats.Add(new SymbolRunStat(symbol, outcome.Inserted, outcome.Unchanged, symbolQuarantined, false));
            }
            catch (CompanyIdentityUnresolvedException)
            {
                // The bound symbol has no VYK companyId (FR-MDF-008/FR-MDF-012): the company is
                // isolated with the named reason, writes nothing, is named in the per-company
                // stats and never fabricates a mapping — and, per the 046/v1.5.42 honesty pin,
                // never fail-stops the chain on its own.
                _logger.LogWarning(
                    "statements: company {Symbol} could not be mapped to a VYK companyId for {Date} ({ReasonCode}); isolated and the loop continues",
                    symbol,
                    request.Date,
                    CompanyIdentityUnresolved);
                identityIsolated++;
                symbolStats.Add(new SymbolRunStat(symbol, 0, 0, 0, true));
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
            identityIsolated,
            cursorResets,
            symbolStats,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The run's terminal outcome: aggregate the per-company counts, emit the run summary and
    /// raise a single aggregate alert naming the failed companies (the TC-MDF-073 posture).
    /// <para>
    /// <b>Honest status (01 v1.5.42 §10.9.15, TKT-foundation-063).</b> A run is
    /// <see cref="IngestResult.Failed"/> only when <b>every</b> constituent failed for a genuine
    /// source reason (transport/list non-success, or an unparseable payload) — so the retry
    /// ladder keeps its meaning. A run whose writes are zero because companies were honestly
    /// skipped — a stale-cursor reset, an unfetchable detail, an unresolved identity, a
    /// quarantine — is <see cref="IngestResult.Partial"/>, never <c>failed</c> and never a
    /// chain fail-stop (the 2026-10-23 run-212 shape). Some failed or some facts quarantined →
    /// <see cref="IngestResult.Partial"/>; otherwise <see cref="IngestResult.Succeeded"/>.
    /// </para>
    /// </summary>
    private async Task<IngestResult> CompleteAsync(
        IngestionRequest request,
        int constituentCount,
        int written,
        int unchanged,
        int quarantined,
        List<string> failedSymbols,
        int parseFailed,
        int identityIsolated,
        int cursorResets,
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

        // Every constituent a genuine source failure → failed (the retry ladder's meaning). A
        // parse-only world keeps the pre-063 posture (all unparseable → failed).
        var allGenuinelyFailed = failedSymbols.Count + parseFailed == constituentCount;

        var status = allGenuinelyFailed
            ? IngestResult.Failed
            : failedSymbols.Count > 0
                || quarantined > 0
                || identityIsolated > 0
                || (written == 0 && cursorResets > 0)
                    ? IngestResult.Partial
                    : IngestResult.Succeeded;

        _logger.LogInformation(
            "statements: run for {Date} complete — constituents {Constituents}, written {Written}, unchanged {Unchanged}, quarantined {Quarantined}, failed {Failed}, identity-isolated {IdentityIsolated}, cursor-resets {CursorResets} ({Status})",
            request.Date,
            constituentCount,
            written,
            unchanged,
            quarantined,
            failedSymbols.Count,
            identityIsolated,
            cursorResets,
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
