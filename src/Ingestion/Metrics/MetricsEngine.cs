using Degerli.Core.Metrics;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Ingestion.Metrics;

/// <summary>
/// The computed metric set for one instrument at one date: the 26 canonical codes plus
/// the coverage boundary the honest no-data state needs (FR-MDF-011, UC-MDF-005 alt a).
/// </summary>
/// <param name="Symbol">Instrument ticker.</param>
/// <param name="ListingDate">IPO date — the coverage boundary for a short history.</param>
/// <param name="Metrics">All 26 canonical metric codes (NULL = not meaningful).</param>
public sealed record InstrumentMetricResult(
    string Symbol,
    DateOnly? ListingDate,
    IReadOnlyList<ComputedMetric> Metrics);

/// <summary>
/// C3b Metrics Engine (FR-MDF-013/014, UC-MDF-005; AD-06): reads the canonical fact
/// tables and produces every metric any surface displays through the single shared
/// definition in <c>Degerli.Core.Metrics</c>. It performs no formula math of its own —
/// it assembles TTM facts, fiscal-year series and dividends and delegates to
/// <see cref="CanonicalMetrics"/> (one definition per number, BR-MDF-009).
/// </summary>
public sealed class MetricsEngine
{
    private const string Rev = "REV";
    private const string Cogs = "COGS";
    private const string GrossProfit = "GROSS_PROFIT";
    private const string Ebit = "EBIT";
    private const string DeprAmort = "DEPR_AMORT";
    private const string Ebitda = "EBITDA";
    private const string FinExp = "FIN_EXP";
    private const string PretaxInc = "PRETAX_INC";
    private const string TaxExp = "TAX_EXP";
    private const string Ni = "NI";
    private const string Capex = "CAPEX";
    private const string Dwc = "ΔWC";
    private const string Cash = "CASH";
    private const string StDebt = "ST_DEBT";
    private const string LtDebt = "LT_DEBT";
    private const string TotalEquity = "TOTAL_EQUITY";
    private const string CurAssets = "CUR_ASSETS";
    private const string CurLiab = "CUR_LIAB";
    private const string SharesDiluted = "SHARES_DILUTED";

    private readonly DegerliDbContext _db;

    public MetricsEngine(DegerliDbContext db) => _db = db;

    /// <summary>
    /// Computes the metric set for every instrument with a price on
    /// <paramref name="asOfDate"/> — the covered current universe at that date.
    /// </summary>
    public async Task<IReadOnlyList<InstrumentMetricResult>> ComputeAsync(
        DateOnly asOfDate,
        CancellationToken cancellationToken = default)
    {
        var prices = await _db.DailyPrices
            .Where(p => p.PriceDate == asOfDate)
            .ToDictionaryAsync(p => p.InstrumentId, p => p.CloseRaw, cancellationToken)
            .ConfigureAwait(false);

        if (prices.Count == 0)
        {
            return [];
        }

        var instrumentIds = prices.Keys.ToList();
        var instruments = await _db.Instruments
            .Where(i => instrumentIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, cancellationToken)
            .ConfigureAwait(false);

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

        var dividends = await _db.Dividends
            .Where(d => instrumentIds.Contains(d.InstrumentId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var statementsByInstrument = statements
            .GroupBy(s => s.InstrumentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<FinancialStatement>)g.ToList());
        var dividendsByInstrument = dividends
            .GroupBy(d => d.InstrumentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Dividend>)g.ToList());

        var results = new List<InstrumentMetricResult>(prices.Count);
        foreach (var (instrumentId, price) in prices)
        {
            if (!instruments.TryGetValue(instrumentId, out var instrument))
            {
                continue;
            }

            statementsByInstrument.TryGetValue(instrumentId, out var instrumentStatements);
            dividendsByInstrument.TryGetValue(instrumentId, out var instrumentDividends);
            instrumentStatements ??= [];
            instrumentDividends ??= [];

            var input = BuildInput(price, instrumentStatements, linesByStatement, instrumentDividends, asOfDate);
            results.Add(new InstrumentMetricResult(
                instrument.Symbol,
                instrument.ListingDate,
                CanonicalMetrics.Compute(input)));
        }

        return results;
    }

