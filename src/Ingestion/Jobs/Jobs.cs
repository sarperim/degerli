using System.Text.Json;
using Degerli.Ingestion.Sources;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.Extensions.Logging;

namespace Degerli.Ingestion.Jobs;

/// <summary>
/// What to ingest: the target trading day (UC-MDF-001 step 1). When
/// <see cref="BackfillFrom"/> is set the job runs in <b>backfill mode</b> (UC-MDF-002,
/// FR-MDF-010): it asks the adapter for the historical range starting at that date and
/// records the actual achieved depth. The admin trigger (TKT-mdf-010) carries the same
/// <c>{backfillFrom}</c> body at the job level.
/// </summary>
public sealed record IngestionRequest(DateOnly Date, DateOnly? BackfillFrom = null)
{
    /// <summary>True when this run is a historical backfill rather than the incremental EOD ingest.</summary>
    public bool IsBackfill => BackfillFrom is not null;
}

/// <summary>
/// Outcome of one job run, recorded in the run ledger (<c>ingest_runs</c>).
/// <see cref="Skipped"/> counts facts/entities deliberately excluded by a scope bound
/// rather than by a data-quality failure (e.g. out-of-scope fund types, BR-FDF-006) —
/// it rides the ledger's <c>stats_json</c> alongside the written/unchanged/quarantined
/// counts.
/// </summary>
public sealed record IngestResult(string JobCode, int Written, int Unchanged, int Quarantined, string Status, int Skipped = 0)
{
    public const string Succeeded = "succeeded";
    public const string Partial = "partial";
    public const string Failed = "failed";

    /// <summary>
    /// Per-symbol breakdown of the run (TC-MDF-072/073): what each fetched constituent of the
    /// configured index contributed. Empty for jobs with no per-symbol loop. It rides the
    /// ledger's <c>stats_json</c> alongside the aggregate counts, so the run-ledger row carries
    /// the per-symbol stats rather than surfacing them only as structured logs.
    /// </summary>
    public IReadOnlyList<SymbolRunStat> SymbolStats { get; init; } = [];

    /// <summary>
    /// Rows repaired in place because their inputs changed (the same-day snapshot repair,
    /// TKT-foundation-042). Distinct from <see cref="Written"/> on the ledger's
    /// <c>stats_json</c> so a repair is reported as a repair, not as a fresh write
    /// (TKT-foundation-044's fold-in).
    /// </summary>
    public int Rewritten { get; init; }

    /// <summary>
    /// Why the run failed — the reason code plus a bounded upstream detail
    /// (<see cref="IngestFailureReason"/>), recorded in the ledger's <c>error</c> column.
    /// A <see cref="Failed"/> result must carry one: a failed run an operator cannot
    /// diagnose from the ledger is a silent hole (FR-MDF-009, NFR-MDF-001).
    /// </summary>
    public string? FailureReason { get; init; }
}

/// <summary>
/// One constituent's contribution to a run (TC-MDF-072/073). <see cref="Failed"/> marks a
/// per-symbol transport failure — a source-down, not a parse/validation event: it quarantines
/// nothing but is named in the run's aggregate alert.
/// <para>
/// <see cref="Reason"/> is the explicit outcome class when the counts alone would be
/// indistinguishable from "nothing to do" — today the prices lane's
/// <c>NO_ROW_FOR_DATE</c> (the source answered with a valid payload carrying no session row
/// for the requested date, so the symbol was skipped for a stated reason rather than
/// silently). <c>null</c> when the counts speak for themselves; optional so every existing
/// job's stats shape is unchanged.
/// </para>
/// </summary>
public sealed record SymbolRunStat(
    string Symbol,
    int Written,
    int Unchanged,
    int Quarantined,
    bool Failed,
    string? Reason = null);

/// <summary>
/// Retry-ladder context for a run-ledger row (C3c, NFR-MDF-001): which attempt this is
/// and how many are allowed. The scheduler supplies it so the ledger records the
/// attempt count of a recovered run; direct/admin callers omit it.
/// </summary>
public sealed record IngestionRunContext(int Attempt, int MaxAttempts)
{
    /// <summary>Retry ordinal (0 for the first attempt).</summary>
    public int Retries => Attempt - 1;
}

/// <summary>
/// A failure that knows its own operator-visible reason code (FR-MDF-009). An exception type
/// (or a job) that can name its reason precisely implements this; the runner prefers the
/// named code over its command of general classifiers.
/// </summary>
public interface IReasonCodedFailure
{
    /// <summary>The stable reason code recorded at the head of the ledger's <c>error</c> value.</summary>
    string ReasonCode { get; }
}

