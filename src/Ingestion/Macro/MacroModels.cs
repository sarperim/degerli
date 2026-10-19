using System.Text.Json.Serialization;

namespace Degerli.Ingestion.Macro;

/// <summary>
/// Source wiring for the macro jobs (config section <c>Ingestion:Macro</c>). In production
/// the base address points at the <b>TCMB EVDS 3.0</b> platform
/// (<c>https://evds3.tcmb.gov.tr</c>, 01 v1.5.6 §10.9.12) and <see cref="Path"/> is the
/// platform's relative values route; the additive <see cref="CatalogPath"/> /
/// <see cref="SerieListPath"/> keys drive catalog-driven series-code discovery. In L2 tests
/// it points at the WireMock source double and <see cref="Path"/> selects a named canned
/// payload (the legacy single-GET canonical double), so the golden lane is untouched.
/// </summary>
public sealed class MacroSourceOptions
{
    public const string SectionName = "Ingestion:Macro";

    /// <summary>The DI key of the macro-bound source client (prices/TEFAS keep their own).</summary>
    public const string ClientKey = "macro";

    /// <summary>Base address of the macro source (empty → adapter must be configured).</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Path of the macro resource relative to <see cref="BaseUrl"/>. The EVDS 3.0 platform's
    /// <b>values</b> route (<c>/igmevdsms-dis/fe</c>, a <c>POST</c>) is the production value;
    /// the legacy canonical L2 double overrides it with its own GET route (so the golden lane
    /// is unchanged). Mirrors the İş Yatırım universe adapter's "production sets the real
    /// route, bare L2 compositions keep the double" posture (01 v1.5 §10.9.8).
    /// </summary>
    public string Path { get; set; } = "/igmevdsms-dis/fe";

    /// <summary>
    /// The anonymous EVDS 3.0 <b>catalog</b> route used for series-code discovery
    /// (<c>/igmevdsms-dis/categories/withDatagroups/type=json</c>, 01 v1.5.6 §10.9.12).
    /// <b>Empty disables v3 discovery</b> and the adapter falls back to the legacy single-GET
    /// canonical-double fetch (the L2 golden lane); the shipped <c>appsettings.json</c> sets it.
    /// </summary>
    public string CatalogPath { get; set; } = string.Empty;

    /// <summary>
    /// The anonymous EVDS 3.0 <b>serieList</b> route (<c>/igmevdsms-dis/serieList/fe/type=json&amp;code={code}</c>,
    /// 01 v1.5.6 §10.9.12); <c>{code}</c> is substituted with a datagroup code. Empty disables
    /// v3 discovery (see <see cref="CatalogPath"/>).
    /// </summary>
    public string SerieListPath { get; set; } = string.Empty;

    /// <summary>
    /// The optional TCMB EVDS API key (OQ-MOV-005 resolution). Live verification established
    /// that the EVDS 3.0 <c>/fe</c> values endpoint is <b>anonymous</b> — no key, no login
    /// (01 v1.5.6 §10.9.12) — so the v3 request carries <b>no credential</b>; the key is kept
    /// only as an optional redaction surface (defence in depth) and, for the legacy
    /// canonical-double GET, as the <c>key</c> query parameter. Empty default; never committed
    /// — supplied through the environment (<c>Ingestion__Macro__EvdsApiKey</c>).
    /// </summary>
    public string EvdsApiKey { get; set; } = string.Empty;

    /// <summary>
    /// True when catalog-driven v3 discovery is configured (both <see cref="CatalogPath"/> and
    /// <see cref="SerieListPath"/> non-empty). When false the adapter uses the legacy
    /// single-GET canonical-double fetch, keeping the L2 golden lane unchanged.
    /// </summary>
    public bool UsesV3Discovery =>
        !string.IsNullOrWhiteSpace(CatalogPath) && !string.IsNullOrWhiteSpace(SerieListPath);

    /// <summary>Builds the per-datagroup serieList route from the templated <see cref="SerieListPath"/>.</summary>
    public string SerieListRoute(string datagroupCode) =>
        SerieListPath.Replace("{code}", Uri.EscapeDataString(datagroupCode), StringComparison.Ordinal);
}

/// <summary>
/// The macro source payload (FR-MOV-014): one fetch carries every series' published rows
/// (value, value date, unit, recorded_at). Revisions arrive as additional rows for the same
/// value date with a later <see cref="MacroItem.RecordedAt"/>.
/// </summary>
public sealed record MacroPayload(
    [property: JsonPropertyName("sourceRef")] string? SourceRef,
    [property: JsonPropertyName("series")] IReadOnlyList<MacroItem>? Series);

