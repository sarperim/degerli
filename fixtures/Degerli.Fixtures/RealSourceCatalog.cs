using System.Text;

namespace Degerli.Fixtures;

/// <summary>
/// Provenance of a real-source catalog entry (01 v1.5 §10.9.7 labeling rule): whether the
/// body is a bounded verbatim capture from the live public source, or a body that was
/// hand-constructed for parsing fidelity. A constructed body is never presented as a
/// capture — the DR-03 doc-accuracy rule, mechanically enforced by TC-MDF-063.
/// </summary>
public enum RealSourceProvenance
{
    /// <summary>A verbatim bounded capture from the live public source endpoint.</summary>
    Captured,

    /// <summary>A hand-constructed, shape-faithful body — never labeled a verbatim capture.</summary>
    Constructed,
}

/// <summary>
/// One real-source payload (TKT-foundation-015; 01 v1.5 §10.9.7). Unlike
/// <see cref="CannedSourcePayload"/>, whose bodies are generated from the single synthetic
/// fixture universe (FU §1), this catalog preserves the real source envelopes — the İş
/// Yatırım <c>HGDG_*</c> EOD shape with its real field names, numeric layout and
/// <c>dd-MM-yyyy</c> dates — so ingestion parsing is exercised against the production
/// shape. The FU golden values are untouched: this catalog tests parsing <em>shape</em>,
/// never derived numbers.
/// </summary>
/// <param name="Name">Stable catalog name (see the constants on <see cref="RealSourceCatalog"/>).</param>
/// <param name="Source">Concrete source (<c>isyatirim</c>).</param>
/// <param name="Method">HTTP method the payload is served with.</param>
/// <param name="DefaultPath">Source-double route the payload is served at by default.</param>
/// <param name="StatusCode">HTTP status the double returns.</param>
/// <param name="EncodingName">Charset of <see cref="RawBytes"/> (the harness serves this in
/// the declared <c>Content-Type; charset=</c> header).</param>
/// <param name="Provenance">Whether the body is a bounded capture or hand-constructed.</param>
/// <param name="ProvenanceLabel">Human-readable provenance statement (never claims a
/// verbatim capture for a constructed body).</param>
/// <param name="RawBytes">The verbatim captured body bytes (or the constructed body bytes).</param>
public sealed record RealSourcePayload(
    string Name,
    string Source,
    string Method,
    string DefaultPath,
    int StatusCode,
    string EncodingName,
    RealSourceProvenance Provenance,
    string ProvenanceLabel,
    byte[] RawBytes)
{
    /// <summary>True when the bytes are not UTF-8 (a legacy code-page artifact).</summary>
    public bool IsLegacyEncoded => !string.Equals(EncodingName, "utf-8", StringComparison.OrdinalIgnoreCase);

    /// <summary>The canonical decoded body the harness serves (raw bytes decoded under the declared charset).</summary>
    public string Body => RealSourceCatalog.Decode(RawBytes, EncodingName);

    /// <summary>Decodes <see cref="RawBytes"/> with the declared charset.</summary>
    public string DecodeRawText() => Body;
}

/// <summary>
/// The real-source payload catalog (TKT-foundation-015; FU v1.5 §10b) — a second,
/// real-shape catalog alongside the synthetic FU §10 one, loaded through the same
/// <c>Names</c>/<c>Build</c>/<c>Get</c>/<c>TryGet</c> helper seam. It holds the bounded
/// İş Yatırım EOD <c>HGDG_*</c> captures (five tickers, 2026-10-06..2026-10-08) plus the
/// honestly-labeled constructed variants the parsing-fidelity cases need. The bodies are
/// served through the WireMock harness as <b>raw bytes</b> with their declared
/// <c>Content-Type; charset=</c> header and parsed by the <b>production</b> adapter in
/// <c>src/Ingestion</c> (never a fixture-owned decoder — the DR-01 rule overruled by the
/// v1.5 architecture, §10.9.7).
/// </summary>
public static class RealSourceCatalog
{
    // FU §10-style named payloads — the real-source price catalog (TKT-foundation-015).
    public const string PricesAsels = "isyatirim-prices-ASELS";
    public const string PricesThyao = "isyatirim-prices-THYAO";
    public const string PricesGaran = "isyatirim-prices-GARAN";
    public const string PricesFroto = "isyatirim-prices-FROTO";
    public const string PricesEbebk = "isyatirim-prices-EBEBK";
    public const string PricesHgFallback = "isyatirim-prices-hg-fallback";
    public const string PricesOkFalse = "isyatirim-prices-ok-false";
    public const string PricesErrorCode = "isyatirim-prices-error-code";

    // KAP financial-statement (XBRL fact) payloads — the statements-side lane (TKT-foundation-017).
    public const string StatementsAsels = "kap-statements-ASELS";
    public const string StatementsThyao = "kap-statements-THYAO";
    public const string StatementsGaran = "kap-statements-GARAN";
    public const string StatementsFroto = "kap-statements-FROTO";
    public const string StatementsEbebk = "kap-statements-EBEBK";
    public const string StatementsToleranceEdges = "kap-statements-tolerance-edges";
    public const string StatementsDuplicateEqual = "kap-statements-duplicate-equal";
    public const string StatementsDuplicateUnequal = "kap-statements-duplicate-unequal";

    // EVDS macro (column-oriented real envelope) — the macro-side lane (TKT-foundation-019).
    public const string MacroEvds = "evds-macro";

    // EVDS 3.0 macro — the migrated macro-source lane (TKT-foundation-027). The v3 values
    // body is the anonymous `POST /igmevdsms-dis/fe` shape (underscored series columns,
    // comma-grouped value strings, daily `dd-MM-yyyy` + monthly `yyyy-MM` rows); the catalog
    // and serieList bodies back catalog-driven series-code discovery.
    public const string MacroEvdsV3 = "evds-v3-values";
    public const string MacroEvdsV3Capture = "evds-v3-capture";
    public const string MacroEvdsV3Catalog = "evds-v3-catalog";
    public const string MacroEvdsV3SerieList = "evds-v3-serie-list";

    // Universe / index-levels real envelopes — the universe-sync lane (TKT-foundation-021);
    // the XU100 index-levels capture joins the per-index set lane (TKT-foundation-025).
    public const string Universe = "isyatirim-universe";
    public const string IndexLevels = "isyatirim-index-levels";
    public const string IndexLevelsXu100 = "isyatirim-index-levels-xu100";

    // KAP VYK API list/detail envelopes — the company-scoped statements-source lane
    // (TKT-foundation-026). The MKK "KAP Data Dissemination Services" gateway identifies
    // companies by `companyId` (resolved from `/members`), lists financial-report
    // disclosures (`/disclosures`) and returns one disclosure's structured detail
    // (`/disclosureDetail/{index}?fileType=data`, an XBRL-style `presentation` tree — the
    // OpenAPI's declared `flatData` is absent from the real response). The list bodies are
    // honestly-labeled constructed shapes; the detail body is a bounded live capture
    // (2026-10-18) — never a fabricated capture (TC-MDF-063 posture).
    public const string VykMembers = "kap-vyk-members";
    public const string VykDisclosures = "kap-vyk-disclosures";
    public const string VykDisclosureDetail = "kap-vyk-disclosure-detail";

    // KAP **public-site** payloads — the public-site source lane (TKT-foundation-079). A
    // different evidence class from the VYK envelopes above (`kap-public`, never `kap`), and
    // a different identifier space (its own `disclosureIndex` and company oid).
    public const string KapPublicRobots = "kap-public-robots";
    public const string KapPublicByCriteria = "kap-public-bycriteria-asels-fr";
    public const string KapPublicMarketProbe = "kap-public-bycriteria-market-probe";
    public const string KapPublicReport = "kap-public-report-1643141";
    public const string KapPublicIdentity = "kap-public-identity-ASELS";

    /// <summary>Concrete source name of the KAP public-site payloads (never `kap`).</summary>
    public const string KapPublicSource = "kap-public";

    /// <summary>Concrete source name of the captured EOD price payloads.</summary>
    public const string IsYatirimSource = "isyatirim";

    /// <summary>Concrete source name of the KAP financial-statement payloads.</summary>
    public const string KapSource = "kap";

    /// <summary>Concrete source name of the EVDS macro payloads (03 §1).</summary>
    public const string EvdsSource = "evds";

    /// <summary>The five tickers captured into the real-source catalog (TKT-foundation-015).</summary>
    public static IReadOnlyList<string> Tickers { get; } = ["ASELS", "THYAO", "GARAN", "FROTO", "EBEBK"];

