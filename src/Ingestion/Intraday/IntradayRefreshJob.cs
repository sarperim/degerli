using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Prices;
using Degerli.Ingestion.Quarantine;
using Degerli.Ingestion.Sources;
using Degerli.Ingestion.Universe;
using Degerli.Ingestion.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.Intraday;

/// <summary>
/// The <c>intraday-quotes</c> sweep (TKT-foundation-047, architecture §10.9.18;
/// FR-MDF-001's intraday mode): the scheduler's four Mon–Fri passes
/// (11:15/13:15/15:15/17:15 Europe/Istanbul, shipped as the <c>JobSchedules</c> cron) walk the
/// current constituents of the configured universe <b>strictly sequentially</b>, paced by
/// <see cref="IntradayQuoteSourceOptions.RequestDelayMs"/> — the same politeness posture as
/// the EOD lane (01 §10.9.2), ~400 paced calls a day. The only day gate is the cron's
/// <c>1-5</c>: <c>JobSchedules</c> entries register as per-job streams with
/// <c>TradingDaysOnly = false</c> (<c>IngestionScheduler.BuildStreams</c>), so a Turkish
/// weekday holiday still sweeps until TKT-foundation-048 wires the calendar in.
/// <para>
/// <b>Honest per-symbol accounting</b> (043's no-data doctrine): a symbol the source has no
/// same-day row for is counted in the run's <c>skipped</c> counter and named in the log —
/// never silently invisible. A failed symbol is isolated exactly like the EOD loop's
/// (TC-MDF-073): the sweep continues and one aggregate alert names the failures.
/// </para>
/// <para>
/// The sweep writes <b>only</b> <c>intraday_quotes</c> — an intraday reading is never a close,
/// and <c>daily_prices</c> stays the EOD lane's append-only fact table.
/// </para>
/// </summary>
public sealed class IntradayRefreshJob : IIngestJob
{
    /// <summary>Stable job code used by the ledger, scheduler and admin trigger.</summary>
    public const string Code = "intraday-quotes";

    /// <summary>Reason code for a per-symbol transport failure (a source-down, not a parse event).</summary>
    public const string SourceFetchFailed = "SOURCE_FETCH_FAILED";

    private readonly IntradayQuoteSourceAdapter _adapter;
    private readonly IntradayQuoteStore _store;
    private readonly UniverseSyncStore _universe;
    private readonly IQuarantineService _quarantine;
    private readonly IIngestionAlerter _alerter;
    private readonly IntradayQuoteSourceOptions _options;
    private readonly PricesSourceOptions _pricesOptions;
    private readonly TimeProvider _clock;
    private readonly ILogger<IntradayRefreshJob> _logger;

