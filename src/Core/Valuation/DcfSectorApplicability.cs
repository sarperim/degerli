namespace Degerli.Core.Valuation;

/// <summary>
/// The financial-sector (profile-driven) not-applicable verdict for the DCF
/// (<c>02-data-model.md</c> §6.4, <c>03-api-design.md</c> §5/§7 v1.4; TKT-val-010):
/// for an instrument whose <c>accounting_profile ∈ {bank, insurance}</c> a
/// free-cash-flow-to-firm DCF does not apply. This is a <em>not-applicable</em> verdict
/// judged by the instrument's builder-curated classification (never a source fact,
/// <c>02</c> §3.1) — distinct from a violated math constraint and from the
/// missing-inputs state.
/// </summary>
public static class DcfSectorApplicability
{
    /// <summary>
    /// The <c>params.reason</c> the compute endpoint returns for a financial-sector
    /// instrument (<c>03</c> §7): the SPA renders the honest sector explanation from this
    /// code + the profile, not from the math-constraint path.
    /// </summary>
    public const string FinancialSectorReason = "financial_sector";

    /// <summary>
    /// The baseline <c>state</c> the generation job records (and the read endpoint serves)
    /// for a financial-sector instrument (<c>02</c> §6.4): no FCF-derived baseline exists.
    /// </summary>
    public const string SectorNotApplicableState = "sector_not_applicable";

    /// <summary>
    /// The accounting profiles for which a free-cash-flow-to-firm DCF does not apply
    /// (<c>02</c> §6.4): banks and insurers report under BDDK/insurance rules with no
    /// <c>REV</c>/<c>COGS</c>/<c>EBITDA</c>/<c>FCF</c> semantics. Mirrors the metric
    /// gate's profile set (<c>02</c> §6.2); <c>reit</c> and <c>industrial</c> keep the
    /// model.
    /// </summary>
    private static readonly IReadOnlySet<string> FinancialProfiles =
        new HashSet<string>(StringComparer.Ordinal) { "bank", "insurance" };

    /// <summary>
    /// Whether the accounting profile is a financial sector for which the DCF does not
    /// apply. An unknown/blank profile is treated as non-financial (the industrial
    /// default), matching <c>02</c> §3.1.
    /// </summary>
    public static bool IsFinancialSector(string? accountingProfile) =>
        accountingProfile is not null && FinancialProfiles.Contains(accountingProfile);
}
