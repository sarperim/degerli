using System.Text.Json;
using System.Text.Json.Serialization;
using Degerli.Ingestion.Kap;

namespace Degerli.Ingestion.CorporateActions;

/// <summary>The KAP corporate-action payload (FU §10 <c>corporate-actions-ok.json</c>,
/// FR-MDF-004).</summary>
public sealed record CorporateActionsPayload(
    [property: JsonPropertyName("sourceRef")] string? SourceRef,
    [property: JsonPropertyName("actions")] IReadOnlyList<CorporateActionFact> Actions) : IKapPayload;

/// <summary>One corporate action: type, date and the free-form terms (ratio/terms JSON).</summary>
public sealed record CorporateActionFact(
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("actionType")] string ActionType,
    [property: JsonPropertyName("actionDate")] DateOnly ActionDate,
    [property: JsonPropertyName("terms")] JsonElement Terms);

/// <summary>Result of one corporate-action batch.</summary>
public sealed record CorporateActionWriteResult(
    int Inserted,
    int Unchanged,
    IReadOnlyList<string> UnknownSymbols)
{
    public static readonly CorporateActionWriteResult Empty = new(0, 0, []);
}
