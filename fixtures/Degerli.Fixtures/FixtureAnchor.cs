namespace Degerli.Fixtures;

/// <summary>
/// The time anchor a fixture set is built against (FU §1.4 / §8.3). Exactly two
/// variants exist and both load through <see cref="FixtureUniverse.Build"/>:
/// <list type="bullet">
///   <item><see cref="L2"/> — absolute dates with reference date R = 2026-10-06
///   (Tuesday); assertions may name absolute dates.</item>
///   <item><see cref="NowAnchored"/> — T = the seed day (container start); every
///   fresh row is placed relative to T and tests assert offsets/flags, never
///   wall-clock dates.</item>
/// </list>
/// </summary>
public sealed record FixtureAnchor
{
    /// <summary>The latest fixture trading day, <c>T</c>.</summary>
    public required DateOnly TradingDate { get; init; }

    /// <summary>True for the L4 now-anchored variant.</summary>
    public required bool IsNowAnchored { get; init; }

    /// <summary>The L2 absolute-date anchor: R = 2026-10-06 (FU §2).</summary>
    public static FixtureAnchor L2 { get; } =
        new() { TradingDate = new DateOnly(2026, 10, 6), IsNowAnchored = false };

    /// <summary>The L4 anchor: T = the seed day read from the container clock.</summary>
    public static FixtureAnchor NowAnchored(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        var now = timeProvider.GetUtcNow();
        return new FixtureAnchor
        {
            TradingDate = DateOnly.FromDateTime(now.UtcDateTime),
            IsNowAnchored = true,
        };
    }

    /// <summary>The latest trading day <c>T</c>.</summary>
    public DateOnly T => TradingDate;

    /// <summary><c>T + days</c> as a calendar offset (used for macro/stale rows).</summary>
    public DateOnly Calendar(int days) => TradingDate.AddDays(days);

    /// <summary>
    /// The first day of the month <c>monthsBack</c> months before <c>T</c>'s month
    /// (0 = current month, -1 = previous month).
    /// </summary>
    public DateOnly MonthStart(int monthsBack) =>
        new DateOnly(TradingDate.Year, TradingDate.Month, 1).AddMonths(monthsBack);

    /// <summary>
    /// The <c>offset</c>-th trading day from <c>T</c> (0 = T, -1 = T−1) using the
    /// Mon–Fri fixture calendar (FU §2; no holidays in fixtures).
    /// </summary>
    public DateOnly TradingDay(int offset)
    {
        var date = TradingDate;
        var step = offset <= 0 ? -1 : 1;
        var remaining = Math.Abs(offset);
        while (remaining > 0)
        {
            date = date.AddDays(step);
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                remaining--;
            }
        }

        return date;
    }

    /// <summary>The <paramref name="count"/> trading days ending at T, latest last.</summary>
    public IReadOnlyList<DateOnly> TradingDays(int count)
    {
        var days = new List<DateOnly>(count);
        for (var i = count - 1; i >= 0; i--)
        {
            days.Add(TradingDay(-i));
        }

        return days;
    }

    /// <summary>A fiscal year expressed relative to T (0 = T's year, 1 = prior FY).</summary>
    public int FiscalYear(int yearsBack) => TradingDate.Year - yearsBack;
}
