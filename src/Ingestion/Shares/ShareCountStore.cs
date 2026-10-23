using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Ingestion.Shares;

/// <summary>
/// What one <c>shares</c> store pass did (TKT-foundation-058, 01 v1.5.36 §10.9.11).
/// <see cref="Inserted"/> + <see cref="Updated"/> are the run's <c>written</c> facts;
/// <see cref="SkippedLines"/> counts the security lines the source carried without a
/// <c>currentCapital</c> (skipped, never zero-filled) — including the lines of a matched group
/// withheld because one of its tertips was unreadable.
/// </summary>
public sealed record ShareCountWriteResult(int Inserted, int Updated, int Unchanged, int SkippedLines)
{
    /// <summary>The run's <c>written</c> count (new rows + repaired rows).</summary>
    public int Written => Inserted + Updated;
}

/// <summary>
/// The per-instrument share-count store (FR-MDF-002's lane, 01 v1.5.36 §10.9.11): matches each
/// member's security lines to our instruments and writes one <c>share_counts</c> row per matched
/// instrument.
/// <para>
/// <b>Matching:</b> a line resolves to an instrument by its <c>borsaKodu</c> (<b>primary</b>,
/// case-insensitive) and, when no instrument carries that code, by its <c>isin</c>
/// (<b>fallback</b>). A member's lines are grouped per resolved instrument and the group is
/// matched only when <b>at least one of its lines is trading-open</b> (<c>borsadaIslemeAcik</c>)
/// — a delisted/closed line never seeds a count on its own.
/// </para>
/// <para>
/// <b>Tertip aggregation (evidenced hypothesis):</b> a matched instrument's count is the sum of
/// its lines' <c>currentCapital</c> — ASELS's A+B lines sum to the real 4.56B count (live
/// 2026-10-22), which is what the shared line carries across tertips. The live cross-validation
/// on the ticket confirms or refutes the rule per company; a refuted rule is recorded, never
/// shipped as a fabricated number.
/// </para>
/// <para>
/// <b>Honest absence:</b> an instrument with no line produces no row (never a zero). A matched
/// group whose lines are not all readable is <b>withheld</b> (its total is unknown — a partial sum
/// would be a fabricated count) and its unreadable lines are counted as skipped. A re-run is
/// input-aware: an unchanged count writes nothing, a changed count repairs the row in place
/// (the TKT-foundation-042/050 doctrine).
/// </para>
/// </summary>
public sealed class ShareCountStore
{
    private readonly DegerliDbContext _db;

    public ShareCountStore(DegerliDbContext db) => _db = db;

    public async Task<ShareCountWriteResult> UpsertAsync(
        string sourceRef,
        DateTimeOffset recordedAt,
        SharesPayload payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRef);

        var instruments = await _db.Instruments
            .Select(instrument => new { instrument.Id, instrument.Symbol, instrument.Isin })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var bySymbol = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var byIsin = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var instrument in instruments)
        {
            bySymbol[instrument.Symbol] = instrument.Id;
            if (!string.IsNullOrWhiteSpace(instrument.Isin))
            {
                byIsin[instrument.Isin] = instrument.Id;
            }
        }

        var counts = new SortedDictionary<long, decimal>();
        var withheld = new HashSet<long>();
        var skipped = payload.SkippedLines;
        foreach (var member in payload.Members)
        {
            var groups = new Dictionary<long, List<ShareSecurityLine>>();
            foreach (var line in member.Lines)
            {
                var instrumentId = Resolve(line, bySymbol, byIsin);
                if (instrumentId is null)
                {
                    // Not one of our instruments — the source lists 671 members and we cover a
                    // fraction of them; an unmatched line is out of scope, not a skip.
                    continue;
                }

                if (!groups.TryGetValue(instrumentId.Value, out var lines))
                {
                    groups[instrumentId.Value] = lines = [];
                }

                lines.Add(line);
            }

            foreach (var (instrumentId, lines) in groups)
            {
                if (!lines.Any(line => line.TradingOpen))
                {
                    // No trading-open line: the instrument's code appears only on closed lines,
                    // so the member is not a live security identity for that instrument.
                    continue;
                }

                if (lines.Any(line => line.CurrentCapital is null))
                {
                    // A partial tertip sum is not the instrument's count. The group is withheld
                    // (honest absence) and its unreadable lines were already counted as skipped
                    // by the adapter. A withheld group wins over a complete group for the same
                    // instrument — an unknown total is never replaced by a partial one.
                    withheld.Add(instrumentId);
                    counts.Remove(instrumentId);
                    continue;
                }

                if (!withheld.Contains(instrumentId))
                {
                    counts[instrumentId] = lines.Sum(line => line.CurrentCapital!.Value);
                }
            }
        }

        var instrumentIds = counts.Keys.ToList();
        var existing = await _db.ShareCounts
            .Where(row => instrumentIds.Contains(row.InstrumentId))
            .ToDictionaryAsync(row => row.InstrumentId, cancellationToken)
            .ConfigureAwait(false);

        var inserted = 0;
        var updated = 0;
        var unchanged = 0;
        foreach (var (instrumentId, shares) in counts)
        {
            if (!existing.TryGetValue(instrumentId, out var row))
            {
                _db.ShareCounts.Add(new ShareCount
                {
                    InstrumentId = instrumentId,
                    SharesOutstanding = shares,
                    RecordedAt = recordedAt,
                    SourceRef = sourceRef,
                });
                inserted++;
                continue;
            }

            if (row.SharesOutstanding == shares)
            {
                // Input-aware: the stored count already matches the source — no write, and
                // `recorded_at` (the served asOf) must not move.
                unchanged++;
                continue;
            }

            row.SharesOutstanding = shares;
            row.RecordedAt = recordedAt;
            row.SourceRef = sourceRef;
            updated++;
        }

        if (inserted + updated > 0)
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new ShareCountWriteResult(inserted, updated, unchanged, skipped);
    }

    /// <summary>
    /// Resolves a security line to one of our instruments: <c>borsaKodu</c> (primary), then
    /// <c>isin</c> (fallback). A line whose code matches nothing is out of scope.
    /// </summary>
    private static long? Resolve(
        ShareSecurityLine line,
        IReadOnlyDictionary<string, long> bySymbol,
        IReadOnlyDictionary<string, long> byIsin)
    {
        if (line.BorsaKodu is not null && bySymbol.TryGetValue(line.BorsaKodu, out var byCode))
        {
            return byCode;
        }

        if (line.Isin is not null && byIsin.TryGetValue(line.Isin, out var byId))
        {
            return byId;
        }

        return null;
    }
}
