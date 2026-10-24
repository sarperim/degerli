using Degerli.SourceProbes.Shapes;

namespace Degerli.SourceProbes.Probing;

/// <summary>The three — and only three — things a probe can conclude (TKT-foundation-068).</summary>
public enum ProbeOutcomeKind
{
    /// <summary>The live shape matched the committed double's shape.</summary>
    Clean,

    /// <summary>The live shape differs; every drifted path is named.</summary>
    Drift,

    /// <summary>
    /// The source could not be read (transport failure, timeout, empty or non-JSON body, no
    /// configured endpoint). <b>Never</b> reported as clean: "we could not check" and "we
    /// checked and it matched" are different answers, and conflating them is how a source
    /// shape rots invisibly.
    /// </summary>
    Unreachable,
}

/// <summary>How the observed body was obtained.</summary>
public enum ProbeMode
{
    /// <summary>A live, paced, single request against the real source.</summary>
    Live,

    /// <summary>An operator-supplied saved body (a manual capture, or a mutated double for a demo).</summary>
    SavedBody,

    /// <summary>No body at all — the probe declined or was unable to fetch.</summary>
    None,
}

/// <summary>
/// One probe's conclusion: outcome, drifted paths, and a structural excerpt of what was
/// observed. The excerpt carries <b>paths and kinds only</b> — never a value, never the raw
/// body (acceptance iii).
/// </summary>
/// <param name="Source">The probe key.</param>
/// <param name="Endpoint">The redacted endpoint actually used (hostnames are fine; credentials are not).</param>
/// <param name="Mode">How the body was obtained.</param>
/// <param name="Outcome">Clean, drift or unreachable.</param>
/// <param name="StatusCode">The HTTP status observed, when there was one.</param>
/// <param name="Findings">The drifted paths (empty only when <paramref name="Outcome"/> is not drift).</param>
/// <param name="ObservedShape">The structural excerpt (capped; kinds only).</param>
/// <param name="Detail">A short note: why it was unreachable, or the double it was compared against.</param>
/// <param name="CheckedAt">When the probe ran (UTC).</param>
public sealed record ProbeResult(
    string Source,
    string Endpoint,
    ProbeMode Mode,
    ProbeOutcomeKind Outcome,
    int? StatusCode,
    IReadOnlyList<DriftFinding> Findings,
    IReadOnlyList<ShapePath> ObservedShape,
    string Detail,
    DateTimeOffset CheckedAt)
{
    /// <summary>How many shape paths the report retains (a structural excerpt, not a dump).</summary>
    public const int MaxExcerptPaths = 400;

    /// <summary>
    /// Builds a result from a comparison. A comparison with no findings is clean; anything
    /// else is drift with every path named.
    /// </summary>
    public static ProbeResult FromComparison(
        string source,
        string endpoint,
        ProbeMode mode,
        int? statusCode,
        ShapeSpec observed,
        IReadOnlyList<DriftFinding> findings,
        string detail,
        DateTimeOffset checkedAt)
    {
        ArgumentNullException.ThrowIfNull(observed);
        ArgumentNullException.ThrowIfNull(findings);

        return new ProbeResult(
            source,
            endpoint,
            mode,
            findings.Count == 0 ? ProbeOutcomeKind.Clean : ProbeOutcomeKind.Drift,
            statusCode,
            findings,
            Excerpt(observed),
            detail,
            checkedAt);
    }

    /// <summary>
    /// The honest failure: the source could not be read, so no shape claim can be made.
    /// </summary>
    public static ProbeResult Unreachable(
        string source,
        string endpoint,
        string reason,
        DateTimeOffset checkedAt,
        int? statusCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new ProbeResult(
            source,
            endpoint,
            ProbeMode.None,
            ProbeOutcomeKind.Unreachable,
            statusCode,
            [],
            [],
            reason,
            checkedAt);
    }

    private static IReadOnlyList<ShapePath> Excerpt(ShapeSpec observed) =>
        observed.Paths.Count <= MaxExcerptPaths
            ? observed.Paths
            : observed.Paths.Take(MaxExcerptPaths).ToArray();
}
