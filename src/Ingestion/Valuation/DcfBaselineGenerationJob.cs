using System.Text.Json;
using Degerli.Core.Metrics;
using Degerli.Ingestion.Jobs;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.Valuation;

/// <summary>
/// The <c>dcf-baselines</c> job (C3b; <c>02</c> §5.8, §3.5; UC-VAL-003 generation side,
/// BR-VAL-008, FR-VAL-001..003). Derives one <b>active</b> <c>dcf_baselines</c> row per
/// covered stock from the canonical facts — base FCF (indirect method: NI + D&amp;A −
/// ΔWC − CAPEX), total debt (ST+LT), cash and diluted share count — combined with the
/// builder default rules for growth/horizon/rates (<see cref="DcfBaselineOptions"/>).
/// <para>
/// Baselines are <i>derived rows</i>, never hand-edited content. Every run of the job
/// regenerates from the canonical facts and <b>bumps the version</b> (one active version
/// per stock, prior versions retained) — the admin/CLI regeneration path (TKT-val-009)
/// and the build cycle call this same job (the per-job registration convention of
/// TKT-mdf-008).
/// </para>
/// <para>
/// The stored <c>params_json</c> also carries the canonical-fact provenance the read
/// endpoint serves (the <c>asOf</c> / <c>restated</c> refs per fact, UXR-VAL-010) and
/// the list of statement-derived facts that were missing (e.g. PART's absent cash-flow
/// statement → base FCF missing) so the calculator can explain instead of computing on
/// partial data (UXR-VAL-009).
/// </para>
/// </summary>
public sealed class DcfBaselineGenerationJob : IIngestJob
{
    /// <summary>Stable job code used by the ledger, scheduler and admin trigger.</summary>
    public const string Code = "dcf-baselines";

    // Canonical chart-of-accounts codes (02 §6.1), matching the Metrics Engine.
    private const string StatementIncome = "IS";
    private const string StatementBalanceSheet = "BS";
    private const string StatementCashFlow = "CF";
    private const string Restated = "restated";
    private const string Ni = "NI";
    private const string DeprAmort = "DEPR_AMORT";
    private const string Capex = "CAPEX";
    private const string Dwc = "ΔWC";
    private const string Cash = "CASH";
    private const string StDebt = "ST_DEBT";
    private const string LtDebt = "LT_DEBT";
    private const string SharesDiluted = "SHARES_DILUTED";

    /// <summary>Wire order of the confirmed parameter set (OQ-UX-001).</summary>
    private static readonly string[] ParameterOrder =
    {
        "base_fcf",
        "growth_rate",
        "horizon_years",
        "terminal_growth",
        "discount_rate",
        "debt",
        "cash",
        "share_count",
    };

    private readonly DegerliDbContext _db;
    private readonly DcfBaselineOptions _defaults;

    public DcfBaselineGenerationJob(
        DegerliDbContext db,
        IOptions<DcfBaselineOptions> defaults)
    {
        _db = db;
        _defaults = defaults.Value;
    }

    /// <inheritdoc />
    public string JobCode => Code;

