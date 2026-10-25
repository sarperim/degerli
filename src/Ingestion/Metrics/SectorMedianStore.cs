using Degerli.Core.Metrics;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Ingestion.Metrics;

/// <summary>Outcome of a sector-median write pass (rides the run ledger's stats).</summary>
public sealed record SectorMedianWriteResult(int Written, int Unchanged);

/// <summary>
/// Fact storage for the sector-median computation (FR-RES-007, `02` §3.4). Like
/// <see cref="DerivedMetricStore"/> the write is idempotent by its composite key
/// <c>(sector, metric, as_of_date)</c>: a re-run for the same date updates the existing rows
/// in place rather than inserting duplicates, and never touches another date's rows.
/// <para>
/// Every row written — insert or repair — also carries the producing definition's
/// <c>formula_version</c> (<see cref="CanonicalMetrics.Version"/>, TKT-foundation-070), the
/// same provenance discipline as <see cref="DerivedMetricStore"/>.
/// </para>
/// </summary>
public sealed class SectorMedianStore
{
    private readonly DegerliDbContext _db;

    public SectorMedianStore(DegerliDbContext db) => _db = db;

    public async Task<SectorMedianWriteResult> UpsertAsync(
        IReadOnlyList<SectorMedianResult> results,
        DateOnly asOfDate,
        DateTimeOffset computedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(results);

        var existing = (await _db.SectorMetricMedians
                .Where(median => median.AsOfDate == asOfDate)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(median => (median.SectorId, median.MetricCode), MedianKeyComparer.Instance);

        var written = 0;
        var unchanged = 0;
        foreach (var result in results)
        {
            var key = (result.SectorId, result.MetricCode);
            if (existing.TryGetValue(key, out var row))
            {
                row.MedianValue = result.MedianValue;
                row.PeerCount = result.PeerCount;
                row.ExcludedCount = result.ExcludedCount;
                row.IsAdjusted = false;
                // A repair restates the definition too (TKT-foundation-070).
                row.FormulaVersion = CanonicalMetrics.Version;
                row.ComputedAt = computedAt;
                unchanged++;
                continue;
            }

            _db.SectorMetricMedians.Add(new SectorMetricMedian
            {
                SectorId = result.SectorId,
                MetricCode = result.MetricCode,
                AsOfDate = asOfDate,
                MedianValue = result.MedianValue,
                PeerCount = result.PeerCount,
                ExcludedCount = result.ExcludedCount,
                IsAdjusted = false,
                FormulaVersion = CanonicalMetrics.Version,
                ComputedAt = computedAt,
            });
            written++;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new SectorMedianWriteResult(written, unchanged);
    }

    /// <summary>Value equality over the composite key, so the lookup is ordinal like the DB.</summary>
    private sealed class MedianKeyComparer : IEqualityComparer<(long SectorId, string MetricCode)>
    {
        public static readonly MedianKeyComparer Instance = new();

        public bool Equals((long SectorId, string MetricCode) x, (long SectorId, string MetricCode) y) =>
            x.SectorId == y.SectorId && string.Equals(x.MetricCode, y.MetricCode, StringComparison.Ordinal);

        public int GetHashCode((long SectorId, string MetricCode) obj) =>
            HashCode.Combine(obj.SectorId, StringComparer.Ordinal.GetHashCode(obj.MetricCode));
    }
}
