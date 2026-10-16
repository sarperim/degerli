using Degerli.Core.Metrics;

namespace Degerli.Core.UnitTests.Metrics;

/// <summary>
/// L1 acceptance for the canonical free-cash-flow definition (TKT-mdf-017; TC-MDF-059,
/// FR-MDF-013, <c>02</c> §6.1/§6.2): <c>FCF = OCF − |CAPEX|</c>. Operating cash flow is
/// taken as the KAP statement reports it; the absolute value of CAPEX guards the KAP sign
/// convention where investing outflows are negative — a reported −150 is <em>subtracted</em>
/// (250 − 150 = 100), not added. Goldens are hand-derived in FU §5.
/// </summary>
public sealed class OcfFcfTests
{
    /// <summary>TC-MDF-059 (L1) — the canonical magnitude case: OCF 250 − |150| = 100.</summary>
    [Fact]
    public void TC_MDF_059_fcf_is_ocf_minus_absolute_capex()
    {
        Assert.Equal(100m, CanonicalMetrics.FreeCashFlow(250m, 150m));
    }

    /// <summary>
    /// TC-MDF-059 (L1) — the KAP sign trap: an as-reported negative CAPEX (−150) is still
    /// subtracted, so FCF stays 100. A naive <c>OCF − CAPEX</c> would yield 400; the
    /// absolute value is what the canonical definition requires (02 §6.2).
    /// </summary>
    [Fact]
    public void TC_MDF_059_negative_capex_is_subtracted_not_added()
    {
        Assert.Equal(100m, CanonicalMetrics.FreeCashFlow(250m, -150m));

        // Negative guard: the rejected sign-unaware wiring OCF − CAPEX = 400 must not appear.
        Assert.NotEqual(400m, CanonicalMetrics.FreeCashFlow(250m, -150m));
        Assert.NotEqual(400m, CanonicalMetrics.FreeCashFlow(250m, 150m));
    }

    /// <summary>
    /// TC-MDF-059 (L1) — FCF is honestly NULL when either input is missing (no CF
    /// statement → no OCF; a CF statement without CAPEX), never 0-for-missing.
    /// </summary>
    [Fact]
    public void TC_MDF_059_missing_ocf_or_capex_yields_null_never_zero()
    {
        Assert.Null(CanonicalMetrics.FreeCashFlow(ocf: null, capex: 150m));
        Assert.Null(CanonicalMetrics.FreeCashFlow(ocf: 250m, capex: null));
        Assert.Null(CanonicalMetrics.FreeCashFlow(ocf: null, capex: null));
    }
}
