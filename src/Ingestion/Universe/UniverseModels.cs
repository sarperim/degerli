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

/// <summary>
/// One sector reference row as published by the universe source (FR-MDF-006; 02 §3.1):
/// bilingual labels and an optional parent code for the 2-level sector→industry
/// hierarchy. Sector rows are reference data, upserted idempotently on their natural
/// key (<see cref="Code"/>).
/// </summary>
public sealed record UniverseSector(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("nameTr")] string? NameTr,
    [property: JsonPropertyName("nameEn")] string? NameEn,
    [property: JsonPropertyName("parentCode")] string? ParentCode);

/// <summary>
/// One index reference row as published by the universe source (FR-MDF-006; 02 §3.1):
/// index code plus bilingual labels. Upserted idempotently on its natural key
/// (<see cref="Code"/>).
/// </summary>
public sealed record UniverseIndex(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("nameTr")] string? NameTr,
    [property: JsonPropertyName("nameEn")] string? NameEn);

/// <summary>One constituent add/remove within a universe change set (FR-MDF-007).</summary>
public sealed record MembershipChange(
    [property: JsonPropertyName("indexCode")] string IndexCode,
    [property: JsonPropertyName("symbol")] string Symbol);

/// <summary>
/// A <c>universe-sync</c> source payload (FR-MDF-006/007). The same resource shape
/// carries sector/index reference data (<see cref="Sectors"/>/<see cref="Indices"/>)
/// alongside either a full instrument snapshot or an effective-dated membership change
/// set (<see cref="EffectiveDate"/> + <see cref="Add"/>/<see cref="Remove"/>).
/// </summary>
public sealed record UniversePayload(
    [property: JsonPropertyName("sourceRef")] string? SourceRef,
    [property: JsonPropertyName("effectiveDate")] DateOnly? EffectiveDate,
    [property: JsonPropertyName("sectors")] IReadOnlyList<UniverseSector>? Sectors,
    [property: JsonPropertyName("indices")] IReadOnlyList<UniverseIndex>? Indices,
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
