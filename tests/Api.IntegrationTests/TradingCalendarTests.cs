using System.Text.Json;
using Degerli.Ingestion.Scheduling;
using Microsoft.Extensions.Options;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-foundation-048 acceptance — <b>TC-MDF-087 (i): the BIST holiday calendar's semantics</b>
/// (FR-MDF-009, FR-MOV-010/016, `01` v1.5.43 §10.9.19; the calendar seam TKT-mdf-005 built and
/// `appsettings.json` shipped empty).
/// <para>
/// The rule is one sentence — <b>a date is non-trading iff it is a weekend or a configured
/// holiday</b> — and the load-bearing consequence is the <b>last completed trading day</b>: the
/// reference every display freshness flag compares against, so a holiday Monday leaves Friday as
/// the session the user is owed. A <b>half day</b> (the BIST "yarım gün" arefe, open until 13:00)
/// is a trading day: it produces a real EOD bar, so it must count.
/// </para>
/// <para>
/// The list is static configuration, so the cases also pin the shipped artifact itself: the
/// official BIST closures for the current and next calendar year, no weekend padding, no
/// duplicate, and the source/retrieval date travelling beside the data. A holiday the list misses
/// is the documented conservative failure (it reads as an ordinary weekday → the data trails →
/// <c>stale</c>), never fabricated data — pinned here as behaviour, not prose.
/// </para>
/// </summary>
public sealed class TradingCalendarTests
{
    // Official BIST closure days used by the calendar semantics below. Half days (2026-03-19,
    // 2026-05-26, 2026-10-28) are deliberately NOT holidays: the market trades until 13:00.
    private const string NewYear2026 = "2026-01-01";
    private const string KurbanWednesday2026 = "2026-05-27";
    private const string KurbanThursday2026 = "2026-05-28";
    private const string KurbanFriday2026 = "2026-05-29";
    private const string RepublicDay2026 = "2026-10-29";

    // ---------------------------------------------------------------------------------
    // (a) A date is non-trading iff weekend ∪ configured holiday
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// TC-MDF-087 (i)(a) — the trading-day predicate: a plain weekday trades, a weekend day
    /// never does (no configuration needed), and a configured holiday does not.
    /// </summary>
    [Fact]
    public void TC_MDF_087_weekend_union_configured_holiday_is_non_trading()
    {
        var calendar = Calendar(NewYear2026, KurbanWednesday2026, KurbanThursday2026, KurbanFriday2026);

        // A plain weekday in the same year trades.
        Assert.True(calendar.IsTradingDay(new DateOnly(2026, 6, 3))); // Wednesday
        Assert.True(calendar.IsTradingDay(new DateOnly(2026, 10, 23))); // Friday

        // Weekend days are non-trading with nothing configured.
        Assert.False(calendar.IsTradingDay(new DateOnly(2026, 10, 24))); // Saturday
        Assert.False(calendar.IsTradingDay(new DateOnly(2026, 10, 25))); // Sunday
        Assert.False(Calendar().IsTradingDay(new DateOnly(2026, 10, 24)));

        // Configured holidays are non-trading on weekdays…
        Assert.False(calendar.IsTradingDay(new DateOnly(2026, 1, 1))); // Thursday
        Assert.False(calendar.IsTradingDay(new DateOnly(2026, 5, 27))); // Wednesday
        Assert.False(calendar.IsTradingDay(new DateOnly(2026, 5, 28))); // Thursday
        Assert.False(calendar.IsTradingDay(new DateOnly(2026, 5, 29))); // Friday

        // …and the surrounding weekdays still trade (the closure is exactly the listed days).
        Assert.True(calendar.IsTradingDay(new DateOnly(2026, 5, 26))); // Tuesday half day — trades
        Assert.True(calendar.IsTradingDay(new DateOnly(2026, 6, 1))); // Monday after the cluster
    }

