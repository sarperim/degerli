using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Quarantine;
using Degerli.Ingestion.Sources;
using Microsoft.Extensions.Logging;

namespace Degerli.Ingestion.Funds;

/// <summary>
/// Shared run for the three fund jobs (FR-FDF-001..003). Every fund job fetches the TEFAS
/// universe payload, enforces the fund-universe bound (BR-FDF-006: out-of-scope types are
/// skipped-and-counted, never quarantined — I-FDF-1), ingests the job's own fact type
/// append-only/idempotently, and quarantines conflicting values under the shared Q1
/// policy. The fact type is the only per-job difference.
/// </summary>
public abstract class FundJobBase : IIngestJob
{
    private readonly ISourceAdapter<TefasUniversePayload> _adapter;
    private readonly IIngestionAlerter _alerter;
    private readonly ILogger<FundJobBase> _logger;

    protected FundJobBase(
        ISourceAdapter<TefasUniversePayload> adapter,
        TefasFundStore store,
        IQuarantineWriter quarantine,
        IIngestionAlerter alerter,
        TimeProvider clock,
        ILogger<FundJobBase> logger)
    {
        _adapter = adapter;
        Store = store;
        Quarantine = quarantine;
        _alerter = alerter;
        Clock = clock;
        _logger = logger;
    }

    /// <summary>Stable job code used by the ledger, scheduler and admin trigger.</summary>
    public abstract string JobCode { get; }

    protected TefasFundStore Store { get; }

    protected IQuarantineWriter Quarantine { get; }

    protected TimeProvider Clock { get; }

    /// <inheritdoc />
    public async Task<IngestResult> RunAsync(
        IngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        SourcePayload<TefasUniversePayload> fetched;
        try
        {
            fetched = await _adapter.FetchAsync(request.Date, cancellationToken).ConfigureAwait(false);
        }
        catch (SourcePayloadException exception)
        {
            await QuarantineAsync(JobCode, null, exception.RawJson, "UNPARSEABLE_PAYLOAD", cancellationToken)
                .ConfigureAwait(false);
            return new IngestResult(JobCode, 0, 0, 1, IngestResult.Failed);
        }

        if (string.IsNullOrWhiteSpace(fetched.SourceRef))
        {
            // Provenance is required on every fact (FR-MDF-008): refuse the payload whole.
            _logger.LogWarning(
                "{Job}: TEFAS payload for {Date} carries no provenance (MISSING_PROVENANCE)",
                JobCode,
                request.Date);
            await QuarantineAsync(JobCode, null, fetched.RawJson, "MISSING_PROVENANCE", cancellationToken)
                .ConfigureAwait(false);
            return new IngestResult(JobCode, 0, 0, 1, IngestResult.Failed);
        }

        var recordedAt = Clock.GetUtcNow();
        var outcome = await Store
            .SyncUniverseAsync(fetched.Payload.Funds ?? [], recordedAt, cancellationToken)
            .ConfigureAwait(false);

        var inserted = 0;
        var unchanged = 0;
        var conflicts = new List<FundConflict>();

        foreach (var fund in outcome.InScope)
        {
            var sourceRef = fund.SourceRef ?? fetched.SourceRef!;
            var result = await ProcessFundAsync(fund, request.Date, sourceRef, recordedAt, cancellationToken)
                .ConfigureAwait(false);
            inserted += result.Inserted;
            unchanged += result.Unchanged;
            conflicts.AddRange(result.Conflicts);
        }

        foreach (var conflict in conflicts)
        {
            _logger.LogWarning(
                "{Job}: conflicting value for fund {FundId}; stored fact kept (CONFLICTING_VALUE)",
                JobCode,
                conflict.FundId);
            await QuarantineAsync(JobCode, fetched.SourceRef, conflict.PayloadJson, "CONFLICTING_VALUE", cancellationToken)
                .ConfigureAwait(false);
            await RaiseAlertAsync(conflict, fetched.SourceRef, cancellationToken).ConfigureAwait(false);
        }

        var status = conflicts.Count > 0 ? IngestResult.Partial : IngestResult.Succeeded;
        return new IngestResult(JobCode, inserted, unchanged, conflicts.Count, status, outcome.Skipped);
    }