    private static CanonicalMetricsInput BuildInput(
        decimal price,
        IReadOnlyList<FinancialStatement> statements,
        IReadOnlyDictionary<long, Dictionary<string, decimal>> lines,
        IReadOnlyList<Dividend> dividends,
        DateOnly asOfDate)
    {
        var periods = statements
            .Select(s => s.PeriodEndDate)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        // Latest restated version serves; as-reported is retained (02 §5.7, BR-MDF-011).
        FinancialStatement? Pick(DateOnly periodEnd, string statementType)
        {
            var candidates = statements
                .Where(s => s.PeriodEndDate == periodEnd && s.StatementType == statementType)
                .ToList();
            return candidates.FirstOrDefault(s => s.Version == "restated")
                ?? candidates.FirstOrDefault(s => s.Version == "as_reported");
        }

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

        FinancialStatement? is0 = null;
        FinancialStatement? bs0 = null;
        FinancialStatement? cf0 = null;
        CanonicalStatement ttm = new(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
        var isRested = false;

        if (periods.Count > 0)
        {
            var latest = periods[^1];
            is0 = Pick(latest, "IS");
            bs0 = Pick(latest, "BS");
            cf0 = Pick(latest, "CF");
            isRested = new[] { is0, bs0, cf0 }.Any(s => s?.Version == "restated");
            ttm = new CanonicalStatement(
                Rev: Line(is0, Rev),
                Cogs: Line(is0, Cogs),
                GrossProfit: Line(is0, GrossProfit),
                Ebit: Line(is0, Ebit),
                DeprAmort: Line(is0, DeprAmort),
                Ebitda: Line(is0, Ebitda),
                FinExp: Line(is0, FinExp),
                PretaxInc: Line(is0, PretaxInc),
                TaxExp: Line(is0, TaxExp),
                Ni: Line(is0, Ni),
                Capex: Line(cf0, Capex),
                Dwc: Line(cf0, Dwc),
                Cash: Line(bs0, Cash),
                StDebt: Line(bs0, StDebt),
                LtDebt: Line(bs0, LtDebt),
                TotalEquity: Line(bs0, TotalEquity),
                CurAssets: Line(bs0, CurAssets),
                CurLiab: Line(bs0, CurLiab));
        }

        var revenue = new List<FiscalValue>();
        var eps = new List<FiscalValue>();
        var fcf = new List<FiscalValue>();
        foreach (var period in periods)
        {
            var isY = Pick(period, "IS");
            var bsY = Pick(period, "BS");
            var cfY = Pick(period, "CF");

            var rev = Line(isY, Rev);
            if (rev is not null)
            {
                revenue.Add(new FiscalValue(period.Year, rev));
            }

            var ni = Line(isY, Ni);
            var shares = Line(bsY, SharesDiluted);
            if (ni is not null && shares is > 0m)
            {
                eps.Add(new FiscalValue(period.Year, ni.Value / shares.Value));
            }

            var freeCash = CanonicalMetrics.FreeCashFlow(
                ni,
                Line(isY, DeprAmort),
                Line(cfY, Dwc),
                Line(cfY, Capex));
            if (freeCash is not null)
            {
                fcf.Add(new FiscalValue(period.Year, freeCash));
            }
        }

        var latestFiscalYear = periods.Count > 0 ? periods[^1].Year : 0;

        // FY DPS series: the annual dividend grouped by its ex-date fiscal year, bounded
        // to the last FY we have statements for (later rows are the TTM/current series).
        var dps = dividends
            .Where(d => d.ExDate.Year <= latestFiscalYear)
            .GroupBy(d => d.ExDate.Year)
            .Select(g => new FiscalValue(g.Key, g.Sum(d => d.AmountPerShare)))
            .OrderBy(p => p.FiscalYear)
            .ToList();

        // TTM dividend per share: dividends with an ex-date inside the trailing year,
        // including the boundary day (matches the fixture's DIV_TTM definition, FU §6).
        var ttmFrom = asOfDate.AddDays(-365);
        var ttmDividendPerShare = dividends
            .Where(d => d.ExDate >= ttmFrom && d.ExDate <= asOfDate)
            .Sum(d => d.AmountPerShare);

        return new CanonicalMetricsInput(
            Price: price,
            SharesDiluted: Line(bs0, SharesDiluted),
            Ttm: ttm,
            RevenueByFiscalYear: revenue,
            EpsByFiscalYear: eps,
            FcfByFiscalYear: fcf,
            DpsByFiscalYear: dps,
            DividendTtmPerShare: ttmDividendPerShare,
            HasEverPaidDividend: dividends.Count > 0,
            IsRested: isRested,
            IsAdjusted: false);
    }
}