    /// <summary>
    /// TC-MDF-087 (i)(a′) — the documented conservative failure: a weekday holiday the static
    /// list does not carry reads as an ordinary trading day. That is the accepted risk of a
    /// config list (a missed holiday shows the data trail honestly as <c>stale</c>), and it is
    /// asserted so the behaviour is recorded rather than discovered.
    /// </summary>
    [Fact]
    public void TC_MDF_087_an_unconfigured_holiday_reads_as_a_trading_day()
    {
        var calendar = Calendar(NewYear2026);

        // 2026-10-29 is an official closure; without it in the list the day trades as far as
        // the platform knows — never silently "fresh with no data", because the freshness flag
        // then trails and says so.
        Assert.True(calendar.IsTradingDay(new DateOnly(2026, 10, 29)));
        Assert.False(Calendar(RepublicDay2026).IsTradingDay(new DateOnly(2026, 10, 29)));
    }

    // ---------------------------------------------------------------------------------
    // (b) The last completed trading day
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// TC-MDF-087 (i)(b) — the last completed trading day across the ordinary cases: the
    /// preceding weekday, and the preceding Friday from a weekend day. Today itself is never
    /// "completed": its EOD close lands in the evening, so a Monday morning is owed Friday.
    /// </summary>
    [Fact]
    public void TC_MDF_087_last_completed_trading_day_across_a_weekend()
    {
        var calendar = Calendar();

        // Thursday 2026-10-22 → Wednesday 2026-10-21.
        Assert.Equal(new DateOnly(2026, 10, 21), calendar.LastCompletedTradingDay(new DateOnly(2026, 10, 22)));

        // Saturday → Friday; Sunday → Friday; Monday morning → Friday (today is not completed).
        Assert.Equal(new DateOnly(2026, 10, 23), calendar.LastCompletedTradingDay(new DateOnly(2026, 10, 24)));
        Assert.Equal(new DateOnly(2026, 10, 23), calendar.LastCompletedTradingDay(new DateOnly(2026, 10, 25)));
        Assert.Equal(new DateOnly(2026, 10, 23), calendar.LastCompletedTradingDay(new DateOnly(2026, 10, 26)));
    }

    /// <summary>
    /// TC-MDF-087 (i)(b) — <b>a single holiday Monday: the last completed trading day is
    /// Friday</b>. This is the ticket's pinned example and the case the freshness flag is
    /// resolved from: on the holiday Monday itself and on the following Tuesday morning the
    /// session the user is owed is Friday, not a Monday the market never traded.
    /// </summary>
    [Fact]
    public void TC_MDF_087_a_holiday_monday_leaves_friday_as_the_last_completed_trading_day()
    {
        var holidayMonday = new DateOnly(2026, 10, 19);
        var tuesday = new DateOnly(2026, 10, 20);
        var friday = new DateOnly(2026, 10, 16);
        var calendar = Calendar(holidayMonday.ToString("yyyy-MM-dd"));

        Assert.False(calendar.IsTradingDay(holidayMonday));
        // The holiday itself: the completed session behind it is Friday.
        Assert.Equal(friday, calendar.LastCompletedTradingDay(holidayMonday));
        // The following Tuesday morning: still Friday (Monday never traded, Tuesday is owed).
        Assert.Equal(friday, calendar.LastCompletedTradingDay(tuesday));
        // Once Tuesday completes, Wednesday's reference is Tuesday — the rule self-heals.
        Assert.Equal(tuesday, calendar.LastCompletedTradingDay(new DateOnly(2026, 10, 21)));
    }