    /// <summary>Every catalog name, in catalog order.</summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        PricesAsels,
        PricesThyao,
        PricesGaran,
        PricesFroto,
        PricesEbebk,
        PricesHgFallback,
        PricesOkFalse,
        PricesErrorCode,
        StatementsAsels,
        StatementsThyao,
        StatementsGaran,
        StatementsFroto,
        StatementsEbebk,
        StatementsToleranceEdges,
        StatementsDuplicateEqual,
        StatementsDuplicateUnequal,
        MacroEvds,
        MacroEvdsV3,
        MacroEvdsV3Capture,
        MacroEvdsV3Catalog,
        MacroEvdsV3SerieList,
        Universe,
        IndexLevels,
        IndexLevelsXu100,
        VykMembers,
        VykDisclosures,
        VykDisclosureDetail,
        KapPublicRobots,
        KapPublicByCriteria,
        KapPublicMarketProbe,
        KapPublicReport,
        KapPublicIdentity,
    ];

    /// <summary>The day the bounded live capture was taken.</summary>
    public static readonly DateOnly CapturedOn = new(2026, 10, 16);

    /// <summary>The bounded capture window (inclusive), a fixed 3-trading-day slice.</summary>
    public const string CaptureWindow = "2026-10-06..2026-10-08";

    /// <summary>
    /// Bounded-capture note recorded with the catalog (TKT-foundation-015): five tickers,
    /// a three-trading-day İş Yatırım EOD <c>HisseTekil</c> slice.
    /// </summary>
    public const string CaptureNote =
        "Bounded live capture: İş Yatırım EOD HisseTekil, 5 tickers, 2026-10-06..2026-10-08.";

    /// <summary>
    /// Redistribution terms recorded per RISK-MDF-001. BIST end-of-day facts are
    /// redistributed here only in the bounded, derived, non-commercial form the risk
    /// posture (brief §7, free/public sources) accepts; per-source terms are re-checked
    /// during the builder's V0 validation.
    /// </summary>
    public const string RedistributionTerms =
        "RISK-MDF-001: public-source (İş Yatırım) facts, bounded non-commercial " +
        "redistribution for test-harness parsing fidelity only; per-source terms re-checked at V0.";

    private const string CapturedLabel =
        "Captured verbatim from the live İş Yatırım EOD HisseTekil endpoint on 2026-10-16.";

    private const string ConstructedLabel =
        "Constructed by hand for parsing-fidelity coverage — a shape-faithful body, not a live capture.";

    private const string KapStatementsLabel =
        "Constructed by hand for KAP XBRL parsing-fidelity coverage (IFRS/TR/KAP tag variance, " +
        "Turkish-locale numbers) — a shape-faithful body, not a live capture.";

    // TKT-foundation-019. The live EVDS endpoint is unavailable from this environment (the
    // documented service route now serves the EVDS SPA shell, not JSON), so the entry is an
    // honestly-labeled constructed body of the real column-oriented shape rather than a
    // fabricated capture; the bounded live capture remains an explicit ticket criterion
    // (recorded as a gap, never invented). No API key is present in the body — the key never
    // travels in a response, and it is redacted before any capture would land here.
    private const string EvdsMacroLabel =
        "Constructed by hand for EVDS real-envelope parsing-fidelity coverage (the column-oriented " +
        "items/Tarih shape, five official series) — a shape-faithful body, not a live capture.";

    // TKT-foundation-027. The EVDS 3.0 (`evds3.tcmb.gov.tr`) migration. The values endpoint
    // (`POST /igmevdsms-dis/fe`) is anonymous; the column keys underscore the dotted series
    // codes and values carry thousands-grouping commas. The construct is a shape-faithful
    // five-series body; the capture is a bounded verbatim live response. No credential is
    // present in either (the endpoint needs none).
    private const string EvdsV3ConstructedLabel =
        "Constructed by hand for EVDS 3.0 /fe parsing-fidelity coverage (the anonymous POST " +
        "/igmevdsms-dis/fe shape: underscored series columns, comma-grouped value strings, daily " +
        "dd-MM-yyyy + monthly yyyy-MM rows; five official series) — a shape-faithful body, not a live capture.";

    private const string EvdsV3CaptureLabel =
        "Captured live (bounded) from the anonymous EVDS 3.0 POST /igmevdsms-dis/fe on 2026-10-18: " +
        "USD/TRY + EUR/TRY selling, 2026-10-15..2026-10-16; no credential (the endpoint is anonymous).";

    private const string EvdsV3CatalogLabel =
        "Constructed by hand for EVDS 3.0 catalog discovery coverage (the target datagroup codes " +
        "live-verified 2026-10-19; the Gold category carries both the monthly Ankara Chamber of " +
        "Jewelers datagroup `bie_mkaltytl` and `bie_altinbistbul`, the BIST gold datagroup that " +
        "actually publishes TP.ALTINPIYASA.KAP03) — a shape-faithful body, not a live capture.";

    private const string EvdsV3SerieListLabel =
        "Constructed by hand for EVDS 3.0 serieList discovery coverage (the live-verified " +
        "SERIE_CODE/DATAGROUP_CODE/FREQUENCY_STR/DEFAULT_AGG_METHOD rows for the five official " +
        "series, verified live 2026-10-18/19 — incl. TP.ALTINPIYASA.KAP02/KAP03 under " +
        "bie_altinbistbul, İŞ GÜNÜ/last) — a shape-faithful body, not a live capture.";

    // TKT-foundation-021. Both bodies are bounded live captures from the real İş Yatırım
    // endpoints verified live 2026-10-18 (01 v1.5 §10.9.8): the universe resource is a legacy
    // ASP.NET AJAX POST handler, the index-levels resource a plain GET on the same host. The
    // universe body is a compact re-serialization of the first five `Oran` rows — it keeps the
    // source's trailing whitespace padding (exercising the decoder's trim) and is therefore not
    // presented as a byte-for-byte verbatim capture. The index body carries nine daily closes.
    private const string UniverseLabel =
        "Captured live from the İş Yatırım GetHissePerformans POST handler on 2026-10-18 " +
        "(bounded: the first five Ulusal-Tüm rows, compact re-serialization; source padding preserved).";

    private const string IndexLevelsLabel =
        "Captured live from the İş Yatırım IndexHistoricalAll chart handler on 2026-10-18 " +
        "(bounded: nine XU030 daily index closes, 2026-10-06..2026-10-16, compact re-serialization).";

    // TKT-foundation-025. The second index of the per-index set: the same live chart handler
    // queried with `endeks=XU100`. The body carries no index code, so the production decoder
    // stamps the requested code (XU100) onto every fact — this capture is what proves the
    // multi-index loop fetches <em>both</em> real BIST indexes (FR-MDF-006).
    private const string IndexLevelsXu100Label =
        "Captured live from the İş Yatırım IndexHistoricalAll chart handler on 2026-10-18 " +
        "(bounded: nine XU100 daily index closes, 2026-10-06..2026-10-16, compact re-serialization).";

    // TKT-foundation-026. The KAP VYK gateway's list/detail envelopes. The `/members`
    // (Şirket Listesi) and `/disclosures` bodies are hand-constructed shape-faithful bodies
    // (live-verified shape, values chosen so the loop tests bind ALFA/EPSL) — never a
    // fabricated capture. The `/disclosureDetail` body is a <b>bounded live capture</b>
    // (2026-10-18, the real `presentation` shape) — see VykDetailCaptureLabel.
    private const string VykLabel =
        "Constructed by hand for KAP VYK API (MKK gateway) list parsing-fidelity coverage " +
        "(the /members and /disclosures shapes, live-verified 2026-10-18) — a shape-faithful " +
        "body, not a live capture.";

    // TKT-foundation-026. The real VYK disclosure-detail envelope for disclosure 1226255
    // (1000 YATIRIMLAR HOLDİNG A.Ş., FY2023 9-month, live 2026-10-18). Bounded: the two
    // sub-reports (balance sheet + income statement) are trimmed to a representative set of
    // real report items with their real CURR/PREV values; the top-level fields are verbatim.
    // This is <b>not</b> the OpenAPI's `flatData` — the gateway returns an XBRL-style
    // `presentation` tree (live-verified), which the production decoder reads.
    private const string VykDetailCaptureLabel =
        "Captured live from the MKK KAP VYK disclosureDetail endpoint on 2026-10-18 " +
        "(bounded: disclosure 1226255's balance-sheet + income-statement presentation trees " +
        "trimmed to a representative real-report-item subset; real CURR/PREV values kept).";

    // ---- verbatim captured bodies (bounded; see CaptureNote) ----
    private const string PricesAselsJson = """{"ok":true,"errorCode":null,"errorDescription":null,"transactionId":"23c0bb08b7f14485b4a7dff0fed2727a","value":[{"HGDG_HS_KODU":"ASELS","HGDG_TARIH":"06-10-2026","HGDG_KAPANIS":376.0000,"HGDG_AOF":371.181,"HGDG_MIN":364.25,"HGDG_MAX":379.0,"HGDG_HACIM":13239396512.000000,"END_ENDEKS_KODU":"01","END_TARIH":1791234000000,"END_SEANS":2,"END_DEGER":12374.26,"DD_DOVIZ_KODU":"USD","DD_DT_KODU":"01","DD_TARIH":1791234000000,"DD_DEGER":49.1802,"DOLAR_BAZLI_FIYAT":7.6454,"ENDEKS_BAZLI_FIYAT":0.0304,"DOLAR_HACIM":269201762.3352,"SERMAYE":4.56E9,"HG_KAPANIS":376.0,"HG_AOF":371.181,"HG_MIN":364.25,"HG_MAX":379.0,"PD":1714560000000.00000,"PD_USD":34862810643.30767,"HAO_PD":442013568000.000000,"HAO_PD_USD":8987632583.844717,"HG_HACIM":1.3239396512E10,"DOLAR_BAZLI_MIN":7.4064,"DOLAR_BAZLI_MAX":7.7064,"DOLAR_BAZLI_AOF":7.5474},{"HGDG_HS_KODU":"ASELS","HGDG_TARIH":"07-10-2026","HGDG_KAPANIS":348.7500,"HGDG_AOF":356.437,"HGDG_MIN":346.75,"HGDG_MAX":377.25,"HGDG_HACIM":17063642691.000000,"END_ENDEKS_KODU":"01","END_TARIH":1791320400000,"END_SEANS":2,"END_DEGER":12122.94,"DD_DOVIZ_KODU":"USD","DD_DT_KODU":"01","DD_TARIH":1791320400000,"DD_DEGER":49.1976,"DOLAR_BAZLI_FIYAT":7.0888,"ENDEKS_BAZLI_FIYAT":0.0288,"DOLAR_HACIM":346838924.8866,"SERMAYE":4.56E9,"HG_KAPANIS":348.75,"HG_AOF":356.437,"HG_MIN":346.75,"HG_MAX":377.25,"PD":1590300000000.00000,"PD_USD":32324747548.66091,"HAO_PD":409979340000.000000,"HAO_PD_USD":8333319918.044783,"HG_HACIM":1.7063642691E10,"DOLAR_BAZLI_MIN":7.0481,"DOLAR_BAZLI_MAX":7.6681,"DOLAR_BAZLI_AOF":7.2450},{"HGDG_HS_KODU":"ASELS","HGDG_TARIH":"08-10-2026","HGDG_KAPANIS":348.7500,"HGDG_AOF":345.473,"HGDG_MIN":340.25,"HGDG_MAX":354.0,"HGDG_HACIM":12941100644.000000,"END_ENDEKS_KODU":"01","END_TARIH":1791406800000,"END_SEANS":2,"END_DEGER":12213.58,"DD_DOVIZ_KODU":"USD","DD_DT_KODU":"01","DD_TARIH":1791406800000,"DD_DEGER":49.2152,"DOLAR_BAZLI_FIYAT":7.0862,"ENDEKS_BAZLI_FIYAT":0.0286,"DOLAR_HACIM":262949264.5362,"SERMAYE":4.56E9,"HG_KAPANIS":348.75,"HG_AOF":345.473,"HG_MIN":340.25,"HG_MAX":354.0,"PD":1590300000000.00000,"PD_USD":32313187795.64037,"HAO_PD":409979340000.000000,"HAO_PD_USD":8330339813.716087,"HG_HACIM":1.2941100644E10,"DOLAR_BAZLI_MIN":6.9135,"DOLAR_BAZLI_MAX":7.1929,"DOLAR_BAZLI_AOF":7.0196}]}""";
    private const string PricesThyaoJson = """{"ok":true,"errorCode":null,"errorDescription":null,"transactionId":"def507b11493419d9812dcee5ec7daca","value":[{"HGDG_HS_KODU":"THYAO","HGDG_TARIH":"06-10-2026","HGDG_KAPANIS":290.5000,"HGDG_AOF":292.381,"HGDG_MIN":290.5,"HGDG_MAX":294.25,"HGDG_HACIM":7982835728.000000,"END_ENDEKS_KODU":"01","END_TARIH":1791234000000,"END_SEANS":2,"END_DEGER":12374.26,"DD_DOVIZ_KODU":"USD","DD_DT_KODU":"01","DD_TARIH":1791234000000,"DD_DEGER":49.1802,"DOLAR_BAZLI_FIYAT":5.9068,"ENDEKS_BAZLI_FIYAT":0.0235,"DOLAR_HACIM":162318081.8297,"SERMAYE":1.38E9,"HG_KAPANIS":290.5,"HG_AOF":292.381,"HG_MIN":290.5,"HG_MAX":294.25,"PD":400890000000.00000,"PD_USD":8151451193.77310,"HAO_PD":196957257000.000000,"HAO_PD_USD":4004807971.500724,"HG_HACIM":7.982835728E9,"DOLAR_BAZLI_MIN":5.9068,"DOLAR_BAZLI_MAX":5.9831,"DOLAR_BAZLI_AOF":5.9451},{"HGDG_HS_KODU":"THYAO","HGDG_TARIH":"07-10-2026","HGDG_KAPANIS":286.2500,"HGDG_AOF":286.776,"HGDG_MIN":283.75,"HGDG_MAX":290.5,"HGDG_HACIM":7765288779.000000,"END_ENDEKS_KODU":"01","END_TARIH":1791320400000,"END_SEANS":2,"END_DEGER":12122.94,"DD_DOVIZ_KODU":"USD","DD_DT_KODU":"01","DD_TARIH":1791320400000,"DD_DEGER":49.1976,"DOLAR_BAZLI_FIYAT":5.8184,"ENDEKS_BAZLI_FIYAT":0.0236,"DOLAR_HACIM":157838772.1962,"SERMAYE":1.38E9,"HG_KAPANIS":286.25,"HG_AOF":286.776,"HG_MIN":283.75,"HG_MAX":290.5,"PD":395025000000.00000,"PD_USD":8029355090.49222,"HAO_PD":194115285000.000000,"HAO_PD_USD":3945625091.467877,"HG_HACIM":7.765288779E9,"DOLAR_BAZLI_MIN":5.7676,"DOLAR_BAZLI_MAX":5.9048,"DOLAR_BAZLI_AOF":5.8291},{"HGDG_HS_KODU":"THYAO","HGDG_TARIH":"08-10-2026","HGDG_KAPANIS":284.2500,"HGDG_AOF":284.492,"HGDG_MIN":282.0,"HGDG_MAX":287.5,"HGDG_HACIM":5829586658.000000,"END_ENDEKS_KODU":"01","END_TARIH":1791406800000,"END_SEANS":2,"END_DEGER":12213.58,"DD_DOVIZ_KODU":"USD","DD_DT_KODU":"01","DD_TARIH":1791406800000,"DD_DEGER":49.2152,"DOLAR_BAZLI_FIYAT":5.7757,"ENDEKS_BAZLI_FIYAT":0.0233,"DOLAR_HACIM":118450939.1001,"SERMAYE":1.38E9,"HG_KAPANIS":284.25,"HG_AOF":284.492,"HG_MIN":282.0,"HG_MAX":287.5,"PD":392265000000.00000,"PD_USD":7970403452.59188,"HAO_PD":192759021000.000000,"HAO_PD_USD":3916656256.603650,"HG_HACIM":5.829586658E9,"DOLAR_BAZLI_MIN":5.7299,"DOLAR_BAZLI_MAX":5.8417,"DOLAR_BAZLI_AOF":5.7806}]}""";
    private const string PricesGaranJson = """{"ok":true,"errorCode":null,"errorDescription":null,"transactionId":"9418dc7cdf134f9e9e3879de1206e407","value":[{"HGDG_HS_KODU":"GARAN","HGDG_TARIH":"06-10-2026","HGDG_KAPANIS":131.2000,"HGDG_AOF":130.388,"HGDG_MIN":129.4,"HGDG_MAX":131.4,"HGDG_HACIM":2284977654.000000,"END_ENDEKS_KODU":"01","END_TARIH":1791234000000,"END_SEANS":2,"END_DEGER":12374.26,"DD_DOVIZ_KODU":"USD","DD_DT_KODU":"01","DD_TARIH":1791234000000,"DD_DEGER":49.1802,"DOLAR_BAZLI_FIYAT":2.6677,"ENDEKS_BAZLI_FIYAT":0.0106,"DOLAR_HACIM":46461333.0975,"SERMAYE":4.2E9,"HG_KAPANIS":131.2,"HG_AOF":130.388,"HG_MIN":129.4,"HG_MAX":131.4,"PD":551039987182.61719,"PD_USD":11204508871.10295,"HAO_PD":76980286209.411621,"HAO_PD_USD":1565269889.293082,"HG_HACIM":2.284977654E9,"DOLAR_BAZLI_MIN":2.6311,"DOLAR_BAZLI_MAX":2.6718,"DOLAR_BAZLI_AOF":2.6512},{"HGDG_HS_KODU":"GARAN","HGDG_TARIH":"07-10-2026","HGDG_KAPANIS":129.9000,"HGDG_AOF":129.547,"HGDG_MIN":128.0,"HGDG_MAX":131.5,"HGDG_HACIM":2263850308.000000,"END_ENDEKS_KODU":"01","END_TARIH":1791320400000,"END_SEANS":2,"END_DEGER":12122.94,"DD_DOVIZ_KODU":"USD","DD_DT_KODU":"01","DD_TARIH":1791320400000,"DD_DEGER":49.1976,"DOLAR_BAZLI_FIYAT":2.6404,"ENDEKS_BAZLI_FIYAT":0.0107,"DOLAR_HACIM":46015462.2990,"SERMAYE":4.2E9,"HG_KAPANIS":129.9,"HG_AOF":129.547,"HG_MIN":128.0,"HG_MAX":131.5,"PD":545579974365.23438,"PD_USD":11089564823.59372,"HAO_PD":76162964421.386719,"HAO_PD_USD":1548103249.373683,"HG_HACIM":2.263850308E9,"DOLAR_BAZLI_MIN":2.6018,"DOLAR_BAZLI_MAX":2.6729,"DOLAR_BAZLI_AOF":2.6332},{"HGDG_HS_KODU":"GARAN","HGDG_TARIH":"08-10-2026","HGDG_KAPANIS":128.5000,"HGDG_AOF":128.389,"HGDG_MIN":127.0,"HGDG_MAX":129.8,"HGDG_HACIM":2001849137.000000,"END_ENDEKS_KODU":"01","END_TARIH":1791406800000,"END_SEANS":2,"END_DEGER":12213.58,"DD_DOVIZ_KODU":"USD","DD_DT_KODU":"01","DD_TARIH":1791406800000,"DD_DEGER":49.2152,"DOLAR_BAZLI_FIYAT":2.6110,"ENDEKS_BAZLI_FIYAT":0.0105,"DOLAR_HACIM":40675424.1982,"SERMAYE":4.2E9,"HG_KAPANIS":128.5,"HG_AOF":128.389,"HG_MIN":127.0,"HG_MAX":129.8,"PD":539700000000.00000,"PD_USD":10966124286.80570,"HAO_PD":75342120000.000000,"HAO_PD_USD":1530870950.438076,"HG_HACIM":2.001849137E9,"DOLAR_BAZLI_MIN":2.5805,"DOLAR_BAZLI_MAX":2.6374,"DOLAR_BAZLI_AOF":2.6087}]}""";
    private const string PricesFrotoJson = """{"ok":true,"errorCode":null,"errorDescription":null,"transactionId":"a771211e548141e3bc6f5d4578e98cb1","value":[{"HGDG_HS_KODU":"FROTO","HGDG_TARIH":"06-10-2026","HGDG_KAPANIS":74.6000,"HGDG_AOF":74.826,"HGDG_MIN":74.25,"HGDG_MAX":75.4,"HGDG_HACIM":1020358525.000000,"END_ENDEKS_KODU":"01","END_TARIH":1791234000000,"END_SEANS":2,"END_DEGER":12374.26,"DD_DOVIZ_KODU":"USD","DD_DT_KODU":"01","DD_TARIH":1791234000000,"DD_DEGER":49.1802,"DOLAR_BAZLI_FIYAT":1.5169,"ENDEKS_BAZLI_FIYAT":0.0060,"DOLAR_HACIM":20747343.9514,"SERMAYE":3.5091E9,"HG_KAPANIS":74.6,"HG_AOF":74.826,"HG_MIN":74.25,"HG_MAX":75.4,"PD":261778854645.53833,"PD_USD":5322850550.53738,"HAO_PD":46230145730.402069,"HAO_PD_USD":940015407.224901,"HG_HACIM":1.020358525E9,"DOLAR_BAZLI_MIN":1.5098,"DOLAR_BAZLI_MAX":1.5331,"DOLAR_BAZLI_AOF":1.5215},{"HGDG_HS_KODU":"FROTO","HGDG_TARIH":"07-10-2026","HGDG_KAPANIS":73.8500,"HGDG_AOF":73.984,"HGDG_MIN":73.2,"HGDG_MAX":74.75,"HGDG_HACIM":1013299362.000000,"END_ENDEKS_KODU":"01","END_TARIH":1791320400000,"END_SEANS":2,"END_DEGER":12122.94,"DD_DOVIZ_KODU":"USD","DD_DT_KODU":"01","DD_TARIH":1791320400000,"DD_DEGER":49.1976,"DOLAR_BAZLI_FIYAT":1.5011,"ENDEKS_BAZLI_FIYAT":0.0061,"DOLAR_HACIM":20596520.1961,"SERMAYE":3.5091E9,"HG_KAPANIS":73.85,"HG_AOF":73.984,"HG_MIN":73.2,"HG_MAX":74.75,"PD":259147029645.53833,"PD_USD":5267472999.60848,"HAO_PD":45791280138.366623,"HAO_PD_USD":930762479.030818,"HG_HACIM":1.013299362E9,"DOLAR_BAZLI_MIN":1.4879,"DOLAR_BAZLI_MAX":1.5194,"DOLAR_BAZLI_AOF":1.5038},{"HGDG_HS_KODU":"FROTO","HGDG_TARIH":"08-10-2026","HGDG_KAPANIS":74.4000,"HGDG_AOF":74.271,"HGDG_MIN":73.7,"HGDG_MAX":74.7,"HGDG_HACIM":837500049.000000,"END_ENDEKS_KODU":"01","END_TARIH":1791406800000,"END_SEANS":2,"END_DEGER":12213.58,"DD_DOVIZ_KODU":"USD","DD_DT_KODU":"01","DD_TARIH":1791406800000,"DD_DEGER":49.2152,"DOLAR_BAZLI_FIYAT":1.5117,"ENDEKS_BAZLI_FIYAT":0.0061,"DOLAR_HACIM":17017101.4036,"SERMAYE":3.5091E9,"HG_KAPANIS":74.4,"HG_AOF":74.271,"HG_MIN":73.7,"HG_MAX":74.7,"PD":261077045354.46167,"PD_USD":5304805128.38435,"HAO_PD":46132313914.133377,"HAO_PD_USD":937359066.185515,"HG_HACIM":8.37500049E8,"DOLAR_BAZLI_MIN":1.4975,"DOLAR_BAZLI_MAX":1.5178,"DOLAR_BAZLI_AOF":1.5091}]}""";
    private const string PricesEbebkJson = """{"ok":true,"errorCode":null,"errorDescription":null,"transactionId":"e1575568203e49559c78cb689889ef75","value":[{"HGDG_HS_KODU":"EBEBK","HGDG_TARIH":"06-10-2026","HGDG_KAPANIS":79.4719,"HGDG_AOF":79.78935,"HGDG_MIN":79.27344,"HGDG_MAX":80.61285,"HGDG_HACIM":13476761.000000,"END_ENDEKS_KODU":"01","END_TARIH":1791234000000,"END_SEANS":2,"END_DEGER":12374.26,"DD_DOVIZ_KODU":"USD","DD_DT_KODU":"01","DD_TARIH":1791234000000,"DD_DEGER":49.1802,"DOLAR_BAZLI_FIYAT":1.6159,"ENDEKS_BAZLI_FIYAT":0.0064,"DOLAR_HACIM":274028.1861,"SERMAYE":1.6E8,"HG_KAPANIS":80.1,"HG_AOF":80.42,"HG_MIN":79.9,"HG_MAX":81.25,"PD":12815999755.85938,"PD_USD":260592672.57676,"HAO_PD":3552595132.324220,"HAO_PD_USD":72236288.838278,"HG_HACIM":1.3476761E7,"DOLAR_BAZLI_MIN":1.6119,"DOLAR_BAZLI_MAX":1.6391,"DOLAR_BAZLI_AOF":1.6224},{"HGDG_HS_KODU":"EBEBK","HGDG_TARIH":"07-10-2026","HGDG_KAPANIS":76.1481,"HGDG_AOF":77.61355,"HGDG_MIN":76.14813,"HGDG_MAX":79.86873,"HGDG_HACIM":42309082.000000,"END_ENDEKS_KODU":"01","END_TARIH":1791320400000,"END_SEANS":2,"END_DEGER":12122.94,"DD_DOVIZ_KODU":"USD","DD_DT_KODU":"01","DD_TARIH":1791320400000,"DD_DEGER":49.1976,"DOLAR_BAZLI_FIYAT":1.5478,"ENDEKS_BAZLI_FIYAT":0.0063,"DOLAR_HACIM":859982.6414,"SERMAYE":1.6E8,"HG_KAPANIS":76.75,"HG_AOF":78.227,"HG_MIN":76.75,"HG_MAX":80.5,"PD":12280000000.00000,"PD_USD":249605671.82139,"HAO_PD":3408928000.000000,"HAO_PD_USD":69290534.497618,"HG_HACIM":4.2309082E7,"DOLAR_BAZLI_MIN":1.5478,"DOLAR_BAZLI_MAX":1.6234,"DOLAR_BAZLI_AOF":1.5776},{"HGDG_HS_KODU":"EBEBK","HGDG_TARIH":"08-10-2026","HGDG_KAPANIS":80.0176,"HGDG_AOF":78.376526,"HGDG_MIN":74.95755,"HGDG_MAX":80.56323,"HGDG_HACIM":39335497.000000,"END_ENDEKS_KODU":"01","END_TARIH":1791406800000,"END_SEANS":2,"END_DEGER":12213.58,"DD_DOVIZ_KODU":"USD","DD_DT_KODU":"01","DD_TARIH":1791406800000,"DD_DEGER":49.2152,"DOLAR_BAZLI_FIYAT":1.6259,"ENDEKS_BAZLI_FIYAT":0.0066,"DOLAR_HACIM":799255.0472,"SERMAYE":1.6E8,"HG_KAPANIS":80.65,"HG_AOF":78.996,"HG_MIN":75.55,"HG_MAX":81.2,"PD":12904000244.14063,"PD_USD":262195424.26203,"HAO_PD":3588602467.895509,"HAO_PD_USD":72916547.487271,"HG_HACIM":3.9335497E7,"DOLAR_BAZLI_MIN":1.5231,"DOLAR_BAZLI_MAX":1.6370,"DOLAR_BAZLI_AOF":1.5925}]}""";

    // ---- constructed, shape-faithful variants (honestly labeled; never captures) ----
    // A single-row body that carries only the alternate HG_* value spellings (and a
    // tempting HGDG_AOF that must NOT be used to invent `open`).
    private const string PricesHgFallbackJson = """{"ok":true,"errorCode":null,"value":[{"HGDG_HS_KODU":"ASELS","HGDG_TARIH":"08-10-2026","HGDG_AOF":345.473,"HG_KAPANIS":348.75,"HG_MIN":340.25,"HG_MAX":354.0,"HG_HACIM":1.2941100644E10}]}""";

    // Transport-failure bodies (TC-MDF-070): a real-shaped envelope reporting a source failure.
    private const string PricesOkFalseJson = """{"ok":false,"errorCode":null,"errorDescription":"geçersiz istek","transactionId":"0000000000000000000000000000000a","value":null}""";
    private const string PricesErrorCodeJson = """{"ok":true,"errorCode":"ERR_NO_DATA","errorDescription":"veri bulunamadı","transactionId":"0000000000000000000000000000000b","value":[]}""";

    // ---- constructed, shape-faithful KAP XBRL fact bodies (TKT-foundation-017; honestly
    //      labeled — a bounded live capture may replace them via TKT-foundation-018) ----

    // ASELS: IFRS spelling for FY2025, the Turkish-taxonomy `tr:Hasilat` for FY2024 — both
    // collapse onto the canonical `REV`. An unmapped KAP tag passes through as `OTHER_*`.
    private const string StatementsAselsJson = """{"companyCode":"ASELS","unitScale":"units","currency":"TRY","facts":[{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"IS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:Revenue","value":"1.234.567,89"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"IS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:CostOfSales","value":"987.654,32"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"IS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:ProfitLoss","value":"123.456,78"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"BS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:Equity","value":"5.000.000,00"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"BS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:NumberOfShares","value":"456.000.000"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"BS","version":"as_reported","publishedAt":"2026-03-10","concept":"kap:OzkaynakDegisimTablosu","value":"42.000,00"},{"fiscalYear":2024,"periodType":"FY","periodEndDate":"2024-12-31","statementType":"IS","version":"as_reported","publishedAt":"2025-03-10","concept":"tr:Hasilat","value":"1.100.000,00"},{"fiscalYear":2024,"periodType":"FY","periodEndDate":"2024-12-31","statementType":"IS","version":"as_reported","publishedAt":"2025-03-10","concept":"ifrs-full:ProfitLoss","value":"100.000,00"}]}""";
    private const string StatementsThyaoJson = """{"companyCode":"THYAO","unitScale":"units","currency":"TRY","facts":[{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"IS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:Revenue","value":"2.345.678.901,23"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"IS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:CostOfSales","value":"1.900.000.000,00"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"IS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:ProfitLoss","value":"250.000.000,00"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"BS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:Equity","value":"900.000.000,00"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"BS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:NumberOfShares","value":"1.380.000.000"},{"fiscalYear":2024,"periodType":"FY","periodEndDate":"2024-12-31","statementType":"IS","version":"as_reported","publishedAt":"2025-03-10","concept":"ifrs-full:Revenue","value":"2.000.000.000,00"},{"fiscalYear":2024,"periodType":"FY","periodEndDate":"2024-12-31","statementType":"IS","version":"as_reported","publishedAt":"2025-03-10","concept":"ifrs-full:ProfitLoss","value":"200.000.000,00"}]}""";
    // GARAN FY2024 carries the KAP spelling `kap:NetSatisTutari` — the third tag variant
    // that collapses onto `REV` (TC-MDF-067).
    private const string StatementsGaranJson = """{"companyCode":"GARAN","unitScale":"units","currency":"TRY","facts":[{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"IS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:Revenue","value":"3.456.789.012,34"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"IS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:ProfitLoss","value":"456.789.012,34"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"BS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:Equity","value":"4.200.000.000,00"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"BS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:NumberOfShares","value":"4.200.000.000"},{"fiscalYear":2024,"periodType":"FY","periodEndDate":"2024-12-31","statementType":"IS","version":"as_reported","publishedAt":"2025-03-10","concept":"kap:NetSatisTutari","value":"3.000.000.000,00"},{"fiscalYear":2024,"periodType":"FY","periodEndDate":"2024-12-31","statementType":"IS","version":"as_reported","publishedAt":"2025-03-10","concept":"ifrs-full:ProfitLoss","value":"400.000.000,00"}]}""";
    private const string StatementsFrotoJson = """{"companyCode":"FROTO","unitScale":"units","currency":"TRY","facts":[{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"IS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:Revenue","value":"4.567.890.123,45"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"IS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:ProfitLoss","value":"567.890.123,45"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"BS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:Equity","value":"3.509.100.000,00"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"BS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:NumberOfShares","value":"350.910.000"},{"fiscalYear":2024,"periodType":"FY","periodEndDate":"2024-12-31","statementType":"IS","version":"as_reported","publishedAt":"2025-03-10","concept":"ifrs-full:Revenue","value":"4.000.000.000,00"},{"fiscalYear":2024,"periodType":"FY","periodEndDate":"2024-12-31","statementType":"IS","version":"as_reported","publishedAt":"2025-03-10","concept":"ifrs-full:ProfitLoss","value":"500.000.000,00"}]}""";
    private const string StatementsEbebkJson = """{"companyCode":"EBEBK","unitScale":"units","currency":"TRY","facts":[{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"IS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:Revenue","value":"5.678.901.234,56"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"IS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:ProfitLoss","value":"67.890.123,45"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"BS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:Equity","value":"160.000.000,00"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"BS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:NumberOfShares","value":"160.000.000"},{"fiscalYear":2024,"periodType":"FY","periodEndDate":"2024-12-31","statementType":"IS","version":"as_reported","publishedAt":"2025-03-10","concept":"ifrs-full:Revenue","value":"5.000.000.000,00"},{"fiscalYear":2024,"periodType":"FY","periodEndDate":"2024-12-31","statementType":"IS","version":"as_reported","publishedAt":"2025-03-10","concept":"ifrs-full:ProfitLoss","value":"50.000.000,00"}]}""";

    // The tolerance-edge variant (TC-MDF-065): one FY2025 income-statement group carrying a
    // distinct concept per edge so no two collapse — accounting parentheses, en-dash minus,
    // hyphen minus, NBSP thousand-groupings, a trailing `%` and a plain integer.
    private const string StatementsToleranceEdgesJson = """{"companyCode":"ASELS","unitScale":"units","currency":"TRY","facts":[{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"IS","version":"as_reported","concept":"kap:EdgeParentheses","value":"(1.234,50)"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"IS","version":"as_reported","concept":"kap:EdgeEnDash","value":"–1.234,50"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"IS","version":"as_reported","concept":"kap:EdgeHyphen","value":"-1.234,50"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"IS","version":"as_reported","concept":"kap:EdgeNbsp","value":"1\u00A0234\u00A0567,89"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"IS","version":"as_reported","concept":"kap:EdgePercent","value":"12,5%"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"IS","version":"as_reported","concept":"kap:EdgePlain","value":"456.000.000"}]}""";

    // Duplicate-concept bodies for TC-MDF-071 (constructed, honestly labeled). Both use the
    // seeded ALFA ticker so the full statements job resolves the instrument.
    // Equal duplicates collapse idempotently (both `ifrs-full:Revenue` and
    // `kap:NetSatisTutari` → REV 1.234.567,89).
    private const string StatementsDuplicateEqualJson = """{"companyCode":"ALFA","unitScale":"units","currency":"TRY","facts":[{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"IS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:Revenue","value":"1.234.567,89"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"IS","version":"as_reported","publishedAt":"2026-03-10","concept":"kap:NetSatisTutari","value":"1.234.567,89"}]}""";
    // Unequal duplicates (REV 1.234.567,89 vs 9.999,99) are a `CONFLICTING_VALUE` event; the
    // BS group is a non-conflicting sibling that must still ingest (partial batch).
    private const string StatementsDuplicateUnequalJson = """{"companyCode":"ALFA","unitScale":"units","currency":"TRY","facts":[{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"IS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:Revenue","value":"1.234.567,89"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"IS","version":"as_reported","publishedAt":"2026-03-10","concept":"tr:Hasilat","value":"9.999,99"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"BS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:Equity","value":"5.000.000,00"}]}""";

    // ---- constructed, shape-faithful EVDS macro body (TKT-foundation-019; honestly labeled
    //      — the bounded live capture is an explicit ticket criterion, recorded as a gap,
    //      never fabricated). The real EVDS envelope is column-oriented: one `items` row per
    //      observation date (`Tarih`, dd-MM-yyyy) with one column per requested series code;
    //      a series with no observation that date is `null`. A value may arrive as a JSON
    //      number or a locale-free numeric string (TP.APIFON1 below). `YEARWEEK` is an
    //      unrelated EVDS export column the decoder must ignore. Dates sit inside the
    //      bounded 90-day lookback ending 2026-10-06 (2026-07-08..2026-10-06).
    private const string MacroEvdsJson = """{"totalCount":4,"items":[{"Tarih":"01-09-2026","YEARWEEK":"2026-36","TP.TUKFIY2025.GENEL":46.30,"TP.APIFON1":null,"TP.DK.USD.A.YTL":null,"TP.DK.EUR.A.YTL":null,"TP.MK.ALTIN":null},{"Tarih":"15-09-2026","YEARWEEK":"2026-38","TP.TUKFIY2025.GENEL":null,"TP.APIFON1":"43.00","TP.DK.USD.A.YTL":null,"TP.DK.EUR.A.YTL":null,"TP.MK.ALTIN":null},{"Tarih":"26-09-2026","YEARWEEK":"2026-39","TP.TUKFIY2025.GENEL":null,"TP.APIFON1":null,"TP.DK.USD.A.YTL":null,"TP.DK.EUR.A.YTL":null,"TP.MK.ALTIN":5325.50},{"Tarih":"06-10-2026","YEARWEEK":"2026-41","TP.TUKFIY2025.GENEL":null,"TP.APIFON1":null,"TP.DK.USD.A.YTL":47.15,"TP.DK.EUR.A.YTL":51.55,"TP.MK.ALTIN":null}]}""";

    // ---- EVDS 3.0 macro bodies (TKT-foundation-027) ----
    // The constructed five-series body: daily FX/gold (06-10-2026) + monthly repo/CPI (2026-09)
    // + the year-prior CPI index (2025-09) so the CPI YoY pair is computable. Column keys
    // underscore the dotted codes; gold carries a thousands-grouping comma to exercise the decode.
    private const string MacroEvdsV3Json = """{"totalCount":3,"items":[{"Tarih":"06-10-2026","TP_DK_USD_S_YTL":"48.10","TP_DK_EUR_S_YTL":"53.20","TP_ALTINPIYASA_KAP03":"4,200.00","TP_BISPOLFAIZ_TUR":null,"TP_TUKFIY2025_GENEL":null},{"Tarih":"2026-09","TP_DK_USD_S_YTL":null,"TP_DK_EUR_S_YTL":null,"TP_ALTINPIYASA_KAP03":null,"TP_BISPOLFAIZ_TUR":"40.50","TP_TUKFIY2025_GENEL":"137.23"},{"Tarih":"2025-09","TP_DK_USD_S_YTL":null,"TP_DK_EUR_S_YTL":null,"TP_ALTINPIYASA_KAP03":null,"TP_BISPOLFAIZ_TUR":null,"TP_TUKFIY2025_GENEL":"105.78"}],"transposedItems":[]}""";

    // The bounded live capture: verbatim from the anonymous EVDS 3.0 /fe (2026-10-18).
    private const string MacroEvdsV3CaptureJson = """{"totalCount":2,"items":[{"Tarih":"15-10-2026","TP_DK_USD_S_YTL":"49.39","TP_DK_EUR_S_YTL":"55.30","UNIXTIME":{"$numberLong":"1792011600"}},{"Tarih":"16-10-2026","TP_DK_USD_S_YTL":"49.42","TP_DK_EUR_S_YTL":"55.41","UNIXTIME":{"$numberLong":"1792098000"}}],"transposedItems":[{"serieCode":"TP_DK_USD_S_YTL","serieName":"(USD)  ABD  Doları  (Döviz  Satış)","15-10-2026":"49.39","16-10-2026":"49.42"},{"serieCode":"TP_DK_EUR_S_YTL","serieName":"(EUR)  Euro  (Döviz  Satış)","15-10-2026":"55.30","16-10-2026":"55.41"}],"transposedColumns":[{"key":"serieName","title":""},{"key":"16-10-2026","title":"16-10-2026"},{"key":"15-10-2026","title":"15-10-2026"}],"seriesNames":{"TP_DK_USD_S_YTL":"(USD)  ABD  Doları  (Döviz  Satış)","TP_DK_EUR_S_YTL":"(EUR)  Euro  (Döviz  Satış)"},"frequencyConversion":{"TP.DK.EUR.S.YTL":false,"TP.DK.USD.S.YTL":false}}""";

    private const string MacroEvdsV3CatalogJson = """[{"CATEGORY_ID":1,"TOPIC_TITLE_ENG":"Exchange Rates","SEVIYE":3,"UST_CATEGORY_ID":1,"DATAGROUPS":[{"screenOrder":10,"DATAGROUP_CODE":"bie_dkdovytl","CATEGORY_ID":1,"DATAGROUP_TYPE":"Döviz Kurları","DATAGROUP_TYPE_ENG":"Exchange Rates"}]},{"CATEGORY_ID":2,"TOPIC_TITLE_ENG":"Gold","SEVIYE":3,"UST_CATEGORY_ID":2,"DATAGROUPS":[{"screenOrder":10,"DATAGROUP_CODE":"bie_mkaltytl","CATEGORY_ID":2,"DATAGROUP_TYPE":"Altın Fiyatları","DATAGROUP_TYPE_ENG":"Gold Prices"},{"screenOrder":20,"DATAGROUP_CODE":"bie_altinbistbul","CATEGORY_ID":2,"DATAGROUP_TYPE":"Altın BIST Bülten","DATAGROUP_TYPE_ENG":"Gold BIST Bulletin"}]},{"CATEGORY_ID":3,"TOPIC_TITLE_ENG":"Policy Rate","SEVIYE":3,"UST_CATEGORY_ID":3,"DATAGROUPS":[{"screenOrder":10,"DATAGROUP_CODE":"bie_bispolfaiz","CATEGORY_ID":3,"DATAGROUP_TYPE":"Merkez Bankaları Politika Faiz Oranı","DATAGROUP_TYPE_ENG":"Central Banks' Policy Interest Rate"}]},{"CATEGORY_ID":4,"TOPIC_TITLE_ENG":"Consumer Price Index","SEVIYE":3,"UST_CATEGORY_ID":4,"DATAGROUPS":[{"screenOrder":10,"DATAGROUP_CODE":"bie_tukfiy2025","CATEGORY_ID":4,"DATAGROUP_TYPE":"Tüketici Fiyat Endeksi (2025=100)","DATAGROUP_TYPE_ENG":"Consumer Price Index (2025=100)"}]}]""";

    private const string MacroEvdsV3SerieListJson = """[{"SERIE_CODE":"TP.DK.USD.S.YTL","DATAGROUP_CODE":"bie_dkdovytl","SERIE_NAME":"(USD) ABD Doları (Döviz Satış)","SERIE_NAME_ENG":"USD/TRY Selling","FREQUENCY_STR":"GÜNLÜK","DEFAULT_AGG_METHOD":"avg"},{"SERIE_CODE":"TP.DK.EUR.S.YTL","DATAGROUP_CODE":"bie_dkdovytl","SERIE_NAME":"(EUR) Euro (Döviz Satış)","SERIE_NAME_ENG":"EUR/TRY Selling","FREQUENCY_STR":"GÜNLÜK","DEFAULT_AGG_METHOD":"avg"},{"SERIE_CODE":"TP.ALTINPIYASA.KAP02","DATAGROUP_CODE":"bie_altinbistbul","SERIE_NAME":"Altın - Kapanış Fiyatı  - TL/kg","SERIE_NAME_ENG":"Gold - Closing Price - TRY/kg","FREQUENCY_STR":"İŞ GÜNÜ","DEFAULT_AGG_METHOD":"last"},{"SERIE_CODE":"TP.ALTINPIYASA.KAP03","DATAGROUP_CODE":"bie_altinbistbul","SERIE_NAME":"Altın - Kapanış Fiyatı  - USD/ons","SERIE_NAME_ENG":"Gold - Closing Price - USD/ons","FREQUENCY_STR":"İŞ GÜNÜ","DEFAULT_AGG_METHOD":"last"},{"SERIE_CODE":"TP.BISPOLFAIZ.TUR","DATAGROUP_CODE":"bie_bispolfaiz","SERIE_NAME":"Türkiye (TUR) Merkez Bankası Politika Faiz Oranı","SERIE_NAME_ENG":"Türkiye (TUR) Central Bank Policy Interest Rate","FREQUENCY_STR":"AYLIK","DEFAULT_AGG_METHOD":"last"},{"SERIE_CODE":"TP.TUKFIY2025.GENEL","DATAGROUP_CODE":"bie_tukfiy2025","SERIE_NAME":"Genel Endeks","SERIE_NAME_ENG":"General Index","FREQUENCY_STR":"AYLIK","DEFAULT_AGG_METHOD":"avg"}]""";

    // ---- bounded live captures: universe / index-levels (TKT-foundation-021) ----
    private const string UniverseJson = """{"d":"{\"Sector\":null,\"Oran\":[{\"HISSE_KODU\":\"A1CAP\",\"PRICE_TL\":3.73,\"ONERI\":null,\"HEDEF_PD_YTL1\":null,\"HEDEF_FIYAT\":null,\"GETIRI_POT\":null,\"TARIH\":null,\"MARKET_CAP_TL\":2517750000.0,\"PAID_UP_CAP\":675000000.0,\"AS_ALT_SEKTOR_KODU\":null,\"AS_ALT_SEKTOR_TANIMI\":\"Aracı Kurumlar\",\"AS_ALT_SEKTOR_TANIMI_YD\":null,\"AS_SEKTOR_KODU\":null,\"HISSE_TANIM\":\"A1 Capital \",\"HISSE_TANIM_YD\":null,\"BHG_GRUP_KODU\":null,\"HSG_TANIMI\":null,\"HSG_TANIMI_YD\":null,\"PE\":1.48040337094977,\"MV_BV\":0.275777604698282,\"EV_NS\":-0.290713111577104,\"EV_EBITDA\":-1.75023754855589,\"BASLANGIC_TRH\":null,\"GUN_TRH\":null,\"END_GUN_GETIRI\":-3.74,\"DOV_GUN_GETIRI\":-6.58,\"GUN_GETIRI\":-6.52,\"HAFTA_TRH\":null,\"END_HF_GETIRI\":-15.7,\"DOV_HF_GETIRI\":-19.82,\"HAFTA_GETIRI\":-19.44,\"AY_TRH\":null,\"END_AY_GETIRI\":-35.26,\"DOV_AY_GETIRI\":-43.08,\"AY_GETIRI\":-42.17,\"YIL_TRH\":null,\"END_YIL_GETIRI\":-72.45,\"DOV_YIL_GETIRI\":-73.65,\"YIL_GETIRI\":-68.86,\"YTD_TRH\":null,\"END_YTD_GETIRI\":-69.63,\"DOV_YTD_GETIRI\":-72.56,\"YTD_GETIRI\":-68.39,\"FK_PRİM\":null,\"PDD_PRIM\":null,\"FDFAVOK_PRIM\":null,\"FK\":0.0,\"PDD\":null,\"FDFAVOK\":null,\"TARIH_0\":null,\"TARIH_1\":null,\"TARIH_2\":null,\"TARIH_3\":null,\"MARKET_CAP_DOV\":50.918876,\"FREE_FLOAT_RATE\":30.96,\"SON_DONEM\":\"6/2026\"},{\"HISSE_KODU\":\"A1YEN\",\"PRICE_TL\":1.56,\"ONERI\":null,\"HEDEF_PD_YTL1\":null,\"HEDEF_FIYAT\":null,\"GETIRI_POT\":null,\"TARIH\":null,\"MARKET_CAP_TL\":936000000.0,\"PAID_UP_CAP\":600000000.0,\"AS_ALT_SEKTOR_KODU\":null,\"AS_ALT_SEKTOR_TANIMI\":\"Elektrik Üretim\",\"AS_ALT_SEKTOR_TANIMI_YD\":null,\"AS_SEKTOR_KODU\":null,\"HISSE_TANIM\":\"Kartal Yenilenebili Enerji\",\"HISSE_TANIM_YD\":null,\"BHG_GRUP_KODU\":null,\"HSG_TANIMI\":null,\"HSG_TANIMI_YD\":null,\"PE\":7.29857147462194,\"MV_BV\":0.262637347337202,\"EV_NS\":2.1390573998773,\"EV_EBITDA\":3.52693012872535,\"BASLANGIC_TRH\":null,\"GUN_TRH\":null,\"END_GUN_GETIRI\":-0.23,\"DOV_GUN_GETIRI\":-3.17,\"GUN_GETIRI\":-3.11,\"HAFTA_TRH\":null,\"END_HF_GETIRI\":-7.24,\"DOV_HF_GETIRI\":-11.77,\"HAFTA_GETIRI\":-11.36,\"AY_TRH\":null,\"END_AY_GETIRI\":-27.23,\"DOV_AY_GETIRI\":-36.02,\"AY_GETIRI\":-35.0,\"YIL_TRH\":null,\"END_YIL_GETIRI\":-45.87,\"DOV_YIL_GETIRI\":-48.22,\"YIL_GETIRI\":-38.82,\"YTD_TRH\":null,\"END_YTD_GETIRI\":-43.44,\"DOV_YTD_GETIRI\":-48.9,\"YTD_GETIRI\":-41.13,\"FK_PRİM\":null,\"PDD_PRIM\":null,\"FDFAVOK_PRIM\":null,\"FK\":0.0,\"PDD\":null,\"FDFAVOK\":null,\"TARIH_0\":null,\"TARIH_1\":null,\"TARIH_2\":null,\"TARIH_3\":null,\"MARKET_CAP_DOV\":18.929626,\"FREE_FLOAT_RATE\":74.77,\"SON_DONEM\":\"6/2026\"},{\"HISSE_KODU\":\"AAGYO\",\"PRICE_TL\":8.05,\"ONERI\":null,\"HEDEF_PD_YTL1\":null,\"HEDEF_FIYAT\":null,\"GETIRI_POT\":null,\"TARIH\":null,\"MARKET_CAP_TL\":5643050000.0,\"PAID_UP_CAP\":701000000.0,\"AS_ALT_SEKTOR_KODU\":null,\"AS_ALT_SEKTOR_TANIMI\":\"GYO\",\"AS_ALT_SEKTOR_TANIMI_YD\":null,\"AS_SEKTOR_KODU\":null,\"HISSE_TANIM\":\"Ağaoğlu GYO\",\"HISSE_TANIM_YD\":null,\"BHG_GRUP_KODU\":null,\"HSG_TANIMI\":null,\"HSG_TANIMI_YD\":null,\"PE\":-133.319668365597,\"MV_BV\":0.340123745663634,\"EV_NS\":-100.0,\"EV_EBITDA\":-100.0,\"BASLANGIC_TRH\":null,\"GUN_TRH\":null,\"END_GUN_GETIRI\":-0.12,\"DOV_GUN_GETIRI\":-3.07,\"GUN_GETIRI\":-3.01,\"HAFTA_TRH\":null,\"END_HF_GETIRI\":-2.5,\"DOV_HF_GETIRI\":-7.27,\"HAFTA_GETIRI\":-6.83,\"AY_TRH\":null,\"END_AY_GETIRI\":-13.42,\"DOV_AY_GETIRI\":-23.89,\"AY_GETIRI\":-22.67,\"YIL_TRH\":null,\"END_YIL_GETIRI\":null,\"DOV_YIL_GETIRI\":0.0,\"YIL_GETIRI\":0.0,\"YTD_TRH\":null,\"END_YTD_GETIRI\":null,\"DOV_YTD_GETIRI\":null,\"YTD_GETIRI\":null,\"FK_PRİM\":null,\"PDD_PRIM\":null,\"FDFAVOK_PRIM\":null,\"FK\":0.0,\"PDD\":null,\"FDFAVOK\":null,\"TARIH_0\":null,\"TARIH_1\":null,\"TARIH_2\":null,\"TARIH_3\":null,\"MARKET_CAP_DOV\":114.124821,\"FREE_FLOAT_RATE\":24.01,\"SON_DONEM\":\"6/2026\"},{\"HISSE_KODU\":\"ACSEL\",\"PRICE_TL\":83.45,\"ONERI\":null,\"HEDEF_PD_YTL1\":null,\"HEDEF_FIYAT\":null,\"GETIRI_POT\":null,\"TARIH\":null,\"MARKET_CAP_TL\":894725865.0,\"PAID_UP_CAP\":10721700.0,\"AS_ALT_SEKTOR_KODU\":null,\"AS_ALT_SEKTOR_TANIMI\":\"Kimyasal Ürün\",\"AS_ALT_SEKTOR_TANIMI_YD\":null,\"AS_SEKTOR_KODU\":null,\"HISSE_TANIM\":\"Acıselsan Acıpayam Selüloz\",\"HISSE_TANIM_YD\":null,\"BHG_GRUP_KODU\":null,\"HSG_TANIMI\":null,\"HSG_TANIMI_YD\":null,\"PE\":-142.130434223168,\"MV_BV\":1.32493397896905,\"EV_NS\":1.13467894269292,\"EV_EBITDA\":9.94970243877881,\"BASLANGIC_TRH\":null,\"GUN_TRH\":null,\"END_GUN_GETIRI\":1.45,\"DOV_GUN_GETIRI\":-1.54,\"GUN_GETIRI\":-1.48,\"HAFTA_TRH\":null,\"END_HF_GETIRI\":-2.65,\"DOV_HF_GETIRI\":-7.41,\"HAFTA_GETIRI\":-6.97,\"AY_TRH\":null,\"END_AY_GETIRI\":-9.56,\"DOV_AY_GETIRI\":-20.49,\"AY_GETIRI\":-19.22,\"YIL_TRH\":null,\"END_YIL_GETIRI\":-35.24,\"DOV_YIL_GETIRI\":-38.05,\"YIL_GETIRI\":-26.8,\"YTD_TRH\":null,\"END_YTD_GETIRI\":-17.95,\"DOV_YTD_GETIRI\":-25.87,\"YTD_GETIRI\":-14.59,\"FK_PRİM\":null,\"PDD_PRIM\":null,\"FDFAVOK_PRIM\":null,\"FK\":0.0,\"PDD\":null,\"FDFAVOK\":null,\"TARIH_0\":null,\"TARIH_1\":null,\"TARIH_2\":null,\"TARIH_3\":null,\"MARKET_CAP_DOV\":18.0949,\"FREE_FLOAT_RATE\":47.91,\"SON_DONEM\":\"6/2026\"},{\"HISSE_KODU\":\"ADEL\",\"PRICE_TL\":24.44,\"ONERI\":null,\"HEDEF_PD_YTL1\":null,\"HEDEF_FIYAT\":null,\"GETIRI_POT\":null,\"TARIH\":null,\"MARKET_CAP_TL\":6351345000.0,\"PAID_UP_CAP\":259875000.0,\"AS_ALT_SEKTOR_KODU\":null,\"AS_ALT_SEKTOR_TANIMI\":\"Kırtasiye\",\"AS_ALT_SEKTOR_TANIMI_YD\":null,\"AS_SEKTOR_KODU\":null,\"HISSE_TANIM\":\"Adel Kalemcilik               \",\"HISSE_TANIM_YD\":null,\"BHG_GRUP_KODU\":null,\"HSG_TANIMI\":null,\"HSG_TANIMI_YD\":null,\"PE\":-14.8974922089773,\"MV_BV\":4.62284482758621,\"EV_NS\":2.92364039261732,\"EV_EBITDA\":46.6648638346784,\"BASLANGIC_TRH\":null,\"GUN_TRH\":null,\"END_GUN_GETIRI\":2.14,\"DOV_GUN_GETIRI\":-0.87,\"GUN_GETIRI\":-0.81,\"HAFTA_TRH\":null,\"END_HF_GETIRI\":-2.39,\"DOV_HF_GETIRI\":-7.16,\"HAFTA_GETIRI\":-6.72,\"AY_TRH\":null,\"END_AY_GETIRI\":-7.5,\"DOV_AY_GETIRI\":-18.68,\"AY_GETIRI\":-17.38,\"YIL_TRH\":null,\"END_YIL_GETIRI\":-27.44,\"DOV_YIL_GETIRI\":-30.59,\"YIL_GETIRI\":-17.99,\"YTD_TRH\":null,\"END_YTD_GETIRI\":-28.85,\"DOV_YTD_GETIRI\":-35.72,\"YTD_GETIRI\":-25.94,\"FK_PRİM\":null,\"PDD_PRIM\":null,\"FDFAVOK_PRIM\":null,\"FK\":0.0,\"PDD\":null,\"FDFAVOK\":null,\"TARIH_0\":null,\"TARIH_1\":null,\"TARIH_2\":null,\"TARIH_3\":null,\"MARKET_CAP_DOV\":128.449351,\"FREE_FLOAT_RATE\":27.59,\"SON_DONEM\":\"6/2026\"}]}"}""";

    private const string IndexLevelsJson = """{"data":[[1791234000000,15381.69043],[1791320400000,15013.83008],[1791406800000,15137.57031],[1791493200000,15145.26953],[1791752400000,15001.28027],[1791838800000,15191.57031],[1791925200000,15047.87012],[1792011600000,14986.78027],[1792098000000,14558.42969]],"timestamp":"2026-10-18T02:45:43.0802138+03:00"}""";

    private const string IndexLevelsXu100Json = """{"data":[[1791234000000,12374.25977],[1791320400000,12122.94043],[1791406800000,12213.58008],[1791493200000,12265.98047],[1791752400000,12150.66992],[1791838800000,12250.67969],[1791925200000,12118.29004],[1792011600000,12070.16992],[1792098000000,11721.71973]],"timestamp":"2026-10-18T07:24:26.4004374+03:00"}""";

    // ---- KAP VYK bodies (TKT-foundation-026): two shape-faithful constructed list bodies
    //      plus one bounded live capture of the real `presentation` detail ----

    // Şirket Listesi: companyId ↔ BIST stockCode (the symbol→companyId map the loop builds).
    private const string VykMembersJson = """[{"id":"1001","title":"ALFA TEST A.Ş.","stockCode":"ALFA","memberType":"stock","kfifUrl":null},{"id":"1002","title":"EPSL TEST A.Ş.","stockCode":"EPSL","memberType":"stock","kfifUrl":null}]""";

    // Bildirim Listesi: the financial-report (`disclosureClass` FR) disclosures of one company,
    // each carrying its owning `companyId` (the cross-company identity guard's input).
    private const string VykDisclosuresJson = """[{"disclosureIndex":"1001","disclosureType":"FR","disclosureClass":"FR","title":"Finansal Rapor","companyId":"1001","subReportIds":[],"acceptedDataFileTypes":["data"]}]""";

    // Bildirim Detayı (`?fileType=data`) — a bounded live capture of the real shape: fiscal
    // coordinates plus the XBRL-style `presentation` trees the gateway actually returns
    // (live-verified 2026-10-18; there is no `flatData` member). Each presentation's
    // `ContextList` binds context ids to periods and each leaf report item carries a bare
    // IFRS taxonomy `name` with a `Values.Value[]` list of `{contextId, currency, rounding,
    // value}` — the decoder reads the `CURR` context's value.
    private const string VykDisclosureDetailJson = """{"disclosureIndex":"1226255","senderId":"5900","senderTitle":"1000 YATIRIMLAR HOLDİNG A.Ş.","senderExchCodes":["BINHO"],"behalfSenderId":"5900","disclosureReason":"NEW","disclosureType":"FR","disclosureClass":"FR","subject":{"tr":"Finansal Rapor ","en":"Financial Report"},"consolidation":"NC","year":"2023","period":{"tr":"9 Aylık","en":"9 Months"},"relatedStocks":[],"summary":{"tr":null,"en":null},"time":"15.12.2023 20:08:10","link":"https://www.kap.org.tr/Bildirim/1226255","attachmentUrls":[],"presentation":[{"id":"general_role_210015","content":{"id":"general_role_210015","isMultiDimensional":"no","ContextList":{"Context":[{"id":"2022-12-31","key":"PREV","Period":{"instant":"2022-12-31"}},{"id":"2023-09-30","key":"CURR","Period":{"instant":"2023-09-30"}}]},"ReportItem":{"name":"StatementOfFinancialPositionBalanceSheetAbstract","abstract":"true","ReportItem":[{"name":"CashAndCashEquivalents","Values":{"Value":[{"contextId":"2023-09-30","currency":"TRY","rounding":"INF","value":"355754"},{"contextId":"2022-12-31","currency":"TRY","rounding":"INF","value":"160725"}]},"langs":{"lang":[{"code":"tr","value":"Nakit ve Nakit Benzerleri"},{"code":"en","value":"Cash and cash equivalents"}]}},{"preferredLabel":"totalLabel","name":"CurrentAssets","Values":{"Value":[{"contextId":"2023-09-30","currency":"TRY","rounding":"INF","value":"256707489"},{"contextId":"2022-12-31","currency":"TRY","rounding":"INF","value":"1348983"}]},"langs":{"lang":[{"code":"tr","value":"TOPLAM DÖNEN VARLIKLAR"},{"code":"en","value":"Total current assets"}]}},{"preferredLabel":"totalLabel","name":"Assets","Values":{"Value":[{"contextId":"2023-09-30","currency":"TRY","rounding":"INF","value":"10169533147"},{"contextId":"2022-12-31","currency":"TRY","rounding":"INF","value":"4994449501"}]},"langs":{"lang":[{"code":"tr","value":"TOPLAM VARLIKLAR"},{"code":"en","value":"Total assets"}]}},{"preferredLabel":"totalLabel","name":"CurrentLiabilities","Values":{"Value":[{"contextId":"2023-09-30","currency":"TRY","rounding":"INF","value":"263190349"},{"contextId":"2022-12-31","currency":"TRY","rounding":"INF","value":"9550357"}]},"langs":{"lang":[{"code":"tr","value":"TOPLAM KISA VADELİ YÜKÜMLÜLÜKLER"},{"code":"en","value":"Total current liabilities"}]}},{"preferredLabel":"totalLabel","name":"Liabilities","Values":{"Value":[{"contextId":"2023-09-30","currency":"TRY","rounding":"INF","value":"2735012181"},{"contextId":"2022-12-31","currency":"TRY","rounding":"INF","value":"1153886755"}]},"langs":{"lang":[{"code":"tr","value":"TOPLAM YÜKÜMLÜLÜKLER"},{"code":"en","value":"Total liabilities"}]}},{"preferredLabel":"totalLabel","name":"Equity","Values":{"Value":[{"contextId":"2023-09-30","currency":"TRY","rounding":"INF","value":"7434520966"},{"contextId":"2022-12-31","currency":"TRY","rounding":"INF","value":"3840562746"}]},"langs":{"lang":[{"code":"tr","value":"TOPLAM ÖZKAYNAKLAR"},{"code":"en","value":"Total equity"}]}}]}}},{"id":"general_role_310003","content":{"id":"general_role_310003","isMultiDimensional":"yes","ContextList":{"Context":[{"id":"2022-01-012022-09-30","key":"PREV","Period":{"startDate":"2022-01-01","endDate":"2022-09-30"}},{"id":"2023-01-012023-09-30","key":"CURR","Period":{"startDate":"2023-01-01","endDate":"2023-09-30"}}]},"ReportItem":{"name":"StatementOfProfitOrLossAndOtherComprehensiveIncomeAbstract","abstract":"true","ReportItem":[{"name":"Revenue","Values":{"Value":[{"contextId":"2023-01-012023-09-30","currency":"TRY","rounding":"INF","value":"651945115"},{"contextId":"2022-01-012022-09-30","currency":"TRY","rounding":"INF","value":"0"}]},"langs":{"lang":[{"code":"tr","value":"Hasılat"},{"code":"en","value":"Revenue"}]}},{"preferredLabel":"standartCapitalsLabel","name":"GrossProfit","Values":{"Value":[{"contextId":"2023-01-012023-09-30","currency":"TRY","rounding":"INF","value":"651945115"},{"contextId":"2022-01-012022-09-30","currency":"TRY","rounding":"INF","value":"0"}]},"langs":{"lang":[{"code":"tr","value":"BRÜT KAR (ZARAR)"},{"code":"en","value":"GROSS PROFIT (LOSS)"}]}},{"preferredLabel":"standartCapitalsLabel","name":"ProfitLoss","Values":{"Value":[{"contextId":"2023-01-012023-09-30","currency":"TRY","rounding":"INF","value":"3566458220"},{"contextId":"2022-01-012022-09-30","currency":"TRY","rounding":"INF","value":"-1174578"}]},"langs":{"lang":[{"code":"tr","value":"DÖNEM KARI (ZARARI)"},{"code":"en","value":"PROFIT (LOSS)"}]}}]}}}]}""";


    // ---- KAP **public-site** captures (TKT-foundation-079; 01 v1.5.53 §10.9.19) ----
    // The public site is a separate source from the VYK gateway above: its own index space
    // (ASELS 2026-06 FR = 1643141; the VYK head is 1231017), its own company-identifier space
    // (ASELS public oid 4028e4a1413b7ef401413bc2251e0047; VYK companyId 866) and its own
    // evidence class (`kap-public`, never `kap`). Every body below was captured on 2026-10-25
    // with one request in flight at a time, >= 3 s apart, with a browser-shaped User-Agent
    // carrying an honest bot token. No credential exists in any body (the endpoints are
    // anonymous — verified), so nothing is redacted.
    private const string KapPublicRobotsLabel =
        "Captured verbatim from https://www.kap.org.tr/robots.txt on 2026-10-25 (a paced " +
        "GET): the www host answers that path with its HTML access-error page and HTTP 666 — " +
        "no robots.txt directive is served there, so the answer is recorded as-is rather than " +
        "summarised. (The separate apex-host probe answered HTTP 307 → /en/robots.txt → HTTP " +
        "404; both probe responses are recorded in 01 §10.9.19.)";

    private const string KapPublicByCriteriaLabel =
        "Captured live from POST /tr/api/disclosure/members/byCriteria on 2026-10-25 with the " +
        "complete request {\"fromDate\": \"2026-01-01\", \"toDate\": \"2026-10-25\", " +
        "\"disclosureClass\": \"FR\", \"subjectList\": [], " +
        "\"mkkMemberOidList\": [\"4028e4a1413b7ef401413bc2251e0047\"], " +
        "\"inactiveMkkMemberOidList\": [], \"bdkMemberOidList\": [], \"fromSrc\": false, " +
        "\"disclosureIndexList\": []}: the complete 10-row answer, verbatim — it carries the three " +
        "'Finansal Rapor' filings 1643141 (2026 6 Aylık, 04.08.2026 18:39:41), 1598316 (2026 3 " +
        "Aylık) and 1561039 (2025 Yıllık) plus their seven companion rows (1643142, 1643140, " +
        "1598321, 1598319, 1561040, 1561038, 1668298). The investigation's 3-row ASELS result " +
        "was the subjectList=[\"Finansal Rapor\"] view; with subjectList empty the same company " +
        "window answers these 10 rows. Every value real, nothing trimmed.";

    private const string KapPublicMarketProbeLabel =
        "OQ-1 probe, captured live from POST /tr/api/disclosure/members/byCriteria on " +
        "2026-10-25 (~03:xx UTC; a later probe than the investigation's 02:0x UTC one) with " +
        "the complete request {\"fromDate\": \"2026-10-24\", \"toDate\": \"2026-10-25\", " +
        "\"disclosureClass\": \"\", \"subjectList\": [], \"mkkMemberOidList\": [], " +
        "\"inactiveMkkMemberOidList\": [], \"bdkMemberOidList\": [], \"fromSrc\": false, " +
        "\"disclosureIndexList\": []}: HTTP 200 with 12 rows " +
        "— market-wide byCriteria WORKS (class mix DKB 4 / FR 3 / ODA 3 / DG 2; newest " +
        "disclosureIndex 1687047 at 24.10.2026 17:08:16, stockCode RODRG). The investigation's " +
        "02:0x UTC probe printed newest 1687040 (24.10.2026 09:57) and stockCode KT — both are " +
        "honest probes on different runs and are not reconciled into one canonical index. The " +
        "sample includes the production test entity 'KAP TEST A.Ş.' (stockCodes KTEST): the " +
        "lane never special-cases it silently — any exclusion is a named, recorded rule. " +
        "Verbatim (the complete answer).";

    private const string KapPublicReportLabel =
        "Captured live from GET /tr/api/notification/export/excel/1643141 on 2026-10-25 " +
        "(ASELSAN 2026-06 FR; the route declares application/vnd.ms-excel and serves HTML — " +
        "1,819,412 bytes, 731 tables, 1,467 rows). BOUNDED TRIM, honestly labelled: the header " +
        "metadata block (Gönderim Tarihi / Bildirim Tipi / Yıl / Periyot / Sunum Para Birimi " +
        "'1.000 TL' / Finansal Tablo Niteliği / Cari + Önceki Dönem period ends) is kept " +
        "verbatim; all four statement tables keep their headings and column-context headers; " +
        "the balance-sheet / income-statement / equity blocks are trimmed to a contiguous " +
        "prefix of their real rows, counted as outer <tr> rows including header/rowspan rows: " +
        "26 of 298 (2 header + 24 data rows), 21 of 154 (2 header + 19 data rows) and 11 of 74 " +
        "for the equity block — where those 11 are 9 header/rowspan/title rows plus only 2 data " +
        "rows ('Dönem Başı Bakiyeler', 'Muhasebe Politikalarındaki Değişiklikler'), so the " +
        "equity block is a header-preserving stub rather than an 11-row data prefix; the " +
        "cash-flow table ('Nakit Akış Tablosu (Dolaylı Yöntem)') is kept COMPLETE. Every " +
        "retained value is real — nothing is synthesised — and this is NOT a byte-verbatim " +
        "capture of the whole document.";

    private const string KapPublicIdentityLabel =
        "Captured live from POST /tr/api/search/combined with body {\"keyword\":\"ASELS\"} on " +
        "2026-10-25: the companyOrFunds hit carrying memberOrFundOid " +
        "4028e4a1413b7ef401413bc2251e0047, cmpOrFundCode 'asels', searchType 'C'. This is the " +
        "public site's company-identifier space — a third space beside the VYK numeric " +
        "companyId (ASELS 866) and our InstrumentId; the mapping is stored, never assumed " +
        "(TKT-foundation-080). Verbatim.";

    // The OQ-2 gate evidence: what the site actually answers at /robots.txt (verbatim).
    private const string KapPublicRobotsBody = """
<html>
<head>
<meta http-equiv="Content-Type" content="text/html; charset=UTF-8" />
<meta http-equiv="Content-Type" content="text/html; charset=iso-8859-9" />
</head>
<body>
<div style="margin-left:30%;margin-top:10%">
<a href="https:/www.kap.org.tr"><img src="https://www.kap.org.tr/assets/images/errors/hata_500_transparent.png" style="border:none"/></a>
<font face=arial />
<p>Yasadiginiz bu erisim problemini "349891-PPE13" hata numarasi ile </p>
<p>kapdestek@mkk.com.tr adresine bildirebilirsiniz.</p>
</div>
</body>
</html>
""";

    // The byCriteria FR window for ASELS (the lane's list double).
    private const string KapPublicByCriteriaJson = """[{"publishDate":"25.09.2026 18:15:28","fundCode":null,"kapTitle":"ASELSAN ELEKTRONİK SANAYİ VE TİCARET A.Ş.","isOldKap":false,"disclosureClass":"FR","disclosureType":"ODA","disclosureCategory":"ODA","summary":"2025 TSRS Uyumlu Sürdürülebilirlik Raporu'nun Kamuya Açıklanması","subject":"TSRS Uyumlu Sürdürülebilirlik Raporu","relatedStocks":null,"year":2025,"ruleType":"Yıllık","period":1,"disclosureIndex":1668298,"isLate":false,"stockCodes":"ASELS","hasMultiLanguageSupport":true,"attachmentCount":0,"modifyStatus":null},{"publishDate":"04.08.2026 18:39:55","fundCode":null,"kapTitle":"ASELSAN ELEKTRONİK SANAYİ VE TİCARET A.Ş.","isOldKap":false,"disclosureClass":"FR","disclosureType":"FR","disclosureCategory":"ODA","summary":"4 Ağustos 2026 Z-AS331-843.05.01-24724","subject":"Sorumluluk Beyanı (Konsolide)","relatedStocks":null,"year":2026,"ruleType":"6 Aylık","period":2,"disclosureIndex":1643142,"isLate":false,"stockCodes":"ASELS","hasMultiLanguageSupport":false,"attachmentCount":0,"modifyStatus":null},{"publishDate":"04.08.2026 18:39:41","fundCode":null,"kapTitle":"ASELSAN ELEKTRONİK SANAYİ VE TİCARET A.Ş.","isOldKap":false,"disclosureClass":"FR","disclosureType":"FR","disclosureCategory":"FR","summary":null,"subject":"Finansal Rapor","relatedStocks":null,"year":2026,"ruleType":"6 Aylık","period":2,"disclosureIndex":1643141,"isLate":false,"stockCodes":"ASELS","hasMultiLanguageSupport":true,"attachmentCount":2,"modifyStatus":null},{"publishDate":"04.08.2026 18:39:22","fundCode":null,"kapTitle":"ASELSAN ELEKTRONİK SANAYİ VE TİCARET A.Ş.","isOldKap":false,"disclosureClass":"FR","disclosureType":"FR","disclosureCategory":"ODA","summary":"4 Ağustos 2026 1315 Sayılı Yönetim Kurulu Kararı","subject":"Faaliyet Raporu (Konsolide)","relatedStocks":null,"year":2026,"ruleType":"6 Aylık","period":2,"disclosureIndex":1643140,"isLate":false,"stockCodes":"ASELS","hasMultiLanguageSupport":true,"attachmentCount":2,"modifyStatus":null},{"publishDate":"28.04.2026 18:32:04","fundCode":null,"kapTitle":"ASELSAN ELEKTRONİK SANAYİ VE TİCARET A.Ş.","isOldKap":false,"disclosureClass":"FR","disclosureType":"FR","disclosureCategory":"ODA","summary":"28 Nisan 2026 Z-AS331-843.05.01-10727","subject":"Sorumluluk Beyanı (Konsolide)","relatedStocks":null,"year":2026,"ruleType":"3 Aylık","period":1,"disclosureIndex":1598321,"isLate":false,"stockCodes":"ASELS","hasMultiLanguageSupport":false,"attachmentCount":0,"modifyStatus":null},{"publishDate":"28.04.2026 18:31:52","fundCode":null,"kapTitle":"ASELSAN ELEKTRONİK SANAYİ VE TİCARET A.Ş.","isOldKap":false,"disclosureClass":"FR","disclosureType":"FR","disclosureCategory":"ODA","summary":"28 Nisan 2026 1304 Sayılı Yönetim Kurulu Kararı","subject":"Faaliyet Raporu (Konsolide)","relatedStocks":null,"year":2026,"ruleType":"3 Aylık","period":1,"disclosureIndex":1598319,"isLate":false,"stockCodes":"ASELS","hasMultiLanguageSupport":true,"attachmentCount":2,"modifyStatus":null},{"publishDate":"28.04.2026 18:31:38","fundCode":null,"kapTitle":"ASELSAN ELEKTRONİK SANAYİ VE TİCARET A.Ş.","isOldKap":false,"disclosureClass":"FR","disclosureType":"FR","disclosureCategory":"FR","summary":null,"subject":"Finansal Rapor","relatedStocks":null,"year":2026,"ruleType":"3 Aylık","period":1,"disclosureIndex":1598316,"isLate":false,"stockCodes":"ASELS","hasMultiLanguageSupport":true,"attachmentCount":2,"modifyStatus":null},{"publishDate":"24.02.2026 18:27:48","fundCode":null,"kapTitle":"ASELSAN ELEKTRONİK SANAYİ VE TİCARET A.Ş.","isOldKap":false,"disclosureClass":"FR","disclosureType":"FR","disclosureCategory":"ODA","summary":"24 Şubat 2026 AS011-2026-0001","subject":"Sorumluluk Beyanı (Konsolide)","relatedStocks":null,"year":2025,"ruleType":"Yıllık","period":4,"disclosureIndex":1561040,"isLate":false,"stockCodes":"ASELS","hasMultiLanguageSupport":false,"attachmentCount":0,"modifyStatus":null},{"publishDate":"24.02.2026 18:27:36","fundCode":null,"kapTitle":"ASELSAN ELEKTRONİK SANAYİ VE TİCARET A.Ş.","isOldKap":false,"disclosureClass":"FR","disclosureType":"FR","disclosureCategory":"FR","summary":null,"subject":"Finansal Rapor","relatedStocks":null,"year":2025,"ruleType":"Yıllık","period":4,"disclosureIndex":1561039,"isLate":false,"stockCodes":"ASELS","hasMultiLanguageSupport":true,"attachmentCount":2,"modifyStatus":null},{"publishDate":"24.02.2026 18:27:13","fundCode":null,"kapTitle":"ASELSAN ELEKTRONİK SANAYİ VE TİCARET A.Ş.","isOldKap":false,"disclosureClass":"FR","disclosureType":"FR","disclosureCategory":"ODA","summary":"24 Şubat 2026 1293 Sayılı Yönetim Kurulu Kararı","subject":"Faaliyet Raporu (Konsolide)","relatedStocks":null,"year":2025,"ruleType":"Yıllık","period":4,"disclosureIndex":1561038,"isLate":false,"stockCodes":"ASELS","hasMultiLanguageSupport":true,"attachmentCount":2,"modifyStatus":null}]""";

    // The OQ-1 market-wide probe answer (TKT-foundation-084's gate evidence).
    private const string KapPublicMarketProbeJson = """[{"publishDate":"24.10.2026 17:08:16","fundCode":null,"kapTitle":"RODRİGO TEKSTİL SANAYİ VE TİCARET A.Ş.","isOldKap":false,"disclosureClass":"FR","disclosureType":"FR","disclosureCategory":"ODA","summary":"Sorumluluk Beyanı","subject":"Sorumluluk Beyanı (Konsolide)","relatedStocks":null,"year":2026,"ruleType":"9 Aylık","period":3,"disclosureIndex":1687047,"isLate":false,"stockCodes":"RODRG","hasMultiLanguageSupport":false,"attachmentCount":0,"modifyStatus":null},{"publishDate":"24.10.2026 17:07:30","fundCode":null,"kapTitle":"RODRİGO TEKSTİL SANAYİ VE TİCARET A.Ş.","isOldKap":false,"disclosureClass":"FR","disclosureType":"FR","disclosureCategory":"ODA","summary":"30 09 2026 tarihli konsolide faaliyet raporu","subject":"Faaliyet Raporu (Konsolide)","relatedStocks":null,"year":2026,"ruleType":"9 Aylık","period":3,"disclosureIndex":1687046,"isLate":false,"stockCodes":"RODRG","hasMultiLanguageSupport":false,"attachmentCount":1,"modifyStatus":null},{"publishDate":"24.10.2026 17:06:14","fundCode":null,"kapTitle":"RODRİGO TEKSTİL SANAYİ VE TİCARET A.Ş.","isOldKap":false,"disclosureClass":"FR","disclosureType":"FR","disclosureCategory":"FR","summary":null,"subject":"Finansal Rapor","relatedStocks":null,"year":2026,"ruleType":"9 Aylık","period":3,"disclosureIndex":1687045,"isLate":false,"stockCodes":"RODRG","hasMultiLanguageSupport":true,"attachmentCount":1,"modifyStatus":null},{"publishDate":"24.10.2026 15:17:14","fundCode":null,"kapTitle":"KAP TEST A.Ş.","isOldKap":false,"disclosureClass":"DKB","disclosureType":"DUY","disclosureCategory":"ODA","summary":"Test bildirimi","subject":"Test Bildirimi","relatedStocks":null,"year":null,"ruleType":"-","period":null,"disclosureIndex":1687044,"isLate":false,"stockCodes":"KTEST","hasMultiLanguageSupport":false,"attachmentCount":0,"modifyStatus":null},{"publishDate":"24.10.2026 15:17:05","fundCode":null,"kapTitle":"BORSA İSTANBUL BISTECH DEVRE KESİCİ UYGULAMASI","isOldKap":false,"disclosureClass":"DKB","disclosureType":"DUY","disclosureCategory":null,"summary":"mkkLB-2 Test bildirimi","subject":"Pay Bazında Devre Kesici Bildirimi","relatedStocks":null,"year":null,"ruleType":"-","period":null,"disclosureIndex":1687043,"isLate":false,"stockCodes":null,"hasMultiLanguageSupport":true,"attachmentCount":0,"modifyStatus":null},{"publishDate":"24.10.2026 15:16:53","fundCode":null,"kapTitle":"BORSA İSTANBUL BISTECH DEVRE KESİCİ UYGULAMASI","isOldKap":false,"disclosureClass":"DKB","disclosureType":"DUY","disclosureCategory":null,"summary":"mkkLB-1 Test bildirimi","subject":"Pay Bazında Devre Kesici Bildirimi","relatedStocks":null,"year":null,"ruleType":"-","period":null,"disclosureIndex":1687042,"isLate":false,"stockCodes":null,"hasMultiLanguageSupport":true,"attachmentCount":0,"modifyStatus":null},{"publishDate":"24.10.2026 10:24:38","fundCode":null,"kapTitle":"GEDİZ AMBALAJ SANAYİ VE TİCARET A.Ş.","isOldKap":false,"disclosureClass":"DG","disclosureType":"DG","disclosureCategory":"ODA","summary":"Kurumsal Yönetim Uyum Raporu -Güncelleme","subject":"Kurumsal Yönetim Uyum Raporu (Güncelleme)","relatedStocks":null,"year":null,"ruleType":"-","period":null,"disclosureIndex":1687041,"isLate":false,"stockCodes":"GEDZA","hasMultiLanguageSupport":false,"attachmentCount":0,"modifyStatus":null},{"publishDate":"24.10.2026 09:57:34","fundCode":null,"kapTitle":"GEDİZ AMBALAJ SANAYİ VE TİCARET A.Ş.","isOldKap":false,"disclosureClass":"DG","disclosureType":"DG","disclosureCategory":"ODA","summary":"KURUMSAL YÖNETİM BİLGİ FORMU - GÜNCELLEME","subject":"Kurumsal Yönetim Bilgi Formu (Güncelleme) - Pay Sahipleri","relatedStocks":null,"year":null,"ruleType":"-","period":null,"disclosureIndex":1687040,"isLate":false,"stockCodes":"GEDZA","hasMultiLanguageSupport":false,"attachmentCount":0,"modifyStatus":null},{"publishDate":"24.10.2026 09:48:11","fundCode":null,"kapTitle":"GEDİZ AMBALAJ SANAYİ VE TİCARET A.Ş.","isOldKap":false,"disclosureClass":"ODA","disclosureType":"ODA","disclosureCategory":"ODA","summary":"Yeni oluşturulan Yönetim Kurulu Komiteleri hk","subject":"Yönetim Kurulu Komiteleri","relatedStocks":null,"year":null,"ruleType":"-","period":null,"disclosureIndex":1687039,"isLate":false,"stockCodes":"GEDZA","hasMultiLanguageSupport":false,"attachmentCount":0,"modifyStatus":null},{"publishDate":"24.10.2026 09:40:19","fundCode":null,"kapTitle":"GEDİZ AMBALAJ SANAYİ VE TİCARET A.Ş.","isOldKap":false,"disclosureClass":"ODA","disclosureType":"ODA","disclosureCategory":"ODA","summary":"Yönetim Kurulu Bağımsız Üyesinin Bağımsızlık Beyanı hk.","subject":"Özel Durum Açıklaması (Genel)","relatedStocks":null,"year":null,"ruleType":"-","period":null,"disclosureIndex":1687038,"isLate":false,"stockCodes":"GEDZA","hasMultiLanguageSupport":true,"attachmentCount":1,"modifyStatus":null},{"publishDate":"24.10.2026 09:05:24","fundCode":null,"kapTitle":"TUREKS TURUNÇ MADENCİLİK İÇ VE DIŞ TİCARET A.Ş.","isOldKap":false,"disclosureClass":"ODA","disclosureType":"CA","disclosureCategory":"STT","summary":"23.10.2026 Tarihli Pay Geri Alım İşlemleri","subject":"Payların Geri Alınmasına İlişkin Bildirim","relatedStocks":null,"year":null,"ruleType":"-","period":null,"disclosureIndex":1687037,"isLate":false,"stockCodes":"MARBL","hasMultiLanguageSupport":true,"attachmentCount":0,"modifyStatus":null},{"publishDate":"24.10.2026 09:04:52","fundCode":null,"kapTitle":"KAP TEST A.Ş.","isOldKap":false,"disclosureClass":"DKB","disclosureType":"DUY","disclosureCategory":"ODA","summary":"Test bildirimi","subject":"Test Bildirimi","relatedStocks":null,"year":null,"ruleType":"-","period":null,"disclosureIndex":1687036,"isLate":false,"stockCodes":"KTEST","hasMultiLanguageSupport":false,"attachmentCount":0,"modifyStatus":null}]""";

    // The identity-resolution answer (TKT-foundation-080's double).
    private const string KapPublicIdentityJson = """[{"category":"combined","results":[]},{"category":"subjects","results":[]},{"category":"companyOrFunds","results":[{"searchValue":"ASELSAN ELEKTRONİK SANAYİ VE TİCARET A.Ş.","searchType":"C","actionKey":"companyFundAction","memberOrFundOid":"4028e4a1413b7ef401413bc2251e0047","subjectOid":null,"marketOid":null,"discType":null,"year":-1,"period":-1,"cmpOrFundCode":"asels","indexList":null}]}]""";

    // The bounded-trimmed excel-HTML report (TKT-foundation-081's decode input).
    private const string KapPublicReportHtml = """"
<html xmlns:v="urn:schemas-microsoft-com:vml" xmlns:o="urn:schemas-microsoft-com:office:office" xmlns:x="urn:schemas-microsoft-com:office:excel" xmlns="http://www.w3.org/TR/REC-html40">
 <head>
  <meta http-equiv="Content-Type" content="text/html;" charset="utf-8">
  <meta name="ProgId" content="Excel.Sheet">
  <meta name="Generator" content="Microsoft Excel 11">
  <title>Document and Custom Property </title><!--[if gte mso 9]><xml><o:CustomDocumentProperties><o:BUSINESSGROUP dt:dt="string">CHANNELA</o:BUSINESSGROUP><o:BUSINESSGROUPID dt:dt="string">2</o:BUSINESSGROUPID></o:CustomDocumentProperties></xml><![endif]-->
  <style> body { text-align: left; } </style>
 </head>
 <body>
  <h1 style="font-size: medium">ASELSAN ELEKTRONİK SANAYİ VE TİCARET A.Ş.</h1>
  <h1 style="font-size: medium">Finansal Rapor</h1>
  <div>
   Gönderim Tarihi:04.08.2026 18:39:41
  </div>
  <div>
   Bildirim Tipi:FR
  </div>
  <div>
   Yıl:2026
  </div>
  <div>
   Periyot:2
  </div>
  <div>
   <table class="financial-header-table" border="1" style="font-size:10px">
    <tbody>
     <tr>
      <td class="financial-header-title">Sunum Para Birimi</td>
      <td>1.000 TL</td>
     </tr>
     <tr>
      <td class="financial-header-title">Finansal Tablo Niteliği</td>
      <td>Konsolide</td>
     </tr>
    </tbody>
   </table>
   <table class="financial-table tbl_general_role_210015" border="1" style="font-size:10px">
    <tbody>
     <tr>
      <td colspan="3" rowspan="1">
       <div class="gwt-Label"></div></td>
      <td align="center" class="context-header" column-id="5">
       <div>
        <div class="gwt-HTML multi-language-content content-tr" style="display: block;">
         Cari Dönem
         <br>
         30.06.2026
        </div>
       </div></td>
      <td align="center" class="context-header" column-id="6">
       <div>
        <div class="gwt-HTML multi-language-content content-tr" style="display: block;">
         Önceki Dönem
         <br>
         31.12.2025
        </div>
       </div></td>
     </tr><tr class="general_role_210015-row-1 abstract-row">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel taxonomy-abstract-title standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Finansal Durum Tablosu (Bilanço)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
     </tr><tr class="general_role_210015-row-2 abstract-row">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel taxonomy-abstract-title standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Varlıklar
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
     </tr><tr class="general_role_210015-row-3 abstract-row">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel taxonomy-abstract-title standartCapitalsLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            DÖNEN VARLIKLAR
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
     </tr><tr class="general_role_210015-row-4 data-input-row alternate-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Nakit ve Nakit Benzerleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value">
         3
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="39468926">
         39.468.926
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="34251653">
         34.251.653
        </div>
       </div></td>
     </tr><tr class="general_role_210015-row-5 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Gayrimenkul Projeleri Kapsamında Açılan Nakit Hesapları
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr><tr class="general_role_210015-row-6 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Finansal Yatırımlar
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr><tr class="general_role_210015-row-7 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Kullanımı Kısıtlı Banka Bakiyeleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr><tr class="general_role_210015-row-8 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Vadeli Mevduatlar
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr><tr class="general_role_210015-row-9 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Satılmaya Hazır Finansal Varlıklar
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr><tr class="general_role_210015-row-10 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Vadeye Kadar Elde Tutulacak Finansal Yatırımlar
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr><tr class="general_role_210015-row-11 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Gerçeğe Uygun Değer Farkı Kar/Zarara Yansıtılan Finansal Varlıklar
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr><tr class="general_role_210015-row-12 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Alım Satım Amaçlı Elde Tutulan Finansal Varlıklar
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr><tr class="general_role_210015-row-13 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Gerçeğe Uygun Değer Farkı Kar/Zarara Yansıtılarak Ölçülen Diğer Finansal Varlıklar
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr><tr class="general_role_210015-row-14 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Gerçeğe Uygun Değer Farkı Kar/Zarara Yansıtılan Olarak Sınıflandırılan Finansal Varlıklar
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr><tr class="general_role_210015-row-15 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Yatırım Riski Hayat Poliçesi Sahiplerine Ait Finansal Yatırımlar
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr><tr class="general_role_210015-row-16 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Gerçeğe Uygun Değer Farkı Diğer Kapsamlı Gelire Yansıtılan Finansal Varlıklar
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr><tr class="general_role_210015-row-17 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Gerçeğe Uygun Değer Farkı Diğer Kapsamlı Gelire Yansıtılarak Ölçülen Finansal Varlıklar
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr><tr class="general_role_210015-row-18 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Özkaynağa Dayalı Finansal Araçlara Yapılan Yatırımlar
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr><tr class="general_role_210015-row-19 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            İtfa Edilmiş Maliyetiyle Ölçülen Finansal Varlıklar
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr><tr class="general_role_210015-row-20 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Diğer Finansal Yatırımlar
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr><tr class="general_role_210015-row-21 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Teminata Verilen Finansal Varlıklar
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr><tr class="general_role_210015-row-22 data-input-row alternate-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Ticari Alacaklar
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value">
         5
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default" title="47167326">
         47.167.326
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default" title="45760320">
         45.760.320
        </div>
       </div></td>
     </tr><tr class="general_role_210015-row-23 data-input-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            İlişkili Taraflardan Ticari Alacaklar
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value">
         4
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default" title="21831106">
         21.831.106
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default" title="37763402">
         37.763.402
        </div>
       </div></td>
     </tr><tr class="general_role_210015-row-24 data-input-row alternate-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            İlişkili Olmayan Taraflardan Ticari Alacaklar
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default" title="25336220">
         25.336.220
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default" title="7996918">
         7.996.918
        </div>
       </div></td>
     </tr><tr class="general_role_210015-row-25 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Finans Sektörü Faaliyetlerinden Alacaklar
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
   </table><table class="financial-table tbl_general_role_310003" border="1" style="font-size:10px">
    <tbody>
     <tr>
      <td colspan="3" rowspan="1">
       <div class="gwt-Label"></div></td>
      <td align="center" class="context-header">
       <div>
        <div class="gwt-HTML multi-language-content content-tr" style="display: block;">
         Cari Dönem
         <br>
         01.01.2026 - 30.06.2026
        </div>
       </div></td>
      <td align="center" class="context-header">
       <div>
        <div class="gwt-HTML multi-language-content content-tr" style="display: block;">
         Önceki Dönem
         <br>
         01.01.2025 - 30.06.2025
        </div>
       </div></td>
      <td align="center" class="context-header">
       <div>
        <div class="gwt-HTML multi-language-content content-tr" style="display: block;">
         Cari Dönem 3 Aylık
         <br>
         01.04.2026 - 30.06.2026
        </div>
       </div></td>
      <td align="center" class="context-header">
       <div>
        <div class="gwt-HTML multi-language-content content-tr" style="display: block;">
         Önceki Dönem 3 Aylık
         <br>
         01.04.2025 - 30.06.2025
        </div>
       </div></td>
     </tr><tr class="general_role_310003-row-1 abstract-row">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel taxonomy-abstract-title standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Kar veya Zarar ve Diğer Kapsamlı Gelir Tablosu
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
     </tr><tr class="general_role_310003-row-2 abstract-row">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel taxonomy-abstract-title verboseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            KAR VEYA ZARAR KISMI
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
     </tr><tr class="general_role_310003-row-3 data-input-row alternate-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Hasılat
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value">
         14
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="88494252">
         88.494.252
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="70956004">
         70.956.004
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-6">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="51782243">
         51.782.243
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-7">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="39038714">
         39.038.714
        </div>
       </div></td>
     </tr><tr class="general_role_310003-row-4 data-input-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Satışların Maliyeti
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value">
         14
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default" title="-60397561">
         -60.397.561
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default" title="-48249151">
         -48.249.151
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-6">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default" title="-34961861">
         -34.961.861
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-7">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default" title="-25884838">
         -25.884.838
        </div>
       </div></td>
     </tr><tr class="general_role_310003-row-5 data-input-row alternate-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standartCapitalsLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            TİCARİ FAALİYETLERDEN BRÜT KAR (ZARAR)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="28096691">
         28.096.691
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="22706853">
         22.706.853
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-6">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="16820382">
         16.820.382
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-7">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="13153876">
         13.153.876
        </div>
       </div></td>
     </tr><tr class="general_role_310003-row-6 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Finans Sektörü Faaliyetleri Hasılatı
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-6">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-7">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr><tr class="general_role_310003-row-7 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Ücret, Prim, Komisyon ve Diğer Hizmet Gelirleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-6">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-7">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr><tr class="general_role_310003-row-8 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Kambiyo Gelirleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-6">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-7">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr><tr class="general_role_310003-row-9 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Faiz Gelirleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-6">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-7">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr><tr class="general_role_310003-row-10 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Kar Payı Gelirleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-6">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-7">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr><tr class="general_role_310003-row-11 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Türev Finansal İşlemlerden Kazanç
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-6">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-7">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr><tr class="general_role_310003-row-12 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Portföy İşletmeciliği Faaliyetinden Elde Edilen Gelir
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-6">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-7">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr><tr class="general_role_310003-row-13 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Sigortacılık Hizmetlerinden Elde Edilen Gelir
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-6">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-7">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr><tr class="general_role_310003-row-14 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Emeklilik Hizmetlerinden Elde Edilen Gelir
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-6">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-7">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr><tr class="general_role_310003-row-15 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Finansal Varlık ve Yükümlülüklere İlişkin Gerçekleşmemiş Karlar
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-6">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-7">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr><tr class="general_role_310003-row-16 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Finans Sektörü Faaliyetlerinden Diğer Gelirler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-6">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-7">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr><tr class="general_role_310003-row-17 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Finans Sektörü Faaliyetleri Maliyeti
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-6">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-7">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
     </tr><tr class="general_role_310003-row-18 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Ücret, Prim, Komisyon ve Diğer Hizmet Giderleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-6">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-7">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
     </tr><tr class="general_role_310003-row-19 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedTerseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Kambiyo Giderleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-6">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-7">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
     </tr><tr class="general_role_310003-row-20 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Faiz Giderleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-6">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-7">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
     </tr>
   </table><table class="financial-table tbl_general_role_520003" border="1" style="font-size:10px">
    <tbody>
     <tr>
      <td colspan="3" rowspan="1">
       <div class="gwt-Label"></div></td>
      <td align="center" class="context-header" column-id="5">
       <div>
        <div class="gwt-HTML multi-language-content content-tr" style="display: block;">
         Cari Dönem
         <br>
         01.01.2026 - 30.06.2026
        </div>
       </div></td>
      <td align="center" class="context-header" column-id="6">
       <div>
        <div class="gwt-HTML multi-language-content content-tr" style="display: block;">
         Önceki Dönem
         <br>
         01.01.2025 - 30.06.2025
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-1 abstract-row">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel taxonomy-abstract-title standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Nakit Akış Tablosu (Dolaylı Yöntem)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
     </tr>
     <tr class="general_role_520003-row-2 data-input-row alternate-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel alternativeTotalLabel containsTotalLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            İŞLETME FAALİYETLERİNDEN NAKİT AKIŞLARI
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field alternativeTotalLabel containsTotalLabel monetary-field-default" title="15206440">
         15.206.440
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field alternativeTotalLabel containsTotalLabel monetary-field-default" title="18013334">
         18.013.334
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-3 data-input-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Dönem Karı (Zararı)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default" title="14449834">
         14.449.834
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default" title="8468992">
         8.468.992
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-4 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Sürdürülen Faaliyetlerden Dönem Karı (Zararı)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-5 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Durdurulan Faaliyetlerden Dönem Karı (Zararı)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-6 data-input-row alternate-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel alternativeTotalLabel containsTotalLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Dönem Net Karı (Zararı) Mutabakatı İle İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field alternativeTotalLabel containsTotalLabel monetary-field-default" title="11924401">
         11.924.401
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field alternativeTotalLabel containsTotalLabel monetary-field-default" title="3066846">
         3.066.846
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-7 data-input-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Amortisman ve İtfa Gideri İle İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value">
         8
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="4338537">
         4.338.537
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="2967002">
         2.967.002
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-8 data-input-row alternate-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Değer Düşüklüğü (İptali) İle İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default" title="-218085">
         -218.085
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default" title="175657">
         175.657
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-9 data-input-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Alacaklarda Değer Düşüklüğü (İptali) ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value">
         5
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default" title="-5720">
         -5.720
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default" title="-1780">
         -1.780
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-10 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Diğer Finansal Varlıklar veya Yatırımlar Değer Düşüklüğü (İptali) ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-11 data-input-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Stok Değer Düşüklüğü (İptali) ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value">
         6
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default" title="-212365">
         -212.365
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default" title="177437">
         177.437
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-12 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Canlı Varlıklar Değer Düşüklüğü (İptali) ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-13 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Maddi Duran Varlık Değer Düşüklüğü (İptali) ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-14 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Şerefiye Değer Düşüklüğü ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-15 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Diğer Maddi Olmayan Duran Varlık Değer Düşüklüğü (İptali) ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-16 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Yatırım Amaçlı Gayrimenkul Değer Düşüklüğü (İptali) ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-17 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Satış Amaçlı Duran Varlıklar Değer Düşüklüğü (İptali) ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-18 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Diğer Değer Düşüklükleri (İptalleri) ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-19 data-input-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Karşılıklar İle İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="104807">
         104.807
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="-1312990">
         -1.312.990
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-20 data-input-row alternate-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Çalışanlara Sağlanan Faydalara İlişkin Karşılıklar (İptali) ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value">
         12
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="-2643296">
         -2.643.296
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="-1159621">
         -1.159.621
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-21 data-input-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Dava ve/veya Ceza Karşılıkları (İptali) ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="-1007727">
         -1.007.727
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="-2457057">
         -2.457.057
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-22 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Yeniden Yapılanma Karşılıkları (İptali) ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-23 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Genel Karşılıklar (İptali) ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-24 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Muhtemel Riskler için Ayrılan Serbest Karşılıklar (İptali) ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-25 data-input-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Garanti Karşılıkları (İptali) ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="3773256">
         3.773.256
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="2760530">
         2.760.530
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-26 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Sektörel Gereksinimler Çerçevesinde Ayrılan Karşılıklar (İptali) ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-27 data-input-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Diğer Karşılıklar (İptalleri) ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="-17426">
         -17.426
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="-456842">
         -456.842
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-28 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Kar Payı (Geliri) Gideri ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-29 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Katılım (Kar) Payı ve Diğer Finansal Araçlardan (Gelirler) Giderler ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-30 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Pazarlıklı Satın Alım Sonucu Oluşan Kazanç ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-31 data-input-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Faiz (Gelirleri) ve Giderleri İle İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default" title="-771804">
         -771.804
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default" title="-834713">
         -834.713
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-32 data-input-row alternate-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Faiz Gelirleri ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default" title="-2088953">
         -2.088.953
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default" title="-1727951">
         -1.727.951
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-33 data-input-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Faiz Giderleri ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="1317149">
         1.317.149
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="893238">
         893.238
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-34 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Vadeli Alımlardan Kaynaklanan Ertelenmiş Finansman Gideri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-35 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Vadeli Satışlardan Kaynaklanan Kazanılmamış Finansman Geliri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-36 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Devlet Teşviklerinden Elde Edilen Gelirler ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-37 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Takas İşlemlerinden Kaynaklanan Kayıplar (Kazançlar) ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-38 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Gerçekleşmemiş Yabancı Para Çevrim Farkları İle İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-39 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Pay Bazlı Ödemeler İle İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-40 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Hibe Krediler ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-41 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Gerçeğe Uygun Değer Kayıpları (Kazançları) İle İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-42 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            İhraç Edilmiş Finansal Araçların Gerçeğe Uygun Değer Kayıpları (Kazançları) ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-43 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Yatırım Amaçlı Gayrimenkullerin Gerçeğe Uygun Değer Kayıpları (Kazançları) ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-44 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Finansal Varlıkların Gerçeğe Uygun Değer Kayıpları (Kazançları) ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-45 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Türev Finansal Araçların Gerçeğe Uygun Değer Kayıpları (Kazançları) ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-46 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Canlı Varlıklar veya Tarımsal Ürünlerin Gerçeğe Uygun Değer Kayıpları (Kazançları) ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-47 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Gerçeğe Uygun Değer Kayıpları (Kazançları) ile İlgili Diğer Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-48 data-input-row alternate-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Özkaynak Yöntemiyle Değerlenen Yatırımların Dağıtılmamış Karları ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default" title="248997">
         248.997
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default" title="274237">
         274.237
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-49 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            İştiraklerin Dağıtılmamış Karları İle İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-50 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            İş Ortaklıklarının Dağıtılmamış Karları ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-51 data-input-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Vergi (Geliri) Gideri İle İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default" title="-1799510">
         -1.799.510
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default" title="-11620227">
         -11.620.227
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-52 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Nakit Dışı Kalemlere İlişkin Diğer Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-53 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Duran Varlıkların Elden Çıkarılmasından Kaynaklanan Kayıplar (Kazançlar) İle İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-54 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Maddi Duran Varlıkların Elden Çıkarılmasından Kaynaklanan Kayıplar (Kazançlar) ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-55 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Maddi Olmayan Duran Varlıkların Elden Çıkarılmasından Kaynaklanan Kayıplar (Kazançlar) ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-56 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Yatırım Amaçlı Gayrimenkullerin Elden Çıkarılmasından Kaynaklanan Kayıplar (Kazançlar) ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-57 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Canlı Varlıkların Elden Çıkarılmasından Kaynaklanan Kayıplar (Kazançlar) ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-58 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Diğer Duran Varlıkların Elden Çıkarılmasından Kaynaklanan Kayıplar (Kazançlar) ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-59 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Satış Amaçlı veya Ortaklara Dağıtılmak Üzere Elde Tutulan Duran Varlıkların Elden Çıkarılmasından Kaynaklanan Kayıplar (Kazançlar) ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-60 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            İştirak, İş ortaklığı ve Finansal Yatırımların Elden Çıkarılmasından veya Paylarındaki Değişim Sebebi ile Oluşan Kayıplar (Kazançlar) ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-61 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Bağlı Ortaklıkların veya Müşterek Faaliyetlerin Elden Çıkarılmasından Kaynaklanan Kayıplar (Kazançlar) ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-62 data-input-row alternate-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Yatırım ya da Finansman Faaliyetlerinden Kaynaklanan Nakit Akışlarına Neden Olan Diğer Kalemlere İlişkin Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="2396383">
         2.396.383
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="8100117">
         8.100.117
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-63 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Parasal Pozisyon Kazanç ve Kayıplarıyla İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-64 data-input-row alternate-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Kar (Zarar) Mutabakatı İle İlgili Diğer Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="7625076">
         7.625.076
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="5317763">
         5.317.763
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-65 data-input-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedTotalLabel containsTotalLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            İşletme Sermayesinde Gerçekleşen Değişimler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTotalLabel containsTotalLabel monetary-field-default" title="-5991513">
         -5.991.513
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTotalLabel containsTotalLabel monetary-field-default" title="9525588">
         9.525.588
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-66 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Finansal Yatırımlardaki Azalış (Artış)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-67 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Türkiye Cumhuriyet Merkez Bankası Hesabındaki Azalış (Artış)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-68 data-input-row alternate-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Ticari Alacaklardaki Azalış (Artış) ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="20882800">
         20.882.800
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="17530522">
         17.530.522
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-69 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            İlişkili Taraflardan Ticari Alacaklardaki Azalış (Artış)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-70 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            İlişkili Olmayan Taraflardan Ticari Alacaklardaki Azalış (Artış)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-71 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Finans Sektörü Faaliyetlerinden Alacaklarda Azalış (Artış)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-72 data-input-row alternate-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Faaliyetlerle İlgili Diğer Alacaklardaki Azalış (Artış) ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="-4120273">
         -4.120.273
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="47122">
         47.122
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-73 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            İlişkili Taraflardan Faaliyetlerle İlgili Diğer Alacaklardaki Azalış (Artış)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-74 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            İlişkili Olmayan Taraflardan Faaliyetlerle İlgili Diğer Alacaklardaki Azalış (Artış)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-75 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Müşteri Sözleşmelerinden Doğan Varlıklardaki Azalış (Artış) İle İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-76 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Devam Eden İnşaat ve Taahhüt İşlerinden Doğan Sözleşme Varlıklarındaki Azalış (Artış)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-77 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Mal Ve Hizmet Satışlarından Doğan Sözleşme Varlıklarındaki Azalış (Artış)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-78 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Diğer Sözleşme Varlıklarındaki Azalış (Artış)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-79 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            İmtiyaz Sözleşmelerine İlişkin Finansal Varlıklardaki Azalış (Artış)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-80 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Türev Varlıklardaki Azalış (Artış)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-81 data-input-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Stoklardaki Azalışlar (Artışlar) İle İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value">
         6
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="-18995613">
         -18.995.613
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="-2772166">
         -2.772.166
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-82 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Canlı Varlıklardaki Azalış (Artış)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-83 data-input-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Peşin Ödenmiş Giderlerdeki Azalış (Artış)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value">
         7
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="-10475951">
         -10.475.951
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="-6075035">
         -6.075.035
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-84 data-input-row alternate-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Ticari Borçlardaki Artış (Azalış) ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value">
         5
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="-5961773">
         -5.961.773
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="-5148080">
         -5.148.080
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-85 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            İlişkili Taraflara Ticari Borçlardaki Artış (Azalış)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-86 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            İlişkili Olmayan Taraflara Ticari Borçlardaki Artış (Azalış)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-87 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Finans Sektörü Faaliyetlerinden Borçlardaki Artış (Azalış)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-88 data-input-row alternate-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Çalışanlara Sağlanan Faydalar Kapsamında Borçlardaki Artış (Azalış)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value">
         12
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default" title="3830997">
         3.830.997
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default" title="345471">
         345.471
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-89 data-input-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Müşteri Sözleşmelerinden Doğan Yükümlülüklerdeki Artış (Azalış) İle İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="646599">
         646.599
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="1176028">
         1.176.028
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-90 data-input-row alternate-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Devam Eden İnşaat Ve Taahhüt İşlerinden Doğan Sözleşme Yükümlülüklerindeki Artış (Azalış)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default" title="646599">
         646.599
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default" title="1176028">
         1.176.028
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-91 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Mal Ve Hizmet Satışlarından Doğan Sözleşme Yükümlülüklerindeki Artış (Azalış)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-92 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Diğer Sözleşme Yükümlülüklerindeki Artış (Azalış)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-93 data-input-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Faaliyetler ile İlgili Diğer Borçlardaki Artış (Azalış) ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="66615">
         66.615
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="-548282">
         -548.282
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-94 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            İlişkili Taraflara Faaliyetlerle İlgili Diğer Borçlardaki Artış (Azalış)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-95 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            İlişkili Olmayan Taraflara Faaliyetlerle İlgili Diğer Borçlardaki Artış (Azalış)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-96 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Türev Yükümlülüklerdeki Artış (Azalış)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-97 data-input-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Devlet Teşvik ve Yardımlarındaki Artış (Azalış)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="13389">
         13.389
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="-14546">
         -14.546
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-98 data-input-row alternate-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel verboseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Ertelenmiş Gelirlerdeki (Müşteri Sözleşmelerinden Doğan Yükümlülüklerin Dışında Kalanlar) Artış (Azalış)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field verboseLabel monetary-field-default" title="14805786">
         14.805.786
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field verboseLabel monetary-field-default" title="12061849">
         12.061.849
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-99 data-input-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            İşletme Sermayesinde Gerçekleşen Diğer Artış (Azalış) ile İlgili Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="-6684089">
         -6.684.089
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="-7077295">
         -7.077.295
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-100 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Faaliyetlerle İlgili Diğer Varlıklardaki Azalış (Artış)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-101 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Faaliyetlerle İlgili Diğer Yükümlülüklerdeki Artış (Azalış)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-102 data-input-row alternate-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel alternativeTotalLabel containsTotalLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Faaliyetlerden Elde Edilen Nakit Akışları
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field alternativeTotalLabel containsTotalLabel monetary-field-default" title="20382722">
         20.382.722
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field alternativeTotalLabel containsTotalLabel monetary-field-default" title="21061426">
         21.061.426
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-103 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedTerseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Ödenen Temettüler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-104 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Alınan Temettüler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-105 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedTerseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Ödenen Faiz
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-106 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Alınan Faiz
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-107 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedTerseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Katılım (Kar) Payı ve Diğer Finansal Araçlardan Nakit Çıkışları
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-108 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Katılım (Kar) Payı ve Diğer Finansal Araçlardan Nakit Girişleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-109 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedTerseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Kira Ödemeleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-110 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Alınan Kira
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-111 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedTerseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Başka İşletmelerin veya Fonların Paylarının veya Borçlanma Araçlarının Edinimi İçin Yapılan Nakit Çıkışları
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-112 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Başka İşletmelerin veya Fonların Paylarının veya Borçlanma Araçlarının Satılması Sonucu Elde Edilen Nakit Girişleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-113 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedTerseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            İştirakler, İş Ortaklıkları ve/veya Müşterek Faaliyetlerin Sermaye Artırımına Katılımdan Kaynaklanan Nakit Çıkışları
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-114 data-input-row alternate-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Çalışanlara Sağlanan Faydalara İlişkin Karşılıklar Kapsamında Yapılan Ödemeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value">
         12
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default" title="-346102">
         -346.102
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default" title="-279037">
         -279.037
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-115 data-input-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Diğer Karşılıklara İlişkin Ödemeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default" title="-2645637">
         -2.645.637
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default" title="-2620773">
         -2.620.773
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-116 data-input-row alternate-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedTerseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Vergi İadeleri (Ödemeleri)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default" title="-2184543">
         -2.184.543
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default" title="-148282">
         -148.282
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-117 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Diğer Nakit Girişleri (Çıkışları)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-118 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel netLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Durdurulan Faaliyetlere İlişkin Net Nakit Akışları
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field netLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field netLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-119 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            İşletme Faaliyetlerine İlişkin Enflasyon Etkisi
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-120 data-input-row alternate-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel alternativeTotalLabel containsTotalLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            YATIRIM FAALİYETLERİNDEN KAYNAKLANAN NAKİT AKIŞLARI
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field alternativeTotalLabel containsTotalLabel monetary-field-default" title="-33537708">
         -33.537.708
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field alternativeTotalLabel containsTotalLabel monetary-field-default" title="-16934728">
         -16.934.728
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-121 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel verboseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Bağlı Ortaklıkların Kontrolünün Kaybı Sonucunu Doğuracak Satışlara İlişkin Nakit Girişleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field verboseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field verboseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-122 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Bağlı Ortaklıkların Kontrolünün Kaybı Sonucunu Doğurmayan Satışlara İlişkin Nakit Girişleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-123 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Bağlı Ortaklıkların Kontrolünün Elde Edilmesine Yönelik Alışlara İlişkin Nakit Çıkışları
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-124 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Bağlı Ortaklıklarda İlave Pay Alımlarına ilişkin Nakit Çıkışları
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-125 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            İştiraklerin ve/veya İş Ortaklıklarının Pay Satışı veya Sermaye Azaltımı Sebebiyle Oluşan Nakit Girişleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-126 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedTerseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            İştiraklar ve/veya İş Ortaklıkları Pay Alımı veya Sermaye Artırımı Sebebiyle Oluşan Nakit Çıkışları
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-127 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedTerseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            İştirakler ve/veya İş Ortaklıkları Sermaye Avansı Ödemelerinden Nakit Çıkışları
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-128 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel verboseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Başka İşletmelerin veya Fonların Paylarının veya Borçlanma Araçlarının Satılması Sonucu Elde Edilen Nakit Girişleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field verboseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field verboseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-129 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedVerboseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Başka İşletmelerin veya Fonların Paylarının veya Borçlanma Araçlarının Edinimi İçin Yapılan Nakit Çıkışları
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedVerboseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedVerboseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-130 data-input-row alternate-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Maddi ve Maddi Olmayan Duran Varlıkların Satışından Kaynaklanan Nakit Girişleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default" title="740908">
         740.908
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default" title="202352">
         202.352
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-131 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Maddi Duran Varlıkların Satışından Kaynaklanan Nakit Girişleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-132 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Maddi Olmayan Duran Varlıkların Satışından Kaynaklanan Nakit Girişleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-133 data-input-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedTerseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Maddi ve Maddi Olmayan Duran Varlıkların Alımından Kaynaklanan Nakit Çıkışları
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default" title="-33746150">
         -33.746.150
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default" title="-17190837">
         -17.190.837
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-134 data-input-row alternate-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedTerseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Maddi Duran Varlık Alımından Kaynaklanan Nakit Çıkışları
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value">
         8
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default" title="-14257326">
         -14.257.326
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default" title="-5513477">
         -5.513.477
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-135 data-input-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedTerseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Maddi Olmayan Duran Varlık Alımından Kaynaklanan Nakit Çıkışları
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value">
         8
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default" title="-19488824">
         -19.488.824
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default" title="-11677360">
         -11.677.360
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-136 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Yatırım Amaçlı Gayrimenkul Satımından Kaynaklanan Nakit Girişleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-137 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedTerseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Yatırım Amaçlı Gayrimenkul Alımından Kaynaklanan Nakit Çıkışları
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-138 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Satış Amacıyla Elde Tutulan Duran Varlık Satışlarından Kaynaklanan Nakit Girişleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-139 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedTerseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Satış Amacıyla Elde Tutulan Duran Varlık Alımlarından Nakit Çıkışları
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-140 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Canlı Varlık Satışından Kaynaklanan Nakit Girişleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-141 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedTerseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Canlı Varlık Alımından Kaynaklanan Nakit Çıkışları
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-142 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Diğer Uzun Vadeli Varlıkların Satışından Kaynaklanan Nakit Girişleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-143 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedTerseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Diğer Uzun Vadeli Varlık Alımından Kaynaklanan Nakit Çıkışları
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-144 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedTerseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Verilen Nakit Avans ve Borçlar
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-145 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedTerseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            İlişkili Taraflara Verilen Nakit Avans ve Borçlar
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-146 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedTerseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Verilen Diğer Nakit Avans ve Borçlar
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-147 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Verilen Nakit Avans ve Borçlardan Geri Ödemeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-148 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            İlişkili Taraflara Verilen Nakit Avans ve Borçlardan Geri Ödemeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-149 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Diğer Verilen Nakit Avans ve Borçlardan Geri Ödemeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-150 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Türev Araçlardan Nakit Girişleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-151 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedTerseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Türev Araçlardan Nakit Çıkışları
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-152 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Devlet Teşviklerinden Elde Edilen Nakit Girişleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-153 data-input-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Alınan Temettüler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default" title="122899">
         122.899
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default" title="87913">
         87.913
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-154 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedTerseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Ödenen Faiz
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-155 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Alınan Faiz
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-156 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedTerseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Katılım (Kar) Payı ve Diğer Finansal Araçlardan Nakit Çıkışları
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-157 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Katılım (Kar) Payı ve Diğer Finansal Araçlardan Nakit Girişleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-158 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedTerseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Vergi İadeleri (Ödemeleri)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-159 data-input-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Diğer Nakit Girişleri (Çıkışları)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default" title="-655365">
         -655.365
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default" title="-34156">
         -34.156
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-160 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel netLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Durdurulan Faaliyetlere İlişkin Net Nakit Akışları
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field netLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field netLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-161 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Yatırım Faaliyetlerine İlişkin Enflasyon Etkisi
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-162 data-input-row alternate-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel alternativeTotalLabel containsTotalLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            FİNANSMAN FAALİYETLERİNDEN NAKİT AKIŞLARI
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field alternativeTotalLabel containsTotalLabel monetary-field-default" title="27717459">
         27.717.459
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field alternativeTotalLabel containsTotalLabel monetary-field-default" title="-2546005">
         -2.546.005
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-163 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Bağlı Ortaklıklardaki Kontrolün Kaybına Yol Açmayan Şekilde Ortaklık Payları Değişmelerinden Kaynaklanan Nakit Girişleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-164 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Bağlı Ortaklıklardaki Kontrolün Kaybına Yol Açmayan Şekilde Ortaklık Payları Değişmelerinden Kaynaklanan Nakit Çıkışları
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-165 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Pay ve Diğer Özkaynağa Dayalı Araçların İhracından Kaynaklanan Nakit Girişleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-166 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Pay İhracından Kaynaklanan Nakit Girişleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-167 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Diğer Özkaynağa Dayalı Araçların İhracından Kaynaklanan Nakit Girişleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-168 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Sermaye Avanslarından Nakit Girişleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-169 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            İşletmenin Kendi Paylarını ve Diğer Özkaynağa Dayalı Araçlarını Almasıyla İlgili Nakit Çıkışları
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-170 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            İşletmenin Kendi Paylarını Almasından Kaynaklanan Nakit Çıkışları
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-171 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            İşletmenin Diğer Özkaynağa Dayalı Araçlarını Almasından Kaynaklanan Nakit Çıkışları
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-172 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Karşılıklı İştirak Paylarındaki Değişimden Kaynaklanan Nakit Çıkışları
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-173 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            İşletmenin Kendi Paylarını ve Diğer Özkaynağa Dayalı Araçlarını Satmasından Kaynaklanan Nakit Girişleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-174 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Geri Alınan Payların Satışından Kaynaklanan Nakit Girişleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-175 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            İşletmenin Aldığı Diğer Özkaynağa Dayalı Araçlarını Satmasından Kaynaklanan Nakit Girişleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-176 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Karşılıklı İştirak Paylarındaki Değişimden Kaynaklanan Nakit Girişleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-177 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Ortak Kontrole Tabi İşletmelerin Birleşme Etkisinden Kaynaklanan Nakit Girişleri (Çıkışları)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-178 data-input-row alternate-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Borçlanmadan Kaynaklanan Nakit Girişleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default" title="48593359">
         48.593.359
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default" title="9489087">
         9.489.087
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-179 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Kredilerden Nakit Girişleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-180 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            İhraç Edilen Borçlanma Araçlarından Nakit Girişleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-181 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Faktoring İşlemlerinden Nakit Girişleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-182 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Diğer Finansal Borçlanmalardan Nakit Girişleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-183 data-input-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedTerseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Borç Ödemelerine İlişkin Nakit Çıkışları
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default" title="-20875900">
         -20.875.900
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default" title="-12035092">
         -12.035.092
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-184 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedTerseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Kredi Geri Ödemelerine İlişkin Nakit Çıkışları
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-185 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedTerseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            İhraç Edilmiş Borçlanma Araçları Geri Ödemelerinden Nakit Çıkışları
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-186 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedTerseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Faktoring İşlemlerinden Nakit Çıkışları
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-187 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedTerseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Diğer Finansal Borç Ödemelerinden Nakit Çıkışları
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-188 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            İlişkili Taraflardan Alınan Diğer Borçlardaki Artış
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-189 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedTerseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            İlişkili Taraflardan Alınan Diğer Borçlardaki Azalış
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-190 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Kira Sözleşmelerinden Kaynaklanan Borç Ödemelerine İlişkin Nakit Çıkışları
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-191 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Türev Araçlardan Nakit Girişleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-192 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedTerseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Türev Araçlardan Nakit Çıkışları
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-193 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Devlet Teşviklerinden Elde Edilen Nakit Girişleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-194 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Ödenen Temettüler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-195 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedTerseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Ödenen Faiz
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-196 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Alınan Faiz
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-197 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedTerseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Katılım (Kar) Payı ve Diğer Finansal Araçlardan Nakit Çıkışları
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-198 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Katılım (Kar) Payı ve Diğer Finansal Araçlardan Nakit Girişleri
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-199 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel negatedTerseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Vergi İadeleri (Ödemeleri)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field negatedTerseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-200 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel terseLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Diğer Nakit Girişleri (Çıkışları)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field terseLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-201 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel netLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Durdurulan Faaliyetlere İlişkin Net Nakit Akışları
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field netLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field netLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-202 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Finansman Faaliyetlerine İlişkin Enflasyon Etkisi
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-203 data-input-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel capslockLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            ENFLASYON ETKİSİ
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field capslockLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field capslockLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-204 data-input-row alternate-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel alternativeNetlLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            YABANCI PARA ÇEVRİM FARKLARININ ETKİSİNDEN ÖNCE NAKİT VE NAKİT BENZERLERİNDEKİ NET ARTIŞ (AZALIŞ)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field alternativeNetlLabel monetary-field-default" title="9386191">
         9.386.191
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field alternativeNetlLabel monetary-field-default" title="-1467399">
         -1.467.399
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-205 data-input-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Yabancı Para Çevrim Farklarının Nakit ve Nakit Benzerleri Üzerindeki Etkisi
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="-4168918">
         -4.168.918
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standardLabel monetary-field-default" title="-3314402">
         -3.314.402
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-206 data-input-row alternate-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel netLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            NAKİT VE NAKİT BENZERLERİNDEKİ NET ARTIŞ (AZALIŞ)
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field netLabel monetary-field-default" title="5217273">
         5.217.273
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field netLabel monetary-field-default" title="-4781801">
         -4.781.801
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-207 data-input-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel periodStartLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            DÖNEM BAŞI NAKİT VE NAKİT BENZERLERİ
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="34251653">
         34.251.653
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="25644155">
         25.644.155
        </div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-208 data-input-row alternate-row presentation-disabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel capslockLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            NAKİT VE NAKİT BENZERLERİNE İLİŞKİN ENFLASYON ETKİSİ
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field capslockLabel monetary-field-default"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field capslockLabel monetary-field-default"></div>
       </div></td>
     </tr>
     <tr class="general_role_520003-row-209 data-input-row presentation-enabled">
      <td class="taxonomy-nondimensional-context-cell"></td>
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel periodEndLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            DÖNEM SONU NAKİT VE NAKİT BENZERLERİ
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value">
         3
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field periodEndLabel monetary-field-default" title="39468926">
         39.468.926
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field periodEndLabel monetary-field-default" title="20862354">
         20.862.354
        </div>
       </div></td>
     </tr>
    </tbody>
   </table><table class="financial-table tbl_general_role_610000" border="1" style="font-size:10px">
    <tbody>
     <tr>
      <td colspan="3" rowspan="6">
       <div class="gwt-Label"></div></td>
     </tr><tr>
      <td colspan="48" align="center" first-col-span="48" class="taxonomy-dimensional-header-cell span-elem span-index-3" parent="0">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Özkaynaklar
        </div>
       </div></td>
      <td rowspan="5" class="taxonomy-dimensional-header-abstract-cell taxonomy-dimensional-header-last-cell col-abstract-index-52 abstract-parent-3">
       <div class="gwt-Label">
       </div></td>
     </tr><tr>
      <td colspan="46" align="center" first-col-span="46" class="taxonomy-dimensional-header-cell span-elem span-index-3" parent="3">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Ana Ortaklığa Ait Özkaynaklar
        </div>
       </div></td>
      <td rowspan="4" class="taxonomy-dimensional-header-abstract-cell taxonomy-dimensional-header-last-cell col-abstract-index-50 abstract-parent-3">
       <div class="gwt-Label">
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-51 exceptional-col-51 taxonomy-dimensional-header-last-cell col-last-index-51" parent="3" rowspan="4">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Kontrol Gücü Olmayan Paylar
        </div>
       </div></td>
     </tr><tr>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-4" parent="3" rowspan="3">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Ödenmiş Sermaye
        </div>
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-5" parent="3" rowspan="3">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Sermaye Düzeltme Farkları
        </div>
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-6" parent="3" rowspan="3">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Birleşme Denkleştirme Hesabı
        </div>
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-7" parent="3" rowspan="3">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Pay Sahiplerinin İlave Sermaye Katkıları
        </div>
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-8" parent="3" rowspan="3">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Sermaye Avansı
        </div>
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-9" parent="3" rowspan="3">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Geri Alınmış Paylar
        </div>
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-10" parent="3" rowspan="3">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Karşılıklı İştirak Sermaye Düzeltmesi
        </div>
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-11" parent="3" rowspan="3">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Pay İhraç Primleri / İskontoları
        </div>
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-12" parent="3" rowspan="3">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Ortak Kontrole Tabi Teşebbüs veya İşletmeleri İçeren Birleşmelerin Etkisi
        </div>
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-13" parent="3" rowspan="3">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Kontrol Gücü Olmayan Paylara İlişkin Satış Opsiyon Değerleme Fonu
        </div>
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-14" parent="3" rowspan="3">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Pay Bazlı Ödemeler
        </div>
       </div></td>
      <td colspan="12" align="center" first-col-span="12" class="taxonomy-dimensional-header-cell span-elem span-index-14" parent="3">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Kar veya Zararda Yeniden Sınıflandırılmayacak Birikmiş Diğer Kapsamlı Gelirler veya Giderler
        </div>
       </div></td>
      <td rowspan="3" class="taxonomy-dimensional-header-abstract-cell taxonomy-dimensional-header-last-cell col-abstract-index-27 abstract-parent-14">
       <div class="gwt-Label">
       </div></td>
      <td colspan="14" align="center" first-col-span="14" class="taxonomy-dimensional-header-cell span-elem span-index-27" parent="3">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Kar veya Zararda Yeniden Sınıflandırılacak Birikmiş Diğer Kapsamlı Gelirler veya Giderler
        </div>
       </div></td>
      <td rowspan="3" class="taxonomy-dimensional-header-abstract-cell taxonomy-dimensional-header-last-cell col-abstract-index-42 abstract-parent-27">
       <div class="gwt-Label">
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-43" parent="3" rowspan="3">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Kardan Ayrılan Kısıtlanmış Yedekler
        </div>
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-44" parent="3" rowspan="3">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Diğer Özkaynak Payları
        </div>
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-45" parent="3" rowspan="3">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Diğer Yedekler
        </div>
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-46" parent="3" rowspan="3">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Ödenen Kar Payı Avansları (Net)
        </div>
       </div></td>
      <td colspan="2" align="center" first-col-span="2" class="taxonomy-dimensional-header-cell span-elem span-index-46" parent="3">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Birikmiş Karlar
        </div>
       </div></td>
      <td rowspan="3" class="taxonomy-dimensional-header-abstract-cell taxonomy-dimensional-header-last-cell col-abstract-index-49 abstract-parent-46">
       <div class="gwt-Label">
       </div></td>
     </tr><tr>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-15" parent="14" rowspan="2">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Özkaynağa Dayalı Finansal Araçlara Yatırımlardan Kaynaklanan Kazançlar (Kayıplar)
        </div>
       </div></td>
      <td colspan="5" align="center" first-col-span="5" class="taxonomy-dimensional-header-cell span-elem span-index-15" parent="14">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Yeniden Değerleme ve Ölçüm Kazanç / Kayıpları
        </div>
       </div></td>
      <td rowspan="2" class="taxonomy-dimensional-header-abstract-cell taxonomy-dimensional-header-last-cell col-abstract-index-21 abstract-parent-15">
       <div class="gwt-Label">
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-22" parent="14" rowspan="2">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Kredi Riskindeki Değişikliğe Bağlı Kazançlar (Kayıplar)
        </div>
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-23" parent="14" rowspan="2">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Özkaynağa Dayalı Finansal Araçlara Yapılan Yatırımlara İlişkin Finansal Riskten Korunma Kazançları (Kayıpları)
        </div>
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-24" parent="14" rowspan="2">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Özkaynak Yöntemi ile Değerlenen Yatırımların Diğer Kapsamlı Gelirinden Kar/Zararda Sınıflandırılmayacak Paylar
        </div>
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-25" parent="14" rowspan="2">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Yabancı Para Çevrim Farkları
        </div>
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-26 taxonomy-dimensional-header-last-cell col-last-index-26" parent="14" rowspan="2">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Diğer Kazanç / Kayıplar
        </div>
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-28" parent="27" rowspan="2">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Yabancı Para Çevrim Farkları
        </div>
       </div></td>
      <td colspan="3" align="center" first-col-span="3" class="taxonomy-dimensional-header-cell span-elem span-index-28" parent="27">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Riskten Korunma Kazanç / Kayıpları
        </div>
       </div></td>
      <td rowspan="2" class="taxonomy-dimensional-header-abstract-cell taxonomy-dimensional-header-last-cell col-abstract-index-32 abstract-parent-28">
       <div class="gwt-Label">
       </div></td>
      <td colspan="3" align="center" first-col-span="3" class="taxonomy-dimensional-header-cell span-elem span-index-32" parent="27">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Yeniden Değerleme ve Sınıflandırma Kazanç / Kayıpları
        </div>
       </div></td>
      <td rowspan="2" class="taxonomy-dimensional-header-abstract-cell taxonomy-dimensional-header-last-cell col-abstract-index-36 abstract-parent-32">
       <div class="gwt-Label">
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-37" parent="27" rowspan="2">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Opsiyonların Zaman Değerindeki Değişiklikler
        </div>
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-38" parent="27" rowspan="2">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Forward Sözleşmesinin Forward Bileşeninin Değerindeki Değişiklikler
        </div>
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-39" parent="27" rowspan="2">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Yabancı Para Alım Satım Marjlarındaki Değişiklikler
        </div>
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-40" parent="27" rowspan="2">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Özkaynak Yöntemi ile Değerlenen Yatırımların Diğer Kapsamlı Gelirinden Kar/Zararda Sınıflandırılacak Paylar
        </div>
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-41 taxonomy-dimensional-header-last-cell col-last-index-41" parent="27" rowspan="2">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Diğer Kazanç / Kayıplar
        </div>
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-47" parent="46" rowspan="2">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Geçmiş Yıllar Kar / Zararları
        </div>
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-48 taxonomy-dimensional-header-last-cell col-last-index-48" parent="46" rowspan="2">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Net Dönem Karı Zararı
        </div>
       </div></td>
     </tr><tr>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-16" parent="15" rowspan="1">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Maddi Duran Varlık Yeniden Değerleme Artışları/Azalışları
        </div>
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-17" parent="15" rowspan="1">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Maddi Olmayan Duran Varlık Yeniden Değerleme Artışları/Azalışları
        </div>
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-18" parent="15" rowspan="1">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Kullanım Hakkı Varlıkları Yeniden Değerleme Artışları/Azalışları
        </div>
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-19" parent="15" rowspan="1">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Tanımlanmış Fayda Planları Yeniden Ölçüm Kazançları/Kayıpları
        </div>
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-20 taxonomy-dimensional-header-last-cell col-last-index-20" parent="15" rowspan="1">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Diğer Yeniden Değerleme ve Ölçüm Kazanç/Kayıpları
        </div>
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-29" parent="28" rowspan="1">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Nakit Akış Riskinden Korunma Kazançları (Kayıpları)
        </div>
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-30" parent="28" rowspan="1">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Yurtdışındaki İşletmeye İlişkin Yatırım Riskinden Korunma Kazançları (Kayıpları)
        </div>
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-31 taxonomy-dimensional-header-last-cell col-last-index-31" parent="28" rowspan="1">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Diğer Riskten Korunma Kazançları (Kayıpları)
        </div>
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-33" parent="32" rowspan="1">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Satılmaya Hazır Finansal Varlıkların Yeniden Değerleme ve/veya Sınıflandırma Kazançları (Kayıpları)
        </div>
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-34" parent="32" rowspan="1">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Gerçeğe Uygun Değer Farkı Diğer Kapsamlı Gelire Yansıtılan Finansal Varlıklardan Kazançlar (Kayıplar)
        </div>
       </div></td>
      <td colspan="0" align="center" class="taxonomy-dimensional-header-cell col-index-35 taxonomy-dimensional-header-last-cell col-last-index-35" parent="32" rowspan="1">
       <div>
        <div class="gwt-Label multi-language-content content-tr" style="display: block;">
         Diğer Yeniden Değerleme ve Sınıflandırma Kazanç/Kayıpları
        </div>
       </div></td>
     </tr><tr>
      <td></td>
      <td rowspan="34" class="taxonomy-context-cell" style="white-space: nowrap;">
       <div>
        <div class="gwt-HTML multi-language-content content-tr" style="display: block;">
         Önceki Dönem
         <br>
         01.01.2025 - 30.06.2025
        </div>
       </div></td>
     </tr><tr class="general_role_610000-row-7 abstract-row">
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel taxonomy-abstract-title standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Özkaynaklar Değişim Tablosu
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
     </tr><tr class="general_role_610000-row-8 abstract-row">
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel taxonomy-abstract-title standardLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Özkaynaklar Değişim Tablosu
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
     </tr><tr class="general_role_610000-row-9 data-input-row alternate-row presentation-enabled">
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel periodStartLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Dönem Başı Bakiyeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="4560000">
         4.560.000
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="39768135">
         39.768.135
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-6">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-7">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-8">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-9">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-10">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-11">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="32375861">
         32.375.861
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-12">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-13">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-14">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-15">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-16">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="7081918">
         7.081.918
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-17">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-18">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-19">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="-1969222">
         -1.969.222
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-20">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-21">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="5112696">
         5.112.696
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-22">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-23">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-24">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-25">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-26">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-27">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="5112696">
         5.112.696
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-28">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="-180038">
         -180.038
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-29">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-30">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-31">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-32">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-33">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-34">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="-755112">
         -755.112
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-35">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-36">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="-755112">
         -755.112
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-37">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-38">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-39">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-40">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-41">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-42">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="-935150">
         -935.150
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-43">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="7827665">
         7.827.665
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-44">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-45">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-46">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-47">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="103961642">
         103.961.642
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-48">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="23581001">
         23.581.001
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-49">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="127542643">
         127.542.643
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-50">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="216251850">
         216.251.850
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-51">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="1634173">
         1.634.173
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-52">
       <div>
        <div class="gwt-Label taxonomy-label-field periodStartLabel monetary-field-default" title="217886023">
         217.886.023
        </div>
       </div></td>
     </tr><tr class="general_role_610000-row-10 data-input-row presentation-enabled">
      <td class="taxonomy-field-title">
       <table cellspacing="0" cellpadding="0" class="taxonomy-title-panel standartCapitalsLabel" border="1" style="font-size:10px">
        <tbody>
         <tr>
          <td align="left" style="vertical-align: top;">
           <div class="gwt-Label multi-language-content content-tr" style="display: block;">
            Muhasebe Politikalarındaki Değişikliklere İlişkin Düzeltmeler
           </div></td>
          <td align="left" style="vertical-align: top;"></td>
          <td align="left" style="vertical-align: top;">
           <div class="fa fa-question-circle fa-2x taxonomy-field-documentation  multi-language-content content-tr" aria-hidden="true" style="display: block;"></div></td>
          <td align="left" style="vertical-align: top;"></td>
         </tr>
        </tbody>
       </table></td>
      <td class="taxonomy-footnote-cell">
       <div>
        <div class="taxonomy-footnote-value"></div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-4">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-5">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-6">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-7">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-8">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-9">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-10">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-11">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-12">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-13">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-14">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-15">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-16">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-17">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-18">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-19">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-20">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-21">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-22">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-23">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-24">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-25">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-26">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-27">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-28">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-29">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-30">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-31">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-32">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-33">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-34">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-35">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-36">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-37">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-38">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-39">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-40">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-41">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-42">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-43">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-44">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-45">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-46">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-47">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-48">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-49">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-50">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-51">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
      <td class="taxonomy-context-value col-order-class-52">
       <div>
        <div class="gwt-Label taxonomy-label-field standartCapitalsLabel monetary-field-default" title="0">
         0
        </div>
       </div></td>
     </tr>
   </table>
  </div>
 </body>
</html>
"""";

    /// <summary>Builds every named real-source payload.</summary>
    public static IReadOnlyList<RealSourcePayload> Build() =>
    [
        Captured(PricesAsels, PricesAselsJson),
        Captured(PricesThyao, PricesThyaoJson),
        Captured(PricesGaran, PricesGaranJson),
        Captured(PricesFroto, PricesFrotoJson),
        Captured(PricesEbebk, PricesEbebkJson),
        Constructed(PricesHgFallback, PricesHgFallbackJson),
        Constructed(PricesOkFalse, PricesOkFalseJson),
        Constructed(PricesErrorCode, PricesErrorCodeJson),
        KapStatements(StatementsAsels, StatementsAselsJson),
        KapStatements(StatementsThyao, StatementsThyaoJson),
        KapStatements(StatementsGaran, StatementsGaranJson),
        KapStatements(StatementsFroto, StatementsFrotoJson),
        KapStatements(StatementsEbebk, StatementsEbebkJson),
        KapStatements(StatementsToleranceEdges, StatementsToleranceEdgesJson),
        KapStatements(StatementsDuplicateEqual, StatementsDuplicateEqualJson),
        KapStatements(StatementsDuplicateUnequal, StatementsDuplicateUnequalJson),
        EvdsMacro(MacroEvds, MacroEvdsJson),
        EvdsV3Constructed(MacroEvdsV3, MacroEvdsV3Json),
        EvdsV3Captured(MacroEvdsV3Capture, MacroEvdsV3CaptureJson),
        EvdsV3Catalog(MacroEvdsV3Catalog, MacroEvdsV3CatalogJson),
        EvdsV3SerieList(MacroEvdsV3SerieList, MacroEvdsV3SerieListJson),
        CapturedUniverse(Universe, UniverseJson),
        CapturedIndexLevels(IndexLevels, IndexLevelsLabel, IndexLevelsJson),
        CapturedIndexLevels(IndexLevelsXu100, IndexLevelsXu100Label, IndexLevelsXu100Json),
        Vyk(VykMembers, VykMembersJson),
        Vyk(VykDisclosures, VykDisclosuresJson),
        CapturedVyk(VykDisclosureDetail, VykDisclosureDetailJson),
        KapPublicCaptured(KapPublicRobots, KapPublicRobotsLabel, KapPublicRobotsBody, statusCode: 666),
        KapPublicCaptured(KapPublicByCriteria, KapPublicByCriteriaLabel, KapPublicByCriteriaJson,
            method: "POST"),
        KapPublicCaptured(KapPublicMarketProbe, KapPublicMarketProbeLabel, KapPublicMarketProbeJson,
            method: "POST"),
        KapPublicCaptured(KapPublicReport, KapPublicReportLabel, KapPublicReportHtml),
        KapPublicCaptured(KapPublicIdentity, KapPublicIdentityLabel, KapPublicIdentityJson,
            method: "POST"),
    ];

    /// <summary>Loads one payload by name; throws <see cref="KeyNotFoundException"/> when unknown.</summary>
    public static RealSourcePayload Get(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var payload = Build().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
        return payload ?? throw new KeyNotFoundException($"No real-source payload named '{name}' (TKT-foundation-015).");
    }

    /// <summary>Attempts to load one payload by name.</summary>
    public static bool TryGet(string name, out RealSourcePayload payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        payload = Build().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal))!;
        return payload is not null;
    }

    /// <summary>Decodes raw captured bytes with the named charset (utf-8 or a code page).</summary>
    public static string Decode(byte[] bytes, string encodingName)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(encodingName);

        var encoding = string.Equals(encodingName, "utf-8", StringComparison.OrdinalIgnoreCase)
            ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false)
            : Encoding.GetEncoding(encodingName);
        return encoding.GetString(bytes);
    }

    private static RealSourcePayload Captured(string name, string json) =>
        Payload(name, CapturedLabel, RealSourceProvenance.Captured, json);

    private static RealSourcePayload Constructed(string name, string json) =>
        Payload(name, ConstructedLabel, RealSourceProvenance.Constructed, json);

    private static RealSourcePayload KapStatements(string name, string json) =>
        Payload(name, KapStatementsLabel, RealSourceProvenance.Constructed, json, KapSource);

    private static RealSourcePayload Vyk(string name, string json) =>
        Payload(name, VykLabel, RealSourceProvenance.Constructed, json, KapSource);

    private static RealSourcePayload CapturedVyk(string name, string json) =>
        Payload(name, VykDetailCaptureLabel, RealSourceProvenance.Captured, json, KapSource);

    /// <summary>
    /// One captured KAP public-site body (TKT-foundation-079). Its source is the lane's own
    /// evidence class <c>kap-public</c> (never the VYK gateway's <c>kap</c>), its default route
    /// is the <b>source-double serving route</b> every catalog entry uses
    /// (<c>/real-sources/{name}</c>), and its status is the status the live probe actually
    /// returned — recorded per entry rather than assumed to be 200, because the <c>robots.txt</c>
    /// probe answered HTTP 666 and the record must not contradict its own provenance label. The
    /// live upstream route is recorded in the entry's provenance label (and in the lane's
    /// <c>Ingestion:KapPublic</c> configuration) — <see cref="RealSourcePayload.DefaultPath"/>
    /// is the double's route, never a repurposing of the upstream one.
    /// </summary>
    private static RealSourcePayload KapPublicCaptured(
        string name,
        string label,
        string body,
        string method = "GET",
        int statusCode = 200) =>
        new(name, KapPublicSource, method, $"/real-sources/{name}", statusCode, "utf-8",
            RealSourceProvenance.Captured, label, Encoding.UTF8.GetBytes(body));

    private static RealSourcePayload EvdsMacro(string name, string json) =>
        Payload(name, EvdsMacroLabel, RealSourceProvenance.Constructed, json, EvdsSource);

    private static RealSourcePayload EvdsV3Constructed(string name, string json) =>
        Payload(name, EvdsV3ConstructedLabel, RealSourceProvenance.Constructed, json, EvdsSource, method: "POST");

    private static RealSourcePayload EvdsV3Captured(string name, string json) =>
        Payload(name, EvdsV3CaptureLabel, RealSourceProvenance.Captured, json, EvdsSource, method: "POST");

    private static RealSourcePayload EvdsV3Catalog(string name, string json) =>
        Payload(name, EvdsV3CatalogLabel, RealSourceProvenance.Constructed, json, EvdsSource);

    private static RealSourcePayload EvdsV3SerieList(string name, string json) =>
        Payload(name, EvdsV3SerieListLabel, RealSourceProvenance.Constructed, json, EvdsSource);

    private static RealSourcePayload CapturedUniverse(string name, string json) =>
        Payload(name, UniverseLabel, RealSourceProvenance.Captured, json);

    private static RealSourcePayload CapturedIndexLevels(string name, string label, string json) =>
        Payload(name, label, RealSourceProvenance.Captured, json);

    private static RealSourcePayload Payload(
        string name,
        string label,
        RealSourceProvenance provenance,
        string json,
        string source = IsYatirimSource,
        string method = "GET") =>
        new(
            name,
            source,
            method,
            $"/real-sources/{name}",
            200,
            "utf-8",
            provenance,
            label,
            Encoding.UTF8.GetBytes(json));
}
