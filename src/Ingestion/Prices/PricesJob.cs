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
/// <para>
/// <b>Completeness gate (01 v1.5 §10.9.2 amendment).</b> İş Yatırım publishes a day's rows
/// progressively, so a symbol can legitimately answer with a valid payload carrying <b>no
/// session row</b> for the requested date — the honest empty, counted and named as
/// <see cref="NoRowForDate"/> rather than recorded as "nothing happened". After the first
/// pass the job reads the day's stored coverage of the bound index
/// (<see cref="DailyPriceStore.GetDayCoverageAsync"/>, the same read the morning chain's repair
/// gate uses) and, while it is not <see cref="PriceDayCoverage.IsComplete"/>, re-sweeps only
/// the symbols that answered empty — bounded by
/// <see cref="PricesSourceOptions.CoverageMaxPasses"/> and
/// <see cref="PricesSourceOptions.CoverageBudget"/>, spaced by
/// <see cref="PricesSourceOptions.CoverageSettleDelay"/>. Only a <b>fully covered</b> day is
/// <see cref="IngestResult.Succeeded"/>; a day that still has uncovered constituents is
/// <see cref="IngestResult.Partial"/> with the missing names in the run stats and the summary
/// line. `Failed` keeps its meaning (transport-level, retry-worthy).
/// </para>
/// </summary>
public sealed class PricesJob : IIngestJob
{
    /// <summary>Stable job code used by the ledger, scheduler and admin trigger.</summary>
    public const string Code = "prices";

    /// <summary>Reason code for a per-symbol transport failure (a source-down, not a parse event).</summary>
    public const string SourceFetchFailed = "SOURCE_FETCH_FAILED";

    /// <summary>
    /// Reason code for the honest empty (01 v1.5 §10.9.2 amendment): the source answered with
    /// a valid payload that carries <b>no session row for the requested date</b>. This is the
    /// progressive-publication class — İş Yatırım publishes a day's rows some time after the
    /// close, so the same symbol legitimately answers EMPTY at 17:36 and full OHLCV at 20:42.
    /// It is <i>not</i> a source failure (nothing to alert or quarantine) and it is <i>not</i>
    /// "nothing to do": a symbol carrying this reason is named in the run stats and in the
    /// summary line, and an uncovered day is never reported as <see cref="IngestResult.Succeeded"/>.
    /// </summary>
    public const string NoRowForDate = "NO_ROW_FOR_DATE";

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

    /// <summary>
    /// The incremental EOD run over the bound index's current constituents, repeated — bounded
    /// — until the day's coverage is complete (01 v1.5 §10.9.2 amendment, the completeness
    /// gate). The first pass fetches every constituent; if the day is still not complete
    /// (<see cref="PriceDayCoverage.IsComplete"/> on the bound) and some constituents answered
    /// <see cref="NoRowForDate"/>, only those are re-swept, after the configured settle delay,
    /// up to <see cref="PricesSourceOptions.CoverageMaxPasses"/> passes and within
    /// <see cref="PricesSourceOptions.CoverageBudget"/>. A symbol that was empty on pass 1 and
    /// has data on pass 2 is <b>written</b>; a day that still ends uncovered is reported
    /// <see cref="IngestResult.Partial"/> and the missing names ride the run stats and the
    /// summary line — never a silent success.
    /// <para>
    /// Re-sweeps target the honest-empty class <b>only</b>: a quarantined symbol
    /// (<c>MISSING_PROVENANCE</c>/<c>UNPARSEABLE_PAYLOAD</c>/<c>NEGATIVE_PRICE</c>/conflict) and
    /// a transport failure are already terminal for this run — re-fetching them would multiply
    /// quarantine noise (the TKT-foundation-035 lesson) or paper over a source-down the retry
    /// ladder owns. Both still make the run <see cref="IngestResult.Partial"/> through the
    /// coverage read.
    /// </para>
    /// </summary>
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
        var symbolStats = new List<SymbolRunStat>(constituents.Count);
        // Where each symbol's stat lives, so a re-swept symbol replaces its entry instead of
        // appending a duplicate — one entry per constituent, merged across passes.
        var statSlots = new Dictionary<string, int>(StringComparer.Ordinal);

        var maxPasses = Math.Max(1, _options.CoverageMaxPasses);
        var sweepStartedAt = _clock.GetUtcNow();
        var pending = constituents;
        var coverage = new PriceDayCoverage(0, 0);
        // Run-global, not pass-local: the politeness posture (§10.9.2 — strictly sequential,
        // spaced) is a property of the whole run, so a re-sweep's first fetch must wait after
        // the previous pass's last fetch too. Only the run's very first request is unpaced.
        var requestsMade = 0;

