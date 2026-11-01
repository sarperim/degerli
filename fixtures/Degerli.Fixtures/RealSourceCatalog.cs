using System.Collections.Frozen;
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
/// <c>Names</c>/<c>Build</c>/<c>Get</c>/<c>TryGet</c> helper seam. Payload bodies live as
/// standalone <c>.json</c>/<c>.html</c> files under <c>fixtures/Degerli.Fixtures/Payloads/</c>
/// compiled into <c>Degerli.Fixtures.dll</c> as embedded resources (<c>real-sources/{name}</c>)
/// and served through the WireMock harness as <b>raw bytes</b> with their declared
/// <c>Content-Type; charset=</c> header, parsed by the <b>production</b> adapter in
/// <c>src/Ingestion</c>.
/// </summary>
public static class RealSourceCatalog
{
    internal const string ResourcePrefix = "real-sources/";

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

    // TKT-foundation-088's capture set — the IS/BS coverage evidence (the 1643141 double above
    // is a 6-month interim whose balance sheet and income statement are trimmed to a prefix, so
    // it cannot evidence a debt line, an equity total or an annual column set). Each of the
    // three is a BOUNDED TRIM of the live export, committed as a double and labelled as such.
    public const string KapPublicReportTuprs = "kap-public-report-1554106";
    public const string KapPublicReportAselsAnnual = "kap-public-report-1561039";
    public const string KapPublicReportMgros = "kap-public-report-1648240";

    // TKT-foundation-093's capture: the industrial FR body the statements-view row set is
    // evidenced from (the full IS/BS/CF taxonomy rows the user's view needs — trade
    // receivables/payables, inventories, PP&E, right-of-use assets, goodwill, lease
    // liabilities, deferred revenue, the equity block and the CF detail lines). A BOUNDED
    // TRIM of the live export, committed as a double and labelled as such.
    public const string KapPublicReportThyao = "kap-public-report-1565996";

    // TKT-foundation-101's capture: the FR body whose `Sunum Para Birimi` is a BARE currency
    // (`TL`, no multiplier) — every other committed body declares a multiplier (`1.000 TL`,
    // `1.000.000 TL`). It is the evidence the decoder's bare-currency reading is authored
    // against, and it is NOT a GUBRF body: the file the orchestrator's 2026-10-29 paced pass
    // produced (`/tmp/opencode/captures/kap-export-GUBRF-1605802.json`) carries ASTOR ENERJİ
    // A.Ş.'s heading and disclosure index 1605802, so it is labelled for what it actually is
    // (see the label below). A BOUNDED TRIM of the export, committed as a double.
    public const string KapPublicReportAstorQ1 = "kap-public-report-1605802";
    public const string KapPublicIdentity = "kap-public-identity-ASELS";

    // TKT-foundation-091's comma-coded identity answers. Run 301 (TKT-foundation-087's live pass)
    // observed that `/tr/api/search/combined` answers several XU030 companies with a COMMA-JOINED
    // `cmpOrFundCode` and that the exact-equality rule could never match them; the only per-symbol
    // fact 087's record carries is GARAN's "garan,tgb" with several candidate rows. These two
    // entries are the committed doubles the comma-token rule is authored against.
    // HONESTY, stated on the entry itself: they are CONSTRUCTED, not captured — this ticket issued
    // ZERO live requests (the lane's politeness directive), so no verbatim answer for these symbols
    // exists in the repository. Recorded run-301 facts: the comma-joined code and that several
    // candidate rows exist, for GARAN only. Everything else — the oids, GARAN's second candidate
    // and the token it shares, ISCTR's whole answer, the row order — is constructed for the branch
    // shape, on the committed ASELS answer's exact frame.
    public const string KapPublicIdentityGaranCommaCoded = "kap-public-identity-garan-comma-coded";
    public const string KapPublicIdentityIsctrCommaCoded = "kap-public-identity-isctr-comma-coded";

