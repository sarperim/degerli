using System.Text.Json;

namespace Degerli.Ingestion.Kap;

/// <summary>
/// One company in the VYK <c>/members</c> response (Şirket Listesi, 01 v1.5 §10.9.11). The
/// gateway identifies companies by <see cref="Id"/>; <see cref="StockCode"/> carries the BIST
/// ticker, which is what makes the symbol → <c>companyId</c> map possible without parsing
/// disclosure bodies. The OpenAPI schema declares the response as a single
/// <c>MemberInfo</c> object, but the service returns the full company list — the parse
/// tolerates a bare array, a wrapped array, or a single object.
/// </summary>
public sealed record MemberInfo(
    string? Id,
    string? Title,
    string? StockCode,
    string? MemberType)
{
    public static MemberInfo From(JsonElement element) => new(
        ReadString(element, "id"),
        ReadString(element, "title"),
        ReadString(element, "stockCode"),
        ReadString(element, "memberType"));

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
}

/// <summary>
/// One row of the VYK <c>/disclosures</c> response (Bildirim Listesi, 01 v1.5 §10.9.11): the
/// disclosure's identity, type/class and the owning <see cref="CompanyId"/> used by the
/// cross-company identity guard before the detail is fetched. The exact wrapping of the
/// response is undeclared (single object per the schema, list in practice), so the parse
/// tolerates both.
/// </summary>
public sealed record DisclosureInfo(
    string? DisclosureIndex,
    string? DisclosureType,
    string? DisclosureClass,
    string? Title,
    string? CompanyId)
{
    public static DisclosureInfo From(JsonElement element) => new(
        ReadString(element, "disclosureIndex"),
        ReadString(element, "disclosureType"),
        ReadString(element, "disclosureClass"),
        ReadString(element, "title"),
        ReadString(element, "companyId"));

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
}

/// <summary>
/// The Apinizer gateway's fault envelope (<c>{"fault":{"faultCode":..,"faultString":..}}</c>),
/// which is what the live gateway actually returns on an auth/throttle failure even though the
/// OpenAPI declares <c>VykErrorResponse {code,message}</c>. Both shapes are recognized so a
/// source-down fault is classified as a transport failure, never quarantined as a parse event
/// (01 v1.5 §10.9.11).
/// </summary>
public sealed record VykFault(string? Code, string? Message)
{
    /// <summary>Reads a fault from a body, or <c>null</c> when the body is not a fault envelope.</summary>
    public static VykFault? TryRead(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (root.TryGetProperty("fault", out var fault) && fault.ValueKind == JsonValueKind.Object)
        {
            return new VykFault(ReadString(fault, "faultCode"), ReadString(fault, "faultString"));
        }

        // The spec-declared VykErrorResponse {code,message} (400/500 handlers).
        if (root.TryGetProperty("code", out _) && root.TryGetProperty("message", out _))
        {
            return new VykFault(ReadString(root, "code"), ReadString(root, "message"));
        }

        return null;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

/// <summary>
/// Tolerant parsers for the VYK list-shaped responses whose array-vs-object wrapping the
/// OpenAPI leaves undeclared (01 v1.5 §10.9.11). A root array is the real shape; a single
/// object (the schema's declared shape) and a single-property wrapper object are also accepted
/// so a gateway/spec drift never turns a real body into an unparseable payload.
/// </summary>
public static class VykEnvelopes
{
    /// <summary>Enumerates the elements of a list-shaped VYK body.</summary>
    public static IEnumerable<JsonElement> Elements(JsonElement root)
    {
        switch (root.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var element in root.EnumerateArray())
                {
                    yield return element;
                }

                break;

            case JsonValueKind.Object:
                // A single object (the schema's declared shape) or a single-array wrapper.
                var arrayProperty = root.EnumerateObject()
                    .FirstOrDefault(p => p.Value.ValueKind == JsonValueKind.Array);
                if (arrayProperty.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in arrayProperty.Value.EnumerateArray())
                    {
                        yield return element;
                    }
                }
                else
                {
                    yield return root;
                }

                break;
        }
    }
}