/// <summary>One published macro value.</summary>
public sealed record MacroItem(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("valueDate")] DateOnly ValueDate,
    [property: JsonPropertyName("value")] decimal Value,
    [property: JsonPropertyName("unit")] string? Unit,
    [property: JsonPropertyName("recordedAt")] DateTimeOffset? RecordedAt);

/// <summary>
/// The canonical TCMB EVDS envelope (OQ-MOV-005) — the L2 golden source-double shape
/// (01 v1.5 §10.9.7): one fetch carries every requested series' published observations in a
/// row list. Each observation names its EVDS <see cref="EvdsObservation.SeriesCode"/> and
/// (for the synthetic fixture) the canonical <see cref="EvdsObservation.Code"/>; the adapter
/// maps the EVDS identifier to the canonical code. Revisions arrive as additional
/// observations for the same value date with a later <see cref="EvdsObservation.RecordedAt"/>.
/// The <b>real</b> EVDS envelope is the column-oriented <c>items</c>/<c>Tarih</c> shape
/// decoded by <see cref="EvdsMacroDecoder"/>; both parse through the one production adapter.
/// </summary>
public sealed record EvdsMacroPayload(
    [property: JsonPropertyName("sourceRef")] string? SourceRef,
    [property: JsonPropertyName("series")] IReadOnlyList<EvdsObservation>? Series);

/// <summary>One EVDS observation.</summary>
public sealed record EvdsObservation(
    [property: JsonPropertyName("seriesCode")] string? SeriesCode,
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("valueDate")] DateOnly ValueDate,
    [property: JsonPropertyName("value")] decimal Value,
    [property: JsonPropertyName("unit")] string? Unit,
    [property: JsonPropertyName("recordedAt")] DateTimeOffset? RecordedAt);

/// <summary>One validated macro fact ready for append-only storage.</summary>
public sealed record MacroFact(string SeriesCode, DateOnly ValueDate, decimal Value, DateTimeOffset RecordedAt);

/// <summary>The append outcome for a macro ingest (mirrors the platform's write results).</summary>
public sealed record MacroAppendResult(int Inserted, int Unchanged);

/// <summary>
/// The six V1 macro series and the job that owns each series' cadence (03 §9): the
/// <c>macro-daily</c> job carries the daily FX/gold series; <c>macro-cpi</c> carries the
/// monthly inflation pair and the per-release policy rate.
/// </summary>
public static class MacroSeries
{
    public const string TuikCpi = "TUIK_CPI";
    public const string IndepCpi = "INDEP_CPI";
    public const string CbrtRepo = "CBRT_REPO";
    public const string UsdTry = "USD_TRY";
    public const string EurTry = "EUR_TRY";
    public const string Gold = "GOLD";
}

/// <summary>
/// The TCMB EVDS 3.0 series identifiers (OQ-MOV-005 resolution on the new platform,
/// 01 v1.5.6 §10.9.12) mapped to the canonical macro series codes (02 §3.2). The single
/// EVDS adapter (FR-MOV-014) discovers these official series from the anonymous catalog +
/// serieList and maps each returned column back to its canonical code, so the jobs, the
/// store and the strip are unaffected by the source migration.
///
/// <para>
/// <b>Union mapping, not replacement (§10.9.7).</b> The L2 golden canonical double still
/// names the <em>fixture-era</em> identifiers (<c>TP.TUKFIY2025.GENEL</c>, <c>TP.APIFON1</c>,
/// <c>TP.DK.USD.A.YTL</c>, <c>TP.DK.EUR.A.YTL</c>, <c>TP.MK.ALTIN</c>); the live v3 platform
/// uses the new identifiers (<c>TP.BISPOLFAIZ.TUR</c>, <c>TP.DK.USD.S.YTL</c>,
/// <c>TP.DK.EUR.S.YTL</c>, <c>TP.ALTINPIYASA.KAP03</c>). The mapping carries <b>both</b> so
/// the fixture world keeps parsing unchanged while the live source resolves to the same
/// canonical codes — never one replacing the other.
/// </para>
/// </summary>
public static class EvdsSeries
{
    /// <summary>TÜİK CPI — <b>index level</b> (2025=100), from the <c>bie_tukfiy2025</c> datagroup.</summary>
    public const string CpiIndex = "TP.TUKFIY2025.GENEL";

