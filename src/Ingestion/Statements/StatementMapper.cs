namespace Degerli.Ingestion.Statements;

/// <summary>
/// The ETL canonical mapping (FR-MDF-002, <c>02</c> §6.1): collapses a source statement's
/// lines onto the internal chart-of-accounts vocabulary, derives <c>GROSS_PROFIT</c> and
/// <c>EBITDA</c> when the source does not report them, and passes unmapped items through
/// under an <c>OTHER_*</c> code (recorded, ignored by metrics — an honest gap, never a
/// silent drop).
/// </summary>
public static class StatementMapper
{
    /// <summary>The canonical chart of accounts (<c>02</c> §6.1).</summary>
    public static readonly IReadOnlySet<string> CanonicalCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        "REV",
        "COGS",
        "GROSS_PROFIT",
        "SGA",
        "EBIT",
        "DEPR_AMORT",
        "EBITDA",
        "FIN_EXP",
        "TAX_EXP",
        "PRETAX_INC",
        "NI",
        "MINORITY_INT",
        "CAPEX",
        "ΔWC",
        "CASH",
        "ST_DEBT",
        "LT_DEBT",
        "TOTAL_EQUITY",
        "CUR_ASSETS",
        "CUR_LIAB",
        "SHARES_DILUTED",
        "SHARES_OUT",
    };

    /// <summary>
    /// Maps one source line set to its canonical stored lines. Canonical codes pass through;
    /// anything else is stored under <c>OTHER_{source}</c> (or left as-is if already
    /// <c>OTHER_</c>-prefixed). Missing <c>GROSS_PROFIT</c> is derived as <c>REV − COGS</c>
    /// and missing <c>EBITDA</c> as <c>EBIT + DEPR_AMORT</c> when the inputs are present.
    /// </summary>
    public static IReadOnlyDictionary<string, decimal> Map(IReadOnlyDictionary<string, decimal> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var mapped = new Dictionary<string, decimal>(StringComparer.Ordinal);

        foreach (var (code, value) in source)
        {
            var target = CanonicalCodes.Contains(code)
                ? code
                : code.StartsWith("OTHER_", StringComparison.Ordinal) ? code : $"OTHER_{code}";
            mapped[target] = value;
        }

        if (!mapped.ContainsKey("GROSS_PROFIT")
            && mapped.TryGetValue("REV", out var rev)
            && mapped.TryGetValue("COGS", out var cogs))
        {
            mapped["GROSS_PROFIT"] = rev - cogs;
        }

        if (!mapped.ContainsKey("EBITDA")
            && mapped.TryGetValue("EBIT", out var ebit)
            && mapped.TryGetValue("DEPR_AMORT", out var deprAmort))
        {
            mapped["EBITDA"] = ebit + deprAmort;
        }

        return mapped;
    }
}
