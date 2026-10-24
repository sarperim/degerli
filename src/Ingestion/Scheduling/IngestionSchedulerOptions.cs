namespace Degerli.Ingestion.Scheduling;

/// <summary>
/// Configuration for the C3c scheduler (FR-MDF-009, NFR-MDF-001; architecture §3 C3c,
/// §5 background jobs). The EOD stream and the retry ladder keep the production contract as
/// their code defaults — 20:30 Europe/Istanbul, the 5/15/60-minute ladder — so a host that
/// configures nothing still behaves per the requirement. The morning stream is the one
/// exception: its empty code default is a <b>composition/test</b> contract (no morning stream
/// is created), and production gets the 08:30 cron from the shipped <c>appsettings.json</c>.
/// </summary>
public sealed class IngestionSchedulerOptions
{
    public const string SectionName = "Ingestion:Scheduler";

    /// <summary>Master switch; the hosted service idles when false (tests/CLI).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Cronos cron expression (six fields, seconds included: <c>sec min hour day month dow</c>)
    /// for the EOD trigger. The default fires at 20:30:00 daily; trading-day eligibility is
    /// applied separately by the calendar (FR-MDF-009).
    /// </summary>
    public string Cron { get; set; } = "0 30 20 * * *";

    /// <summary>
    /// Cronos expression (six fields) for the <b>morning chain</b> trigger — the
    /// EOD-repair safety net + incremental KAP freshness pass (01 v1.5 §10.9.16,
    /// TKT-foundation-040). The shipped <c>appsettings.json</c> carries
    /// <c>0 30 8 * * *</c> (08:30 Europe/Istanbul); the morning stream is trading-day
    /// gated, resolves its run date to the <b>previous trading day</b> (BIST price data
    /// is EOD — a Tuesday 08:30 pass can only ever repair Monday's session) and runs the
    /// pinned <see cref="MorningIngestChain"/> with the stored-prices repair gate.
    /// <para>
    /// Empty (the code template default) means <b>no morning stream is created</b>, so a
    /// host or test composition that configures nothing keeps the pre-040 single-stream
    /// behaviour byte-compatible (the acceptance contract of TKT-foundation-040 (iv));
    /// production behaviour comes from the shipped configuration value.
    /// </para>
    /// </summary>
    public string MorningCron { get; set; } = string.Empty;

    /// <summary>IANA (or Windows) time-zone id the cron is evaluated in. FR-MDF-009 fixes
    /// this at Europe/Istanbul.</summary>
    public string TimeZone { get; set; } = "Europe/Istanbul";

    /// <summary>
    /// Ordered job codes the daily trigger runs. Empty (the default) means the pinned EOD
    /// chain (<see cref="EodIngestChain"/> — TKT-int-001), so a host that configures nothing
    /// still runs the cross-domain chain in order. An explicitly configured list (bound from
    /// configuration, appended to this empty default) overrides it.
    /// </summary>
    public IList<string> JobCodes { get; set; } = new List<string>();

    /// <summary>
    /// Independent per-job triggers for jobs that run on their own cadence rather than on
    /// the shared EOD cron — the macro jobs (03 §9): <c>macro-daily</c> by 09:00 each day,
    /// <c>macro-cpi</c> within 24h of each release. Each entry fires only its own job code
    /// and is not gated by the trading calendar (macro data is published every day). Empty
    /// by default, so the EOD behaviour is unchanged for a host that configures nothing.
    /// </summary>
    public IList<JobScheduleOptions> JobSchedules { get; set; } = new List<JobScheduleOptions>();

    /// <summary>
    /// Retry backoff ladder after a failed attempt (NFR-MDF-001: 5/15/60 minutes). The
    /// scheduler makes one initial attempt plus one per delay.
    /// </summary>
    public IList<TimeSpan> RetryDelays { get; set; } = new List<TimeSpan>
    {
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(60),
    };

    /// <summary>Total attempts per scheduled run (initial + retries).</summary>
    public int MaxAttempts => RetryDelays.Count + 1;

