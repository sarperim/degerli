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
    /// <b>other than</b> <paramref name="laneSource"/> — the <b>two-source coexistence</b> question
    /// (TC-MDF-110): for a period the other source already covers, this lane must not write, must not
    /// overwrite and must not relabel; the incumbent row stays as it is, and the overlap is named and
    /// counted instead of being swallowed as an idempotent <c>unchanged</c>.
    /// <para>
    /// The answer has <b>two</b> shapes, because the store's exact key is not the only way one real
    /// period can be covered: the exact <c>periodType|periodEnd|statementType</c> key, <b>and</b> the
    /// calendar period (same <c>periodEndDate</c> + <c>statementType</c>, different period type). VYK
    /// defaults to <c>FY</c> at 12-31 while this lane's decoder fails safe to <c>Q</c> for an unknown
    /// or duration-mismatched period, so a public annual report and a VYK annual row for the same
    /// 12-31 period can disagree on the period type — an exact-key-only check would then write a
    /// second row for one real period with no finding at all (review round 1, D5).
    /// </para>
    /// </summary>
    public async Task<ForeignStatementCoverage> GetForeignPeriodsAsync(
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

        var periods = rows
            .Select(row => new ForeignStatementPeriod(row.PeriodType, row.PeriodEndDate, row.StatementType, row.SourceRef))
            .ToList();

        // The first incumbent recorded for a key is the one named: the store's natural key is
        // (instrument, period, statement type), so at most one provenance answers per key today.
        return new ForeignStatementCoverage(periods);
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

/// <summary>One foreign-source statement period that covers a period this lane may not write over.</summary>
/// <param name="PeriodType">The <b>incumbent's</b> stored period type.</param>
/// <param name="PeriodEndDate">The calendar period end both sides agree on.</param>
/// <param name="StatementType">The statement type both sides agree on.</param>
/// <param name="SourceRef">The incumbent's provenance (named in the finding's evidence).</param>
public sealed record ForeignStatementPeriod(
    string PeriodType,
    DateOnly PeriodEndDate,
    string StatementType,
    string SourceRef);

/// <summary>
/// The foreign-source coverage of one instrument, answerable by the store's exact natural key
/// <b>and</b> by calendar period (review round 1, D5). Both answers mean "do not write"; the caller
/// names the exact-key case <c>SOURCE_OVERLAP_RETAINED</c> and the calendar case
/// <c>SOURCE_PERIOD_TYPE_DISAGREEMENT</c>, because the second is a period-type decode disagreement
/// that an operator must be able to see rather than a plain overlap.
/// </summary>
public sealed class ForeignStatementCoverage
{
    private readonly Dictionary<string, ForeignStatementPeriod> _exact;
    private readonly Dictionary<string, List<ForeignStatementPeriod>> _byCalendar;

    public ForeignStatementCoverage(IEnumerable<ForeignStatementPeriod> periods)
    {
        ArgumentNullException.ThrowIfNull(periods);

        _exact = new Dictionary<string, ForeignStatementPeriod>(StringComparer.Ordinal);
        _byCalendar = new Dictionary<string, List<ForeignStatementPeriod>>(StringComparer.Ordinal);
        foreach (var period in periods)
        {
            _exact.TryAdd(
                KapPublicStatementTargets.Key(period.PeriodType, period.PeriodEndDate, period.StatementType),
                period);

            var calendarKey = CalendarKey(period.PeriodEndDate, period.StatementType);
            if (!_byCalendar.TryGetValue(calendarKey, out var sameDate))
            {
                sameDate = [];
                _byCalendar.Add(calendarKey, sameDate);
            }

            sameDate.Add(period);
        }
    }

    /// <summary>The incumbent covering the <b>exact</b> natural key, when there is one.</summary>
    public bool TryExact(
        string periodType,
        DateOnly periodEndDate,
        string statementType,
        out ForeignStatementPeriod incumbent) =>
        _exact.TryGetValue(KapPublicStatementTargets.Key(periodType, periodEndDate, statementType), out incumbent!);

    /// <summary>
    /// The incumbents covering the same <b>calendar</b> period (period end + statement type) under
    /// <b>any</b> period type. A non-empty answer with no exact-key match is a period-type
    /// disagreement, not an overlap.
    /// </summary>
    public IReadOnlyList<ForeignStatementPeriod> AtCalendarPeriod(DateOnly periodEndDate, string statementType) =>
        _byCalendar.TryGetValue(CalendarKey(periodEndDate, statementType), out var periods)
            ? periods
            : [];

    private static string CalendarKey(DateOnly periodEndDate, string statementType) =>
        string.Join(
            '|',
            periodEndDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            statementType);
}
