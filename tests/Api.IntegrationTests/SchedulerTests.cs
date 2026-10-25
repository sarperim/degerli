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
using Microsoft.Extensions.Time.Testing;
using Serilog;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-mdf-005 acceptance — the C3c scheduler: the trading-day gate (TC-MDF-011) and the
/// 5/15/60-minute retry ladder (TC-MDF-012). Every test drives the real scheduler engine
/// over a <see cref="FakeTimeProvider"/> so schedule/retry edges are exact, against a
/// migrated Testcontainers PostgreSQL and the FU §10 WireMock source double, composing
/// the same <c>AddMarketDataIngestion</c> registration the API host uses.
/// </summary>
public sealed class SchedulerTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);

    /// <summary>The route the prices adapter reads; both the down and the ok stubs are
    /// bound here so the test can switch the source mid-run without touching config.</summary>
    private const string PricesPath = "/prices/eod";

    /// <summary>A liveness bound for "the tick never returns" — real time, not the fake clock.</summary>
    private static readonly TimeSpan LivenessBound = TimeSpan.FromSeconds(30);

    /// <summary>The watchdog's shipped post-cancel settle window (TKT-foundation-085).</summary>
    private static readonly TimeSpan DefaultSettleWindow = IngestionSchedulerOptions.DefaultPostCancelSettleWindow;

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public SchedulerTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    /// <summary>TC-MDF-011 — No trigger on non-trading days.</summary>
    [Fact]
    public async Task TC_MDF_011_No_trigger_on_non_trading_days()
    {
        await ResetAsync();
        _wireMock.Server.ResetMappings();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesOkTminus1, path: PricesPath);

        await using var host = BuildIngestion();
        var clock = host.Clock;

        // The clock starts on Saturday 2026-10-03 (TRT) and sweeps the weekend.
        clock.SetUtcNow(Trt(2026, 10, 3, 0, 0));

        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 3, 23, 59));
        Assert.Equal(0, await LedgerCountAsync());

        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 4, 23, 59));
        Assert.Equal(0, await LedgerCountAsync());

        // Monday 20:30 TRT triggers normally (the fixture's T−1 trading day).
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 5, 20, 30));

        var runs = await LedgerAsync();
        var run = Assert.Single(runs);
        Assert.Equal("prices", run.JobCode);
        Assert.Equal("succeeded", run.Status);
        Assert.Equal(Trt(2026, 10, 5, 20, 30), run.StartedAt!.Value);

        await using var db = _postgres.CreateContext();
        Assert.Equal(16, await db.DailyPrices.CountAsync(p => p.PriceDate == new DateOnly(2026, 10, 5)));
    }

    /// <summary>
    /// TC-MDF-011 (supporting) — the holiday calendar is configuration: a weekday listed
    /// as a holiday is a non-trading day, and its 20:30 occurrence runs no job.
    /// <para>
    /// <b>Amended by TKT-foundation-048 (planner pin 3).</b> The pre-048 contract asserted a
    /// <i>completely</i> empty ledger — the skip was silent, and an operator reading the ledger
    /// could not tell "the market was closed" from "the scheduler never fired". The pinned rule is
    /// now <b>never a silent no-run</b>: the skip writes exactly one short, named and dated
    /// run-ledger row (<c>holiday-skip</c>), and still runs no job. The assertion is therefore
    /// "no job ran" plus "the one row is the skip", which is the same guarantee plus the intent.
    /// A <i>weekend</i> occurrence is untouched and stays fully silent
    /// (<see cref="TC_MDF_011_No_trigger_on_non_trading_days"/>) — a Sunday needs no explanation,
    /// a holiday is the thing the calendar has to explain.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TC_MDF_011_Holiday_calendar_is_config()
    {
        await ResetAsync();
        _wireMock.Server.ResetMappings();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesOkTminus1, path: PricesPath);

        await using var host = BuildIngestion(holiday: "2026-10-05");
        var clock = host.Clock;
        clock.SetUtcNow(Trt(2026, 10, 5, 0, 0));

        // The WireMock server is a class fixture shared with the earlier cases (which do fetch),
        // so the honest "no source call" measure is the delta across this case's window.
        var requestsBefore = _wireMock.RequestsFor(PricesPath);
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 5, 23, 59));

        var runs = await LedgerAsync();
        var skip = Assert.Single(runs);
        Assert.Equal(IngestionScheduler.HolidaySkipJobCode, skip.JobCode);
        Assert.Equal("trading-calendar", StatString(skip, "gate"));
        Assert.Equal("skipped", StatString(skip, "gateOutcome"));
        Assert.Equal("2026-10-05", StatString(skip, "holiday"));

        // No job ran: zero source calls, zero stored prices (the gate is before the chain).
        Assert.Equal(requestsBefore, _wireMock.RequestsFor(PricesPath));
        await using var db = _postgres.CreateContext();
        Assert.Equal(0, await db.DailyPrices.CountAsync());
    }

    /// <summary>
    /// TC-MDF-087 (iii) — <b>a per-job stream is gated by the same calendar.</b> The intraday
    /// sweep's production cadence (047's cron) is a weekday cron, so a configured holiday inside
    /// the week is the one non-trading day it can still land on: the stream runs its job on
    /// <b>no</b> occurrence of that holiday and records exactly <b>one</b> holiday-skip row for it
    /// — one per gated stream per holiday, not one per skipped occurrence, so the 11:15/13:15/
    /// 15:15/17:15 cadence does not flood the ledger — while the next trading day's occurrences
    /// run normally.
    /// <para>
    /// This is the switch that makes <c>JobSchedules[].TradingDaysOnly</c> real: the macro/fund
    /// streams stay ungated (published every day, the default), only the shipped intraday entry
    /// sets it (pinned by <c>TradingCalendarTests</c> on the shipped configuration).
    /// </para>
    /// </summary>
    [Fact]
    public async Task TC_MDF_087_trading_day_gated_per_job_stream_skips_a_configured_holiday()
    {
        await ResetAsync();

        var intraday = new NamedSuccessStep("intraday-quotes");
        await using var host = BuildPerJobHost(
            intraday,
            cron: "0 15 11,13,15,17 * * 1-5",
            tradingDaysOnly: true,
            holiday: "2026-10-05");
        var clock = host.Clock;

        clock.SetUtcNow(Trt(2026, 10, 5, 0, 0));
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 5, 23, 59));

        // All four Monday occurrences were consumed, the job never ran, and the ledger holds the
        // single skip row that says why.
        var runs = await LedgerAsync();
        var skip = Assert.Single(runs);
        Assert.Equal(IngestionScheduler.HolidaySkipJobCode, skip.JobCode);
        Assert.Equal("2026-10-05", StatString(skip, "holiday"));
        Assert.Equal("job:intraday-quotes", StatString(skip, "stream"));

        // Tuesday 2026-10-06 is not a holiday: the same stream runs at its own cadence.
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 6, 11, 15));

        var afterTuesday = await LedgerAsync();
        Assert.Equal(2, afterTuesday.Count);
        Assert.Equal("intraday-quotes", afterTuesday[1].JobCode);
        Assert.Equal(IngestResult.Succeeded, afterTuesday[1].Status);
    }

    /// <summary>TC-MDF-012 — Source failure retries per the 5/15/60 ladder.</summary>
    [Fact]
    public async Task TC_MDF_012_Source_failure_retries_per_ladder()
    {
        await ResetAsync();
        _wireMock.Server.ResetMappings();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesSourceDown, path: PricesPath);

        await using var host = BuildIngestion();
        var clock = host.Clock;
        clock.SetUtcNow(Trt(2026, 10, 5, 20, 29));

        // Attempt 1 at 20:30 — the source is down (HTTP 500).
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 5, 20, 30));
        var afterFirst = await LedgerAsync();
        var first = Assert.Single(afterFirst);
        Assert.Equal("failed", first.Status);
        Assert.Equal(Trt(2026, 10, 5, 20, 30), first.StartedAt!.Value);
        Assert.Equal(1, Attempt(first));

        // Retry 1 at +5 min — still down.
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 5, 20, 35));
        var afterSecond = await LedgerAsync();
        Assert.Equal(2, afterSecond.Count);
        Assert.Equal("failed", afterSecond[1].Status);
        Assert.Equal(Trt(2026, 10, 5, 20, 35), afterSecond[1].StartedAt!.Value);
        Assert.Equal(2, Attempt(afterSecond[1]));

        // The source recovers before retry 2 (+15 min later).
        _wireMock.Server.ResetMappings();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesOkTminus1, path: PricesPath);

        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 5, 20, 50));
        var afterThird = await LedgerAsync();
        Assert.Equal(3, afterThird.Count);
        Assert.Equal("succeeded", afterThird[2].Status);
        Assert.Equal(Trt(2026, 10, 5, 20, 50), afterThird[2].StartedAt!.Value);
        Assert.Equal(3, Attempt(afterThird[2]));
        Assert.Equal(2, Retries(afterThird[2]));

        // The successful retry-2 attempt wrote the day's facts exactly once — the partial
        // attempts left no partial rows behind.
        await using (var db = _postgres.CreateContext())
        {
            Assert.Equal(16, await db.DailyPrices.CountAsync(p => p.PriceDate == new DateOnly(2026, 10, 5)));
            var duplicates = await db.DailyPrices
                .Where(p => p.PriceDate == new DateOnly(2026, 10, 5))
                .GroupBy(p => new { p.InstrumentId, p.PriceDate })
                .Where(g => g.Count() > 1)
                .CountAsync();
            Assert.Equal(0, duplicates);
        }

        // No further attempt fires on the +60 ladder boundary; the run already succeeded.
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 5, 21, 50));
        Assert.Equal(3, await LedgerCountAsync());
    }

    // ---------------------------------------------------------------------------------
    // TKT-foundation-059 — the four RunAttemptAsync boundary defects of 041's round-3 review
    // (DR-1/DR-2/DR-3 edge repairs, DR-5 boundary-branch coverage). Doubles only, fake clock.
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// DR-1 — the attempt's pre-await prologue is inside the failure-classifying try: a
    /// mis-typed <c>Ingestion:Scheduler:MaxRunDuration</c> (60 days, beyond
    /// <c>Task.Delay</c>'s ~49.7-day accepted range) is a failed attempt on the ordinary
    /// ladder, never an escape that leaves <c>Attempts</c> bumped with <c>NextAttemptAt</c>
    /// still past — the unbounded hot refire the watchdog exists to eliminate.
    /// </summary>
    [Fact]
    public async Task TC_MDF_079_DR_1_Mistyped_MaxRunDuration_is_classified_and_never_refires_hot()
    {
        await ResetAsync();

        await using var host = BuildEdgeHost("60.00:00:00", new SilentStep());
        var clock = host.Clock;
        clock.SetUtcNow(Trt(2026, 10, 5, 20, 29));

        // Pre-fix the ArgumentOutOfRangeException escapes this call: the attempt never
        // reaches the ladder, so nothing below is even observable.
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 5, 21, 55));

        // One attempt per rung of the 5/15/60 ladder (20:30, 20:35, 20:50, 21:50), each
        // classified as a failure, then the exhausted alert — bounded accounting, not a loop.
        Assert.Equal(4, FailureLogCount(host));
        var exhausted = Assert.Single(host.Mail.Sent);
        Assert.Contains("failed after 4 attempts", exhausted.Subject);

        // The chain never began: the prologue failed before the first step.
        Assert.Equal(0, await LedgerCountAsync());

        // Exhausted, not overdue: the stream's next wake is tomorrow's 20:30 occurrence, and
        // sweeping the rest of the day adds no attempt (a hot loop would add thousands).
        Assert.Equal(Trt(2026, 10, 6, 20, 30), host.Scheduler.NextWake(clock.GetUtcNow()));
        clock.SetUtcNow(Trt(2026, 10, 5, 23, 59));
        Assert.Equal(4, FailureLogCount(host));
        Assert.Equal(0, await LedgerCountAsync());
    }

    /// <summary>
    /// DR-2 — an attempt that finishes during the watchdog's cancel window is recorded
    /// exactly once, as the terminal truth: the post-cancel <c>IsCompletedSuccessfully</c>
    /// re-check suppresses the <c>RUN_HUNG</c> row/alert. Pre-fix the same attempt produced
    /// both its success row and a <c>RUN_HUNG</c> row.
    /// <para>
    /// This is the <b>inline-resume</b> half of the guarantee (the swallowing double's
    /// continuation runs on the cancelling thread). TC-MDF-113(a) is the other half — the
    /// queued-continuation case that is normal under load, and that this re-check alone could
    /// not see (TKT-foundation-085). Both are kept: neither assertion set is relaxed.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TC_MDF_079_DR_2_Attempt_finishing_in_the_cancel_window_writes_one_true_row()
    {
        await ResetAsync();

        var step = new SwallowingStep();
        await using var host = BuildEdgeHost("00:10:00", step);
        var clock = host.Clock;
        clock.SetUtcNow(Trt(2026, 10, 5, 20, 29));

        clock.SetUtcNow(Trt(2026, 10, 5, 20, 30));
        var tick = host.Scheduler.TickAsync(Trt(2026, 10, 5, 20, 30));
        await step.Entered.Task.WaitAsync(LivenessBound);

        // The bound expires; the step swallows the watchdog's cancellation and returns its
        // success result, so the attempt finishes inside the cancel window.
        clock.Advance(TimeSpan.FromMinutes(11));
        await AwaitTickAsync(tick);

        var runs = await LedgerAsync();
        var row = Assert.Single(runs);
        Assert.Equal(SwallowingStep.Code, row.JobCode);
        Assert.Equal(IngestResult.Succeeded, row.Status);
        Assert.Equal(1, Attempt(row));
        Assert.Null(ReasonCode(row));
        Assert.DoesNotContain(runs, run => ReasonCode(run) == "RUN_HUNG");
        Assert.DoesNotContain(host.Mail.Sent, mail => mail.Subject.Contains("RUN_HUNG"));

        // The completed attempt ends the run: no retry, no exhausted alert.
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 5, 20, 47));
        Assert.Single(await LedgerAsync());
        Assert.DoesNotContain(host.Mail.Sent, mail => mail.Subject.Contains("failed after"));
    }

    /// <summary>
    /// <b>TC-MDF-113(a)</b> — <b>the "one attempt, one row" guarantee is deterministic, not an
    /// interleaving</b> (TKT-foundation-085; this is TC-MDF-079 DR-2 <i>strengthened</i>, with the
    /// same assertion set and a double that reproduces the production continuation).
    /// </summary>
    /// <remarks>
    /// The doubled step is DR-2's swallowing step <b>plus one <c>await Task.Yield()</c></b>: its
    /// post-cancellation continuation is queued to the thread pool instead of resumed inline on
    /// the cancelling thread, which is what an adapter's own <c>await</c> does. The pre-085 code
    /// re-checked <c>work.IsCompletedSuccessfully</c> once, synchronously — so it saw a pending
    /// task, wrote <c>RUN_HUNG</c>, and the queued continuation then wrote the step's own success
    /// row: <b>two ledger rows for one attempt</b>, a spurious <c>RUN_HUNG</c> alert and a
    /// spurious retry (the E3 escalation of PR #141's review round 2; the CI flake of this very
    /// case). The bounded post-cancel settle window makes the re-check an interval, so the
    /// terminal truth is observed whichever way the continuation is scheduled.
    /// <para>
    /// Nothing here is relaxed: exactly one row, the step's own <c>succeeded</c> row on attempt 1,
    /// no <c>reasonCode</c>, no <c>RUN_HUNG</c> row, no <c>RUN_HUNG</c> alert, no retry and no
    /// exhausted alert — the DR-2 contract verbatim, now across the interleaving that is normal.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TC_MDF_113_Post_cancel_settle_window_makes_the_queued_continuation_one_row()
    {
        await ResetAsync();

        var step = new QueuedSwallowingStep();
        await using var host = BuildEdgeHost("00:10:00", step);
        var clock = host.Clock;
        clock.SetUtcNow(Trt(2026, 10, 5, 20, 29));

        clock.SetUtcNow(Trt(2026, 10, 5, 20, 30));
        var tick = host.Scheduler.TickAsync(Trt(2026, 10, 5, 20, 30));
        await step.Entered.Task.WaitAsync(LivenessBound);

        // 20:40 — the chain ceiling expires; the step swallows the cancellation and its
        // continuation is queued, so it finishes *inside* the watchdog's post-cancel settle
        // window rather than behind its single re-check.
        clock.Advance(TimeSpan.FromMinutes(11));
        await AwaitTickAsync(tick);

        // One attempt, one row: the step's own terminal truth.
        var runs = await LedgerAsync();
        var row = Assert.Single(runs);
        Assert.Equal(QueuedSwallowingStep.Code, row.JobCode);
        Assert.Equal(IngestResult.Succeeded, row.Status);
        Assert.Equal(1, Attempt(row));
        Assert.Null(ReasonCode(row));
        Assert.DoesNotContain(runs, run => ReasonCode(run) == "RUN_HUNG");
        Assert.DoesNotContain(host.Mail.Sent, mail => mail.Subject.Contains("RUN_HUNG"));
        Assert.DoesNotContain(
            host.Logs.Events,
            logEvent => logEvent.RenderMessage().Contains("Hung run detected"));

        // The completed attempt ends the run: no retry row on the ladder, no exhausted alert.
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 5, 20, 47));
        Assert.Single(await LedgerAsync());
        Assert.DoesNotContain(host.Mail.Sent, mail => mail.Subject.Contains("failed after"));
    }

    /// <summary>
    /// DR-3 — the <c>RUN_HUNG</c> row and alert name the step the watchdog actually caught:
    /// the snapshot is hoisted above the cancellation, so a step that swallows the
    /// cancellation and advances the chain cannot rename the hung step in the record.
    /// </summary>
    [Fact]
    public async Task TC_MDF_079_DR_3_RunHung_row_names_the_pre_cancel_step()
    {
        await ResetAsync();

        // Step 1 swallows the cancellation and returns Partial (the chain advances); step 2
        // then hangs uninterruptibly, so the pre-fix snapshot taken after the cancel names
        // the *next* step instead of the one that held the run at the bound.
        var swallow = new SwallowingStep(IngestResult.Partial);
        var hang = new UninterruptibleStep();
        await using var host = BuildEdgeHost("00:10:00", swallow, hang);
        var clock = host.Clock;
        clock.SetUtcNow(Trt(2026, 10, 5, 20, 29));

        clock.SetUtcNow(Trt(2026, 10, 5, 20, 30));
        var tick = host.Scheduler.TickAsync(Trt(2026, 10, 5, 20, 30));
        await swallow.Entered.Task.WaitAsync(LivenessBound);

        clock.Advance(TimeSpan.FromMinutes(11));
        await AwaitTickAsync(tick, host, DefaultSettleWindow);

        var hung = Assert.Single(await LedgerAsync(), run => ReasonCode(run) == "RUN_HUNG");
        Assert.Equal(IngestResult.Failed, hung.Status);
        Assert.Equal(SwallowingStep.Code, hung.JobCode);
        Assert.Equal(SwallowingStep.Code, StatString(hung, "hungJob"));
        Assert.Equal(1, Stat(hung, "step"));
        Assert.Equal(2, Stat(hung, "stepCount"));

        var alert = Assert.Single(host.Mail.Sent, mail => mail.Subject.Contains("RUN_HUNG"));
        Assert.Contains(SwallowingStep.Code, alert.Subject);
        Assert.DoesNotContain(UninterruptibleStep.Code, alert.Subject);
    }

    /// <summary>
    /// DR-5(a) — the round-2 boundary branch "completes as the deadline fires": a run that
    /// finishes while the bound is armed is a completed attempt — one success row, no
    /// <c>RUN_HUNG</c> row or alert, no retry, and the disarmed deadline fires nothing later.
    /// </summary>
    [Fact]
    public async Task TC_MDF_079_DR_5a_Attempt_completing_against_the_armed_bound_is_completed_once()
    {
        await ResetAsync();

        var step = new ControllableStep();
        await using var host = BuildEdgeHost("00:10:00", step);
        var clock = host.Clock;
        clock.SetUtcNow(Trt(2026, 10, 5, 20, 29));

        clock.SetUtcNow(Trt(2026, 10, 5, 20, 30));
        var tick = host.Scheduler.TickAsync(Trt(2026, 10, 5, 20, 30));
        await step.Entered.Task.WaitAsync(LivenessBound);

        // The run finishes at the bound: the watchdog's deadline is armed and its re-check
        // must prefer the terminal truth over a RUN_HUNG row.
        step.Complete();
        await AwaitTickAsync(tick);

        // Sweeping past the bound afterwards must not conjure a hung row either.
        clock.Advance(TimeSpan.FromMinutes(11));

        var row = Assert.Single(await LedgerAsync());
        Assert.Equal(ControllableStep.Code, row.JobCode);
        Assert.Equal(IngestResult.Succeeded, row.Status);
        Assert.Equal(1, Attempt(row));
        Assert.Empty(host.Mail.Sent);
        Assert.DoesNotContain(host.Logs.Events, logEvent => logEvent.RenderMessage().Contains("Hung run detected"));
    }

    /// <summary>
    /// DR-5(b) — the round-2 boundary branch "host stopping → no RUN_HUNG": when the host's
    /// token is already cancelled as the bound expires, the attempt unwinds as a shutdown —
    /// no ledger row, no alert — instead of being reported as a hung run.
    /// </summary>
    [Fact]
    public async Task TC_MDF_079_DR_5b_Host_shutdown_at_the_bound_raises_no_RunHung_row_or_alert()
    {
        await ResetAsync();

        var hang = new UninterruptibleStep();
        await using var host = BuildEdgeHost("00:10:00", hang);
        var clock = host.Clock;
        clock.SetUtcNow(Trt(2026, 10, 5, 20, 29));

        clock.SetUtcNow(Trt(2026, 10, 5, 20, 30));
        using var stopping = new CancellationTokenSource();
        var tick = host.Scheduler.TickAsync(Trt(2026, 10, 5, 20, 30), stopping.Token);
        await hang.Entered.Task.WaitAsync(LivenessBound);

        // The host stops, then the bound expires in the same window.
        stopping.Cancel();
        clock.Advance(TimeSpan.FromMinutes(11));

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await tick.WaitAsync(LivenessBound));
        Assert.Equal(0, await LedgerCountAsync());
        Assert.Empty(host.Mail.Sent);
        Assert.DoesNotContain(host.Logs.Events, logEvent => logEvent.RenderMessage().Contains("Hung run detected"));
    }

    // ---------------------------------------------------------------------------------
    // TKT-foundation-062 — retry-delay starvation: a due instant that falls while another
    // stream's attempt holds the tick. Both cases are doubles-only over the fake clock.
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// TC-MDF-081 (a) — <b>retry punctuality</b> (acceptance ii). The EOD attempt fails at 20:30, so the
    /// ladder arms retry 1 for 20:35. A per-job stream's attempt starts at 20:31 and does not
    /// end on its own, so the tick that started it is still in flight when 20:35 arrives.
    /// <para>
    /// Pre-fix the tick awaits one stream's attempt to its end before it evaluates anything
    /// else, so the retry armed for 20:35 is not re-evaluated until the holding attempt ends:
    /// it fires late (or, in the live 2026-10-22 trace, ~11h49m late) and the ladder's
    /// punctuality is lost. Post-fix the tick honours the armed instant on the injected clock
    /// and the retry's attempt starts at exactly 20:35 — the ledger row's <c>started_at</c>
    /// is the assertion, not merely "a retry eventually happened".
    /// </para>
    /// </summary>
    [Fact]
    public async Task TC_MDF_081_Retry_fires_at_its_scheduled_instant_while_another_stream_holds_the_tick()
    {
        await ResetAsync();

        var retryTarget = new FailOnceStep();
        var holder = new ControllableStep();
        await using var host = BuildStarvationHost(
            maxRunDuration: "02:00:00",
            eodChain: [retryTarget],
            perJobHolder: holder,
            perJobCron: "0 31 20 * * *");
        var clock = host.Clock;
        clock.SetUtcNow(Trt(2026, 10, 5, 20, 29));

        // 20:30 — attempt 1 fails; the ladder arms retry 1 for 20:35 (+5 min).
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 5, 20, 30));
        var first = Assert.Single(await LedgerAsync());
        Assert.Equal(FailOnceStep.Code, first.JobCode);
        Assert.Equal(IngestResult.Failed, first.Status);
        Assert.Equal(1, Attempt(first));

        // 20:31 — the per-job stream's attempt starts and holds the tick (it ends only when the
        // test releases it), so the tick is still in flight when the retry falls due.
        clock.SetUtcNow(Trt(2026, 10, 5, 20, 31));
        var holdingTick = host.Scheduler.TickAsync(Trt(2026, 10, 5, 20, 31));
        await holder.Entered.Task.WaitAsync(LivenessBound);

        // 20:35 — the retry's scheduled instant. Pre-fix nothing observes it here and this wait
        // times out: the retry is frozen behind the unrelated stream's attempt.
        clock.SetUtcNow(Trt(2026, 10, 5, 20, 35));
        await retryTarget.SecondCallEntered.Task.WaitAsync(LivenessBound);

        // Release the holding attempt; the tick that started it drains and returns.
        holder.Complete();
        await AwaitTickAsync(holdingTick);

        // The retry ran at its own instant — 20:35, not at the release instant.
        var ledger = await LedgerAsync();
        var retried = Assert.Single(ledger, run => run.JobCode == FailOnceStep.Code && Attempt(run) == 2);
        Assert.Equal(IngestResult.Succeeded, retried.Status);
        Assert.Equal(Trt(2026, 10, 5, 20, 35), retried.StartedAt!.Value);
        Assert.Equal(1, Retries(retried));

        // Exactly two attempts of the retrying run: the retry succeeded, so the rest of the
        // ladder stayed silent (the holding stream's own row is a different stream).
        Assert.Equal(2, ledger.Count(run => run.JobCode == FailOnceStep.Code));
    }

    /// <summary>
    /// TC-MDF-081 (b) — <b>morning-chain independence while a retry is in flight</b> (acceptance iii). This is
    /// the live shape at the interaction level — the retry's step hangs at an await: the EOD attempt
    /// fails at 20:30, retry 1 starts at 20:35 and its chain step hangs, and the 08:30 Istanbul
    /// (05:30 UTC) morning chain falls due while that retry attempt is still holding the tick. Live, the morning chain did not
    /// fire at 05:30 — it started at 12:11:24Z, the instant the EOD attempt released the loop,
    /// ~6h41m late.
    /// <para>
    /// Pre-fix nothing runs at 05:30 (the tick is inside the retry's attempt) and this test's
    /// morning-step wait times out. Post-fix the morning stream's attempt starts at 05:30 —
    /// independently of the EOD attempt still in flight — for its previous trading day
    /// (2026-10-05) on attempt 1.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TC_MDF_081_Morning_chain_fires_on_schedule_while_a_retry_attempt_holds_the_tick()
    {
        await ResetAsync();

        var eod = new RetryThenHangStep();
        var morningSteps = MorningIngestChain.JobCodes.Select(code => new NamedSuccessStep(code)).ToList();
        await using var host = BuildStarvationHost(
            // TC-MDF-081 (b) premise: 12 h is chosen so the retry attempt must still be in flight
            // when 05:30 UTC arrives the next morning, so neither watchdog bound is what releases
            // it inside this window — the per-step bound (shipped 120 min) is raised alongside the
            // ceiling (TKT-foundation-064).
            maxRunDuration: "12:00:00",
            maxStepDuration: "12:00:00",
            eodChain: [eod],
            morningChain: morningSteps,
            morningCron: "0 30 8 * * *");
        var clock = host.Clock;
        clock.SetUtcNow(Trt(2026, 10, 5, 20, 29));

        // 20:30 — attempt 1 fails; retry 1 is armed for 20:35.
        await AdvanceToAsync(host.Scheduler, clock, Trt(2026, 10, 5, 20, 30));
        Assert.Equal(IngestResult.Failed, Assert.Single(await LedgerAsync()).Status);

        // 20:35 — the retry (attempt 2) starts and its step hangs: the tick holds across the night.
        clock.SetUtcNow(Trt(2026, 10, 5, 20, 35));
        var holdingTick = host.Scheduler.TickAsync(Trt(2026, 10, 5, 20, 35));
        await eod.Hung.Task.WaitAsync(LivenessBound);

        // 2026-10-06 08:30 Istanbul = 05:30Z — the morning chain's own instant. The EOD retry is
        // still in flight; the morning chain must fire anyway (here: its whole chain runs).
        clock.SetUtcNow(Trt(2026, 10, 6, 8, 30));
        await morningSteps[^1].Entered.WaitAsync(LivenessBound);

        // Release the EOD retry so the holding tick can drain and return.
        eod.Release();
        await AwaitTickAsync(holdingTick);

        // Every morning step ran at 05:30Z, attempt 1, for the previous trading day (Monday).
        var ledger = await LedgerAsync();
        foreach (var step in morningSteps)
        {
            var row = Assert.Single(ledger, run => run.JobCode == step.JobCode && Attempt(run) == 1 && run.StartedAt == Trt(2026, 10, 6, 8, 30));
            Assert.Equal(IngestResult.Succeeded, row.Status);
        }

        Assert.All(morningSteps, step => Assert.Equal(new DateOnly(2026, 10, 5), step.RequestedDate));

        // The EOD stream's own retry ran at its instant too (20:35) — the two streams never
        // waited on each other.
        var eodRetry = Assert.Single(ledger, run => run.JobCode == RetryThenHangStep.Code && Attempt(run) == 2);
        Assert.Equal(Trt(2026, 10, 5, 20, 35), eodRetry.StartedAt!.Value);
    }

    /// <summary>Completes the tick within the liveness bound, never hanging the suite.</summary>
    private static async Task AwaitTickAsync(Task tick)
    {
        var finished = await Task.WhenAny(tick, Task.Delay(LivenessBound));
        Assert.Same(tick, finished);
        await tick;
    }

    /// <summary>
    /// Completes a tick that is <b>waiting on the watchdog's post-cancel settle window</b>
    /// (TKT-foundation-085). The grace is a delay on the injected clock, armed on a continuation
    /// thread <i>after</i> the bound fired, so a case that ends in the hung path advances the
    /// fake clock past it — the same premise the cases already make for the bounds themselves.
    /// <para>
    /// The advance is made in exactly two steps, both deterministic: the harness first waits for
    /// the watchdog's own "settling … giving the attempt the &lt;window&gt; post-cancel settle
    /// window" log line (which the implementation emits <i>after</i> arming the timer), then
    /// advances the fake clock by the whole window in one go. Nothing is guessed from timing —
    /// and the clock is not touched before that line, so the instant the watchdog recorded as the
    /// attempt's end is still the bound's instant, exactly as before this ticket.
    /// </para>
    /// <para>
    /// <b>TC-MDF-081 is a kept-green non-regression</b> (acceptance iv). Both of its cases in this
    /// file — <c>TC_MDF_081_Retry_fires_at_its_scheduled_instant_while_another_stream_holds_the_tick</c>
    /// and <c>TC_MDF_081_Morning_chain_fires_on_schedule_while_a_retry_attempt_holds_the_tick</c> —
    /// run unchanged in this same suite. Neither ends in the hung path, so neither takes this
    /// overload — and this harness only moves the fake clock <i>after</i> the attempt's end has
    /// been recorded, so the instants TC-MDF-081 pins stay exact.
    /// </para>
    /// </summary>
    private static async Task AwaitTickAsync(Task tick, IngestionSchedulerHost host, TimeSpan settleWindow)
    {
        if (settleWindow > TimeSpan.Zero)
        {
            await WaitForSettleWindowArmedAsync(host.Logs, settleWindow);
            host.Clock.Advance(settleWindow + SettleAdvanceMargin);
        }

        await AwaitTickAsync(tick);
    }

    /// <summary>The margin added to the grace when the harness advances the fake clock past it.</summary>
    private static readonly TimeSpan SettleAdvanceMargin = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Waits (real time, bounded) for the watchdog to have armed its post-cancel settle window —
    /// observed through the log line it writes immediately after arming, which is the only
    /// observable side of a clock delay. The fake clock is untouched here, so the schedule stays
    /// exact; only the wait for the continuation thread is real.
    /// </summary>
    private static async Task WaitForSettleWindowArmedAsync(InMemorySerilogSink logs, TimeSpan settleWindow)
    {
        var marker = $"the {settleWindow} post-cancel settle window";
        var deadline = DateTime.UtcNow + LivenessBound;
        while (!logs.Contains(logEvent => logEvent.RenderMessage().Contains(marker)))
        {
            Assert.True(
                DateTime.UtcNow < deadline,
                $"the watchdog never armed a {settleWindow} post-cancel settle window");
            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }
    }

    /// <summary>The scheduler's "failed on attempt N" classification lines.</summary>
    private static int FailureLogCount(IngestionSchedulerHost host) =>
        host.Logs.Events.Count(logEvent => logEvent.RenderMessage().Contains("failed on attempt"));

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Drives the same wake-then-tick loop the hosted service runs, but deterministically
    /// over the fake clock: it advances to each due instant up to and including
    /// <paramref name="target"/> and processes it, then settles on the target.
    /// </summary>
    private static async Task AdvanceToAsync(
        IngestionScheduler scheduler,
        FakeTimeProvider clock,
        DateTimeOffset target)
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
            await scheduler.TickAsync(instant);
        }

        clock.SetUtcNow(target.ToUniversalTime());
    }

    private IngestionSchedulerHost BuildIngestion(string? holiday = null)
    {
        var mail = new RecordingMailDispatcher();
        var logs = new InMemorySerilogSink();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _postgres.ConnectionString,
            ["Ingestion:Prices:BaseUrl"] = _wireMock.BaseUrl,
            ["Ingestion:Prices:Path"] = PricesPath,
            // The L2 harness binds the loop to the seeded FU XU100 (§8 preamble) and disables
            // pacing, so the scheduler's price goldens survive the per-ticker loop.
            ["Ingestion:Prices:UniverseIndex"] = "XU100",
            ["Ingestion:Prices:RequestDelayMs"] = "0",
            ["Ingestion:Scheduler:Cron"] = "0 30 20 * * *",
            ["Ingestion:Scheduler:TimeZone"] = "Europe/Istanbul",
            ["Ingestion:Scheduler:JobCodes:0"] = "prices",
        };
        if (holiday is not null)
        {
            settings["Ingestion:TradingCalendar:Holidays:0"] = holiday;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var logger = new LoggerConfiguration().MinimumLevel.Warning().WriteTo.Sink(logs).CreateLogger();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog(logger, dispose: false));
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IIngestionAlerter>(new HarnessMailAlerter(mail));
        services.AddMarketDataIngestion(configuration);

        var provider = services.BuildServiceProvider();
        var scheduler = provider.GetRequiredService<IngestionScheduler>();
        return new IngestionSchedulerHost(provider, clock, scheduler, mail, logs);
    }

    /// <summary>
    /// The TKT-foundation-048 harness: one <b>per-job</b> stream on the real engine, with the
    /// trading-day gate switchable and a holiday configurable — the intraday sweep's production
    /// shape (047's cron), which pre-048 had no way to gate. The EOD stream is parked on a
    /// Saturday cron so it cannot enter the case's Monday–Tuesday window: this harness observes
    /// the per-job stream's gate, never the chain's.
    /// </summary>
    private IngestionSchedulerHost BuildPerJobHost(
        IIngestJob job,
        string cron,
        bool tradingDaysOnly,
        string? holiday = null)
    {
        var mail = new RecordingMailDispatcher();
        var logs = new InMemorySerilogSink();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _postgres.ConnectionString,
            // Out of the window: a Saturday-only EOD cron never occurs Mon–Tue.
            ["Ingestion:Scheduler:Cron"] = "0 30 20 * * 6",
            ["Ingestion:Scheduler:TimeZone"] = "Europe/Istanbul",
            ["Ingestion:Scheduler:JobSchedules:0:JobCode"] = job.JobCode,
            ["Ingestion:Scheduler:JobSchedules:0:Cron"] = cron,
            ["Ingestion:Scheduler:JobSchedules:0:TradingDaysOnly"] = tradingDaysOnly ? "true" : "false",
        };

        if (holiday is not null)
        {
            settings["Ingestion:TradingCalendar:Holidays:0"] = holiday;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var logger = new LoggerConfiguration().MinimumLevel.Warning().WriteTo.Sink(logs).CreateLogger();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog(logger, dispose: false));
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IIngestionAlerter>(new HarnessMailAlerter(mail));
        services.AddMarketDataIngestion(configuration);
        services.AddSingleton<IIngestionJobRunner>(new RecordingJobRunner(_postgres, clock, [job]));

        var provider = services.BuildServiceProvider();
        var scheduler = provider.GetRequiredService<IngestionScheduler>();
        return new IngestionSchedulerHost(provider, clock, scheduler, mail, logs);
    }

    /// <summary>
    /// The TKT-foundation-059 harness: the same real scheduler engine and fake clock, with the
    /// chain composed entirely of doubles and the job runner doubled so the ledger write lands
    /// deterministically (see <see cref="RecordingJobRunner"/>).
    /// </summary>
    private IngestionSchedulerHost BuildEdgeHost(string maxRunDuration, params IIngestJob[] chain)
    {
        var mail = new RecordingMailDispatcher();
        var logs = new InMemorySerilogSink();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _postgres.ConnectionString,
            ["Ingestion:Scheduler:Cron"] = "0 30 20 * * *",
            ["Ingestion:Scheduler:TimeZone"] = "Europe/Istanbul",
            ["Ingestion:Scheduler:MaxRunDuration"] = maxRunDuration,
        };
        for (var index = 0; index < chain.Length; index++)
        {
            settings[$"Ingestion:Scheduler:JobCodes:{index}"] = chain[index].JobCode;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var logger = new LoggerConfiguration().MinimumLevel.Information().WriteTo.Sink(logs).CreateLogger();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog(logger, dispose: false));
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IIngestionAlerter>(new HarnessMailAlerter(mail));
        services.AddMarketDataIngestion(configuration);

        // The doubled runner replaces the real one (last registration wins) so the boundary
        // interleavings below are deterministic; the dispatcher, ledger table, scheduler
        // engine, retry ladder and watchdog are all the production code paths.
        services.AddSingleton<IIngestionJobRunner>(new RecordingJobRunner(_postgres, clock, chain));

        var provider = services.BuildServiceProvider();
        var scheduler = provider.GetRequiredService<IngestionScheduler>();
        return new IngestionSchedulerHost(provider, clock, scheduler, mail, logs);
    }

    /// <summary>
    /// The TKT-foundation-062 harness: the same real scheduler engine and fake clock, with the
    /// EOD and (optionally) the morning stream composed entirely of doubles and the job runner
    /// doubled. It exists to place a <b>second</b> stream's attempt in flight across another
    /// stream's due instant — the starvation interaction, not any single chain's behaviour.
    /// </summary>
    private IngestionSchedulerHost BuildStarvationHost(
        string maxRunDuration,
        IReadOnlyList<IIngestJob> eodChain,
        IReadOnlyList<IIngestJob>? morningChain = null,
        IIngestJob? perJobHolder = null,
        string? perJobCron = null,
        string? morningCron = null,
        string? maxStepDuration = null)
    {
        var mail = new RecordingMailDispatcher();
        var logs = new InMemorySerilogSink();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _postgres.ConnectionString,
            ["Ingestion:Prices:UniverseIndex"] = "XU100",
            ["Ingestion:Prices:RequestDelayMs"] = "0",
            ["Ingestion:Scheduler:Cron"] = "0 30 20 * * *",
            ["Ingestion:Scheduler:TimeZone"] = "Europe/Istanbul",
            ["Ingestion:Scheduler:MaxRunDuration"] = maxRunDuration,
        };
        for (var index = 0; index < eodChain.Count; index++)
        {
            settings[$"Ingestion:Scheduler:JobCodes:{index}"] = eodChain[index].JobCode;
        }

        if (morningCron is not null)
        {
            settings["Ingestion:Scheduler:MorningCron"] = morningCron;
        }

        // The trading-day gate (TC-MDF-011) stays out of this harness's path: both starvation
        // cases start on the 2026-10-05 trading day, so no bound added here can change whether
        // their due instants are skipped.
        if (maxStepDuration is not null)
        {
            settings["Ingestion:Scheduler:MaxStepDuration"] = maxStepDuration;
        }

        if (perJobHolder is not null)
        {
            settings["Ingestion:Scheduler:JobSchedules:0:JobCode"] = perJobHolder.JobCode;
            settings["Ingestion:Scheduler:JobSchedules:0:Cron"] = perJobCron;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var logger = new LoggerConfiguration().MinimumLevel.Information().WriteTo.Sink(logs).CreateLogger();

        var jobs = eodChain
            .Concat(morningChain ?? [])
            .Concat(perJobHolder is null ? [] : [perJobHolder])
            .ToList();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog(logger, dispose: false));
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IIngestionAlerter>(new HarnessMailAlerter(mail));
        services.AddMarketDataIngestion(configuration);
        services.AddSingleton<IIngestionJobRunner>(new RecordingJobRunner(_postgres, clock, jobs));

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

    private async Task<int> LedgerCountAsync()
    {
        await using var db = _postgres.CreateContext();
        return await db.IngestRuns.CountAsync();
    }

    private static int Attempt(IngestRun run) => Stat(run, "attempt");

    private static int Retries(IngestRun run) => Stat(run, "retries");

    private static int Stat(IngestRun run, string name)
    {
        using var document = JsonDocument.Parse(run.StatsJson!);
        return document.RootElement.GetProperty(name).GetInt32();
    }

    private static string StatString(IngestRun run, string name)
    {
        using var document = JsonDocument.Parse(run.StatsJson!);
        return document.RootElement.GetProperty(name).GetString()!;
    }

    /// <summary>The run-ledger row's own reason code, or null for a job row (which has none).</summary>
    private static string? ReasonCode(IngestRun run)
    {
        if (run.StatsJson is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(run.StatsJson);
        return document.RootElement.TryGetProperty("reasonCode", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
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
                new OutboundMail("builder@degerli.test", alert.Subject, alert.BodyTr, alert.BodyEn),
                cancellationToken);
    }

    // ---------------------------------------------------------------------------------
    // TKT-foundation-059 doubles
    // ---------------------------------------------------------------------------------

    /// <summary>A healthy step: the DR-1 chain never reaches it (the prologue fails first).</summary>
    private sealed class SilentStep : IIngestJob
    {
        public const string Code = "silent-step";

        public string JobCode => Code;

        public Task<IngestResult> RunAsync(IngestionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new IngestResult(Code, 1, 0, 0, IngestResult.Succeeded));
    }

    /// <summary>
    /// A chain step that <b>swallows</b> the watchdog's cancellation and returns its terminal
    /// result — the cooperative-but-cancellation-tolerant shape (adapters return
    /// <see cref="IngestResult.Partial"/> in production). It waits on a cancellation callback,
    /// so under the fake clock the attempt's outcome runs deterministically inline on the
    /// cancelling thread: the chain can advance (or complete) inside the watchdog's cancel
    /// window, which is exactly the boundary DR-2 and DR-3 are about.
    /// </summary>
    private sealed class SwallowingStep : IIngestJob
    {
        public const string Code = "swallow-step";

        private readonly string _status;
        private int _calls;

        public SwallowingStep(string status = IngestResult.Succeeded) => _status = status;

        /// <summary>Completes when the doubled step has entered its awaiting branch.</summary>
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string JobCode => Code;

        public Task<IngestResult> RunAsync(IngestionRequest request, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) > 1)
            {
                return Task.FromResult(new IngestResult(Code, 1, 0, 0, _status));
            }

            Entered.TrySetResult();
            return AwaitCancellationAsync(cancellationToken);
        }

        private async Task<IngestResult> AwaitCancellationAsync(CancellationToken cancellationToken)
        {
            // A plain (non-run-continuations-asynchronously) source completed from a
            // cancellation callback: the continuation runs inline when the watchdog cancels.
            var cancelled = new TaskCompletionSource(TaskCreationOptions.None);
            using var registration = cancellationToken.Register(
                static state => ((TaskCompletionSource)state!).TrySetResult(),
                cancelled);

            // The watchdog's own cancellation: swallowed, the step still reports its terminal
            // result — that is what lets the chain advance inside the cancel window.
            await cancelled.Task;
            return new IngestResult(Code, 1, 0, 0, _status);
        }
    }

    /// <summary>
    /// The same cancellation-swallowing step with the interleaving that is <b>normal in
    /// production</b>: the post-cancellation continuation is <b>queued</b> to the thread pool
    /// instead of resumed inline on the cancelling thread (TKT-foundation-085, TC-MDF-113(a)).
    /// <para>
    /// The single <c>await Task.Yield()</c> is the whole difference, and it is the difference
    /// between one ledger row and two: the scheduler's post-cancel re-check now runs while this
    /// step's continuation is still pending, so a re-check that does not <i>wait</i> for the
    /// attempt declares it hung — and this step then writes its own success row beside the
    /// <c>RUN_HUNG</c> row. DR-2's guarantee ("one attempt, one row") has to hold across the
    /// queued-continuation case, not only the inline one.
    /// </para>
    /// </summary>
    private sealed class QueuedSwallowingStep : IIngestJob
    {
        public const string Code = "queued-swallow-step";

        private int _calls;

        /// <summary>Completes when the doubled step has entered its awaiting branch.</summary>
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string JobCode => Code;

        public Task<IngestResult> RunAsync(IngestionRequest request, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) > 1)
            {
                return Task.FromResult(new IngestResult(Code, 1, 0, 0, IngestResult.Succeeded));
            }

            Entered.TrySetResult();
            return AwaitCancellationAsync(cancellationToken);
        }

        private async Task<IngestResult> AwaitCancellationAsync(CancellationToken cancellationToken)
        {
            var cancelled = new TaskCompletionSource(TaskCreationOptions.None);
            using var registration = cancellationToken.Register(
                static state => ((TaskCompletionSource)state!).TrySetResult(),
                cancelled);

            await cancelled.Task;

            // THE HOP: the rest of this step — and therefore its terminal result — resumes on the
            // thread pool, exactly as an adapter's `await` inside a caught cancellation would.
            await Task.Yield();
            return new IngestResult(Code, 1, 0, 0, IngestResult.Succeeded);
        }
    }

    /// <summary>A step whose await never completes and never observes cancellation.</summary>
    private sealed class UninterruptibleStep : IIngestJob
    {
        public const string Code = "uninterruptible-step";

        /// <summary>Completes when the doubled step has entered its awaiting branch.</summary>
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string JobCode => Code;

        public async Task<IngestResult> RunAsync(IngestionRequest request, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.Infinite);
            return new IngestResult(Code, 1, 0, 0, IngestResult.Succeeded);
        }
    }

    /// <summary>
    /// A step that suspends on a test-controlled signal: it completes when
    /// <see cref="Complete"/> is called, independently of the token and of the watchdog.
    /// </summary>
    private sealed class ControllableStep : IIngestJob
    {
        public const string Code = "controllable-step";

        private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completes when the doubled step has entered its awaiting branch.</summary>
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string JobCode => Code;

        public async Task<IngestResult> RunAsync(IngestionRequest request, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await _done.Task;
            return new IngestResult(Code, 1, 0, 0, IngestResult.Succeeded);
        }

        /// <summary>Lets the step finish successfully — the "completes at the bound" signal.</summary>
        public void Complete() => _done.TrySetResult();
    }

    /// <summary>
    /// A chain step that fails its first call and succeeds afterwards — the shape the retry
    /// ladder exists for. <see cref="SecondCallEntered"/> completes when the retry's attempt
    /// reaches the step, which is the instant the test asserts punctuality on.
    /// </summary>
    private sealed class FailOnceStep : IIngestJob
    {
        public const string Code = "eod-fail-once";

        private int _calls;

        /// <summary>Completes when the retry attempt has entered the step.</summary>
        public TaskCompletionSource SecondCallEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string JobCode => Code;

        public Task<IngestResult> RunAsync(IngestionRequest request, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                throw new InvalidOperationException($"{Code}: source step failed (doubled).");
            }

            SecondCallEntered.TrySetResult();
            return Task.FromResult(new IngestResult(Code, 1, 0, 0, IngestResult.Succeeded));
        }
    }

    /// <summary>
    /// A chain step that fails its first call and <b>hangs</b> on its second (the retry's
    /// attempt) until <see cref="Release"/> — the live 2026-10-22 shape, where the retry's own
    /// attempt is what holds the tick across the morning chain's instant.
    /// </summary>
    private sealed class RetryThenHangStep : IIngestJob
    {
        public const string Code = "eod-retry-hang";

        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        /// <summary>Completes when the retry attempt's step has reached its hanging await.</summary>
        public TaskCompletionSource Hung { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string JobCode => Code;

        public async Task<IngestResult> RunAsync(IngestionRequest request, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                throw new InvalidOperationException($"{Code}: source step failed (doubled).");
            }

            Hung.TrySetResult();
            await _released.Task.ConfigureAwait(false);
            return new IngestResult(Code, 1, 0, 0, IngestResult.Succeeded);
        }

        /// <summary>Ends the hanging retry attempt (successfully).</summary>
        public void Release() => _released.TrySetResult();
    }

    /// <summary>
    /// A healthy chain step that announces its first entry (<see cref="Entered"/>) and succeeds —
    /// the morning chain's eight pinned steps composed without any source call. It also records
    /// the run date it was asked for, which is how the morning stream's previous-trading-day
    /// resolution is asserted.
    /// </summary>
    private sealed class NamedSuccessStep : IIngestJob
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public NamedSuccessStep(string code) => JobCode = code;

        public string JobCode { get; }

        /// <summary>Completes when the step's first call is reached.</summary>
        public Task Entered => _entered.Task;

        /// <summary>The day the step was asked to run for (first call).</summary>
        public DateOnly RequestedDate { get; private set; }

        public Task<IngestResult> RunAsync(IngestionRequest request, CancellationToken cancellationToken = default)
        {
            RequestedDate = request.Date;
            _entered.TrySetResult();
            return Task.FromResult(new IngestResult(JobCode, 1, 0, 0, IngestResult.Succeeded));
        }
    }

    /// <summary>
    /// The job runner double (TKT-foundation-059): it runs the real doubled job and appends the
    /// same terminal <c>ingest_runs</c> row the production runner writes, but commits it with
    /// <see cref="CancellationToken.None"/> — pinning the one interleaving the boundary tests
    /// measure, "the step's terminal result was already recorded when the watchdog's
    /// cancellation arrived". The real runner's write and that cancellation race on the thread
    /// pool (both orders are legal); the double removes the race so the scheduler's own
    /// accounting — the row it must <i>not</i> add — is what the test asserts.
    /// </summary>
    private sealed class RecordingJobRunner : IIngestionJobRunner
    {
        private static readonly JsonSerializerOptions StatsJsonOptions = new(JsonSerializerDefaults.Web);

        private readonly PostgresFixture _postgres;
        private readonly TimeProvider _clock;
        private readonly IReadOnlyList<IIngestJob> _jobs;

        public RecordingJobRunner(PostgresFixture postgres, TimeProvider clock, IEnumerable<IIngestJob> jobs)
        {
            _postgres = postgres;
            _clock = clock;
            _jobs = jobs.ToList();
        }

        public IReadOnlyCollection<string> JobCodes => _jobs.Select(job => job.JobCode).ToList();

        public Task<IngestResult> RunAsync(
            string jobCode,
            IngestionRequest request,
            CancellationToken cancellationToken = default) =>
            RunAsync(jobCode, request, new IngestionRunContext(1, 1), cancellationToken);

        public Task<IngestResult> RunAsync(
            string jobCode,
            IngestionRequest request,
            IngestionRunContext context,
            CancellationToken cancellationToken = default) =>
            RunCoreAsync(jobCode, request, context, cancellationToken);

        private async Task<IngestResult> RunCoreAsync(
            string jobCode,
            IngestionRequest request,
            IngestionRunContext context,
            CancellationToken cancellationToken)
        {
            var job = _jobs.Single(candidate => candidate.JobCode == jobCode);
            var startedAt = _clock.GetUtcNow();
            IngestResult result;
            try
            {
                result = await job.RunAsync(request, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Record(jobCode, startedAt, IngestResult.Failed, result: null, request, context);
                throw;
            }

            Record(jobCode, startedAt, result.Status, result, request, context);
            return result;
        }

        /// <summary>
        /// The terminal row, committed <b>synchronously</b> and with
        /// <see cref="CancellationToken.None"/>: the double pins "the step's terminal result was
        /// already recorded when the watchdog's cancellation arrived", and keeps the whole
        /// hand-off inline so the boundary interleaving is deterministic rather than a race.
        /// </summary>
        private void Record(
            string jobCode,
            DateTimeOffset startedAt,
            string status,
            IngestResult? result,
            IngestionRequest request,
            IngestionRunContext context)
        {
            var stats = JsonSerializer.Serialize(new
            {
                written = result?.Written ?? 0,
                unchanged = result?.Unchanged ?? 0,
                quarantined = result?.Quarantined ?? 0,
                skipped = result?.Skipped ?? 0,
                mode = request.IsBackfill ? "backfill" : "incremental",
                attempt = context.Attempt,
                retries = context.Retries,
                maxAttempts = context.MaxAttempts,
            }, StatsJsonOptions);

            using var db = _postgres.CreateContext();
            db.IngestRuns.Add(new IngestRun
            {
                JobCode = jobCode,
                StartedAt = startedAt,
                FinishedAt = _clock.GetUtcNow(),
                Status = status,
                StatsJson = stats,
            });
            db.SaveChanges();
        }
    }
}
