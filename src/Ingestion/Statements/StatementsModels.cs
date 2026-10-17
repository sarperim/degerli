using System.Text.Json.Serialization;
using Degerli.Ingestion.Kap;

namespace Degerli.Ingestion.Statements;

/// <summary>
/// The KAP financial-statement payload (FU §10 <c>statements-*.json</c>): one instrument's
/// statement versions for a period, each carrying the source taxonomy lines that the ETL
/// maps to the canonical chart of accounts (<c>02</c> §6.1). <see cref="SourceRef"/> is the
/// provenance carried onto every stored statement (FR-MDF-008).
/// </summary>
public sealed record StatementsPayload(
    [property: JsonPropertyName("sourceRef")] string? SourceRef,
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("unitScale")] string? UnitScale,
    [property: JsonPropertyName("currency")] string? Currency,
    [property: JsonPropertyName("statements")] IReadOnlyList<StatementFact> Statements) : IKapPayload;

/// <summary>One statement version of one period, with its source-taxonomy lines.</summary>
/// <param name="ConflictingItemCodes">Canonical item codes whose source concept appeared
/// more than once with <b>unequal</b> values within this one group (§10.9.5, DR-05
/// overturned): the fact is a validation event the store quarantines as
/// <c>CONFLICTING_VALUE</c>, both values retained — never a silent latest-wins. Null/empty
/// when no such conflict exists. Not part of the wire contract (the canonical envelopes
/// carry no conflict flag).</param>
public sealed record StatementFact(
    [property: JsonPropertyName("periodType")] string PeriodType,
    [property: JsonPropertyName("periodEndDate")] DateOnly PeriodEndDate,
    [property: JsonPropertyName("fiscalYear")] int? FiscalYear,
    [property: JsonPropertyName("statementType")] string StatementType,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("restatementDate")] DateOnly? RestatementDate,
    [property: JsonPropertyName("publishedAt")] DateOnly? PublishedAt,
    [property: JsonPropertyName("lines")] IReadOnlyDictionary<string, decimal> Lines,
    [property: JsonIgnore] IReadOnlyList<string>? ConflictingItemCodes = null);

/// <summary>Result of one statement batch: statements written, idempotent no-ops, and
/// refusals (unknown instrument or an invalid period).</summary>
public sealed record StatementWriteResult(
    string? Symbol,
    int Inserted,
    int Unchanged,
    IReadOnlyList<string> RejectedReasonCodes)
{
    public static readonly StatementWriteResult Empty = new(null, 0, 0, []);
}
