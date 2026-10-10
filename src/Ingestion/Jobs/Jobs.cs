using System.Text.Json;
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
}

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
            await RecordAsync(jobCode, startedAt, result.Status, result, request, context, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Ingestion job {JobCode} failed", jobCode);
            await RecordAsync(jobCode, startedAt, IngestResult.Failed, result: null, request, context, cancellationToken)
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
        CancellationToken cancellationToken)
    {
        var stats = JsonSerializer.Serialize(new
        {
            written = result?.Written ?? 0,
            unchanged = result?.Unchanged ?? 0,
            quarantined = result?.Quarantined ?? 0,
            skipped = result?.Skipped ?? 0,
            // Backfill mode is part of the run's identity (UC-MDF-002, TC-MDF-043).
            mode = request.IsBackfill ? "backfill" : "incremental",
            backfillFrom = request.BackfillFrom?.ToString("yyyy-MM-dd"),
            attempt = context?.Attempt,
            retries = context?.Retries,
            maxAttempts = context?.MaxAttempts,
        });

        _db.IngestRuns.Add(new IngestRun
        {
            JobCode = jobCode,
            StartedAt = startedAt,
            FinishedAt = _clock.GetUtcNow(),
            Status = status,
            StatsJson = stats,
        });

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
