using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.Scheduling;

/// <summary>
/// Whether a calendar date is a BIST trading day (FR-MDF-009). Kept behind an interface
/// so the holiday calendar is configuration, not code, and so tests can compose a
/// deterministic calendar.
/// </summary>
public interface IIngestionCalendar
{
    /// <summary>Whether <paramref name="date"/> is a trading day (weekday and not a holiday).</summary>
    bool IsTradingDay(DateOnly date);

    /// <summary>
    /// Whether <paramref name="date"/> is a <b>configured market holiday</b> — a weekday closure
    /// named in <see cref="TradingCalendarOptions.Holidays"/>, as opposed to the weekend, which
    /// needs no configuration. The scheduler uses the distinction to explain a holiday skip in
    /// the run ledger while a weekend occurrence stays silent (TKT-foundation-048).
    /// </summary>
    bool IsHoliday(DateOnly date);

    /// <summary>
    /// The latest trading day at or before <paramref name="date"/> — <b>weekend ∪ configured
    /// holiday</b>, walked back, never a weekday assumption (TKT-foundation-048 planner pin 2).
    /// A trading day resolves to itself; a holiday Monday resolves to the preceding trading day
    /// (Friday), and a closure plateau is walked past entirely rather than half-entered.
    /// </summary>
    DateOnly LatestTradingDayOnOrBefore(DateOnly date);
}

/// <summary>
/// Configured trading calendar: weekdays (Mon–Fri) minus the configured holiday set.
/// The holiday list is data (FU §2 records the fixture interpretation: Mon–Fri, no
/// holidays); production supplies the official BIST holiday calendar via configuration.
/// </summary>
public sealed class TradingCalendarOptions
{
    public const string SectionName = "Ingestion:TradingCalendar";

    /// <summary>Market holidays as <c>yyyy-MM-dd</c> strings (culture-invariant).</summary>
    public IList<string> Holidays { get; set; } = new List<string>();
}

/// <inheritdoc />
public sealed class ConfiguredTradingCalendar : IIngestionCalendar
{
    /// <summary>
    /// How far the trading-day walk may go back before the calendar is declared unusable. The
    /// longest BIST closure (a nine-day religious holiday) is well inside a year, so a full year
    /// is generous enough to never trip on a real calendar.
    /// </summary>
    private const int MaxHolidayPlateauDays = 366;

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
    public bool IsHoliday(DateOnly date) => _holidays.Contains(date);

    /// <inheritdoc />
    public DateOnly LatestTradingDayOnOrBefore(DateOnly date)
    {
        // Bounded by a full year: a calendar with no trading day at all is a configuration
        // error, and failing loudly beats an unbounded walk (or a silently invented date).
        var candidate = date;
        for (var back = 0; back <= MaxHolidayPlateauDays; back++)
        {
            if (IsTradingDay(candidate))
            {
                return candidate;
            }

            candidate = candidate.AddDays(-1);
        }

        throw new InvalidOperationException(
            $"No trading day found within {MaxHolidayPlateauDays} days at or before {date:yyyy-MM-dd}; " +
            "check Ingestion:TradingCalendar:Holidays.");
    }
}
