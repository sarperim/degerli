namespace Degerli.SourceProbes.Shapes;

/// <summary>
/// The structural kinds a JSON path was observed to carry (TKT-foundation-068).
/// </summary>
/// <remarks>
/// A path carries a <b>set</b> of kinds, not one: an EVDS observation column legitimately
/// arrives as a number on one row and a locale-free numeric string on another
/// (<c>01</c> §10.9.6), and array elements of the same field may differ the same way. The
/// probe therefore compares kind <i>sets</i> — an observed set that is not a subset of the
/// expected set is drift, a subset is not.
/// </remarks>
[Flags]
public enum ShapeKinds
{
    /// <summary>No observation (used only as the empty set).</summary>
    None = 0,

    /// <summary>A JSON object.</summary>
    Object = 1,

    /// <summary>A JSON array.</summary>
    Array = 2,

    /// <summary>A JSON string.</summary>
    String = 4,

    /// <summary>A JSON number (integral and fractional are the same wire shape).</summary>
    Number = 8,

    /// <summary>A JSON boolean.</summary>
    Boolean = 16,

    /// <summary>A JSON <c>null</c> — recorded as a kind, never as an absence.</summary>
    Null = 32,
}

/// <summary>Renders <see cref="ShapeKinds"/> for reports and contract files.</summary>
public static class ShapeKindsText
{
    /// <summary>The lowercase wire names of <paramref name="kinds"/>, sorted, comma-joined.</summary>
    public static string Render(ShapeKinds kinds)
    {
        if (kinds == ShapeKinds.None)
        {
            return "none";
        }

        var names = new List<string>(6);
        if (kinds.HasFlag(ShapeKinds.Array)) names.Add("array");
        if (kinds.HasFlag(ShapeKinds.Boolean)) names.Add("boolean");
        if (kinds.HasFlag(ShapeKinds.Null)) names.Add("null");
        if (kinds.HasFlag(ShapeKinds.Number)) names.Add("number");
        if (kinds.HasFlag(ShapeKinds.Object)) names.Add("object");
        if (kinds.HasFlag(ShapeKinds.String)) names.Add("string");
        return string.Join(",", names);
    }

    /// <summary>Parses a rendered kind list (contract files carry the rendered form).</summary>
    /// <exception cref="FormatException">An unknown kind name.</exception>
    public static ShapeKinds Parse(string text)
    {
        var kinds = ShapeKinds.None;
        foreach (var raw in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            kinds |= raw switch
            {
                "object" => ShapeKinds.Object,
                "array" => ShapeKinds.Array,
                "string" => ShapeKinds.String,
                "number" => ShapeKinds.Number,
                "boolean" => ShapeKinds.Boolean,
                "null" => ShapeKinds.Null,
                _ => throw new FormatException($"Unknown shape kind '{raw}'."),
            };
        }

        return kinds;
    }
}

/// <summary>
/// One path of a shape: the JSON path, the kinds observed there, and whether it was ever
/// observed as <c>null</c>.
/// </summary>
/// <param name="Path">
/// A <c>$</c>-rooted path. Objects use <c>.name</c>, arrays append <c>[]</c>
/// (<c>$.value[].HGDG_KAPANIS</c>). A key that itself contains <c>.</c> is rendered
/// verbatim (EVDS series codes like <c>TP.DK.USD.S.YTL</c>) — paths are compared as
/// strings, so this is unambiguous for comparison and only a cosmetic caveat in reports.
/// </param>
/// <param name="Kinds">The kinds observed at this path (a set; see <see cref="ShapeKinds"/>).</param>
/// <param name="Nullable">True when a <c>null</c> was observed at this path.</param>
public sealed record ShapePath(string Path, ShapeKinds Kinds, bool Nullable)
{
    /// <summary>True when this path is an array element or a descendant of one.</summary>
    public bool IsElementPath => Path.Contains("[]", StringComparison.Ordinal);
}

/// <summary>
/// The shape of one body: a flat, sorted list of <see cref="ShapePath"/> entries. Flat
/// rather than nested so a drift report and a contract file read as a reviewable diff.
/// </summary>
public sealed class ShapeSpec
{
    private readonly Dictionary<string, ShapePath> _byPath;

    /// <summary>Creates a spec from <paramref name="paths"/> (duplicates merged, order sorted).</summary>
    public ShapeSpec(IEnumerable<ShapePath> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _byPath = new Dictionary<string, ShapePath>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            _byPath[path.Path] = _byPath.TryGetValue(path.Path, out var existing)
                ? existing with { Kinds = existing.Kinds | path.Kinds, Nullable = existing.Nullable || path.Nullable }
                : path;
        }

        Paths = _byPath.Values.OrderBy(p => p.Path, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Every path, sorted ordinally by path text.</summary>
    public IReadOnlyList<ShapePath> Paths { get; }

    /// <summary>True when the spec carries no paths (an unprobeable body).</summary>
    public bool IsEmpty => Paths.Count == 0;

    /// <summary>Looks a path up by its exact text.</summary>
    public bool TryGet(string path, out ShapePath entry)
    {
        if (_byPath.TryGetValue(path, out var found))
        {
            entry = found;
            return true;
        }

        entry = null!;
        return false;
    }
}