        for (var pass = 1; ; pass++)
        {
            var answeredEmpty = new List<string>();

            for (var index = 0; index < pending.Count; index++)
            {
                await PaceAsync(requestsMade, cancellationToken).ConfigureAwait(false);
                requestsMade++;
                var symbol = pending[index];

                var sweep = await SweepSymbolAsync(symbol, request, cancellationToken).ConfigureAwait(false);

                written += sweep.Stat.Written;
                unchanged += sweep.Stat.Unchanged;
                quarantined += sweep.Quarantined;
                if (sweep.Stat.Failed && !failedSymbols.Contains(symbol, StringComparer.Ordinal))
                {
                    failedSymbols.Add(symbol);
                }

                // Invariant: the totals above accumulate across passes while statSlots keeps
                // exactly one merged entry per symbol. They cannot disagree, because a re-swept
                // symbol is only ever one that answered empty on the previous pass — AnsweredEmpty
                // is set exclusively on the all-zero path (written/unchanged/quarantined all 0) —
                // so replacing its slot never drops a count the totals still carry. A duplicated
                // open membership row for the same symbol would be the one way to break this
                // (GetCurrentConstituentSymbolsAsync applies no Distinct); none exists today.
                if (statSlots.TryGetValue(symbol, out var slot))
                {
                    symbolStats[slot] = sweep.Stat;
                }
                else
                {
                    statSlots[symbol] = symbolStats.Count;
                    symbolStats.Add(sweep.Stat);
                }

                if (sweep.AnsweredEmpty)
                {
                    answeredEmpty.Add(symbol);
                }
            }

            coverage = await _store
                .GetDayCoverageAsync(_options.UniverseIndex, request.Date, cancellationToken)
                .ConfigureAwait(false);

            if (coverage.IsComplete)
            {
                _logger.LogInformation(
                    "prices: coverage complete for {Date} after pass {Pass} — {Covered}/{Constituents} {Index} constituents stored",
                    request.Date,
                    pass,
                    coverage.Covered,
                    coverage.Constituents,
                    _options.UniverseIndex);
                break;
            }

            if (answeredEmpty.Count == 0)
            {
                // Nothing in the progressive-publication class is left to retry: the gap is a
                // quarantine or a source-down, both of which stay with this run's status and
                // the retry ladder.
                _logger.LogWarning(
                    "prices: coverage incomplete for {Date} after pass {Pass} — {Covered}/{Constituents} {Index} constituents stored; nothing answered empty, so no re-sweep applies",
                    request.Date,
                    pass,
                    coverage.Covered,
                    coverage.Constituents,
                    _options.UniverseIndex);
                break;
            }

            if (pass >= maxPasses)
            {
                _logger.LogWarning(
                    "prices: coverage incomplete for {Date} after {Passes} pass(es) — {Covered}/{Constituents} {Index} constituents stored; {Missing} symbol(s) still answered empty ({Symbols}); pass bound reached (Ingestion:Prices:CoverageMaxPasses)",
                    request.Date,
                    pass,
                    coverage.Covered,
                    coverage.Constituents,
                    _options.UniverseIndex,
                    answeredEmpty.Count,
                    string.Join(", ", answeredEmpty));
                break;
            }

            if (CoverageBudgetExhausted(sweepStartedAt))
            {
                _logger.LogWarning(
                    "prices: coverage incomplete for {Date} after pass {Pass} — {Covered}/{Constituents} {Index} constituents stored; {Missing} symbol(s) still answered empty ({Symbols}); wall-clock budget {Budget} spent (Ingestion:Prices:CoverageBudget)",
                    request.Date,
                    pass,
                    coverage.Covered,
                    coverage.Constituents,
                    _options.UniverseIndex,
                    answeredEmpty.Count,
                    string.Join(", ", answeredEmpty),
                    _options.CoverageBudget);
                break;
            }

            await SettleAsync(cancellationToken).ConfigureAwait(false);
            pending = answeredEmpty;
        }

        var uncovered = coverage.IsComplete
            ? (IReadOnlyList<string>)[]
            : await _store
                .GetMissingConstituentSymbolsAsync(_options.UniverseIndex, request.Date, cancellationToken)
                .ConfigureAwait(false);

