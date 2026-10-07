namespace Degerli.Core;

/// <summary>
/// Marker for the shared quantitative core assembly. Metric, CAGR and DCF formula
/// definitions live here so the API and the ingestion workers share one source of
/// truth (BR-MDF-009). Concrete formulas land in their domain tickets.
/// </summary>
public static class CoreAssembly
{
    /// <summary>Assembly name of the shared quantitative core.</summary>
    public const string Name = "Degerli.Core";
}