    // TKT-foundation-099's Turkish dotless-ı identity captures. The 091 live pass (run 362) left
    // four XU030 companies unresolved — BIMAS, ENKAI, ISCTR, SISE — because KAP spells their
    // `cmpOrFundCode` with the Turkish dotless ı ('bımas', 'sıse', 'enkaı',
    // 'ısatr,ısbtr,ısctr,ıskur,tıb') and `OrdinalIgnoreCase` folds case but not ı↔i. These four
    // bodies are the VERBATIM search answers the fold is authored against — captured by the
    // ticket's orchestrator-owned paced pass (one request per symbol, ≥3 s apart, single-flight,
    // honest browser-shaped User-Agent), committed unmodified so the dotless spelling in the record
    // is the source's own. No CI test ever calls kap.org.tr.
    public const string KapPublicIdentityBimas = "kap-public-identity-BIMAS";
    public const string KapPublicIdentityEnkai = "kap-public-identity-ENKAI";
    public const string KapPublicIdentitySise = "kap-public-identity-SISE";
    public const string KapPublicIdentityIsctrDotless = "kap-public-identity-ISCTR";

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
        KapPublicReportTuprs,
        KapPublicReportAselsAnnual,
        KapPublicReportMgros,
        KapPublicReportThyao,
        KapPublicReportAstorQ1,
        KapPublicIdentity,
        KapPublicIdentityGaranCommaCoded,
        KapPublicIdentityIsctrCommaCoded,
        KapPublicIdentityBimas,
        KapPublicIdentityEnkai,
        KapPublicIdentitySise,
        KapPublicIdentityIsctrDotless,
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

    private const string EvdsMacroLabel =
        "Constructed by hand for EVDS real-envelope parsing-fidelity coverage (the column-oriented " +
        "items/Tarih shape, five official series) — a shape-faithful body, not a live capture.";

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

    private const string UniverseLabel =
        "Captured live from the İş Yatırım GetHissePerformans POST handler on 2026-10-18 " +
        "(bounded: the first five Ulusal-Tüm rows, compact re-serialization; source padding preserved).";

    private const string IndexLevelsLabel =
        "Captured live from the İş Yatırım IndexHistoricalAll chart handler on 2026-10-18 " +
        "(bounded: nine XU030 daily index closes, 2026-10-06..2026-10-16, compact re-serialization).";

    private const string IndexLevelsXu100Label =
        "Captured live from the İş Yatırım IndexHistoricalAll chart handler on 2026-10-18 " +
        "(bounded: nine XU100 daily index closes, 2026-10-06..2026-10-16, compact re-serialization).";

    private const string VykLabel =
        "Constructed by hand for KAP VYK API (MKK gateway) list parsing-fidelity coverage " +
        "(the /members and /disclosures shapes, live-verified 2026-10-18) — a shape-faithful " +
        "body, not a live capture.";

    private const string VykDetailCaptureLabel =
        "Captured live from the MKK KAP VYK disclosureDetail endpoint on 2026-10-18 " +
        "(bounded: disclosure 1226255's balance-sheet + income-statement presentation trees " +
        "trimmed to a representative real-report-item subset; real CURR/PREV values kept).";

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

    private const string KapPublicIdentityGaranCommaCodedLabel =
        "CONSTRUCTED, not captured: a comma-coded identity answer of the shape the run-301 live " +
        "pass (TKT-foundation-087, 2026-10-27) recorded for a GARAN keyword — POST " +
        "/tr/api/search/combined answering companies whose cmpOrFundCode is comma-JOINED with " +
        "several candidate rows, the recorded code being 'garan,tgb', which the exact-equality " +
        "rule could never match (the convention TKT-foundation-046 fixed for the VYK directory). " +
        "Constructed because TKT-foundation-091 issued ZERO live requests (the lane's politeness " +
        "directive): no verbatim GARAN answer exists in this repository, and 087's record carries " +
        "only the comma-joined code and the multiple candidate rows. Constructed for the branch " +
        "shape: the oids, the second candidate ('garan,gryo', GARANTİ YATIRIM ORTAKLIĞI) and the " +
        "fact that it shares the 'garan' token — 087's record does not say that — the row order, " +
        "and the frame (the committed ASELS answer's exact shape).";

    private const string KapPublicIdentityIsctrCommaCodedLabel =
        "CONSTRUCTED, not captured: a comma-coded identity answer with exactly ONE distinct " +
        "company candidate — a company whose cmpOrFundCode is comma-joined ('isctr,isatr', the " +
        "two share groups of one bank) beside a FUND hit that carries the bare 'isctr' token. " +
        "087's run-301 record lists ISCTR among the eight symbols the comma-coding root cause " +
        "covers but carries nothing per-symbol for it, and TKT-foundation-091 issued ZERO live " +
        "requests (the lane's politeness directive), so this is not a capture: the comma-joined " +
        "code, its second token, the mixed company/fund shape, the oids and the row order are " +
        "all constructed for the branch shape. The frame is the committed ASELS answer's exact " +
        "shape.";

