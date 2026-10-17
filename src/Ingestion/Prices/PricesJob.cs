using System.Text.Json;
using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.Coverage;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Quarantine;
using Degerli.Ingestion.Sources;
using Degerli.Ingestion.Universe;
using Degerli.Ingestion.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.Prices;

/// <summary>
/// The <c>prices</c> EOD job (FR-MDF-001, UC-MDF-001 step 1). Per 01 v1.5 §10.9.2 the real
/// İş Yatırım endpoint is <b>per-ticker</b>: the job resolves the current constituents of the
/// index named by <see cref="PricesSourceOptions.UniverseIndex"/> (<c>index_constituents</c>,
/// <c>effective_to IS NULL</c>) and iterates them <b>strictly sequentially</b>, paced by
/// <see cref="PricesSourceOptions.RequestDelayMs"/> — the politeness posture against the
/// undocumented public endpoint (no parallelism). The adapter is symbol-scoped; persistence,
/// validation and quarantine stay job-side, which is what makes per-symbol failure isolation
/// expressible (a failed ticker alerts/quarantines without aborting the run).
/// <para>
/// On cold start, when the bound is <see cref="Xu030LaunchSeed.IndexCode"/> (<c>XU030</c>) and
/// the index has no open membership row yet, the checked-in XU030 launch-seed is applied once
/// (01 v1.5 §10.9.3, reusing the TC-MDF-062 machinery).
/// </para>
/// When the request carries a <c>backfillFrom</c> the job runs in <b>backfill mode</b>
/// (FR-MDF-010, UC-MDF-002): it ingests the source's historical range per ticker and records
/// the achieved depth per instrument — never fabricating rows before the source limit.
/// </summary>
public sealed class PricesJob : IIngestJob
{
    /// <summary>Stable job code used by the ledger, scheduler and admin trigger.</summary>
    public const string Code = "prices";

    /// <summary>Reason code for a per-symbol transport failure (a source-down, not a parse event).</summary>
    public const string SourceFetchFailed = "SOURCE_FETCH_FAILED";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly PricesSourceAdapter _adapter;
    private readonly DailyPriceStore _store;
    private readonly CoverageRecorder _coverage;
    private readonly IQuarantineService _quarantine;
    private readonly IIngestionAlerter _alerter;
    private readonly UniverseSyncStore _universe;
    private readonly PricesSourceOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<PricesJob> _logger;

