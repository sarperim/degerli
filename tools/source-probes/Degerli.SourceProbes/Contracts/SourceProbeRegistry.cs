using Degerli.SourceProbes.Policy;

namespace Degerli.SourceProbes.Contracts;

/// <summary>
/// One probeable source (TKT-foundation-068): where it lives, how politely to ask, and which
/// committed double its expected shape came from.
/// </summary>
/// <param name="Key">Stable probe key (also the contract file name).</param>
/// <param name="DisplayName">Human label for the report.</param>
/// <param name="SourceRef">Provenance family — <c>isyatirim</c>, <c>yahoo</c>, <c>kap</c>, <c>evds</c>, <c>tefas</c>.</param>
/// <param name="Method">HTTP method the real resource requires.</param>
/// <param name="UrlTemplate">
/// The endpoint with <c>{placeholders}</c>; empty when the repository does not pin a real
/// endpoint for this source — the probe then reports <b>unreachable</b> with that reason
/// rather than inventing a URL.
/// </param>
/// <param name="RequestBody">A POST body, when the resource needs one (the İş AJAX handler).</param>
/// <param name="DeclaredPacingMs">The interval the probe asks for; <see cref="ProbePolicy"/> clamps it up.</param>
/// <param name="DoubleCatalogEntry">The fixture catalog constant its contract was derived from.</param>
/// <param name="SecretQueryParameters">Query parameters whose values must be redacted.</param>
/// <param name="UnconfiguredReason">Why a source with no pinned endpoint cannot be probed.</param>
public sealed record SourceProbe(
    string Key,
    string DisplayName,
    string SourceRef,
    string Method,
    string UrlTemplate,
    string? RequestBody,
    int DeclaredPacingMs,
    string DoubleCatalogEntry,
    IReadOnlyList<string> SecretQueryParameters,
    string? UnconfiguredReason = null)
{
    /// <summary>The contract file name (<c>contracts/&lt;key&gt;.json</c>).</summary>
    public string ContractFileName => Key + ".json";

    /// <summary>The interval the probe will actually honour (clamped to the source's floor).</summary>
    public int EffectivePacingMs => ProbePolicy.EffectiveInterval(SourceRef, DeclaredPacingMs);

    /// <summary>True when the repository pins a real endpoint for this source.</summary>
    public bool HasEndpoint => !string.IsNullOrWhiteSpace(UrlTemplate);
}

/// <summary>
/// The coverage set of the harness — one probe per source the ticket lists, each pinned to
/// the committed double its expected shape was derived from.
/// </summary>
/// <remarks>
/// The endpoints mirror the shipped configuration in <c>src/Api/appsettings.json</c>
/// (<c>Ingestion:Prices</c>, <c>Ingestion:Intraday</c>, <c>Ingestion:Kap</c>,
/// <c>Ingestion:Statements</c>, <c>Ingestion:Macro</c>). When that configuration moves, this
/// registry is updated with it — the probe never guesses a route.
/// </remarks>
public static class SourceProbeRegistry
{
    /// <summary>The İş Yatırım daily EOD feed — the source of incident #1 (empty <c>value</c>).</summary>
    public const string IsYatirimPrices = "isyatirim-prices";

    /// <summary>The Yahoo <c>.IS</c> chart contingency for the same lane.</summary>
    public const string YahooChart = "yahoo-chart";

    /// <summary>The KAP VYK disclosure list — the source of incident #2 (<c>ER005</c>).</summary>
    public const string KapDisclosures = "kap-disclosures";

    /// <summary>The KAP VYK disclosure detail (the <c>presentation</c> tree).</summary>
    public const string KapDisclosureDetail = "kap-disclosure-detail";

    /// <summary>The KAP VYK member (company) list.</summary>
    public const string KapMembers = "kap-members";

    /// <summary>The KAP VYK security lines — the <c>shares</c> lane.</summary>
    public const string KapMemberSecurities = "kap-member-securities";

    /// <summary>The TCMB EVDS 3.0 macro series feed.</summary>
    public const string EvdsMacro = "evds-macro";

    /// <summary>The TEFAS fund feed (no endpoint is pinned in the repository yet).</summary>
    public const string TefasFunds = "tefas-funds";

    /// <summary>Every probe, in report order.</summary>
    public static IReadOnlyList<SourceProbe> All { get; } =
    [
        new SourceProbe(
            IsYatirimPrices,
            "İş Yatırım EOD prices (HisseTekil)",
            "isyatirim",
            "GET",
            "https://www.isyatirim.com.tr/_layouts/15/IsYatirim.Website/Common/Data.aspx/HisseTekil"
            + "?hisse={symbol}&startdate={date}&enddate={date}",
            null,
            3_000,
            "isyatirim-prices-ASELS",
            []),

        new SourceProbe(
            YahooChart,
            "Yahoo Finance chart (.IS contingency)",
            "yahoo",
            "GET",
            "https://query1.finance.yahoo.com/v8/finance/chart/{symbol}.IS?range=5d&interval=1d",
            null,
            3_000,
            "prices-yahoo-contingency",
            []),

        new SourceProbe(
            KapDisclosures,
            "KAP VYK disclosure list (/disclosures)",
            "kap",
            "GET",
            "https://apigwdev.mkk.com.tr/api/vyk/disclosures?disclosureIndex={disclosureIndex}&companyId={companyId}",
            null,
            12_000,
            "kap-vyk-disclosures",
            []),

        new SourceProbe(
            KapDisclosureDetail,
            "KAP VYK disclosure detail (/disclosureDetail)",
            "kap",
            "GET",
            "https://apigwdev.mkk.com.tr/api/vyk/disclosureDetail/{disclosureIndex}",
            null,
            12_000,
            "kap-vyk-disclosure-detail",
            []),

        new SourceProbe(
            KapMembers,
            "KAP VYK member list (/members)",
            "kap",
            "GET",
            "https://apigwdev.mkk.com.tr/api/vyk/members",
            null,
            12_000,
            "kap-vyk-members",
            []),

        new SourceProbe(
            KapMemberSecurities,
            "KAP VYK security lines (/memberSecurities)",
            "kap",
            "GET",
            "https://apigwdev.mkk.com.tr/api/vyk/memberSecurities",
            null,
            12_000,
            "member-securities-ok",
            []),

        new SourceProbe(
            EvdsMacro,
            "TCMB EVDS 3.0 macro series",
            "evds",
            "GET",
            "https://evds3.tcmb.gov.tr/igmevdsms-dis/fe?series={series}&startDate={startDate}&endDate={endDate}&type=json&key={apiKey}",
            null,
            2_000,
            "evds-v3-capture",
            ["key"]),

        new SourceProbe(
            TefasFunds,
            "TEFAS fund feed",
            "tefas",
            "GET",
            string.Empty,
            null,
            2_000,
            "tefas-tef0001",
            [],
            UnconfiguredReason:
            "no real TEFAS endpoint is pinned in the repository (TefasSourceOptions.BaseUrl ships empty); "
            + "the lane is not live yet, so there is nothing honest to probe."),
    ];

    /// <summary>Looks a probe up by key (case-insensitive).</summary>
    public static SourceProbe? Find(string key) =>
        All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
}
