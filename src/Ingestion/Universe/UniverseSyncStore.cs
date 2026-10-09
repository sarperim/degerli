using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Ingestion.Universe;

/// <summary>Outcome of one universe/classification sync batch.</summary>
public sealed record UniverseSyncResult(
    int InstrumentsWritten,
    int InstrumentsUnchanged,
    int MembershipsAdded,
    int MembershipsClosed,
    IReadOnlyList<UniverseInstrument> Unclassified);

/// <summary>Outcome of one index-levels sync batch.</summary>
public sealed record IndexLevelsSyncResult(int Written, int Unchanged);

/// <summary>
/// Fact storage for the universe tables (FR-MDF-006/007; 02 §3.1): instruments,
/// sector links (kept current per instrument), effective-dated append-only
/// <c>index_constituents</c> and <c>index_levels</c>. Instruments are matched on
/// their natural key (symbol); a re-send with no changed fields is an idempotent
/// no-op, while a classification change moves the sector link (TC-MDF-038).
/// Membership changes never delete or reopen a row — a removal closes the open
/// interval (<c>effective_to</c>) and a re-add appends a new row (TC-MDF-037).
/// </summary>
public sealed class UniverseSyncStore
{
    private readonly DegerliDbContext _db;

    public UniverseSyncStore(DegerliDbContext db) => _db = db;

    public async Task<UniverseSyncResult> SyncUniverseAsync(
        string sourceRef,
        DateTimeOffset recordedAt,
        UniversePayload payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRef);
        ArgumentNullException.ThrowIfNull(payload);

        var sectors = await _db.Sectors
            .ToDictionaryAsync(s => s.Code, s => s.Id, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);
        var stored = await _db.Instruments
            .ToDictionaryAsync(i => i.Symbol, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        var written = 0;
        var unchanged = 0;
        var unclassified = new List<UniverseInstrument>();

        foreach (var item in payload.Instruments ?? [])
        {
            var sectorId = item.SectorCode is not null && sectors.TryGetValue(item.SectorCode, out var resolved)
                ? resolved
                : (long?)null;

            if (stored.TryGetValue(item.Symbol, out var existing))
            {
                var changed = false;
                if (!string.IsNullOrWhiteSpace(item.Name) && existing.Name != item.Name)
                {
                    existing.Name = item.Name!;
                    changed = true;
                }

                if (item.ListingDate is { } listing && existing.ListingDate != listing)
                {
                    existing.ListingDate = listing;
                    changed = true;
                }

                if (existing.SectorId != sectorId)
                {
                    existing.SectorId = sectorId;
                    changed = true;
                }

                if (changed)
                {
                    existing.SourceRef = sourceRef;
                    existing.RecordedAt = recordedAt;
                    written++;
                }
                else
                {
                    unchanged++;
                }
            }
            else
            {
                _db.Instruments.Add(new Instrument
                {
                    Symbol = item.Symbol,
                    Name = string.IsNullOrWhiteSpace(item.Name) ? item.Symbol : item.Name!,
                    SectorId = sectorId,
                    ListingDate = item.ListingDate,
                    Status = "active",
                    SourceRef = sourceRef,
                    RecordedAt = recordedAt,
                });
                written++;
            }

            if (sectorId is null)
            {
                unclassified.Add(item);
            }
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var membershipsAdded = 0;
        var membershipsClosed = 0;
        if (payload.EffectiveDate is { } effective
            && ((payload.Add?.Count ?? 0) > 0 || (payload.Remove?.Count ?? 0) > 0))
        {
            var indexIds = await _db.Indices
                .ToDictionaryAsync(i => i.Code, i => i.Id, StringComparer.Ordinal, cancellationToken)
                .ConfigureAwait(false);
            var instrumentIds = await _db.Instruments
                .ToDictionaryAsync(i => i.Symbol, i => i.Id, StringComparer.Ordinal, cancellationToken)
                .ConfigureAwait(false);

            foreach (var change in payload.Remove ?? [])
            {
                if (!indexIds.TryGetValue(change.IndexCode, out var indexId)
                    || !instrumentIds.TryGetValue(change.Symbol, out var instrumentId))
                {
                    continue;
                }

                // Close the open interval — never delete the row, never change its start.
                var open = await _db.IndexConstituents
                    .FirstOrDefaultAsync(
                        c => c.IndexId == indexId
                            && c.InstrumentId == instrumentId
                            && c.EffectiveTo == null
                            && c.EffectiveFrom < effective,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (open is not null)
                {
                    open.EffectiveTo = effective;
                    membershipsClosed++;
                }
            }

            foreach (var change in payload.Add ?? [])
            {
                if (!indexIds.TryGetValue(change.IndexCode, out var indexId)
                    || !instrumentIds.TryGetValue(change.Symbol, out var instrumentId))
                {
                    continue;
                }

                var alreadyOpen = await _db.IndexConstituents
                    .AnyAsync(
                        c => c.IndexId == indexId && c.InstrumentId == instrumentId && c.EffectiveTo == null,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (alreadyOpen)
                {
                    continue;
                }

                _db.IndexConstituents.Add(new IndexConstituent
                {
                    IndexId = indexId,
                    InstrumentId = instrumentId,
                    EffectiveFrom = effective,
                    EffectiveTo = null,
                    SourceRef = sourceRef,
                    RecordedAt = recordedAt,
                });
                membershipsAdded++;
            }

            if (membershipsAdded > 0 || membershipsClosed > 0)
            {
                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        return new UniverseSyncResult(written, unchanged, membershipsAdded, membershipsClosed, unclassified);
    }

    public async Task<IndexLevelsSyncResult> SyncIndexLevelsAsync(
        string sourceRef,
        DateTimeOffset recordedAt,
        IndexLevelsPayload payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRef);
        ArgumentNullException.ThrowIfNull(payload);

        var indexIds = await _db.Indices
            .ToDictionaryAsync(i => i.Code, i => i.Id, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        var written = 0;
        var unchanged = 0;

        foreach (var level in payload.Levels ?? [])
        {
            if (!indexIds.TryGetValue(level.IndexCode, out var indexId))
            {
                continue;
            }

            var existing = await _db.IndexLevels
                .FirstOrDefaultAsync(l => l.IndexId == indexId && l.LevelDate == level.Date, cancellationToken)
                .ConfigureAwait(false);
            if (existing is null)
            {
                _db.IndexLevels.Add(new IndexLevel
                {
                    IndexId = indexId,
                    LevelDate = level.Date,
                    Close = level.Close,
                    SourceRef = sourceRef,
                    RecordedAt = recordedAt,
                });
                written++;
            }
            else
            {
                // An index level for the same (index, date) is append-only: a re-send
                // is an idempotent no-op (FR-MDF-015).
                unchanged++;
            }
        }

        if (written > 0)
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new IndexLevelsSyncResult(written, unchanged);
    }
}
