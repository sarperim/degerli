using Degerli.Fixtures;

namespace Degerli.ContentPipeline.IntegrationTests;

/// <summary>
/// TC-RES-016 drafts <c>THETA</c>. The frozen fixture universe (TKT-foundation-007, FU §9b)
/// seeds KAP disclosures only for ALFA/REST/ZETA, so this helper extends the universe with
/// two THETA disclosures for the database seed and, separately, a THETA drafted entry for
/// the recorded evren payload — leaving the checked-in universe untouched. The THETA
/// description is deliberately absent from the database seed so the CLI starts version 1.
/// </summary>
public static class DraftFixtureSet
{
    public const long ThetaAnnualReportId = 8850;
    public const long ThetaFinancialReportId = 8851;

    public static readonly long[] ThetaDisclosureIds = [ThetaAnnualReportId, ThetaFinancialReportId];

    /// <summary>The universe to persist: base rows plus THETA's two KAP disclosures.</summary>
    public static FixtureSet ForDatabase(FixtureSet baseSet) => Copy(baseSet, includeThetaDraft: false);

    /// <summary>The universe backing the recorded evren payload: base rows plus THETA.</summary>
    public static FixtureSet ForEvren(FixtureSet baseSet) => Copy(baseSet, includeThetaDraft: true);

    private static FixtureSet Copy(FixtureSet source, bool includeThetaDraft) => new()
    {
        Anchor = source.Anchor,
        Sectors = source.Sectors,
        Indices = source.Indices,
        Instruments = source.Instruments,
        Memberships = source.Memberships,
        Prices = source.Prices,
        IndexLevels = source.IndexLevels,
        Statements = source.Statements,
        Dividends = source.Dividends,
        CorporateActions = source.CorporateActions,
        KapDisclosures = [.. source.KapDisclosures, .. ThetaDisclosures(source.Anchor)],
        MacroValues = source.MacroValues,
        Funds = source.Funds,
        FundNavs = source.FundNavs,
        FundPerformances = source.FundPerformances,
        FundHoldings = source.FundHoldings,
        Accounts = source.Accounts,
        SavedScreens = source.SavedScreens,
        Scenarios = source.Scenarios,
        BusinessDescriptions = includeThetaDraft
            ? [.. source.BusinessDescriptions, ThetaDraft()]
            : source.BusinessDescriptions,
        Coverage = source.Coverage,
        MacroFreshness = source.MacroFreshness,
    };

    private static IEnumerable<FixtureKapDisclosure> ThetaDisclosures(FixtureAnchor anchor) =>
    [
        new(
            ThetaAnnualReportId,
            "THETA",
            "annual_report",
            anchor.Calendar(-150),
            "Theta İnşaat 2025 Faaliyet Raporu",
            "https://www.kap.org.tr/tr/Bildirim/8850"),
        new(
            ThetaFinancialReportId,
            "THETA",
            "financial_report",
            anchor.Calendar(-90),
            "Theta İnşaat Finansal Rapor",
            "https://www.kap.org.tr/tr/Bildirim/8851"),
    ];

    private static FixtureBusinessDescription ThetaDraft() =>
        new(
            "THETA",
            1,
            "draft",
            "Theta İnşaat, altyapı ve üstyapı inşaat taahhüt işleri yürütür.",
            "Theta İnşaat carries out infrastructure and superstructure construction contracting.",
            null,
            null,
            ThetaDisclosureIds);
}