    private const string KapPublicIdentityBimasLabel =
        "Captured live from POST /tr/api/search/combined with body {\"keyword\":\"BIMAS\"} on " +
        "2026-10-29 (the TKT-foundation-099 orchestrator-owned capture pass: one paced, " +
        "single-flight request per symbol with the lane's honest browser-shaped User-Agent): the " +
        "companyOrFunds hit carrying memberOrFundOid 4028e4a140e95be70140ee1b7b030119, searchType " +
        "'C', cmpOrFundCode 'bımas' — the Turkish DOTLESS ı (U+0131), which OrdinalIgnoreCase does " +
        "not fold onto ASCII i, the reason the bound symbol BIMAS could never match it. Verbatim: " +
        "the body is committed unmodified, so the dotless code in this record is the source's own.";

    private const string KapPublicIdentityEnkaiLabel =
        "Captured live from POST /tr/api/search/combined with body {\"keyword\":\"ENKAI\"} on " +
        "2026-10-29 (the TKT-foundation-099 orchestrator-owned capture pass: one paced, " +
        "single-flight request per symbol with the lane's honest browser-shaped User-Agent): the " +
        "companyOrFunds hit carrying memberOrFundOid 4028e4a240e95dc90140ed3883fe0093, searchType " +
        "'C', cmpOrFundCode 'enkaı' — the same dotless-ı root cause as BIMAS/SISE, not a different " +
        "code shape. Verbatim: the body is committed unmodified.";

    private const string KapPublicIdentitySiseLabel =
        "Captured live from POST /tr/api/search/combined with body {\"keyword\":\"SISE\"} on " +
        "2026-10-29 (the TKT-foundation-099 orchestrator-owned capture pass: one paced, " +
        "single-flight request per symbol with the lane's honest browser-shaped User-Agent): the " +
        "companyOrFunds hit carrying memberOrFundOid 4028e4a140f2ed710140f385d5690102, searchType " +
        "'C', cmpOrFundCode 'sıse' — the Turkish DOTLESS ı (U+0131) that OrdinalIgnoreCase never " +
        "folds onto ASCII i. Verbatim: the body is committed unmodified.";

    private const string KapPublicIdentityIsctrDotlessLabel =
        "Captured live from POST /tr/api/search/combined with body {\"keyword\":\"ISCTR\"} on " +
        "2026-10-29 (the TKT-foundation-099 orchestrator-owned capture pass: one paced, " +
        "single-flight request per symbol with the lane's honest browser-shaped User-Agent): the " +
        "companyOrFunds hit carrying memberOrFundOid 4028e4a140f2ed7201411682b0cb05c6, searchType " +
        "'C', cmpOrFundCode 'ısatr,ısbtr,ısctr,ıskur,tıb' — a FIVE-token comma list in which every " +
        "token carries the dotless ı, so this answer needs 091's comma-token rule and 099's fold " +
        "together (the token 'ısctr' folds to 'isctr'). Verbatim: the body is committed unmodified.";

    private const string KapPublicReportTuprsLabel =
        "Captured live from GET /tr/api/notification/export/excel/1554106 on 2026-10-27 (a " +
        "paced, single-flight request with the lane's honest browser-shaped User-Agent; the " +
        "route declares application/vnd.ms-excel and serves HTML — 1,330,031 bytes): TÜPRAŞ " +
        "2025 Yıllık FR, Gönderim 06.02.2026 18:34:29, Yıl 2025 / Periyot 4, Sunum Para Birimi " +
        "'1.000 TL', Konsolide. BOUNDED TRIM, honestly labelled: the header metadata block is " +
        "kept verbatim and every table keeps its heading and its column-context headers; of " +
        "the outer <tr> rows the balance sheet keeps 82 of 298, the income statement " +
        "(tbl_general_role_310000) 33 of 72, the other-comprehensive-income statement " +
        "(tbl_general_role_420000) 27 of 84, the cash-flow statement 55 of 210 and the equity " +
        "block 11 of 74 (a header-preserving stub). A row is kept when it carries a valued " +
        "cell, a column-context header, a table title or a named section marker, so every " +
        "retained value is real and the retained rows decode to the same statement rows and " +
        "figures as the whole document. This is NOT a byte-verbatim capture of the whole " +
        "document.";

