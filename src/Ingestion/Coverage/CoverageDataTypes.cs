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

    /// <summary>
    /// The separator of the per-source statements depth family (<c>statements:{sourceRef}</c>).
    /// Since the statement store's natural key includes the source (<c>01</c> v1.5.57 §10.9.19,
    /// TC-MDF-111) one instrument's statements can arrive from more than one evidence class, so
    /// the aggregate <see cref="Statements"/> depth alone can no longer say <b>which</b> source
    /// reached how far — and a company whose only facts are 2023's incumbent rows must read as
    /// exactly that, never as "current".
    /// </summary>
    public const string StatementsSourceSeparator = ":";

    /// <summary>
    /// The per-source statements depth data type for <paramref name="sourceRef"/> — e.g.
    /// <c>statements:kap</c> and <c>statements:kap-public</c>. Deliberately outside
    /// <see cref="InstrumentScoped"/>: the no-silent-gaps rule owns one row per instrument ×
    /// fact type, and a per-source row is present only when that source actually holds facts for
    /// the instrument — its absence means "this source has written nothing here", never "this
    /// source found no statements" (which is the public lane's own achieved-depth provenance,
    /// <c>kap-public-statements-depth</c>).
    /// </summary>
    public static string StatementsBySource(string sourceRef) =>
        Statements + StatementsSourceSeparator + sourceRef;

    /// <summary>
    /// Index-constituent membership history (FR-MDF-007, BR-MDF-005) — recorded at
    /// <c>universe</c> scope (one record for the whole covered universe, not per
    /// instrument). Free structured pre-launch constituent history does not exist, so the
    /// unavailable period is recorded as an explicit accepted gap (BR-MDF-007).
    /// </summary>
    public const string Constituents = "constituents";

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
