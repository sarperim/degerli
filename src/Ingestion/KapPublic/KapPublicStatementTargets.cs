using Degerli.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Ingestion.KapPublic;

/// <summary>
/// The public-site statements lane's <b>write-side facts it reads before it writes</b>
/// (01 v1.5.56 §10.9.19, TKT-foundation-082; FR-MDF-008/012): which instrument a bound symbol is,
/// and which of the periods this lane is about to write are <b>already held by the other source</b>.
/// </summary>
/// <remarks>
/// Both reads are of <b>our</b> store, never of a source: the identity this lane walks with comes
/// from TKT-foundation-080's persisted mapping (never a symbol's spelling), and the coexistence
/// question is asked of <c>financial_statements</c>' own provenance — never of a fresh fetch.
/// </remarks>
public sealed class KapPublicStatementTargets
{
    private readonly DegerliDbContext _db;

    public KapPublicStatementTargets(DegerliDbContext db) => _db = db;

    /// <summary>
    /// The instrument ids of the bound symbols, in one read per run. A symbol with no
    /// <c>instruments</c> row is simply absent (the caller records the named skip — it never
    /// invents an instrument).
    /// </summary>
    public async Task<IReadOnlyDictionary<string, long>> GetInstrumentIdsAsync(
        IReadOnlyCollection<string> symbols,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        if (symbols.Count == 0)
        {
            return new Dictionary<string, long>(StringComparer.Ordinal);
        }

        var wanted = symbols.Distinct(StringComparer.Ordinal).ToList();
        var rows = await _db.Instruments
            .Where(instrument => wanted.Contains(instrument.Symbol))
            .Select(instrument => new { instrument.Symbol, instrument.Id })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ToDictionary(row => row.Symbol, row => row.Id, StringComparer.Ordinal);
    }

    /// <summary>
    /// The statement rows <paramref name="instrumentId"/> already holds from a source
    /// <b>other than</b> <paramref name="laneSource"/>, keyed by the store's natural key
    /// (period type | period end | statement type) and valued by the incumbent's provenance.
    /// <para>
    /// This is the <b>two-source coexistence</b> question (TC-MDF-110): for a period the other
    /// source already covers, this lane must not write, must not overwrite and must not relabel —
    /// the incumbent row stays as it is, and the overlap is named and counted instead of being
    /// swallowed as an idempotent <c>unchanged</c>.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> GetForeignPeriodsAsync(
        long instrumentId,
        string laneSource,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(laneSource);

        var rows = await _db.FinancialStatements
            .Where(statement => statement.InstrumentId == instrumentId
                && statement.SourceRef != laneSource)
            .Select(statement => new
            {
                statement.PeriodType,
                statement.PeriodEndDate,
                statement.StatementType,
                statement.SourceRef,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // The first incumbent recorded for a key is the one named: the store's natural key is
        // (instrument, period, statement type), so at most one provenance answers per key today.
        var foreign = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            foreign.TryAdd(Key(row.PeriodType, row.PeriodEndDate, row.StatementType), row.SourceRef);
        }

        return foreign;
    }

    /// <summary>
    /// The natural key this lane asks about — the <b>same</b> shape
    /// <see cref="Statements.FinancialStatementStore"/> keys on
    /// (<c>instrument|periodType|periodEnd|statementType</c>), minus the instrument, which the
    /// query already scoped.
    /// </summary>
    public static string Key(string periodType, DateOnly periodEndDate, string statementType) =>
        string.Join(
            '|',
            periodType,
            periodEndDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            statementType);
}
