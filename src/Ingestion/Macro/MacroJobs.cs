using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Quarantine;
using Degerli.Ingestion.Sources;
using Degerli.Ingestion.Validation;
using Microsoft.Extensions.Logging;

namespace Degerli.Ingestion.Macro;

/// <summary>
/// Shared run for the macro jobs (FR-MOV-014..016, UC-MOV-002). Every macro job fetches the
/// series payload, partitions it to the series it owns by cadence, and appends the values
/// idempotently. A reachable-but-unusable source (unparseable body, missing provenance)
/// quarantines the payload (INVALID); an unreachable source is UNREACHABLE; an expected
/// series absent from a good payload is LATE. Every failure raises a per-series builder
/// alert (mail + Serilog) naming the series, the failure class and the run — the last-known
/// value is never touched, so it keeps being served (FR-MOV-016).
/// </summary>
public abstract class MacroJobBase : IIngestJob
{
    private readonly ISourceAdapter<MacroPayload> _adapter;
    private readonly MacroStore _store;
    private readonly IQuarantineService _quarantine;
    private readonly IIngestionAlerter _alerter;
    private readonly TimeProvider _clock;
    private readonly ILogger _logger;

    protected MacroJobBase(
        ISourceAdapter<MacroPayload> adapter,
        MacroStore store,
        IQuarantineService quarantine,
        IIngestionAlerter alerter,
        TimeProvider clock,
        ILogger logger)
    {
        _adapter = adapter;
        _store = store;
        _quarantine = quarantine;
        _alerter = alerter;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public abstract string JobCode { get; }

    /// <summary>Whether this job owns the given series (by its registered cadence).</summary>
    protected abstract bool Owns(MacroSeriesDefinition series);

    /// <inheritdoc />
    public async Task<IngestResult> RunAsync(
        IngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        var owned = (await _store.GetSeriesAsync(cancellationToken).ConfigureAwait(false))
            .Where(Owns)
            .ToList();

        SourcePayload<MacroPayload> fetched;
        try
        {
            fetched = await _adapter.FetchAsync(request.Date, cancellationToken).ConfigureAwait(false);
        }
        catch (SourceFetchException)
        {
            await AlertAllAsync(owned, MacroFailureReason.Unreachable, request, cancellationToken).ConfigureAwait(false);
            return new IngestResult(JobCode, 0, 0, 0, IngestResult.Failed);
        }
        catch (SourcePayloadException exception)
        {
            await QuarantineAsync(exception.RawJson, "UNPARSEABLE_PAYLOAD", cancellationToken).ConfigureAwait(false);
            await AlertAllAsync(owned, MacroFailureReason.Invalid, request, cancellationToken).ConfigureAwait(false);
            return new IngestResult(JobCode, 0, 0, 1, IngestResult.Failed);
        }

        if (string.IsNullOrWhiteSpace(fetched.SourceRef))
        {
            await QuarantineAsync(fetched.RawJson, "MISSING_PROVENANCE", cancellationToken).ConfigureAwait(false);
            await AlertAllAsync(owned, MacroFailureReason.Invalid, request, cancellationToken).ConfigureAwait(false);
            return new IngestResult(JobCode, 0, 0, 1, IngestResult.Failed);
        }

        var items = fetched.Payload.Series ?? [];
        var recordedAt = _clock.GetUtcNow();
        var inserted = 0;
        var unchanged = 0;
        var anyLate = false;

        foreach (var series in owned)
        {
            var rows = items
                .Where(i => string.Equals(i.Code, series.Code, StringComparison.Ordinal))
                .ToList();

            if (rows.Count == 0)
            {
                anyLate = true;
                await AlertAsync(series.Code, MacroFailureReason.Late, request, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var facts = rows
                .Select(i => new MacroFact(i.Code, i.ValueDate, i.Value, i.RecordedAt ?? recordedAt))
                .ToList();
            var result = await _store.AppendAsync(fetched.SourceRef!, facts, cancellationToken).ConfigureAwait(false);
            inserted += result.Inserted;
            unchanged += result.Unchanged;
        }

        var status = anyLate ? IngestResult.Partial : IngestResult.Succeeded;
        return new IngestResult(JobCode, inserted, unchanged, 0, status);
    }

    private async Task AlertAllAsync(
        IReadOnlyList<MacroSeriesDefinition> owned,
        string reason,
        IngestionRequest request,
        CancellationToken cancellationToken)
    {
        foreach (var series in owned)
        {
            await AlertAsync(series.Code, reason, request, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task AlertAsync(
        string seriesCode,
        string reason,
        IngestionRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "{JobCode}: macro {Reason} for series {Series} on run {Run}",
            JobCode,
            reason,
            seriesCode,
            request.Date);

        await _alerter.RaiseAsync(
            new IngestionAlert(
                JobCode,
                reason,
                Subject: $"Degerli alert: {JobCode} {reason} for {seriesCode} ({request.Date:yyyy-MM-dd})",
                BodyTr: $"{seriesCode} makro serisi {request.Date:yyyy-MM-dd} çalışmasında \"{reason}\" hatası verdi; son bilinen değer sunulmaya devam ediyor.",
                BodyEn: $"The {seriesCode} macro series reported \"{reason}\" on the {request.Date:yyyy-MM-dd} run; the last-known value continues to be served.",
                SourceRef: null),
            cancellationToken).ConfigureAwait(false);
    }

    private Task QuarantineAsync(string rawJson, string reasonCode, CancellationToken cancellationToken) =>
        _quarantine.QuarantineAsync(
            new QuarantineEntry(JobCode, null, rawJson, reasonCode, _clock.GetUtcNow()),
            cancellationToken);
}

/// <summary>
/// The <c>macro-daily</c> job (FR-MOV-014): FX and gold, ingested daily so the previous
/// trading day's values are available by 09:00 the next day (03 §9 cadence).
/// </summary>
public sealed class MacroDailyJob : MacroJobBase
{
    public const string Code = "macro-daily";

    public MacroDailyJob(
        ISourceAdapter<MacroPayload> adapter,
        MacroStore store,
        IQuarantineService quarantine,
        IIngestionAlerter alerter,
        TimeProvider clock,
        ILogger<MacroDailyJob> logger)
        : base(adapter, store, quarantine, alerter, clock, logger)
    {
    }

    /// <inheritdoc />
    public override string JobCode => Code;

    /// <inheritdoc />
    protected override bool Owns(MacroSeriesDefinition series) =>
        string.Equals(series.Cadence, "daily", StringComparison.Ordinal);
}

/// <summary>
/// The <c>macro-cpi</c> job (FR-MOV-014): the monthly inflation pair (TÜİK + independent
/// measure) and the per-release CBRT policy rate, ingested within 24h of each release.
/// </summary>
public sealed class MacroCpiJob : MacroJobBase
{
    public const string Code = "macro-cpi";

    public MacroCpiJob(
        ISourceAdapter<MacroPayload> adapter,
        MacroStore store,
        IQuarantineService quarantine,
        IIngestionAlerter alerter,
        TimeProvider clock,
        ILogger<MacroCpiJob> logger)
        : base(adapter, store, quarantine, alerter, clock, logger)
    {
    }

    /// <inheritdoc />
    public override string JobCode => Code;

    /// <inheritdoc />
    protected override bool Owns(MacroSeriesDefinition series) =>
        !string.Equals(series.Cadence, "daily", StringComparison.Ordinal);
}
