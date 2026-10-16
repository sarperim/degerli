using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Ingestion.Statements;

/// <summary>
/// Fact storage for <c>financial_statements</c> + <c>fin_line_items</c> (FR-MDF-002,
/// 02 §1). Statements carry an integer restatement sequence (<c>version</c>): <c>0</c>
/// = as-reported, <c>1, 2, …</c> = successive restatements in publication order (02
/// §1 principle 5, §3.1). The wire payload still <em>declares</em>
/// <c>as_reported</c>/<c>restated</c>; this store assigns the sequence per natural key
/// <c>(instrument, period_type, period_end_date, statement_type)</c>, so a period may be
/// restated repeatedly (e.g. successive TMS 29 re-basings) — BR-MDF-011. A restatement
/// is identified by its <see cref="StatementFact.RestatementDate"/>, so re-ingesting an
/// identical payload is an idempotent no-op and a genuinely new restatement becomes the
/// next version; a <c>restated</c> fact without a publication date is refused
/// (<c>SCHEMA_MISMATCH</c>) because 02 §3.1 reserves a NULL date for version 0. Line
/// items are stored mapped to the canonical chart of accounts
/// (<see cref="StatementMapper"/>).
/// </summary>
public sealed class FinancialStatementStore
{
    private const string AsReported = "as_reported";
    private const string Restated = "restated";

    private readonly DegerliDbContext _db;

    public FinancialStatementStore(DegerliDbContext db) => _db = db;

    public async Task<StatementWriteResult> UpsertAsync(
        string sourceRef,
        DateTimeOffset recordedAt,
        StatementsPayload payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRef);

        var instrumentId = await _db.Instruments
            .Where(i => i.Symbol == payload.Symbol)
            .Select(i => (long?)i.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (instrumentId is null)
        {
            return new StatementWriteResult(payload.Symbol, 0, 0, ["SCHEMA_MISMATCH"]);
        }

        var existing = await _db.FinancialStatements
            .Where(s => s.InstrumentId == instrumentId)
            .Select(s => new { s.PeriodType, s.PeriodEndDate, s.StatementType, s.Version, s.RestatementDate })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Per natural key: the versions already stored (so an as-reported re-send is a
        // no-op) and the restatement publication dates already stored (so an identical
        // restatement is a no-op while a new one becomes the next sequence number).
        var versions = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        var restatementDates = new Dictionary<string, HashSet<DateOnly?>>(StringComparer.Ordinal);
        foreach (var s in existing)
        {
            var key = Key(instrumentId.Value, s.PeriodType, s.PeriodEndDate, s.StatementType);
            Stored(versions, key).Add(s.Version);
            if (s.Version > 0)
            {
                Stored(restatementDates, key).Add(s.RestatementDate);
            }
        }

        var inserted = 0;
        var unchanged = 0;
        var rejected = new List<string>();

        foreach (var fact in payload.Statements)
        {
            var key = Key(instrumentId.Value, fact.PeriodType, fact.PeriodEndDate, fact.StatementType);

            int version;
            if (string.Equals(fact.Version, AsReported, StringComparison.Ordinal))
            {
                version = 0;
                if (Stored(versions, key).Contains(0))
                {
                    unchanged++;
                    continue;
                }
            }
            else if (string.Equals(fact.Version, Restated, StringComparison.Ordinal))
            {
                // A restatement carries its restating report's publication date (02 §3.1;
                // a NULL date is reserved for the as-reported version 0). Without a date
                // the restatement cannot be identified, so the fact is refused rather than
                // silently collapsing every later date-less restatement into a no-op.
                if (fact.RestatementDate is null)
                {
                    rejected.Add("SCHEMA_MISMATCH");
                    continue;
                }

                // A restatement is identified by its publication date.
                if (Stored(restatementDates, key).Contains(fact.RestatementDate))
                {
                    unchanged++;
                    continue;
                }

                // Restatements occupy 1, 2, … — never the as-reported slot 0 — even when
                // no prior version exists or the fact precedes its as-reported sibling.
                version = Math.Max(1, Stored(versions, key).DefaultIfEmpty(0).Max() + 1);
            }
            else
            {
                rejected.Add("SCHEMA_MISMATCH");
                continue;
            }

            var statement = new FinancialStatement
            {
                InstrumentId = instrumentId.Value,
                PeriodType = fact.PeriodType,
                PeriodEndDate = fact.PeriodEndDate,
                FiscalYear = fact.FiscalYear,
                StatementType = fact.StatementType,
                Version = version,
                RestatementDate = fact.RestatementDate,
                PublishedAt = fact.PublishedAt,
                SourceRef = sourceRef,
                RecordedAt = recordedAt,
            };
            _db.FinancialStatements.Add(statement);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            foreach (var (itemCode, value) in StatementMapper.Map(fact.Lines))
            {
                _db.FinLineItems.Add(new FinLineItem
                {
                    StatementId = statement.Id,
                    ItemCode = itemCode,
                    Value = value,
                });
            }

            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            Stored(versions, key).Add(version);
            if (version > 0)
            {
                Stored(restatementDates, key).Add(fact.RestatementDate);
            }

            inserted++;
        }

        // One quarantine row per payload per reason code — a payload is the unit of
        // inspection, so repeated refusals of the same class collapse to one entry.
        return new StatementWriteResult(payload.Symbol, inserted, unchanged, rejected.Distinct().ToList());
    }

    private static HashSet<T> Stored<T>(Dictionary<string, HashSet<T>> map, string key)
    {
        if (!map.TryGetValue(key, out var set))
        {
            set = [];
            map[key] = set;
        }

        return set;
    }

    private static string Key(
        long instrumentId,
        string periodType,
        DateOnly periodEndDate,
        string statementType) =>
        string.Join('|', instrumentId, periodType, periodEndDate.ToString("yyyy-MM-dd"), statementType);
}
