using Degerli.SourceProbes.Contracts;

namespace Degerli.SourceProbes.Shapes;

/// <summary>
/// Compares an observed shape against a contract's expected shape and names every drifted
/// path (TKT-foundation-068 acceptance i).
/// </summary>
/// <remarks>
/// The rules, and why each exists:
/// <list type="bullet">
/// <item><b>missing key</b> (breaking) — the double pins a path the live body does not
/// carry: the adapter is parsing a field that is no longer there.</item>
/// <item><b>extra key</b> (warning) — the live body added a path. Drift, but additive; it
/// is reported because a new key can be the early signal of a shape change.</item>
/// <item><b>type changed</b> (breaking) — the kind set observed is not a subset of the
/// expected set (e.g. a number became a string, a list became an object).</item>
/// <item><b>nullability changed</b> (warning) — the live body can be null where the double
/// never was. A field that is always null in the double stays a "null" kind, so a value
/// appearing there is a <i>type</i> change, not a silent success.</item>
/// <item><b>element shape missing</b> (breaking) — the double's array carried an element
/// shape and the live array is empty. This is the İş Yatırım <c>value: []</c> incident: a
/// well-formed envelope with no rows is <b>not</b> "no drift", it is unverifiable.</item>
/// <item><b>status changed</b> (breaking) — the endpoint answered a different status than
/// the double. This is the KAP <c>ER005</c> shape: a 400 the doubles never serve.</item>
/// </list>
/// A comparison with no findings is <see cref="ProbeOutcomeKind.Clean"/>; every finding is
/// drift, severity only ranks it for the reader.
/// </remarks>
public static class ShapeComparer
{
    /// <summary>The synthetic path used for the HTTP status (it is not a body path).</summary>
    public const string StatusPath = "http.status";

    /// <summary>Compares <paramref name="observed"/> against <paramref name="contract"/>.</summary>
    /// <param name="contract">The expected shape and its transport posture.</param>
    /// <param name="observed">The shape extracted from the live (or saved) body.</param>
    /// <param name="observedStatus">The HTTP status observed, when a live probe produced one.</param>
    /// <returns>Findings sorted by path then kind; empty means clean.</returns>
    public static IReadOnlyList<DriftFinding> Compare(
        ProbeContract contract,
        ShapeSpec observed,
        int? observedStatus = null)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(observed);

        var findings = new List<DriftFinding>();
        var expected = contract.Shape;
        var emptyCollections = new List<string>();

        foreach (var expectedPath in expected.Paths)
        {
            if (observed.TryGet(expectedPath.Path, out var observedPath))
            {
                CompareCommon(expectedPath, observedPath, findings);
                continue;
            }

            if (IsEmptyCollectionShape(observed, expectedPath.Path))
            {
                emptyCollections.Add(ElementRootOf(expectedPath.Path));
                findings.Add(new DriftFinding(
                    expectedPath.Path,
                    DriftKind.ElementShapeMissing,
                    ShapeKindsText.Render(expectedPath.Kinds),
                    "absent (the collection answered empty)",
                    DriftSeverity.Breaking));
                continue;
            }

            findings.Add(new DriftFinding(
                expectedPath.Path,
                DriftKind.MissingKey,
                ShapeKindsText.Render(expectedPath.Kinds),
                "absent",
                DriftSeverity.Breaking));
        }

        // One type change at a container explains everything beneath it: when an array becomes
        // an object (the KAP ER005 envelope), its element paths are not independently "missing
        // keys" — reporting them would fabricate a second, misleading explanation. Keep the
        // type change and the paths that survive outside the changed subtree.
        var changedSubtrees = findings
            .Where(f => f.Kind == DriftKind.TypeChanged)
            .Select(f => f.Path)
            .ToArray();
        findings.RemoveAll(f =>
            f.Kind == DriftKind.MissingKey &&
            changedSubtrees.Any(root =>
                f.Path.Length > root.Length &&
                f.Path.StartsWith(root, StringComparison.Ordinal) &&
                f.Path[root.Length] is '.' or '['));

        // One empty collection explains its whole element shape: report the container and the
        // paths that survive, not thirty consequences of the same emptiness.
        findings.RemoveAll(f =>
            f.Kind == DriftKind.ElementShapeMissing &&
            emptyCollections.Any(root =>
                f.Path.Length > root.Length &&
                f.Path.StartsWith(root, StringComparison.Ordinal)));

