namespace Degerli.SourceProbes.Shapes;

/// <summary>What kind of structural difference a finding describes (TKT-foundation-068).</summary>
public enum DriftKind
{
    /// <summary>The double's shape carries the path; the live body does not.</summary>
    MissingKey,

    /// <summary>The live body carries a path the double's shape does not.</summary>
    ExtraKey,

    /// <summary>The path exists on both sides with a different kind set (e.g. string became number).</summary>
    TypeChanged,

    /// <summary>The path can be null live but was never null in the double.</summary>
    NullabilityChanged,

    /// <summary>The double's array carried an element shape; the live array was empty.</summary>
    ElementShapeMissing,

    /// <summary>The HTTP status differs from the double's (a 400 where the double serves 200).</summary>
    StatusChanged,
}

/// <summary>How loudly a finding should be read.</summary>
public enum DriftSeverity
{
    /// <summary>Parsing/consumption of the source will break as written.</summary>
    Breaking,

    /// <summary>Additive or value-varying — worth a look, not an outage.</summary>
    Warning,
}

/// <summary>
/// One drifted path: expected vs observed, with the path named (acceptance i). Carries
/// shape text only — never a body value.
/// </summary>
/// <param name="Path">The JSON path (<c>$.value[].HGDG_KAPANIS</c>).</param>
/// <param name="Kind">The class of difference.</param>
/// <param name="Expected">The double's side, rendered (kinds, or an HTTP status).</param>
/// <param name="Observed">The live side, rendered (kinds, or an HTTP status).</param>
/// <param name="Severity">How loudly to read it.</param>
public sealed record DriftFinding(
    string Path,
    DriftKind Kind,
    string Expected,
    string Observed,
    DriftSeverity Severity);
