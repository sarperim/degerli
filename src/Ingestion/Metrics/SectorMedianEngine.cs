using Degerli.Core.Metrics;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
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
/// The write plan for one sector-median pass (<c>01</c> §10.9.13, extended to this lane by
/// TKT-foundation-050): the computed medians whose stored same-day row differs from the
/// current computation — the only rows that need writing — plus the honest counts the run
/// reports. Identical medians are never rewritten and are counted in
/// <see cref="Unchanged"/>; a median whose upstream <c>derived_metrics</c> inputs changed is
/// repaired in place and counted in <see cref="Rewritten"/>.
/// </summary>
/// <param name="ToWrite">The computed medians to upsert (new rows + input-changed rows).</param>
/// <param name="Unchanged">The recomputed medians already identical to their stored row.</param>
/// <param name="Rewritten">The rows repaired in place because their inputs changed.</param>
public sealed record SectorMedianWritePlan(
    IReadOnlyList<SectorMedianResult> ToWrite,
    int Unchanged,
    int Rewritten);

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

    /// <summary>
    /// Classifies a computed median set against the stored same-day rows — the input-aware
    /// half of the recompute (<c>01</c> §10.9.13, extended to this lane by
    /// TKT-foundation-050). A median whose stored values already match the current
    /// computation is reported <c>unchanged</c> and left untouched; a median whose upstream
    /// <c>derived_metrics</c> inputs changed is repaired in place and counted as rewritten.
    /// Only the resolved <paramref name="asOfDate"/>'s rows are addressed, so historical days
    /// stay immutable.
    /// </summary>
    public async Task<SectorMedianWritePlan> PlanWriteAsync(
        IReadOnlyList<SectorMedianResult> computed,
        DateOnly asOfDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(computed);

        var stored = (await _db.SectorMetricMedians
                .Where(median => median.AsOfDate == asOfDate)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(median => (median.SectorId, median.MetricCode), MedianKeyComparer.Instance);

        var toWrite = new List<SectorMedianResult>(computed.Count);
        var unchanged = 0;
        var rewritten = 0;
        foreach (var result in computed)
        {
            if (stored.TryGetValue((result.SectorId, result.MetricCode), out var row))
            {
                if (HasSameValues(row, result))
                {
                    // Identical inputs → identical median: nothing to write, and the run
                    // reports the honest unchanged count instead of a silent vacuum.
                    unchanged++;
                    continue;
                }

                rewritten++;
            }

            toWrite.Add(result);
        }

        return new SectorMedianWritePlan(toWrite, unchanged, rewritten);
    }

    /// <summary>
    /// True when the stored row already carries the computed median. Compares the
    /// input-derived values only — never <c>computed_at</c>, which stamps the write, not the
    /// facts — and compares the median in the stored column's own precision
    /// (<c>numeric(18,6)</c>), so an even-peer-count mean is honestly unchanged on a
    /// recompute instead of a perpetual pseudo-repair.
    /// </summary>
    private static bool HasSameValues(SectorMetricMedian stored, SectorMedianResult computed) =>
        stored.MedianValue == AtStoredScale(computed.MedianValue)
        && stored.PeerCount == computed.PeerCount
        && stored.ExcludedCount == computed.ExcludedCount
        && !stored.IsAdjusted; // the medians are always computed on the current (unadjusted) basis

    /// <summary>The persisted scale of <c>sector_metric_medians.median_value</c> (<c>02</c> §3.4).</summary>
    private const int StoredValueScale = 6;

    /// <summary>Projects a computed median onto the column's precision (half away from zero, like the DB).</summary>
    private static decimal? AtStoredScale(decimal? value) =>
        value is null ? null : decimal.Round(value.Value, StoredValueScale, MidpointRounding.AwayFromZero);

    /// <summary>Value equality over the composite key, so the lookup is ordinal like the DB.</summary>
    private sealed class MedianKeyComparer : IEqualityComparer<(long SectorId, string MetricCode)>
    {
        public static readonly MedianKeyComparer Instance = new();

        public bool Equals((long SectorId, string MetricCode) x, (long SectorId, string MetricCode) y) =>
            x.SectorId == y.SectorId && string.Equals(x.MetricCode, y.MetricCode, StringComparison.Ordinal);

        public int GetHashCode((long SectorId, string MetricCode) obj) =>
            HashCode.Combine(obj.SectorId, StringComparer.Ordinal.GetHashCode(obj.MetricCode));
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
