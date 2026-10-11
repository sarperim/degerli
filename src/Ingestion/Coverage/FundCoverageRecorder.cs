using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Ingestion.Coverage;

/// <summary>
/// Writes the recording side of <c>coverage_metadata</c> for the fund scope
/// (FR-FDF-004, NFR-FDF-001; 02 §3.1). It reconciles every fund in the universe across
/// the three fund data types (nav, performance, holdings): a data type present in storage
/// records its achieved depth (earliest/latest date), a data type absent from storage is
/// recorded as an explicit gap — the fund-side no-silent-gaps rule (UC-FDF-003).
/// </summary>
/// <remarks>
/// In backfill mode (<paramref name="requestedFrom"/> set) a NAV history whose earliest
/// row is later than the requested target carries a source-limit note: the limitation is
/// recorded as the actual achieved depth, never fabricated over (UC-FDF-001 alternate a).
/// Upserts are idempotent on (scope, fund_id, data_type): a re-run refreshes the depth in
/// place and never duplicates a row.
/// </remarks>
public sealed class FundCoverageRecorder
{
    internal const string FundScope = "fund";

    private readonly DegerliDbContext _db;

    public FundCoverageRecorder(DegerliDbContext db) => _db = db;

    /// <summary>
    /// Reconciles the fund-scope coverage rows for every fund in the universe. When
    /// <paramref name="requestedFrom"/> is given (backfill mode) a NAV history shallower
    /// than the target is noted as source-limited; otherwise existing notes are preserved.
    /// </summary>
    public async Task<int> ReconcileAsync(
        DateOnly? requestedFrom = null,
        CancellationToken cancellationToken = default)
    {
        var fundCodes = await _db.Funds
            .Select(fund => fund.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (fundCodes.Count == 0)
        {
            return 0;
        }

        var navRanges = await _db.FundNavs
            .Where(nav => fundCodes.Contains(nav.FundId))
            .GroupBy(nav => nav.FundId)
            .Select(group => new { FundId = group.Key, From = group.Min(nav => nav.NavDate), To = group.Max(nav => nav.NavDate) })
            .ToDictionaryAsync(row => row.FundId, row => (row.From, row.To), cancellationToken)
            .ConfigureAwait(false);

        var performanceRanges = await _db.FundPerformances
            .Where(performance => fundCodes.Contains(performance.FundId))
            .GroupBy(performance => performance.FundId)
            .Select(group => new { FundId = group.Key, From = group.Min(performance => performance.AsOfDate), To = group.Max(performance => performance.AsOfDate) })
            .ToDictionaryAsync(row => row.FundId, row => (row.From, row.To), cancellationToken)
            .ConfigureAwait(false);

        var holdingsRanges = await _db.FundHoldings
            .Where(holding => fundCodes.Contains(holding.FundId))
            .GroupBy(holding => holding.FundId)
            .Select(group => new { FundId = group.Key, From = group.Min(holding => holding.AsOfDate), To = group.Max(holding => holding.AsOfDate) })
            .ToDictionaryAsync(row => row.FundId, row => (row.From, row.To), cancellationToken)
            .ConfigureAwait(false);

        var existing = await _db.CoverageMetadata
            .Where(row => row.Scope == FundScope && row.FundId != null && fundCodes.Contains(row.FundId!))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var byKey = existing
            .GroupBy(row => (row.FundId!, row.DataType))
            .ToDictionary(group => group.Key, group => group.First());

        var written = 0;
        foreach (var fundCode in fundCodes)
        {
            written += Upsert(byKey, fundCode, CoverageDataTypes.Nav, navRanges, requestedFrom);
            written += Upsert(byKey, fundCode, CoverageDataTypes.Performance, performanceRanges, requestedFrom);
            written += Upsert(byKey, fundCode, CoverageDataTypes.Holdings, holdingsRanges, requestedFrom);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return written;
    }

    private int Upsert(
        Dictionary<(string FundId, string DataType), CoverageMetadata> existing,
        string fundCode,
        string dataType,
        Dictionary<string, (DateOnly From, DateOnly To)> ranges,
        DateOnly? requestedFrom)
    {
        if (ranges.TryGetValue(fundCode, out var range))
        {
            // Only dated history carries a backfill depth; performance/holdings are
            // point-in-time snapshots and never fabricate a range note.
            var note = requestedFrom is { } target
                && dataType == CoverageDataTypes.Nav
                && range.From > target
                    ? SourceLimitNote(target, range.From)
                    : null;

            // An incremental run refreshes the depth but preserves any recorded limitation.
            return UpsertRow(existing, fundCode, dataType, range.From, range.To, note, preserveNotes: requestedFrom is null);
        }

        return UpsertRow(existing, fundCode, dataType, null, null, GapNote(dataType), preserveNotes: false);
    }

    private int UpsertRow(
        Dictionary<(string FundId, string DataType), CoverageMetadata> existing,
        string fundCode,
        string dataType,
        DateOnly? from,
        DateOnly? to,
        string? notes,
        bool preserveNotes)
    {
        if (existing.TryGetValue((fundCode, dataType), out var row))
        {
            row.AvailableFrom = from;
            row.AvailableTo = to;
            if (!preserveNotes)
            {
                row.Notes = notes;
            }

            return 1;
        }

        row = new CoverageMetadata
        {
            Scope = FundScope,
            FundId = fundCode,
            DataType = dataType,
            AvailableFrom = from,
            AvailableTo = to,
            Notes = notes,
        };
        _db.CoverageMetadata.Add(row);
        existing[(fundCode, dataType)] = row;
        return 1;
    }

    private static string GapNote(string dataType) =>
        $"No {dataType} data available for this fund; recorded as an explicit coverage gap (NFR-FDF-001).";

    private static string SourceLimitNote(DateOnly requestedFrom, DateOnly achievedFrom) =>
        $"Source history begins {achievedFrom:yyyy-MM-dd}; backfill target {requestedFrom:yyyy-MM-dd} not reached (source limit).";
}
