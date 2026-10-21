using System.Text.Json;
using Cronos;
using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Prices;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.Scheduling;

/// <summary>
/// C3c scheduler engine (FR-MDF-009, NFR-MDF-001): decides when a run is due (Cronos cron in
/// a configured time zone, the shared EOD trigger and the morning catch-up trigger — both
/// filtered by the trading calendar — plus any independent per-job triggers), executes it
/// through the TKT-mdf-002 job runner, and retries a failed attempt on the 5/15/60 ladder. It
/// holds only in-memory scheduling state; the durable record is the <c>ingest_runs</c> ledger
/// the runner writes per attempt.
/// </summary>
/// <remarks>
/// The engine is transport-agnostic and clock-driven: <see cref="NextWake"/> and
/// <see cref="TickAsync"/> are pure scheduling decisions over a supplied instant, which
/// is what makes scheduled/retry behaviour deterministic under a fake clock. The
/// <see cref="IngestionSchedulerHostedService"/> is the thin production loop over them.
/// The shared EOD trigger runs <see cref="IngestionSchedulerOptions.JobCodes"/> — the pinned
/// <see cref="EodIngestChain"/> by default, or the explicitly configured order — on trading
/// days; each <see cref="JobScheduleOptions"/> entry additionally runs its own job on its own
/// cadence — the macro jobs (03 §9). The cross-domain EOD chain order is pinned by TKT-int-001.
/// <para>
/// A second chain stream — the <b>morning chain</b> (01 v1.5 §10.9.16, TKT-foundation-040) — is
/// created only when <see cref="IngestionSchedulerOptions.MorningCron"/> is configured (the
/// shipped `appsettings.json` sets 08:30 Europe/Istanbul). It is trading-day gated like the EOD
/// stream, resolves its run date to the <b>previous trading day</b> (BIST prices are EOD — a
/// Tuesday 08:30 pass can only ever repair Monday's session) and runs the pinned
/// <see cref="MorningIngestChain"/> with the <b>stored-prices repair gate</b>: when every
/// current constituent of the prices bound already has a stored row for the run date, the
/// prices job is skipped (one ledger row records the gate outcome) and the chain continues —
/// a fully covered day is never re-fetched.
/// </para>
/// <para>
/// <b>No attempt is unbounded</b> (01 v1.5 §10.9.17, TKT-foundation-041). A tick runs a stream's
/// chain sequentially, so one step whose await never completes used to starve every stream
/// silently — the live 2026-10-19/20 signature. Each attempt now races
/// <see cref="IngestionSchedulerOptions.MaxRunDuration"/> on the injected clock; on expiry the
/// attempt is cancelled, an honest <c>RUN_HUNG</c> run-ledger row and alert name the step, and
/// the ordinary retry ladder resumes. Every step logs its start and its outcome, and a
/// cancellation that is not the host's stopping token is a failed attempt, never an escape from
/// the loop.
/// </para>
/// </remarks>
public sealed class IngestionScheduler
{
    private static readonly JsonSerializerOptions StatsJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The named reason code of the hung-run watchdog's alert and ledger row (01 v1.5
    /// §10.9.17, TKT-foundation-041): a scheduled attempt exceeded
    /// <see cref="IngestionSchedulerOptions.MaxRunDuration"/> and was cancelled.
    /// </summary>
    public const string RunHungReasonCode = "RUN_HUNG";

    /// <summary>
    /// How far the morning stream's run date back-walks to find a trading day. Generous enough
    /// for any BIST holiday cluster (the longest closure is a 9-day religious holiday); the
    /// configured calendar is the only truth here, and a walk that exhausts it logs and uses
    /// the last candidate rather than fabricating a date.
    /// </summary>
    private const int PreviousTradingDayLookbackDays = 14;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IIngestionCalendar _calendar;
    private readonly IIngestionAlerter _alerter;
    private readonly IngestionSchedulerOptions _options;
    private readonly TimeProvider _clock;
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
        TimeProvider clock,
        ILogger<IngestionScheduler> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(alerter);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _calendar = calendar;
        _alerter = alerter;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
        _timeZone = ResolveTimeZone(_options.TimeZone);
        _streams = BuildStreams(_options);