        var expectedPaths = new HashSet<string>(expected.Paths.Select(p => p.Path), StringComparer.Ordinal);
        foreach (var observedPath in observed.Paths)
        {
            if (expectedPaths.Contains(observedPath.Path) || IsExempt(contract, observedPath.Path))
            {
                continue;
            }

            findings.Add(new DriftFinding(
                observedPath.Path,
                DriftKind.ExtraKey,
                "absent",
                ShapeKindsText.Render(observedPath.Kinds),
                DriftSeverity.Warning));
        }

        if (observedStatus is { } status && status != contract.ExpectedStatus)
        {
            findings.Add(new DriftFinding(
                StatusPath,
                DriftKind.StatusChanged,
                contract.ExpectedStatus.ToString(System.Globalization.CultureInfo.InvariantCulture),
                status.ToString(System.Globalization.CultureInfo.InvariantCulture),
                DriftSeverity.Breaking));
        }

        return findings
            .OrderBy(f => f.Path, StringComparer.Ordinal)
            .ThenBy(f => f.Kind)
            .ToArray();
    }

    private static void CompareCommon(
        ShapePath expectedPath,
        ShapePath observedPath,
        List<DriftFinding> findings)
    {
        // Null is compared as its own axis (below): a value that vanished is a nullability
        // change, not a type change, so the kind comparison looks at non-null kinds only.
        var expectedKinds = expectedPath.Kinds & ~ShapeKinds.Null;
        var observedKinds = observedPath.Kinds & ~ShapeKinds.Null;

        if (observedKinds != ShapeKinds.None && (observedKinds & ~expectedKinds) != 0)
        {
            findings.Add(new DriftFinding(
                expectedPath.Path,
                DriftKind.TypeChanged,
                ShapeKindsText.Render(expectedPath.Kinds),
                ShapeKindsText.Render(observedPath.Kinds),
                DriftSeverity.Breaking));
        }

        if (observedPath.Nullable && !expectedPath.Nullable)
        {
            findings.Add(new DriftFinding(
                expectedPath.Path,
                DriftKind.NullabilityChanged,
                "never null",
                "null observed",
                DriftSeverity.Warning));
        }
    }

    /// <summary>
    /// True when the missing path is unverifiable because its collection answered <b>empty</b>:
    /// the element root (<c>$.value[]</c>) is absent from the observed shape while the container
    /// itself (<c>$.value</c>) is present <b>and is an array</b>. That is the İş Yatırım
    /// <c>value: []</c> case.
    /// </summary>
    /// <remarks>
    /// A field that is merely missing from a <i>non-empty</i> element is a plain missing key —
    /// the distinction matters, because calling it "empty collection" would hide a removed field
    /// behind the wrong explanation. The container-kind check matters just as much: an array
    /// that turned into an <i>object</i> is a type change, not an empty collection, and must not
    /// manufacture downstream "empty" findings (the KAP <c>ER005</c> envelope).
    /// </remarks>
    private static bool IsEmptyCollectionShape(ShapeSpec observed, string expectedPath)
    {
        var lastElement = expectedPath.LastIndexOf("[]", StringComparison.Ordinal);
        if (lastElement < 0)
        {
            return false;
        }

        var elementRoot = expectedPath[..(lastElement + 2)];
        if (observed.TryGet(elementRoot, out _))
        {
            return false;
        }

        var arrayPath = elementRoot[..^2];
        return observed.TryGet(arrayPath, out var container) &&
               container.Kinds.HasFlag(ShapeKinds.Array);
    }

    /// <summary>The element root of a path (<c>$.value[].X</c> → <c>$.value[]</c>).</summary>
    private static string ElementRootOf(string path)
    {
        var lastElement = path.LastIndexOf("[]", StringComparison.Ordinal);
        return lastElement < 0 ? path : path[..(lastElement + 2)];
    }

    /// <summary>
    /// True when <paramref name="observedPath"/> sits directly under a container whose keys
    /// are data-derived (EVDS series codes, transposed date columns) and is therefore not
    /// reported as an extra key. Shape checks on the listed keys still apply.
    /// </summary>
    private static bool IsExempt(ProbeContract contract, string observedPath)
    {
        foreach (var container in contract.AllowExtraUnder)
        {
            if (observedPath.Length > container.Length &&
                observedPath.StartsWith(container, StringComparison.Ordinal) &&
                observedPath[container.Length] is '.' or '[')
            {
                return true;
            }
        }

        return false;
    }
}
