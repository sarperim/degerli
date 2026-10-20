using Degerli.Ingestion.CorporateActions;
using Degerli.Ingestion.Disclosures;
using Degerli.Ingestion.Dividends;
using Degerli.Ingestion.Metrics;
using Degerli.Ingestion.Prices;
using Degerli.Ingestion.Statements;

namespace Degerli.Ingestion.Scheduling;

/// <summary>
/// The pinned order of the <b>morning chain</b> (01 v1.5 §10.9.16; `03-api-design.md` §9;
/// TKT-foundation-040) — the sibling of <see cref="EodIngestChain"/> that runs on the
/// scheduler's morning stream.
/// <para>
/// <b>Honest constraint:</b> BIST price data is EOD. The morning stream resolves its run
/// date to the <b>previous trading day</b> (the epoch
/// <see cref="IngestionSchedulerOptions.MorningCron"/> pass on Tuesday targets Monday's
/// session), so this chain is <b>catch-up + KAP freshness, never newer market prices</b>:
/// today's session belongs to the 20:30 EOD chain.
/// </para>
/// <para>
/// Phase 1 (shipped): <c>prices (repair-gated) → statements (incremental) →
/// metrics-recompute → market-snapshot → medians</c>. The <c>prices</c> step is gated by the
/// scheduler on stored coverage — a fully covered day is never re-fetched (the §10.9.13
/// stored-prices-as-truth doctrine, implemented against <see cref="DailyPriceStore"/>) — so a
/// normal morning costs zero İş Yatırım calls; <c>statements</c> rides its per-company
/// cursors (§10.9.11) and is incremental; metrics/snapshot/medians recompute idempotently and
/// close the loop so a failed night still yields a served overview row by ~09:00.
/// </para>
/// <para>
/// <b>Phase 2 seam (gated on TKT-foundation-034 / PR #106):</b> the three KAP event families
/// (<c>dividends → corporate-actions → disclosures</c>, mirroring the EOD order) join the
/// morning chain between <c>statements</c> and <c>metrics-recompute</c> once their VYK rewire
/// and per-family cursors land. They are deliberately <b>not</b> members today: on the current
/// tree they fail fast on their placeholder KAP paths (live-verified), which would poison this
/// chain's fail-stop on every morning pass. The post-034 composition is pinned in
/// <see cref="Phase2JobCodes"/> so the wiring point is a one-line change in the scheduler's
/// stream construction, never a re-derivation of the order.
/// </para>
/// </summary>
public static class MorningIngestChain
{
    /// <summary>
    /// The ordered morning job codes as shipped (phase 1): prices (repair-gated),
    /// statements, metrics-recompute, market-snapshot, medians.
    /// </summary>
    public static IReadOnlyList<string> JobCodes { get; } = new[]
    {
        PricesJob.Code,
        StatementsJob.Code,
        MetricsRecomputeJob.Code,
        MarketSnapshotJob.Code,
        SectorMediansJob.Code,
    };

    /// <summary>
    /// The phase-2 event-family jobs (dividends, corporate-actions, disclosures) — members of
    /// the morning chain only after TKT-foundation-034 lands. Not wired into
    /// <see cref="JobCodes"/> today (see the class remarks).
    /// </summary>
    public static IReadOnlyList<string> EventFamilyJobCodes { get; } = new[]
    {
        DividendsJob.Code,
        CorporateActionsJob.Code,
        DisclosuresJob.Code,
    };

    /// <summary>
    /// The phase-2 composition (the post-TKT-foundation-034 order): the three event families
    /// spliced between statements and metrics-recompute, exactly as
    /// <see cref="EodIngestChain"/> orders them. Deliberately not the scheduler's wired list
    /// yet — flipping the morning stream to this list is the whole phase-2 change.
    /// </summary>
    public static IReadOnlyList<string> Phase2JobCodes { get; } = new[]
    {
        PricesJob.Code,
        StatementsJob.Code,
        DividendsJob.Code,
        CorporateActionsJob.Code,
        DisclosuresJob.Code,
        MetricsRecomputeJob.Code,
        MarketSnapshotJob.Code,
        SectorMediansJob.Code,
    };
}