    /// <summary>
    /// TC-MDF-087 (i)(b) — <b>a holiday cluster adjacent to the weekend</b> (the Kurban cluster
    /// 2026-05-27…29, Wednesday–Friday): from every day of the closure and its weekend the last
    /// completed trading day is <b>Tuesday the 26th</b> (a half-day session, open until 13:00,
    /// so a real traded day) — the closure plus the weekend is one gap, never a fabricated Friday.
    /// </summary>
    [Fact]
    public void TC_MDF_087_a_holiday_cluster_adjacent_to_the_weekend_walks_past_both()
    {
        var calendar = Calendar(KurbanWednesday2026, KurbanThursday2026, KurbanFriday2026);
        var tuesdayHalfDay = new DateOnly(2026, 5, 26);

        // Friday 29 May is a holiday: the completed session behind it is Tuesday the 26th.
        Assert.Equal(tuesdayHalfDay, calendar.LastCompletedTradingDay(new DateOnly(2026, 5, 29)));
        // Saturday/Sunday of that weekend: the same Tuesday.
        Assert.Equal(tuesdayHalfDay, calendar.LastCompletedTradingDay(new DateOnly(2026, 5, 30)));
        Assert.Equal(tuesdayHalfDay, calendar.LastCompletedTradingDay(new DateOnly(2026, 5, 31)));
        // Monday 1 June: still Tuesday — no Friday, no Monday-of-the-closure.
        Assert.Equal(tuesdayHalfDay, calendar.LastCompletedTradingDay(new DateOnly(2026, 6, 1)));
    }

    /// <summary>
    /// TC-MDF-087 (i)(b) — <b>a half day is a trading day</b>: 2026-05-26 (Tuesday, arefe,
    /// open until 13:00) produces a real EOD bar, so it is the last completed session before
    /// the Kurban closure. Counting it keeps a half-day session from being reported stale.
    /// </summary>
    [Fact]
    public void TC_MDF_087_a_half_day_counts_as_a_completed_trading_day()
    {
        var calendar = Calendar(KurbanWednesday2026, KurbanThursday2026, KurbanFriday2026);

        Assert.True(calendar.IsTradingDay(new DateOnly(2026, 5, 26)));
        Assert.Equal(new DateOnly(2026, 5, 26), calendar.LastCompletedTradingDay(new DateOnly(2026, 5, 27)));
    }

    /// <summary>
    /// TC-MDF-087 (i)(b) — a calendar that cannot resolve a trading day inside the bounded
    /// lookback returns <see langword="null"/> instead of guessing one: the caller then makes
    /// no freshness claim rather than inventing a session.
    /// </summary>
    [Fact]
    public void TC_MDF_087_an_exhausted_lookback_returns_no_last_completed_trading_day()
    {
        var today = new DateOnly(2026, 10, 20);
        var blocked = Enumerable.Range(1, ConfiguredTradingCalendar.MaxTradingDayLookbackDays + 1)
            .Select(back => today.AddDays(-back).ToString("yyyy-MM-dd"))
            .ToArray();

        var calendar = Calendar(blocked);

        Assert.True(calendar.IsTradingDay(today));
        Assert.Null(calendar.LastCompletedTradingDay(today));
    }

