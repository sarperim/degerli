using System.Text.Json;
using Degerli.Ingestion.Scheduling;
using Microsoft.Extensions.Options;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-foundation-048 acceptance (i) — <b>TC-MDF-087</b>: the BIST trading calendar is
/// <c>weekend ∪ configured holiday</c>, and the last trading day is resolved by walking that
/// same set back — never by a weekday assumption.
/// <para>
/// The calendar is pure date arithmetic over configuration (<see cref="TradingCalendarOptions"/>),
/// so this class needs no container and no clock: the cases pin the semantics the serving
/// freshness rule and the scheduler's trading-day gate both consume (planner pins 1–2 of the
/// ticket: a static, source-traceable holiday list — no live calendar source — and the
/// "<c>weekend ∪ configured holiday</c>" honesty rule).
/// </para>
/// <para>
/// The date sets are the <b>official BIST publication</b> (Borsa İstanbul, "Resmi Tatil Günleri",
/// Pay Piyasası tables for 2025/2026): full-closure weekdays only — a closure that falls on a
/// Saturday/Sunday is already non-trading by the weekend rule, and an <i>arefe</i> (half day)
/// is a trading session and is deliberately <b>not</b> a holiday date.
/// </para>
/// </summary>
public sealed class TradingCalendarTests
{
    /// <summary>The 2025 BIST full-closure weekdays (Pay Piyasası 2025 tatil tablosu).</summary>
    private static readonly DateOnly[] Bist2025Closures =
    [
        new(2025, 1, 1),   // Yılbaşı (Wed)
        new(2025, 3, 31),  // Ramazan Bayramı (Mon)
        new(2025, 4, 1),   // Ramazan Bayramı (Tue)
        new(2025, 4, 23),  // Ulusal Egemenlik ve Çocuk Bayramı (Wed)
        new(2025, 5, 1),   // Emek ve Dayanışma Günü (Thu)
        new(2025, 5, 19),  // Atatürk'ü Anma, Gençlik ve Spor Bayramı (Mon)
        new(2025, 6, 6),   // Kurban Bayramı (Fri)
        new(2025, 6, 9),   // Kurban Bayramı (Mon)
        new(2025, 7, 15),  // Demokrasi ve Millî Birlik Günü (Tue)
        new(2025, 10, 29), // Cumhuriyet Bayramı (Wed)
    ];

    /// <summary>
    /// The 2026 closures still ahead of the seeding date (2026-10-24) — the ticket pins
    /// "2025 + remaining 2026", so earlier 2026 closures are not re-seeded here.
    /// </summary>
    private static readonly DateOnly[] Bist2026RemainingClosures =
    [
        new(2026, 10, 29), // Cumhuriyet Bayramı (Thu)
    ];

    // ---------------------------------------------------------------------------------
    // TC-MDF-087 (i) — weekend ∪ configured holiday
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// TC-MDF-087 (i) — a configured holiday weekday is <b>non-trading</b> while its neighbours
    /// are trading days, and Saturday/Sunday are non-trading with or without configuration.
    /// </summary>
    [Fact]
    public void TC_MDF_087_weekend_and_configured_holiday_are_non_trading_days()
    {
        var calendar = Calendar("2026-10-29");

        // Weekend: non-trading with no configuration at all.
        Assert.False(calendar.IsTradingDay(new DateOnly(2026, 10, 24))); // Saturday
        Assert.False(calendar.IsTradingDay(new DateOnly(2026, 10, 25))); // Sunday

        // The configured holiday (Cumhuriyet Bayramı, Thursday 2026-10-29) is the only
        // non-trading weekday inside that week: its neighbours all trade.
        Assert.False(calendar.IsTradingDay(new DateOnly(2026, 10, 29)));
        Assert.True(calendar.IsTradingDay(new DateOnly(2026, 10, 26))); // Monday
        Assert.True(calendar.IsTradingDay(new DateOnly(2026, 10, 27))); // Tuesday
        Assert.True(calendar.IsTradingDay(new DateOnly(2026, 10, 28))); // Wednesday — arefe, half day: trades
        Assert.True(calendar.IsTradingDay(new DateOnly(2026, 10, 30))); // Friday
    }

    /// <summary>
    /// TC-MDF-087 (i) — the same weekday is trading or not <b>purely by configuration</b>: the
    /// holiday list is data, so an empty list is the pure weekday calendar (the pre-048 posture)
    /// and the list is what makes a weekday non-trading.
    /// </summary>
    [Fact]
    public void TC_MDF_087_holidays_are_configuration_and_empty_means_weekdays_only()
    {
        var unconfigured = Calendar();
        var configured = Calendar("2026-10-29");

        Assert.True(unconfigured.IsTradingDay(new DateOnly(2026, 10, 29)));
        Assert.False(configured.IsTradingDay(new DateOnly(2026, 10, 29)));

        // The weekend rule is not configuration: both calendars agree there.
        Assert.False(unconfigured.IsTradingDay(new DateOnly(2026, 10, 24)));
        Assert.False(configured.IsTradingDay(new DateOnly(2026, 10, 24)));
    }

