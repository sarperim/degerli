using System.Text.Json;
using Degerli.Core.Adjustments;
using Degerli.Core.Metrics;
using Degerli.Ingestion.Statements;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Ingestion.Metrics;

/// <summary>
/// The computed metric set for one instrument at one date: the canonical codes plus
/// the coverage boundary the honest no-data state needs (FR-MDF-011, UC-MDF-005 alt a).
/// </summary>
/// <param name="Symbol">Instrument ticker.</param>
/// <param name="ListingDate">IPO date — the coverage boundary for a short history.</param>
/// <param name="Metrics">All canonical metric codes (<see cref="MetricCodes.All"/>; NULL = not meaningful).</param>
public sealed record InstrumentMetricResult(
    string Symbol,
    DateOnly? ListingDate,
    IReadOnlyList<ComputedMetric> Metrics);

/// <summary>
/// The write plan for one recompute pass (<c>01</c> §10.9.13, TKT-foundation-050): the
/// computed results whose stored same-day rows differ from the current computation — the only
/// rows that need writing — plus the honest counts the run reports. A row that is already
/// identical is <b>not</b> in <see cref="ToWrite"/>: it is never rewritten, and it is counted
/// in <see cref="Unchanged"/>. A row whose stored values no longer match the current facts is
/// in <see cref="ToWrite"/> (the store overwrites it in place, one row per key) and is counted
/// in <see cref="Rewritten"/>. Rows with no stored row are in <see cref="ToWrite"/> and are
/// the store's <c>Written</c> count.
/// <para>
/// A stored row is also <see cref="Rewritten"/> — not <see cref="Unchanged"/> — when its
/// definition stamp is not the current one, even though its values are identical
/// (<see cref="FormulaVersionRewritten"/>, <c>01</c> §10.9.13 / TKT-foundation-070).
/// </para>
/// </summary>
/// <param name="ToWrite">The computed rows to upsert (new rows + input-changed rows).</param>
/// <param name="Unchanged">The recomputed rows already identical to their stored row.</param>
/// <param name="Rewritten">The rows repaired in place because their inputs changed.</param>
/// <param name="FormulaVersionRewritten">
/// The subset of <paramref name="Rewritten"/> whose stored values were <b>identical</b> and
/// only their definition version differed (<see cref="CanonicalMetrics.Version"/>,
/// TKT-foundation-070) — the "a formula changed, the numbers did not" rewrite, reported apart
/// from a fact repair so the two are never confused.
/// </param>
public sealed record MetricWritePlan(
    IReadOnlyList<InstrumentMetricResult> ToWrite,
    int Unchanged,
    int Rewritten,
    int FormulaVersionRewritten = 0);

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

    // ── the key-value & moat panel's inputs (02 §6.1/§6.2, v2; TKT-foundation-096) ──
    // Every one is a canonical line 093 landed; nothing here widens a DR-1/DR-2 fence —
    // ST_DEBT/LT_DEBT and TOTAL_EQUITY are consumed exactly as stored.
    private const string TotalAssets = "TOTAL_ASSETS";
    private const string Goodwill = "GOODWILL";
    private const string IntangiblesNet = "INTANGIBLES_NET";
    private const string NetMonetaryGl = "NET_MONETARY_GL";
    private const string InterestPaid = "INTEREST_PAID";

    private readonly DegerliDbContext _db;

    public MetricsEngine(DegerliDbContext db) => _db = db;

    /// <summary>
    /// Resolves the day this lane computes over: the latest stored trading day at or before
    /// <paramref name="onOrBefore"/> in the covered universe, via the one implementation of
    /// that rule (<see cref="MarketSnapshotStore.ResolveLatestStoredTradingDayAsync(DegerliDbContext, DateOnly, CancellationToken)"/>,
    /// <c>01</c> §10.9.13) — TKT-foundation-089. Stored prices are the truth: a request for a
    /// day with no stored prices (a weekend, a cold start, a missed run) recomputes the latest
    /// real session rather than writing nothing and reporting a green <c>0/0</c>.
    /// </summary>
    public Task<DateOnly?> ResolveComputeDayAsync(
        DateOnly onOrBefore,
        CancellationToken cancellationToken = default) =>
        MarketSnapshotStore.ResolveLatestStoredTradingDayAsync(_db, onOrBefore, cancellationToken);

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

            var (input, epsAdjusted, panelAdjusted) = BuildInput(
                price, instrument.AccountingProfile, instrumentStatements, linesByStatement, instrumentDividends, instrumentActions, asOfDate);

            // The corporate-action basis marker is per metric (FR-MDF-018): only the
            // per-share growth CAGRs computed from a genuinely adjusted per-share series
            // carry it. The EPS/DPS families and the panel's BVPS/FCF-per-share families are
            // marked from their own series' Π factors, so an instrument whose only adjusted
            // series belongs to one family never re-stamps the other family's rows (the
            // pre-v2 EPS/DPS rows stay byte-identical — TKT-foundation-096 non-regression).
            IReadOnlyList<ComputedMetric> metrics = CanonicalMetrics.Compute(input);
            if (epsAdjusted)
            {
                metrics = metrics
                    .Select(m => IsEpsDividendGrowth(m.Code) ? AdjustedPerShareMetric(m) : m)
                    .ToList();
            }

            if (panelAdjusted)
            {
                metrics = metrics
                    .Select(m => IsPanelPerShareGrowth(m.Code) ? AdjustedPerShareMetric(m) : m)
                    .ToList();
            }

            results.Add(new InstrumentMetricResult(
                instrument.Symbol,
                instrument.ListingDate,
                metrics));
        }

        return results;
    }

    /// <summary>
    /// Classifies a computed metric set against the stored same-day rows — the input-aware
    /// half of the recompute (<c>01</c> §10.9.13, extended to this lane by
    /// TKT-foundation-050). The recompute is never a blind overwrite: a row whose stored
    /// values already match the current computation is reported <c>unchanged</c> and left
    /// untouched (its <c>computed_at</c> does not move), while a row whose inputs changed is
    /// repaired in place and counted as rewritten — so operational repair is a plain re-run
    /// and the run's own stats say whether anything was repaired.
    /// <para>
    /// Only the resolved <paramref name="asOfDate"/>'s rows are addressed, so historical days
    /// stay immutable (FR-MDF-015/NFR-MDF-002); a stored row for a metric this run no longer
    /// computes is not a repair target and is left alone.
    /// </para>
    /// <para>
    /// <b>Definition-aware since TKT-foundation-070</b> (<c>01</c> §10.9.13): a stored row whose
    /// <c>formula_version</c> is not <see cref="CanonicalMetrics.Version"/> is rewritten — and
    /// counted in <see cref="MetricWritePlan.FormulaVersionRewritten"/> when its values were
    /// identical — because the row was produced by a different definition and its stamp is
    /// untrue. That is the one non-value reason a recompute writes.
    /// </para>
    /// </summary>
    public async Task<MetricWritePlan> PlanWriteAsync(
        IReadOnlyList<InstrumentMetricResult> computed,
        DateOnly asOfDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(computed);

        var instrumentIds = await _db.Instruments
            .ToDictionaryAsync(i => i.Symbol, i => i.Id, cancellationToken)
            .ConfigureAwait(false);

        var stored = (await _db.DerivedMetrics
                .Where(m => m.AsOfDate == asOfDate)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(m => (m.InstrumentId, m.MetricCode), MetricKeyComparer.Instance);

        var toWrite = new List<InstrumentMetricResult>(computed.Count);
        var unchanged = 0;
        var rewritten = 0;
        var formulaVersionRewritten = 0;
        foreach (var result in computed)
        {
            if (!instrumentIds.TryGetValue(result.Symbol, out var instrumentId))
            {
                continue;
            }

            var pending = new List<ComputedMetric>(result.Metrics.Count);
            foreach (var metric in result.Metrics)
            {
                if (stored.TryGetValue((instrumentId, metric.Code), out var row))
                {
                    var sameValues = HasSameValues(row, metric);
                    var currentVersion = HasCurrentFormulaVersion(row);
                    if (sameValues && currentVersion)
                    {
                        // Identical inputs → identical outputs: nothing to write, and the run
                        // reports the honest unchanged count instead of a silent vacuum.
                        unchanged++;
                        continue;
                    }

                    rewritten++;
                    if (sameValues)
                    {
                        // The values are identical and only the definition moved (or was never
                        // recorded): a formula-version rewrite, counted apart from a fact repair
                        // so "a formula changed" and "a fact changed" are never conflated
                        // (01 §10.9.13, TKT-foundation-070).
                        formulaVersionRewritten++;
                    }
                }

                pending.Add(metric);
            }

            if (pending.Count > 0)
            {
                toWrite.Add(result with { Metrics = pending });
            }
        }

        return new MetricWritePlan(toWrite, unchanged, rewritten, formulaVersionRewritten);
    }

    /// <summary>
    /// True when the stored row already carries the computed metric. Compares the
    /// input-derived values only — never <c>computed_at</c>, which stamps the write, not the
    /// facts — and compares the value in the stored column's own precision
    /// (<c>numeric(18,6)</c>): a recompute that reproduces a high-precision value is honestly
    /// unchanged, not a perpetual pseudo-repair.
    /// </summary>
    private static bool HasSameValues(DerivedMetric stored, ComputedMetric computed) =>
        stored.Value == AtStoredScale(computed.Value)
        && stored.WindowYears == computed.WindowYears
        && stored.IsAdjusted == computed.IsAdjusted
        && stored.IsRested == computed.IsRested
        && stored.RegimeBreak == computed.RegimeBreak;

    /// <summary>The persisted scale of <c>derived_metrics.value</c> (<c>02</c> §3.1).</summary>
    private const int StoredValueScale = 6;

    /// <summary>
    /// True when the stored row was produced by the current definition
    /// (<see cref="CanonicalMetrics.Version"/>, <c>01</c> §10.9.13 / TKT-foundation-070). A stamp
    /// that differs — including the honest <c>NULL</c> of a row written before versioning
    /// existed — means the row cannot claim to be the current formula's output, so the recompute
    /// rewrites it (and the store stamps the current version).
    /// </summary>
    private static bool HasCurrentFormulaVersion(DerivedMetric stored) =>
        stored.FormulaVersion == CanonicalMetrics.Version;

    /// <summary>Projects a computed value onto the column's precision (half away from zero, like the DB).</summary>
    private static decimal? AtStoredScale(decimal? value) =>
        value is null ? null : decimal.Round(value.Value, StoredValueScale, MidpointRounding.AwayFromZero);

    /// <summary>Value equality over the composite key, so the lookup is ordinal like the DB.</summary>
    private sealed class MetricKeyComparer : IEqualityComparer<(long InstrumentId, string MetricCode)>
    {
        public static readonly MetricKeyComparer Instance = new();

        public bool Equals((long InstrumentId, string MetricCode) x, (long InstrumentId, string MetricCode) y) =>
            x.InstrumentId == y.InstrumentId && string.Equals(x.MetricCode, y.MetricCode, StringComparison.Ordinal);

        public int GetHashCode((long InstrumentId, string MetricCode) obj) =>
            HashCode.Combine(obj.InstrumentId, StringComparer.Ordinal.GetHashCode(obj.MetricCode));
    }

    private static (CanonicalMetricsInput Input, bool EpsDividendAdjusted, bool PanelPerShareAdjusted) BuildInput(
        decimal price,
        string accountingProfile,
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

        // The restated version of the preferred source serves (02 §5.7, BR-MDF-011); since the
        // store's natural key includes the source (01 v1.5.57 §10.9.19, TC-MDF-111), the pinned
        // cross-source preference decides which source's fact is the current one — all of a
        // period's lines come from that one row, never a mixture of two filings.
        FinancialStatement? Pick(DateOnly periodEnd, string statementType)
            => StatementSourcePreference.Pick(
                statements.Where(s => s.PeriodEndDate == periodEnd && s.StatementType == statementType));

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
                CurLiab: Line(bs0, CurLiab),
                TotalAssets: Line(bs0, TotalAssets),
                Goodwill: Line(bs0, Goodwill),
                IntangiblesNet: Line(bs0, IntangiblesNet),
                NetMonetaryGl: Line(is0, NetMonetaryGl),
                CashInterest: Line(cf0, InterestPaid));
        }

        var revenue = new List<FiscalValue>();
        var ebitSeries = new List<FiscalValue>();
        var niSeries = new List<FiscalValue>();
        var eps = new List<FiscalValue>();
        var bvps = new List<FiscalValue>();
        var fcf = new List<FiscalValue>();
        var fcfPerShare = new List<FiscalValue>();
        var epsAdjusted = false;
        var panelAdjusted = false;
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

            var ebitY = Line(isY, Ebit);
            if (ebitY is not null)
            {
                ebitSeries.Add(new FiscalValue(period.Year, ebitY, period));
            }

            var niY = Line(isY, Ni);
            if (niY is not null)
            {
                niSeries.Add(new FiscalValue(period.Year, niY, period));
            }

            var shares = Line(bsY, SharesDiluted);
            if (niY is not null && shares is > 0m)
            {
                // Per-share CAGR basis (02 §6.2): the as-reported EPS is put on today's
                // share basis by Π over the actions after its fiscal period end.
                var factor = PriceAdjustment.FactorAfter(period, actions);
                epsAdjusted |= factor != AdjustmentFactors.None;
                eps.Add(new FiscalValue(period.Year, niY.Value / shares.Value * factor, period));
            }

            // BVPS and FCF-per-share follow the same per-share basis doctrine (02 §6.2, v2):
            // the identical Π factor, the identical share-count gate (no count → the year is
            // absent from the series, never a fabricated per-share figure — the user's
            // exclusion #4), and the identical adjusted-basis marker.
            var equityY = Line(bsY, TotalEquity);
            if (equityY is not null && shares is > 0m)
            {
                var factor = PriceAdjustment.FactorAfter(period, actions);
                panelAdjusted |= factor != AdjustmentFactors.None;
                bvps.Add(new FiscalValue(period.Year, equityY.Value / shares.Value * factor, period));
            }

            var freeCash = CanonicalMetrics.FreeCashFlow(
                Line(cfY, Ocf),
                Line(cfY, Capex));
            if (freeCash is not null)
            {
                fcf.Add(new FiscalValue(period.Year, freeCash, period));
                if (shares is > 0m)
                {
                    var factor = PriceAdjustment.FactorAfter(period, actions);
                    panelAdjusted |= factor != AdjustmentFactors.None;
                    fcfPerShare.Add(new FiscalValue(period.Year, freeCash.Value / shares.Value * factor, period));
                }
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
            epsAdjusted |= factor != AdjustmentFactors.None;
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
                IsAdjusted: false,
                AccountingProfile: accountingProfile,
                EbitByFiscalYear: ebitSeries,
                NiByFiscalYear: niSeries,
                BvpsByFiscalYear: bvps,
                FcfPsByFiscalYear: fcfPerShare),
            epsAdjusted,
            panelAdjusted);
    }

    /// <summary>
    /// Whether a metric code is an EPS or DPS growth CAGR — the families whose series the
    /// engine built before v2, marked from their own Π factors.
    /// </summary>
    private static bool IsEpsDividendGrowth(string code) =>
        code is MetricCodes.EpsCagr3y or MetricCodes.EpsCagr5y or MetricCodes.EpsCagr10y
            or MetricCodes.DivCagr3y or MetricCodes.DivCagr5y or MetricCodes.DivCagr10y;

    /// <summary>
    /// Whether a metric code is one of the panel's per-share growth CAGRs (BVPS or
    /// FCF-per-share, <c>02</c> §6.2 v2) — added by TKT-foundation-096 and on the same
    /// per-share basis doctrine as EPS/DPS.
    /// </summary>
    private static bool IsPanelPerShareGrowth(string code) =>
        code is MetricCodes.BvpsCagr3y or MetricCodes.BvpsCagr5y
            or MetricCodes.FcfPsCagr3y or MetricCodes.FcfPsCagr5y;

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
