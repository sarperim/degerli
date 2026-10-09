using System.Text.Json.Serialization;
using Degerli.Ingestion.Kap;

namespace Degerli.Ingestion.Disclosures;

/// <summary>The KAP disclosure payload (FU §10 <c>disclosures-ok.json</c>, FR-MDF-005).</summary>
public sealed record DisclosuresPayload(
    [property: JsonPropertyName("sourceRef")] string? SourceRef,
    [property: JsonPropertyName("disclosures")] IReadOnlyList<DisclosureFact> Disclosures) : IKapPayload;

/// <summary>One disclosure's metadata. Documents live on disk outside the DB; the stored
/// row carries only <c>document_path</c>.</summary>
public sealed record DisclosureFact(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("disclosureType")] string? DisclosureType,
    [property: JsonPropertyName("publishDate")] DateOnly? PublishDate,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("sourceUrl")] string? SourceUrl);

/// <summary>Result of one disclosure batch.</summary>
public sealed record DisclosureWriteResult(
    int Inserted,
    int Unchanged,
    IReadOnlyList<string> UnknownSymbols)
{
    public static readonly DisclosureWriteResult Empty = new(0, 0, []);
}
