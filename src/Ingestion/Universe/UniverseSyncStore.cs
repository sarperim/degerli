using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Ingestion.Universe;

/// <summary>
/// Outcome of one universe/classification sync batch. <see cref="RejectedMemberships"/>
/// carries membership changes whose index code or symbol resolved to no stored
/// reference row: they are never silently dropped (NFR-MDF-003, FR-MDF-012) — the
/// caller quarantines and counts them.
/// </summary>
public sealed record UniverseSyncResult(
    int InstrumentsWritten,
    int InstrumentsUnchanged,
    int MembershipsAdded,
    int MembershipsClosed,
    IReadOnlyList<UniverseInstrument> Unclassified,
    IReadOnlyList<MembershipChange> RejectedMemberships);

/// <summary>
/// Outcome of one index-levels sync batch. <see cref="Rejected"/> carries levels whose
/// index code resolved to no stored reference row (quarantined by the caller, never
/// silently dropped).
/// </summary>
public sealed record IndexLevelsSyncResult(
    int Written,
    int Unchanged,
    IReadOnlyList<IndexLevelFact> Rejected);

/// <summary>
/// Fact storage for the universe tables (FR-MDF-006/007; 02 §3.1): sector and index
/// reference rows (upserted by natural key from the payload), instruments,
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

        // Sector and index reference data are synced first (FR-MDF-006) so that the
        // instrument links and membership changes in the same payload can resolve them.
        var sectors = await UpsertSectorsAsync(payload.Sectors, cancellationToken).ConfigureAwait(false);
        var indices = await UpsertIndicesAsync(payload.Indices, cancellationToken).ConfigureAwait(false);
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
        var rejected = new List<MembershipChange>();
        if (payload.EffectiveDate is { } effective
            && ((payload.Add?.Count ?? 0) > 0 || (payload.Remove?.Count ?? 0) > 0))
        {
            var instrumentIds = await _db.Instruments
                .ToDictionaryAsync(i => i.Symbol, i => i.Id, StringComparer.Ordinal, cancellationToken)
                .ConfigureAwait(false);

            // Load the open intervals once and track the batch's staged decisions in
            // memory. Re-querying the database mid-batch would not see edits still
            // staged in the change tracker: a duplicate add would insert two open
            // rows, and a remove+add of the same member would silently keep it closed
            // (BR-MDF-007, S1).
            var openRows = (await _db.IndexConstituents
                    .Where(c => c.EffectiveTo == null)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false))
                .ToDictionary(c => (c.IndexId, c.InstrumentId));
            var openPairs = new HashSet<(long IndexId, long InstrumentId)>(openRows.Keys);

            foreach (var change in payload.Remove ?? [])
            {
                if (!indices.TryGetValue(change.IndexCode, out var indexId)
                    || !instrumentIds.TryGetValue(change.Symbol, out var instrumentId))
                {
                    rejected.Add(change);
                    continue;
                }

                // Close the open interval — never delete the row, never change its start.
                if (openPairs.Contains((indexId, instrumentId))
                    && openRows.TryGetValue((indexId, instrumentId), out var open)
                    && open.EffectiveFrom < effective)
                {
                    open.EffectiveTo = effective;
                    openPairs.Remove((indexId, instrumentId));
                    membershipsClosed++;
                }
            }

            foreach (var change in payload.Add ?? [])
            {
                if (!indices.TryGetValue(change.IndexCode, out var indexId)
                    || !instrumentIds.TryGetValue(change.Symbol, out var instrumentId))
                {
                    rejected.Add(change);
                    continue;
                }

                // Already open in the database *or* staged by an earlier add in this
                // same batch — an idempotent no-op either way.
                if (!openPairs.Add((indexId, instrumentId)))
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

        return new UniverseSyncResult(
            written,
            unchanged,
            membershipsAdded,
            membershipsClosed,
            unclassified,
            rejected);
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
        var rejected = new List<IndexLevelFact>();

        foreach (var level in payload.Levels ?? [])
        {
            if (!indexIds.TryGetValue(level.IndexCode, out var indexId))
            {
                // Unknown index reference: quarantined by the caller, never dropped
                // (NFR-MDF-003, FR-MDF-012).
                rejected.Add(level);
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

        return new IndexLevelsSyncResult(written, unchanged, rejected);
    }

    /// <summary>
    /// Upserts sector reference data by natural key (FR-MDF-006; 02 §3.1). A missing
    /// sector is created with the published bilingual labels; an existing one has its
    /// labels refreshed. Parent links (2-level hierarchy) are resolved by code once
    /// every referenced sector has an id, so a parent published in the same payload
    /// resolves. Returns code → id for the instrument-link and membership phases.
    /// </summary>
    private async Task<Dictionary<string, long>> UpsertSectorsAsync(
        IReadOnlyList<UniverseSector>? references,
        CancellationToken cancellationToken)
    {
        var sectors = await _db.Sectors
            .ToDictionaryAsync(s => s.Code, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        if (references is { Count: > 0 })
        {
            var changed = false;
            foreach (var reference in references)
            {
                if (string.IsNullOrWhiteSpace(reference.Code))
                {
                    continue;
                }

                if (sectors.TryGetValue(reference.Code, out var existing))
                {
                    if (!string.IsNullOrWhiteSpace(reference.NameTr) && existing.NameTr != reference.NameTr)
                    {
                        existing.NameTr = reference.NameTr!;
                        changed = true;
                    }

                    if (!string.IsNullOrWhiteSpace(reference.NameEn) && existing.NameEn != reference.NameEn)
                    {
                        existing.NameEn = reference.NameEn!;
                        changed = true;
                    }
                }
                else
                {
                    var sector = new Sector
                    {
                        Code = reference.Code,
                        NameTr = string.IsNullOrWhiteSpace(reference.NameTr) ? reference.Code : reference.NameTr!,
                        NameEn = string.IsNullOrWhiteSpace(reference.NameEn) ? reference.Code : reference.NameEn!,
                    };
                    _db.Sectors.Add(sector);
                    sectors[reference.Code] = sector;
                    changed = true;
                }
            }

            if (changed)
            {
                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            var hierarchyChanged = false;
            foreach (var reference in references)
            {
                if (string.IsNullOrWhiteSpace(reference.ParentCode)
                    || !sectors.TryGetValue(reference.Code, out var child)
                    || !sectors.TryGetValue(reference.ParentCode, out var parent))
                {
                    continue;
                }

                if (child.ParentSectorId != parent.Id)
                {
                    child.ParentSectorId = parent.Id;
                    hierarchyChanged = true;
                }
            }

            if (hierarchyChanged)
            {
                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        return sectors.ToDictionary(kv => kv.Key, kv => kv.Value.Id, StringComparer.Ordinal);
    }

    /// <summary>
    /// Upserts index reference data by natural key (FR-MDF-006; 02 §3.1) so membership
    /// changes and index levels can resolve their index code. Returns code → id.
    /// </summary>
    private async Task<Dictionary<string, long>> UpsertIndicesAsync(
        IReadOnlyList<UniverseIndex>? references,
        CancellationToken cancellationToken)
    {
        var indices = await _db.Indices
            .ToDictionaryAsync(i => i.Code, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        if (references is { Count: > 0 })
        {
            var changed = false;
            foreach (var reference in references)
            {
                if (string.IsNullOrWhiteSpace(reference.Code))
                {
                    continue;
                }

                if (indices.TryGetValue(reference.Code, out var existing))
                {
                    if (!string.IsNullOrWhiteSpace(reference.NameTr) && existing.NameTr != reference.NameTr)
                    {
                        existing.NameTr = reference.NameTr!;
                        changed = true;
                    }

                    if (!string.IsNullOrWhiteSpace(reference.NameEn) && existing.NameEn != reference.NameEn)
                    {
                        existing.NameEn = reference.NameEn!;
                        changed = true;
                    }
                }
                else
                {
                    var index = new MarketIndex
                    {
                        Code = reference.Code,
                        NameTr = string.IsNullOrWhiteSpace(reference.NameTr) ? reference.Code : reference.NameTr!,
                        NameEn = string.IsNullOrWhiteSpace(reference.NameEn) ? reference.Code : reference.NameEn!,
                    };
                    _db.Indices.Add(index);
                    indices[reference.Code] = index;
                    changed = true;
                }
            }

            if (changed)
            {
                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        return indices.ToDictionary(kv => kv.Key, kv => kv.Value.Id, StringComparer.Ordinal);
    }
}
