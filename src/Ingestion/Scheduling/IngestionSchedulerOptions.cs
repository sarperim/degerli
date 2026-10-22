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
    /// The <b>hung-run watchdog</b> bound (01 v1.5 §10.9.17, TKT-foundation-041): the longest a
    /// single scheduled attempt may hold the scheduler before it is declared hung. The
    /// production loop awaits one tick to completion and a tick runs a chain's jobs
    /// sequentially, so before this bound existed a chain step whose await never completed
    /// starved <b>every</b> stream silently — no ledger row (the runner records at completion),
    /// no error, no retry, no alert, process alive (the live signature of 2026-10-19/20).
    /// <para>
    /// On expiry the attempt is cancelled, an honest run-ledger row is written with
    /// <c>reasonCode: RUN_HUNG</c> and the hung step named, a <c>RUN_HUNG</c> alert is raised,
    /// and the stream continues on the existing retry ladder. The default (90 minutes) is
    /// ~2.5× the observed full-chain duration (prices ≈ 7 min + statements ≈ 25 min on the dev
    /// stack) — generous headroom for a slow night, far short of the overnight window, and well
    /// before the 08:30 morning safety net (01 §10.9.16). Zero or negative disables the
    /// watchdog (unbounded await — not recommended; only for a deliberately long one-off).
    /// </para>
    /// </summary>
    public TimeSpan MaxRunDuration { get; set; } = TimeSpan.FromMinutes(90);
}

/// <summary>One per-job cron trigger (e.g. a macro cadence).</summary>
public sealed class JobScheduleOptions
{
    /// <summary>The registered job code to run.</summary>
    public string JobCode { get; set; } = string.Empty;

    /// <summary>Cronos cron expression (six fields, seconds included).</summary>
    public string Cron { get; set; } = string.Empty;
}