    // ---------------------------------------------------------------------------------
    // (c) Configuration is validated, and the shipped list is the official one
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// TC-MDF-087 (i)(c) — a malformed holiday entry fails loudly at composition instead of
    /// silently narrowing the calendar (an unparsed date would otherwise behave as "no
    /// holiday configured"); blank entries are ignored so an operator's trailing comma does
    /// not break startup.
    /// </summary>
    [Fact]
    public void TC_MDF_087_malformed_holiday_configuration_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => Calendar("2026-13-01"));
        Assert.Throws<InvalidOperationException>(() => Calendar("29/10/2026"));

        // Blanks are ignored, not fatal.
        var calendar = Calendar(RepublicDay2026, "  ");
        Assert.False(calendar.IsTradingDay(new DateOnly(2026, 10, 29)));
    }

    /// <summary>
    /// TC-MDF-087 (i)(c) — <b>the shipped <c>appsettings.json</c> carries the official BIST
    /// list, date-stamped with its source</b>: the current and next calendar year are covered,
    /// every entry is a weekday (weekends are inherent, never padded into the list), no date is
    /// duplicated, and the operator note names the refresh path (01 §10.9.19).
    /// </summary>
    [Fact]
    public void TC_MDF_087_shipped_configuration_carries_the_official_date_stamped_list()
    {
        var path = Path.Combine(FindRepositoryRoot(), "src", "Api", "appsettings.json");
        Assert.True(File.Exists(path), $"appsettings.json missing at {path}");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var section = document.RootElement
            .GetProperty("Ingestion")
            .GetProperty("TradingCalendar");

        // Provenance travels with the data: a list without its source and date is unverifiable.
        Assert.False(string.IsNullOrWhiteSpace(section.GetProperty("Source").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(section.GetProperty("Note").GetString()));
        var retrievedOn = section.GetProperty("RetrievedOn").GetString();
        Assert.True(
            DateOnly.TryParseExact(retrievedOn, "yyyy-MM-dd", out _),
            $"RetrievedOn '{retrievedOn}' is not a yyyy-MM-dd date.");

        var holidays = section.GetProperty("Holidays")
            .EnumerateArray()
            .Select(entry => entry.GetString()!)
            .ToArray();

        Assert.NotEmpty(holidays);
        // Exactly the full closure days: every entry is a weekday, and none repeats.
        Assert.All(holidays, value =>
        {
            Assert.True(
                DateOnly.TryParseExact(value, "yyyy-MM-dd", out var date),
                $"Holiday '{value}' is not a yyyy-MM-dd date.");
            Assert.DoesNotContain(
                date.DayOfWeek,
                new[] { DayOfWeek.Saturday, DayOfWeek.Sunday });
        });
        Assert.Equal(holidays.Length, holidays.Distinct(StringComparer.Ordinal).Count());

        // Coverage: the current and next calendar year (the ticket's pin), plus the past year
        // kept for completeness.
        Assert.Contains(holidays, value => value.StartsWith("2026-", StringComparison.Ordinal));
        Assert.Contains(holidays, value => value.StartsWith("2027-", StringComparison.Ordinal));

        // The official BIST 2026 closures (borsaistanbul.com "Resmi Tatil Günleri") — the
        // weekday ones; 21–22 March fell on the weekend and are inherent.
        foreach (var expected in new[]
        {
            "2026-01-01", // Yeni Yıl
            "2026-03-20", // Ramazan Bayramı (the 21st–22nd are the weekend)
            "2026-04-23", // Ulusal Egemenlik ve Çocuk Bayramı
            "2026-05-01", // Emek ve Dayanışma Günü
            "2026-05-19", // Atatürk'ü Anma, Gençlik ve Spor Bayramı
            "2026-05-27", // Kurban Bayramı (the 30th is the weekend)
            "2026-05-28",
            "2026-05-29",
            "2026-07-15", // Demokrasi ve Millî Birlik Günü
            "2026-10-29", // Cumhuriyet Bayramı
        })
        {
            Assert.Contains(expected, holidays);
        }

        // …and the half-day arefes are deliberately absent: the market trades (until 13:00),
        // so the day has a real EOD bar and must never count as a closure.
        Assert.DoesNotContain("2026-03-19", holidays);
        Assert.DoesNotContain("2026-05-26", holidays);
        Assert.DoesNotContain("2026-10-28", holidays);

        // The shipped list composes into a working calendar: the next BIST holiday after the
        // list's own retrieval date is a non-trading day, and the session behind it is a real
        // trading day (the live criterion's world).
        var calendar = new ConfiguredTradingCalendar(Options.Create(new TradingCalendarOptions
        {
            Holidays = holidays.ToList(),
        }));
        Assert.False(calendar.IsTradingDay(new DateOnly(2026, 10, 29)));
        Assert.Equal(new DateOnly(2026, 10, 28), calendar.LastCompletedTradingDay(new DateOnly(2026, 10, 29)));
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    private static ConfiguredTradingCalendar Calendar(params string[] holidays) =>
        new(Options.Create(new TradingCalendarOptions { Holidays = holidays.ToList() }));

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
