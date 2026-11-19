using Degerli.Ingestion.CorporateActions;
using Degerli.Ingestion.Disclosures;
using Degerli.Ingestion.Dividends;
using Degerli.Ingestion.Metrics;
using Degerli.Ingestion.Prices;
using Degerli.Ingestion.Statements;
using Degerli.Ingestion.Universe;

namespace Degerli.Ingestion.Scheduling;

/// <summary>
/// The pinned order of the daily EOD ingest chain (FR-MDF-009; `03-api-design.md` §9;
/// updated in SLICE-014):
/// <c>universe-sync → prices → metrics-recompute → market-snapshot → medians → statements →
/// dividends → corporate-actions → disclosures</c>. The scheduler's shared daily trigger
/// runs exactly these jobs, in this order, on each trading day (TKT-int-001).
/// <para>
/// <b><c>universe-sync</c> leads</b> since TKT-foundation-041 (01 v1.5 §10.9.17): the index-level
/// and membership refresh had no schedule of its own (admin trigger / cold start only), so a
/// nightly chain that never ran it left the day's official closes to a manual repair. It runs
/// first because membership changes and the day's index levels then feed prices' constituent
/// resolution and the snapshot's index moves; it is idempotent and costs ~3 cheap İş Yatırım
/// calls, and the once-only launch seed is unaffected by running it nightly.
/// </para>
/// <para>
/// <b>Price-derived computation runs immediately after <c>prices</c></b> (SLICE-014):
/// <c>metrics-recompute</c>, <c>market-snapshot</c>, and <c>medians</c> depend primarily on
/// the day's closing prices and existing statements, whereas the four KAP walks
/// (<c>statements</c>, <c>dividends</c>, <c>corporate-actions</c>, <c>disclosures</c>) take
/// 1–3+ hours combined and already refresh fundamentals throughout the day on their own
/// 2-hourly schedules. Running metrics, market snapshot, and sector medians right after
/// <c>prices</c> prevents slow KAP walks from pushing daily valuation, overview, and screener
/// freshness past <see cref="Degerli.Ingestion.Scheduling"/>'s evening cutoff.
/// </para>
/// <para>
/// The chain is the production default for <see cref="IngestionSchedulerOptions.JobCodes"/>,
/// so a host that configures no explicit order still behaves per the requirement. Jobs on
/// their own cadences (the macro jobs, the fund jobs) are configured through
/// <see cref="IngestionSchedulerOptions.JobSchedules"/> and are deliberately not part of
/// this chain.
/// </para>
/// </summary>
public static class EodIngestChain
{
    /// <summary>The ordered EOD job codes.</summary>
    public static IReadOnlyList<string> JobCodes { get; } = new[]
    {
        UniverseSyncJob.Code,
        PricesJob.Code,
        MetricsRecomputeJob.Code,
        MarketSnapshotJob.Code,
        SectorMediansJob.Code,
        StatementsJob.Code,
        DividendsJob.Code,
        CorporateActionsJob.Code,
        DisclosuresJob.Code,
    };
}
