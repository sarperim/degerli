using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Ingestion.Macro;

/// <summary>
/// Fact storage for <c>macro_values</c> (FR-MOV-014, FR-MDF-015; 02 §1 principles 1–2,
/// §5.6). Writes are append-only and idempotent: a re-send of an identical
/// (series, value_date, recorded_at) row is a no-op, while a *revision* — a new value for an
/// existing value_date with a later <c>recorded_at</c> — appends a new row and never
/// mutates the stored one. The canonical value for a date is the latest <c>recorded_at</c>.
/// </summary>
public sealed class MacroStore
{
    private readonly DegerliDbContext _db;

    public MacroStore(DegerliDbContext db) => _db = db;

    /// <summary>The registered series (code, cadence) that jobs partition by cadence.</summary>
    public async Task<IReadOnlyList<MacroSeriesDefinition>> GetSeriesAsync(CancellationToken cancellationToken = default) =>
        await _db.MacroSeries
            .OrderBy(s => s.Code)
            .Select(s => new MacroSeriesDefinition(s.Code, s.Cadence))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Appends the supplied facts. Rows whose (series, value_date, recorded_at) already
    /// exist are counted as unchanged; all others are inserted. Nothing is ever updated
    /// or deleted (FR-MDF-015).
    /// </summary>
    public async Task<MacroAppendResult> AppendAsync(
        string sourceRef,
        IReadOnlyList<MacroFact> facts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRef);

        if (facts.Count == 0)
        {
            return new MacroAppendResult(0, 0);
        }

        var seriesCodes = facts.Select(f => f.SeriesCode).Distinct(StringComparer.Ordinal).ToList();
        var dates = facts.Select(f => f.ValueDate).Distinct().ToList();

        var knownSeries = await _db.MacroSeries
            .Where(s => seriesCodes.Contains(s.Code))
            .Select(s => s.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var known = knownSeries.ToHashSet(StringComparer.Ordinal);

        var existing = await _db.MacroValues
            .Where(v => seriesCodes.Contains(v.SeriesCode) && dates.Contains(v.ValueDate))
            .Select(v => new { v.SeriesCode, v.ValueDate, v.RecordedAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var keys = existing
            .Select(v => (v.SeriesCode, v.ValueDate, v.RecordedAt))
            .ToHashSet();

        var inserted = 0;
        var unchanged = 0;

        foreach (var fact in facts)
        {
            if (!known.Contains(fact.SeriesCode))
            {
                // The series definition is seeded (02 §3.2); an unknown code cannot be
                // stored without violating the FK and is not part of the V1 series set.
                continue;
            }

            var key = (fact.SeriesCode, fact.ValueDate, fact.RecordedAt);
            if (!keys.Add(key))
            {
                unchanged++;
                continue;
            }

            _db.MacroValues.Add(new MacroValue
            {
                SeriesCode = fact.SeriesCode,
                ValueDate = fact.ValueDate,
                Value = fact.Value,
                SourceRef = sourceRef,
                RecordedAt = fact.RecordedAt,
            });
            inserted++;
        }

        if (inserted > 0)
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new MacroAppendResult(inserted, unchanged);
    }

    /// <summary>The canonical value for a (series, value_date): the latest recorded_at row.</summary>
    public Task<MacroValue?> GetCanonicalAsync(
        string seriesCode,
        DateOnly valueDate,
        CancellationToken cancellationToken = default) =>
        _db.MacroValues
            .Where(v => v.SeriesCode == seriesCode && v.ValueDate == valueDate)
            .OrderByDescending(v => v.RecordedAt)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Every retained row for a (series, value_date), oldest first — the revision
    /// history an as-of read walks.</summary>
    public async Task<IReadOnlyList<MacroValue>> GetHistoryAsync(
        string seriesCode,
        DateOnly valueDate,
        CancellationToken cancellationToken = default) =>
        await _db.MacroValues
            .Where(v => v.SeriesCode == seriesCode && v.ValueDate == valueDate)
            .OrderBy(v => v.RecordedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}

/// <summary>A registered macro series definition (macro_series row).</summary>
public sealed record MacroSeriesDefinition(string Code, string? Cadence);
