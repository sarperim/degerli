namespace Degerli.Ingestion.Scheduling;

/// <summary>
/// Configuration for the C3c scheduler (FR-MDF-009, NFR-MDF-001; architecture §3 C3c,
/// §5 background jobs). Defaults encode the production contract — 20:30 Europe/Istanbul,
/// the 5/15/60-minute retry ladder — so a host that configures nothing still behaves
/// per the requirement.
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

    /// <summary>IANA (or Windows) time-zone id the cron is evaluated in. FR-MDF-009 fixes
    /// this at Europe/Istanbul.</summary>
    public string TimeZone { get; set; } = "Europe/Istanbul";

    /// <summary>
    /// Ordered job codes the daily trigger runs. Empty (the default) means every job
    /// registered through the TKT-mdf-002 convention, in registration order. TKT-int-001
    /// pins the concrete EOD-chain order here; this ticket deliberately does not.
    /// </summary>
    public IList<string> JobCodes { get; set; } = new List<string>();

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
}