/// <summary>
/// The run ledger's failure-reason vocabulary (FR-MDF-009, NFR-MDF-001): a failed run records
/// <c>"{CODE}: {bounded detail}"</c> in <c>ingest_runs.error</c>, so an operator never needs the
/// console to know why a run failed. The detail is whitespace-collapsed, single-lined and
/// bounded (<see cref="MaxLength"/>) — a runaway upstream message cannot bloat the ledger row.
/// Redaction stays where it already lives (each adapter redacts its own credentials before
/// throwing, e.g. <c>KapSourceOptions.Redact</c>); the runner never echoes a raw payload.
/// </summary>
public static class IngestFailureReason
{
    /// <summary>The source could not be reached or answered a non-success response.</summary>
    public const string SourceFetchFailed = "SOURCE_FETCH_FAILED";

    /// <summary>The source has no configured address/route — nothing was ever wired to call.</summary>
    public const string SourceNotConfigured = "SOURCE_NOT_CONFIGURED";

    /// <summary>The source answered a body that could not be parsed (UNPARSEABLE_PAYLOAD class).</summary>
    public const string UnparseablePayload = "UNPARSEABLE_PAYLOAD";

    /// <summary>A job-reported failure that named no more specific reason (defensive fallback).</summary>
    public const string Unspecified = "FAILED_UNSPECIFIED";

    /// <summary>Upper bound on the detail portion of a recorded reason.</summary>
    public const int MaxLength = 300;

    /// <summary>Composes <c>"CODE: detail"</c>, bounding and single-lining the detail.</summary>
    public static string Compose(string reasonCode, string? detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);

        var bounded = Bound(detail);
        return bounded.Length == 0 ? reasonCode : $"{reasonCode}: {bounded}";
    }

    /// <summary>Classifies a thrown failure and composes its ledger reason.</summary>
    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var code = exception switch
        {
            // A failure that names itself wins (e.g. the fund lane's SOURCE_NOT_CONFIGURED).
            IReasonCodedFailure coded => coded.ReasonCode,
            SourcePayloadException => UnparseablePayload,
            SourceFetchException => SourceFetchFailed,
            // A transport failure below HTTP (connection refused, DNS, TLS) is the same
            // source-down class the jobs already treat as a fetch failure.
            HttpRequestException => SourceFetchFailed,
            _ => Unspecified,
        };

        return Compose(code, exception.Message);
    }

    /// <summary>Collapses whitespace/newlines and truncates to <see cref="MaxLength"/>.</summary>
    public static string Bound(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return string.Empty;
        }

        var collapsed = string.Join(' ', detail.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= MaxLength ? collapsed : collapsed[..(MaxLength - 1)] + "…";
    }
}

/// <summary>
/// One ingestion job (the per-job seam of TKT-mdf-002). Jobs carry a stable
/// <see cref="JobCode"/> — the key the run ledger, admin triggers (TKT-mdf-010) and
/// the scheduler (TKT-mdf-005) use — and register themselves through
/// <c>AddMarketDataIngestion</c>'s job list.
/// </summary>
public interface IIngestJob
{
    string JobCode { get; }