    /// <summary>CBRT policy (1-week repo) rate, monthly, from the <c>bie_bispolfaiz</c> datagroup.</summary>
    public const string CbrtRepo = "TP.BISPOLFAIZ.TUR";

    /// <summary>USD/TRY <b>selling</b> (satış) rate, from the <c>bie_dkdovytl</c> datagroup.</summary>
    public const string UsdTry = "TP.DK.USD.S.YTL";

    /// <summary>EUR/TRY <b>selling</b> (satış) rate, from the <c>bie_dkdovytl</c> datagroup.</summary>
    public const string EurTry = "TP.DK.EUR.S.YTL";

    /// <summary>
    /// Gold close in <b>USD/ounce</b> (matches the seeded <c>GOLD</c> unit "USD/oz"),
    /// from the <c>bie_altinbistbul</c> datagroup ("Altın - Kapanış Fiyatı - USD/ons",
    /// BIST gold — live-verified 2026-10-19). The alternative <c>TP.ALTINPIYASA.KAP02</c>
    /// is the TL/kg close; the USD/oz variant is chosen so the stored unit stays honest.
    /// The <c>bie_mkaltytl</c> datagroup carries only the monthly Ankara Chamber of
    /// Jewelers series (<c>TP.MK.CUM/KUL/LON/RES.YTL</c>) and does <b>not</b> publish any
    /// <c>TP.ALTINPIYASA.*</c> identifier (live-verified 2026-10-19).
    /// </summary>
    public const string Gold = "TP.ALTINPIYASA.KAP03";

    /// <summary>Brent crude close, from the <c>bie_brentpetrol</c> datagroup. Carried for
    /// reference only — no V1 macro series maps to it, so the adapter does not request it.</summary>
    public const string Brent = "TP.BRENTPETROL.EUBP";

    // ---- fixture-era identifiers (kept for the L2 canonical double, §10.9.7) ----
    /// <summary>Fixture-era CBRT repo identifier (the canonical double's <c>seriesCode</c>).</summary>
    public const string FixtureCbrtRepo = "TP.APIFON1";

    /// <summary>Fixture-era USD/TRY buying identifier (the canonical double's <c>seriesCode</c>).</summary>
    public const string FixtureUsdTry = "TP.DK.USD.A.YTL";

    /// <summary>Fixture-era EUR/TRY buying identifier (the canonical double's <c>seriesCode</c>).</summary>
    public const string FixtureEurTry = "TP.DK.EUR.A.YTL";

    /// <summary>Fixture-era gold identifier (the canonical double's <c>seriesCode</c>).</summary>
    public const string FixtureGold = "TP.MK.ALTIN";

    /// <summary>
    /// EVDS series identifier → canonical code (the official five, <b>union</b> of the live v3
    /// identifiers and the fixture-era identifiers, so both the live envelope and the canonical
    /// double resolve to the same canonical series — §10.9.7).
    /// </summary>
    public static IReadOnlyDictionary<string, string> CanonicalByEvdsCode { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CpiIndex] = MacroSeries.TuikCpi,
            [CbrtRepo] = MacroSeries.CbrtRepo,
            [FixtureCbrtRepo] = MacroSeries.CbrtRepo,
            [UsdTry] = MacroSeries.UsdTry,
            [FixtureUsdTry] = MacroSeries.UsdTry,
            [EurTry] = MacroSeries.EurTry,
            [FixtureEurTry] = MacroSeries.EurTry,
            [Gold] = MacroSeries.Gold,
            [FixtureGold] = MacroSeries.Gold,
        };

    /// <summary>The live v3 EVDS identifiers, in catalog order. Carried for the legacy lane's
    /// series join; the v3 lane requests them grouped by cadence (see
    /// <see cref="Definition.Monthly"/>). The CPI index is requested separately (see
    /// <see cref="CpiIndex"/>).</summary>
    public static IReadOnlyList<string> RequestOrder { get; } =
        [UsdTry, EurTry, Gold, CbrtRepo];

    /// <summary>
    /// One live series to discover and request (01 v1.5.6 §10.9.12): the canonical macro code
    /// it resolves to, the EVDS 3.0 <b>datagroup</b> code it lives in, its live-verified
    /// <see cref="SerieCode"/> (confirmed from the datagroup's serieList, never assumed), and
    /// whether the platform publishes it at a cadence coarser than business day.
    /// <para>
    /// <see cref="Monthly"/> is a <b>request-shape</b> fact, not a discovery shortcut: the
    /// <c>/fe</c> platform collapses a mixed-frequency response to the <em>lowest common</em>
    /// frequency (live-verified 2026-10-19 — a body carrying the monthly repo alongside the
    /// daily FX/gold series returns 4 monthly rows instead of 65 business-day rows), so the
    /// monthly series must ride a request of their own.
    /// </para>
    /// </summary>
    public sealed record Definition(string CanonicalCode, string Datagroup, string SerieCode, bool Monthly = false);

    /// <summary>
    /// The series the adapter discovers from the catalog + serieList and ingests: the five
    /// official series (01 v1.5.6 §10.9.12). CPI arrives as an <b>index</b> and is converted to
    /// the canonical YoY measure by the adapter; Brent carries no V1 series so it is absent.
    /// </summary>
    public static IReadOnlyList<Definition> Requested { get; } =
    [
        new(MacroSeries.UsdTry, "bie_dkdovytl", UsdTry),
        new(MacroSeries.EurTry, "bie_dkdovytl", EurTry),
        new(MacroSeries.Gold, "bie_altinbistbul", Gold),
        new(MacroSeries.CbrtRepo, "bie_bispolfaiz", CbrtRepo, Monthly: true),
        new(MacroSeries.TuikCpi, "bie_tukfiy2025", CpiIndex),
    ];
}

