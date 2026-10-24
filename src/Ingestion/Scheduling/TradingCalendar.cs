using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.Scheduling;

/// <summary>
/// Whether a calendar date is a BIST trading day (FR-MDF-009). Kept behind an interface
/// so the holiday calendar is configuration, not code, and so tests can compose a
/// deterministic calendar.
/// </summary>
/// <remarks>
/// <b>The holiday calendar is static, date-stamped configuration</b> (01 v1.5.43 §10.9.19,
/// TKT-foundation-048): the BIST official trading calendar is a small, yearly, publicly
/// published list, so it ships in <c>appsettings.json</c> with its source and retrieval date
/// beside it — never a live scrape (no known paced endpoint; a source probe is the parked
/// hardening item). The consequence is stated honestly rather than hidden: a holiday the list
/// does not carry behaves as an ordinary weekday, which surfaces as the <b>conservative</b>
/// failure (a snapshot that trails and reads <c>stale</c>), never as fabricated data.
/// </remarks>
public interface IIngestionCalendar
{
    /// <summary>True when the market trades on <paramref name="date"/> (weekday and not a configured holiday).</summary>
    bool IsTradingDay(DateOnly date);

    /// <summary>
    /// The last trading day <b>strictly before</b> <paramref name="today"/> — the most recent
    /// session that has completed by the start of <paramref name="today"/>, or
    /// <see langword="null"/> when the configured calendar carries no trading day in the
    /// lookback window.
    /// </summary>
    /// <remarks>
    /// This is the reference a display freshness flag compares its served as-of date against
    /// (FR-MOV-010/016, 01 §10.9.19): on a Tuesday whose Monday was a BIST holiday the last
    /// completed session is Friday, so Friday's data is fresh — a holiday is a day the market
    /// did not trade, never a session the platform failed to fetch. <c>today</c> itself is
    /// always excluded: the current session's EOD close lands in the evening, so it can never
    /// be "completed" when the flag is computed.
    /// </remarks>
    DateOnly? LastCompletedTradingDay(DateOnly today);
}

/// <summary>
/// Configured trading calendar: weekdays (Mon–Fri) minus the configured holiday set.
/// The holiday list is data (FU §2 records the fixture interpretation: Mon–Fri, no
/// holidays); production supplies the official BIST holiday calendar via configuration.
/// </summary>
/// <remarks>
/// <b>Half-day sessions are trading days</b> (the BIST "yarım gün" arefes: the market is open
/// until 13:00), so they are deliberately <b>not</b> in <see cref="Holidays"/> — only the full
/// closure days are. A half-day still produces an EOD bar and must therefore satisfy the
/// equality checks the freshness rules make.
/// </remarks>
public sealed class TradingCalendarOptions
{
    public const string SectionName = "Ingestion:TradingCalendar";

    /// <summary>Market holidays as <c>yyyy-MM-dd</c> strings (culture-invariant).</summary>
    public IList<string> Holidays { get; set; } = new List<string>();

    /// <summary>
    /// Where the shipped <see cref="Holidays"/> list came from (01 §10.9.19). Operator-facing
    /// provenance: the list is only as good as its source and its date, so both travel with it
    /// in configuration instead of living in a commit message.
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>When the shipped list was retrieved/verified (<c>yyyy-MM-dd</c>).</summary>
    public string RetrievedOn { get; set; } = string.Empty;

    /// <summary>How and when to refresh the list, and which years it covers (operator note).</summary>
    public string Note { get; set; } = string.Empty;
}

/// <inheritdoc />
public sealed class ConfiguredTradingCalendar : IIngestionCalendar
{
    /// <summary>
    /// How far <see cref="LastCompletedTradingDay"/> walks back before giving up. Generous
    /// enough for the longest BIST closure (the 9-day Kurban cluster plus its weekend) and
    /// small enough to stay a bounded query.
    /// </summary>
    public const int MaxTradingDayLookbackDays = 21;

    private readonly HashSet<DateOnly> _holidays;

    public ConfiguredTradingCalendar(IOptions<TradingCalendarOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _holidays = new HashSet<DateOnly>();
        foreach (var value in options.Value.Holidays)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", out var holiday))
            {
                throw new InvalidOperationException(
                    $"TradingCalendar holiday '{value}' is not a valid yyyy-MM-dd date.");
            }

            _holidays.Add(holiday);
        }
    }

    /// <inheritdoc />
    public bool IsTradingDay(DateOnly date) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
        && !_holidays.Contains(date);

    /// <inheritdoc />
    public DateOnly? LastCompletedTradingDay(DateOnly today)
    {
        for (var back = 1; back <= MaxTradingDayLookbackDays; back++)
        {
            var candidate = today.AddDays(-back);
            if (IsTradingDay(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