        // The one line an operator greps to answer "is the 08:30 safety net on?": the
        // configured cron streams, the time zone, and — explicitly — an absent morning
        // stream, which is otherwise indistinguishable from a healthy scheduler (DR-02).
        // The watchdog bound rides along: "the chain can stall invisibly" is exactly the
        // question an operator asks while standing in front of a silent ledger (§10.9.17).
        _logger.LogInformation(
            "Ingestion scheduler streams: EOD cron '{EodCron}'; morning cron '{MorningCron}' ({MorningState}); time zone {TimeZone}; {PerJobStreams} per-job schedule(s); hung-run watchdog {Watchdog}.",
            _options.Cron,
            _options.MorningCron,
            string.IsNullOrWhiteSpace(_options.MorningCron)
                ? "DISABLED — no morning stream is created"
                : "enabled",
            _options.TimeZone,
            _options.JobSchedules.Count,
            _options.MaxRunDuration > TimeSpan.Zero
                ? $"bounded at {_options.MaxRunDuration} per attempt (RUN_HUNG)"
                : "DISABLED — a hung step can hold every stream silently");
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
    /// (if the local date is a trading day for the calendar-gated chain streams) and/or a due
    /// retry.</summary>
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
                    // on a calendar-gated stream is skipped silently (no ledger row); a
                    // per-job stream is not trading-calendar gated. The first eligible
                    // occurrence starts a run.
                    while (stream.NextScheduled is { } occurrence && occurrence <= now)
                    {
                        stream.NextScheduled = stream.Cron.GetNextOccurrence(occurrence, _timeZone, inclusive: false);
                        var occurrenceDate = TradingDateOf(occurrence);
                        if (!stream.TradingDaysOnly || _calendar.IsTradingDay(occurrenceDate))
                        {
                            stream.Active = new ActiveRun(ResolveRunDate(stream, occurrenceDate), nextAttemptAt: now);
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
            // The pinned cross-domain EOD chain (TKT-int-001) is the default; an explicit
            // configuration order overrides it.
            JobCodes = options.JobCodes is { Count: > 0 }
                ? options.JobCodes.ToList()
                : EodIngestChain.JobCodes.ToList(),
        };

        var streams = new List<ScheduleStream> { eod };

        // The morning chain (01 v1.5 §10.9.16, TKT-foundation-040): a second chain stream on
        // the same engine. Created only when configured, so a host/test that configures
        // nothing keeps the pre-040 single-stream behaviour byte-compatible.
        if (!string.IsNullOrWhiteSpace(options.MorningCron))
        {
            streams.Add(new ScheduleStream
            {
                Cron = CronExpression.Parse(options.MorningCron, CronFormat.IncludeSeconds),
                TradingDaysOnly = true,
                JobCodes = MorningIngestChain.JobCodes.ToList(),
                RunDate = RunDateMode.PreviousTradingDay,
                GatePricesOnStoredCoverage = true,
            });
        }

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

    /// <summary>
    /// The day a run targets (01 v1.5 §10.9.16). The EOD stream runs for its occurrence date;
    /// the morning stream runs for the <b>previous trading day</b> — BIST prices are EOD, so an
    /// 08:30 pass has no session of its own to ingest and must never imply newer market prices.
    /// The back-walk uses the configured calendar (the same weekday/ holiday posture every
    /// stream has: the holiday list is configuration and ships empty, so the back-walk is
    /// weekday-based in production today; a job's own stored-facts resolution self-heals, the
    /// §10.9.13 doctrine).
    /// </summary>
    private DateOnly ResolveRunDate(ScheduleStream stream, DateOnly occurrenceDate)
    {
        if (stream.RunDate == RunDateMode.OccurrenceDate)
        {
            return occurrenceDate;
        }

        var candidate = occurrenceDate;
        for (var back = 1; back <= PreviousTradingDayLookbackDays; back++)
        {
            candidate = occurrenceDate.AddDays(-back);
            if (_calendar.IsTradingDay(candidate))
            {
                return candidate;
            }
        }

        _logger.LogWarning(
            "No trading day found in the {Days} days before {Date}; the morning run falls back to {Candidate}, the last candidate tested.",
            PreviousTradingDayLookbackDays,
            occurrenceDate,
            candidate);

        return candidate;
    }

    private async Task RunAttemptAsync(
        ScheduleStream stream,
        ActiveRun run,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var attempt = run.Attempts + 1;
        run.Attempts = attempt;
        run.AttemptStartedAt = now;

        var step = new ChainStepProgress();
        var scope = _scopeFactory.CreateScope();
        var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var deadlineCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        IReadOnlyList<string> jobCodes = [];
        Exception? failure = null;
        var hung = false;

        // The hung-run watchdog (01 v1.5 §10.9.17): the attempt races a deadline on the
        // injected clock, so the bound is deterministic under a fake clock exactly like every
        // other scheduling decision here. A step that never completes can no longer hold the
        // loop — and with it every other stream — in silence.
        var deadline = _options.MaxRunDuration > TimeSpan.Zero
            ? Task.Delay(_options.MaxRunDuration, _clock, deadlineCts.Token)
            : null;

        var work = ExecuteChainAsync(stream, run, scope.ServiceProvider, attempt, step, attemptCts.Token);
        try
        {
            if (deadline is null)
            {
                var outcome = await work.ConfigureAwait(false);
                jobCodes = outcome.JobCodes;
                failure = outcome.Failure;
            }
            else
            {
                var winner = await Task.WhenAny(work, deadline).ConfigureAwait(false);
                if (ReferenceEquals(winner, work))
                {
                    var outcome = await work.ConfigureAwait(false);
                    jobCodes = outcome.JobCodes;
                    failure = outcome.Failure;
                    await deadlineCts.CancelAsync().ConfigureAwait(false);
                }
                else
                {
                    hung = true;
                    attemptCts.Cancel();
                    await deadlineCts.CancelAsync().ConfigureAwait(false);

                    // The step cannot be killed (a hung await is not interruptible); the scope is
                    // handed to the abandoned task so it is disposed only when that task ends,
                    // and its terminal exception is observed rather than left unobserved.
                    var abandonedScope = scope;
                    var abandonedAttempt = attemptCts;
                    var abandonedDeadline = deadlineCts;
                    var abandonedWork = work;
                    _ = work.ContinueWith(
                        completed =>
                        {
                            _ = completed.Exception;
                            abandonedScope.Dispose();
                            abandonedAttempt.Dispose();
                            abandonedDeadline.Dispose();
                        },
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);

                    failure = await HandleHungAttemptAsync(run, attempt, step, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host is stopping: unwind without pretending the run failed.
            throw;
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            if (!hung)
            {
                scope.Dispose();
                attemptCts.Dispose();
                deadlineCts.Dispose();
            }
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

        // Measured from the instant the attempt actually ended, not from the instant it was
        // due: a watchdog-bounded attempt ends up to MaxRunDuration after `now`, and a retry
        // computed from `now` would already be overdue the moment it is scheduled.
        var nextAttemptAt = _clock.GetUtcNow() + _options.RetryDelays[attempt - 1];
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

    /// <summary>
    /// Runs one attempt's chain, step by step, and reports the attempt's outcome instead of
    /// throwing — except for a cancellation, which means the attempt is over regardless of the
    /// reason (the watchdog's own bound, or the host stopping).
    /// <para>
    /// Every step announces itself before it is awaited (<b>chain-step progress</b>, 01 v1.5
    /// §10.9.17). That one log line is what the 2026-10-19/20 incident lacked: the ledger rows
    /// are written at <i>completion</i>, so the first failing night looked like "prices, then
    /// nothing at all".
    /// </para>
    /// </summary>
    private async Task<ChainAttemptOutcome> ExecuteChainAsync(
        ScheduleStream stream,
        ActiveRun run,
        IServiceProvider provider,
        int attempt,
        ChainStepProgress step,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> jobCodes = [];
        try
        {
            var runner = provider.GetRequiredService<IIngestionJobRunner>();
            jobCodes = ResolveJobCodes(stream, runner);
            var request = new IngestionRequest(run.TradingDate);
            var context = new IngestionRunContext(attempt, _options.MaxAttempts);

            for (var index = 0; index < jobCodes.Count; index++)
            {
                var jobCode = jobCodes[index];
                step.Begin(jobCode, index + 1, jobCodes.Count);
                _logger.LogInformation(
                    "Chain step {Step}/{Total} '{JobCode}' starting for {Date} (attempt {Attempt}).",
                    index + 1,
                    jobCodes.Count,
                    jobCode,
                    run.TradingDate,
                    attempt);

                var gateSkipped = await SkipPricesForCoveredDayAsync(
                        stream,
                        provider,
                        jobCode,
                        request.Date,
                        attempt,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (gateSkipped)
                {
                    _logger.LogInformation(
                        "Chain step {Step}/{Total} '{JobCode}' skipped by the prices coverage gate for {Date}.",
                        index + 1,
                        jobCodes.Count,
                        jobCode,
                        run.TradingDate);
                    continue;
                }

                var result = await runner.RunAsync(jobCode, request, context, cancellationToken).ConfigureAwait(false);

                _logger.LogInformation(
                    "Chain step {Step}/{Total} '{JobCode}' finished for {Date} with {Status} (written {Written}, unchanged {Unchanged}, quarantined {Quarantined}, skipped {Skipped}).",
                    index + 1,
                    jobCodes.Count,
                    jobCode,
                    run.TradingDate,
                    result.Status,
                    result.Written,
                    result.Unchanged,
                    result.Quarantined,
                    result.Skipped);

                if (result.Status == IngestResult.Failed)
                {
                    return new ChainAttemptOutcome(
                        jobCodes,
                        new InvalidOperationException($"Job '{jobCode}' reported a failed run."));
                }
            }

            return new ChainAttemptOutcome(jobCodes, Failure: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Our own bound fired (the watchdog path) or the host is stopping: not a job failure.
            throw;
        }
        catch (OperationCanceledException exception)
        {
            // A cancellation that is NOT ours — a job-level timeout, a source adapter's own CTS.
            // Before this guard it escaped the tick entirely: no retry ladder, no honest retry
            // log, and the loop re-fired the same due attempt forever (01 v1.5 §10.9.17).
            return new ChainAttemptOutcome(jobCodes, exception);
        }
        catch (Exception exception)
        {
            return new ChainAttemptOutcome(jobCodes, exception);
        }
    }

    /// <summary>
    /// The watchdog's terminal report (01 v1.5 §10.9.17): an honest run-ledger row naming the
    /// step that held the attempt, the attempt's bound, and a <c>RUN_HUNG</c> alert. Returns the
    /// failure the attempt reports, so the ordinary retry ladder takes over from here.
    /// </summary>
    private async Task<Exception> HandleHungAttemptAsync(
        ActiveRun run,
        int attempt,
        ChainStepProgress step,
        CancellationToken cancellationToken)
    {
        var hungJob = step.CurrentJobCode ?? "scheduler-chain";

        _logger.LogError(
            "Hung run detected for {Date} on attempt {Attempt}: chain step {Step}/{Total} '{JobCode}' exceeded the {Bound} bound and was cancelled ({ReasonCode}); the retry ladder takes over.",
            run.TradingDate,
            attempt,
            step.Index,
            step.Total,
            hungJob,
            _options.MaxRunDuration,
            RunHungReasonCode);

        await RecordHungRunAsync(run, attempt, step, hungJob, cancellationToken).ConfigureAwait(false);
        await RaiseHungAsync(run, attempt, step, hungJob, cancellationToken).ConfigureAwait(false);

        return new TimeoutException(
            $"Scheduled run for {run.TradingDate:yyyy-MM-dd} exceeded the {_options.MaxRunDuration} bound in chain step '{hungJob}' ({RunHungReasonCode}).");
    }

    /// <summary>
    /// The durable record of a hung attempt (NFR-MDF-001 — the ledger must never have a silent
    /// hole): one <c>ingest_runs</c> row whose job code is the hung step, with the watchdog's own
    /// fields (<c>reasonCode</c>/<c>hungJob</c>/<c>step</c>/<c>maxRunDurationSeconds</c>) so the
    /// class is filterable from the admin run list.
    /// </summary>
    private async Task RecordHungRunAsync(
        ActiveRun run,
        int attempt,
        ChainStepProgress step,
        string hungJob,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DegerliDbContext>();

            db.IngestRuns.Add(new IngestRun
            {
                JobCode = hungJob,
                StartedAt = run.AttemptStartedAt,
                FinishedAt = _clock.GetUtcNow(),
                Status = IngestResult.Failed,
                Error = $"{RunHungReasonCode}: chain step '{hungJob}' exceeded the {_options.MaxRunDuration} bound.",
                StatsJson = JsonSerializer.Serialize(new
                {
                    written = 0,
                    unchanged = 0,
                    quarantined = 0,
                    skipped = 0,
                    mode = "incremental",
                    reasonCode = RunHungReasonCode,
                    hungJob,
                    step = step.Index,
                    stepCount = step.Total,
                    runDate = run.TradingDate.ToString("yyyy-MM-dd"),
                    attempt,
                    retries = attempt - 1,
                    maxAttempts = _options.MaxAttempts,
                    maxRunDurationSeconds = (int)_options.MaxRunDuration.TotalSeconds,
                }, StatsJsonOptions),
            });

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // The alert below is the backstop: an unwritable ledger row must not also lose the
            // alarm, and must not turn a hung run into a crashed tick.
            _logger.LogError(
                exception,
                "Could not write the {ReasonCode} run-ledger row for {Date} (chain step '{JobCode}').",
                RunHungReasonCode,
                run.TradingDate,
                hungJob);
        }
    }

    private Task RaiseHungAsync(
        ActiveRun run,
        int attempt,
        ChainStepProgress step,
        string hungJob,
        CancellationToken cancellationToken)
    {
        var subject = $"Degerli alert: {RunHungReasonCode} — chain step '{hungJob}' exceeded {_options.MaxRunDuration} for {run.TradingDate:yyyy-MM-dd}";

        return _alerter.RaiseAsync(
            new IngestionAlert(
                JobCode: hungJob,
                ReasonCode: RunHungReasonCode,
                Subject: subject,
                BodyTr: $"Planlı veri zinciri {run.TradingDate:yyyy-MM-dd} için {step.Index}/{step.Total}. adımda ('{hungJob}') {_options.MaxRunDuration} boyunca ilerlemedi ve durduruldu ({attempt}. deneme); son bilinen iyi veri sunulmaya devam ediyor ve yeniden deneme merdiveni devreye girdi.",
                BodyEn: $"The scheduled chain for {run.TradingDate:yyyy-MM-dd} made no progress in step {step.Index}/{step.Total} ('{hungJob}') for {_options.MaxRunDuration} and was cancelled (attempt {attempt}); last-known-good data continues to be served and the retry ladder has taken over.",
                SourceRef: null),
            cancellationToken);
    }

    /// <summary>
    /// The morning chain's <b>prices repair gate</b> (01 v1.5 §10.9.16 — the §10.9.13
    /// stored-prices-as-truth doctrine applied to the scheduler): when every current
    /// constituent of the prices bound already has a stored <c>daily_prices</c> row for the run
    /// date, the prices job is <b>not invoked</b> — zero source calls, zero
    /// <c>CONFLICTING_VALUE</c> quarantine noise — and one run-ledger row records the gate's
    /// outcome. An uncovered/partial day (or an empty bound, which still has a cold start to
    /// seed or fetch) runs the job at the existing pacing.
    /// </summary>
    private async Task<bool> SkipPricesForCoveredDayAsync(
        ScheduleStream stream,
        IServiceProvider provider,
        string jobCode,
        DateOnly runDate,
        int attempt,
        CancellationToken cancellationToken)
    {
        if (!stream.GatePricesOnStoredCoverage
            || !string.Equals(jobCode, PricesJob.Code, StringComparison.Ordinal))
        {
            return false;
        }

        var indexCode = provider.GetRequiredService<IOptions<PricesSourceOptions>>().Value.UniverseIndex;
        var coverage = await provider
            .GetRequiredService<DailyPriceStore>()
            .GetDayCoverageAsync(indexCode, runDate, cancellationToken)
            .ConfigureAwait(false);

        if (!coverage.IsComplete)
        {
            return false;
        }

        await RecordPricesGateSkipAsync(provider, runDate, indexCode, coverage, attempt, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Morning chain: prices gate SKIPPED for {Date} — {Covered}/{Constituents} {Index} constituents already stored; zero source calls (01 §10.9.16).",
            runDate,
            coverage.Covered,
            coverage.Constituents,
            indexCode);

        return true;
    }

    /// <summary>
    /// The durable record of a gated prices step: one run-ledger row per attempt, in the same
    /// shape the job runner writes (counts + attempt context), with the gate's own fields
    /// (<c>gate</c>/<c>gateOutcome</c>/<c>constituents</c>/<c>covered</c>) so the morning pass
    /// is auditable at zero cost (NFR-MDF-001 run-ledger budget).
    /// </summary>
    private async Task RecordPricesGateSkipAsync(
        IServiceProvider provider,
        DateOnly runDate,
        string indexCode,
        PriceDayCoverage coverage,
        int attempt,
        CancellationToken cancellationToken)
    {
        var db = provider.GetRequiredService<DegerliDbContext>();
        var clock = provider.GetRequiredService<TimeProvider>();
        var recordedAt = clock.GetUtcNow();

        db.IngestRuns.Add(new IngestRun
        {
            JobCode = PricesJob.Code,
            StartedAt = recordedAt,
            FinishedAt = recordedAt,
            Status = IngestResult.Succeeded,
            StatsJson = JsonSerializer.Serialize(new
            {
                written = 0,
                unchanged = 0,
                quarantined = 0,
                skipped = 0,
                mode = "incremental",
                gate = "prices-coverage",
                gateOutcome = "skipped",
                index = indexCode,
                constituents = coverage.Constituents,
                covered = coverage.Covered,
                runDate = runDate.ToString("yyyy-MM-dd"),
                attempt,
                retries = attempt - 1,
                maxAttempts = _options.MaxAttempts,
            }, StatsJsonOptions),
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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
                Subject: $"Degerli alert: {target} scheduled run for {run.TradingDate:yyyy-MM-dd} failed after {attempts} attempts",
                BodyTr: $"{target} planlı veri işi {run.TradingDate:yyyy-MM-dd} için {attempts} denemede tamamlanamadı; son bilinen iyi veri sunulmaya devam ediyor.",
                BodyEn: $"The scheduled data run for {target} on {run.TradingDate:yyyy-MM-dd} failed after {attempts} attempts; last-known-good data continues to be served.",
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

    /// <summary>One cron stream: the shared EOD trigger, the morning chain, or a per-job cadence.</summary>
    private sealed class ScheduleStream
    {
        public required CronExpression Cron { get; init; }

        /// <summary>Whether the trading calendar gates this stream's occurrences.</summary>
        public required bool TradingDaysOnly { get; init; }

        /// <summary>The job codes to run; null means the registered jobs (EOD stream).</summary>
        public required IReadOnlyList<string>? JobCodes { get; init; }

        /// <summary>Which day a run targets (the EOD stream: its occurrence date).</summary>
        public RunDateMode RunDate { get; init; } = RunDateMode.OccurrenceDate;

        /// <summary>Whether the stored-prices repair gate applies to this stream's prices step.</summary>
        public bool GatePricesOnStoredCoverage { get; init; }

        public DateTimeOffset? NextScheduled { get; set; }

        public ActiveRun? Active { get; set; }
    }

    /// <summary>What day a stream's run targets (01 v1.5 §10.9.16).</summary>
    private enum RunDateMode
    {
        /// <summary>The run targets the occurrence's local date (the EOD stream).</summary>
        OccurrenceDate,

        /// <summary>The run targets the previous trading day (the morning catch-up stream).</summary>
        PreviousTradingDay,
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

        /// <summary>When the in-flight attempt started (the hung-run row's <c>startedAt</c>).</summary>
        public DateTimeOffset AttemptStartedAt { get; set; }
    }

    /// <summary>
    /// One attempt's outcome: the chain it was asked to run and the failure it hit, if any
    /// (<see langword="null"/> means the attempt succeeded). Failures are values, not thrown
    /// exceptions, so the watchdog can race the attempt without swallowing anything.
    /// </summary>
    private sealed record ChainAttemptOutcome(IReadOnlyList<string> JobCodes, Exception? Failure);

    /// <summary>
    /// The chain's step cursor (01 v1.5 §10.9.17): what the attempt is currently running. The
    /// watchdog reads it to name the step that held the run in both the ledger row and the
    /// <c>RUN_HUNG</c> alert, and it is what makes a stalled chain legible instead of silent.
    /// </summary>
    private sealed class ChainStepProgress
    {
        private volatile string? _currentJobCode;

        public string? CurrentJobCode => _currentJobCode;

        public int Index { get; private set; }

        public int Total { get; private set; }

        public void Begin(string jobCode, int index, int total)
        {
            _currentJobCode = jobCode;
            Index = index;
            Total = total;
        }
    }
}