    // ---------------------------------------------------------------------------------
    // TC-MDF-087 (ii) — last trading day resolution
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// TC-MDF-087 (ii) — across a weekend the last trading day backs to Friday; a date that is
    /// itself a trading day resolves to itself (the ordinary case the serving rules rely on).
    /// </summary>
    [Fact]
    public void TC_MDF_087_last_trading_day_across_a_weekend_is_friday()
    {
        var calendar = Calendar("2026-10-29");
        var friday = new DateOnly(2026, 10, 23);

        Assert.Equal(friday, calendar.LatestTradingDayOnOrBefore(friday));
        Assert.Equal(friday, calendar.LatestTradingDayOnOrBefore(new DateOnly(2026, 10, 24))); // Saturday
        Assert.Equal(friday, calendar.LatestTradingDayOnOrBefore(new DateOnly(2026, 10, 25))); // Sunday
    }

    /// <summary>
    /// TC-MDF-087 (ii) — <b>a holiday Monday resolves to the preceding Friday</b> (the ticket's
    /// pinned example): the holiday is skipped exactly like a weekend, so "the last trading day
    /// before Monday" is Friday and not Sunday.
    /// </summary>
    [Fact]
    public void TC_MDF_087_last_trading_day_before_a_holiday_monday_is_friday()
    {
        // Monday 2026-10-05 is configured as a holiday for this world (the fixture anchor's
        // preceding weekday; production supplies the real publication dates).
        var calendar = Calendar("2026-10-05");

        Assert.False(calendar.IsTradingDay(new DateOnly(2026, 10, 5)));
        Assert.Equal(new DateOnly(2026, 10, 2), calendar.LatestTradingDayOnOrBefore(new DateOnly(2026, 10, 5)));

        // …and the holiday-adjacent weekend backs to the same Friday, not to the Sunday.
        Assert.Equal(new DateOnly(2026, 10, 2), calendar.LatestTradingDayOnOrBefore(new DateOnly(2026, 10, 4)));
        Assert.Equal(new DateOnly(2026, 10, 2), calendar.LatestTradingDayOnOrBefore(new DateOnly(2026, 10, 3)));
    }

    /// <summary>
    /// TC-MDF-087 (ii) — a <b>holiday plateau</b> reaching into a weekend (the real Kurban Bayramı
    /// 2026 shape: Wednesday 27 / Thursday 28 / Friday 29 May closures, then the weekend) walks
    /// back to the last trading day <b>before</b> the whole closure — Tuesday 2026-05-26, the arefe
    /// that still traded — never into the middle of it.
    /// </summary>
    [Fact]
    public void TC_MDF_087_last_trading_day_before_a_holiday_plateau_is_before_the_plateau()
    {
        var calendar = Calendar("2026-05-27", "2026-05-28", "2026-05-29");
        var tuesday = new DateOnly(2026, 5, 26);

        // The plateau itself, and the weekend it runs into, all resolve to the arefe that
        // traded: never into the middle of the closure and never past it.
        Assert.Equal(tuesday, calendar.LatestTradingDayOnOrBefore(new DateOnly(2026, 5, 31))); // Sunday
        Assert.Equal(tuesday, calendar.LatestTradingDayOnOrBefore(new DateOnly(2026, 5, 30))); // Saturday
        Assert.Equal(tuesday, calendar.LatestTradingDayOnOrBefore(new DateOnly(2026, 5, 29))); // Friday closure
        Assert.Equal(tuesday, calendar.LatestTradingDayOnOrBefore(new DateOnly(2026, 5, 27))); // Wednesday closure

        // …and the first trading day after the plateau resolves to itself, not backwards.
        Assert.Equal(new DateOnly(2026, 6, 1), calendar.LatestTradingDayOnOrBefore(new DateOnly(2026, 6, 1)));
    }

    /// <summary>
    /// TC-MDF-087 (ii) — a <b>single</b> holiday adjacent to a weekend: Sunday 2025-06-08 (the
    /// Kurban Bayramı weekend) and Monday 2025-06-09 (Kurban Bayramı — a configured closure) both
    /// resolve to Friday 2025-06-06, which is itself a closure in that year's table: resolution
    /// walks past every configured non-trading date, not just the first weekend it meets.
    /// </summary>
    [Fact]
    public void TC_MDF_087_last_trading_day_walks_past_consecutive_closures()
    {
        var calendar = Calendar(Bist2025Closures.Select(date => date.ToString("yyyy-MM-dd")).ToArray());

        // 6-7-8-9 June 2025 (Fri-Sat-Sun-Mon) were all closed → Thursday 5 June traded.
        Assert.Equal(new DateOnly(2025, 6, 5), calendar.LatestTradingDayOnOrBefore(new DateOnly(2025, 6, 9)));
        Assert.Equal(new DateOnly(2025, 6, 5), calendar.LatestTradingDayOnOrBefore(new DateOnly(2025, 6, 8)));
        Assert.Equal(new DateOnly(2025, 6, 5), calendar.LatestTradingDayOnOrBefore(new DateOnly(2025, 6, 7)));
        Assert.Equal(new DateOnly(2025, 6, 5), calendar.LatestTradingDayOnOrBefore(new DateOnly(2025, 6, 6)));
    }

