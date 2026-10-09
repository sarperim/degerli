using System.Text.Json;

namespace Degerli.Ingestion.Validation;

/// <summary>One item refused by payload-schema validation: its reason code and the
/// retained item JSON (the <c>quarantined_facts.payload_json</c> value).</summary>
public sealed record ValidationFailure(string ReasonCode, string PayloadJson);

/// <summary>
/// The reusable raw-payload schema check (C3a validation, FR-MDF-012). A payload item
/// whose declared numeric field carries a non-numeric (or absent) value is a
/// <see cref="QuarantineReason.SchemaMismatch"/>: the item is quarantined with its
/// payload retained, never written. Every adapter whose source delivers values as free
/// text (e.g. statement line items) validates through this seam before mapping to facts.
/// </summary>
public interface IPayloadSchemaValidator
{
    /// <summary>
    /// Validates that every item in the payload's array property named
    /// <paramref name="itemsProperty"/> has a JSON number for each of
    /// <paramref name="numericFields"/>. Returns one failure per offending item.
    /// </summary>
    IReadOnlyList<ValidationFailure> ValidateNumericFields(
        string payloadJson,
        string itemsProperty,
        IReadOnlyList<string> numericFields);
}

/// <summary>JSON implementation of <see cref="IPayloadSchemaValidator"/>. Items are
/// emitted as their raw JSON so the quarantine payload is inspectable and re-ingestable
/// after a fix (BR-MDF-007).</summary>
public sealed class JsonPayloadSchemaValidator : IPayloadSchemaValidator
{
    /// <inheritdoc />
    public IReadOnlyList<ValidationFailure> ValidateNumericFields(
        string payloadJson,
        string itemsProperty,
        IReadOnlyList<string> numericFields)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemsProperty);
        ArgumentNullException.ThrowIfNull(numericFields);

        var failures = new List<ValidationFailure>();

        using var document = JsonDocument.Parse(payloadJson);
        if (!document.RootElement.TryGetProperty(itemsProperty, out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return failures;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (numericFields.All(field => IsNumber(item, field)))
            {
                continue;
            }

            failures.Add(new ValidationFailure(QuarantineReason.SchemaMismatch, item.GetRawText()));
        }

        return failures;
    }

    private static bool IsNumber(JsonElement item, string field) =>
        item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty(field, out var value)
        && value.ValueKind == JsonValueKind.Number;
}