    public PricesJob(
        PricesSourceAdapter adapter,
        DailyPriceStore store,
        CoverageRecorder coverage,
        IQuarantineService quarantine,
        IIngestionAlerter alerter,
        UniverseSyncStore universe,
        IOptions<PricesSourceOptions> options,
        TimeProvider clock,
        ILogger<PricesJob> logger)
    {
        _adapter = adapter;
        _store = store;
        _coverage = coverage;
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
        var constituents = await ResolveConstituentsAsync(cancellationToken).ConfigureAwait(false);
        if (constituents.Count == 0)
        {
            _logger.LogWarning(
                "prices: index {IndexCode} has no current constituents; nothing to fetch for {Date}",
                _options.UniverseIndex,
                request.Date);
            return EmptyBoundResult();
        }

        var written = 0;
        var unchanged = 0;
        var quarantined = 0;
        var failedSymbols = new List<string>();

        for (var index = 0; index < constituents.Count; index++)
        {
            await PaceAsync(index, cancellationToken).ConfigureAwait(false);
            var symbol = constituents[index];

            try
            {
                var fetched = await _adapter
                    .FetchSymbolAsync(symbol, request.Date, cancellationToken)
                    .ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(fetched.SourceRef))
                {
                    // Provenance is required on every fact (FR-MDF-008): refuse this symbol's payload.
                    _logger.LogWarning(
                        "prices: symbol {Symbol} payload for {Date} carries no provenance (MISSING_PROVENANCE)",
                        symbol,
                        request.Date);
                    await QuarantineAsync(request, null, fetched.RawJson, QuarantineReason.MissingProvenance, cancellationToken)
                        .ConfigureAwait(false);
                    quarantined++;
                    continue;
                }

                var outcome = await _store
                    .UpsertAsync(request.Date, fetched.SourceRef, _clock.GetUtcNow(), fetched.Payload.Items, cancellationToken)
                    .ConfigureAwait(false);

                var symbolQuarantined = await QuarantineOutcomeAsync(request, fetched.SourceRef, outcome, cancellationToken)
                    .ConfigureAwait(false);

                written += outcome.Inserted;
                unchanged += outcome.Unchanged;
                quarantined += symbolQuarantined;

                _logger.LogInformation(
                    "prices: symbol {Symbol} on {Date} — written {Written}, unchanged {Unchanged}, quarantined {Quarantined}",
                    symbol,
                    request.Date,
                    outcome.Inserted,
                    outcome.Unchanged,
                    symbolQuarantined);
            }
            catch (SourcePayloadException exception)
            {
                // A per-symbol parse failure is isolated: it quarantines and the loop continues.
                _logger.LogWarning(
                    "prices: symbol {Symbol} payload for {Date} unparseable (UNPARSEABLE_PAYLOAD)",
                    symbol,
                    request.Date);
                await QuarantineAsync(request, null, exception.RawJson, QuarantineReason.UnparseablePayload, cancellationToken)
                    .ConfigureAwait(false);
                quarantined++;
            }
            catch (SourceFetchException exception)
            {
                // A transport failure is NOT a parse/validation event: no quarantine. The
                // symbol is marked failed; the run continues and is alerted once at the end.
                _logger.LogWarning(
                    exception,
                    "prices: symbol {Symbol} fetch failed for {Date} (SOURCE_FETCH_FAILED)",
                    symbol,
                    request.Date);
                failedSymbols.Add(symbol);
            }
        }