    /// <inheritdoc />
    public async Task<IngestResult> RunAsync(
        IngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        // The request date is the baseline's build date / canonical-fact as-of date
        // (`02` §5.8 build-time versioning; FU §11.5's "asOf R−6d").
        var buildDate = request.Date;

        var instruments = await _db.Instruments
            .Where(i => i.Status == "active")
            .Select(i => new { i.Id, i.Symbol })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (instruments.Count == 0)
        {
            return new IngestResult(Code, 0, 0, 0, IngestResult.Succeeded);
        }

        var instrumentIds = instruments.Select(i => i.Id).ToList();

        var statements = await _db.FinancialStatements
            .Where(s => instrumentIds.Contains(s.InstrumentId) && s.PeriodType == "FY")
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var statementIds = statements.Select(s => s.Id).ToList();
        var lines = await _db.FinLineItems
            .Where(l => statementIds.Contains(l.StatementId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var linesByStatement = lines
            .GroupBy(l => l.StatementId)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(l => l.ItemCode, l => l.Value, StringComparer.Ordinal));

        var existingBaselines = await _db.DcfBaselines
            .Where(b => instrumentIds.Contains(b.InstrumentId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var baselinesByInstrument = existingBaselines
            .GroupBy(b => b.InstrumentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var written = 0;
        foreach (var instrument in instruments)
        {
            var instrumentStatements = statements
                .Where(s => s.InstrumentId == instrument.Id)
                .ToList();

            var paramsJson = DeriveBaseline(instrumentStatements, linesByStatement, buildDate);

            baselinesByInstrument.TryGetValue(instrument.Id, out var existing);
            var nextVersion = existing is { Count: > 0 }
                ? existing.Max(b => b.Version) + 1
                : 1;

            if (existing is not null)
            {
                // One active baseline per stock (`02` §3.5): retire the previous active row.
                foreach (var prior in existing.Where(b => b.IsActive))
                {
                    prior.IsActive = false;
                }
            }

            _db.DcfBaselines.Add(new DcfBaseline
            {
                InstrumentId = instrument.Id,
                Version = nextVersion,
                ParamsJson = paramsJson,
                BuildDate = buildDate,
                IsActive = true,
            });
            written++;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new IngestResult(Code, written, 0, 0, IngestResult.Succeeded);
    }

    /// <summary>
    /// Derives the baseline <c>params_json</c> for one instrument: the four canonical
    /// fact values (latest FY, restated version preferred) plus the builder defaults,
    /// alongside the per-fact provenance refs and the missing-fact list.
    /// </summary>
    private string DeriveBaseline(
        IReadOnlyList<FinancialStatement> statements,
        IReadOnlyDictionary<long, Dictionary<string, decimal>> lines,
        DateOnly buildDate)
    {
        var periods = statements
            .Select(s => s.PeriodEndDate)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        // Latest restated version serves; as-reported is retained (02 §5.7, BR-MDF-011).
        FinancialStatement? Pick(DateOnly periodEnd, string statementType)
            => statements
                .Where(s => s.PeriodEndDate == periodEnd && s.StatementType == statementType)
                .OrderByDescending(s => s.Version == Restated)
                .FirstOrDefault();

        decimal? Line(FinancialStatement? statement, string code)
        {
            if (statement is null
                || !lines.TryGetValue(statement.Id, out var map)
                || !map.TryGetValue(code, out var value))
            {
                return null;
            }

            return value;
        }

        FinancialStatement? income = null;
        FinancialStatement? balance = null;
        FinancialStatement? cashFlow = null;
        if (periods.Count > 0)
        {
            var latest = periods[^1];
            income = Pick(latest, StatementIncome);
            balance = Pick(latest, StatementBalanceSheet);
            cashFlow = Pick(latest, StatementCashFlow);
        }

        var baseFcf = CanonicalMetrics.FreeCashFlow(
            Line(income, Ni),
            Line(income, DeprAmort),
            Line(cashFlow, Dwc),
            Line(cashFlow, Capex));
        var cash = Line(balance, Cash);
        var shortTermDebt = Line(balance, StDebt);
        var longTermDebt = Line(balance, LtDebt);
        var debt = shortTermDebt is not null && longTermDebt is not null
            ? shortTermDebt.Value + longTermDebt.Value
            : (decimal?)null;
        var shareCount = Line(balance, SharesDiluted);

        // A fact is restated when the statement it was read from is the restated version.
        var fcfRested = income?.Version == Restated || cashFlow?.Version == Restated;
        var balanceRested = balance?.Version == Restated;

        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        var refs = new Dictionary<string, object?>(StringComparer.Ordinal);
        var missing = new List<string>();

        void Fact(string name, decimal? value, bool restated)
        {
            if (value is null)
            {
                missing.Add(name);
                return;
            }

            parameters[name] = value.Value;
            refs[name] = new Dictionary<string, object?>
            {
                ["asOf"] = buildDate,
                ["restated"] = restated,
            };
        }

        parameters["growth_rate"] = _defaults.GrowthRate;
        parameters["horizon_years"] = _defaults.HorizonYears;
        parameters["terminal_growth"] = _defaults.TerminalGrowth;
        parameters["discount_rate"] = _defaults.DiscountRate;

        Fact("base_fcf", baseFcf, fcfRested);
        Fact("debt", debt, balanceRested);
        Fact("cash", cash, balanceRested);
        Fact("share_count", shareCount, balanceRested);

        var ordered = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var name in ParameterOrder)
        {
            if (parameters.TryGetValue(name, out var value))
            {
                ordered[name] = value;
            }
        }

        var envelope = new Dictionary<string, object?>
        {
            ["params"] = ordered,
            ["canonicalFactRefs"] = refs,
            ["missingInputs"] = missing,
        };

        return JsonSerializer.Serialize(envelope);
    }
}