/// <summary>
/// One EVDS 3.0 <c>/fe</c> serieList entry (01 v1.5.6 §10.9.12): the discovery response that
/// resolves a datagroup's series to their concrete codes and default aggregation methods.
/// Only the fields the adapter reads are modelled; unknown fields are ignored.
/// </summary>
public sealed record EvdsSerieInfo(
    [property: JsonPropertyName("SERIE_CODE")] string? SerieCode,
    [property: JsonPropertyName("DATAGROUP_CODE")] string? DatagroupCode,
    [property: JsonPropertyName("SERIE_NAME")] string? SerieName,
    [property: JsonPropertyName("DEFAULT_AGG_METHOD")] string? DefaultAggMethod);

/// <summary>
/// The EVDS 3.0 <c>/fe</c> values request body, verbatim from the live-captured request
/// (01 v1.5.6 §10.9.12): <c>series</c>/<c>aggregationTypes</c>/<c>formulas</c> are
/// <c>-</c>-joined per selected series; <c>frequency</c> "2" = business day (the site's
/// default); <c>dateFormat</c>/<c>yon</c>/<c>sira</c> are numeric codes ("0"); dates are
/// <c>dd-MM-yyyy</c>. No credential travels in the body — the endpoint is anonymous.
/// </summary>
public sealed record EvdsV3Request(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("series")] string Series,
    [property: JsonPropertyName("aggregationTypes")] string AggregationTypes,
    [property: JsonPropertyName("formulas")] string Formulas,
    [property: JsonPropertyName("startDate")] string StartDate,
    [property: JsonPropertyName("endDate")] string EndDate,
    [property: JsonPropertyName("frequency")] string Frequency,
    [property: JsonPropertyName("decimalSeperator")] string DecimalSeperator,
    [property: JsonPropertyName("decimal")] string Decimal,
    [property: JsonPropertyName("dateFormat")] string DateFormat,
    [property: JsonPropertyName("lang")] string Lang,
    [property: JsonPropertyName("yon")] string Yon,
    [property: JsonPropertyName("sira")] string Sira,
    [property: JsonPropertyName("ozelFormuller")] IReadOnlyList<string> OzelFormuller,
    [property: JsonPropertyName("groupSeperator")] bool GroupSeperator,
    [property: JsonPropertyName("isRaporSayfasi")] bool IsRaporSayfasi);

/// <summary>
/// The failure classes FR-MOV-015 requires the builder alert to distinguish.
/// </summary>
public static class MacroFailureReason
{
    /// <summary>The source could not be reached (non-success response).</summary>
    public const string Unreachable = "UNREACHABLE";

    /// <summary>The source returned a body that could not be parsed or carried no provenance.</summary>
    public const string Invalid = "INVALID";

    /// <summary>An expected series is missing from a successfully-fetched payload.</summary>
    public const string Late = "LATE";
}
