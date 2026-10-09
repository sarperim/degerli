using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Ingestion.Dividends;

/// <summary>
/// Fact storage for <c>dividends</c> (FR-MDF-003, 02 §1). A re-run of an already-stored
/// (instrument, ex-date, amount) record is an idempotent no-op; rows are append-only.
/// </summary>
public sealed class DividendStore
{
    private readonly DegerliDbContext _db;

    public DividendStore(DegerliDbContext db) => _db = db;

    public async Task<DividendWriteResult> UpsertAsync(
        string sourceRef,
        DateTimeOffset recordedAt,
        DividendsPayload payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRef);

        var symbols = payload.Dividends.Select(d => d.Symbol).Distinct(StringComparer.Ordinal).ToList();
        var instrumentIds = await _db.Instruments
            .Where(i => symbols.Contains(i.Symbol))
            .ToDictionaryAsync(i => i.Symbol, i => i.Id, cancellationToken)
            .ConfigureAwait(false);

        var instrumentIdList = instrumentIds.Values.ToList();
        var existing = (await _db.Dividends
                .Where(d => instrumentIdList.Contains(d.InstrumentId))
                .Select(d => new { d.InstrumentId, d.ExDate, d.AmountPerShare })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .Select(d => (d.InstrumentId, d.ExDate, d.AmountPerShare))
            .ToHashSet();

        var inserted = 0;
        var unchanged = 0;
        var unknown = new HashSet<string>(StringComparer.Ordinal);

        foreach (var dividend in payload.Dividends)
        {
            if (!instrumentIds.TryGetValue(dividend.Symbol, out var instrumentId))
            {
                unknown.Add(dividend.Symbol);
                continue;
            }

            if (!existing.Add((instrumentId, dividend.ExDate, dividend.AmountPerShare)))
            {
                unchanged++;
                continue;
            }

            _db.Dividends.Add(new Dividend
            {
                InstrumentId = instrumentId,
                ExDate = dividend.ExDate,
                PayDate = dividend.PayDate,
                AmountPerShare = dividend.AmountPerShare,
                Currency = dividend.Currency,
                SourceRef = sourceRef,
                RecordedAt = recordedAt,
            });
            inserted++;
        }

        if (inserted > 0)
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new DividendWriteResult(inserted, unchanged, unknown.ToList());
    }
}
