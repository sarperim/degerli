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
/// stream, resolves its run date to the <b>previous trading day</b> (BIST prices are EOD, so a
/// 08:30 pass can only repair the last completed session) and runs the pinned
/// <see cref="MorningIngestChain"/> with the <b>stored-prices repair gate</b>: when every
/// current constituent of the prices bound already has a stored row for the run date, the
/// prices job is skipped (one ledger row records the gate outcome) and the chain continues —
/// a fully covered day is never re-fetched.
/// </para>
/// </remarks>
public sealed class IngestionScheduler
{
    private static readonly JsonSerializerOptions StatsJsonOptions = new(JsonSerializerDefaults.Web);

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

        var candidate = occurrenceDate.AddDays(-1);
        for (var back = 1; back < PreviousTradingDayLookbackDays; back++)
        {
            if (_calendar.IsTradingDay(candidate))
            {
                return candidate;
            }

            candidate = candidate.AddDays(-1);
        }

        _logger.LogWarning(
            "No trading day found in the {Days} days before {Date}; the morning run targets {Candidate}.",
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
                var gateSkipped = await SkipPricesForCoveredDayAsync(
                        stream,
                        scope.ServiceProvider,
                        jobCode,
                        request.Date,
                        attempt,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (gateSkipped)
                {
                    continue;
                }

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
    }
}