    /// <summary>Ingests the job's own fact type for one in-scope fund.</summary>
    protected abstract Task<FundFactWriteResult> ProcessFundAsync(
        TefasFund fund,
        DateOnly requestDate,
        string sourceRef,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken);

    private Task QuarantineAsync(
        string jobCode,
        string? sourceRef,
        string? payloadJson,
        string reasonCode,
        CancellationToken cancellationToken) =>
        Quarantine.WriteAsync(
            new QuarantineEntry(jobCode, sourceRef, payloadJson, reasonCode, Clock.GetUtcNow()),
            cancellationToken);

    private Task RaiseAlertAsync(FundConflict conflict, string sourceRef, CancellationToken cancellationToken) =>
        _alerter.RaiseAsync(
            new IngestionAlert(
                JobCode,
                "CONFLICTING_VALUE",
                Subject: $"Degerli alert: {JobCode} CONFLICTING_VALUE for fund {conflict.FundId}",
                BodyTr: $"{JobCode} işi {conflict.FundId} fonu için çelişen bir değer buldu; saklanan değer korundu.",
                BodyEn: $"The {JobCode} job found a conflicting value for fund {conflict.FundId}; the stored value was kept.",
                SourceRef: sourceRef),
            cancellationToken);
}

/// <summary>The <c>fund-nav</c> job (FR-FDF-001): dated, idempotent NAV history.</summary>
public sealed class FundNavJob : FundJobBase
{
    public const string Code = "fund-nav";

    public FundNavJob(
        ISourceAdapter<TefasUniversePayload> adapter,
        TefasFundStore store,
        IQuarantineWriter quarantine,
        IIngestionAlerter alerter,
        TimeProvider clock,
        ILogger<FundNavJob> logger)
        : base(adapter, store, quarantine, alerter, clock, logger)
    {
    }

    /// <inheritdoc />
    public override string JobCode => Code;

    /// <inheritdoc />
    protected override Task<FundFactWriteResult> ProcessFundAsync(
        TefasFund fund,
        DateOnly requestDate,
        string sourceRef,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken) =>
        Store.UpsertNavsAsync(fund.Fund!.Code, sourceRef, recordedAt, fund.Navs ?? [], cancellationToken);
}

/// <summary>The <c>fund-performance</c> job (FR-FDF-002): performance as published.</summary>
public sealed class FundPerformanceJob : FundJobBase
{
    public const string Code = "fund-performance";

    public FundPerformanceJob(
        ISourceAdapter<TefasUniversePayload> adapter,
        TefasFundStore store,
        IQuarantineWriter quarantine,
        IIngestionAlerter alerter,
        TimeProvider clock,
        ILogger<FundPerformanceJob> logger)
        : base(adapter, store, quarantine, alerter, clock, logger)
    {
    }

    /// <inheritdoc />
    public override string JobCode => Code;

    /// <inheritdoc />
    protected override Task<FundFactWriteResult> ProcessFundAsync(
        TefasFund fund,
        DateOnly requestDate,
        string sourceRef,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken) =>
        Store.UpsertPerformancesAsync(fund.Fund!.Code, sourceRef, recordedAt, fund.Performance ?? [], cancellationToken);
}

/// <summary>
/// The <c>fund-holdings</c> job (FR-FDF-003): holdings snapshots as of the run date, with
/// symbol matching to MDF instruments (the future look-through link).
/// </summary>
public sealed class FundHoldingsJob : FundJobBase
{
    public const string Code = "fund-holdings";

    public FundHoldingsJob(
        ISourceAdapter<TefasUniversePayload> adapter,
        TefasFundStore store,
        IQuarantineWriter quarantine,
        IIngestionAlerter alerter,
        TimeProvider clock,
        ILogger<FundHoldingsJob> logger)
        : base(adapter, store, quarantine, alerter, clock, logger)
    {
    }

    /// <inheritdoc />
    public override string JobCode => Code;

    /// <inheritdoc />
    protected override Task<FundFactWriteResult> ProcessFundAsync(
        TefasFund fund,
        DateOnly requestDate,
        string sourceRef,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken) =>
        Store.UpsertHoldingsAsync(
            fund.Fund!.Code,
            requestDate,
            sourceRef,
            recordedAt,
            fund.Holdings ?? [],
            cancellationToken);
}
