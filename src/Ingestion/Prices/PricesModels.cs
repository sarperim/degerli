using System.Text.Json.Serialization;

namespace Degerli.Ingestion.Prices;

/// <summary>
/// The İşbank candidate EOD price payload (FU §10 <c>prices-*.json</c>). The top-level
/// <see cref="SourceRef"/> is the provenance carried onto every stored fact (FR-MDF-008);
/// its absence is the missing-provenance class (TC-MDF-008).
/// </summary>
public sealed record PricesPayload(
    [property: JsonPropertyName("sourceRef")] string? SourceRef,
    [property: JsonPropertyName("date")] DateOnly Date,
    [property: JsonPropertyName("items")] IReadOnlyList<PriceFact> Items);

/// <summary>One instrument's EOD row within a payload.</summary>
public sealed record PriceFact(
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("previousClose")] decimal? PreviousClose,
    [property: JsonPropertyName("open")] decimal? Open,
    [property: JsonPropertyName("high")] decimal? High,
    [property: JsonPropertyName("low")] decimal? Low,
    [property: JsonPropertyName("close")] decimal? Close,
    [property: JsonPropertyName("volume")] long? Volume);

/// <summary>A fact refused by the fact-storage invariants, with its reason code.</summary>
public sealed record RejectedPriceFact(PriceFact Fact, string ReasonCode);

/// <summary>Result of one batch upsert: what was written, what was an idempotent
/// no-op, and what was refused (conflicts and structural rejects).</summary>
public sealed record PriceWriteResult(
    int Inserted,
    int Unchanged,
    IReadOnlyList<PriceFact> Conflicts,
    IReadOnlyList<RejectedPriceFact> Rejected)
{
    public static readonly PriceWriteResult Empty = new(0, 0, [], []);
}
