using System.Text.Json.Serialization;

namespace Degerli.Ingestion.Universe;

/// <summary>
/// One instrument as published by the universe source (FU §10 <c>universe-*.json</c>).
/// <see cref="SectorCode"/> null (or unknown) is the missing-classification class
/// (UC-MDF-003 alternate a, TC-MDF-025).
/// </summary>
public sealed record UniverseInstrument(
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("sectorCode")] string? SectorCode,
    [property: JsonPropertyName("listingDate")] DateOnly? ListingDate);

/// <summary>One constituent add/remove within a universe change set (FR-MDF-007).</summary>
public sealed record MembershipChange(
    [property: JsonPropertyName("indexCode")] string IndexCode,
    [property: JsonPropertyName("symbol")] string Symbol);

/// <summary>
/// A <c>universe-sync</c> source payload (FR-MDF-006/007). The same resource shape
/// carries either a full instrument snapshot (instruments only) or an effective-dated
/// membership change set (<see cref="EffectiveDate"/> + <see cref="Add"/>/<see cref="Remove"/>).
/// </summary>
public sealed record UniversePayload(
    [property: JsonPropertyName("sourceRef")] string? SourceRef,
    [property: JsonPropertyName("effectiveDate")] DateOnly? EffectiveDate,
    [property: JsonPropertyName("instruments")] IReadOnlyList<UniverseInstrument>? Instruments,
    [property: JsonPropertyName("add")] IReadOnlyList<MembershipChange>? Add,
    [property: JsonPropertyName("remove")] IReadOnlyList<MembershipChange>? Remove);

/// <summary>One index level as published by the index source (FR-MDF-006).</summary>
public sealed record IndexLevelFact(
    [property: JsonPropertyName("indexCode")] string IndexCode,
    [property: JsonPropertyName("date")] DateOnly Date,
    [property: JsonPropertyName("close")] decimal Close);

/// <summary>The <c>index-levels</c> source payload (FR-MDF-006).</summary>
public sealed record IndexLevelsPayload(
    [property: JsonPropertyName("sourceRef")] string? SourceRef,
    [property: JsonPropertyName("levels")] IReadOnlyList<IndexLevelFact>? Levels);
