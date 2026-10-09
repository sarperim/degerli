using Cronos;
using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.Scheduling;

/// <summary>
/// C3c scheduler engine (FR-MDF-009, NFR-MDF-001): decides when the EOD run is due
/// (Cronos cron in a configured time zone, filtered by the trading calendar), executes
/// it through the TKT-mdf-002 job runner, and retries a failed attempt on the 5/15/60
/// ladder. It holds only in-memory scheduling state; the durable record is the
/// <c>ingest_runs</c> ledger the runner writes per attempt.
/// </summary>
/// <remarks>
/// The engine is transport-agnostic and clock-driven: <see cref="NextWake"/> and
/// <see cref="TickAsync"/> are pure scheduling decisions over a supplied instant, which
/// is what makes scheduled/retry behaviour deterministic under a fake clock. The
/// <see cref="IngestionSchedulerHostedService"/> is the thin production loop over them.
/// Running the concrete EOD chain order is not this ticket's concern — an empty
/// <see cref="IngestionSchedulerOptions.JobCodes"/> runs every registered job in
/// registration order; TKT-int-001 pins the cross-domain order.
/// </remarks>
public sealed class IngestionScheduler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IIngestionCalendar _calendar;
    private readonly IIngestionAlerter _alerter;
    private readonly IngestionSchedulerOptions _options;
    private readonly ILogger<IngestionScheduler> _logger;
    private readonly CronExpression _cron;
    private readonly TimeZoneInfo _timeZone;
    private readonly object _gate = new();

    private DateTimeOffset? _nextScheduled;
    private ActiveRun? _active;
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
        _cron = CronExpression.Parse(_options.Cron, CronFormat.IncludeSeconds);
        _timeZone = ResolveTimeZone(_options.TimeZone);
    }

    /// <summary>Whether the scheduler is enabled in configuration.</summary>
    public bool Enabled => _options.Enabled;

    /// <summary>The next instant the scheduler needs to be woken at, or null when idle.</summary>
    public DateTimeOffset? NextWake(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_active is { } active)
            {
                return active.NextAttemptAt;
            }

            EnsureScheduled(now);
            return _nextScheduled;
        }
    }

    /// <summary>Processes everything due at <paramref name="now"/>: a scheduled trigger
    /// (if the local date is a trading day) and/or a due retry.</summary>
    public async Task TickAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ActiveRun due;
        lock (_gate)
        {
            if (!_options.Enabled)
            {
                return;
            }

            if (_active is null)
            {
                EnsureScheduled(now);

                // Consume every cron occurrence up to `now`. A non-trading occurrence is
                // skipped silently (no ledger row); the first trading one starts a run.
                while (_nextScheduled is { } occurrence && occurrence <= now)
                {
                    _nextScheduled = _cron.GetNextOccurrence(occurrence, _timeZone, inclusive: false);
                    var tradingDate = TradingDateOf(occurrence);
                    if (_calendar.IsTradingDay(tradingDate))
                    {
                        _active = new ActiveRun(tradingDate, nextAttemptAt: now);
                        break;
                    }
                }
            }

            if (_active is null || now < _active.NextAttemptAt)
            {
                return;
            }

            due = _active;
        }

        await RunAttemptAsync(due, now, cancellationToken).ConfigureAwait(false);
    }

    private void EnsureScheduled(DateTimeOffset now) =>
        _nextScheduled ??= _cron.GetNextOccurrence(now, _timeZone, inclusive: true);

    private DateOnly TradingDateOf(DateTimeOffset occurrence)
    {
        var local = TimeZoneInfo.ConvertTime(occurrence, _timeZone);
        return DateOnly.FromDateTime(local.DateTime);
    }

    private async Task RunAttemptAsync(ActiveRun run, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var attempt = run.Attempts + 1;
        run.Attempts = attempt;

        Exception? failure = null;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<IIngestionJobRunner>();
            var request = new IngestionRequest(run.TradingDate);
            var context = new IngestionRunContext(attempt, _options.MaxAttempts);

            foreach (var jobCode in ResolveJobCodes(runner))
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
                _active = null;
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
                _active = null;
            }

            await RaiseExhaustedAsync(run, attempt, cancellationToken).ConfigureAwait(false);
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

    private IReadOnlyList<string> ResolveJobCodes(IIngestionJobRunner runner)
    {
        if (_options.JobCodes is { Count: > 0 })
        {
            return _options.JobCodes.ToList();
        }

        return _resolvedJobCodes ??= runner.JobCodes.ToList();
    }

    private Task RaiseExhaustedAsync(ActiveRun run, int attempts, CancellationToken cancellationToken)
    {
        var target = _options.JobCodes is { Count: > 0 }
            ? string.Join(", ", _options.JobCodes)
            : "registered jobs";

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

    /// <summary>One in-flight run: the trading day it targets and its retry state.</summary>
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
