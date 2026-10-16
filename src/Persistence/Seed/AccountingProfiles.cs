namespace Degerli.Persistence.Seed;

/// <summary>
/// The checked-in, builder-curated accounting-profile classification (02 §3.1, v1.3):
/// which BIST 100 members report under non-industrial accounting rules. The profile
/// drives which metrics are meaningful (§6.2) and whether a free-cash-flow DCF applies
/// (§6.4).
/// <para>
/// This is a <em>classification</em>, not a source fact: the universe/statement source
/// syncs never set or overwrite it (02 §3.1). New instruments are classified from this
/// list on creation — a symbol absent from the list keeps the <c>industrial</c> default.
/// The list is intentionally a small, hand-maintained set so a new financial-sector
/// listing is a one-line, reviewable change rather than a silent data-driven guess.
/// </para>
/// </summary>
public static class AccountingProfiles
{
    /// <summary>The default profile for an instrument not classified here (02 §3.1).</summary>
    public const string Industrial = "industrial";

    /// <summary>Deposit / participation / development &amp; investment banks.</summary>
    public const string Bank = "bank";

    /// <summary>Insurance and reinsurance companies.</summary>
    public const string Insurance = "insurance";

    /// <summary>Real-estate investment trusts (GYO).</summary>
    public const string Reit = "reit";

    /// <summary>
    /// The known profile values (02 §3.1). Any other value is a data error, not a new
    /// class.
    /// </summary>
    public static readonly IReadOnlySet<string> Known =
        new HashSet<string>(StringComparer.Ordinal) { Industrial, Bank, Insurance, Reit };

    /// <summary>
    /// BIST 100 financial-sector members → accounting profile (builder-curated,
    /// checked in). Symbols are the KAP/BIST tickers.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> BySymbol =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Banks.
            ["AKBNK"] = Bank,
            ["ALBRK"] = Bank,
            ["GARAN"] = Bank,
            ["HALKB"] = Bank,
            ["ICBCT"] = Bank,
            ["ISCTR"] = Bank,
            ["KLNMA"] = Bank,
            ["QNBFB"] = Bank,
            ["SKBNK"] = Bank,
            ["TSKB"] = Bank,
            ["VAKBN"] = Bank,
            ["YKBNK"] = Bank,

            // Insurance / reinsurance.
            ["AGESA"] = Insurance,
            ["AKGRT"] = Insurance,
            ["ANSGR"] = Insurance,
            ["GRSEL"] = Insurance,
            ["RAYSG"] = Insurance,
            ["TURSG"] = Insurance,

            // Real-estate investment trusts (GYO).
            ["AGYO"] = Reit,
            ["EKGYO"] = Reit,
            ["HLGYO"] = Reit,
            ["ISGYO"] = Reit,
            ["OZKGY"] = Reit,
            ["PEKGY"] = Reit,
            ["TRGYO"] = Reit,
            ["VKGYO"] = Reit,
        };

    /// <summary>
    /// The curated profile for <paramref name="symbol"/>, or <see cref="Industrial"/>
    /// when the symbol is not a known financial-sector member. A blank/unknown symbol
    /// is classified <see cref="Industrial"/> — never null.
    /// </summary>
    public static string GetOrDefault(string? symbol) =>
        symbol is not null && BySymbol.TryGetValue(symbol, out var profile)
            ? profile
            : Industrial;
}
