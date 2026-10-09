using System.Text.Json.Serialization;
using Degerli.Ingestion.Kap;

namespace Degerli.Ingestion.Dividends;

/// <summary>The KAP dividend payload (FU §10 <c>dividends-ok.json</c>, FR-MDF-003).</summary>
public sealed record DividendsPayload(
    [property: JsonPropertyName("sourceRef")] string? SourceRef,
    [property: JsonPropertyName("dividends")] IReadOnlyList<DividendFact> Dividends) : IKapPayload;

/// <summary>One dividend record: amount per share and the relevant dates.</summary>
public sealed record DividendFact(
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("exDate")] DateOnly ExDate,
    [property: JsonPropertyName("payDate")] DateOnly? PayDate,
    [property: JsonPropertyName("amountPerShare")] decimal AmountPerShare,
    [property: JsonPropertyName("currency")] string? Currency);

/// <summary>Result of one dividend batch: rows written, idempotent no-ops, and symbols
/// that could not be matched to an instrument.</summary>
public sealed record DividendWriteResult(
    int Inserted,
    int Unchanged,
    IReadOnlyList<string> UnknownSymbols)
{
    public static readonly DividendWriteResult Empty = new(0, 0, []);
}
