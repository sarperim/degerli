using Degerli.Core.Metrics;
using Degerli.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Ingestion.Metrics;

/// <summary>One computed sector median row for a (sector, metric) pair at one date.</summary>
public sealed record SectorMedianResult(
    long SectorId,
    string MetricCode,
    decimal? MedianValue,
    int PeerCount,
    int ExcludedCount);

/// <summary>
/// C3b sector-median computation for the vs.-sector strip (FR-RES-007/SD-002, `02` §6.3):
/// the median of each of the five valuation-family metrics across same-sector
/// current-universe peers, on the current basis. It reads the canonical
/// <c>derived_metrics</c> rows every other surface serves (BR-RES-004/BR-MDF-009) and
/// performs no formula math of its own.
/// <para>
/// A peer whose metric is not meaningful (NULL — e.g. a P/E for a loss-maker, `02` §6.2)
/// is <b>excluded</b> from the median and counted in <see cref="SectorMedianResult.ExcludedCount"/>,
/// never treated as zero (BR-RES-008); <see cref="SectorMedianResult.PeerCount"/> is the
/// number of peers that contributed to the median (UXR-RES-024/025). Even peer counts use
/// the mean of the two middle values (FU §2 median convention).
/// </para>
/// </summary>
public sealed class SectorMedianEngine
{
    /// <summary>The index whose current constituents form the covered universe.</summary>
    private const string UniverseIndexCode = "XU100";

    /// <summary>The five valuation-family metrics the strip carries (confirmed OQ-UX-002).</summary>
    private static readonly string[] ValuationMetricCodes =
    [
        MetricCodes.Pe,
        MetricCodes.Pb,
        MetricCodes.EvEbitda,
        MetricCodes.EvFcf,
        MetricCodes.FcfYield,
    ];

    private readonly DegerliDbContext _db;

    public SectorMedianEngine(DegerliDbContext db) => _db = db;

    /// <summary>
    /// Computes the sector medians at <paramref name="asOfDate"/> from the canonical
    /// metrics of that date across the current-universe constituents (classified peers only).
    /// </summary>
    public async Task<IReadOnlyList<SectorMedianResult>> ComputeAsync(
        DateOnly asOfDate,
        CancellationToken cancellationToken = default)
    {
        var rows = await (
                from metric in _db.DerivedMetrics
                join instrument in _db.Instruments on metric.InstrumentId equals instrument.Id
                join constituent in _db.IndexConstituents on instrument.Id equals constituent.InstrumentId
                join index in _db.Indices on constituent.IndexId equals index.Id
                where metric.AsOfDate == asOfDate
                    && ValuationMetricCodes.Contains(metric.MetricCode)
                    && index.Code == UniverseIndexCode
                    && constituent.EffectiveTo == null
                    && instrument.SectorId != null
                select new { InstrumentSectorId = instrument.SectorId!.Value, metric.MetricCode, metric.Value })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var results = new List<SectorMedianResult>();
        foreach (var group in rows.GroupBy(row => (row.InstrumentSectorId, row.MetricCode)))
        {
            var values = group
                .Where(row => row.Value is not null)
                .Select(row => row.Value!.Value)
                .OrderBy(value => value)
                .ToList();

            // `values` is the meaningful peer set; `group` also carries same-day rows whose
            // value is NULL (not-meaningful, excluded). A peer with no derived_metrics row at
            // all never joins here, so it contributes to neither count — deliberate: the
            // strip discloses the peers that were *assessed* (UXR-RES-024/025).
            results.Add(new SectorMedianResult(
                group.Key.InstrumentSectorId,
                group.Key.MetricCode,
                Median(values),
                PeerCount: values.Count,
                ExcludedCount: group.Count() - values.Count));
        }

        return results;
    }

    /// <summary>Standard statistical median; even count → the mean of the two middle values (FU §2).</summary>
    private static decimal? Median(IReadOnlyList<decimal> sortedValues)
    {
        if (sortedValues.Count == 0)
        {
            return null;
        }

        var middle = sortedValues.Count / 2;
        return sortedValues.Count % 2 == 1
            ? sortedValues[middle]
            : (sortedValues[middle - 1] + sortedValues[middle]) / 2m;
    }
}
