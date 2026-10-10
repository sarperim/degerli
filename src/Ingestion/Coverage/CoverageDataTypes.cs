namespace Degerli.Ingestion.Coverage;

/// <summary>
/// The data types whose per-instrument coverage is recorded in
/// <c>coverage_metadata</c> (FR-MDF-010/011, NFR-MDF-003). These mirror the fact types
/// UC-MDF-002 backfills (prices, statements, dividends, corporate actions, disclosures):
/// for every universe instrument × data type there is either ingested data or an
/// explicit coverage/gap record — the no-silent-gaps rule.
/// </summary>
public static class CoverageDataTypes
{
    public const string Prices = "prices";
    public const string Statements = "statements";
    public const string Dividends = "dividends";
    public const string CorporateActions = "corporate_actions";
    public const string Disclosures = "disclosures";

    /// <summary>Instrument-scoped data types, in a stable order.</summary>
    public static IReadOnlyList<string> InstrumentScoped { get; } =
    [
        Prices,
        Statements,
        Dividends,
        CorporateActions,
        Disclosures,
    ];
}
