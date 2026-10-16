using System.Globalization;
using System.Text;
using System.Text.Json;
using Degerli.Ingestion.Prices;
using Degerli.Ingestion.Statements;

namespace Degerli.Fixtures;

/// <summary>
/// One payload captured verbatim from a live public source endpoint (TKT-foundation-015).
/// Unlike <see cref="CannedSourcePayload"/>, whose bodies are generated from the single
/// synthetic fixture universe (FU §1), this catalog preserves the <em>actual</em> bytes a
/// real source returned — its real field names (<c>HGDG_*</c>), its real numeric layout,
/// its Turkish/XBRL shape and, for the legacy artifact, its non-UTF-8 charset — so
/// ingestion parsing is exercised against real-world shape. The FU golden values are
/// untouched: this catalog tests parsing <em>shape</em>, never derived numbers.
/// </summary>
/// <param name="Name">Stable catalog name (see the constants on <see cref="RealSourceCatalog"/>).</param>
/// <param name="Source">Concrete source: <c>isyatirim</c> or <c>kap</c>.</param>
/// <param name="Method">HTTP method the payload was captured with.</param>
/// <param name="DefaultPath">Source-double route the payload is served at by default.</param>
/// <param name="StatusCode">Captured HTTP status.</param>
/// <param name="EncodingName">Charset of <see cref="RawBytes"/> (<c>utf-8</c> or <c>windows-1254</c>).</param>
/// <param name="RawBytes">The verbatim captured body bytes.</param>
/// <param name="Body">The canonical, UTF-8 body served through the WireMock harness: the
/// envelope the ingestion adapter parses, or (for the legacy artifact) the correctly
/// decoded UTF-8 text.</param>
public sealed record RealSourcePayload(
    string Name,
    string Source,
    string Method,
    string DefaultPath,
    int StatusCode,
    string EncodingName,
    byte[] RawBytes,
    string Body)
{
    /// <summary>True when the captured bytes are not UTF-8 (the legacy-charset artifact).</summary>
    public bool IsLegacyEncoded => !string.Equals(EncodingName, "utf-8", StringComparison.OrdinalIgnoreCase);

    /// <summary>Decodes <see cref="RawBytes"/> with the declared charset.</summary>
    public string DecodeRawText() => RealSourceCatalog.Decode(RawBytes, EncodingName);
}

/// <summary>
/// The real-source payload catalog (TKT-foundation-015) — a second, real-shape catalog
/// that sits alongside the synthetic FU §10 one and is loaded through the same
/// <c>Names</c>/<c>Build</c>/<c>Get</c>/<c>TryGet</c> helper seam. Five capped captures
/// (bounded history, 3 trading days) cover the requested tickers — <c>ASELS</c>,
/// <c>THYAO</c>, <c>GARAN</c>, <c>FROTO</c> and <c>EBEBK</c> (the recent-IPO pick at
/// capture time) — from the İş Yatırım EOD endpoint, plus KAP financial-statement shapes
/// (Turkish-formatted numbers, IFRS/Turkish XBRL tag variance) and one legacy
/// windows-1254 body. The catalog also decodes each raw shape into the canonical
/// ingestion models (<see cref="PricesHistoryPayload"/>, <see cref="StatementsPayload"/>)
/// so parsing can be exercised end-to-end through the harness.
/// </summary>
public static class RealSourceCatalog
{
    // FU §10-style named payloads — the real-source catalog (TKT-foundation-015).
    public const string PricesAsels = "isyatirim-prices-ASELS";
    public const string PricesThyao = "isyatirim-prices-THYAO";
    public const string PricesGaran = "isyatirim-prices-GARAN";
    public const string PricesFroto = "isyatirim-prices-FROTO";
    public const string PricesEbebk = "isyatirim-prices-EBEBK";
    public const string StatementsAsels = "kap-statements-ASELS";
    public const string StatementsThyao = "kap-statements-THYAO";
    public const string StatementsGaran = "kap-statements-GARAN";
    public const string StatementsFroto = "kap-statements-FROTO";
    public const string StatementsEbebk = "kap-statements-EBEBK";
    public const string LegacyWindows1254 = "isyatirim-legacy-windows1254";

    /// <summary>Concrete source name of the captured EOD price payloads.</summary>
    public const string IsYatirimSource = "isyatirim";

