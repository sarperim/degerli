using System.Globalization;
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
        ReadIndex(element, "disclosureIndex"),
        ReadString(element, "disclosureType"),
        ReadString(element, "disclosureClass"),
        ReadString(element, "title"),
        ReadString(element, "companyId"));

    /// <summary>
    /// Reads the disclosure index, tolerating a numeric JSON value: the gateway sends a string,
    /// but <see cref="VykDisclosureList.Parse"/> accepts a numeric index too — the two must agree,
    /// or an accepted row would arrive with a null index and be skipped (a fabricated empty
    /// success). A number is normalized to its invariant string form.
    /// </summary>
    private static string? ReadIndex(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt64(out var index) =>
                index.ToString(CultureInfo.InvariantCulture),
            _ => null,
        };
    }

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
/// One security line of a VYK <c>/memberSecurities</c> member (01 v1.5 §10.9.11). The real
/// per-line shape (live-verified 2026-10-22, dev gateway) is
/// <c>isin, isinDesc, borsaKodu, takasKodu, tertipGroup, capital, currentCapital, groupCode,
/// groupCodeDesc, borsadaIslemeAcik</c>: the BIST trading code is <c>borsaKodu</c> (a member's
/// several tertips share it, e.g. ASELS A+B), <c>isin</c> is the fallback identity, and
/// <c>borsadaIslemeAcik</c> flags a line that is actually trading. <see cref="CurrentCapital"/> is
/// the value the share-count store carries — the unit interpretation is live-cross-validated by
/// the ticket's pinned criterion, never assumed here.
/// </summary>
public sealed record SecurityLineInfo(
    string? Isin,
    string? IsinDescription,
    string? BorsaKodu,
    string? TakasKodu,
    string? TertipGroup,
    string? GroupCode,
    string? GroupCodeDescription,
    decimal? Capital,
    decimal? CurrentCapital,
    bool TradingOpen)
{
    /// <summary>Reads one line, tolerating numeric values sent as strings and a string flag.</summary>
    public static SecurityLineInfo From(JsonElement element) => new(
        ReadString(element, "isin"),
        ReadString(element, "isinDesc"),
        ReadString(element, "borsaKodu"),
        ReadString(element, "takasKodu"),
        ReadString(element, "tertipGroup"),
        ReadString(element, "groupCode"),
        ReadString(element, "groupCodeDesc"),
        ReadDecimal(element, "capital"),
        ReadDecimal(element, "currentCapital"),
        ReadBoolean(element, "borsadaIslemeAcik"));

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// A decimal that may arrive as a JSON number (the real shape) or a dotted string (a gateway
    /// variant). An absent/null member is <c>null</c> — an unreadable value is <b>not</b> a zero.
    /// </summary>
    private static decimal? ReadDecimal(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
            JsonValueKind.String when decimal.TryParse(
                value.GetString(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => null,
        };
    }

    /// <summary>
    /// The trading-open flag. The 2026-10-22 dev-gateway capture carried JSON booleans only
    /// (1,062/1,062), so the boolean form is the real shape; the decoder is nonetheless tolerant
    /// of the numeric <c>0</c>/<c>1</c> form and of the string spellings <c>"true"</c>/<c>"false"</c>
    /// (any casing) and <c>"1"</c>/<c>"0"</c>, because a single unrecognised spelling would silently
    /// read as <b>closed</b> and drop the line's whole member from the store (a fabricated empty
    /// success). Any other value — including a missing member — reads closed.
    /// </summary>
    private static bool ReadBoolean(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number == 1m,
            JsonValueKind.String => ReadBooleanString(value.GetString()),
            _ => false,
        };
    }

    private static bool ReadBooleanString(string? text)
    {
        var trimmed = text?.Trim();
        if (bool.TryParse(trimmed, out var parsed))
        {
            return parsed;
        }

        return trimmed == "1";
    }
}

/// <summary>
/// One member of the VYK <c>/memberSecurities</c> response: its security lines. The response is a
/// list of these — 671 members / 1,062 lines on the live 2026-10-22 dev gateway — and a member may
/// carry <b>no</b> securities at all (the codeless auditor-type/financing members), which decodes
/// to an empty line list, never a fabricated line. The <c>member</c> wrapper's identity is
/// deliberately not carried: matching uses the lines' own <c>borsaKodu</c>/<c>isin</c>, so no
/// decoded field rides unused.
/// </summary>
public sealed record MemberSecuritiesInfo(IReadOnlyList<SecurityLineInfo> Securities)
{
    public static MemberSecuritiesInfo From(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return new MemberSecuritiesInfo([]);
        }

        var securities = new List<SecurityLineInfo>();
        if (element.TryGetProperty("securities", out var lines) && lines.ValueKind == JsonValueKind.Array)
        {
            foreach (var line in lines.EnumerateArray())
            {
                securities.Add(SecurityLineInfo.From(line));
            }
        }

        return new MemberSecuritiesInfo(securities);
    }
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