    private const string KapPublicReportAselsAnnualLabel =
        "Captured live from GET /tr/api/notification/export/excel/1561039 on 2026-10-27 (a " +
        "paced, single-flight request with the lane's honest browser-shaped User-Agent; the " +
        "route declares application/vnd.ms-excel and serves HTML — 1,762,721 bytes): ASELSAN " +
        "2025 Yıllık FR, Gönderim 24.02.2026 18:27:36, Yıl 2025 / Periyot 4, Sunum Para Birimi " +
        "'1.000 TL', Konsolide. This is the report the committed `byCriteria` capture lists and " +
        "TKT-foundation-081 recorded as the annual body it did not yet have. BOUNDED TRIM, " +
        "honestly labelled: the header metadata block is kept verbatim and every table keeps " +
        "its heading and its column-context headers; of the outer <tr> rows the balance sheet " +
        "keeps 97 of 298, the combined income-and-comprehensive-income statement " +
        "(tbl_general_role_310003) 58 of 154, the cash-flow statement 60 of 210 and the equity " +
        "block 11 of 74 (a header-preserving stub). A row is kept when it carries a valued " +
        "cell, a column-context header, a table title or a named section marker, so every " +
        "retained value is real and the retained rows decode to the same statement rows and " +
        "figures as the whole document. This is NOT a byte-verbatim capture of the whole " +
        "document.";

    private const string KapPublicReportMgrosLabel =
        "Captured live from GET /tr/api/notification/export/excel/1648240 on 2026-10-27 (a " +
        "paced, single-flight request with the lane's honest browser-shaped User-Agent; the " +
        "route declares application/vnd.ms-excel and serves HTML — 1,382,517 bytes): MİGROS " +
        "2026-06 6 Aylık FR, Gönderim 11.08.2026 21:43:53, Yıl 2026 / Periyot 2, Sunum Para " +
        "Birimi '1.000 TL', Konsolide. BOUNDED TRIM, honestly labelled: the header metadata " +
        "block is kept verbatim and every table keeps its heading and its column-context " +
        "headers; of the outer <tr> rows the balance sheet keeps 82 of 298, the income " +
        "statement (tbl_general_role_310000) 31 of 72, the other-comprehensive-income " +
        "statement (tbl_general_role_420000) 16 of 84, the cash-flow statement 53 of 210 and " +
        "the equity block 11 of 74 (a header-preserving stub). A row is kept when it carries a " +
        "valued cell, a column-context header, a table title or a named section marker, so " +
        "every retained value is real and the retained rows decode to the same statement rows " +
        "and figures as the whole document. This is NOT a byte-verbatim capture of the whole " +
        "document.";

    private const string KapPublicReportThyaoLabel =
        "Captured live into /tmp/opencode/captures/kap-excel-1565996.html (1,349,431 bytes; the " +
        "fetch predates this ticket, which issued ZERO live requests): THYAO 2025 Yıllık FR, " +
        "Gönderim 04.03.2026 18:55:17, Yıl 2025 / Periyot 4, Sunum Para Birimi '1.000.000 TL' " +
        "(millions), Finansal Tablo Niteliği Konsolide, company heading 'TÜRK HAVA YOLLARI " +
        "A.O.' / 'Finansal Rapor'. This body evidences the industrial row set the statements " +
        "view needs; the four 088 bodies keep their own billings. BOUNDED TRIM, honestly " +
        "labelled: the header metadata block is kept verbatim and every table keeps its " +
        "heading and its column-context headers; of the outer <tr> rows the balance sheet " +
        "(tbl_general_role_210015) keeps 126 of 298, the income statement " +
        "(tbl_general_role_310003) 65 of 152, the cash-flow statement " +
        "(tbl_general_role_520003) 84 of 210 and the equity block (tbl_general_role_610000) " +
        "11 of 74 (a header-preserving stub, the 1643141 precedent). A row is kept when it " +
        "carries a valued cell, a column-context header, a table title or a named section " +
        "marker, so every retained value is real and the retained rows decode to the same " +
        "statement rows and figures as the whole document — verified cell-by-cell against the " +
        "whole capture before commit. This is NOT a byte-verbatim capture of the whole " +
        "document.";