    /// <summary>
    /// The <b>per-step</b> bound of the hung-run watchdog (01 v1.5.43 §10.9.17): the longest a
    /// single chain step — one <c>runner.RunAsync(jobCode, …)</c> call inside
    /// <c>ExecuteChainAsync</c> — may take before it is declared hung. The shipped default
    /// (120 minutes) is ≈1.6× the observed healthy corporate-actions walk (58–74 min at ≥12 s
    /// per item) and ≈10× the 12 s item pace, so a slow-but-healthy step is never "hung"; a step
    /// that exceeds it is genuinely stuck, is cancelled, and is named in exactly one
    /// <c>RUN_HUNG</c> ledger row + alert (with <c>bound: "step"</c>). Zero or negative disables
    /// the per-step bound independently of <see cref="MaxRunDuration"/>.
    /// </summary>
    public TimeSpan MaxStepDuration { get; set; } = DefaultMaxStepDuration;

    /// <summary>The shipped per-step bound (120 minutes ≈ 1.6× the healthy 58–74 min step).</summary>
    public static readonly TimeSpan DefaultMaxStepDuration = TimeSpan.FromMinutes(120);

    /// <summary>
    /// The <b>whole-chain ceiling</b> of the hung-run watchdog (01 v1.5 §10.9.17,
    /// TKT-foundation-041; scope corrected by TKT-foundation-064): the longest a single
    /// scheduled attempt may hold the scheduler before the attempt is declared hung. The
    /// production loop awaits one tick to completion and a tick runs a chain's jobs
    /// sequentially, so before this bound existed a chain step whose await never completed
    /// starved <b>every</b> stream silently — no ledger row (the runner records at completion),
    /// no error, no retry, no alert, process alive (the live signature of 2026-10-19/20).
    /// <para>
    /// This is the <b>outer</b> bound, distinct from <see cref="MaxStepDuration"/>: a healthy
    /// paced chain legitimately needs ~100–120 minutes (prices ~5 min, statements ~13 min,
    /// dividends ~16 min, corporate-actions 58–74 min, then disclosures + metrics + snapshot +
    /// medians), so the ceiling is 4 hours — ≈2× the healthy chain, still ahead of the 08:30
    /// morning safety net (01 §10.9.16). A ceiling expiry names the in-flight step and records
    /// <c>bound: "chain"</c>, so an operator can tell it apart from a per-step expiry.
    /// </para>
    /// <para>
    /// On expiry the attempt is cancelled, an honest run-ledger row is written with
    /// <c>reasonCode: RUN_HUNG</c> and the in-flight step named, a <c>RUN_HUNG</c> alert is
    /// raised, and the stream continues on the existing retry ladder. Zero or negative disables
    /// the ceiling (unbounded attempt — not recommended; only for a deliberately long one-off)
    /// independently of <see cref="MaxStepDuration"/>.
    /// </para>
    /// </summary>
    public TimeSpan MaxRunDuration { get; set; } = DefaultMaxRunDuration;

    /// <summary>The shipped whole-chain ceiling (4 hours ≈ 2× the healthy ~100–120 min chain).</summary>
    public static readonly TimeSpan DefaultMaxRunDuration = TimeSpan.FromHours(4);
}

/// <summary>One per-job cron trigger (e.g. a macro cadence).</summary>
public sealed class JobScheduleOptions
{
    /// <summary>The registered job code to run.</summary>
    public string JobCode { get; set; } = string.Empty;

    /// <summary>Cronos cron expression (six fields, seconds included).</summary>
    public string Cron { get; set; } = string.Empty;

    /// <summary>
    /// Whether the trading calendar gates this job's occurrences (TKT-foundation-048, planner
    /// pin 3). <c>false</c> — the default — keeps the pre-048 posture for data published every
    /// day (the macro and fund streams). The intraday sweep is a market stream, so the shipped
    /// configuration sets it to <c>true</c>: on a configured holiday the sweep does not run and
    /// the stream writes one <c>holiday-skip</c> run-ledger row instead of sweeping a closed
    /// market.
    /// </summary>
    public bool TradingDaysOnly { get; set; }
}
