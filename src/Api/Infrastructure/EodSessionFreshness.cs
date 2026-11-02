using Degerli.Ingestion.Scheduling;
using Degerli.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Api.Infrastructure;

/// <summary>
/// The shared EOD display-freshness rule for the stock/screener surfaces (SLICE-002, discharging
/// the v1.5.44 follow-up flag): a served as-of date is stale only when it trails the newest session
/// that <b>should have completed</b> by now, resolved through the <see cref="IIngestionCalendar"/>
/// (weekend ∪ configured holiday, TKT-foundation-048).
/// <para>
/// BIST price data is EOD: during a trading Tuesday before the EOD chain has finished, the freshest
/// completed session is Monday's close. The pre-slice rule
/// (<c>asOf &lt; LatestTradingDayOnOrBefore(today)</c>, weekday-backing only) compared against
/// <i>today</i>, so every trading day read <c>stale: true</c> from midnight until the chain landed —
/// the exact defect the overview's stored-prices doctrine (TC-MOV-032, `01` §10.9.13) already
/// rejects, here applied to the surfaces whose as-of comes from the store itself rather than from a
/// snapshot row.
/// </para>
/// <para>
/// <b>The expected session follows the chain's completion, not an unconditional yesterday.</b>
/// Before <see cref="DefaultEodCompletionCutoff"/> the newest session that should have completed is
/// the latest trading day strictly before today (Tuesday's EOD has not landed yet). At or after the
/// cutoff the chain has had its normal window, so <i>today</i> (resolved through the calendar) is
/// the expected session: a same-day run that is genuinely missing is reported the same night,
/// instead of waiting for the next trading day. See <see cref="DefaultEodCompletionCutoff"/> for
/// how the instant is derived.
/// </para>
/// <para>
/// <b>Dated supersession (2026-11-02, user arbitration on PR #174 review round 2, G-1).</b> The
/// first cut of this slice shipped the unconditional <c>today − 1</c> expectation and called the
/// missed-run signal "honestly late". The user arbitrated the completion-cutoff shape instead:
/// same-night honesty once the chain should have finished, and no false stale during the legitimate
/// chain window. <c>TC_SCR_010</c> accordingly pins both halves (mid-chain fresh, post-cutoff
/// stale) — its pre-slice expectation was itself a false-alarm expectation at 18:00 UTC, which sits
/// inside the healthy chain run.
/// </para>
/// <para>
/// A weekend or configured holiday serves the last completed session fresh — the calendar, never a
/// weekday assumption, resolves it, before and after the cutoff alike. An empty store keeps the
/// caller's today-fallback with <c>stale: false</c> (these surfaces' pre-existing unavailable
/// posture).
/// </para>
/// </summary>
public static class EodSessionFreshness
{
    /// <summary>
    /// The instant after which the day's EOD chain is expected to have completed, expressed as a
    /// UTC time-of-day: <b>19:45 UTC</b> (22:45 Europe/Istanbul).
    /// <para>
    /// Derived, not guessed: the chain <b>starts</b> at 20:30 Europe/Istanbul = 17:30 UTC
    /// (<see cref="IngestionSchedulerOptions.Cron"/>, <c>0 30 20 * * *</c>), and a healthy paced
    /// chain legitimately needs ~100–120 minutes with disclosures + metrics + snapshot + medians as
    /// the <b>last</b> steps (<see cref="IngestionSchedulerOptions.MaxRunDuration"/>'s own doc), so
    /// same-day metrics land ≈19:10–19:30 UTC. 19:45 UTC leaves retry slack above that and still
    /// precedes the next session comfortably.
    /// </para>
    /// <para>
    /// A UTC time-of-day constant is stable because Europe/Istanbul is a fixed UTC+3 with no DST:
    /// the local instant never shifts under it. Weekday/holiday selection stays with the calendar —
    /// this constant only answers <i>when</i> the day's session becomes expected, never <i>which</i>
    /// day is a trading day.
    /// </para>
    /// </summary>
    public static readonly TimeSpan DefaultEodCompletionCutoff = new(19, 45, 0);

    /// <summary>
    /// The price-backed surfaces' freshness: the as-of is the latest canonical daily-price date;
    /// stale when it trails the newest session that should have completed (`03` §1.2 envelope,
    /// `01` §10.9.13 doctrine).
    /// </summary>
    public static async Task<(DateOnly AsOf, bool Stale)> FromDailyPricesAsync(
        DegerliDbContext db,
        IIngestionCalendar calendar,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var asOf = await db.DailyPrices
            .MaxAsync(price => (DateOnly?)price.PriceDate, cancellationToken);
        return AgainstCompletedSession(asOf, calendar, clock);
    }

    /// <summary>
    /// The metrics-backed surfaces' freshness (the screener, the valuation section): the as-of is
    /// the latest derived-metric date — the metrics recompute rides prices in both chains, so the
    /// same completed-session rule applies to it.
    /// </summary>
    public static async Task<(DateOnly AsOf, bool Stale)> FromDerivedMetricsAsync(
        DegerliDbContext db,
        IIngestionCalendar calendar,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var asOf = await db.DerivedMetrics
            .MaxAsync(metric => (DateOnly?)metric.AsOfDate, cancellationToken);
        return AgainstCompletedSession(asOf, calendar, clock);
    }

    /// <summary>
    /// The rule itself: before <see cref="DefaultEodCompletionCutoff"/> (UTC)
    /// <c>stale = asOf &lt; calendar.LatestTradingDayOnOrBefore(today − 1)</c>; at or after it
    /// <c>stale = asOf &lt; calendar.LatestTradingDayOnOrBefore(today)</c> — the chain has had its
    /// normal run, so the day's own session is expected. A null as-of (empty store) keeps the
    /// caller's today-fallback and reads fresh — availability is the <c>state</c> lane's question,
    /// never this boolean's.
    /// </summary>
    public static (DateOnly AsOf, bool Stale) AgainstCompletedSession(
        DateOnly? stored,
        IIngestionCalendar calendar,
        TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var asOf = stored ?? today;

        // Inclusive: at-or-after the cutoff the day's own session is expected; before it, the
        // latest session strictly before today. The calendar still resolves weekends/holidays.
        var chainHasCompleted = now.UtcDateTime.TimeOfDay >= DefaultEodCompletionCutoff;
        var expectedCompletedSession = calendar.LatestTradingDayOnOrBefore(
            chainHasCompleted ? today : today.AddDays(-1));
        return (asOf, asOf < expectedCompletedSession);
    }
}