    private const string KapPublicReportAstorQ1Label =
        "Captured live into /tmp/opencode/captures/kap-export-GUBRF-1605802.json on 2026-10-29 " +
        "(the orchestrator's paced pass; TKT-foundation-101 issued ZERO live requests): the FR " +
        "export answer for disclosure index 1605802 — company heading 'ASTOR ENERJİ A.Ş.' / " +
        "'Finansal Rapor', Gönderim Tarihi 11.05.2026 22:38:31, Bildirim Tipi FR, Yıl 2026 / " +
        "Periyot 1, Finansal Tablo Niteliği Konsolide. HONESTY, stated on the entry itself: the " +
        "capture was filed under the GUBRF name, but its own <h1> and its own disclosure index " +
        "name ASTOR ENERJİ — nothing in this body is GUBRF data and none is treated as such. Its " +
        "evidentiary value is the presentation-unit declaration: 'Sunum Para Birimi' is the BARE " +
        "currency 'TL' (no multiplier), the shape the decoder could not read and the store " +
        "therefore refused as SCHEMA_MISMATCH. BOUNDED TRIM, honestly labelled: the header " +
        "metadata block is kept verbatim; the balance sheet (tbl_general_role_210015, 443,752 of " +
        "1,278,975 body bytes) and the income statement (tbl_general_role_310003, 230,636 bytes) " +
        "are kept COMPLETE, each with its headings and column-context headers; the cash-flow " +
        "(tbl_general_role_520003) and equity (tbl_general_role_610000) tables are omitted whole " +
        "rather than trimmed mid-table. Every retained value is real and the retained tables " +
        "decode to the same rows and figures as the whole document. This is NOT a byte-verbatim " +
        "capture of the whole document.";

    private static readonly Lazy<(IReadOnlyList<RealSourcePayload> All, FrozenDictionary<string, RealSourcePayload> ByName)> Catalog =
        new(InitializeCatalog);

    /// <summary>Builds every named real-source payload.</summary>
    public static IReadOnlyList<RealSourcePayload> Build() => Catalog.Value.All;

    /// <summary>Loads one payload by name; throws <see cref="KeyNotFoundException"/> when unknown.</summary>
    public static RealSourcePayload Get(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return Catalog.Value.ByName.TryGetValue(name, out var payload)
            ? payload
            : throw new KeyNotFoundException($"No real-source payload named '{name}' (TKT-foundation-015).");
    }

