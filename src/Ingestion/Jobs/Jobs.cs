using System.Text.Json;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.Extensions.Logging;

namespace Degerli.Ingestion.Jobs;

/// <summary>What to ingest: the target trading day (UC-MDF-001 step 1). Backfill
/// ranges extend this in TKT-mdf-007.</summary>
public sealed record IngestionRequest(DateOnly Date);

/// <summary>Outcome of one job run, recorded in the run ledger (<c>ingest_runs</c>).</summary>
public sealed record IngestResult(string JobCode, int Written, int Unchanged, int Quarantined, string Status)
{
    public const string Succeeded = "succeeded";
    public const string Partial = "partial";
    public const string Failed = "failed";
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
    Task<IngestResult> RunAsync(string jobCode, IngestionRequest request, CancellationToken cancellationToken = default);
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
    private readonly DegerliDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<IngestionJobRunner> _logger;

    public IngestionJobRunner(
        IEnumerable<IIngestJob> jobs,
        DegerliDbContext db,
        TimeProvider clock,
        ILogger<IngestionJobRunner> logger)
    {
        _jobs = jobs.ToDictionary(job => job.JobCode, StringComparer.Ordinal);
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IngestResult> RunAsync(
        string jobCode,
        IngestionRequest request,
        CancellationToken cancellationToken = default)
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
            await RecordAsync(jobCode, startedAt, result.Status, result, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Ingestion job {JobCode} failed", jobCode);
            await RecordAsync(jobCode, startedAt, IngestResult.Failed, result: null, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private async Task RecordAsync(
        string jobCode,
        DateTimeOffset startedAt,
        string status,
        IngestResult? result,
        CancellationToken cancellationToken)
    {
        var stats = result is null
            ? "{}"
            : JsonSerializer.Serialize(new
            {
                written = result.Written,
                unchanged = result.Unchanged,
                quarantined = result.Quarantined,
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
