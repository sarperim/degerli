namespace Degerli.Ingestion.Coverage;

/// <summary>
/// The data types whose per-instrument and per-fund coverage is recorded in
/// <c>coverage_metadata</c> (FR-MDF-010/011, FR-FDF-004, NFR-MDF-003/NFR-FDF-001).
/// The instrument types mirror the fact types UC-MDF-002 backfills (prices, statements,
/// dividends, corporate actions, disclosures); the fund types mirror the three TEFAS
/// facts (nav, performance, holdings). For every universe instrument/fund × its data
/// types there is either ingested data or an explicit coverage/gap record — the
/// no-silent-gaps rule.
/// </summary>
public static class CoverageDataTypes
{
    public const string Prices = "prices";
    public const string Statements = "statements";
    public const string Dividends = "dividends";
    public const string CorporateActions = "corporate_actions";
    public const string Disclosures = "disclosures";

    /// <summary>Dated NAV history (FR-FDF-001) — the fund's historical time series.</summary>
    public const string Nav = "nav";

    /// <summary>Performance as published (FR-FDF-002).</summary>
    public const string Performance = "performance";

    /// <summary>Holdings snapshots where published (FR-FDF-003).</summary>
    public const string Holdings = "holdings";

    /// <summary>Instrument-scoped data types, in a stable order.</summary>
    public static IReadOnlyList<string> InstrumentScoped { get; } =
    [
        Prices,
        Statements,
        Dividends,
        CorporateActions,
        Disclosures,
    ];

    /// <summary>Fund-scoped data types, in a stable order.</summary>
    public static IReadOnlyList<string> FundScoped { get; } =
    [
        Nav,
        Performance,
        Holdings,
    ];
}