    /// <summary>Attempts to load one payload by name.</summary>
    public static bool TryGet(string name, out RealSourcePayload payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return Catalog.Value.ByName.TryGetValue(name, out payload!);
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

    private static (IReadOnlyList<RealSourcePayload> All, FrozenDictionary<string, RealSourcePayload> ByName) InitializeCatalog()
    {
        IReadOnlyList<RealSourcePayload> all =
        [
            Captured(PricesAsels),
            Captured(PricesThyao),
            Captured(PricesGaran),
            Captured(PricesFroto),
            Captured(PricesEbebk),
            Constructed(PricesHgFallback),
            Constructed(PricesOkFalse),
            Constructed(PricesErrorCode),
            KapStatements(StatementsAsels),
            KapStatements(StatementsThyao),
            KapStatements(StatementsGaran),
            KapStatements(StatementsFroto),
            KapStatements(StatementsEbebk),
            KapStatements(StatementsToleranceEdges),
            KapStatements(StatementsDuplicateEqual),
            KapStatements(StatementsDuplicateUnequal),
            EvdsMacro(MacroEvds),
            EvdsV3Constructed(MacroEvdsV3),
            EvdsV3Captured(MacroEvdsV3Capture),
            EvdsV3Catalog(MacroEvdsV3Catalog),
            EvdsV3SerieList(MacroEvdsV3SerieList),
            CapturedUniverse(Universe),
            CapturedIndexLevels(IndexLevels, IndexLevelsLabel),
            CapturedIndexLevels(IndexLevelsXu100, IndexLevelsXu100Label),
            Vyk(VykMembers),
            Vyk(VykDisclosures),
            CapturedVyk(VykDisclosureDetail),
            KapPublicCaptured(KapPublicRobots, KapPublicRobotsLabel, statusCode: 666),
            KapPublicCaptured(KapPublicByCriteria, KapPublicByCriteriaLabel, method: "POST"),
            KapPublicCaptured(KapPublicMarketProbe, KapPublicMarketProbeLabel, method: "POST"),
            KapPublicCaptured(KapPublicReport, KapPublicReportLabel),
            KapPublicCaptured(KapPublicReportTuprs, KapPublicReportTuprsLabel),
            KapPublicCaptured(KapPublicReportAselsAnnual, KapPublicReportAselsAnnualLabel),
            KapPublicCaptured(KapPublicReportMgros, KapPublicReportMgrosLabel),
            KapPublicCaptured(KapPublicReportThyao, KapPublicReportThyaoLabel),
            KapPublicCaptured(KapPublicReportAstorQ1, KapPublicReportAstorQ1Label),
            KapPublicCaptured(KapPublicIdentity, KapPublicIdentityLabel, method: "POST"),
            KapPublicDerived(KapPublicIdentityGaranCommaCoded, KapPublicIdentityGaranCommaCodedLabel),
            KapPublicDerived(KapPublicIdentityIsctrCommaCoded, KapPublicIdentityIsctrCommaCodedLabel),
            KapPublicCaptured(KapPublicIdentityBimas, KapPublicIdentityBimasLabel, method: "POST"),
            KapPublicCaptured(KapPublicIdentityEnkai, KapPublicIdentityEnkaiLabel, method: "POST"),
            KapPublicCaptured(KapPublicIdentitySise, KapPublicIdentitySiseLabel, method: "POST"),
            KapPublicCaptured(KapPublicIdentityIsctrDotless, KapPublicIdentityIsctrDotlessLabel, method: "POST"),
        ];

        var byName = all.ToFrozenDictionary(p => p.Name, StringComparer.Ordinal);
        return (all, byName);
    }

    private static byte[] LoadEmbeddedBytes(string name)
    {
        var resourceName = ResourcePrefix + name;
        using var stream = typeof(RealSourceCatalog).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded real-source payload '{resourceName}' is missing from Degerli.Fixtures. " +
                "Add the file under fixtures/Degerli.Fixtures/Payloads/<source>/.");

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static RealSourcePayload Captured(string name) =>
        Payload(name, CapturedLabel, RealSourceProvenance.Captured);

    private static RealSourcePayload Constructed(string name) =>
        Payload(name, ConstructedLabel, RealSourceProvenance.Constructed);

    private static RealSourcePayload KapStatements(string name) =>
        Payload(name, KapStatementsLabel, RealSourceProvenance.Constructed, KapSource);

    private static RealSourcePayload Vyk(string name) =>
        Payload(name, VykLabel, RealSourceProvenance.Constructed, KapSource);

    private static RealSourcePayload CapturedVyk(string name) =>
        Payload(name, VykDetailCaptureLabel, RealSourceProvenance.Captured, KapSource);

    private static RealSourcePayload KapPublicCaptured(
        string name,
        string label,
        string method = "GET",
        int statusCode = 200) =>
        new(name, KapPublicSource, method, $"/real-sources/{name}", statusCode, "utf-8",
            RealSourceProvenance.Captured, label, LoadEmbeddedBytes(name));

    private static RealSourcePayload KapPublicDerived(string name, string label) =>
        new(name, KapPublicSource, "POST", $"/real-sources/{name}", 200, "utf-8",
            RealSourceProvenance.Constructed, label, LoadEmbeddedBytes(name));

    private static RealSourcePayload EvdsMacro(string name) =>
        Payload(name, EvdsMacroLabel, RealSourceProvenance.Constructed, EvdsSource);

    private static RealSourcePayload EvdsV3Constructed(string name) =>
        Payload(name, EvdsV3ConstructedLabel, RealSourceProvenance.Constructed, EvdsSource, method: "POST");

    private static RealSourcePayload EvdsV3Captured(string name) =>
        Payload(name, EvdsV3CaptureLabel, RealSourceProvenance.Captured, EvdsSource, method: "POST");

    private static RealSourcePayload EvdsV3Catalog(string name) =>
        Payload(name, EvdsV3CatalogLabel, RealSourceProvenance.Constructed, EvdsSource);

    private static RealSourcePayload EvdsV3SerieList(string name) =>
        Payload(name, EvdsV3SerieListLabel, RealSourceProvenance.Constructed, EvdsSource);

    private static RealSourcePayload CapturedUniverse(string name) =>
        Payload(name, UniverseLabel, RealSourceProvenance.Captured);

    private static RealSourcePayload CapturedIndexLevels(string name, string label) =>
        Payload(name, label, RealSourceProvenance.Captured);

    private static RealSourcePayload Payload(
        string name,
        string label,
        RealSourceProvenance provenance,
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
            LoadEmbeddedBytes(name));
}