        return await CompleteAsync(
            request,
            constituents.Count,
            written,
            unchanged,
            quarantined,
            failedSymbols,
            symbolStats,
            uncovered,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// One symbol's fetch/store/validate step, isolated exactly as before (a per-symbol failure
    /// never aborts the run). Returns the stat it contributes and whether the source answered
    /// honestly empty for the date — the only outcome a coverage re-sweep retries.
    /// </summary>
    private async Task<SymbolSweep> SweepSymbolAsync(
        string symbol,
        IngestionRequest request,
        CancellationToken cancellationToken)
    {
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
                return new SymbolSweep(new SymbolRunStat(symbol, 0, 0, 1, false), 1, AnsweredEmpty: false);
            }

            if (fetched.Payload.Items.Count == 0)
            {
                // The honest empty: a valid payload with no session row for this date. Named
                // and counted rather than recorded as "nothing happened" (01 v1.5 §10.9.2).
                _logger.LogInformation(
                    "prices: symbol {Symbol} answered empty for {Date} ({ReasonCode}) — the source has published no session row yet",
                    symbol,
                    request.Date,
                    NoRowForDate);
                return new SymbolSweep(
                    new SymbolRunStat(symbol, 0, 0, 0, false, NoRowForDate),
                    0,
                    AnsweredEmpty: true);
            }

            var outcome = await _store
                .UpsertAsync(request.Date, fetched.SourceRef, _clock.GetUtcNow(), fetched.Payload.Items, cancellationToken)
                .ConfigureAwait(false);

            var symbolQuarantined = await QuarantineOutcomeAsync(request, fetched.SourceRef, outcome, cancellationToken)
                .ConfigureAwait(false);

            // The per-symbol counts ride the run-ledger row (TC-MDF-072); they are no
            // longer emitted only as structured logs.
            return new SymbolSweep(
                new SymbolRunStat(symbol, outcome.Inserted, outcome.Unchanged, symbolQuarantined, false),
                symbolQuarantined,
                AnsweredEmpty: false);
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
            return new SymbolSweep(new SymbolRunStat(symbol, 0, 0, 1, false), 1, AnsweredEmpty: false);
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
            return new SymbolSweep(new SymbolRunStat(symbol, 0, 0, 0, true), 0, AnsweredEmpty: false);
        }
    }

    /// <summary>
    /// Whether the run has spent its completeness budget (<see cref="PricesSourceOptions.CoverageBudget"/>)
    /// — the second bound beside the pass count, so a slow source can never extend a run
    /// indefinitely. Read from the injected <see cref="TimeProvider"/> so the budget is
    /// deterministic under a fake clock. A non-positive budget budgets no re-sweep at all.
    /// </summary>
    private bool CoverageBudgetExhausted(DateTimeOffset startedAt)
    {
        if (_options.CoverageBudget <= TimeSpan.Zero)
        {
            return true;
        }

        return _clock.GetUtcNow() - startedAt >= _options.CoverageBudget;
    }

    /// <summary>
    /// The settle wait between coverage passes (<see cref="PricesSourceOptions.CoverageSettleDelay"/>):
    /// a real, configurable pause on the injected clock, skipped entirely when non-positive so
    /// L2 runs never wait on a timer.
    /// </summary>
    private async Task SettleAsync(CancellationToken cancellationToken)
    {
        if (_options.CoverageSettleDelay <= TimeSpan.Zero)
        {
            return;
        }

        _logger.LogInformation(
            "prices: settling {Delay} before re-sweeping the symbols the source has not published yet",
            _options.CoverageSettleDelay);
        await Task.Delay(_options.CoverageSettleDelay, _clock, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>One symbol's outcome for one pass of the coverage loop.</summary>
    private sealed record SymbolSweep(SymbolRunStat Stat, int Quarantined, bool AnsweredEmpty);

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
        var symbolStats = new List<SymbolRunStat>(constituents.Count);
        var coveredSymbols = new HashSet<string>(StringComparer.Ordinal);
        DateOnly? achievedFrom = null;
        var requestsMade = 0;

        for (var index = 0; index < constituents.Count; index++)
        {
            await PaceAsync(requestsMade, cancellationToken).ConfigureAwait(false);
            requestsMade++;
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
                    symbolStats.Add(new SymbolRunStat(symbol, 0, 0, 1, false));
                    continue;
                }

                var rows = fetched.Payload.Rows;
                if (rows.Count == 0)
                {
                    _logger.LogInformation(
                        "prices: symbol {Symbol} backfill from {From} returned no history",
                        symbol,
                        requestedFrom);
                    symbolStats.Add(new SymbolRunStat(symbol, 0, 0, 0, false));
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

                symbolStats.Add(new SymbolRunStat(symbol, outcome.Inserted, outcome.Unchanged, symbolQuarantined, false));
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
                symbolStats.Add(new SymbolRunStat(symbol, 0, 0, 1, false));
            }
            catch (SourceFetchException exception)
            {
                _logger.LogWarning(
                    exception,
                    "prices: symbol {Symbol} backfill fetch failed from {From} (SOURCE_FETCH_FAILED)",
                    symbol,
                    requestedFrom);
                failedSymbols.Add(symbol);
                symbolStats.Add(new SymbolRunStat(symbol, 0, 0, 0, true));
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

        // Backfill rides its own achieved-depth contract (FR-MDF-010): the coverage gate is the
        // incremental day's, never a historical range's, so no uncovered names are reported.
        return await CompleteAsync(
            request,
            constituents.Count,
            written,
            unchanged,
            quarantined,
            failedSymbols,
            symbolStats,
            uncoveredSymbols: [],
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The run's terminal outcome: aggregate the per-symbol counts, emit the run summary and
    /// raise a single aggregate alert naming the failed symbols (TC-MDF-073). Status semantics
    /// (01 v1.5 §10.9.2, amended by the completeness gate): every symbol failed →
    /// <see cref="IngestResult.Failed"/> (so the retry ladder applies); some failed, some facts
    /// quarantined, or any current constituent still <b>uncovered</b> after the bounded sweeps →
    /// <see cref="IngestResult.Partial"/>; only a day whose bound is fully stored is
    /// <see cref="IngestResult.Succeeded"/>. A symbol the source honestly answered empty for
    /// raises <b>no</b> alert (it is not a source failure) — but it can never be reported as
    /// success.
    /// </summary>
    private async Task<IngestResult> CompleteAsync(
        IngestionRequest request,
        int constituentCount,
        int written,
        int unchanged,
        int quarantined,
        List<string> failedSymbols,
        IReadOnlyList<SymbolRunStat> symbolStats,
        IReadOnlyList<string> uncoveredSymbols,
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
            : failedSymbols.Count > 0 || quarantined > 0 || uncoveredSymbols.Count > 0
                ? IngestResult.Partial
                : IngestResult.Succeeded;

        var noRowCount = symbolStats.Count(stat => stat.Reason == NoRowForDate);
        if (uncoveredSymbols.Count > 0)
        {
            // Named, never silent: the day is not complete and nothing about the run may imply
            // that it is (the 243-run shape: "written 18, unchanged 71, quarantined 0" over 11
            // symbols the source had not published yet, reported as success).
            _logger.LogWarning(
                "prices: {Date} is NOT fully covered — {Uncovered} of {Constituents} {Index} constituents have no stored price ({Symbols}) ({Status})",
                request.Date,
                uncoveredSymbols.Count,
                constituentCount,
                _options.UniverseIndex,
                string.Join(", ", uncoveredSymbols),
                status);
        }

        _logger.LogInformation(
            "prices: run for {Date} complete — constituents {Constituents}, written {Written}, unchanged {Unchanged}, quarantined {Quarantined}, failed {Failed}, noRow {NoRow}, uncovered {Uncovered} ({Status})",
            request.Date,
            constituentCount,
            written,
            unchanged,
            quarantined,
            failedSymbols.Count,
            noRowCount,
            uncoveredSymbols.Count,
            status);

        return new IngestResult(Code, written, unchanged, quarantined, status) { SymbolStats = symbolStats };
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
    /// <see cref="PricesSourceOptions.RequestDelayMs"/> between requests of the run, and never
    /// before its very first request. <paramref name="requestsMadeSoFar"/> is the run-global
    /// count, not the index within the current pass — a coverage re-sweep is a continuation of
    /// the same loop, so its first fetch is spaced from the previous pass's last fetch too. A
    /// non-positive delay disables the wait (deterministic L2 runs). The wait is a real
    /// wall-clock timer — the delay is a politeness measure, not a scheduling decision — so the
    /// TC-MDF-074 lower-bound timing assertion reflects the live posture.
    /// </summary>
    private async Task PaceAsync(int requestsMadeSoFar, CancellationToken cancellationToken)
    {
        if (requestsMadeSoFar == 0 || _options.RequestDelayMs <= 0)
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
