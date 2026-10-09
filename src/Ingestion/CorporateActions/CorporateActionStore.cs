using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Ingestion.CorporateActions;

/// <summary>
/// Fact storage for <c>corporate_actions</c> (FR-MDF-004, 02 §1). Terms are retained as
/// JSON (<c>terms_json</c>); a re-run of an already-stored (instrument, type, date) action
/// is an idempotent no-op.
/// </summary>
public sealed class CorporateActionStore
{
    private readonly DegerliDbContext _db;

    public CorporateActionStore(DegerliDbContext db) => _db = db;

    public async Task<CorporateActionWriteResult> UpsertAsync(
        string sourceRef,
        DateTimeOffset recordedAt,
        CorporateActionsPayload payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRef);

        var symbols = payload.Actions.Select(a => a.Symbol).Distinct(StringComparer.Ordinal).ToList();
        var instrumentIds = await _db.Instruments
            .Where(i => symbols.Contains(i.Symbol))
            .ToDictionaryAsync(i => i.Symbol, i => i.Id, cancellationToken)
            .ConfigureAwait(false);

        var instrumentIdList = instrumentIds.Values.ToList();
        var existing = (await _db.CorporateActions
                .Where(a => instrumentIdList.Contains(a.InstrumentId))
                .Select(a => new { a.InstrumentId, a.ActionType, a.ActionDate })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .Select(a => (a.InstrumentId, a.ActionType, a.ActionDate))
            .ToHashSet();

        var inserted = 0;
        var unchanged = 0;
        var unknown = new HashSet<string>(StringComparer.Ordinal);

        foreach (var action in payload.Actions)
        {
            if (!instrumentIds.TryGetValue(action.Symbol, out var instrumentId))
            {
                unknown.Add(action.Symbol);
                continue;
            }

            if (!existing.Add((instrumentId, action.ActionType, action.ActionDate)))
            {
                unchanged++;
                continue;
            }

            _db.CorporateActions.Add(new CorporateAction
            {
                InstrumentId = instrumentId,
                ActionType = action.ActionType,
                ActionDate = action.ActionDate,
                TermsJson = action.Terms.ValueKind == System.Text.Json.JsonValueKind.Undefined
                    ? null
                    : action.Terms.GetRawText(),
                SourceRef = sourceRef,
                RecordedAt = recordedAt,
            });
            inserted++;
        }

        if (inserted > 0)
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new CorporateActionWriteResult(inserted, unchanged, unknown.ToList());
    }
}