    // ---------------------------------------------------------------------------------
    // Configuration posture — the shipped list is the source-traceable BIST publication
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// TC-MDF-087 (iii) — the shipped <c>src/Api/appsettings.json</c> carries the seeded BIST
    /// publication dates and nothing else: every entry is a weekday (a weekend closure is
    /// already non-trading, so it must not be seeded), no duplicates, strictly <c>yyyy-MM-dd</c>,
    /// 2025 complete and the remaining 2026 closures present. A missed holiday is the accepted
    /// honest failure (it reads <c>stale</c>); an <i>invented</i> one would hide a real gap, so
    /// the list is pinned exactly.
    /// </summary>
    [Fact]
    public void TC_MDF_087_shipped_holiday_list_is_the_bist_publication()
    {
        var shipped = ShippedHolidays();

        Assert.All(shipped, date => Assert.DoesNotContain(
            date.DayOfWeek,
            new[] { DayOfWeek.Saturday, DayOfWeek.Sunday }));
        Assert.Equal(shipped.Count, shipped.Distinct().Count());

        Assert.Equal(
            Bist2025Closures.OrderBy(date => date),
            shipped.Where(date => date.Year == 2025).OrderBy(date => date));
        Assert.Equal(
            Bist2026RemainingClosures.OrderBy(date => date),
            shipped.Where(date => date.Year == 2026).OrderBy(date => date));

        // The arefe (half-day) dates are trading sessions, never seeded as holidays.
        Assert.DoesNotContain(new DateOnly(2026, 10, 28), shipped);
        Assert.DoesNotContain(new DateOnly(2025, 10, 28), shipped);
        Assert.DoesNotContain(new DateOnly(2025, 6, 5), shipped);

        // The shipped list is what production actually reads: composing it through the same
        // options shape the container binds keeps this pin honest.
        var calendar = Calendar(shipped.Select(date => date.ToString("yyyy-MM-dd")).ToArray());
        Assert.All(shipped, date => Assert.False(calendar.IsTradingDay(date)));
        Assert.Equal(new DateOnly(2026, 10, 28), calendar.LatestTradingDayOnOrBefore(new DateOnly(2026, 10, 29)));
    }

    /// <summary>
    /// TC-MDF-087 (iii) — the intraday sweep stream is <b>holiday gated</b> in the shipped
    /// configuration (planner pin 3: a holiday must skip the stream, and the skip is a named
    /// ledger row rather than silence) while the macro/fund streams keep their daily cadence.
    /// </summary>
    [Fact]
    public void TC_MDF_087_shipped_intraday_stream_is_trading_day_gated()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Api", "appsettings.json")));
        var schedules = document.RootElement
            .GetProperty("Ingestion")
            .GetProperty("Scheduler")
            .GetProperty("JobSchedules")
            .EnumerateArray()
            .ToList();

        var intraday = schedules.Single(entry => entry.GetProperty("JobCode").GetString() == "intraday-quotes");
        Assert.True(intraday.GetProperty("TradingDaysOnly").GetBoolean());

        // Macro and fund data are published every day: those streams stay ungated (the default).
        foreach (var code in new[] { "macro-daily", "macro-cpi", "fund-nav" })
        {
            var schedule = schedules.Single(entry => entry.GetProperty("JobCode").GetString() == code);
            Assert.False(schedule.TryGetProperty("TradingDaysOnly", out var flag) && flag.GetBoolean());
        }
    }

    /// <summary>
    /// A malformed configured date fails at composition (never silently ignored): a typo must
    /// not quietly widen the trading calendar.
    /// </summary>
    [Fact]
    public void TC_MDF_087_malformed_configured_holiday_is_rejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Calendar("29.10.2026"));
        Assert.Contains("29.10.2026", exception.Message, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    private static ConfiguredTradingCalendar Calendar(params string[] holidays) =>
        new(Options.Create(new TradingCalendarOptions { Holidays = holidays.ToList() }));

    private static IReadOnlyList<DateOnly> ShippedHolidays()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Api", "appsettings.json")));
        return document.RootElement
            .GetProperty("Ingestion")
            .GetProperty("TradingCalendar")
            .GetProperty("Holidays")
            .EnumerateArray()
            .Select(entry => DateOnly.ParseExact(entry.GetString()!, "yyyy-MM-dd"))
            .ToList();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Degerli.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root (Degerli.sln).");
    }
}
