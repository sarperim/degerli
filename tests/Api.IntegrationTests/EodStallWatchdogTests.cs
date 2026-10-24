using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Api.IntegrationTests.Harness.Mail;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Scheduling;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Serilog;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-foundation-041 acceptance — the <b>silent-stall class</b> of the C3c scheduler
/// (01 v1.5 §10.9.17, TC-MDF-079).
/// <para>
/// The production loop awaits one <see cref="IngestionScheduler.TickAsync"/> to completion and
/// that tick runs a chain stream's jobs sequentially, so a chain step whose await never
/// completes holds the <b>whole</b> scheduler: no further wakes, no retries on any stream, no
/// exhausted alert, process alive. That is the live signature this ticket was sliced on —
/// Tuesday 2026-10-20, the 20:30 chain's <c>prices</c> run completed at 17:37:03 UTC and the
/// fund retry already due at 17:30:00.026 UTC never fired, with no ledger row, no error and no
/// alert until the process was restarted at 20:15 UTC.
/// </para>
/// <para>
/// The reproduction is doubles-only and clock-deterministic: the chain's post-prices step is a
/// doubled job that never completes (or throws), the scheduler runs over a
/// <see cref="FakeTimeProvider"/>, and a second stream due <i>after</i> the stall instant proves
/// whether the loop is alive. Pre-fix the tick never returns; post-fix the hung-run watchdog
/// bounds the attempt, records an honest <c>RUN_HUNG</c> ledger row + alert naming the step, and
/// the stream recovers through the existing retry ladder.
/// </para>
/// </summary>
public sealed class EodStallWatchdogTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);

    /// <summary>The route the prices adapter reads (the one real job the chain still runs).</summary>
    private const string PricesPath = "/prices/eod";

    /// <summary>The healthy step preceding the slow/hung one (TC-MDF-098).</summary>
    private const string PreludeStepCode = "prelude-step";

    /// <summary>The slow-but-healthy step (TC-MDF-098(a)/(c)).</summary>
    private const string SlowStepCode = "slow-step";

    /// <summary>A liveness bound for "the tick never returns" — real time, not the fake clock.</summary>
    private static readonly TimeSpan LivenessBound = TimeSpan.FromSeconds(30);

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public EodStallWatchdogTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    /// <summary>
    /// TC-MDF-079 — a chain step that never completes does not stall the scheduler silently:
    /// the watchdog bounds the attempt (honest ledger row + <c>RUN_HUNG</c> alert naming the
    /// step), the next due wake on another stream still fires, and the retry ladder recovers
    /// the stream.
    /// </summary>
    [Fact]
    public async Task TC_MDF_079_Hung_chain_step_is_bounded_by_the_watchdog_and_the_loop_recovers()
    {
        await ResetAsync();
        _wireMock.Server.ResetMappings();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesOkTminus1, path: PricesPath);

        var stall = new StallStep(StallMode.HangOnFirstAttempt);
        var probe = new ProbeStep();
        await using var host = BuildScheduler(stall, probe, maxRunDuration: "00:10:00");
        var clock = host.Clock;
        clock.SetUtcNow(Trt(2026, 10, 5, 20, 29));

        // 20:30 — the EOD chain starts: prices succeeds, then the doubled post-prices step
        // hangs. This is the live signature's exact shape (a completed step, then silence).
        clock.SetUtcNow(Trt(2026, 10, 5, 20, 30));
        var tick = host.Scheduler.TickAsync(Trt(2026, 10, 5, 20, 30));
        await stall.Entered.Task.WaitAsync(LivenessBound);

        var afterPrices = await LedgerAsync();
        Assert.Contains(afterPrices, run => run.JobCode == "prices" && run.Status == IngestResult.Succeeded);

        // The step has hung. Under the pre-fix code the tick never returns — no watchdog, no
        // ledger row, no alert, and no other stream ever runs again. Advancing the clock past
        // the configured bound must not be required for anything else to be true.
        clock.Advance(TimeSpan.FromMinutes(11));

        var finished = await Task.WhenAny(tick, Task.Delay(LivenessBound));
        Assert.Same(tick, finished);
        await tick;

        // (1) An honest ledger row for the hung attempt: failed, naming the step, RUN_HUNG.
        var runs = await LedgerAsync();
        var hung = Assert.Single(runs, run => ReasonCode(run) == "RUN_HUNG");
        Assert.Equal(StallStep.Code, hung.JobCode);
        Assert.Equal(IngestResult.Failed, hung.Status);
        Assert.Equal(StallStep.Code, StatString(hung, "hungJob"));
        Assert.Equal(1, Stat(hung, "attempt"));
        Assert.Equal(600, Stat(hung, "maxRunDurationSeconds"));
        Assert.NotNull(hung.StartedAt);
        Assert.NotNull(hung.FinishedAt);

        // (2) The alert machinery engaged with the named reason code.
        var alert = Assert.Single(host.Mail.Sent, mail => mail.Subject.Contains("RUN_HUNG"));
        Assert.Contains(StallStep.Code, alert.Subject);

        // (3) Per-job chain-step progress is observable (the missing telemetry in the live
        //     incident): each step announces itself before it is awaited.
        var chainStepLog = host.Logs.Events
            .Select(logEvent => logEvent.RenderMessage())
            .Where(message => message.Contains("Chain step"))
            .ToList();
        Assert.True(
            chainStepLog.Any(message => message.Contains("Chain step 2/2") && message.Contains(StallStep.Code)),
            "Chain-step progress log missing. Seen: " + string.Join(" | ", chainStepLog));

        // (4) The loop is alive: the second stream due at 20:42 (after the stall instant)
        //     still fires. Pre-fix nothing after prices ever ran again.
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 5, 20, 42));
        Assert.Contains(await LedgerAsync(), run => run.JobCode == ProbeStep.Code);

        // (4b) The retry is measured from the instant the hung attempt ENDED (20:41 + 5 min =
        //     20:46), not from the instant it was due (20:30 + 5 min = 20:35). This assertion
        //     pins that rule rather than relying on the harness: a start-relative retry was
        //     already due at 20:35 — before the 20:42 advance above — so it would have run by
        //     now. One minute before the end-relative due instant the stream must still be
        //     waiting: no retry has run.
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 5, 20, 45));
        Assert.DoesNotContain(
            await LedgerAsync(),
            run => run.JobCode == StallStep.Code && run.Status == IngestResult.Succeeded);

        // (5) The stream recovers through the retry ladder: the 5-minute retry (measured from
        //     the instant the hung attempt ended — 20:41 — so 20:46) re-runs the chain and the
        //     now-healthy step completes the run.
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 5, 20, 46));
        var recovered = await LedgerAsync();
        var recoveredStep = Assert.Single(
            recovered,
            run => run.JobCode == StallStep.Code && run.Status == IngestResult.Succeeded);
        Assert.Equal(2, Stat(recoveredStep, "attempt"));
        Assert.Equal(1, Stat(recoveredStep, "retries"));
        Assert.Equal(1, recovered.Count(run => ReasonCode(run) == "RUN_HUNG"));
    }

    /// <summary>
    /// TC-MDF-079 (supporting) — a chain step that throws between steps is not swallowed: the
    /// run-ledger row is written failed and the existing 5-minute retry ladder engages.
    /// </summary>
    [Fact]
    public async Task TC_MDF_079_Throwing_chain_step_enters_the_retry_ladder()
    {
        await ResetAsync();
        _wireMock.Server.ResetMappings();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesOkTminus1, path: PricesPath);

        var stall = new StallStep(StallMode.ThrowOnFirstAttempt);
        var probe = new ProbeStep();
        await using var host = BuildScheduler(stall, probe, maxRunDuration: "00:10:00");
        var clock = host.Clock;
        clock.SetUtcNow(Trt(2026, 10, 5, 20, 29));

        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 5, 20, 30));

        var afterFailure = await LedgerAsync();
        Assert.Contains(
            afterFailure,
            run => run.JobCode == StallStep.Code && run.Status == IngestResult.Failed && ReasonCode(run) is null);

        // The retry ladder — not a silent swallow and not a hot loop.
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 5, 20, 35));
        var recovered = await LedgerAsync();
        var retried = Assert.Single(
            recovered,
            run => run.JobCode == StallStep.Code && run.Status == IngestResult.Succeeded);
        Assert.Equal(2, Stat(retried, "attempt"));
        Assert.Equal(1, Stat(retried, "retries"));
        Assert.DoesNotContain(recovered, run => ReasonCode(run) == "RUN_HUNG");
    }

    /// <summary>
    /// TC-MDF-079 (supporting) — the premise the watchdog rests on: a chain step whose await is
    /// <b>not</b> interruptible. The doubled hang here ignores the attempt's cancellation, so the
    /// coordinator must abandon it rather than wait for it: the tick still returns within the
    /// bound, the <c>RUN_HUNG</c> row and alert are written once, the loop keeps waking and the
    /// retry recovers the stream — while the abandoned task is still pending and has written
    /// nothing. (The main case's double is cooperative, so it cannot prove this.)
    /// </summary>
    [Fact]
    public async Task TC_MDF_079_Uninterruptible_hang_is_abandoned_and_still_bounded()
    {
        await ResetAsync();
        _wireMock.Server.ResetMappings();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesOkTminus1, path: PricesPath);

        var stall = new StallStep(StallMode.HangWithoutToken);
        var probe = new ProbeStep();
        await using var host = BuildScheduler(stall, probe, maxRunDuration: "00:10:00");
        var clock = host.Clock;
        clock.SetUtcNow(Trt(2026, 10, 5, 20, 29));

        clock.SetUtcNow(Trt(2026, 10, 5, 20, 30));
        var tick = host.Scheduler.TickAsync(Trt(2026, 10, 5, 20, 30));
        await stall.Entered.Task.WaitAsync(LivenessBound);

        clock.Advance(TimeSpan.FromMinutes(11));
        var finished = await Task.WhenAny(tick, Task.Delay(LivenessBound));
        Assert.Same(tick, finished);
        await tick;

        var hung = Assert.Single(await LedgerAsync(), run => ReasonCode(run) == "RUN_HUNG");
        Assert.Equal(StallStep.Code, hung.JobCode);
        Assert.Single(host.Mail.Sent, mail => mail.Subject.Contains("RUN_HUNG"));

        // The watchdog did not wait for the step: the first call is still suspended.
        Assert.False(stall.FirstAttemptReturned, "the uninterruptible hang must still be pending");

        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 5, 20, 46));
        var recovered = await LedgerAsync();
        var recoveredStep = Assert.Single(
            recovered,
            run => run.JobCode == StallStep.Code && run.Status == IngestResult.Succeeded);
        Assert.Equal(2, Stat(recoveredStep, "attempt"));
        Assert.Equal(1, recovered.Count(run => ReasonCode(run) == "RUN_HUNG"));
    }

    /// <summary>
    /// TC-MDF-079 (supporting) — a step that cancels itself (an <see cref="OperationCanceledException"/>
    /// that is <b>not</b> the host's stopping token — a job-level timeout, a source's own CTS)
    /// is a failure of the attempt: it enters the retry ladder instead of escaping the tick and
    /// leaving the stream to re-fire in a hot loop.
    /// <para>
    /// This pins the ladder and the scheduler's attempt-level honesty only — <b>not</b> a
    /// per-job ledger row: the job runner's cancellation path writes none
    /// (<c>src/Ingestion/Jobs/Jobs.cs</c>, the known fold-forward to TKT-foundation-044).
    /// </para>
    /// </summary>
    [Fact]
    public async Task TC_MDF_079_Unexpected_cancellation_enters_the_retry_ladder()
    {
        await ResetAsync();
        _wireMock.Server.ResetMappings();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesOkTminus1, path: PricesPath);

        var stall = new StallStep(StallMode.CancelOnFirstAttempt);
        var probe = new ProbeStep();
        await using var host = BuildScheduler(stall, probe, maxRunDuration: "00:10:00");
        var clock = host.Clock;
        clock.SetUtcNow(Trt(2026, 10, 5, 20, 29));

        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 5, 20, 30));

        // The attempt failed with the ladder's own log line (no escaping cancellation).
        Assert.Contains(
            host.Logs.Events,
            logEvent => logEvent.RenderMessage().Contains("failed on attempt 1"));

        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 5, 20, 35));
        var recovered = await LedgerAsync();
        var retried = Assert.Single(
            recovered,
            run => run.JobCode == StallStep.Code && run.Status == IngestResult.Succeeded);
        Assert.Equal(2, Stat(retried, "attempt"));
    }

    // ---------------------------------------------------------------------------------
    // TKT-foundation-064 — the watchdog's two bounds: per-step (120 min shipped) and the
    // whole-chain ceiling (4 h shipped). Doubles only, fake clock, no real delays.
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// TC-MDF-098 (constant case) — the shipped defaults are the design: a chain step is
    /// bounded at <b>120 minutes</b> on its own and the attempt as a whole at <b>4 hours</b>
    /// (01 v1.5.43 §10.9.17). Both are configuration-overridable independently; the per-step
    /// key binds through the same options section the scheduler already reads.
    /// </summary>
    [Fact]
    public void TC_MDF_098_Shipped_bounds_are_a_120_minute_step_and_a_4_hour_chain()
    {
        var shipped = new IngestionSchedulerOptions();
        Assert.Equal(TimeSpan.FromMinutes(120), shipped.MaxStepDuration);
        Assert.Equal(TimeSpan.FromHours(4), shipped.MaxRunDuration);

        // The per-step bound is configuration, not a hard-coded literal: the options section
        // binds it the same way MaxRunDuration binds.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ingestion:Scheduler:MaxStepDuration"] = "00:05:00",
                ["Ingestion:Scheduler:MaxRunDuration"] = "00:20:00",
            })
            .Build();
        var services = new ServiceCollection();
        services.Configure<IngestionSchedulerOptions>(
            configuration.GetSection(IngestionSchedulerOptions.SectionName));
        var bound = services.BuildServiceProvider()
            .GetRequiredService<IOptions<IngestionSchedulerOptions>>()
            .Value;

        Assert.Equal(TimeSpan.FromMinutes(5), bound.MaxStepDuration);
        Assert.Equal(TimeSpan.FromMinutes(20), bound.MaxRunDuration);

        // An unconfigured bound keeps its code default (the shipped-config posture: the API's
        // appsettings.json carries neither key).
        var unconfigured = new ServiceCollection()
            .Configure<IngestionSchedulerOptions>(
                new ConfigurationBuilder().Build().GetSection(IngestionSchedulerOptions.SectionName))
            .BuildServiceProvider()
            .GetRequiredService<IOptions<IngestionSchedulerOptions>>()
            .Value;
        Assert.Equal(TimeSpan.FromMinutes(120), unconfigured.MaxStepDuration);
        Assert.Equal(TimeSpan.FromHours(4), unconfigured.MaxRunDuration);
    }

    /// <summary>
    /// TC-MDF-098(a) — <b>a slow but healthy chain is not hung</b>. A chain whose first step
    /// consumes 40 minutes and whose current step consumes 60 minutes (~100 minutes total —
    /// the measured shape of a healthy paced dev-stack chain) completes under the shipped
    /// bounds, with one succeeded ledger row per step and <b>zero</b> <c>RUN_HUNG</c>.
    /// <para>
    /// Red before this ticket: the bound is the <b>whole attempt</b> at 90 minutes, so it
    /// expires inside the healthy 60-minute step and a perfectly healthy chain can never
    /// complete.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TC_MDF_098_Slow_but_healthy_chain_completes_under_the_shipped_bounds()
    {
        await ResetAsync();

        var clock = NewClock();
        var prelude = new PacedStep(PreludeStepCode, clock, TimeSpan.FromMinutes(40));
        var slow = new PacedStep(SlowStepCode, clock, TimeSpan.FromMinutes(60));

        // No bound overrides: the shipped defaults (120 min step / 4 h chain) are what a
        // host that configures nothing runs.
        await using var host = BuildChainHost(clock, [prelude, slow]);
        clock.SetUtcNow(Trt(2026, 10, 5, 20, 29));

        clock.SetUtcNow(Trt(2026, 10, 5, 20, 30));
        var tick = host.Scheduler.TickAsync(Trt(2026, 10, 5, 20, 30));
        await prelude.Entered.Task.WaitAsync(LivenessBound);

        // 20:30 → 21:10: the first step's 40 minutes.
        clock.Advance(TimeSpan.FromMinutes(40));
        await slow.Entered.Task.WaitAsync(LivenessBound);

        // 21:10 → 22:10: the slow step's 60 minutes, still short of its own 120-minute bound.
        clock.Advance(TimeSpan.FromMinutes(60));
        await AwaitTickAsync(tick);

        var runs = await LedgerAsync();
        Assert.DoesNotContain(runs, run => ReasonCode(run) == "RUN_HUNG");
        Assert.DoesNotContain(runs, run => run.Status == IngestResult.Failed);
        Assert.Empty(host.Mail.Sent);

        var preludeRow = Assert.Single(runs, run => run.JobCode == PreludeStepCode);
        Assert.Equal(IngestResult.Succeeded, preludeRow.Status);
        Assert.Equal(Trt(2026, 10, 5, 20, 30), preludeRow.StartedAt!.Value);
        Assert.Equal(Trt(2026, 10, 5, 21, 10), preludeRow.FinishedAt!.Value);

        var slowRow = Assert.Single(runs, run => run.JobCode == SlowStepCode);
        Assert.Equal(IngestResult.Succeeded, slowRow.Status);
        Assert.Equal(Trt(2026, 10, 5, 21, 10), slowRow.StartedAt!.Value);
        Assert.Equal(Trt(2026, 10, 5, 22, 10), slowRow.FinishedAt!.Value);

        // The step's own duration is asserted against the step bound: 60 minutes of healthy
        // pacing, comfortably inside the shipped 120-minute per-step bound (and the healthy
        // chain's ~100 minutes, which the old 90-minute whole-attempt bound could not fit).
        var slowDuration = slowRow.FinishedAt.Value - slowRow.StartedAt.Value;
        Assert.Equal(TimeSpan.FromMinutes(60), slowDuration);
        Assert.True(
            slowDuration < new IngestionSchedulerOptions().MaxStepDuration,
            $"a healthy {slowDuration} step must sit inside the {new IngestionSchedulerOptions().MaxStepDuration} step bound");

        // Exactly the chain's two steps: no retry, no duplicate row.
        Assert.Equal(2, runs.Count);
    }

    /// <summary>
    /// TC-MDF-098(b) — <b>a hung step is cancelled and named with the bound that caught it</b>.
    /// A doubled step that never completes is bounded at its own <b>step</b> bound (10 minutes
    /// here, the chain ceiling left at the shipped 4 hours): exactly one <c>RUN_HUNG</c> row
    /// names that step with <c>bound: "step"</c> and <c>maxStepDurationSeconds</c>, one alert
    /// names it, the retry ladder starts from the instant the attempt ended, and the next
    /// attempt's healthy step lands <c>succeeded</c> with no second <c>RUN_HUNG</c>.
    /// </summary>
    [Fact]
    public async Task TC_MDF_098_Hung_step_is_cancelled_and_named_with_the_step_bound()
    {
        await ResetAsync();

        var clock = NewClock();
        var prelude = new InstantStep(PreludeStepCode);
        var stall = new StallStep(StallMode.HangOnFirstAttempt);
        await using var host = BuildChainHost(
            clock,
            [prelude, stall],
            maxStepDuration: "00:10:00");
        clock.SetUtcNow(Trt(2026, 10, 5, 20, 29));

        clock.SetUtcNow(Trt(2026, 10, 5, 20, 30));
        var tick = host.Scheduler.TickAsync(Trt(2026, 10, 5, 20, 30));
        await stall.Entered.Task.WaitAsync(LivenessBound);

        // 20:40 — the hung step's own 10-minute bound expires (the 4-hour chain ceiling is not
        // what caught it).
        clock.Advance(TimeSpan.FromMinutes(10));
        await AwaitTickAsync(tick);

        var runs = await LedgerAsync();
        var hung = Assert.Single(runs, run => ReasonCode(run) == "RUN_HUNG");
        Assert.Equal(StallStep.Code, hung.JobCode);
        Assert.Equal(IngestResult.Failed, hung.Status);
        Assert.Equal(StallStep.Code, StatString(hung, "hungJob"));
        Assert.Equal(2, Stat(hung, "step"));
        Assert.Equal(2, Stat(hung, "stepCount"));

        // The ledger alone tells an operator which bound fired — step, not chain — and
        // records both configured bounds additively.
        Assert.Equal("step", StatString(hung, "bound"));
        Assert.Equal(600, Stat(hung, "maxStepDurationSeconds"));
        Assert.Equal((int)TimeSpan.FromHours(4).TotalSeconds, Stat(hung, "maxRunDurationSeconds"));
        Assert.Equal(1, Stat(hung, "attempt"));
        Assert.Equal(Trt(2026, 10, 5, 20, 30), hung.StartedAt!.Value);
        Assert.Equal(Trt(2026, 10, 5, 20, 40), hung.FinishedAt!.Value);

        var alert = Assert.Single(host.Mail.Sent, mail => mail.Subject.Contains("RUN_HUNG"));
        Assert.Contains(StallStep.Code, alert.Subject);
        Assert.Contains("per-step", alert.Subject);

        // The retry is measured from the attempt's end (20:40 + 5 min = 20:45): one minute
        // earlier the stream must still be waiting.
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 5, 20, 44));
        Assert.DoesNotContain(
            await LedgerAsync(),
            run => run.JobCode == StallStep.Code && run.Status == IngestResult.Succeeded);

        // The next attempt's now-healthy step completes, and the event produced exactly one
        // RUN_HUNG row in total.
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 5, 20, 45));
        var recovered = await LedgerAsync();
        var recoveredStep = Assert.Single(
            recovered,
            run => run.JobCode == StallStep.Code && run.Status == IngestResult.Succeeded);
        Assert.Equal(2, Stat(recoveredStep, "attempt"));
        Assert.Equal(1, Stat(recoveredStep, "retries"));
        Assert.Equal(1, recovered.Count(run => ReasonCode(run) == "RUN_HUNG"));
        Assert.Single(host.Mail.Sent, mail => mail.Subject.Contains("RUN_HUNG"));
    }

    /// <summary>
    /// TC-MDF-098(c) — <b>the whole-chain ceiling still bites</b>, and is distinguishable from
    /// the step bound. Individually-healthy steps whose total exceeds a small configured
    /// <c>MaxRunDuration</c> produce exactly one <c>RUN_HUNG</c> naming the in-flight step
    /// with <c>bound: "chain"</c> and <c>maxRunDurationSeconds</c> recorded — no step is hung,
    /// the ceiling is what fired.
    /// </summary>
    [Fact]
    public async Task TC_MDF_098_Chain_ceiling_still_bites_and_names_the_chain_bound()
    {
        await ResetAsync();

        var clock = NewClock();
        var quick = new InstantStep(PreludeStepCode);
        // Healthy and well inside its own step bound; only the chain ceiling can catch it.
        var slow = new PacedStep(SlowStepCode, clock, TimeSpan.FromMinutes(15));
        await using var host = BuildChainHost(
            clock,
            [quick, slow],
            maxRunDuration: "00:10:00",
            maxStepDuration: "02:00:00");
        clock.SetUtcNow(Trt(2026, 10, 5, 20, 29));

        clock.SetUtcNow(Trt(2026, 10, 5, 20, 30));
        var tick = host.Scheduler.TickAsync(Trt(2026, 10, 5, 20, 30));
        await slow.Entered.Task.WaitAsync(LivenessBound);

        // 20:40 — the chain ceiling expires mid-step; the step's own bound (2 h) does not.
        clock.Advance(TimeSpan.FromMinutes(11));
        await AwaitTickAsync(tick);

        var runs = await LedgerAsync();
        var hung = Assert.Single(runs, run => ReasonCode(run) == "RUN_HUNG");
        Assert.Equal(SlowStepCode, hung.JobCode);
        Assert.Equal(IngestResult.Failed, hung.Status);
        Assert.Equal(SlowStepCode, StatString(hung, "hungJob"));
        Assert.Equal(2, Stat(hung, "step"));

        // Distinguishable from (b) from the ledger alone.
        Assert.Equal("chain", StatString(hung, "bound"));
        Assert.Equal(600, Stat(hung, "maxRunDurationSeconds"));
        Assert.Equal((int)TimeSpan.FromHours(2).TotalSeconds, Stat(hung, "maxStepDurationSeconds"));

        var alert = Assert.Single(host.Mail.Sent, mail => mail.Subject.Contains("RUN_HUNG"));
        Assert.Contains(SlowStepCode, alert.Subject);
        Assert.Contains("whole-chain", alert.Subject);

        // The healthy first step's own row is untouched; the ceiling added exactly the one
        // RUN_HUNG row, never a second.
        Assert.Single(runs, run => run.JobCode == PreludeStepCode && run.Status == IngestResult.Succeeded);
        Assert.Equal(1, runs.Count(run => ReasonCode(run) == "RUN_HUNG"));
    }

    /// <summary>
    /// TC-MDF-098 (supporting) — a <b>mis-typed per-step bound</b> (60 days, beyond the timer's
    /// ~49.7-day range) is an ordinary failed attempt on the 5/15/60 ladder — the 059 DR-1 path —
    /// never an escape that leaves the attempt overdue and refires it in a hot loop, and never a
    /// <c>RUN_HUNG</c> row.
    /// </summary>
    [Fact]
    public async Task TC_MDF_098_Mistyped_step_bound_is_an_ordinary_failed_attempt_never_a_hot_refire()
    {
        await ResetAsync();

        var clock = NewClock();
        await using var host = BuildChainHost(
            clock,
            [new InstantStep(PreludeStepCode)],
            maxStepDuration: "60.00:00:00");
        clock.SetUtcNow(Trt(2026, 10, 5, 20, 29));

        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 5, 21, 55));

        // One attempt per rung of the ladder (20:30, 20:35, 20:50, 21:50), then the exhausted
        // alert — bounded accounting, not a loop.
        Assert.Equal(4, host.Logs.Events.Count(logEvent => logEvent.RenderMessage().Contains("failed on attempt")));
        var exhausted = Assert.Single(host.Mail.Sent);
        Assert.Contains("failed after 4 attempts", exhausted.Subject);
        Assert.Empty(await LedgerAsync());

        // Exhausted, not overdue: sweeping the rest of the day adds no attempt.
        clock.SetUtcNow(Trt(2026, 10, 5, 23, 59));
        Assert.Equal(4, host.Logs.Events.Count(logEvent => logEvent.RenderMessage().Contains("failed on attempt")));
        Assert.Empty(await LedgerAsync());
    }

    // ---------------------------------------------------------------------------------
    // Doubles
    // ---------------------------------------------------------------------------------

    /// <summary>How the doubled post-prices chain step misbehaves on its first attempt.</summary>
    private enum StallMode
    {
        /// <summary>The await never completes (the live signature's class).</summary>
        HangOnFirstAttempt,

        /// <summary>The await never completes <b>and</b> ignores the attempt's cancellation —
        /// the uninterruptible premise behind the watchdog (the attempt must be abandoned).</summary>
        HangWithoutToken,

        /// <summary>A plain exception between steps.</summary>
        ThrowOnFirstAttempt,

        /// <summary>A cancellation that is not the host's stopping token.</summary>
        CancelOnFirstAttempt,
    }

    /// <summary>
    /// The doubled chain step that reproduces the stall: on its first attempt it hangs (or
    /// throws / cancels); afterwards it succeeds, so the retry ladder can recover the stream.
    /// </summary>
    private sealed class StallStep : IIngestJob
    {
        public const string Code = "stall-step";

        private readonly StallMode _mode;
        private int _calls;

        public StallStep(StallMode mode) => _mode = mode;

        /// <summary>Completes when the doubled step has entered its pathological branch.</summary>
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>True only if the first (pathological) call ever ran to completion.</summary>
        public bool FirstAttemptReturned { get; private set; }

        public string JobCode => Code;

        public async Task<IngestResult> RunAsync(IngestionRequest request, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                Entered.TrySetResult();
                switch (_mode)
                {
                    case StallMode.HangOnFirstAttempt:
                        // Never completes on its own: only the watchdog's cancellation ends it.
                        await Task.Delay(Timeout.Infinite, cancellationToken);
                        break;
                    case StallMode.HangWithoutToken:
                        // Never completes and never observes cancellation: the watchdog's cancel
                        // cannot end it, so the attempt has to be abandoned, not awaited.
                        await Task.Delay(Timeout.Infinite);
                        break;
                    case StallMode.ThrowOnFirstAttempt:
                        throw new InvalidOperationException("stall-step: source step failed (doubled).");
                    case StallMode.CancelOnFirstAttempt:
                        throw new OperationCanceledException("stall-step: job-level timeout (doubled).");
                }

                FirstAttemptReturned = true;
            }

            return new IngestResult(Code, 1, 0, 0, IngestResult.Succeeded);
        }
    }

    /// <summary>
    /// A healthy chain step that consumes a <b>fake-clock</b> duration — the healthy-paced but
    /// slow chain of TC-MDF-098(a)/(c). No real delay: it completes when the test advances the
    /// clock, exactly as the production pacer's own delay would.
    /// </summary>
    private sealed class PacedStep : IIngestJob
    {
        private readonly FakeTimeProvider _clock;
        private readonly TimeSpan _duration;

        public PacedStep(string code, FakeTimeProvider clock, TimeSpan duration)
        {
            JobCode = code;
            _clock = clock;
            _duration = duration;
        }

        public string JobCode { get; }

        /// <summary>Completes when the doubled step has begun its paced wait.</summary>
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IngestResult> RunAsync(IngestionRequest request, CancellationToken cancellationToken = default)
        {
            // Registered before the signal so the test's clock advance always finds the timer.
            var paced = Task.Delay(_duration, _clock, cancellationToken);
            Entered.TrySetResult();
            await paced;
            return new IngestResult(JobCode, 1, 0, 0, IngestResult.Succeeded);
        }
    }

    /// <summary>An instantaneous healthy chain step (no pacing, no source call).</summary>
    private sealed class InstantStep : IIngestJob
    {
        public InstantStep(string code) => JobCode = code;

        public string JobCode { get; }

        public Task<IngestResult> RunAsync(IngestionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new IngestResult(JobCode, 1, 0, 0, IngestResult.Succeeded));
    }

    /// <summary>A healthy per-job stream target: proof that the loop keeps waking.</summary>
    private sealed class ProbeStep : IIngestJob
    {
        public const string Code = "probe";

        public string JobCode => Code;

        public Task<IngestResult> RunAsync(IngestionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new IngestResult(Code, 1, 0, 0, IngestResult.Succeeded));
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Drives the same wake-then-tick loop the hosted service runs, deterministically over the
    /// fake clock (test strategy §7).
    /// </summary>
    private static async Task AdvanceToAsync(IngestionScheduler scheduler, FakeTimeProvider clock, DateTimeOffset target)
    {
        while (true)
        {
            var now = clock.GetUtcNow();
            var next = scheduler.NextWake(now);
            if (next is null || next.Value > target)
            {
                break;
            }

            var instant = next.Value.ToUniversalTime();
            clock.SetUtcNow(instant);

            var tick = scheduler.TickAsync(instant);
            var finished = await Task.WhenAny(tick, Task.Delay(LivenessBound));
            Assert.Same(tick, finished);
            await tick;
        }

        clock.SetUtcNow(target.ToUniversalTime());
    }

    /// <summary>A fake clock anchored at the harness's fixed instant (no real delays).</summary>
    private static FakeTimeProvider NewClock() =>
        new(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

    /// <summary>Completes the tick within the liveness bound, never hanging the suite.</summary>
    private static async Task AwaitTickAsync(Task tick)
    {
        var finished = await Task.WhenAny(tick, Task.Delay(LivenessBound));
        Assert.Same(tick, finished);
        await tick;
    }

    /// <summary>
    /// The TC-MDF-098 harness (01 v1.5.43 §10.9.17): the same real scheduler engine and fake
    /// clock, with a chain composed entirely of doubles and the <b>production</b> job runner,
    /// so each step's own ledger row carries its real duration. A null bound leaves the shipped
    /// code default in effect — which is what the slow-but-healthy case needs.
    /// </summary>
    private IngestionSchedulerHost BuildChainHost(
        FakeTimeProvider clock,
        IReadOnlyList<IIngestJob> chain,
        string? maxRunDuration = null,
        string? maxStepDuration = null)
    {
        var mail = new RecordingMailDispatcher();
        var logs = new InMemorySerilogSink();

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _postgres.ConnectionString,
            ["Ingestion:Prices:BaseUrl"] = _wireMock.BaseUrl,
            ["Ingestion:Prices:Path"] = PricesPath,
            ["Ingestion:Prices:UniverseIndex"] = "XU100",
            ["Ingestion:Prices:RequestDelayMs"] = "0",
            ["Ingestion:Scheduler:Cron"] = "0 30 20 * * *",
            ["Ingestion:Scheduler:TimeZone"] = "Europe/Istanbul",
        };
        for (var index = 0; index < chain.Count; index++)
        {
            settings[$"Ingestion:Scheduler:JobCodes:{index}"] = chain[index].JobCode;
        }

        if (maxRunDuration is not null)
        {
            settings["Ingestion:Scheduler:MaxRunDuration"] = maxRunDuration;
        }

        if (maxStepDuration is not null)
        {
            settings["Ingestion:Scheduler:MaxStepDuration"] = maxStepDuration;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var logger = new LoggerConfiguration().MinimumLevel.Information().WriteTo.Sink(logs).CreateLogger();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog(logger, dispose: false));
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IIngestionAlerter>(new HarnessMailAlerter(mail));
        services.AddMarketDataIngestion(configuration);
        foreach (var job in chain)
        {
            services.AddSingleton<IIngestJob>(job);
        }

        var provider = services.BuildServiceProvider();
        var scheduler = provider.GetRequiredService<IngestionScheduler>();
        return new IngestionSchedulerHost(provider, clock, scheduler, mail, logs);
    }

    private IngestionSchedulerHost BuildScheduler(StallStep stall, ProbeStep probe, string maxRunDuration)
    {
        var mail = new RecordingMailDispatcher();
        var logs = new InMemorySerilogSink();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _postgres.ConnectionString,
            ["Ingestion:Prices:BaseUrl"] = _wireMock.BaseUrl,
            ["Ingestion:Prices:Path"] = PricesPath,
            ["Ingestion:Prices:UniverseIndex"] = "XU100",
            ["Ingestion:Prices:RequestDelayMs"] = "0",
            ["Ingestion:Scheduler:Cron"] = "0 30 20 * * *",
            ["Ingestion:Scheduler:TimeZone"] = "Europe/Istanbul",
            // The chain the scheduler runs: the one real job, then the doubled stall step.
            ["Ingestion:Scheduler:JobCodes:0"] = "prices",
            ["Ingestion:Scheduler:JobCodes:1"] = StallStep.Code,
            // The independent stream whose wake proves the loop is still alive.
            ["Ingestion:Scheduler:JobSchedules:0:JobCode"] = ProbeStep.Code,
            ["Ingestion:Scheduler:JobSchedules:0:Cron"] = "0 42 20 * * *",
            ["Ingestion:Scheduler:MaxRunDuration"] = maxRunDuration,
        };

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var logger = new LoggerConfiguration().MinimumLevel.Information().WriteTo.Sink(logs).CreateLogger();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog(logger, dispose: false));
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IIngestionAlerter>(new HarnessMailAlerter(mail));
        services.AddMarketDataIngestion(configuration);
        services.AddSingleton<IIngestJob>(stall);
        services.AddSingleton<IIngestJob>(probe);

        var provider = services.BuildServiceProvider();
        var scheduler = provider.GetRequiredService<IngestionScheduler>();
        return new IngestionSchedulerHost(provider, clock, scheduler, mail, logs);
    }

    private async Task ResetAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM quarantined_facts");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM daily_prices");
    }

    private async Task<List<IngestRun>> LedgerAsync()
    {
        await using var db = _postgres.CreateContext();
        return await db.IngestRuns.OrderBy(r => r.StartedAt).ThenBy(r => r.Id).ToListAsync();
    }

    /// <summary>The run-ledger row's own reason code, or null for a job row (which has none).</summary>
    private static string? ReasonCode(IngestRun run) =>
        run.StatsJson is null ? null : StatOrNull(run, "reasonCode") as string;

    private static int Stat(IngestRun run, string name) =>
        (int)(StatOrNull(run, name) ?? throw new InvalidOperationException($"Ledger stats lack '{name}'."));

    private static string StatString(IngestRun run, string name) =>
        StatOrNull(run, name) as string ?? throw new InvalidOperationException($"Ledger stats lack '{name}'.");

    private static object? StatOrNull(IngestRun run, string name)
    {
        using var document = JsonDocument.Parse(run.StatsJson!);
        var root = document.RootElement;
        if (!root.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetInt32(),
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null => null,
            _ => value.GetRawText(),
        };
    }

    /// <summary>An instant expressed in Turkey time (Europe/Istanbul = UTC+3, no DST
    /// since 2016) as a UTC <see cref="DateTimeOffset"/> for the fake clock.</summary>
    private static DateTimeOffset Trt(int year, int month, int day, int hour, int minute) =>
        new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.FromHours(3)).ToUniversalTime();

    /// <summary>The active engine provider plus its clock and the mail/log doubles.</summary>
    private sealed class IngestionSchedulerHost : IAsyncDisposable
    {
        public IngestionSchedulerHost(
            ServiceProvider provider,
            FakeTimeProvider clock,
            IngestionScheduler scheduler,
            RecordingMailDispatcher mail,
            InMemorySerilogSink logs)
        {
            Provider = provider;
            Clock = clock;
            Scheduler = scheduler;
            Mail = mail;
            Logs = logs;
        }

        public ServiceProvider Provider { get; }

        public FakeTimeProvider Clock { get; }

        public IngestionScheduler Scheduler { get; }

        public RecordingMailDispatcher Mail { get; }

        public InMemorySerilogSink Logs { get; }

        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    /// <summary>Funnels the ingestion alert seam into the shared recording mail double.</summary>
    private sealed class HarnessMailAlerter : IIngestionAlerter
    {
        private readonly RecordingMailDispatcher _mail;

        public HarnessMailAlerter(RecordingMailDispatcher mail) => _mail = mail;

        public Task RaiseAsync(IngestionAlert alert, CancellationToken cancellationToken = default) =>
            _mail.SendAsync(
                new Degerli.Api.Mail.OutboundMail("builder@degerli.test", alert.Subject, alert.BodyTr, alert.BodyEn),
                cancellationToken);
    }
}
