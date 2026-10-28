using Degerli.Ingestion.Validation;
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
/// <c>(instrument, source, period_type, period_end_date, statement_type)</c>, so a period may be
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

    // 02 §3.1 — the source's declared unit scale (FU §2) and its factor to single units.
    private static readonly IReadOnlyDictionary<string, decimal> UnitScaleFactors =
        new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            ["units"] = 1m,
            ["thousands"] = 1_000m,
            ["millions"] = 1_000_000m,
        };

    // 02 §3.1/§5.9 — the macro series a non-TRY reporting currency is converted from.
    private static readonly IReadOnlyDictionary<string, string> FxSeriesByCurrency =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["USD"] = "USD_TRY",
            ["EUR"] = "EUR_TRY",
        };

    // Share-count lines are normalized by scale only — they are counts, not money, so no
    // FX conversion applies (02 §3.1).
    private static readonly HashSet<string> ShareCountCodes =
        new(StringComparer.Ordinal) { "SHARES_DILUTED", "SHARES_OUT" };

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

        // Nothing to persist: an empty statement set is a deliberate no-op, never a
        // schema failure. The per-company identity guard narrows a body whose identity
        // does not match the bound symbol to an empty set (01 v1.5 §10.9.10, FR-MDF-008),
        // so it must return clean here — validating scale/currency first would otherwise
        // refuse it with SCHEMA_MISMATCH and quarantine the *other* company's raw body.
        if (payload.Statements.Count == 0)
        {
            return new StatementWriteResult(payload.Symbol, 0, 0, []);
        }

        // The payload's scale/currency declaration is mandatory and must be valid: an
        // undeclared or unknown scale/currency is a payload-schema failure (02 §3.1,
        // TC-MDF-055) — never a silent guess.
        var currency = payload.Currency;
        var isTry = string.Equals(currency, "TRY", StringComparison.Ordinal);
        if (payload.UnitScale is null
            || !UnitScaleFactors.TryGetValue(payload.UnitScale, out var scale)
            || currency is null
            || !(isTry || FxSeriesByCurrency.ContainsKey(currency)))
        {
            return new StatementWriteResult(payload.Symbol, 0, 0, [QuarantineReason.SchemaMismatch]);
        }

        var instrumentId = await _db.Instruments
            .Where(i => i.Symbol == payload.Symbol)
            .Select(i => (long?)i.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (instrumentId is null)
        {
            return new StatementWriteResult(payload.Symbol, 0, 0, [QuarantineReason.SchemaMismatch]);
        }

        // Period-end FX per distinct period end (02 §5.9): the latest macro_values row for
        // the currency's series at or before the period end. A non-TRY statement with no
        // such row is refused with MISSING_FX_RATE, never converted at a guessed rate.
        var fxByPeriodEnd = new Dictionary<DateOnly, decimal?>();
        if (!isTry)
        {
            var seriesCode = FxSeriesByCurrency[currency!];
            foreach (var periodEnd in payload.Statements.Select(s => s.PeriodEndDate).Distinct())
            {
                var rate = await _db.MacroValues
                    .Where(m => m.SeriesCode == seriesCode && m.ValueDate <= periodEnd)
                    .OrderByDescending(m => m.ValueDate)
                    .ThenByDescending(m => m.RecordedAt)
                    .Select(m => (decimal?)m.Value)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
                fxByPeriodEnd[periodEnd] = rate;
            }
        }

        var existing = await _db.FinancialStatements
            .Where(s => s.InstrumentId == instrumentId)
            .Select(s => new { s.PeriodType, s.PeriodEndDate, s.StatementType, s.SourceRef, s.Version, s.RestatementDate })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Per natural key: the versions already stored (so an as-reported re-send is a
        // no-op) and the restatement publication dates already stored (so an identical
        // restatement is a no-op while a new one becomes the next sequence number).
        var versions = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        var restatementDates = new Dictionary<string, HashSet<DateOnly?>>(StringComparer.Ordinal);
        foreach (var s in existing)
        {
            var key = Key(instrumentId.Value, s.SourceRef, s.PeriodType, s.PeriodEndDate, s.StatementType);
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
            // Duplicate-concept rule (01 v1.5 §10.9.5, DR-05 overturned): a group whose
            // canonical item code appeared more than once with unequal values is a
            // validation event — quarantined with both values retained, never a silent
            // latest-wins. The conflicting group is not stored; the payload's other groups
            // ingest normally (partial batch).
            if (fact.ConflictingItemCodes is { Count: > 0 })
            {
                rejected.Add(QuarantineReason.ConflictingValue);
                continue;
            }

            var key = Key(instrumentId.Value, sourceRef, fact.PeriodType, fact.PeriodEndDate, fact.StatementType);

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

            // A non-TRY statement with no period-end rate is refused, never converted at a
            // guessed rate (02 §3.1/§5.9, TC-MDF-055). Only reached when the fact is new —
            // an idempotent re-send of an already-stored fact stays a no-op.
            decimal? fxRate = null;
            if (!isTry)
            {
                fxRate = fxByPeriodEnd[fact.PeriodEndDate];
                if (fxRate is null)
                {
                    rejected.Add(QuarantineReason.MissingFxRate);
                    continue;
                }
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
                PeriodStartDate = fact.PeriodStartDate,
                DurationMonths = fact.DurationMonths,
                Consolidated = fact.Consolidated,
                SourceUnitScale = payload.UnitScale!,
                SourceCurrency = currency!,
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
                    Value = Normalize(itemCode, value, scale, fxRate),
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

    /// <summary>
    /// Normalizes one mapped line to raw single-unit TRY (02 §3.1): the source's declared
    /// scale is applied to every value; a non-TRY reporting currency is then converted at
    /// the period-end rate. Share-count lines scale only — a share count is not money, so
    /// no FX conversion applies.
    /// </summary>
    private static decimal Normalize(string itemCode, decimal value, decimal scale, decimal? fxRate)
    {
        var normalized = value * scale;
        if (fxRate is decimal rate && !ShareCountCodes.Contains(itemCode))
        {
            normalized *= rate;
        }

        return normalized;
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

    /// <summary>
    /// The store's natural key, source included (<c>01</c> v1.5.57 §10.9.19, TC-MDF-111): the
    /// source is part of the key, so the same instrument may hold a fact for the same period from
    /// more than one source (the VYK lane's <c>kap</c> and the public-site lane's
    /// <c>kap-public</c>), and the restatement sequence <c>version</c> is counted per source —
    /// never across sources, because a fact from another source is not a restatement of this
    /// one (02 §3.1).
    /// </summary>
    private static string Key(
        long instrumentId,
        string sourceRef,
        string periodType,
        DateOnly periodEndDate,
        string statementType) =>
        string.Join('|', instrumentId, sourceRef, periodType, periodEndDate.ToString("yyyy-MM-dd"), statementType);
}
