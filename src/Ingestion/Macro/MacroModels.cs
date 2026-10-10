using System.Text.Json.Serialization;

namespace Degerli.Ingestion.Macro;

/// <summary>
/// Source wiring for the macro jobs (config section <c>Ingestion:Macro</c>). In production
/// the base address points at the macro source facade (TÜİK/ENAG/CBRT/FX/gold); in L2 tests
/// it points at the WireMock source double and the path selects a named canned payload.
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
