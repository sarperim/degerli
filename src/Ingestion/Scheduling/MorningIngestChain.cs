using Degerli.Ingestion.CorporateActions;
using Degerli.Ingestion.Disclosures;
using Degerli.Ingestion.Dividends;
using Degerli.Ingestion.Metrics;
using Degerli.Ingestion.Prices;
using Degerli.Ingestion.Statements;

namespace Degerli.Ingestion.Scheduling;

/// <summary>
/// The pinned order of the <b>morning chain</b> (01 v1.5 §10.9.16; `03-api-design.md` §9;
/// TKT-foundation-040, completed by TKT-foundation-053) — the sibling of
/// <see cref="EodIngestChain"/> that runs on the scheduler's morning stream.
/// <para>
/// <b>Honest constraint:</b> BIST price data is EOD. The morning stream resolves its run
/// date to the <b>previous trading day</b> (the epoch
/// <see cref="IngestionSchedulerOptions.MorningCron"/> pass on Tuesday targets Monday's
/// session), so this chain is <b>catch-up + KAP freshness, never newer market prices</b>:
/// today's session belongs to the 20:30 EOD chain.
/// </para>
/// <para>
/// The composition: <c>prices (repair-gated) → statements (incremental) →
/// dividends → corporate-actions → disclosures → metrics-recompute → market-snapshot →
/// medians</c>. The <c>prices</c> step is gated by the scheduler on stored coverage — a fully
/// covered day is never re-fetched (the §10.9.13 stored-prices-as-truth doctrine, implemented
/// against <see cref="DailyPriceStore"/>) — so a normal morning costs zero İş Yatırım calls.
/// Every KAP lane rides its per-company cursors (§10.9.11) and is incremental: the morning pass
/// is never a full-universe first pull (§10.9.15's disclosures backfill belongs to the EOD
/// chain). metrics/snapshot/medians recompute idempotently and close the loop so a failed night
/// still yields a served overview row by ~09:00.
/// </para>
/// <para>
/// <b>Phase 2 is wired (TKT-foundation-053).</b> The three KAP event families
/// (<c>dividends → corporate-actions → disclosures</c>, mirroring the EOD order) sat here as a
/// deliberate gap while they failed fast on placeholder KAP paths — a fail-stop that would have
/// poisoned this chain on every morning pass. They became completable members when the VYK
/// rewire (TKT-foundation-034) and the per-constituent identity isolation of
/// TKT-foundation-046 landed: a family lane now completes <c>partial</c> with its honest
/// per-constituent skips counted (<c>COMPANY_IDENTITY_UNRESOLVED</c>), and only a genuinely
/// <c>failed</c> step stops the chain — the same fail-stop/retry ladder as the EOD chain.
/// </para>
/// </summary>
public static class MorningIngestChain
{
    /// <summary>
    /// The ordered morning job codes: prices (repair-gated), statements (incremental), the
    /// three KAP event families (cursor-incremental), metrics-recompute, market-snapshot,
    /// sector-medians.
    /// </summary>
    public static IReadOnlyList<string> JobCodes { get; } = new[]
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
