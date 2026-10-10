using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Ingestion.Metrics;

/// <summary>Outcome of a metric write pass (rides the run ledger's stats).</summary>
public sealed record DerivedMetricWriteResult(int Written, int Unchanged);

/// <summary>
/// Fact storage for the Metrics Engine output (FR-MDF-013, 02 §3.1). The table is
/// append-only by date and idempotent by <c>(instrument, metric, as_of_date)</c>: a
/// re-run for the same date updates the existing rows in place (recompute) rather than
/// inserting duplicates, and never touches another date's rows (NFR-MDF-002).
/// </summary>
public sealed class DerivedMetricStore
{
    private readonly DegerliDbContext _db;

    public DerivedMetricStore(DegerliDbContext db) => _db = db;

    public async Task<DerivedMetricWriteResult> UpsertAsync(
        IReadOnlyList<InstrumentMetricResult> results,
        DateOnly asOfDate,
        DateTimeOffset computedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(results);

        var instrumentIds = await _db.Instruments
            .ToDictionaryAsync(i => i.Symbol, i => i.Id, cancellationToken)
            .ConfigureAwait(false);

        var existing = (await _db.DerivedMetrics
                .Where(m => m.AsOfDate == asOfDate)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(m => (m.InstrumentId, m.MetricCode), MetricKeyComparer.Instance);

        var written = 0;
        var unchanged = 0;
        foreach (var result in results)
        {
            if (!instrumentIds.TryGetValue(result.Symbol, out var instrumentId))
            {
                continue;
            }

            foreach (var metric in result.Metrics)
            {
                var key = (instrumentId, metric.Code);
                if (existing.TryGetValue(key, out var row))
                {
                    row.Value = metric.Value;
                    row.WindowYears = metric.WindowYears;
                    row.IsAdjusted = metric.IsAdjusted;
                    row.IsRested = metric.IsRested;
                    row.ComputedAt = computedAt;
                    unchanged++;
                    continue;
                }

                _db.DerivedMetrics.Add(new DerivedMetric
                {
                    InstrumentId = instrumentId,
                    MetricCode = metric.Code,
                    AsOfDate = asOfDate,
                    Value = metric.Value,
                    WindowYears = metric.WindowYears,
                    IsAdjusted = metric.IsAdjusted,
                    IsRested = metric.IsRested,
                    ComputedAt = computedAt,
                });
                written++;
            }
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new DerivedMetricWriteResult(written, unchanged);
    }

    /// <summary>Value equality over the composite key, so the lookup is ordinal like the DB.</summary>
    private sealed class MetricKeyComparer : IEqualityComparer<(long InstrumentId, string MetricCode)>
    {
        public static readonly MetricKeyComparer Instance = new();

        public bool Equals((long InstrumentId, string MetricCode) x, (long InstrumentId, string MetricCode) y) =>
            x.InstrumentId == y.InstrumentId && string.Equals(x.MetricCode, y.MetricCode, StringComparison.Ordinal);

        public int GetHashCode((long InstrumentId, string MetricCode) obj) =>
            HashCode.Combine(obj.InstrumentId, StringComparer.Ordinal.GetHashCode(obj.MetricCode));
    }
}
