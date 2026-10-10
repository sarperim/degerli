using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Ingestion.Coverage;

/// <summary>
/// Writes the recording side of <c>coverage_metadata</c> (FR-MDF-011, FR-MDF-010,
/// NFR-MDF-003; 02 §3.1). Two entry points:
/// <list type="bullet">
/// <item><see cref="RecordPricesCoverageAsync"/> — after a price backfill, records the
/// per-instrument achieved depth and a note when the requested target exceeded the
/// source's real history limit (UC-MDF-002 alternate a, BR-MDF-006).</item>
/// <item><see cref="ReconcileUniverseAsync"/> — the no-silent-gaps rule: every current
/// universe instrument × data type gets a row, carrying the depth when data exists or an
/// explicit gap note when it does not. Nothing is silently omitted.</item>
/// </list>
/// Upserts are idempotent on (scope, instrument_id, data_type): a re-run refreshes the
/// depth in place and never duplicates a row.
/// </summary>
public sealed class CoverageRecorder
{
    internal const string InstrumentScope = "instrument";

    private readonly DegerliDbContext _db;

    public CoverageRecorder(DegerliDbContext db) => _db = db;

    /// <summary>
    /// Records the achieved backfill depth per instrument for <c>prices</c>. When the
    /// earliest stored row is later than the requested target, the source limit is
    /// written as a coverage note (the limitation is recorded, never fabricated over).
    /// </summary>
    public async Task<int> RecordPricesCoverageAsync(
        IReadOnlyCollection<string> symbols,
        DateOnly requestedFrom,
        DateOnly achievedFrom,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        if (symbols.Count == 0)
        {
            return 0;
        }

        var instruments = await _db.Instruments
            .Where(i => symbols.Contains(i.Symbol))
            .Select(i => new { i.Id, i.Symbol })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (instruments.Count == 0)
        {
            return 0;
        }

        var ids = instruments.Select(i => i.Id).ToList();
        var ranges = await PriceRangesAsync(ids, cancellationToken).ConfigureAwait(false);
        var existing = await LoadExistingAsync(ids, cancellationToken).ConfigureAwait(false);

        var note = achievedFrom > requestedFrom
            ? $"Source history begins {achievedFrom:yyyy-MM-dd}; backfill target {requestedFrom:yyyy-MM-dd} not reached (source limit)."
            : null;

        var written = 0;
        foreach (var instrument in instruments)
        {
            if (!ranges.TryGetValue(instrument.Id, out var range))
            {
                continue;
            }

            Upsert(existing, instrument.Id, CoverageDataTypes.Prices, range.From, range.To, note, preserveNotes: note is null);
            written++;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return written;
    }

    /// <summary>
    /// The no-silent-gaps consistency rule (NFR-MDF-003, TC-MDF-018): for every current
    /// universe instrument × tracked data type, upsert a coverage row carrying the
    /// available depth (data present) or an explicit gap note (data absent).
    /// </summary>
    public async Task<int> ReconcileUniverseAsync(CancellationToken cancellationToken = default)
    {
        var universeIds = await CurrentUniverseIdsAsync(cancellationToken).ConfigureAwait(false);
        if (universeIds.Count == 0)
        {
            return 0;
        }

        var existing = await LoadExistingAsync(universeIds, cancellationToken).ConfigureAwait(false);

        var ranges = new Dictionary<string, Dictionary<long, (DateOnly From, DateOnly To)>>(StringComparer.Ordinal)
        {
            [CoverageDataTypes.Prices] = await PriceRangesAsync(universeIds, cancellationToken).ConfigureAwait(false),
            [CoverageDataTypes.Statements] = await _db.FinancialStatements
                .Where(s => universeIds.Contains(s.InstrumentId))
                .GroupBy(s => s.InstrumentId)
                .Select(g => new { InstrumentId = g.Key, From = g.Min(s => s.PeriodEndDate), To = g.Max(s => s.PeriodEndDate) })
                .ToDictionaryAsync(x => x.InstrumentId, x => (x.From, x.To), cancellationToken)
                .ConfigureAwait(false),
            [CoverageDataTypes.Dividends] = await _db.Dividends
                .Where(d => universeIds.Contains(d.InstrumentId))
                .GroupBy(d => d.InstrumentId)
                .Select(g => new { InstrumentId = g.Key, From = g.Min(d => d.ExDate), To = g.Max(d => d.ExDate) })
                .ToDictionaryAsync(x => x.InstrumentId, x => (x.From, x.To), cancellationToken)
                .ConfigureAwait(false),
            [CoverageDataTypes.CorporateActions] = await _db.CorporateActions
                .Where(a => universeIds.Contains(a.InstrumentId))
                .GroupBy(a => a.InstrumentId)
                .Select(g => new { InstrumentId = g.Key, From = g.Min(a => a.ActionDate), To = g.Max(a => a.ActionDate) })
                .ToDictionaryAsync(x => x.InstrumentId, x => (x.From, x.To), cancellationToken)
                .ConfigureAwait(false),
            [CoverageDataTypes.Disclosures] = await _db.KapDisclosures
                .Where(k => universeIds.Contains(k.InstrumentId) && k.PublishDate != null)
                .GroupBy(k => k.InstrumentId)
                .Select(g => new { InstrumentId = g.Key, From = g.Min(k => k.PublishDate)!.Value, To = g.Max(k => k.PublishDate)!.Value })
                .ToDictionaryAsync(x => x.InstrumentId, x => (x.From, x.To), cancellationToken)
                .ConfigureAwait(false),
        };

        var written = 0;
        foreach (var instrumentId in universeIds)
        {
            foreach (var dataType in CoverageDataTypes.InstrumentScoped)
            {
                if (ranges[dataType].TryGetValue(instrumentId, out var range))
                {
                    // Present data: refresh the depth but preserve any existing note
                    // (e.g. a source-limit limitation recorded during backfill).
                    Upsert(existing, instrumentId, dataType, range.From, range.To, notes: null, preserveNotes: true);
                }
                else
                {
                    Upsert(existing, instrumentId, dataType, null, null, GapNote(dataType), preserveNotes: false);
                }

                written++;
            }
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return written;
    }

    private static string GapNote(string dataType) =>
        $"No {dataType} data available; recorded as an explicit coverage gap (NFR-MDF-003).";

    private async Task<List<long>> CurrentUniverseIdsAsync(CancellationToken cancellationToken)
    {
        var xu100 = await _db.Indices
            .Where(i => i.Code == "XU100")
            .Select(i => (long?)i.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (xu100 is null)
        {
            return [];
        }

        return await _db.IndexConstituents
            .Where(c => c.IndexId == xu100.Value && c.EffectiveTo == null)
            .Select(c => c.InstrumentId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Dictionary<long, (DateOnly From, DateOnly To)>> PriceRangesAsync(
        IReadOnlyCollection<long> instrumentIds,
        CancellationToken cancellationToken) =>
        await _db.DailyPrices
            .Where(p => instrumentIds.Contains(p.InstrumentId))
            .GroupBy(p => p.InstrumentId)
            .Select(g => new { InstrumentId = g.Key, From = g.Min(p => p.PriceDate), To = g.Max(p => p.PriceDate) })
            .ToDictionaryAsync(x => x.InstrumentId, x => (x.From, x.To), cancellationToken)
            .ConfigureAwait(false);

    private async Task<Dictionary<(long InstrumentId, string DataType), CoverageMetadata>> LoadExistingAsync(
        IReadOnlyCollection<long> instrumentIds,
        CancellationToken cancellationToken)
    {
        var rows = await _db.CoverageMetadata
            .Where(c => c.Scope == InstrumentScope && c.InstrumentId != null && instrumentIds.Contains(c.InstrumentId!.Value))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .GroupBy(c => (c.InstrumentId!.Value, c.DataType))
            .ToDictionary(g => g.Key, g => g.First());
    }

    private CoverageMetadata Upsert(
        Dictionary<(long InstrumentId, string DataType), CoverageMetadata> existing,
        long instrumentId,
        string dataType,
        DateOnly? from,
        DateOnly? to,
        string? notes,
        bool preserveNotes)
    {
        if (existing.TryGetValue((instrumentId, dataType), out var row))
        {
            row.AvailableFrom = from;
            row.AvailableTo = to;
            if (!preserveNotes)
            {
                row.Notes = notes;
            }

            return row;
        }

        row = new CoverageMetadata
        {
            Scope = InstrumentScope,
            InstrumentId = instrumentId,
            DataType = dataType,
            AvailableFrom = from,
            AvailableTo = to,
            Notes = notes,
        };
        _db.CoverageMetadata.Add(row);
        existing[(instrumentId, dataType)] = row;
        return row;
    }
}
