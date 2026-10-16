using System.Text.Json;
using Degerli.Core.Adjustments;
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
    private const string Ocf = "OCF";
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

        // Corporate actions feed the per-share CAGR basis (FR-MDF-018, 02 §3.1/§6.2): the
        // historical EPS/DPS series are put on the same adjusted basis as close_adjusted.
        var actions = await _db.CorporateActions
            .Where(a => instrumentIds.Contains(a.InstrumentId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var statementsByInstrument = statements
            .GroupBy(s => s.InstrumentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<FinancialStatement>)g.ToList());
        var dividendsByInstrument = dividends
            .GroupBy(d => d.InstrumentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Dividend>)g.ToList());
        var actionsByInstrument = actions
            .GroupBy(a => a.InstrumentId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<DatedAction>)g
                    .Select(ToDatedAction)
                    .Where(a => a is not null)
                    .Select(a => a!)
                    .ToList());

        var results = new List<InstrumentMetricResult>(prices.Count);
        foreach (var (instrumentId, price) in prices)
        {
            if (!instruments.TryGetValue(instrumentId, out var instrument))
            {
                continue;
            }

            statementsByInstrument.TryGetValue(instrumentId, out var instrumentStatements);
            dividendsByInstrument.TryGetValue(instrumentId, out var instrumentDividends);
            actionsByInstrument.TryGetValue(instrumentId, out var instrumentActions);
            instrumentStatements ??= [];
            instrumentDividends ??= [];
            instrumentActions ??= [];

            var (input, adjusted) = BuildInput(
                price, instrumentStatements, linesByStatement, instrumentDividends, instrumentActions, asOfDate);

            // The corporate-action basis marker is per metric (FR-MDF-018): only the
            // per-share growth CAGRs computed from the adjusted EPS/DPS series carry it.
            IReadOnlyList<ComputedMetric> metrics = CanonicalMetrics.Compute(input);
            if (adjusted)
            {
                metrics = metrics
                    .Select(m => IsPerShareGrowth(m.Code) ? AdjustedPerShareMetric(m) : m)
                    .ToList();
            }

            results.Add(new InstrumentMetricResult(
                instrument.Symbol,
                instrument.ListingDate,
                metrics));
        }

        return results;
    }

    private static (CanonicalMetricsInput Input, bool PerShareAdjusted) BuildInput(
        decimal price,
        IReadOnlyList<FinancialStatement> statements,
        IReadOnlyDictionary<long, Dictionary<string, decimal>> lines,
        IReadOnlyList<Dividend> dividends,
        IReadOnlyList<DatedAction> actions,
        DateOnly asOfDate)
    {
        var periods = statements
            .Select(s => s.PeriodEndDate)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        // The highest version serves; earlier versions are retained (02 §5.7, BR-MDF-011).
        FinancialStatement? Pick(DateOnly periodEnd, string statementType)
            => statements
                .Where(s => s.PeriodEndDate == periodEnd && s.StatementType == statementType)
                .OrderByDescending(s => s.Version)
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

        FinancialStatement? is0 = null;
        FinancialStatement? bs0 = null;
        FinancialStatement? cf0 = null;
        CanonicalStatement ttm = new(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
        var isRested = false;

        if (periods.Count > 0)
        {
            var latest = periods[^1];
            is0 = Pick(latest, "IS");
            bs0 = Pick(latest, "BS");
            cf0 = Pick(latest, "CF");
            isRested = new[] { is0, bs0, cf0 }.Any(s => s is not null && s.Version > 0);
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
                Ocf: Line(cf0, Ocf),
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
        var perShareAdjusted = false;
        foreach (var period in periods)
        {
            var isY = Pick(period, "IS");
            var bsY = Pick(period, "BS");
            var cfY = Pick(period, "CF");

            var rev = Line(isY, Rev);
            if (rev is not null)
            {
                revenue.Add(new FiscalValue(period.Year, rev, period));
            }

            var ni = Line(isY, Ni);
            var shares = Line(bsY, SharesDiluted);
            if (ni is not null && shares is > 0m)
            {
                // Per-share CAGR basis (02 §6.2): the as-reported EPS is put on today's
                // share basis by Π over the actions after its fiscal period end.
                var factor = PriceAdjustment.FactorAfter(period, actions);
                perShareAdjusted |= factor != AdjustmentFactors.None;
                eps.Add(new FiscalValue(period.Year, ni.Value / shares.Value * factor, period));
            }

            var freeCash = CanonicalMetrics.FreeCashFlow(
                Line(cfY, Ocf),
                Line(cfY, Capex));
            if (freeCash is not null)
            {
                fcf.Add(new FiscalValue(period.Year, freeCash, period));
            }
        }

        var latestFiscalYear = periods.Count > 0 ? periods[^1].Year : 0;

        // FY DPS series: the annual dividend grouped by its ex-date fiscal year, bounded
        // to the last FY we have statements for (later rows are the TTM/current series).
        // Each dividend is put on today's basis by Π over the actions after its ex-date
        // (02 §6.2) before it is summed into its fiscal year.
        var dps = new List<FiscalValue>();
        var byFiscalYear = new SortedDictionary<int, decimal>();
        foreach (var dividend in dividends.Where(d => d.ExDate.Year <= latestFiscalYear))
        {
            var factor = PriceAdjustment.FactorAfter(dividend.ExDate, actions);
            perShareAdjusted |= factor != AdjustmentFactors.None;
            byFiscalYear[dividend.ExDate.Year] =
                byFiscalYear.GetValueOrDefault(dividend.ExDate.Year) + dividend.AmountPerShare * factor;
        }

        foreach (var (year, amount) in byFiscalYear)
        {
            // The DPS series is grouped by ex-date fiscal year; its period end is the
            // fiscal-year end used for the TMS 29 regime-break check (02 §6.2).
            dps.Add(new FiscalValue(year, amount, new DateOnly(year, 12, 31)));
        }

        // TTM dividend per share: dividends with an ex-date inside the trailing year,
        // including the boundary day (matches the fixture's DIV_TTM definition, FU §6).
        // The current-basis yield/payout read stays unadjusted (close-raw basis, §6.2).
        var ttmFrom = asOfDate.AddDays(-365);
        var ttmDividendPerShare = dividends
            .Where(d => d.ExDate >= ttmFrom && d.ExDate <= asOfDate)
            .Sum(d => d.AmountPerShare);

        return (
            new CanonicalMetricsInput(
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
                IsAdjusted: false),
            perShareAdjusted);
    }

    /// <summary>Whether a metric code is a per-share growth CAGR (EPS or DPS).</summary>
    private static bool IsPerShareGrowth(string code) =>
        code is MetricCodes.EpsCagr3y or MetricCodes.EpsCagr5y or MetricCodes.EpsCagr10y
            or MetricCodes.DivCagr3y or MetricCodes.DivCagr5y or MetricCodes.DivCagr10y;

    /// <summary>
    /// Marks a per-share CAGR as computed on the corporate-action-adjusted basis. The
    /// adjusted EPS/DPS history is only meaningful over a span it fully covers, so a
    /// window the series does not reach (FU §5.3: EPSL carries 4 FYs, so only the 3Y
    /// window has enough intervals) is reported as the honest no-value — never a
    /// shortened-span value presented as the nominal window.
    /// </summary>
    private static ComputedMetric AdjustedPerShareMetric(ComputedMetric metric)
    {
        if (metric.WindowYears is int used && used < NominalWindow(metric.Code))
        {
            // The window is discarded as an honest no-value, so its regime-break marker
            // goes with it — a flag never rides a metric that serves no value.
            return metric with { Value = null, WindowYears = null, IsAdjusted = true, RegimeBreak = null };
        }

        return metric with { IsAdjusted = true };
    }

    /// <summary>The nominal window (years) encoded in a window-suffixed CAGR code.</summary>
    private static int NominalWindow(string code) =>
        code switch
        {
            _ when code.EndsWith("_10y", StringComparison.Ordinal) => 10,
            _ when code.EndsWith("_5y", StringComparison.Ordinal) => 5,
            _ => 3,
        };

    /// <summary>
    /// Maps a stored corporate action onto the adjustment-factor system (02 §3.1). The
    /// persisted terms carry the ratio under the class's canonical key (<c>n</c> for a
    /// split, <c>b</c> for a bonus, <c>q</c> for a rights issue). An unrecognized class or
    /// unreadable terms yields no factor (the action is skipped, never guessed).
    /// </summary>
    private static DatedAction? ToDatedAction(CorporateAction action)
    {
        var type = action.ActionType switch
        {
            "split" => AdjustmentActionType.Split,
            "bonus_issue" => AdjustmentActionType.BonusIssue,
            "rights_issue" => AdjustmentActionType.RightsIssue,
            _ => (AdjustmentActionType?)null,
        };

        if (type is null || string.IsNullOrWhiteSpace(action.TermsJson))
        {
            return null;
        }

        var ratioKey = type.Value switch
        {
            AdjustmentActionType.Split => "n",
            AdjustmentActionType.BonusIssue => "b",
            _ => "q",
        };

        try
        {
            using var document = JsonDocument.Parse(action.TermsJson);
            var root = document.RootElement;
            if (!TryGetDecimal(root, ratioKey, out var ratio))
            {
                return null;
            }

            decimal? subscriptionPrice = null;
            if (type == AdjustmentActionType.RightsIssue
                && TryGetDecimal(root, "subscriptionPrice", out var price))
            {
                subscriptionPrice = price;
            }

            return new DatedAction(
                action.ActionDate,
                new AdjustmentAction(type.Value, ratio, subscriptionPrice));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryGetDecimal(JsonElement element, string property, out decimal value)
    {
        value = 0m;
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var raw)
            && raw.ValueKind == JsonValueKind.Number
            && raw.TryGetDecimal(out value);
    }
}
