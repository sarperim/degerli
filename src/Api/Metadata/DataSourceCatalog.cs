namespace Degerli.Api.Metadata;

/// <summary>
/// The product-side, static data-source provenance catalog (TKT-foundation-029).
///
/// It answers "where does the data come from?" per data class for the site-wide
/// disclosure surface — distinct from, and complementing, the informational-only
/// "not investment advice" disclaimer (UXR-G-016). Honesty is server-owned: whether
/// a class is actually live changes as the real-source work lands, so liveness lives
/// here rather than in a build-time web bundle (`01` §10.7 centralizes honest state
/// in server payloads).
///
/// It discloses exactly the AD-14 real-source classes (no new source, `01` §10.9):
/// <list type="bullet">
/// <item><c>prices</c> / <c>indexLevels</c> / <c>universe</c> — İş Yatırım public EOD
/// endpoints, live.</item>
/// <item><c>statements</c> — MKK "KAP Data Dissemination Services" (KAP VYK API);
/// the live activation is still pending, so it reports <c>unavailable</c>.</item>
/// <item><c>macro</c> — TCMB EVDS; pending its live activation, reports
/// <c>unavailable</c>.</item>
/// </list>
/// Every class states its EOD / as-of nature (not real-time). A class with no live
/// values is honestly marked <see cref="Unavailable"/> — never claimed as a live
/// source, never a fabricated live state. Fixture-only worlds are dev-only and are
/// never presented as real. This is new static data in the product, not a database
/// model; the test-only <c>fixtures/Degerli.Fixtures/RealSourceCatalog.cs</c> is a
/// separate parsing-fidelity lane and is never referenced here.
/// </summary>
public static class DataSourceCatalog
{
    /// <summary>A class served from a live source.</summary>
    public const string Live = "live";

    /// <summary>A class whose source is not yet live — no live values exist.</summary>
    public const string Unavailable = "unavailable";

    /// <summary>The disclosed as-of nature: end-of-day, never real-time.</summary>
    public const string NatureEod = "eod";

    /// <summary>Concrete source name of the İş Yatırım public EOD endpoints.</summary>
    public const string IsYatirimSource = "İş Yatırım";

    /// <summary>Concrete source name of the MKK KAP Data Dissemination Services.</summary>
    public const string KapSource = "MKK KAP";

    /// <summary>Concrete source name of the TCMB EVDS macro series.</summary>
    public const string EvdsSource = "TCMB EVDS";

    private static readonly IReadOnlyList<DataSourceEntry> Entries =
    [
        new("isyatirim-prices", "prices", IsYatirimSource, Live, NatureEod),
        new("isyatirim-index-levels", "indexLevels", IsYatirimSource, Live, NatureEod),
        new("isyatirim-universe", "universe", IsYatirimSource, Live, NatureEod),
        new("kap-statements", "statements", KapSource, Unavailable, NatureEod),
        new("evds-macro", "macro", EvdsSource, Unavailable, NatureEod),
    ];

    /// <summary>Every disclosed class, in serving order.</summary>
    public static IReadOnlyList<DataSourceEntry> All => Entries;
}

/// <summary>
/// One disclosed data class and the concrete source that produces it.
/// </summary>
/// <param name="Id">Stable machine identifier of the source-class entry.</param>
/// <param name="Class">Stable data-class code the UI keys its copy by.</param>
/// <param name="Source">Concrete source name (proper noun; codes/names come from the
/// server, display words from the client — `03` §1.1).</param>
/// <param name="State">Honest liveness: <see cref="DataSourceCatalog.Live"/> or
/// <see cref="DataSourceCatalog.Unavailable"/>.</param>
/// <param name="Nature">As-of nature of the class (end-of-day, not real-time).</param>
public sealed record DataSourceEntry(
    string Id,
    string Class,
    string Source,
    string State,
    string Nature);
