using System.Text.Json.Serialization;

namespace Degerli.Ingestion.Macro;

/// <summary>
/// Source wiring for the macro jobs (config section <c>Ingestion:Macro</c>). In production
/// the base address points at the TCMB EVDS API (<c>https://evds2.tcmb.gov.tr</c>) and
/// <see cref="EvdsApiKey"/> carries the free API key (03 §1); in L2 tests it points at the
/// WireMock source double and the path selects a named canned payload.
/// </summary>
public sealed class MacroSourceOptions
{
    public const string SectionName = "Ingestion:Macro";

    /// <summary>The DI key of the macro-bound source client (prices/TEFAS keep their own).</summary>
    public const string ClientKey = "macro";

    /// <summary>Base address of the macro source (empty → adapter must be configured).</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Path of the macro resource relative to <see cref="BaseUrl"/>.</summary>
    public string Path { get; set; } = "/macro";

    /// <summary>
    /// The free TCMB EVDS API key (OQ-MOV-005 resolution). Sent as the <c>key</c> query
    /// parameter on every EVDS request; empty in local/test wiring, where the WireMock
    /// double ignores it. Never committed — supplied through the environment
    /// (<c>Ingestion__Macro__EvdsApiKey</c>).
    /// </summary>
    public string EvdsApiKey { get; set; } = string.Empty;
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
/// The TCMB EVDS envelope (OQ-MOV-005): one fetch carries every requested series'
/// published observations. Each observation names its EVDS <see cref="EvdsObservation.SeriesCode"/>
/// and (for the synthetic fixture) the canonical <see cref="EvdsObservation.Code"/>; the
/// adapter maps the EVDS identifier to the canonical code. Revisions arrive as additional
/// observations for the same value date with a later <see cref="EvdsObservation.RecordedAt"/>.
/// </summary>
public sealed record EvdsMacroPayload(
    [property: JsonPropertyName("sourceRef")] string? SourceRef,
    [property: JsonPropertyName("totalCount")] int TotalCount,
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
/// The TCMB EVDS series identifiers (OQ-MOV-005) mapped to the canonical macro series codes
/// (02 §3.2). The single EVDS adapter (FR-MOV-014) requests these five official series and
/// maps each returned <c>seriesCode</c> back to its canonical code, so the jobs, the store
/// and the strip are unaffected by the source consolidation.
/// </summary>
public static class EvdsSeries
{
    /// <summary>TÜİK CPI — general index, annual % change (TÜFE).</summary>
    public const string TuikCpi = "TP.TUKFIY2025.GENEL";

    /// <summary>CBRT 1-week repo policy rate.</summary>
    public const string CbrtRepo = "TP.APIFON1";

    /// <summary>USD/TRY buying rate.</summary>
    public const string UsdTry = "TP.DK.USD.A.YTL";

    /// <summary>EUR/TRY buying rate.</summary>
    public const string EurTry = "TP.DK.EUR.A.YTL";

    /// <summary>Gold (gram/ounce, USD).</summary>
    public const string Gold = "TP.MK.ALTIN";

    /// <summary>EVDS series identifier → canonical code (the official five, in request order).</summary>
    public static IReadOnlyDictionary<string, string> CanonicalByEvdsCode { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TuikCpi] = MacroSeries.TuikCpi,
            [CbrtRepo] = MacroSeries.CbrtRepo,
            [UsdTry] = MacroSeries.UsdTry,
            [EurTry] = MacroSeries.EurTry,
            [Gold] = MacroSeries.Gold,
        };

    /// <summary>The EVDS identifiers the adapter requests, in catalog order.</summary>
    public static IReadOnlyList<string> RequestOrder { get; } =
        [TuikCpi, CbrtRepo, UsdTry, EurTry, Gold];
}

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