    public IntradayRefreshJob(
        IntradayQuoteSourceAdapter adapter,
        IntradayQuoteStore store,
        UniverseSyncStore universe,
        IQuarantineService quarantine,
        IIngestionAlerter alerter,
        IOptions<IntradayQuoteSourceOptions> options,
        IOptions<PricesSourceOptions> pricesOptions,
        TimeProvider clock,
        ILogger<IntradayRefreshJob> logger)
    {
        _adapter = adapter;
        _store = store;
        _universe = universe;
        _quarantine = quarantine;
        _alerter = alerter;
        _options = options.Value;
        _pricesOptions = pricesOptions.Value;
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
        var constituents = await ResolveConstituentsAsync(cancellationToken).ConfigureAwait(false);
        if (constituents.Count == 0)
        {
            // An empty bound has nothing to refresh; it is not a data failure of this lane
            // (the EOD stream owns the cold start / launch seed). Recorded, never silent.
            _logger.LogWarning(
                "intraday: index {IndexCode} has no current constituents; nothing to sweep for {Date}",
                ResolveUniverseIndex(),
                request.Date);
            return new IngestResult(Code, 0, 0, 0, IngestResult.Succeeded);
        }

        var written = 0;
        var unchanged = 0;
        var rewritten = 0;
        var noData = 0;
        var quarantined = 0;
        var failedSymbols = new List<string>();
        var symbolStats = new List<SymbolRunStat>(constituents.Count);

        for (var index = 0; index < constituents.Count; index++)
        {
            await PaceAsync(index, cancellationToken).ConfigureAwait(false);
            var symbol = constituents[index];

            try
            {
                var fetched = await _adapter
                    .FetchSymbolAsync(symbol, request.Date, cancellationToken)
                    .ConfigureAwait(false);

                if (fetched.Payload.Items.Count == 0)
                {
                    // The probe-gated source served no same-day row: counted, never invisible
                    // (the 043 no-data doctrine; architecture §10.9.18).
                    _logger.LogInformation(
                        "intraday: symbol {Symbol} has no same-day row for {Date} (NO_DATA)",
                        symbol,
                        request.Date);
                    noData++;
                    symbolStats.Add(new SymbolRunStat(symbol, 0, 0, 0, false));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(fetched.SourceRef))
                {
                    // Provenance is required on every fact (FR-MDF-008): the payload is
                    // quarantined and counted, exactly as the EOD lane does (PricesJob's
                    // MISSING_PROVENANCE branch) — not silently folded into "no data".
                    _logger.LogWarning(
                        "intraday: symbol {Symbol} payload for {Date} carries no provenance (MISSING_PROVENANCE)",
                        symbol,
                        request.Date);
                    await _quarantine.QuarantineAsync(
                            new QuarantineEntry(Code, null, fetched.RawJson, QuarantineReason.MissingProvenance, _clock.GetUtcNow()),
                            cancellationToken)
                        .ConfigureAwait(false);
                    quarantined++;
                    symbolStats.Add(new SymbolRunStat(symbol, 0, 0, 1, false));
                    continue;
                }

                var outcome = await _store
                    .UpsertAsync(request.Date, fetched.SourceRef, _clock.GetUtcNow(), fetched.Payload.Items, cancellationToken)
                    .ConfigureAwait(false);

                foreach (var reject in outcome.Rejected)
                {
                    _logger.LogWarning(
                        "intraday: rejected reading for {Symbol} on {Date} ({ReasonCode})",
                        reject.Fact.Symbol,
                        request.Date,
                        reject.ReasonCode);
                    await QuarantineAsync(reject, fetched.SourceRef, cancellationToken).ConfigureAwait(false);
                    quarantined++;
                }

                written += outcome.Inserted;
                unchanged += outcome.Unchanged;
                rewritten += outcome.Rewritten;

                symbolStats.Add(new SymbolRunStat(symbol, outcome.Inserted, outcome.Unchanged, outcome.Rejected.Count, false));
            }
            catch (SourcePayloadException exception)
            {
                // A per-symbol parse failure is isolated: it quarantines and the loop continues.
                _logger.LogWarning(
                    "intraday: symbol {Symbol} payload for {Date} unparseable (UNPARSEABLE_PAYLOAD)",
                    symbol,
                    request.Date);
                await _quarantine.QuarantineAsync(
                        new QuarantineEntry(Code, null, exception.RawJson, QuarantineReason.UnparseablePayload, _clock.GetUtcNow()),
                        cancellationToken)
                    .ConfigureAwait(false);
                quarantined++;
                symbolStats.Add(new SymbolRunStat(symbol, 0, 0, 1, false));
            }
            catch (SourceFetchException exception)
            {
                // A transport failure is a source-down, not a parse event: no quarantine.
                _logger.LogWarning(
                    exception,
                    "intraday: symbol {Symbol} fetch failed for {Date} (SOURCE_FETCH_FAILED)",
                    symbol,
                    request.Date);
                failedSymbols.Add(symbol);
                symbolStats.Add(new SymbolRunStat(symbol, 0, 0, 0, true));
            }
        }

        if (failedSymbols.Count > 0)
        {
            await _alerter.RaiseAsync(
                new IngestionAlert(
                    JobCode: Code,
                    ReasonCode: SourceFetchFailed,
                    Subject: $"Degerli alert: {Code} source fetch failed for {string.Join(", ", failedSymbols)}",
                    BodyTr: $"Gün içi fiyat işi {request.Date:yyyy-MM-dd} için şu sembollerin kaynağından veri çekemedi: {string.Join(", ", failedSymbols)}. Diğer semboller işlendi; kaynak hatası karantinaya alınmaz.",
                    BodyEn: $"The intraday quotes job could not fetch data for {string.Join(", ", failedSymbols)} on {request.Date:yyyy-MM-dd}. The remaining symbols were processed; a source failure is not quarantined (it is a source-down, not a parse event).",
                    SourceRef: null),
                cancellationToken).ConfigureAwait(false);
        }

        var status = failedSymbols.Count == constituents.Count
            ? IngestResult.Failed
            : failedSymbols.Count > 0 || quarantined > 0
                ? IngestResult.Partial
                : IngestResult.Succeeded;

        _logger.LogInformation(
            "intraday: sweep for {Date} complete — constituents {Constituents}, written {Written}, unchanged {Unchanged}, rewritten {Rewritten}, no-data {NoData}, quarantined {Quarantined}, failed {Failed} ({Status})",
            request.Date,
            constituents.Count,
            written,
            unchanged,
            rewritten,
            noData,
            quarantined,
            failedSymbols.Count,
            status);

        return new IngestResult(Code, written, unchanged, quarantined, status, Skipped: noData)
        {
            SymbolStats = symbolStats,
        };
    }

    /// <summary>
    /// The sweep bound: the current constituents of the configured index
    /// (<c>Ingestion:Intraday:UniverseIndex</c>, empty → the prices lane's bound) — the
    /// intraday lane refreshes exactly the universe the EOD lane stores.
    /// </summary>
    private Task<IReadOnlyList<string>> ResolveConstituentsAsync(CancellationToken cancellationToken) =>
        _universe.GetCurrentConstituentSymbolsAsync(ResolveUniverseIndex(), cancellationToken);

    private string ResolveUniverseIndex() =>
        string.IsNullOrWhiteSpace(_options.UniverseIndex)
            ? _pricesOptions.UniverseIndex
            : _options.UniverseIndex;

    /// <summary>
    /// Paces the strictly sequential sweep (01 §10.9.2): waits
    /// <see cref="IntradayQuoteSourceOptions.RequestDelayMs"/> between requests, never before
    /// the first. A non-positive delay disables the wait (deterministic L2 runs).
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
        RejectedIntradayQuote reject,
        string sourceRef,
        CancellationToken cancellationToken) =>
        _quarantine.QuarantineAsync(
            new QuarantineEntry(
                Code,
                sourceRef,
                System.Text.Json.JsonSerializer.Serialize(reject.Fact),
                reject.ReasonCode,
                _clock.GetUtcNow()),
            cancellationToken);
}
