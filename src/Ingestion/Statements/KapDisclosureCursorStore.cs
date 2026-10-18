using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Ingestion.Statements;

/// <summary>
/// Persists the per-company incremental VYK disclosure cursor (01 v1.5 §10.9.11,
/// FR-MDF-010/011) in the <b>existing</b> <c>coverage_metadata</c> table — Scope
/// <c>instrument</c>, a <c>kap-disclosure-cursor</c> data type, the last-processed
/// <c>disclosureIndex</c> in <see cref="CoverageMetadata.Notes"/>. No schema migration:
/// the table the <c>CoverageRecorder</c> already writes is reused. The data type is
/// deliberately <b>outside</b> <c>CoverageDataTypes.InstrumentScoped</c>, so the
/// no-silent-gaps reconcile never refreshes or clobbers the cursor row.
/// <para>
/// An unresolvable symbol (no instrument row) is a no-op: the cursor is not persisted
/// (nothing to key it to) and a later run simply starts from the configured early index.
/// </para>
/// </summary>
public sealed class KapDisclosureCursorStore
{
    /// <summary>The <c>coverage_metadata.data_type</c> of the per-company VYK cursor.</summary>
    public const string DataType = "kap-disclosure-cursor";

    private const string InstrumentScope = "instrument";

    private readonly DegerliDbContext _db;

    public KapDisclosureCursorStore(DegerliDbContext db) => _db = db;

    /// <summary>The persisted cursor for <paramref name="symbol"/>, or <c>null</c> when none exists.</summary>
    public async Task<string?> GetAsync(string symbol, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        var instrumentId = await _db.Instruments
            .Where(i => i.Symbol == symbol)
            .Select(i => (long?)i.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (instrumentId is null)
        {
            return null;
        }

        return await _db.CoverageMetadata
            .Where(c => c.Scope == InstrumentScope && c.InstrumentId == instrumentId && c.DataType == DataType)
            .Select(c => c.Notes)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Upserts <paramref name="symbol"/>'s cursor (idempotent on the instrument/data-type
    /// key). An unresolvable symbol is a no-op.
    /// </summary>
    public async Task SetAsync(string symbol, string cursor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(cursor);

        var instrumentId = await _db.Instruments
            .Where(i => i.Symbol == symbol)
            .Select(i => (long?)i.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (instrumentId is null)
        {
            return;
        }

        var row = await _db.CoverageMetadata
            .FirstOrDefaultAsync(
                c => c.Scope == InstrumentScope && c.InstrumentId == instrumentId && c.DataType == DataType,
                cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            _db.CoverageMetadata.Add(new CoverageMetadata
            {
                Scope = InstrumentScope,
                InstrumentId = instrumentId,
                DataType = DataType,
                Notes = cursor,
            });
        }
        else
        {
            row.Notes = cursor;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
