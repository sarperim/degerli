using System.Text.Json;

namespace Degerli.SourceProbes.Shapes;

/// <summary>
/// Extracts the <b>structural shape</b> of a JSON body (TKT-foundation-068): every path,
/// its kind set, and whether it was ever null. Values are never carried — a shape cannot
/// leak a price, a title or a credential, which is why the report can retain it
/// (acceptance iii).
/// </summary>
/// <remarks>
/// Rules, pinned so the comparer can be read against them:
/// <list type="bullet">
/// <item>Objects contribute one path per property; properties of array elements are
/// aggregated (union) across every element, so a field missing from one row but present in
/// another is still part of the element shape.</item>
/// <item>Arrays contribute their own path plus, when they are non-empty, their element
/// path (<c>[]</c>). An <b>empty</b> array contributes no element entries — that absence is
/// the signal the comparer reports as <c>element-shape-missing</c>, which is exactly the
/// İş Yatırım empty-<c>value</c> incident (2026-10-23).</item>
/// <item><c>null</c> sets the path's <see cref="ShapePath.Nullable"/> flag and contributes
/// the <see cref="ShapeKinds.Null"/> kind; a path that is only ever null keeps just that
/// kind.</item>
/// <item>Duplicate keys and malformed bodies are rejected, not normalized: a body the
/// harness cannot parse is <i>unreachable</i>, never "clean".</item>
/// </list>
/// </remarks>
public static class JsonShapeExtractor
{
    /// <summary>The kinds a JSON value contributes, ignoring null (tracked separately).</summary>
    private static ShapeKinds KindOf(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => ShapeKinds.Object,
        JsonValueKind.Array => ShapeKinds.Array,
        JsonValueKind.String => ShapeKinds.String,
        JsonValueKind.Number => ShapeKinds.Number,
        JsonValueKind.True or JsonValueKind.False => ShapeKinds.Boolean,
        JsonValueKind.Null => ShapeKinds.Null,
        _ => ShapeKinds.None,
    };

    /// <summary>Extracts the shape of <paramref name="json"/>.</summary>
    /// <exception cref="ShapeExtractionException">The text is not a single JSON document.</exception>
    public static ShapeSpec Extract(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ShapeExtractionException("The body is empty.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 128 });
        }
        catch (JsonException ex)
        {
            throw new ShapeExtractionException($"The body is not JSON ({ex.Message}).", ex);
        }

        using (document)
        {
            var accumulator = new Dictionary<string, (ShapeKinds Kinds, bool Nullable)>(StringComparer.Ordinal);
            Walk(document.RootElement, "$", accumulator);
            var paths = accumulator.Select(kv => new ShapePath(kv.Key, kv.Value.Kinds, kv.Value.Nullable));
            return new ShapeSpec(paths);
        }
    }

    private static void Walk(
        JsonElement element,
        string path,
        Dictionary<string, (ShapeKinds Kinds, bool Nullable)> accumulator)
    {
        var kind = KindOf(element);
        Accumulate(accumulator, path, kind, element.ValueKind == JsonValueKind.Null);

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Walk(property.Value, path + "." + property.Name, accumulator);
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Walk(item, path + "[]", accumulator);
                }

                break;

            default:
                break;
        }
    }

    private static void Accumulate(
        Dictionary<string, (ShapeKinds Kinds, bool Nullable)> accumulator,
        string path,
        ShapeKinds kind,
        bool nullable)
    {
        if (accumulator.TryGetValue(path, out var existing))
        {
            accumulator[path] = (existing.Kinds | kind, existing.Nullable || nullable);
            return;
        }

        accumulator[path] = (kind, nullable);
    }
}

/// <summary>The body could not be turned into a shape (empty, truncated or not JSON).</summary>
public sealed class ShapeExtractionException : Exception
{
    /// <summary>Creates the exception with a human-readable <paramref name="message"/>.</summary>
    public ShapeExtractionException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception wrapping a parser failure.</summary>
    public ShapeExtractionException(string message, Exception inner) : base(message, inner)
    {
    }
}
