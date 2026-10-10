using Cronos;
using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.Scheduling;

/// <summary>
/// C3c scheduler engine (FR-MDF-009, NFR-MDF-001): decides when a run is due (Cronos cron in
/// a configured time zone, the shared EOD trigger filtered by the trading calendar and any
/// independent per-job triggers), executes it through the TKT-mdf-002 job runner, and
/// retries a failed attempt on the 5/15/60 ladder. It holds only in-memory scheduling state;
/// the durable record is the <c>ingest_runs</c> ledger the runner writes per attempt.
/// </summary>
/// <remarks>
/// The engine is transport-agnostic and clock-driven: <see cref="NextWake"/> and
/// <see cref="TickAsync"/> are pure scheduling decisions over a supplied instant, which
/// is what makes scheduled/retry behaviour deterministic under a fake clock. The
/// <see cref="IngestionSchedulerHostedService"/> is the thin production loop over them.
/// The shared EOD trigger runs <see cref="IngestionSchedulerOptions.JobCodes"/> (or every
/// registered job when empty) on trading days; each <see cref="JobScheduleOptions"/> entry
/// additionally runs its own job on its own cadence — the macro jobs (03 §9). Running the
/// concrete EOD chain order is not this ticket's concern — TKT-int-001 pins it.
/// </remarks>
public sealed class IngestionScheduler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IIngestionCalendar _calendar;
    private readonly IIngestionAlerter _alerter;
    private readonly IngestionSchedulerOptions _options;
    private readonly ILogger<IngestionScheduler> _logger;
    private readonly TimeZoneInfo _timeZone;
    private readonly IReadOnlyList<ScheduleStream> _streams;
    private readonly object _gate = new();
    private IReadOnlyList<string>? _resolvedJobCodes;

    public IngestionScheduler(
        IServiceScopeFactory scopeFactory,
        IIngestionCalendar calendar,
        IIngestionAlerter alerter,
        IOptions<IngestionSchedulerOptions> options,
        ILogger<IngestionScheduler> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(alerter);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _calendar = calendar;
        _alerter = alerter;
        _options = options.Value;
        _logger = logger;
        _timeZone = ResolveTimeZone(_options.TimeZone);
        _streams = BuildStreams(_options);
    }

    /// <summary>Whether the scheduler is enabled in configuration.</summary>
    public bool Enabled => _options.Enabled;

    /// <summary>The next instant the scheduler needs to be woken at, or null when idle.</summary>
    public DateTimeOffset? NextWake(DateTimeOffset now)
    {
        lock (_gate)
        {
            DateTimeOffset? next = null;
            foreach (var stream in _streams)
            {
                if (stream.Active is { } active)
                {
                    next = Earliest(next, active.NextAttemptAt);
                    continue;
                }

                EnsureScheduled(stream, now);
                next = Earliest(next, stream.NextScheduled);
            }

            return next;
        }
    }

    /// <summary>Processes everything due at <paramref name="now"/>: a scheduled trigger
    /// (if the local date is a trading day for the EOD stream) and/or a due retry.</summary>
    public async Task TickAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var due = new List<(ScheduleStream Stream, ActiveRun Run)>();
        lock (_gate)
        {
            if (!_options.Enabled)
            {
                return;
            }

            foreach (var stream in _streams)
            {
                if (stream.Active is null)
                {
                    EnsureScheduled(stream, now);

                    // Consume every cron occurrence up to `now`. A non-trading occurrence
                    // on the EOD stream is skipped silently (no ledger row); a per-job
                    // stream is not trading-calendar gated. The first eligible occurrence
                    // starts a run.
                    while (stream.NextScheduled is { } occurrence && occurrence <= now)
                    {
                        stream.NextScheduled = stream.Cron.GetNextOccurrence(occurrence, _timeZone, inclusive: false);
                        var runDate = TradingDateOf(occurrence);
                        if (!stream.TradingDaysOnly || _calendar.IsTradingDay(runDate))
                        {
                            stream.Active = new ActiveRun(runDate, nextAttemptAt: now);
                            break;
                        }
                    }
                }

                if (stream.Active is { } active && now >= active.NextAttemptAt)
                {
                    due.Add((stream, active));
                }
            }
        }

        foreach (var (stream, run) in due)
        {
            await RunAttemptAsync(stream, run, now, cancellationToken).ConfigureAwait(false);
        }
    }

    private static IReadOnlyList<ScheduleStream> BuildStreams(IngestionSchedulerOptions options)
    {
        var eod = new ScheduleStream
        {
            Cron = CronExpression.Parse(options.Cron, CronFormat.IncludeSeconds),
            TradingDaysOnly = true,
            JobCodes = options.JobCodes is { Count: > 0 } ? options.JobCodes.ToList() : null,
        };

        var streams = new List<ScheduleStream> { eod };
        foreach (var schedule in options.JobSchedules)
        {
            if (string.IsNullOrWhiteSpace(schedule.JobCode) || string.IsNullOrWhiteSpace(schedule.Cron))
            {
                throw new InvalidOperationException(
                    "Each Ingestion:Scheduler:JobSchedules entry requires a JobCode and a Cron.");
            }

            streams.Add(new ScheduleStream
            {
                Cron = CronExpression.Parse(schedule.Cron, CronFormat.IncludeSeconds),
                TradingDaysOnly = false,
                JobCodes = new[] { schedule.JobCode },
            });
        }

        return streams;
    }

    private void EnsureScheduled(ScheduleStream stream, DateTimeOffset now) =>
        stream.NextScheduled ??= stream.Cron.GetNextOccurrence(now, _timeZone, inclusive: true);

    private static DateTimeOffset? Earliest(DateTimeOffset? current, DateTimeOffset? candidate)
    {
        if (candidate is null)
        {
            return current;
        }

        return current is null || candidate < current ? candidate : current;
    }

    private DateOnly TradingDateOf(DateTimeOffset occurrence)
    {
        var local = TimeZoneInfo.ConvertTime(occurrence, _timeZone);
        return DateOnly.FromDateTime(local.DateTime);
    }

    private async Task RunAttemptAsync(
        ScheduleStream stream,
        ActiveRun run,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var attempt = run.Attempts + 1;
        run.Attempts = attempt;

        IReadOnlyList<string> jobCodes = [];
        Exception? failure = null;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<IIngestionJobRunner>();
            jobCodes = ResolveJobCodes(stream, runner);
            var request = new IngestionRequest(run.TradingDate);
            var context = new IngestionRunContext(attempt, _options.MaxAttempts);

            foreach (var jobCode in jobCodes)
            {
                var result = await runner.RunAsync(jobCode, request, context, cancellationToken).ConfigureAwait(false);
                if (result.Status == IngestResult.Failed)
                {
                    failure = new InvalidOperationException($"Job '{jobCode}' reported a failed run.");
                    break;
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            failure = exception;
        }

        if (failure is null)
        {
            _logger.LogInformation(
                "Scheduled run for {Date} succeeded on attempt {Attempt}.",
                run.TradingDate,
                attempt);
            lock (_gate)
            {
                stream.Active = null;
            }

            return;
        }

        _logger.LogWarning(
            failure,
            "Scheduled run for {Date} failed on attempt {Attempt}.",
            run.TradingDate,
            attempt);

        if (attempt >= _options.MaxAttempts)
        {
            lock (_gate)
            {
                stream.Active = null;
            }

            await RaiseExhaustedAsync(run, jobCodes, attempt, cancellationToken).ConfigureAwait(false);
            return;
        }

        var nextAttemptAt = now + _options.RetryDelays[attempt - 1];
        lock (_gate)
        {
            run.NextAttemptAt = nextAttemptAt;
        }

        _logger.LogInformation(
            "Retry {Retry} for {Date} scheduled at {At}.",
            attempt,
            run.TradingDate,
            nextAttemptAt);
    }

    private IReadOnlyList<string> ResolveJobCodes(ScheduleStream stream, IIngestionJobRunner runner)
    {
        if (stream.JobCodes is { Count: > 0 })
        {
            return stream.JobCodes;
        }

        return _resolvedJobCodes ??= runner.JobCodes.ToList();
    }

    private Task RaiseExhaustedAsync(
        ActiveRun run,
        IReadOnlyList<string> jobCodes,
        int attempts,
        CancellationToken cancellationToken)
    {
        var target = jobCodes.Count > 0 ? string.Join(", ", jobCodes) : "registered jobs";

        return _alerter.RaiseAsync(
            new IngestionAlert(
                JobCode: target,
                ReasonCode: "RETRIES_EXHAUSTED",
                Subject: $"Degerli alert: scheduled run for {run.TradingDate:yyyy-MM-dd} failed after {attempts} attempts",
                BodyTr: $"Planlı veri işi {run.TradingDate:yyyy-MM-dd} için {attempts} denemede tamamlanamadı; son bilinen iyi veri sunulmaya devam ediyor.",
                BodyEn: $"The scheduled data run for {run.TradingDate:yyyy-MM-dd} failed after {attempts} attempts; last-known-good data continues to be served.",
                SourceRef: null),
            cancellationToken);
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        // IANA on Linux/ICU; the Windows registry id is the fallback when the host runs
        // in invariant-globalization mode and cannot map IANA ids.
        foreach (var candidate in new[] { id, "Turkey Standard Time", "Europe/Istanbul" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(candidate);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        throw new InvalidOperationException($"Scheduler time zone '{id}' could not be resolved.");
    }

    /// <summary>One cron stream: the shared EOD trigger or a per-job cadence.</summary>
    private sealed class ScheduleStream
    {
        public required CronExpression Cron { get; init; }

        /// <summary>Whether the trading calendar gates this stream's occurrences.</summary>
        public required bool TradingDaysOnly { get; init; }

        /// <summary>The job codes to run; null means the registered jobs (EOD stream).</summary>
        public required IReadOnlyList<string>? JobCodes { get; init; }

        public DateTimeOffset? NextScheduled { get; set; }

        public ActiveRun? Active { get; set; }
    }

    /// <summary>One in-flight run: the target day it runs for and its retry state.</summary>
    private sealed class ActiveRun
    {
        public ActiveRun(DateOnly tradingDate, DateTimeOffset nextAttemptAt)
        {
            TradingDate = tradingDate;
            NextAttemptAt = nextAttemptAt;
        }

        public DateOnly TradingDate { get; }

        public int Attempts { get; set; }

        public DateTimeOffset NextAttemptAt { get; set; }
    }
}
