using Degerli.Ingestion.Scheduling;
using Degerli.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Api.Infrastructure;

/// <summary>
/// The shared EOD display-freshness rule for the stock/screener surfaces (SLICE-002, discharging
/// the v1.5.44 follow-up flag): a served as-of date is stale only when it trails the newest session
/// that <b>should have completed</b> by now — the latest trading day strictly before today, resolved
/// through the <see cref="IIngestionCalendar"/> (weekend ∪ configured holiday, TKT-foundation-048).
/// <para>
/// BIST price data is EOD: during a trading Tuesday the freshest completed session is Monday's
/// close, because Tuesday's session closes 18:00 TRT and its EOD chain lands ~20:30. The pre-slice
/// rule (<c>asOf &lt; LatestTradingDayOnOrBefore(today)</c>, weekday-backing only) compared against
/// <i>today</i>, so every trading day read <c>stale: true</c> from midnight until the chain landed —
/// the exact defect the overview's stored-prices doctrine (TC-MOV-032, `01` §10.9.13) already
/// rejects, here applied to the surfaces whose as-of comes from the store itself rather than from a
/// snapshot row.
/// </para>
/// <para>
/// <b>The missed-run signal survives, honestly late.</b> A genuinely missed Monday EOD reads
/// <c>stale: false</c> through Monday night (the watchdog, alerting and the 08:30 morning chain own
/// same-night detection) and <c>stale: true</c> from Tuesday, because Tuesday's expected completed
/// session is Monday. A weekend or configured holiday serves the last completed session fresh —
/// the calendar, never a weekday assumption, resolves it. An empty store keeps the caller's
/// today-fallback with <c>stale: false</c> (these surfaces' pre-existing unavailable posture).
/// </para>
/// </summary>
public static class EodSessionFreshness
{
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
    /// The rule itself: <c>stale = asOf &lt; calendar.LatestTradingDayOnOrBefore(today − 1)</c>.
    /// A null as-of (empty store) keeps the caller's today-fallback and reads fresh — availability
    /// is the <c>state</c> lane's question, never this boolean's.
    /// </summary>
    public static (DateOnly AsOf, bool Stale) AgainstCompletedSession(
        DateOnly? stored,
        IIngestionCalendar calendar,
        TimeProvider clock)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var asOf = stored ?? today;
        var expectedCompletedSession = calendar.LatestTradingDayOnOrBefore(today.AddDays(-1));
        return (asOf, asOf < expectedCompletedSession);
    }
}
