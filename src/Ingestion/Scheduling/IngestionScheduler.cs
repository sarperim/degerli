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
/// <b>No attempt is unbounded, and no single step is judged by the whole chain's budget</b>
/// (01 v1.5 §10.9.17, TKT-foundation-041; scope corrected by TKT-foundation-064). A tick runs a
/// stream's chain sequentially, so one step whose await never completes used to starve every
/// stream silently — the live 2026-10-19/20 signature. Each attempt races a
/// <b>whole-chain ceiling</b> (<see cref="IngestionSchedulerOptions.MaxRunDuration"/>) and each
/// individual step races its own <b>per-step bound</b>
/// (<see cref="IngestionSchedulerOptions.MaxStepDuration"/>) on the injected clock. The per-step
/// bound is the load-bearing one — a healthy paced chain legitimately needs ~100–120 minutes, so
/// a whole-attempt bound alone declares healthy nights hung — and it names the step that held the
/// attempt in exactly one <c>RUN_HUNG</c> run-ledger row and alert, carrying
/// <c>bound: "step" | "chain"</c> so the two bounds are distinguishable from the ledger alone.
/// The ordinary retry ladder then resumes. Every step logs its start and its outcome, and a
/// cancellation that is not the host's stopping token is a failed attempt, never an escape from
/// the loop.
/// </para>
/// <para>
/// <b>Streams do not starve each other</b> (01 v1.5 §10.9.17, TKT-foundation-062). A bounded
/// attempt still held the calling loop for its whole duration, and the loop is what evaluates
/// every other stream's due instant — so a retry armed for T, or the 05:30Z morning chain,
/// started only when the loop was released (live: ~6h41m late). <see cref="TickAsync"/>
/// therefore keeps evaluating due-ness on the injected clock while attempts are in flight: each
/// stream's armed retry and each cron occurrence starts at its own instant <b>for as long as the
/// in-flight attempt is at an await</b> — a step that blocks synchronously still holds the pump
/// thread and still postpones the other streams' due instants (the unbounded class under "Scope
/// of the guarantee") — and an in-flight stream is neither a wake nor a due candidate until its
/// attempt ends.
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
    /// The run-ledger job code of a <b>holiday skip</b> (TKT-foundation-048, planner pin 3): a
    /// trading-day-gated stream whose occurrence fell on a configured holiday ran no job, and
    /// this one short row says so — named here, dated in its stats (<c>holiday</c>) and in its
    /// timestamps. It is deliberately <b>not</b> a registered job code, so it can never be
    /// mistaken for a job's own success by a freshness query.
    /// </summary>
    public const string HolidaySkipJobCode = "holiday-skip";

    /// <summary>
    /// The two watchdog bounds (01 v1.5.43 §10.9.17, TKT-foundation-064). Exactly one of them
    /// fires for one hung attempt, and the value is recorded in the <c>RUN_HUNG</c> ledger row's
    /// <c>stats_json.bound</c> and named in the alert text, so an operator can tell a genuinely
    /// stuck <b>step</b> from a <b>chain</b> whose individually-healthy steps overran the ceiling.
    /// </summary>
    private static class ChainBoundNames
    {
        /// <summary>A single chain step exceeded its own <c>MaxStepDuration</c>.</summary>
        public const string Step = "step";

        /// <summary>The attempt as a whole exceeded the <c>MaxRunDuration</c> ceiling.</summary>
        public const string Chain = "chain";
    }

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
        // The watchdog bounds ride along: "the chain can stall invisibly" is exactly the
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
            _options.MaxStepDuration > TimeSpan.Zero || _options.MaxRunDuration > TimeSpan.Zero
                ? $"bounded at {DescribeConfiguredBound(_options.MaxStepDuration)} per step and {DescribeConfiguredBound(_options.MaxRunDuration)} per chain attempt (RUN_HUNG)"
                : "DISABLED — a hung step can hold every stream silently");
    }

    /// <summary>
    /// A configured bound for the operator line: the duration when it is armed, or
    /// <c>disabled</c> when zero/negative turned that bound off — never the raw <c>00:00:00</c>,
    /// which reads like a bound that fires immediately.
    /// </summary>
    private static string DescribeConfiguredBound(TimeSpan bound) =>
        bound > TimeSpan.Zero ? bound.ToString() : "disabled";

    /// <summary>Whether the scheduler is enabled in configuration.</summary>
    public bool Enabled => _options.Enabled;

    /// <summary>The next instant the scheduler needs to be woken at, or null when idle.
    /// A stream whose attempt is in flight (only possible inside a tick) has no wake: its own
    /// attempt owns it until it ends.</summary>
    public DateTimeOffset? NextWake(DateTimeOffset now)
    {
        lock (_gate)
        {
            DateTimeOffset? next = null;
            foreach (var stream in _streams)
            {
                if (stream.InFlight)
                {
                    continue;
                }

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

    /// <summary>
    /// Processes everything due at <paramref name="now"/>: a scheduled trigger (if the local
    /// date is a trading day for the calendar-gated chain streams) and/or a due retry — and,
    /// while an attempt is in flight, every due instant that falls on <b>another</b> stream.
    /// </summary>
    /// <remarks>
    /// <b>Streams do not starve each other (01 v1.5 §10.9.17, TKT-foundation-062).</b> The
    /// calling loop is a single logical loop: it computes <see cref="NextWake"/>, delays, and
    /// calls this method, and it is inside this method for as long as any attempt it started
    /// runs. Before this pump, the tick awaited each due stream's attempt to its end in stream
    /// order, so an attempt in flight postponed <i>every</i> other stream's due instant — a
    /// retry armed for T fired hours late, and a cron occurrence (the 05:30Z morning chain)
    /// was consumed only when the loop was released, firing at the release instant instead of
    /// at its own (the live 2026-10-22/23 trace: morning chain at 12:11:24Z, ~6h41m late).
    /// The pump therefore keeps evaluating due-ness on the injected clock itself while
    /// attempts are in flight: each stream's armed retry and each cron occurrence starts at
    /// its own instant <b>for as long as the in-flight attempt is at an await</b>. A step that
    /// blocks synchronously still holds the pump thread and still postpones the other streams'
    /// due instants (the unbounded class under "Scope of the guarantee"). Lateness is then
    /// bounded by nothing this engine adds — the only remaining bound on an attempt is its own
    /// <see cref="IngestionSchedulerOptions.MaxRunDuration"/> watchdog.
    /// </remarks>
    public async Task TickAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return;
        }

        // The attempts this tick owns. Each is awaited here (the tick's contract: when it
        // returns, the attempts it started have ended), but concurrently rather than one
        // stream at a time.
        var running = new List<Task>();
        StartDueAttempts(now, running, cancellationToken);

        while (running.Count > 0)
        {
            // Settle the attempts that ended — awaiting them folds their failures (including
            // the host-stop cancellation) into this tick exactly as the sequential tick did.
            foreach (var ended in running.Where(task => task.IsCompleted).ToList())
            {
                running.Remove(ended);
                try
                {
                    await ended.ConfigureAwait(false);
                }
                catch
                {
                    // This tick is unwinding on the attempt's failure (or the host's stop): the
                    // siblings it already started end on their own — each clears its stream's
                    // InFlight in its finally — but this returning tick will never await them,
                    // so their terminal faults are observed here rather than left unobserved.
                    ObserveAbandoned(running);
                    throw;
                }
            }

            if (running.Count == 0)
            {
                return;
            }

            var current = _clock.GetUtcNow();
            var next = NextWake(current);
            if (next is { } immediate && immediate <= current)
            {
                // A stopping host is not a due instant: do not start an attempt it would only
                // cancel. The siblings already started end on their own; observe their faults
                // since this tick is about to unwind without awaiting them.
                if (cancellationToken.IsCancellationRequested)
                {
                    ObserveAbandoned(running);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (StartDueAttempts(current, running, cancellationToken) == 0)
                {
                    // Nothing was startable at an instant `NextWake` reported as due: the
                    // remaining work is the running attempts. Never spin; wait for them.
                    await AwaitAnyRunningAsync(running, wakeAt: null, cancellationToken).ConfigureAwait(false);
                }

                continue;
            }

            var pause = await AwaitAnyRunningAsync(running, next, cancellationToken).ConfigureAwait(false);
            if (pause)
            {
                // A stopping host is not a due instant: unwind instead of starting work. The
                // siblings already started end on their own (their finally clears InFlight);
                // observe their faults since this tick unwinds without awaiting them.
                if (cancellationToken.IsCancellationRequested)
                {
                    ObserveAbandoned(running);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                // Another stream's due instant arrived while an attempt held the tick: start it
                // now, at its own instant, never at this tick's release instant.
                StartDueAttempts(_clock.GetUtcNow(), running, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Waits until one of <paramref name="running"/> ends or <paramref name="wakeAt"/> falls due
    /// on the injected clock. Returns true when the wait ended because the instant arrived
    /// (a due re-evaluation is owed) and false when an attempt ended.
    /// </summary>
    private async Task<bool> AwaitAnyRunningAsync(
        IReadOnlyList<Task> running,
        DateTimeOffset? wakeAt,
        CancellationToken cancellationToken)
    {
        if (wakeAt is not { } at)
        {
            await Task.WhenAny(running).ConfigureAwait(false);
            return false;
        }

        var wait = at - _clock.GetUtcNow();
        using var pauseCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var pause = Task.Delay(wait > TimeSpan.Zero ? wait : TimeSpan.Zero, _clock, pauseCts.Token);
        var waiters = running.Append(pause).ToList();
        var completed = await Task.WhenAny(waiters).ConfigureAwait(false);

        await pauseCts.CancelAsync().ConfigureAwait(false);

        return ReferenceEquals(completed, pause);
    }

    /// <summary>
    /// Observes the terminal exception of attempts this tick started but will not await because
    /// it is unwinding (a faulted sibling, or a stopping host). The attempts still end — each
    /// clears its stream's <c>InFlight</c> in its <c>finally</c> — but a fault raised after the
    /// unwind (for example the exhausted alert) must not surface later as an unobserved-task
    /// exception. A task that is awaited normally is unaffected: this only reads its exception.
    /// </summary>
    private static void ObserveAbandoned(IEnumerable<Task> running)
    {
        foreach (var task in running)
        {
            _ = task.ContinueWith(
                static abandoned => _ = abandoned.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    /// <summary>
    /// Starts this instant's due attempts — a scheduled trigger (trading-day gated for the
    /// calendar-gated streams) and/or a due retry — and appends their tasks to
    /// <paramref name="running"/>. A stream whose attempt is already in flight is skipped:
    /// its next attempt is decided when that attempt ends (success or the retry ladder).
    /// Returns how many tasks were started (attempts and holiday-skip ledger writes).
    /// <para>
    /// <b>A holiday skip is never silent</b> (TKT-foundation-048, planner pin 3). A gated
    /// occurrence that falls on a <i>configured holiday</i> runs no job but writes one short
    /// run-ledger row (<see cref="HolidaySkipJobCode"/>) naming the stream and dating the
    /// holiday — once per stream per holiday <i>per process lifetime</i>, not per occurrence, so a
    /// four-pass cadence does not flood the ledger; the dedupe marker is in memory, so a restart
    /// mid-holiday may re-record the same holiday (one extra row, never a silent gap). A
    /// <i>weekend</i> occurrence stays silent: it needs no explanation
    /// and the pre-048 ledger contract for weekends is unchanged. The skip is not an attempt:
    /// no step timer is armed, no <c>RUN_HUNG</c> can be declared, and the retry ladder is not
    /// involved — it completes immediately.
    /// </para>
    /// </summary>
    private int StartDueAttempts(DateTimeOffset now, List<Task> running, CancellationToken cancellationToken)
    {
        var due = new List<(ScheduleStream Stream, ActiveRun Run)>();
        var holidaySkips = new List<(ScheduleStream Stream, DateOnly Holiday)>();
        lock (_gate)
        {
            foreach (var stream in _streams)
            {
                if (stream.InFlight)
                {
                    continue;
                }

                if (stream.Active is null)
                {
                    EnsureScheduled(stream, now);

                    // Consume every cron occurrence up to `now`. A non-trading occurrence on a
                    // calendar-gated stream runs nothing; a per-job stream is gated only when
                    // its own configuration says so. The first eligible occurrence starts a run.
                    while (stream.NextScheduled is { } occurrence && occurrence <= now)
                    {
                        stream.NextScheduled = stream.Cron.GetNextOccurrence(occurrence, _timeZone, inclusive: false);
                        var occurrenceDate = TradingDateOf(occurrence);
                        if (stream.TradingDaysOnly && !_calendar.IsTradingDay(occurrenceDate))
                        {
                            if (_calendar.IsHoliday(occurrenceDate)
                                && stream.HolidaySkipRecordedFor != occurrenceDate)
                            {
                                stream.HolidaySkipRecordedFor = occurrenceDate;
                                holidaySkips.Add((stream, occurrenceDate));
                            }

                            continue;
                        }

                        stream.Active = new ActiveRun(ResolveRunDate(stream, occurrenceDate), nextAttemptAt: now);
                        break;
                    }
                }

                if (stream.Active is { } active && now >= active.NextAttemptAt)
                {
                    stream.InFlight = true;
                    due.Add((stream, active));
                }
            }
        }

        foreach (var (stream, run) in due)
        {
            running.Add(RunAttemptAsync(stream, run, now, cancellationToken));
        }

        foreach (var (stream, holiday) in holidaySkips)
        {
            running.Add(RecordHolidaySkipAsync(stream, holiday, cancellationToken));
        }

        return due.Count + holidaySkips.Count;
    }

    private static IReadOnlyList<ScheduleStream> BuildStreams(IngestionSchedulerOptions options)
    {
        var eod = new ScheduleStream
        {
            Name = "eod-chain",
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
                Name = "morning-chain",
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
                Name = $"job:{schedule.JobCode}",
                Cron = CronExpression.Parse(schedule.Cron, CronFormat.IncludeSeconds),
                // Per-job streams are ungated by default (macro/fund data is published every
                // day); a market stream opts in — the intraday sweep does (TKT-foundation-048).
                TradingDaysOnly = schedule.TradingDaysOnly,
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
    /// The back-walk uses the configured calendar (the same weekday/holiday posture every
    /// stream has: the shipped configuration carries the BIST closures, so the walk skips both
    /// weekends and those configured holidays; a job's own stored-facts resolution still
    /// self-heals, the §10.9.13 doctrine).
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
        try
        {
            await RunAttemptCoreAsync(stream, run, now, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // The attempt is over (succeeded, failed onto the ladder, exhausted, or the host
            // stopped) whichever way it ended: the stream is schedulable again, so the next
            // due instant — its own wake or another stream's — can be evaluated.
            lock (_gate)
            {
                stream.InFlight = false;
            }
        }
    }

    private async Task RunAttemptCoreAsync(
        ScheduleStream stream,
        ActiveRun run,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var attempt = run.Attempts + 1;
        run.Attempts = attempt;
        run.AttemptStartedAt = now;

        var step = new ChainStepProgress();

        // The bound that fires hands its finding to the attempt-level watchdog (01 v1.5.43
        // §10.9.17, TKT-foundation-064): the per-step bound is enforced inside the chain, the
        // whole-chain ceiling out here, and both end in the same single report.
        var boundSignal = new TaskCompletionSource<ChainBoundExpiry>(TaskCreationOptions.RunContinuationsAsynchronously);

        IReadOnlyList<string> jobCodes = [];
        Exception? failure = null;
        var hung = false;

        // The instant the attempt actually ended, when that is not "now": the hung path ends the
        // attempt when the bound is honoured (the cancellation), and the post-cancel settle
        // window that follows is the watchdog's own grace — not run time. Recording the row's
        // `finished_at` and measuring the retry from the bound instant keeps 064's "the retry
        // ladder starts from the attempt's end" rule exact instead of shifting it by the grace
        // (TKT-foundation-085).
        DateTimeOffset? attemptEndedAt = null;

        // The attempt's owned resources are created inside the failure-classifying try (DR-1,
        // TKT-foundation-059) and disposed in its finally. Anything that throws before the
        // attempt's first await — most realistically a mis-typed `MaxRunDuration` beyond
        // Task.Delay's accepted range — used to escape RunAttemptAsync with `Attempts` already
        // bumped and `NextAttemptAt` still in the past, so the hosted loop refired the attempt
        // immediately and forever. A prologue failure is now a failed attempt on the ladder.
        // (A mis-typed `MaxStepDuration` is caught earlier still, inside ExecuteChainAsync's own
        // catch — either way it is a failed attempt, never a hot refire.)
        IServiceScope? ownedScope = null;
        CancellationTokenSource? ownedAttemptCts = null;
        CancellationTokenSource? ownedDeadlineCts = null;

        try
        {
            var scope = ownedScope = _scopeFactory.CreateScope();
            var attemptCts = ownedAttemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var deadlineCts = ownedDeadlineCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // The hung-run watchdog's whole-chain ceiling (01 v1.5 §10.9.17): the attempt races a
            // deadline on the injected clock, so the bound is deterministic under a fake clock
            // exactly like every other scheduling decision here. A chain can no longer hold the
            // loop — and with it every other stream — in silence.
            var deadline = _options.MaxRunDuration > TimeSpan.Zero
                ? Task.Delay(_options.MaxRunDuration, _clock, deadlineCts.Token)
                : null;

            // The watchdog bounds an await, not a synchronous block (01 v1.5 §10.9.17 "Scope of
            // the guarantee"): this chain is invoked on the loop thread, so a step that blocks
            // synchronously before its first await cannot be pre-empted by an in-process timer.
            // Such a step is still *legible* — its "Chain step … starting" line was already
            // written — so a synchronous freeze is never silent, even though only the awaits are
            // bounded here.
            var work = ExecuteChainAsync(stream, run, scope.ServiceProvider, attempt, step, boundSignal, attemptCts.Token);

            // Whichever comes first — the chain finishing, the whole-chain ceiling, or a single
            // step's own bound — decides this attempt. With both bounds disabled there is nothing
            // to race but the chain itself (the documented unbounded opt-out).
            var waiters = new List<Task>(3) { work, boundSignal.Task };
            if (deadline is not null)
            {
                waiters.Add(deadline);
            }

            var winner = await Task.WhenAny(waiters).ConfigureAwait(false);

            // Which bound, if any, fired. This is read from the bounds' own completion rather than
            // from whichever task `WhenAny` happened to return: a step that answers the bound's
            // cancellation can complete the chain in the same window, and `WhenAny` may then hand
            // back the chain task — the attempt is still a hung one, never an ordinary failure.
            // The step bound is checked first because it is the more actionable attribution: if it
            // completed at all, a step really did exceed its own bound, and the ceiling having
            // expired in the same window does not change that. A ceiling-only expiry is untouched
            // by the reorder — `boundSignal` is completed exclusively by a per-step timer.
            ChainBoundExpiry? boundExpiry;
            if (boundSignal.Task.IsCompleted)
            {
                boundExpiry = boundSignal.Task.Result;
            }
            else if (deadline is not null && ReferenceEquals(winner, deadline))
            {
                boundExpiry = new ChainBoundExpiry(ChainBoundNames.Chain, step.Snapshot());
            }
            else
            {
                boundExpiry = null;
            }

            if (boundExpiry is null)
            {
                var outcome = await work.ConfigureAwait(false);
                jobCodes = outcome.JobCodes;
                failure = outcome.Failure;
                await deadlineCts.CancelAsync().ConfigureAwait(false);
            }
            else
            {
                // The step bound's snapshot is already the pre-cancel one (DR-3,
                // TKT-foundation-059); the chain ceiling's was taken above, before anything is
                // cancelled. Either way the record names the state the run was in when the bound
                // expired — never the next step an abandoned chain may have advanced to.
                var expiry = boundExpiry.Value;

                if (cancellationToken.IsCancellationRequested)
                {
                    // The host is stopping and the bound only lost the race to it: this is
                    // a shutdown, not a hung run — no RUN_HUNG row and no alert on the way out.
                    // The attempt's task is still running, so its scope/CTS pair is abandoned
                    // (never disposed under it) and the stop is rethrown to the loop.
                    hung = true;
                    attemptCts.Cancel();
                    await deadlineCts.CancelAsync().ConfigureAwait(false);
                    DisposeWithAbandonedWork(work, scope, attemptCts, deadlineCts);

                    throw new OperationCanceledException(cancellationToken);
                }

                hung = true;
                attemptEndedAt = _clock.GetUtcNow();
                attemptCts.Cancel();
                await deadlineCts.CancelAsync().ConfigureAwait(false);

                // DR-2 (TKT-foundation-059) as a bounded settle window (TKT-foundation-085): the
                // cancel window is not instantaneous — a step that swallows the cancellation can
                // still finish successfully after it, and its continuation may be *queued* rather
                // than resumed inline on this thread. A single synchronous re-check therefore saw
                // a pending task under load, wrote RUN_HUNG, and the queued continuation then wrote
                // the step's own success row: two rows, a spurious alert, a spurious retry. The
                // re-check is now an interval — the attempt is given the configured grace to
                // settle — and only its expiry is the hung verdict.
                //
                // Prefer the terminal truth: an attempt that genuinely completed (no failure) in
                // that window is recorded by its own row and is never also declared RUN_HUNG (one
                // attempt, one row, one retry decision). A chain that merely *ended* on our own
                // cancellation is not a success: it is the hung attempt this report is about.
                var settleWindow = _options.PostCancelSettleWindow;
                Task? settle;
                try
                {
                    settle = CreateSettleDelay();
                }
                catch (ArgumentOutOfRangeException)
                {
                    // A mis-typed window (beyond Task.Delay's accepted range) is an ordinary failed
                    // attempt on the ladder — the DR-1 path — never an escape out of the tick; the
                    // running attempt is abandoned exactly as the hung path abandons it.
                    DisposeWithAbandonedWork(work, scope, attemptCts, deadlineCts);
                    throw;
                }

                if (settle is not null)
                {
                    // The grace is stated when it is given, not only when it expires: an operator
                    // watching the log of a slow-but-then-fine attempt should see that the watchdog
                    // waited instead of guessing why nothing was written yet.
                    _logger.LogWarning(
                        "Settling attempt {Attempt} for {Date}: chain step {Step}/{Total} '{JobCode}' exceeded the {Bound} and was cancelled; giving the attempt the {Settle} post-cancel settle window before declaring the run hung ({ReasonCode}, bound={BoundKind}).",
                        attempt,
                        run.TradingDate,
                        expiry.Step.Index,
                        expiry.Step.Total,
                        expiry.Step.JobCode,
                        DescribeBound(expiry.Bound, _options.MaxStepDuration, _options.MaxRunDuration),
                        settleWindow,
                        RunHungReasonCode,
                        expiry.Bound);

                    await Task.WhenAny(work, settle).ConfigureAwait(false);
                }

                if (work.IsCompletedSuccessfully && work.Result.Failure is null)
                {
                    hung = false;
                    attemptEndedAt = null;
                    var outcome = await work.ConfigureAwait(false);
                    jobCodes = outcome.JobCodes;
                    failure = outcome.Failure;
                }
                else
                {
                    // The step cannot be killed (a hung await is not interruptible); the scope
                    // is handed to the abandoned task so it is disposed only when that task
                    // ends, and its terminal exception is observed rather than left
                    // unobserved.
                    DisposeWithAbandonedWork(work, scope, attemptCts, deadlineCts);

                    // The chain's own codes are not known on this path, so the hung step is
                    // what the exhausted alert names (R6).
                    jobCodes = [expiry.Step.JobCode];
                    failure = await HandleHungAttemptAsync(
                            run,
                            attempt,
                            expiry,
                            attemptEndedAt ?? _clock.GetUtcNow(),
                            cancellationToken)
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
                ownedScope?.Dispose();
                ownedAttemptCts?.Dispose();
                ownedDeadlineCts?.Dispose();
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
        // computed from `now` would already be overdue the moment it is scheduled. The hung
        // path's `attemptEndedAt` keeps that instant the bound's, so the post-cancel settle
        // window (TKT-foundation-085) never delays the ladder.
        var nextAttemptAt = (attemptEndedAt ?? _clock.GetUtcNow()) + _options.RetryDelays[attempt - 1];
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
        TaskCompletionSource<ChainBoundExpiry> boundSignal,
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

                var result = await RunStepUnderBoundAsync(
                        token => runner.RunAsync(jobCode, request, context, token),
                        step,
                        boundSignal,
                        cancellationToken)
                    .ConfigureAwait(false);

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
    /// Awaits one chain step under the watchdog's <b>per-step bound</b> (01 v1.5.43 §10.9.17,
    /// TKT-foundation-064). The bound is the longest a single <c>RunAsync(jobCode, …)</c> may
    /// take on its own — independent of how long the chain before it legitimately consumed — so a
    /// slow-but-healthy step (the live 58–74 minute corporate-actions walk) is never declared
    /// hung. When the bound expires, the step's progress is snapshotted <b>before</b> any
    /// cancellation (DR-3, TKT-foundation-059) and handed to the attempt-level watchdog, which
    /// owns the cancellation, the single <c>RUN_HUNG</c> row and the alert; this method then lets
    /// the attempt's cancellation surface. A step that ignores cancellation never returns and the
    /// attempt is abandoned exactly as the whole-chain bound's path already did.
    /// <para>
    /// The per-step bound composes with the ceiling: whichever expires first fires, and the
    /// report says which (<c>bound: "step" | "chain"</c>).
    /// </para>
    /// </summary>
    private async Task<IngestResult> RunStepUnderBoundAsync(
        Func<CancellationToken, Task<IngestResult>> stepCall,
        ChainStepProgress step,
        TaskCompletionSource<ChainBoundExpiry> boundSignal,
        CancellationToken cancellationToken)
    {
        if (_options.MaxStepDuration <= TimeSpan.Zero)
        {
            return await stepCall(cancellationToken).ConfigureAwait(false);
        }

        using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // The identity this arm is armed for. `step` is the chain-wide cursor and `Begin(...)`
        // overwrites it at every step, so a callback that outlives its step would otherwise name
        // whichever step is in flight when it happens to run — the DR-3 wrong-step class
        // (TKT-foundation-059) on the per-step bound. The arm therefore carries the identity and
        // the callback matches on it before publishing anything.
        var arm = new StepBoundArm(step.CurrentJobCode, step.Index, step.Total);

        // The bound is armed as a clock-driven timer over *this step only*: on expiry it records
        // the step's progress (before any cancellation, DR-3/TKT-foundation-059) and hands the
        // finding to the attempt-level watchdog, which owns the cancellation of the attempt, the
        // single RUN_HUNG ledger row and the alert. Cancelling the attempt's own token propagates
        // into this step through the linked source, so the ordinary cancellation path keeps its
        // exact synchronous shape (a step that finishes inside the cancel window still finishes).
        using var stepDeadline = _clock.CreateTimer(
            _ =>
            {
                // A stopping host — or an attempt another bound already declared hung — is not a
                // per-step expiry.
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                // Only the step this timer was armed for may be declared hung: a callback that
                // runs after its step returned (and, in the worst case, after the chain moved on
                // to the next step) must publish nothing at all. This guard precedes every line
                // that could declare a hung run.
                if (arm.Retired || !arm.Matches(step))
                {
                    return;
                }

                // The snapshot is the armed identity itself — already immutable, and therefore
                // necessarily taken before any cancellation in this callback.
                boundSignal.TrySetResult(new ChainBoundExpiry(ChainBoundNames.Step, arm.Snapshot));

                try
                {
                    stepCts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // The step finished (and its bound's timers were disposed) as this callback
                    // was already running: the step's own outcome is the truth either way.
                }
            },
            state: null,
            dueTime: _options.MaxStepDuration,
            period: Timeout.InfiniteTimeSpan);

        try
        {
            return await stepCall(stepCts.Token).ConfigureAwait(false);
        }
        finally
        {
            // Whatever ended the step (completion, its own exception, the attempt's cancellation)
            // retires this arm, so a queued callback can never declare it hung afterwards.
            arm.Retire();
        }
    }

    /// <summary>
    /// The watchdog's <b>post-cancel settle window</b> as a clock-driven delay (01 v1.5.59
    /// §10.9.17, TKT-foundation-085), or <c>null</c> when the window is disabled
    /// (<see cref="IngestionSchedulerOptions.PostCancelSettleWindow"/> <c>&lt;= 0</c>): the
    /// re-check is then the pre-085 single synchronous one, the documented opt-out.
    /// <para>
    /// The delay is a <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/> on the
    /// injected clock, so the grace is deterministic under a fake clock exactly like every other
    /// scheduling decision here — no real-time sleep bounds a test. A value beyond that
    /// overload's accepted range throws, which the caller classifies as an ordinary failed
    /// attempt (the DR-1 path).
    /// </para>
    /// </summary>
    private Task? CreateSettleDelay()
    {
        var window = _options.PostCancelSettleWindow;
        return window > TimeSpan.Zero ? Task.Delay(window, _clock) : null;
    }

    /// <summary>
    /// The grace the watchdog gave a cancelled attempt before declaring it hung, as an operator
    /// reads it in the ledger row, the alert and the log line — or <c>null</c> when the window is
    /// disabled (then the pre-085 immediate verdict is the honest description and nothing is
    /// claimed about a grace that was never given).
    /// </summary>
    private string? DescribeSettleWindow()
    {
        var window = _options.PostCancelSettleWindow;
        return window > TimeSpan.Zero ? window.ToString() : null;
    }

    /// <summary>
    /// The watchdog's terminal report (01 v1.5.43 §10.9.17): an honest run-ledger row naming the
    /// step that held the attempt, <b>which bound fired</b> (per-step vs whole-chain ceiling), and
    /// a <c>RUN_HUNG</c> alert saying the same. Returns the failure the attempt reports, so the
    /// ordinary retry ladder takes over from here.
    /// <para>
    /// <paramref name="endedAt"/> is the instant the bound ended the attempt; the post-cancel
    /// settle window after it is the watchdog's grace and deliberately does not move the record's
    /// timestamps (TKT-foundation-085).
    /// </para>
    /// </summary>
    private async Task<Exception> HandleHungAttemptAsync(
        ActiveRun run,
        int attempt,
        ChainBoundExpiry expiry,
        DateTimeOffset endedAt,
        CancellationToken cancellationToken)
    {
        var step = expiry.Step;
        var hungJob = step.JobCode;
        var bound = DescribeBound(expiry.Bound, _options.MaxStepDuration, _options.MaxRunDuration);
        var settle = DescribeSettleWindow();

        _logger.LogError(
            "Hung run detected for {Date} on attempt {Attempt}: chain step {Step}/{Total} '{JobCode}' exceeded the {Bound} and was cancelled ({ReasonCode}, bound={BoundKind}){Settled}; the retry ladder takes over.",
            run.TradingDate,
            attempt,
            step.Index,
            step.Total,
            hungJob,
            bound,
            RunHungReasonCode,
            expiry.Bound,
            settle is null
                ? string.Empty
                : $"; the cancelled step was given the {settle} post-cancel settle window and did not settle in it");

        await RecordHungRunAsync(run, attempt, expiry, endedAt, cancellationToken).ConfigureAwait(false);
        await RaiseHungAsync(run, attempt, expiry, cancellationToken).ConfigureAwait(false);

        return new TimeoutException(
            settle is null
                ? $"Scheduled run for {run.TradingDate:yyyy-MM-dd} exceeded the {bound} in chain step '{hungJob}' ({RunHungReasonCode}, bound={expiry.Bound})."
                : $"Scheduled run for {run.TradingDate:yyyy-MM-dd} exceeded the {bound} in chain step '{hungJob}' ({RunHungReasonCode}, bound={expiry.Bound}); the step did not settle within the {settle} post-cancel settle window.");
    }

    /// <summary>
    /// The operator-facing name of a bound, shared by the log line, the ledger row's error text
    /// and the alert: <c>per-step bound 01:00:00</c> or <c>whole-chain ceiling 04:00:00</c>. The
    /// wording is what makes the two classes distinguishable in a grep.
    /// </summary>
    private static string DescribeBound(string bound, TimeSpan maxStepDuration, TimeSpan maxRunDuration) =>
        bound == ChainBoundNames.Step
            ? $"per-step bound {maxStepDuration}"
            : $"whole-chain ceiling {maxRunDuration}";

    /// <summary>Seconds of a configured bound, floored at zero (a disabled bound records 0).</summary>
    private static int BoundSeconds(TimeSpan bound) => (int)Math.Max(0, bound.TotalSeconds);

    /// <summary>
    /// Hands an abandoned attempt's DI scope and cancellation sources to a continuation on the
    /// task that is still running, so they are disposed only when that task finally ends and its
    /// terminal exception is observed instead of surfacing as an unobserved-task crash
    /// (01 v1.5 §10.9.17 step 1).
    /// </summary>
    private static void DisposeWithAbandonedWork(
        Task work,
        IServiceScope scope,
        CancellationTokenSource attemptCts,
        CancellationTokenSource deadlineCts)
    {
        _ = work.ContinueWith(
            completed =>
            {
                _ = completed.Exception;
                scope.Dispose();
                attemptCts.Dispose();
                deadlineCts.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
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
        ChainBoundExpiry expiry,
        DateTimeOffset endedAt,
        CancellationToken cancellationToken)
    {
        var step = expiry.Step;
        var hungJob = step.JobCode;
        var bound = DescribeBound(expiry.Bound, _options.MaxStepDuration, _options.MaxRunDuration);
        var settle = DescribeSettleWindow();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DegerliDbContext>();

            db.IngestRuns.Add(new IngestRun
            {
                JobCode = hungJob,
                StartedAt = run.AttemptStartedAt,
                FinishedAt = endedAt,
                Status = IngestResult.Failed,
                Error = settle is null
                    ? $"{RunHungReasonCode}: chain step '{hungJob}' exceeded the {bound}."
                    : $"{RunHungReasonCode}: chain step '{hungJob}' exceeded the {bound} and did not settle within the {settle} post-cancel settle window.",
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
                    // Which bound fired (TKT-foundation-064) and both configured bounds,
                    // additively — a RUN_HUNG row is self-explanatory to an operator.
                    bound = expiry.Bound,
                    maxStepDurationSeconds = BoundSeconds(_options.MaxStepDuration),
                    maxRunDurationSeconds = BoundSeconds(_options.MaxRunDuration),
                    // The grace this verdict was given (TKT-foundation-085): 0 when disabled.
                    settleWindowSeconds = BoundSeconds(_options.PostCancelSettleWindow),
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
        ChainBoundExpiry expiry,
        CancellationToken cancellationToken)
    {
        var step = expiry.Step;
        var hungJob = step.JobCode;
        var bound = DescribeBound(expiry.Bound, _options.MaxStepDuration, _options.MaxRunDuration);
        var settle = DescribeSettleWindow();
        var subject = settle is null
            ? $"Degerli alert: {RunHungReasonCode} — chain step '{hungJob}' exceeded the {bound} for {run.TradingDate:yyyy-MM-dd}"
            : $"Degerli alert: {RunHungReasonCode} — chain step '{hungJob}' exceeded the {bound} for {run.TradingDate:yyyy-MM-dd} (no settle within the {settle} post-cancel settle window)";

        return _alerter.RaiseAsync(
            new IngestionAlert(
                JobCode: hungJob,
                ReasonCode: RunHungReasonCode,
                Subject: subject,
                BodyTr: settle is null
                    ? $"Planlı veri zinciri {run.TradingDate:yyyy-MM-dd} için {step.Index}/{step.Total}. adımda ('{hungJob}') {bound} sınırını aştı ve durduruldu ({attempt}. deneme); son bilinen iyi veri sunulmaya devam ediyor ve yeniden deneme merdiveni devreye girdi."
                    : $"Planlı veri zinciri {run.TradingDate:yyyy-MM-dd} için {step.Index}/{step.Total}. adımda ('{hungJob}') {bound} sınırını aştı ve durduruldu ({attempt}. deneme); iptal sonrası {settle} bekleme penceresinde de bitmedi, son bilinen iyi veri sunulmaya devam ediyor ve yeniden deneme merdiveni devreye girdi.",
                BodyEn: settle is null
                    ? $"The scheduled chain for {run.TradingDate:yyyy-MM-dd} exceeded the {bound} in step {step.Index}/{step.Total} ('{hungJob}') and was cancelled (attempt {attempt}); last-known-good data continues to be served and the retry ladder has taken over."
                    : $"The scheduled chain for {run.TradingDate:yyyy-MM-dd} exceeded the {bound} in step {step.Index}/{step.Total} ('{hungJob}') and was cancelled (attempt {attempt}); it did not settle within the {settle} post-cancel settle window either; last-known-good data continues to be served and the retry ladder has taken over.",
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

    /// <summary>
    /// The durable record of a <b>holiday skip</b> (TKT-foundation-048, planner pin 3): one short
    /// run-ledger row per gated stream per configured holiday (per process lifetime: the marker is
    /// in memory, so a restart mid-holiday may write the row again), in the same shape as the prices
    /// gate's skip row (<c>gate</c>/<c>gateOutcome</c>) plus the stream name and the holiday date.
    /// <para>
    /// The job code is <see cref="HolidaySkipJobCode"/>, deliberately not a registered job: a
    /// green <c>succeeded</c> row under a real job's code would let a freshness query mistake
    /// "the market was closed" for "the job ran today". Writes nothing else — no attempt, no
    /// step bound, no alert — and a ledger-write failure is logged, never turned into a failed
    /// tick (the same posture as <see cref="RecordHungRunAsync"/>).
    /// </para>
    /// </summary>
    private async Task RecordHolidaySkipAsync(
        ScheduleStream stream,
        DateOnly holiday,
        CancellationToken cancellationToken)
    {
        var recordedAt = _clock.GetUtcNow();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DegerliDbContext>();

            db.IngestRuns.Add(new IngestRun
            {
                JobCode = HolidaySkipJobCode,
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
                    gate = "trading-calendar",
                    gateOutcome = "skipped",
                    stream = stream.Name,
                    holiday = holiday.ToString("yyyy-MM-dd"),
                    tradingDaysOnly = true,
                }, StatsJsonOptions),
            });

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Stream {Stream} skipped its {Date} occurrence(s): {Date} is a configured market holiday (trading-calendar gate, {JobCode} row recorded).",
                stream.Name,
                holiday,
                holiday,
                HolidaySkipJobCode);
        }
        catch (Exception exception)
        {
            // The row did not land: forget the dedupe marker so the next occurrence of this
            // holiday retries the write instead of leaving the holiday unexplained forever
            // (TKT-foundation-048: never a silent no-run, even under a transient store failure).
            lock (_gate)
            {
                if (stream.HolidaySkipRecordedFor == holiday)
                {
                    stream.HolidaySkipRecordedFor = null;
                }
            }

            _logger.LogError(
                exception,
                "Could not write the {ReasonCode} run-ledger row for stream {Stream} on {Date}.",
                HolidaySkipJobCode,
                stream.Name,
                holiday);
        }
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
        /// <summary>
        /// The stream's operator-facing name, recorded in a holiday-skip ledger row
        /// (<c>eod-chain</c>, <c>morning-chain</c> or <c>job:&lt;code&gt;</c>) so the row says
        /// <b>which</b> stream was gated, not just that something was (TKT-foundation-048).
        /// </summary>
        public required string Name { get; init; }

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

        /// <summary>
        /// The holiday date this stream has already explained in the run ledger
        /// (TKT-foundation-048): a gated stream writes <b>one</b> holiday-skip row per holiday,
        /// however many occurrences its cron has inside that day, and starts explaining again
        /// on the next holiday. The marker lives in memory only — a restart mid-holiday
        /// re-seeds from the next occurrence and may write the same holiday's row again.
        /// </summary>
        public DateOnly? HolidaySkipRecordedFor { get; set; }

        /// <summary>
        /// Whether this stream's attempt is running inside a tick (01 v1.5 §10.9.17,
        /// TKT-foundation-062). An in-flight stream is neither a wake candidate nor a
        /// due candidate: its attempt owns it until it ends, and the pump keeps the
        /// <b>other</b> streams' due instants honest meanwhile.
        /// </summary>
        public bool InFlight { get; set; }
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

        /// <summary>
        /// An immutable copy of the progress at one instant. The watchdog snapshots because the
        /// abandoned chain may still be advancing this live object while the run-ledger row and
        /// the alert are written.
        /// </summary>
        public ChainStepSnapshot Snapshot() =>
            new(_currentJobCode ?? "scheduler-chain", Index, Total);
    }

    /// <summary>The chain step the watchdog caught, captured before any await (see
    /// <see cref="ChainStepProgress.Snapshot"/>).</summary>
    private readonly record struct ChainStepSnapshot(string JobCode, int Index, int Total);

    /// <summary>
    /// One arm of the per-step bound (TKT-foundation-064): the identity of the step the timer was
    /// armed for, plus whether that step has already ended. The timer callback matches the live
    /// chain-wide cursor against this arm rather than trusting the cursor alone — the cursor is
    /// <c>Begin</c>-ed for the next step the moment this one returns, so a late callback reading
    /// it would name a step that was never over its bound.
    /// </summary>
    private sealed class StepBoundArm
    {
        private int _retired;

        public StepBoundArm(string? jobCode, int index, int total) =>
            Snapshot = new ChainStepSnapshot(jobCode ?? "scheduler-chain", index, total);

        /// <summary>The step this arm names — immutable, so necessarily a pre-cancel snapshot.</summary>
        public ChainStepSnapshot Snapshot { get; }

        /// <summary>Set once the step this arm was armed for has ended, one way or another.</summary>
        public bool Retired => Volatile.Read(ref _retired) != 0;

        public void Retire() => Interlocked.Exchange(ref _retired, 1);

        /// <summary>Whether the live cursor still points at the step this arm was armed for.</summary>
        public bool Matches(ChainStepProgress step) =>
            step.Index == Snapshot.Index
            && step.Total == Snapshot.Total
            && string.Equals(step.CurrentJobCode, Snapshot.JobCode, StringComparison.Ordinal);
    }

    /// <summary>
    /// Which of the two watchdog bounds fired, and the step that was in flight when it did
    /// (01 v1.5.43 §10.9.17, TKT-foundation-064). <see cref="Bound"/> is
    /// <c>"step"</c> or <c>"chain"</c>; the attempt-level watchdog turns it into exactly one
    /// <c>RUN_HUNG</c> row and alert, so an operator can tell the two classes apart from the
    /// ledger alone.
    /// </summary>
    private readonly record struct ChainBoundExpiry(string Bound, ChainStepSnapshot Step);
}
