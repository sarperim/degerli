using Degerli.Ingestion.CorporateActions;
using Degerli.Ingestion.Disclosures;
using Degerli.Ingestion.Dividends;
using Degerli.Ingestion.Metrics;
using Degerli.Ingestion.Prices;
using Degerli.Ingestion.Statements;

namespace Degerli.Ingestion.Scheduling;

/// <summary>
/// The pinned order of the daily EOD ingest chain (FR-MDF-009; `03-api-design.md` §9):
/// <c>prices → statements → dividends → corporate-actions → disclosures →
/// metrics-recompute → market-snapshot → medians</c>. The scheduler's shared daily trigger
/// runs exactly these jobs, in this order, on each trading day (TKT-int-001).
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