    Task<IngestResult> RunAsync(IngestionRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Resolves and executes a registered job, recording the run ledger row.</summary>
public interface IIngestionJobRunner
{
    /// <summary>
    /// Registered job codes in registration order — the scheduler's default run set when
    /// no explicit chain order is configured (the TKT-mdf-002 registration convention).
    /// </summary>
    IReadOnlyCollection<string> JobCodes { get; }

    Task<IngestResult> RunAsync(string jobCode, IngestionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Runs a job and tags its ledger row with the retry-ladder attempt context.</summary>
    Task<IngestResult> RunAsync(
        string jobCode,
        IngestionRequest request,
        IngestionRunContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default runner: looks the job up by <see cref="IIngestJob.JobCode"/>, executes it and
/// appends an <c>ingest_runs</c> row (02 §3.1). A thrown source failure still records a
/// failed run before propagating, so the ledger never has a silent hole (FR-MDF-009).
/// Scheduling and the retry ladder remain C3c's responsibility (TKT-mdf-005); the runner
/// is the seam they call into.
/// </summary>
public sealed class IngestionJobRunner : IIngestionJobRunner
{
    // Web defaults camelCase the stats keys; the existing keys are already lower-first, so
    // this only governs the per-symbol breakdown's record properties.
    private static readonly JsonSerializerOptions StatsJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IReadOnlyDictionary<string, IIngestJob> _jobs;
    private readonly IReadOnlyList<string> _jobCodes;
    private readonly DegerliDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<IngestionJobRunner> _logger;

    public IngestionJobRunner(
        IEnumerable<IIngestJob> jobs,
        DegerliDbContext db,
        TimeProvider clock,
        ILogger<IngestionJobRunner> logger)
    {
        var registered = jobs.ToList();
        _jobs = registered.ToDictionary(job => job.JobCode, StringComparer.Ordinal);
        _jobCodes = registered.Select(job => job.JobCode).ToList();
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> JobCodes => _jobCodes;

    /// <inheritdoc />
    public Task<IngestResult> RunAsync(
        string jobCode,
        IngestionRequest request,
        CancellationToken cancellationToken = default) =>
        RunCoreAsync(jobCode, request, context: null, cancellationToken);

    /// <inheritdoc />
    public Task<IngestResult> RunAsync(
        string jobCode,
        IngestionRequest request,
        IngestionRunContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return RunCoreAsync(jobCode, request, context, cancellationToken);
    }

    private async Task<IngestResult> RunCoreAsync(
        string jobCode,
        IngestionRequest request,
        IngestionRunContext? context,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobCode);

        if (!_jobs.TryGetValue(jobCode, out var job))
        {
            throw new KeyNotFoundException($"No ingestion job registered with code '{jobCode}'.");
        }

        var startedAt = _clock.GetUtcNow();
        try
        {
            var result = await job.RunAsync(request, cancellationToken).ConfigureAwait(false);
            await RecordAsync(jobCode, startedAt, result.Status, result, request, context, thrownReason: null, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Ingestion job {JobCode} failed", jobCode);
            await RecordAsync(
                    jobCode,
                    startedAt,
                    IngestResult.Failed,
                    result: null,
                    request,
                    context,
                    IngestFailureReason.Describe(exception),
                    cancellationToken)
                .ConfigureAwait(false);
            throw;
        }
    }

    private async Task RecordAsync(
        string jobCode,
        DateTimeOffset startedAt,
        string status,
        IngestResult? result,
        IngestionRequest request,
        IngestionRunContext? context,
        string? thrownReason,
        CancellationToken cancellationToken)
    {
        var stats = JsonSerializer.Serialize(new
        {
            written = result?.Written ?? 0,
            unchanged = result?.Unchanged ?? 0,
            quarantined = result?.Quarantined ?? 0,
            skipped = result?.Skipped ?? 0,
            // Same-day repairs are counted distinctly (TKT-foundation-042/044): a repaired row
            // is never reported as a fresh write without saying so.
            rewritten = result?.Rewritten ?? 0,
            // Per-symbol breakdown (TC-MDF-072/073): the run-ledger row carries per-symbol stats.
            symbols = result?.SymbolStats ?? Array.Empty<SymbolRunStat>(),
            // Backfill mode is part of the run's identity (UC-MDF-002, TC-MDF-043).
            mode = request.IsBackfill ? "backfill" : "incremental",
            backfillFrom = request.BackfillFrom?.ToString("yyyy-MM-dd"),
            attempt = context?.Attempt,
            retries = context?.Retries,
            maxAttempts = context?.MaxAttempts,
        }, StatsJsonOptions);

        _db.IngestRuns.Add(new IngestRun
        {
            JobCode = jobCode,
            StartedAt = startedAt,
            FinishedAt = _clock.GetUtcNow(),
            Status = status,
            StatsJson = stats,
            Error = ResolveFailureReason(jobCode, status, result, thrownReason),
        });

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The ledger's <c>error</c> value for this row (FR-MDF-009): a thrown failure's classified
    /// reason, then a job-reported <see cref="IngestResult.FailureReason"/>, and — if neither
    /// names one — a defensive <see cref="IngestFailureReason.Unspecified"/> with a warning, so
    /// a failed row can never again be written with an empty reason.
    /// </summary>
    private string? ResolveFailureReason(string jobCode, string status, IngestResult? result, string? thrownReason)
    {
        if (!string.Equals(status, IngestResult.Failed, StringComparison.Ordinal))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(thrownReason))
        {
            return thrownReason;
        }

        if (!string.IsNullOrWhiteSpace(result?.FailureReason))
        {
            return result.FailureReason;
        }

        _logger.LogWarning(
            "Ingestion job {JobCode} reported a failed run without a reason; recording {ReasonCode}",
            jobCode,
            IngestFailureReason.Unspecified);
        return IngestFailureReason.Compose(
            IngestFailureReason.Unspecified,
            $"the {jobCode} job reported a failed run without a reason");
    }
}