        return await CompleteAsync(
            request,
            constituents.Count,
            written,
            unchanged,
            quarantined,
            failedSymbols,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Backfill mode (FR-MDF-010, UC-MDF-002) over the same per-ticker loop: ingests each
    /// current constituent's historical range and records the achieved depth per instrument.
    /// The earliest row the source returns is the real limit; rows before it are never
    /// fabricated (BR-MDF-006).
    /// </summary>
    private async Task<IngestResult> RunBackfillAsync(
        IngestionRequest request,
        CancellationToken cancellationToken)
    {
        var requestedFrom = request.BackfillFrom!.Value;

        var constituents = await ResolveConstituentsAsync(cancellationToken).ConfigureAwait(false);
        if (constituents.Count == 0)
        {
            _logger.LogWarning(
                "prices: index {IndexCode} has no current constituents; nothing to backfill from {From}",
                _options.UniverseIndex,
                requestedFrom);
            return EmptyBoundResult();
        }

        var written = 0;
        var unchanged = 0;
        var quarantined = 0;
        var failedSymbols = new List<string>();
        var coveredSymbols = new HashSet<string>(StringComparer.Ordinal);
        DateOnly? achievedFrom = null;

        for (var index = 0; index < constituents.Count; index++)
        {
            await PaceAsync(index, cancellationToken).ConfigureAwait(false);
            var symbol = constituents[index];

            try
            {
                var fetched = await _adapter
                    .FetchSymbolBackfillAsync(symbol, requestedFrom, cancellationToken)
                    .ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(fetched.SourceRef))
                {
                    _logger.LogWarning(
                        "prices: symbol {Symbol} backfill payload from {From} carries no provenance (MISSING_PROVENANCE)",
                        symbol,
                        requestedFrom);
                    await QuarantineAsync(request, null, fetched.RawJson, QuarantineReason.MissingProvenance, cancellationToken)
                        .ConfigureAwait(false);
                    quarantined++;
                    continue;
                }

                var rows = fetched.Payload.Rows;
                if (rows.Count == 0)
                {
                    _logger.LogInformation(
                        "prices: symbol {Symbol} backfill from {From} returned no history",
                        symbol,
                        requestedFrom);
                    continue;
                }

                var outcome = await _store
                    .UpsertHistoryAsync(fetched.SourceRef, _clock.GetUtcNow(), rows, cancellationToken)
                    .ConfigureAwait(false);

                var symbolQuarantined = await QuarantineOutcomeAsync(request, fetched.SourceRef, outcome, cancellationToken)
                    .ConfigureAwait(false);

                written += outcome.Inserted;
                unchanged += outcome.Unchanged;
                quarantined += symbolQuarantined;

                var symbolFrom = rows.Min(row => row.Date);
                achievedFrom = achievedFrom is null || symbolFrom < achievedFrom ? symbolFrom : achievedFrom;
                coveredSymbols.UnionWith(rows.Select(row => row.Symbol));

                _logger.LogInformation(
                    "prices: symbol {Symbol} backfill from {From} — written {Written}, unchanged {Unchanged}, quarantined {Quarantined}, achieved {Achieved}",
                    symbol,
                    requestedFrom,
                    outcome.Inserted,
                    outcome.Unchanged,
                    symbolQuarantined,
                    symbolFrom);
            }
            catch (SourcePayloadException exception)
            {
                _logger.LogWarning(
                    "prices: symbol {Symbol} backfill payload from {From} unparseable (UNPARSEABLE_PAYLOAD)",
                    symbol,
                    requestedFrom);
                await QuarantineAsync(request, null, exception.RawJson, QuarantineReason.UnparseablePayload, cancellationToken)
                    .ConfigureAwait(false);
                quarantined++;
            }
            catch (SourceFetchException exception)
            {
                _logger.LogWarning(
                    exception,
                    "prices: symbol {Symbol} backfill fetch failed from {From} (SOURCE_FETCH_FAILED)",
                    symbol,
                    requestedFrom);
                failedSymbols.Add(symbol);
            }
        }

        if (coveredSymbols.Count > 0 && achievedFrom is { } from)
        {
            // Record the achieved depth per instrument, with a source-limit note when the
            // requested target was not reached (UC-MDF-002 alternate a, BR-MDF-006).
            await _coverage
                .RecordPricesCoverageAsync(coveredSymbols.ToList(), requestedFrom, from, cancellationToken)
                .ConfigureAwait(false);
        }

        return await CompleteAsync(
            request,
            constituents.Count,
            written,
            unchanged,
            quarantined,
            failedSymbols,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The run's terminal outcome: aggregate the per-symbol counts, emit the run summary and
    /// raise a single aggregate alert naming the failed symbols (TC-MDF-073). Status semantics
    /// (01 v1.5 §10.9.2): every symbol failed → <see cref="IngestResult.Failed"/> (so the retry
    /// ladder applies); some failed, or none failed but some facts were quarantined →
    /// <see cref="IngestResult.Partial"/>; otherwise <see cref="IngestResult.Succeeded"/>.
    /// </summary>
    private async Task<IngestResult> CompleteAsync(
        IngestionRequest request,
        int constituentCount,
        int written,
        int unchanged,
        int quarantined,
        List<string> failedSymbols,
        CancellationToken cancellationToken)
    {
        if (failedSymbols.Count > 0)
        {
            await _alerter.RaiseAsync(
                new IngestionAlert(
                    JobCode: Code,
                    ReasonCode: SourceFetchFailed,
                    Subject: $"Degerli alert: {Code} source fetch failed for {string.Join(", ", failedSymbols)}",
                    BodyTr: $"Fiyat işi {request.Date:yyyy-MM-dd} için şu sembollerin kaynağından veri çekemedi: {string.Join(", ", failedSymbols)}. Diğer semboller işlendi; kaynak hatası karantinaya alınmaz (kaynak hatası, ayrıştırma olayı değildir).",
                    BodyEn: $"The prices job could not fetch data for {string.Join(", ", failedSymbols)} on {request.Date:yyyy-MM-dd}. The remaining symbols were processed; a source failure is not quarantined (it is a source-down, not a parse event).",
                    SourceRef: null),
                cancellationToken).ConfigureAwait(false);
        }

        var status = failedSymbols.Count == constituentCount
            ? IngestResult.Failed
            : failedSymbols.Count > 0 || quarantined > 0
                ? IngestResult.Partial
                : IngestResult.Succeeded;

        _logger.LogInformation(
            "prices: run for {Date} complete — constituents {Constituents}, written {Written}, unchanged {Unchanged}, quarantined {Quarantined}, failed {Failed} ({Status})",
            request.Date,
            constituentCount,
            written,
            unchanged,
            quarantined,
            failedSymbols.Count,
            status);

        return new IngestResult(Code, written, unchanged, quarantined, status);
    }

    /// <summary>
    /// The result of a run whose configured bound resolved to no current constituents. A bound
    /// naming an unpublished index degrades to an empty set (never throws). Under the
    /// production launch default <see cref="Xu030LaunchSeed.IndexCode"/> however, an empty
    /// bound is a real cold-start/misconfiguration gap — the launch-seed materialised no
    /// members — so it is reported as a failure rather than a silent success that would look
    /// healthy in the run ledger while fetching nothing (01 v1.5 §10.9.2).
    /// </summary>
    private IngestResult EmptyBoundResult() =>
        string.Equals(_options.UniverseIndex, Xu030LaunchSeed.IndexCode, StringComparison.Ordinal)
            ? new IngestResult(Code, 0, 0, 0, IngestResult.Failed)
            : new IngestResult(Code, 0, 0, 0, IngestResult.Succeeded);

    /// <summary>
    /// The fetch bound: the current constituents of the configured index (01 v1.5 §10.9.2).
    /// On cold start — the bound is <see cref="Xu030LaunchSeed.IndexCode"/> and the index
    /// carries no open membership row yet — the checked-in XU030 launch-seed is applied once
    /// (01 v1.5 §10.9.3). The seed creates its own index reference row, so the cold-start
    /// path does not depend on the universe snapshot having published that exact code
    /// (FLG-08). A bound naming an unpublished index resolves to an empty set (degrade, never
    /// throw — the instruments the bound needs may not exist yet).
    /// </summary>
    private async Task<IReadOnlyList<string>> ResolveConstituentsAsync(CancellationToken cancellationToken)
    {
        var indexCode = _options.UniverseIndex;

        if (string.Equals(indexCode, Xu030LaunchSeed.IndexCode, StringComparison.Ordinal)
            && !await _universe.HasOpenConstituentsAsync(indexCode, cancellationToken).ConfigureAwait(false))
        {
            var seed = await Xu030LaunchSeed
                .ApplyAsync(_universe, _coverage, _clock.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "prices: XU030 launch-seed applied — seeded {Seeded}, already present {AlreadyPresent}, unknown {Unknown}",
                seed.Seeded,
                seed.AlreadyPresent,
                seed.UnknownSymbols.Count);

            foreach (var symbol in seed.UnknownSymbols)
            {
                _logger.LogWarning(
                    "prices: launch-seed symbol {Symbol} resolved to no stored instrument (UNKNOWN_REFERENCE)",
                    symbol);
            }
        }

        return await _universe
            .GetCurrentConstituentSymbolsAsync(indexCode, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Paces the strictly sequential loop (01 v1.5 §10.9.2): waits
    /// <see cref="PricesSourceOptions.RequestDelayMs"/> between requests, never before the
    /// first. A non-positive delay disables the wait (deterministic L2 runs). The wait is a
    /// real wall-clock timer — the delay is a politeness measure, not a scheduling decision —
    /// so the TC-MDF-074 lower-bound timing assertion reflects the live posture.
    /// </summary>
    private async Task PaceAsync(int index, CancellationToken cancellationToken)
    {
        if (index == 0 || _options.RequestDelayMs <= 0)
        {
            return;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(_options.RequestDelayMs), cancellationToken).ConfigureAwait(false);
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
