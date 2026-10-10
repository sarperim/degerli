namespace Degerli.Core.Metrics;

/// <summary>
/// The canonical metric-code catalog (02-data-model.md §6.2; FR-MDF-013).
/// Growth concepts are stored as <b>window-suffixed</b> codes (Q5 resolution
/// 2026-10-07), so the <c>derived_metrics</c> model stays flat and the plain
/// <c>(instrument, metric, as_of_date)</c> key already distinguishes windows.
/// 14 non-growth codes + 12 windowed codes = the 26 codes the Metrics Engine writes.
/// </summary>
public static class MetricCodes
{
    // ── valuation ────────────────────────────────────────────────────────────
    public const string Pe = "pe";
    public const string Pb = "pb";
    public const string EvEbitda = "ev_ebitda";
    public const string EvFcf = "ev_fcf";
    public const string FcfYield = "fcf_yield";

    // ── quality ──────────────────────────────────────────────────────────────
    public const string Roic = "roic";
    public const string Roe = "roe";
    public const string GrossMargin = "gross_margin";
    public const string OperatingMargin = "operating_margin";

    // ── financial health ─────────────────────────────────────────────────────
    public const string NetDebtEbitda = "net_debt_ebitda";
    public const string InterestCoverage = "interest_coverage";
    public const string CurrentRatio = "current_ratio";

    // ── dividends ────────────────────────────────────────────────────────────
    public const string DivYield = "div_yield";
    public const string PayoutRatio = "payout_ratio";

    // ── growth (window-suffixed) ─────────────────────────────────────────────
    public const string RevCagr3y = "rev_cagr_3y";
    public const string RevCagr5y = "rev_cagr_5y";
    public const string RevCagr10y = "rev_cagr_10y";
    public const string EpsCagr3y = "eps_cagr_3y";
    public const string EpsCagr5y = "eps_cagr_5y";
    public const string EpsCagr10y = "eps_cagr_10y";
    public const string FcfCagr3y = "fcf_cagr_3y";
    public const string FcfCagr5y = "fcf_cagr_5y";
    public const string FcfCagr10y = "fcf_cagr_10y";
    public const string DivCagr3y = "div_cagr_3y";
    public const string DivCagr5y = "div_cagr_5y";
    public const string DivCagr10y = "div_cagr_10y";

    /// <summary>The nominal CAGR windows (years), in catalog order.</summary>
    public static readonly IReadOnlyList<int> CagrWindows = [3, 5, 10];

    /// <summary>All 26 metric codes written by the engine (14 + 12 windowed).</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Pe, Pb, EvEbitda, EvFcf, FcfYield,
        Roic, Roe, GrossMargin, OperatingMargin,
        NetDebtEbitda, InterestCoverage, CurrentRatio,
        DivYield, PayoutRatio,
        RevCagr3y, RevCagr5y, RevCagr10y,
        EpsCagr3y, EpsCagr5y, EpsCagr10y,
        FcfCagr3y, FcfCagr5y, FcfCagr10y,
        DivCagr3y, DivCagr5y, DivCagr10y,
    ];

    /// <summary>Whether <paramref name="code"/> is a windowed growth CAGR.</summary>
    public static bool IsGrowth(string code) =>
        code.EndsWith("_cagr_3y", StringComparison.Ordinal)
        || code.EndsWith("_cagr_5y", StringComparison.Ordinal)
        || code.EndsWith("_cagr_10y", StringComparison.Ordinal);
}
