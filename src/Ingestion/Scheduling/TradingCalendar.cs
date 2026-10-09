using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.Scheduling;

/// <summary>
/// Whether a calendar date is a BIST trading day (FR-MDF-009). Kept behind an interface
/// so the holiday calendar is configuration, not code, and so tests can compose a
/// deterministic calendar.
/// </summary>
public interface IIngestionCalendar
{
    bool IsTradingDay(DateOnly date);
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
}