    /// <summary>Concrete source name of the captured financial-statement payloads.</summary>
    public const string KapSource = "kap";

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
        StatementsAsels,
        StatementsThyao,
        StatementsGaran,
        StatementsFroto,
        StatementsEbebk,
        LegacyWindows1254,
    ];

    /// <summary>The day the live capture was taken.</summary>
    public static readonly DateOnly CapturedOn = new(2026, 10, 16);

    /// <summary>The bounded capture window (inclusive), a fixed 3-trading-day slice.</summary>
    public const string CaptureWindow = "2026-10-06..2026-10-08";

    /// <summary>
    /// Bounded-capture note recorded with the catalog (TKT-foundation-015): 5 tickers, a
    /// 3-trading-day price slice, one statement period pair per ticker. Prices are the
    /// İş Yatırım <c>HisseTekil</c> envelope; statements model the KAP XBRL fact shape.
    /// </summary>
    public const string CaptureNote =
        "Bounded live capture: İş Yatırım EOD HisseTekil, 5 tickers, 2026-10-06..2026-10-08; " +
        "KAP financial-statement fact shapes (IFRS/TR XBRL tags, TR number formatting); " +
        "legacy windows-1254 report artifact.";

    /// <summary>
    /// Redistribution terms recorded per RISK-MDF-001. BIST end-of-day prices and KAP
    /// statements are public-source facts, redistributed here only in the bounded,
    /// derived, non-commercial form the risk posture (brief §7, free/public sources)
    /// accepts; per-source terms are re-checked during the builder's V0 validation.
    /// </summary>
    public const string RedistributionTerms =
        "RISK-MDF-001: public-source (İş Yatırım / KAP) facts, bounded non-commercial " +
        "redistribution for test-harness parsing fidelity only; per-source terms re-checked at V0.";

    // ---- verbatim captured bodies (bounded; see CaptureNote) ----
    private const string PricesAselsJson = """{"ok":true,"errorCode":null,"errorDescription":null,"transactionId":"23c0bb08b7f14485b4a7dff0fed2727a","value":[{"HGDG_HS_KODU":"ASELS","HGDG_TARIH":"06-10-2026","HGDG_KAPANIS":376.0000,"HGDG_AOF":371.181,"HGDG_MIN":364.25,"HGDG_MAX":379.0,"HGDG_HACIM":13239396512.000000,"END_ENDEKS_KODU":"01","END_TARIH":1791234000000,"END_SEANS":2,"END_DEGER":12374.26,"DD_DOVIZ_KODU":"USD","DD_DT_KODU":"01","DD_TARIH":1791234000000,"DD_DEGER":49.1802,"DOLAR_BAZLI_FIYAT":7.6454,"ENDEKS_BAZLI_FIYAT":0.0304,"DOLAR_HACIM":269201762.3352,"SERMAYE":4.56E9,"HG_KAPANIS":376.0,"HG_AOF":371.181,"HG_MIN":364.25,"HG_MAX":379.0,"PD":1714560000000.00000,"PD_USD":34862810643.30767,"HAO_PD":442013568000.000000,"HAO_PD_USD":8987632583.844717,"HG_HACIM":1.3239396512E10,"DOLAR_BAZLI_MIN":7.4064,"DOLAR_BAZLI_MAX":7.7064,"DOLAR_BAZLI_AOF":7.5474},{"HGDG_HS_KODU":"ASELS","HGDG_TARIH":"07-10-2026","HGDG_KAPANIS":348.7500,"HGDG_AOF":356.437,"HGDG_MIN":346.75,"HGDG_MAX":377.25,"HGDG_HACIM":17063642691.000000,"END_ENDEKS_KODU":"01","END_TARIH":1791320400000,"END_SEANS":2,"END_DEGER":12122.94,"DD_DOVIZ_KODU":"USD","DD_DT_KODU":"01","DD_TARIH":1791320400000,"DD_DEGER":49.1976,"DOLAR_BAZLI_FIYAT":7.0888,"ENDEKS_BAZLI_FIYAT":0.0288,"DOLAR_HACIM":346838924.8866,"SERMAYE":4.56E9,"HG_KAPANIS":348.75,"HG_AOF":356.437,"HG_MIN":346.75,"HG_MAX":377.25,"PD":1590300000000.00000,"PD_USD":32324747548.66091,"HAO_PD":409979340000.000000,"HAO_PD_USD":8333319918.044783,"HG_HACIM":1.7063642691E10,"DOLAR_BAZLI_MIN":7.0481,"DOLAR_BAZLI_MAX":7.6681,"DOLAR_BAZLI_AOF":7.2450},{"HGDG_HS_KODU":"ASELS","HGDG_TARIH":"08-10-2026","HGDG_KAPANIS":348.7500,"HGDG_AOF":345.473,"HGDG_MIN":340.25,"HGDG_MAX":354.0,"HGDG_HACIM":12941100644.000000,"END_ENDEKS_KODU":"01","END_TARIH":1791406800000,"END_SEANS":2,"END_DEGER":12213.58,"DD_DOVIZ_KODU":"USD","DD_DT_KODU":"01","DD_TARIH":1791406800000,"DD_DEGER":49.2152,"DOLAR_BAZLI_FIYAT":7.0862,"ENDEKS_BAZLI_FIYAT":0.0286,"DOLAR_HACIM":262949264.5362,"SERMAYE":4.56E9,"HG_KAPANIS":348.75,"HG_AOF":345.473,"HG_MIN":340.25,"HG_MAX":354.0,"PD":1590300000000.00000,"PD_USD":32313187795.64037,"HAO_PD":409979340000.000000,"HAO_PD_USD":8330339813.716087,"HG_HACIM":1.2941100644E10,"DOLAR_BAZLI_MIN":6.9135,"DOLAR_BAZLI_MAX":7.1929,"DOLAR_BAZLI_AOF":7.0196}]}""";
    private const string PricesThyaoJson = """{"ok":true,"errorCode":null,"errorDescription":null,"transactionId":"def507b11493419d9812dcee5ec7daca","value":[{"HGDG_HS_KODU":"THYAO","HGDG_TARIH":"06-10-2026","HGDG_KAPANIS":290.5000,"HGDG_AOF":292.381,"HGDG_MIN":290.5,"HGDG_MAX":294.25,"HGDG_HACIM":7982835728.000000,"END_ENDEKS_KODU":"01","END_TARIH":1791234000000,"END_SEANS":2,"END_DEGER":12374.26,"DD_DOVIZ_KODU":"USD","DD_DT_KODU":"01","DD_TARIH":1791234000000,"DD_DEGER":49.1802,"DOLAR_BAZLI_FIYAT":5.9068,"ENDEKS_BAZLI_FIYAT":0.0235,"DOLAR_HACIM":162318081.8297,"SERMAYE":1.38E9,"HG_KAPANIS":290.5,"HG_AOF":292.381,"HG_MIN":290.5,"HG_MAX":294.25,"PD":400890000000.00000,"PD_USD":8151451193.77310,"HAO_PD":196957257000.000000,"HAO_PD_USD":4004807971.500724,"HG_HACIM":7.982835728E9,"DOLAR_BAZLI_MIN":5.9068,"DOLAR_BAZLI_MAX":5.9831,"DOLAR_BAZLI_AOF":5.9451},{"HGDG_HS_KODU":"THYAO","HGDG_TARIH":"07-10-2026","HGDG_KAPANIS":286.2500,"HGDG_AOF":286.776,"HGDG_MIN":283.75,"HGDG_MAX":290.5,"HGDG_HACIM":7765288779.000000,"END_ENDEKS_KODU":"01","END_TARIH":1791320400000,"END_SEANS":2,"END_DEGER":12122.94,"DD_DOVIZ_KODU":"USD","DD_DT_KODU":"01","DD_TARIH":1791320400000,"DD_DEGER":49.1976,"DOLAR_BAZLI_FIYAT":5.8184,"ENDEKS_BAZLI_FIYAT":0.0236,"DOLAR_HACIM":157838772.1962,"SERMAYE":1.38E9,"HG_KAPANIS":286.25,"HG_AOF":286.776,"HG_MIN":283.75,"HG_MAX":290.5,"PD":395025000000.00000,"PD_USD":8029355090.49222,"HAO_PD":194115285000.000000,"HAO_PD_USD":3945625091.467877,"HG_HACIM":7.765288779E9,"DOLAR_BAZLI_MIN":5.7676,"DOLAR_BAZLI_MAX":5.9048,"DOLAR_BAZLI_AOF":5.8291},{"HGDG_HS_KODU":"THYAO","HGDG_TARIH":"08-10-2026","HGDG_KAPANIS":284.2500,"HGDG_AOF":284.492,"HGDG_MIN":282.0,"HGDG_MAX":287.5,"HGDG_HACIM":5829586658.000000,"END_ENDEKS_KODU":"01","END_TARIH":1791406800000,"END_SEANS":2,"END_DEGER":12213.58,"DD_DOVIZ_KODU":"USD","DD_DT_KODU":"01","DD_TARIH":1791406800000,"DD_DEGER":49.2152,"DOLAR_BAZLI_FIYAT":5.7757,"ENDEKS_BAZLI_FIYAT":0.0233,"DOLAR_HACIM":118450939.1001,"SERMAYE":1.38E9,"HG_KAPANIS":284.25,"HG_AOF":284.492,"HG_MIN":282.0,"HG_MAX":287.5,"PD":392265000000.00000,"PD_USD":7970403452.59188,"HAO_PD":192759021000.000000,"HAO_PD_USD":3916656256.603650,"HG_HACIM":5.829586658E9,"DOLAR_BAZLI_MIN":5.7299,"DOLAR_BAZLI_MAX":5.8417,"DOLAR_BAZLI_AOF":5.7806}]}""";
    private const string PricesGaranJson = """{"ok":true,"errorCode":null,"errorDescription":null,"transactionId":"9418dc7cdf134f9e9e3879de1206e407","value":[{"HGDG_HS_KODU":"GARAN","HGDG_TARIH":"06-10-2026","HGDG_KAPANIS":131.2000,"HGDG_AOF":130.388,"HGDG_MIN":129.4,"HGDG_MAX":131.4,"HGDG_HACIM":2284977654.000000,"END_ENDEKS_KODU":"01","END_TARIH":1791234000000,"END_SEANS":2,"END_DEGER":12374.26,"DD_DOVIZ_KODU":"USD","DD_DT_KODU":"01","DD_TARIH":1791234000000,"DD_DEGER":49.1802,"DOLAR_BAZLI_FIYAT":2.6677,"ENDEKS_BAZLI_FIYAT":0.0106,"DOLAR_HACIM":46461333.0975,"SERMAYE":4.2E9,"HG_KAPANIS":131.2,"HG_AOF":130.388,"HG_MIN":129.4,"HG_MAX":131.4,"PD":551039987182.61719,"PD_USD":11204508871.10295,"HAO_PD":76980286209.411621,"HAO_PD_USD":1565269889.293082,"HG_HACIM":2.284977654E9,"DOLAR_BAZLI_MIN":2.6311,"DOLAR_BAZLI_MAX":2.6718,"DOLAR_BAZLI_AOF":2.6512},{"HGDG_HS_KODU":"GARAN","HGDG_TARIH":"07-10-2026","HGDG_KAPANIS":129.9000,"HGDG_AOF":129.547,"HGDG_MIN":128.0,"HGDG_MAX":131.5,"HGDG_HACIM":2263850308.000000,"END_ENDEKS_KODU":"01","END_TARIH":1791320400000,"END_SEANS":2,"END_DEGER":12122.94,"DD_DOVIZ_KODU":"USD","DD_DT_KODU":"01","DD_TARIH":1791320400000,"DD_DEGER":49.1976,"DOLAR_BAZLI_FIYAT":2.6404,"ENDEKS_BAZLI_FIYAT":0.0107,"DOLAR_HACIM":46015462.2990,"SERMAYE":4.2E9,"HG_KAPANIS":129.9,"HG_AOF":129.547,"HG_MIN":128.0,"HG_MAX":131.5,"PD":545579974365.23438,"PD_USD":11089564823.59372,"HAO_PD":76162964421.386719,"HAO_PD_USD":1548103249.373683,"HG_HACIM":2.263850308E9,"DOLAR_BAZLI_MIN":2.6018,"DOLAR_BAZLI_MAX":2.6729,"DOLAR_BAZLI_AOF":2.6332},{"HGDG_HS_KODU":"GARAN","HGDG_TARIH":"08-10-2026","HGDG_KAPANIS":128.5000,"HGDG_AOF":128.389,"HGDG_MIN":127.0,"HGDG_MAX":129.8,"HGDG_HACIM":2001849137.000000,"END_ENDEKS_KODU":"01","END_TARIH":1791406800000,"END_SEANS":2,"END_DEGER":12213.58,"DD_DOVIZ_KODU":"USD","DD_DT_KODU":"01","DD_TARIH":1791406800000,"DD_DEGER":49.2152,"DOLAR_BAZLI_FIYAT":2.6110,"ENDEKS_BAZLI_FIYAT":0.0105,"DOLAR_HACIM":40675424.1982,"SERMAYE":4.2E9,"HG_KAPANIS":128.5,"HG_AOF":128.389,"HG_MIN":127.0,"HG_MAX":129.8,"PD":539700000000.00000,"PD_USD":10966124286.80570,"HAO_PD":75342120000.000000,"HAO_PD_USD":1530870950.438076,"HG_HACIM":2.001849137E9,"DOLAR_BAZLI_MIN":2.5805,"DOLAR_BAZLI_MAX":2.6374,"DOLAR_BAZLI_AOF":2.6087}]}""";
    private const string PricesFrotoJson = """{"ok":true,"errorCode":null,"errorDescription":null,"transactionId":"a771211e548141e3bc6f5d4578e98cb1","value":[{"HGDG_HS_KODU":"FROTO","HGDG_TARIH":"06-10-2026","HGDG_KAPANIS":74.6000,"HGDG_AOF":74.826,"HGDG_MIN":74.25,"HGDG_MAX":75.4,"HGDG_HACIM":1020358525.000000,"END_ENDEKS_KODU":"01","END_TARIH":1791234000000,"END_SEANS":2,"END_DEGER":12374.26,"DD_DOVIZ_KODU":"USD","DD_DT_KODU":"01","DD_TARIH":1791234000000,"DD_DEGER":49.1802,"DOLAR_BAZLI_FIYAT":1.5169,"ENDEKS_BAZLI_FIYAT":0.0060,"DOLAR_HACIM":20747343.9514,"SERMAYE":3.5091E9,"HG_KAPANIS":74.6,"HG_AOF":74.826,"HG_MIN":74.25,"HG_MAX":75.4,"PD":261778854645.53833,"PD_USD":5322850550.53738,"HAO_PD":46230145730.402069,"HAO_PD_USD":940015407.224901,"HG_HACIM":1.020358525E9,"DOLAR_BAZLI_MIN":1.5098,"DOLAR_BAZLI_MAX":1.5331,"DOLAR_BAZLI_AOF":1.5215},{"HGDG_HS_KODU":"FROTO","HGDG_TARIH":"07-10-2026","HGDG_KAPANIS":73.8500,"HGDG_AOF":73.984,"HGDG_MIN":73.2,"HGDG_MAX":74.75,"HGDG_HACIM":1013299362.000000,"END_ENDEKS_KODU":"01","END_TARIH":1791320400000,"END_SEANS":2,"END_DEGER":12122.94,"DD_DOVIZ_KODU":"USD","DD_DT_KODU":"01","DD_TARIH":1791320400000,"DD_DEGER":49.1976,"DOLAR_BAZLI_FIYAT":1.5011,"ENDEKS_BAZLI_FIYAT":0.0061,"DOLAR_HACIM":20596520.1961,"SERMAYE":3.5091E9,"HG_KAPANIS":73.85,"HG_AOF":73.984,"HG_MIN":73.2,"HG_MAX":74.75,"PD":259147029645.53833,"PD_USD":5267472999.60848,"HAO_PD":45791280138.366623,"HAO_PD_USD":930762479.030818,"HG_HACIM":1.013299362E9,"DOLAR_BAZLI_MIN":1.4879,"DOLAR_BAZLI_MAX":1.5194,"DOLAR_BAZLI_AOF":1.5038},{"HGDG_HS_KODU":"FROTO","HGDG_TARIH":"08-10-2026","HGDG_KAPANIS":74.4000,"HGDG_AOF":74.271,"HGDG_MIN":73.7,"HGDG_MAX":74.7,"HGDG_HACIM":837500049.000000,"END_ENDEKS_KODU":"01","END_TARIH":1791406800000,"END_SEANS":2,"END_DEGER":12213.58,"DD_DOVIZ_KODU":"USD","DD_DT_KODU":"01","DD_TARIH":1791406800000,"DD_DEGER":49.2152,"DOLAR_BAZLI_FIYAT":1.5117,"ENDEKS_BAZLI_FIYAT":0.0061,"DOLAR_HACIM":17017101.4036,"SERMAYE":3.5091E9,"HG_KAPANIS":74.4,"HG_AOF":74.271,"HG_MIN":73.7,"HG_MAX":74.7,"PD":261077045354.46167,"PD_USD":5304805128.38435,"HAO_PD":46132313914.133377,"HAO_PD_USD":937359066.185515,"HG_HACIM":8.37500049E8,"DOLAR_BAZLI_MIN":1.4975,"DOLAR_BAZLI_MAX":1.5178,"DOLAR_BAZLI_AOF":1.5091}]}""";
    private const string PricesEbebkJson = """{"ok":true,"errorCode":null,"errorDescription":null,"transactionId":"e1575568203e49559c78cb689889ef75","value":[{"HGDG_HS_KODU":"EBEBK","HGDG_TARIH":"06-10-2026","HGDG_KAPANIS":79.4719,"HGDG_AOF":79.78935,"HGDG_MIN":79.27344,"HGDG_MAX":80.61285,"HGDG_HACIM":13476761.000000,"END_ENDEKS_KODU":"01","END_TARIH":1791234000000,"END_SEANS":2,"END_DEGER":12374.26,"DD_DOVIZ_KODU":"USD","DD_DT_KODU":"01","DD_TARIH":1791234000000,"DD_DEGER":49.1802,"DOLAR_BAZLI_FIYAT":1.6159,"ENDEKS_BAZLI_FIYAT":0.0064,"DOLAR_HACIM":274028.1861,"SERMAYE":1.6E8,"HG_KAPANIS":80.1,"HG_AOF":80.42,"HG_MIN":79.9,"HG_MAX":81.25,"PD":12815999755.85938,"PD_USD":260592672.57676,"HAO_PD":3552595132.324220,"HAO_PD_USD":72236288.838278,"HG_HACIM":1.3476761E7,"DOLAR_BAZLI_MIN":1.6119,"DOLAR_BAZLI_MAX":1.6391,"DOLAR_BAZLI_AOF":1.6224},{"HGDG_HS_KODU":"EBEBK","HGDG_TARIH":"07-10-2026","HGDG_KAPANIS":76.1481,"HGDG_AOF":77.61355,"HGDG_MIN":76.14813,"HGDG_MAX":79.86873,"HGDG_HACIM":42309082.000000,"END_ENDEKS_KODU":"01","END_TARIH":1791320400000,"END_SEANS":2,"END_DEGER":12122.94,"DD_DOVIZ_KODU":"USD","DD_DT_KODU":"01","DD_TARIH":1791320400000,"DD_DEGER":49.1976,"DOLAR_BAZLI_FIYAT":1.5478,"ENDEKS_BAZLI_FIYAT":0.0063,"DOLAR_HACIM":859982.6414,"SERMAYE":1.6E8,"HG_KAPANIS":76.75,"HG_AOF":78.227,"HG_MIN":76.75,"HG_MAX":80.5,"PD":12280000000.00000,"PD_USD":249605671.82139,"HAO_PD":3408928000.000000,"HAO_PD_USD":69290534.497618,"HG_HACIM":4.2309082E7,"DOLAR_BAZLI_MIN":1.5478,"DOLAR_BAZLI_MAX":1.6234,"DOLAR_BAZLI_AOF":1.5776},{"HGDG_HS_KODU":"EBEBK","HGDG_TARIH":"08-10-2026","HGDG_KAPANIS":80.0176,"HGDG_AOF":78.376526,"HGDG_MIN":74.95755,"HGDG_MAX":80.56323,"HGDG_HACIM":39335497.000000,"END_ENDEKS_KODU":"01","END_TARIH":1791406800000,"END_SEANS":2,"END_DEGER":12213.58,"DD_DOVIZ_KODU":"USD","DD_DT_KODU":"01","DD_TARIH":1791406800000,"DD_DEGER":49.2152,"DOLAR_BAZLI_FIYAT":1.6259,"ENDEKS_BAZLI_FIYAT":0.0066,"DOLAR_HACIM":799255.0472,"SERMAYE":1.6E8,"HG_KAPANIS":80.65,"HG_AOF":78.996,"HG_MIN":75.55,"HG_MAX":81.2,"PD":12904000244.14063,"PD_USD":262195424.26203,"HAO_PD":3588602467.895509,"HAO_PD_USD":72916547.487271,"HG_HACIM":3.9335497E7,"DOLAR_BAZLI_MIN":1.5231,"DOLAR_BAZLI_MAX":1.6370,"DOLAR_BAZLI_AOF":1.5925}]}""";
    private const string StatementsAselsJson = """{"companyCode":"ASELS","unitScale":"units","currency":"TRY","facts":[{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"IS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:Revenue","value":"1.234.567,89"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"IS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:CostOfSales","value":"987.654,32"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"IS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:ProfitLoss","value":"123.456,78"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"BS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:Equity","value":"5.000.000,00"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"BS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:NumberOfShares","value":"456.000.000"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"BS","version":"as_reported","publishedAt":"2026-03-10","concept":"kap:OzkaynakDegisimTablosu","value":"42.000,00"},{"fiscalYear":2024,"periodType":"FY","periodEndDate":"2024-12-31","statementType":"IS","version":"as_reported","publishedAt":"2025-03-10","concept":"tr:Hasilat","value":"1.100.000,00"},{"fiscalYear":2024,"periodType":"FY","periodEndDate":"2024-12-31","statementType":"IS","version":"as_reported","publishedAt":"2025-03-10","concept":"ifrs-full:ProfitLoss","value":"100.000,00"}]}""";
    private const string StatementsThyaoJson = """{"companyCode":"THYAO","unitScale":"units","currency":"TRY","facts":[{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"IS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:Revenue","value":"2.345.678.901,23"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"IS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:CostOfSales","value":"1.900.000.000,00"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"IS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:ProfitLoss","value":"250.000.000,00"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"BS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:Equity","value":"900.000.000,00"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"BS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:NumberOfShares","value":"1.380.000.000"},{"fiscalYear":2024,"periodType":"FY","periodEndDate":"2024-12-31","statementType":"IS","version":"as_reported","publishedAt":"2025-03-10","concept":"ifrs-full:Revenue","value":"2.000.000.000,00"},{"fiscalYear":2024,"periodType":"FY","periodEndDate":"2024-12-31","statementType":"IS","version":"as_reported","publishedAt":"2025-03-10","concept":"ifrs-full:ProfitLoss","value":"200.000.000,00"}]}""";
    private const string StatementsGaranJson = """{"companyCode":"GARAN","unitScale":"units","currency":"TRY","facts":[{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"IS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:Revenue","value":"3.456.789.012,34"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"IS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:ProfitLoss","value":"456.789.012,34"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"BS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:Equity","value":"4.200.000.000,00"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"BS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:NumberOfShares","value":"4.200.000.000"},{"fiscalYear":2024,"periodType":"FY","periodEndDate":"2024-12-31","statementType":"IS","version":"as_reported","publishedAt":"2025-03-10","concept":"ifrs-full:Revenue","value":"3.000.000.000,00"},{"fiscalYear":2024,"periodType":"FY","periodEndDate":"2024-12-31","statementType":"IS","version":"as_reported","publishedAt":"2025-03-10","concept":"ifrs-full:ProfitLoss","value":"400.000.000,00"}]}""";
    private const string StatementsFrotoJson = """{"companyCode":"FROTO","unitScale":"units","currency":"TRY","facts":[{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"IS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:Revenue","value":"4.567.890.123,45"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"IS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:ProfitLoss","value":"567.890.123,45"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"BS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:Equity","value":"3.509.100.000,00"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"BS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:NumberOfShares","value":"350.910.000"},{"fiscalYear":2024,"periodType":"FY","periodEndDate":"2024-12-31","statementType":"IS","version":"as_reported","publishedAt":"2025-03-10","concept":"ifrs-full:Revenue","value":"4.000.000.000,00"},{"fiscalYear":2024,"periodType":"FY","periodEndDate":"2024-12-31","statementType":"IS","version":"as_reported","publishedAt":"2025-03-10","concept":"ifrs-full:ProfitLoss","value":"500.000.000,00"}]}""";
    private const string StatementsEbebkJson = """{"companyCode":"EBEBK","unitScale":"units","currency":"TRY","facts":[{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"IS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:Revenue","value":"5.678.901.234,56"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"IS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:ProfitLoss","value":"67.890.123,45"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"BS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:Equity","value":"160.000.000,00"},{"fiscalYear":2025,"periodType":"FY","periodEndDate":"2025-12-31","statementType":"BS","version":"as_reported","publishedAt":"2026-03-10","concept":"ifrs-full:NumberOfShares","value":"160.000.000"},{"fiscalYear":2024,"periodType":"FY","periodEndDate":"2024-12-31","statementType":"IS","version":"as_reported","publishedAt":"2025-03-10","concept":"ifrs-full:Revenue","value":"5.000.000.000,00"},{"fiscalYear":2024,"periodType":"FY","periodEndDate":"2024-12-31","statementType":"IS","version":"as_reported","publishedAt":"2025-03-10","concept":"ifrs-full:ProfitLoss","value":"50.000.000,00"}]}""";
    private const string LegacyWindows1254Base64 = "QVNFTFNBTiBFTEVLVFJPTt1LIFNBTkFZ3SBWRSBU3UNBUkVUIEEu3i4KRPZuZW06IDIwMjUvMTIgLSBLYXBhbv3+OiAzNzYsMDAgVEwgLSBIYWNpbTogMS4yMzQuNTY3LDg5IC0gRGXwaf5pbTogJSsyLDE1Ct5pcmtldCBr4nL9IHZlIPZ6a2F5bmFrOyD98Pz+9ufd0Nze1sc=";

    /// <summary>
    /// The real KAP XBRL concept → canonical chart-of-accounts mapping (<c>02</c> §6.1).
    /// The catalog deliberately carries <em>inconsistent</em> tag spellings for the same
    /// canonical line — <c>ifrs-full:Revenue</c> and the Turkish-taxonomy
    /// <c>tr:Hasilat</c> — so the parse collapses both onto <c>REV</c>.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> XbrlConceptMap =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ifrs-full:Revenue"] = FinItemCodes.Rev,
            ["tr:Hasilat"] = FinItemCodes.Rev,
            ["kap:NetSatisTutari"] = FinItemCodes.Rev,
            ["ifrs-full:CostOfSales"] = FinItemCodes.Cogs,
            ["ifrs-full:GrossProfit"] = FinItemCodes.GrossProfit,
            ["ifrs-full:ProfitLoss"] = FinItemCodes.Ni,
            ["ifrs-full:Equity"] = FinItemCodes.TotalEquity,
            ["ifrs-full:NumberOfShares"] = FinItemCodes.SharesDiluted,
            ["ifrs-full:CashAndCashEquivalents"] = FinItemCodes.Cash,
        };

    private static readonly JsonSerializerOptions CanonicalJson = new(JsonSerializerDefaults.Web);

    static RealSourceCatalog()
    {
        // windows-1254 (and ISO-8859-9) are not part of the default .NET Core encoding
        // set, so the code-page provider must be registered before the legacy body decodes.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>Builds every named real-source payload.</summary>
    public static IReadOnlyList<RealSourcePayload> Build() =>
    [
        PricePayload(PricesAsels, PricesAselsJson),
        PricePayload(PricesThyao, PricesThyaoJson),
        PricePayload(PricesGaran, PricesGaranJson),
        PricePayload(PricesFroto, PricesFrotoJson),
        PricePayload(PricesEbebk, PricesEbebkJson),
        StatementPayload(StatementsAsels, StatementsAselsJson),
        StatementPayload(StatementsThyao, StatementsThyaoJson),
        StatementPayload(StatementsGaran, StatementsGaranJson),
        StatementPayload(StatementsFroto, StatementsFrotoJson),
        StatementPayload(StatementsEbebk, StatementsEbebkJson),
        LegacyPayload(),
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

    /// <summary>
    /// Parses a Turkish-formatted number (<c>1.234.567,89</c>): <c>.</c> is the thousands
    /// separator, <c>,</c> the decimal separator. Tolerates surrounding whitespace, a
    /// leading <c>-</c> (or accounting parentheses) and a trailing <c>%</c>. Culture-free
    /// (the host builds under <c>InvariantGlobalization</c>).
    /// </summary>
    public static decimal ParseTurkishNumber(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var value = text.Trim();
        var negative = false;
        if (value.StartsWith('(') && value.EndsWith(')'))
        {
            negative = true;
            value = value[1..^1].Trim();
        }

        if (value.StartsWith('-') || value.StartsWith('−'))
        {
            negative = true;
            value = value[1..].Trim();
        }

        if (value.EndsWith('%'))
        {
            value = value[..^1].Trim();
        }

        value = value
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\u00A0", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace(',', '.');

        var parsed = decimal.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        return negative ? -parsed : parsed;
    }

    /// <summary>
    /// Decodes an İş Yatırım EOD price payload into the canonical dated row facts. The
    /// real envelope is a <c>value</c> array with <c>HGDG_*</c> fields; close is read
    /// from <c>HGDG_KAPANIS</c>, falling back to the alternate <c>HG_KAPANIS</c> spelling
    /// the source also emits.
    /// </summary>
    public static IReadOnlyList<DatedPriceFact> DecodePriceRows(RealSourcePayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        using var document = JsonDocument.Parse(payload.DecodeRawText());
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("value", out var items))
        {
            throw new ArgumentException($"Payload '{payload.Name}' is not an İş Yatırım EOD envelope.", nameof(payload));
        }

        var rows = new List<DatedPriceFact>();
        foreach (var item in items.EnumerateArray())
        {
            var symbol = item.GetProperty("HGDG_HS_KODU").GetString()!;
            var date = DateOnly.ParseExact(
                item.GetProperty("HGDG_TARIH").GetString()!,
                "dd-MM-yyyy",
                CultureInfo.InvariantCulture);
            rows.Add(new DatedPriceFact(
                symbol,
                date,
                Open: null,
                High: ReadDecimal(item, "HGDG_MAX", "HG_MAX"),
                Low: ReadDecimal(item, "HGDG_MIN", "HG_MIN"),
                Close: ReadDecimal(item, "HGDG_KAPANIS", "HG_KAPANIS"),
                Volume: ReadVolume(item, "HGDG_HACIM", "HG_HACIM")));
        }

        return rows;
    }

    /// <summary>Wraps decoded İş Yatırım rows in the canonical history payload the adapter parses.</summary>
    public static PricesHistoryPayload BuildPricesHistoryPayload(RealSourcePayload payload) =>
        new(IsYatirimSource, DecodePriceRows(payload));

    /// <summary>
    /// Decodes a KAP XBRL fact payload into canonical statement facts: maps the (possibly
    /// inconsistent) source concept tags onto the chart of accounts, parses Turkish-formatted
    /// values and applies the shared <see cref="StatementMapper"/> (derivation + passthrough).
    /// </summary>
    public static IReadOnlyList<StatementFact> DecodeStatementFacts(RealSourcePayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        using var document = JsonDocument.Parse(payload.DecodeRawText());
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("facts", out var facts))
        {
            throw new ArgumentException($"Payload '{payload.Name}' is not a KAP XBRL fact envelope.", nameof(payload));
        }

        var groups = new List<StatementGroup>();
        foreach (var fact in facts.EnumerateArray())
        {
            var concept = fact.GetProperty("concept").GetString()!;
            var code = XbrlConceptMap.TryGetValue(concept, out var mapped) ? mapped : $"OTHER_{concept}";
            var value = ParseTurkishNumber(fact.GetProperty("value").GetString()!);

            var key = string.Join(
                '|',
                fact.GetProperty("periodType").GetString(),
                fact.GetProperty("periodEndDate").GetString(),
                fact.GetProperty("statementType").GetString(),
                fact.GetProperty("version").GetString());

            var group = groups.FirstOrDefault(g => string.Equals(g.Key, key, StringComparison.Ordinal));
            if (group is null)
            {
                group = new StatementGroup(key, fact);
                groups.Add(group);
            }

            group.Lines[code] = value;
        }

        return groups.Select(ToStatementFact).ToList();
    }

    /// <summary>Wraps decoded KAP facts in the canonical statements payload the adapter parses.</summary>
    public static StatementsPayload BuildStatementsPayload(RealSourcePayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        using var document = JsonDocument.Parse(payload.DecodeRawText());
        var root = document.RootElement;
        var symbol = root.GetProperty("companyCode").GetString()!;
        var unitScale = root.TryGetProperty("unitScale", out var unit) ? unit.GetString() : null;
        var currency = root.TryGetProperty("currency", out var cur) ? cur.GetString() : null;

        return new StatementsPayload(
            $"kap://statements/{symbol}",
            symbol,
            unitScale,
            currency,
            DecodeStatementFacts(payload));
    }

    private static RealSourcePayload PricePayload(string name, string json)
    {
        var raw = Encoding.UTF8.GetBytes(json);
        var provisional = new RealSourcePayload(
            name,
            IsYatirimSource,
            "GET",
            $"/real-sources/{name}",
            200,
            "utf-8",
            raw,
            string.Empty);
        var body = JsonSerializer.Serialize(BuildPricesHistoryPayload(provisional), CanonicalJson);
        return provisional with { Body = body };
    }

    private static RealSourcePayload StatementPayload(string name, string json)
    {
        var raw = Encoding.UTF8.GetBytes(json);
        var provisional = new RealSourcePayload(
            name,
            KapSource,
            "GET",
            $"/real-sources/{name}",
            200,
            "utf-8",
            raw,
            string.Empty);
        var body = JsonSerializer.Serialize(BuildStatementsPayload(provisional), CanonicalJson);
        return provisional with { Body = body };
    }

    private static RealSourcePayload LegacyPayload()
    {
        var raw = Convert.FromBase64String(LegacyWindows1254Base64);
        var text = Decode(raw, "windows-1254");
        return new RealSourcePayload(
            LegacyWindows1254,
            IsYatirimSource,
            "GET",
            $"/real-sources/{LegacyWindows1254}",
            200,
            "windows-1254",
            raw,
            text);
    }

    private static decimal? ReadDecimal(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (item.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number)
            {
                return property.GetDecimal();
            }
        }

        return null;
    }

    private static long? ReadVolume(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (item.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number)
            {
                return (long)decimal.Round(property.GetDecimal(), MidpointRounding.AwayFromZero);
            }
        }

        return null;
    }

    private static StatementFact ToStatementFact(StatementGroup group)
    {
        var mapped = StatementMapper.Map(group.Lines);
        return new StatementFact(
            group.PeriodType,
            group.PeriodEndDate,
            group.FiscalYear,
            group.StatementType,
            group.Version,
            group.RestatementDate,
            group.PublishedAt,
            mapped);
    }

    /// <summary>
    /// Accumulates one statement period's canonical lines as its XBRL facts are read.
    /// The latest fact for a concept wins (a restated value overwrites the earlier tag).
    /// </summary>
    private sealed class StatementGroup
    {
        public StatementGroup(string key, JsonElement firstFact)
        {
            Key = key;
            PeriodType = firstFact.GetProperty("periodType").GetString()!;
            PeriodEndDate = DateOnly.ParseExact(
                firstFact.GetProperty("periodEndDate").GetString()!,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture);
            FiscalYear = firstFact.TryGetProperty("fiscalYear", out var fy) && fy.ValueKind == JsonValueKind.Number
                ? fy.GetInt32()
                : null;
            StatementType = firstFact.GetProperty("statementType").GetString()!;
            Version = firstFact.GetProperty("version").GetString()!;
            RestatementDate = ReadDate(firstFact, "restatementDate");
            PublishedAt = ReadDate(firstFact, "publishedAt");
        }

        public string Key { get; }

        public string PeriodType { get; }

        public DateOnly PeriodEndDate { get; }

        public int? FiscalYear { get; }

        public string StatementType { get; }

        public string Version { get; }

        public DateOnly? RestatementDate { get; }

        public DateOnly? PublishedAt { get; }

        public Dictionary<string, decimal> Lines { get; } = new(StringComparer.Ordinal);

        private static DateOnly? ReadDate(JsonElement fact, string name) =>
            fact.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? DateOnly.ParseExact(value.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                : null;
    }
}
