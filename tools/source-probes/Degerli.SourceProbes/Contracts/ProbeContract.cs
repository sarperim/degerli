using Degerli.SourceProbes.Shapes;

namespace Degerli.SourceProbes.Contracts;

/// <summary>How the checked-in expected shape was derived from the committed double.</summary>
public enum DoubleProvenance
{
    /// <summary>The double is a bounded verbatim capture from the live source (<c>01</c> §10.9.7).</summary>
    Captured,

    /// <summary>The double is a hand-constructed shape-faithful body — never presented as a capture.</summary>
    Constructed,
}

/// <summary>
/// Where a contract's expected shape comes from. Recorded so a reader can always answer
/// "shape-faithful to what, and since when?" (acceptance iv; <c>01</c> §10.9.7 labeling rule).
/// </summary>
/// <param name="CatalogEntry">The fixture catalog constant the shape was derived from.</param>
/// <param name="File">The committed file that catalog lives in.</param>
/// <param name="Provenance">Captured vs hand-constructed.</param>
/// <param name="Note">The catalog's own provenance note, abbreviated.</param>
public sealed record DoubleReference(
    string CatalogEntry,
    string File,
    DoubleProvenance Provenance,
    string Note);

/// <summary>
/// The expected shape of one source endpoint: the committed double's shape plus the
/// source's transport posture (status) and the narrow, named exemptions a data-derived
/// container needs (TKT-foundation-068).
/// </summary>
public sealed class ProbeContract
{
    /// <summary>The probe key (matches <see cref="SourceProbeRegistry"/>).</summary>
    public required string Source { get; init; }

    /// <summary>The endpoint, with placeholders (<c>{symbol}</c>, <c>{index}</c>, <c>{series}</c>).</summary>
    public required string Endpoint { get; init; }

    /// <summary>The committed double this shape was derived from.</summary>
    public required DoubleReference Double { get; init; }

    /// <summary>The date (ISO) the shape was derived / re-checked against the double.</summary>
    public required string DerivedAt { get; init; }

    /// <summary>The HTTP status the double serves for a healthy response.</summary>
    public required int ExpectedStatus { get; init; }

    /// <summary>
    /// Containers whose <b>direct children</b> are data-derived keys (EVDS series codes,
    /// transposed date columns), so an unseen key there is not drift. Narrow and per-source:
    /// every listed key is still shape-checked, and anything outside the container is not
    /// exempt. Never used to silence a difference the double actually pins.
    /// </summary>
    public IReadOnlyList<string> AllowExtraUnder { get; init; } = [];

    /// <summary>The expected shape (flat path list).</summary>
    public required ShapeSpec Shape { get; init; }
}
