namespace Degerli.Core.Metrics;

/// <summary>
/// The canonical metric-code catalog (02-data-model.md §6.2; FR-MDF-013).
/// Growth concepts are stored as <b>window-suffixed</b> codes (Q5 resolution
/// 2026-10-07), so the <c>derived_metrics</c> model stays flat and the plain
/// <c>(instrument, metric, as_of_date)</c> key already distinguishes windows.
/// 14 non-growth codes + 12 windowed codes = the 26 codes the Metrics Engine wrote
/// through v1; the key-value/moat panel (TKT-foundation-096, v2) adds 11 quality/value
/// codes and 6 windowed growth codes, so the engine now writes <b>43</b> codes.
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

    // ── quality/value — the key-value & moat panel (02 §6.2, v2) ─────────────
    public const string Roa = "roa";
    public const string Croic = "croic";
    public const string EvEbit = "ev_ebit";
    public const string PFcf = "p_fcf";
    public const string PTbv = "p_tbv";
    public const string FcfNi = "fcf_ni";
    public const string FcfNi3y = "fcf_ni_3y";
    public const string CfoEbit = "cfo_ebit";
    public const string MonetaryGainPretax = "monetary_gain_pretax";
    public const string CfoInterest = "cfo_interest";
    public const string NetDebtFcf = "net_debt_fcf";

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

    // ── growth — the panel's EBIT / BVPS / FCF-per-share CAGRs (02 §6.2, v2) ──
    // No 10Y window for these: the user's exclusion #5 (a 10Y window is
    // impossible on the ~3.5-year stored depth, and the 3/5Y pair is what the
    // panel asks for). Window-honesty (`window_years`) applies unchanged.
    public const string EbitCagr3y = "ebit_cagr_3y";
    public const string EbitCagr5y = "ebit_cagr_5y";
    public const string BvpsCagr3y = "bvps_cagr_3y";
    public const string BvpsCagr5y = "bvps_cagr_5y";
    public const string FcfPsCagr3y = "fcf_ps_cagr_3y";
    public const string FcfPsCagr5y = "fcf_ps_cagr_5y";

    /// <summary>The nominal CAGR windows (years), in catalog order.</summary>
    public static readonly IReadOnlyList<int> CagrWindows = [3, 5, 10];

    /// <summary>All 43 metric codes written by the engine (25 + 18 windowed).</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Pe, Pb, EvEbitda, EvFcf, FcfYield,
        Roic, Roe, GrossMargin, OperatingMargin,
        Roa, Croic, EvEbit, PFcf, PTbv, FcfNi, FcfNi3y,
        CfoEbit, MonetaryGainPretax, CfoInterest, NetDebtFcf,
        NetDebtEbitda, InterestCoverage, CurrentRatio,
        DivYield, PayoutRatio,
        RevCagr3y, RevCagr5y, RevCagr10y,
        EpsCagr3y, EpsCagr5y, EpsCagr10y,
        FcfCagr3y, FcfCagr5y, FcfCagr10y,
        DivCagr3y, DivCagr5y, DivCagr10y,
        EbitCagr3y, EbitCagr5y,
        BvpsCagr3y, BvpsCagr5y,
        FcfPsCagr3y, FcfPsCagr5y,
    ];

    /// <summary>Whether <paramref name="code"/> is a windowed growth CAGR.</summary>
    public static bool IsGrowth(string code) =>
        code.EndsWith("_cagr_3y", StringComparison.Ordinal)
        || code.EndsWith("_cagr_5y", StringComparison.Ordinal)
        || code.EndsWith("_cagr_10y", StringComparison.Ordinal);

    /// <summary>
    /// The codes the key-value &amp; moat panel serves (TKT-foundation-096; the user's v0
    /// row set on TKT-foundation-095, "KEY VALUE METRICS &amp; MOAT INDICATORS"), in panel
    /// order. Every entry is a code the Metrics Engine writes — the endpoint reads these
    /// from <c>derived_metrics</c> and computes nothing (BR-RES-004).
    /// </summary>
    public static readonly IReadOnlyList<string> KeyMetricPanel =
    [
        Roic, Croic, Roe, Roa,
        FcfNi, FcfNi3y, CfoEbit, MonetaryGainPretax,
        EvEbit, EvEbitda, Pe, PFcf, FcfYield, Pb, PTbv, DivYield,
        NetDebtEbitda, NetDebtFcf, InterestCoverage, CfoInterest, CurrentRatio,
        RevCagr3y, RevCagr5y,
        EbitCagr3y, EbitCagr5y,
        EpsCagr3y, EpsCagr5y,
        FcfPsCagr3y, FcfPsCagr5y,
        BvpsCagr3y, BvpsCagr5y,
    ];
}
