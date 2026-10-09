using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Ingestion.Statements;

/// <summary>
/// Fact storage for <c>financial_statements</c> + <c>fin_line_items</c> (FR-MDF-002,
/// 02 §1). Statements are versioned (<c>as_reported</c> / <c>restated</c>) and append-only:
/// the natural key <c>(instrument, period_type, period_end_date, statement_type, version)</c>
/// makes a re-run an idempotent no-op and a restatement a new retained version (BR-MDF-011).
/// Line items are stored mapped to the canonical chart of accounts (<see cref="StatementMapper"/>).
/// </summary>
public sealed class FinancialStatementStore
{
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

        var existing = (await _db.FinancialStatements
                .Where(s => s.InstrumentId == instrumentId)
                .Select(s => new { s.PeriodType, s.PeriodEndDate, s.StatementType, s.Version })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .Select(s => Key(instrumentId.Value, s.PeriodType, s.PeriodEndDate, s.StatementType, s.Version))
            .ToHashSet(StringComparer.Ordinal);

        var inserted = 0;
        var unchanged = 0;

        foreach (var fact in payload.Statements)
        {
            if (!existing.Add(Key(instrumentId.Value, fact.PeriodType, fact.PeriodEndDate, fact.StatementType, fact.Version)))
            {
                unchanged++;
                continue;
            }

            var statement = new FinancialStatement
            {
                InstrumentId = instrumentId.Value,
                PeriodType = fact.PeriodType,
                PeriodEndDate = fact.PeriodEndDate,
                FiscalYear = fact.FiscalYear,
                StatementType = fact.StatementType,
                Version = fact.Version,
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
            inserted++;
        }

        return new StatementWriteResult(payload.Symbol, inserted, unchanged, []);
    }

    private static string Key(
        long instrumentId,
        string periodType,
        DateOnly periodEndDate,
        string statementType,
        string version) =>
        string.Join('|', instrumentId, periodType, periodEndDate.ToString("yyyy-MM-dd"), statementType, version);
}
