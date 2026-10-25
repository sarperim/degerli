using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Ingestion.KapPublic;

/// <summary>What one sync of the mapping store changed (the idempotent re-resolution vocabulary).</summary>
/// <param name="Inserted">Mappings that did not exist before.</param>
/// <param name="Updated">Mappings whose recorded identity or evidence changed (repaired in place).</param>
/// <param name="Unchanged">Mappings re-resolved to exactly the recorded values — no row touched.</param>
public sealed record KapPublicCompanyMapSync(int Inserted, int Updated, int Unchanged)
{
    /// <summary>The total number of mappings the sync considered.</summary>
    public int Total => Inserted + Updated + Unchanged;
}

/// <summary>
/// The <b>stored</b> public-oid ↔ instrument mapping (01 v1.5.54 §10.9.19, TKT-foundation-080;
/// <c>kap_public_company_map</c>). The mapping is a persisted, provenance-carrying fact — never an
/// in-memory assumption and never inferred from a symbol's spelling — so every consumer (082's
/// statements walk, 084's disclosures feed) reads the same record, and an unmapped instrument is
/// simply absent.
/// <para>
/// The store writes <b>one row per instrument</b> with the three identifier spaces in three
/// separate columns (ours, the public oid, the VYK numeric companyId). Each space carries its own
/// provenance and evidence date; nothing here translates one space into another. Re-resolution is
/// <b>idempotent</b>: identical input leaves the rows <c>unchanged</c> (no write, no timestamp
/// move), a changed identity repairs the row in place.
/// </para>
/// </summary>
public sealed class KapPublicCompanyMapStore
{
    private readonly DegerliDbContext _db;
    private readonly TimeProvider _clock;

    public KapPublicCompanyMapStore(DegerliDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>
    /// Persists the resolved mappings. An instrument with no row is inserted; an identical mapping is
    /// left untouched (<c>unchanged</c>); a differing one is repaired in place. Nothing is written for
    /// an instrument that is not in <paramref name="mappings"/> — absence stays absence.
    /// </summary>
    public async Task<KapPublicCompanyMapSync> SyncAsync(
        IReadOnlyList<KapPublicCompanyMapping> mappings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        if (mappings.Count == 0)
        {
            return new KapPublicCompanyMapSync(0, 0, 0);
        }

        var instrumentIds = mappings.Select(mapping => mapping.InstrumentId).Distinct().ToList();
        var existing = await _db.KapPublicCompanyMaps
            .Where(row => instrumentIds.Contains(row.InstrumentId))
            .ToDictionaryAsync(row => row.InstrumentId, cancellationToken)
            .ConfigureAwait(false);

        var now = _clock.GetUtcNow();
        var inserted = 0;
        var updated = 0;
        var unchanged = 0;

        foreach (var mapping in mappings)
        {
            if (!existing.TryGetValue(mapping.InstrumentId, out var row))
            {
                _db.KapPublicCompanyMaps.Add(NewRow(mapping, now));
                inserted++;
                continue;
            }

            if (Matches(row, mapping))
            {
                unchanged++;
                continue;
            }

            Apply(row, mapping, now);
            updated++;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new KapPublicCompanyMapSync(inserted, updated, unchanged);
    }

    /// <summary>
    /// The stored public oid for one instrument, or <c>null</c> when the instrument is unmapped (the
    /// named-and-counted skip path — never a guessed oid).
    /// </summary>
    public async Task<PublicCompanyOid?> GetPublicOidAsync(long instrumentId, CancellationToken cancellationToken = default)
    {
        var value = await _db.KapPublicCompanyMaps
            .Where(row => row.InstrumentId == instrumentId)
            .Select(row => row.KapPublicOid)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return value is null ? null : PublicCompanyOid.Parse(value);
    }

    /// <summary>
    /// The stored public oids for a bound set of instruments — one read per walk, so a lane never
    /// resolves per company from the source. Unmapped instruments are simply absent from the result.
    /// </summary>
    public async Task<IReadOnlyDictionary<long, PublicCompanyOid>> GetPublicOidsAsync(
        IReadOnlyCollection<long> instrumentIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instrumentIds);
        if (instrumentIds.Count == 0)
        {
            return new Dictionary<long, PublicCompanyOid>();
        }

        var rows = await _db.KapPublicCompanyMaps
            .Where(row => instrumentIds.Contains(row.InstrumentId))
            .Select(row => new { row.InstrumentId, row.KapPublicOid })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ToDictionary(
            row => row.InstrumentId,
            row => PublicCompanyOid.Parse(row.KapPublicOid));
    }

    private static KapPublicCompanyMap NewRow(KapPublicCompanyMapping mapping, DateTimeOffset now)
    {
        var row = new KapPublicCompanyMap { InstrumentId = mapping.InstrumentId };
        Apply(row, mapping, now);
        return row;
    }

    private static void Apply(KapPublicCompanyMap row, KapPublicCompanyMapping mapping, DateTimeOffset now)
    {
        row.KapPublicOid = mapping.PublicOid.Value;
        row.KapPublicOidSource = mapping.PublicEvidence.Source;
        row.KapPublicOidAsOf = mapping.PublicEvidence.AsOf;
        row.VykCompanyId = mapping.VykCompanyId?.Value;
        row.VykCompanyIdSource = mapping.VykEvidence?.Source;
        row.VykCompanyIdAsOf = mapping.VykEvidence?.AsOf;
        row.RecordedAt = now;
    }

    /// <summary>
    /// True when the stored row already records exactly this mapping — the identity of each space
    /// <b>and</b> its evidence. An identical re-resolution writes nothing, so a repeated run cannot
    /// masquerade as a change (the <c>unchanged</c> case).
    /// </summary>
    private static bool Matches(KapPublicCompanyMap row, KapPublicCompanyMapping mapping) =>
        string.Equals(row.KapPublicOid, mapping.PublicOid.Value, StringComparison.Ordinal)
        && string.Equals(row.KapPublicOidSource, mapping.PublicEvidence.Source, StringComparison.Ordinal)
        && row.KapPublicOidAsOf == mapping.PublicEvidence.AsOf
        && string.Equals(row.VykCompanyId, mapping.VykCompanyId?.Value, StringComparison.Ordinal)
        && string.Equals(row.VykCompanyIdSource, mapping.VykEvidence?.Source, StringComparison.Ordinal)
        && row.VykCompanyIdAsOf == mapping.VykEvidence?.AsOf;
}
