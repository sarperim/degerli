using Degerli.SourceProbes.Contracts;
using Degerli.SourceProbes.Shapes;

namespace Degerli.SourceProbes.Probing;

/// <summary>
/// Turns a body into a <see cref="ProbeResult"/>: the single place where "what we read" becomes
/// an outcome. Shared by the live executor and the saved-body mode so both report the same way,
/// and unit-testable without a network.
/// </summary>
/// <remarks>
/// The three unreachable outcomes are all here on purpose: an empty body, a body that is not
/// JSON, and a body with no structure. Each one means "we could not see the source's shape", so
/// none of them may ever be reported as clean (acceptance i/iii).
/// </remarks>
public static class ProbeRunner
{
    /// <summary>Evaluates <paramref name="body"/> against <paramref name="contract"/>.</summary>
    /// <param name="source">The probe key.</param>
    /// <param name="endpoint">The redacted endpoint.</param>
    /// <param name="contract">The expected shape.</param>
    /// <param name="body">The body text read from the source (or a saved file).</param>
    /// <param name="mode">How the body was obtained.</param>
    /// <param name="statusCode">The HTTP status, when one was observed.</param>
    /// <param name="detail">A short note recorded with the result.</param>
    /// <param name="checkedAt">The check timestamp (UTC), injected for determinism.</param>
    public static ProbeResult Evaluate(
        string source,
        string endpoint,
        ProbeContract contract,
        string? body,
        ProbeMode mode,
        int? statusCode,
        string detail,
        DateTimeOffset checkedAt)
    {
        ArgumentNullException.ThrowIfNull(contract);

        if (string.IsNullOrWhiteSpace(body))
        {
            return ProbeResult.Unreachable(source, endpoint, "the body was empty", checkedAt, statusCode);
        }

        ShapeSpec observed;
        try
        {
            observed = JsonShapeExtractor.Extract(body);
        }
        catch (ShapeExtractionException ex)
        {
            return ProbeResult.Unreachable(source, endpoint, ex.Message, checkedAt, statusCode);
        }

        if (observed.IsEmpty)
        {
            return ProbeResult.Unreachable(source, endpoint, "the body carried no structure", checkedAt, statusCode);
        }

        var findings = ShapeComparer.Compare(contract, observed, statusCode);
        return ProbeResult.FromComparison(source, endpoint, mode, statusCode, observed, findings, detail, checkedAt);
    }
}
