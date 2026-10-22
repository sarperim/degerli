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
