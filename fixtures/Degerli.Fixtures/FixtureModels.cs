namespace Degerli.Fixtures;

/// <summary>Sector row (FU §3; 02 §3.1) with bilingual labels.</summary>
public sealed record FixtureSector(string Code, string NameTr, string NameEn);

/// <summary>Market index row (XU100 / XU30).</summary>
public sealed record FixtureIndex(string Code, string NameTr, string NameEn);

/// <summary>Instrument row (FU §3). <see cref="SharesDiluted"/> is a share count.</summary>
public sealed record FixtureInstrument(
    string Symbol,
    string Name,
    string? SectorCode,
    DateOnly ListingDate,
    long SharesDiluted,
    string Archetype);

/// <summary>Effective-dated index membership (XU100 all, XU30 subset).</summary>
public sealed record FixtureMembership(
    string IndexCode,
    string Symbol,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo);

/// <summary>One OHLC/volume row; <see cref="CloseAdjusted"/> NULL means "same as raw".</summary>
public sealed record FixturePrice(
    string Symbol,
    DateOnly Date,
    decimal Open,
    decimal High,
    decimal Low,
    decimal CloseRaw,
    decimal? CloseAdjusted,
    long Volume);

/// <summary>One index level row.</summary>
public sealed record FixtureIndexLevel(string IndexCode, DateOnly Date, decimal Close);

/// <summary>
/// One statement version of one period. <see cref="Lines"/> are canonical chart
/// item codes (02 §6.1).
/// </summary>
public sealed record FixtureStatement(
    string Symbol,
    string PeriodType,
    DateOnly PeriodEndDate,
    int FiscalYear,
    string StatementType,
    string Version,
    DateOnly? RestatementDate,
    DateOnly? PublishedAt,
    IReadOnlyDictionary<string, decimal> Lines);

/// <summary>Dividend row.</summary>
public sealed record FixtureDividend(
    string Symbol,
    DateOnly ExDate,
    DateOnly? PayDate,
    decimal AmountPerShare,
    string Currency);

/// <summary>Corporate action feeding the adjustment factor (FU §4, 02 §6.1 note).</summary>
public sealed record FixtureCorporateAction(
    string Symbol,
    string ActionType,
    DateOnly ActionDate,
    string TermsJson);

/// <summary>KAP disclosure metadata (only metadata lives in the DB).</summary>
public sealed record FixtureKapDisclosure(
    long Id,
    string Symbol,
    string DisclosureType,
    DateOnly PublishDate,
    string Title,
    string SourceUrl);

/// <summary>Macro series value; revisions append with distinct <see cref="RecordedAt"/>.</summary>
public sealed record FixtureMacroValue(
    string SeriesCode,
    DateOnly ValueDate,
    decimal Value,
    DateTimeOffset RecordedAt);

/// <summary>Fund row (FU §8).</summary>
public sealed record FixtureFund(string Code, string Name, string? FundType);

/// <summary>Fund NAV row.</summary>
public sealed record FixtureFundNav(string FundCode, DateOnly NavDate, decimal NavValue);

/// <summary>Fund performance row as published.</summary>
public sealed record FixtureFundPerformance(
    string FundCode,
    string Period,
    DateOnly AsOfDate,
    decimal? ReturnValue);

/// <summary>Fund holding line; <see cref="Symbol"/> NULL means unmatched name_raw.</summary>
public sealed record FixtureFundHolding(
    string FundCode,
    DateOnly AsOfDate,
    int LineNo,
    string? Symbol,
    string? NameRaw,
    decimal? Weight,
    decimal? Units);

/// <summary>Seeded account (FU §9) with its Identity role.</summary>
public sealed record FixtureAccount(
    string Email,
    string Password,
    bool Verified,
    string Role,
    string Language)
{
    /// <summary>
    /// Redacts the password from the positional record's default <c>ToString</c>:
    /// an assertion failure over the seeded accounts would otherwise print the
    /// repository-known credential into public CI logs (CWE-798).
    /// </summary>
    public override string ToString() =>
        $"{nameof(FixtureAccount)} {{ Email = {Email}, Password = ***, " +
        $"Verified = {Verified}, Role = {Role}, Language = {Language} }}";
}

/// <summary>Pre-seeded saved screen (FU §9).</summary>
public sealed record FixtureSavedScreen(string UserEmail, string Name, string CriteriaJson);

/// <summary>Pre-seeded DCF scenario (FU §9).</summary>
public sealed record FixtureScenario(
    string UserEmail,
    string Symbol,
    string Name,
    string ParamsJson);

/// <summary>Business description version (FU §9b).</summary>
public sealed record FixtureBusinessDescription(
    string Symbol,
    int Version,
    string Status,
    string? TextTr,
    string? TextEn,
    DateTimeOffset? LastReviewedAt,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<long> SourceRefs);

/// <summary>Coverage-metadata row (FU §8 fund gaps, FR-FDF-004).</summary>
public sealed record FixtureCoverage(
    string Scope,
    string? Symbol,
    string? FundCode,
    string DataType,
    DateOnly? AvailableFrom,
    DateOnly? AvailableTo,
    string? Notes);

/// <summary>
/// Per-series freshness emitted by the module (FU §7; never a wall-clock date):
/// the age in days of the latest ingest (<c>recorded_at</c>) relative to T and
/// whether that ingest has overrun the series' cadence window.
/// </summary>
public sealed record FixtureMacroFreshness(string SeriesCode, int AgeDays, bool IsStale);

/// <summary>The fully-built, anchor-resolved fixture universe.</summary>
public sealed class FixtureSet
{
    public required FixtureAnchor Anchor { get; init; }

    public IReadOnlyList<FixtureSector> Sectors { get; init; } = [];
    public IReadOnlyList<FixtureIndex> Indices { get; init; } = [];
    public IReadOnlyList<FixtureInstrument> Instruments { get; init; } = [];
    public IReadOnlyList<FixtureMembership> Memberships { get; init; } = [];
    public IReadOnlyList<FixturePrice> Prices { get; init; } = [];
    public IReadOnlyList<FixtureIndexLevel> IndexLevels { get; init; } = [];
    public IReadOnlyList<FixtureStatement> Statements { get; init; } = [];
    public IReadOnlyList<FixtureDividend> Dividends { get; init; } = [];
    public IReadOnlyList<FixtureCorporateAction> CorporateActions { get; init; } = [];
    public IReadOnlyList<FixtureKapDisclosure> KapDisclosures { get; init; } = [];
    public IReadOnlyList<FixtureMacroValue> MacroValues { get; init; } = [];
    public IReadOnlyList<FixtureFund> Funds { get; init; } = [];
    public IReadOnlyList<FixtureFundNav> FundNavs { get; init; } = [];
    public IReadOnlyList<FixtureFundPerformance> FundPerformances { get; init; } = [];
    public IReadOnlyList<FixtureFundHolding> FundHoldings { get; init; } = [];
    public IReadOnlyList<FixtureAccount> Accounts { get; init; } = [];
    public IReadOnlyList<FixtureSavedScreen> SavedScreens { get; init; } = [];
    public IReadOnlyList<FixtureScenario> Scenarios { get; init; } = [];
    public IReadOnlyList<FixtureBusinessDescription> BusinessDescriptions { get; init; } = [];
    public IReadOnlyList<FixtureCoverage> Coverage { get; init; } = [];
    public IReadOnlyList<FixtureMacroFreshness> MacroFreshness { get; init; } = [];
}

/// <summary>Canonical chart-of-accounts item codes (02 §6.1), used by the fixtures.</summary>
public static class FinItemCodes
{
    public const string Rev = "REV";
    public const string Cogs = "COGS";
    public const string GrossProfit = "GROSS_PROFIT";
    public const string Ebit = "EBIT";
    public const string DeprAmort = "DEPR_AMORT";
    public const string Ebitda = "EBITDA";
    public const string FinExp = "FIN_EXP";
    public const string PretaxInc = "PRETAX_INC";
    public const string TaxExp = "TAX_EXP";
    public const string Ni = "NI";
    public const string Capex = "CAPEX";
    public const string Dwc = "ΔWC";
    public const string Cash = "CASH";
    public const string StDebt = "ST_DEBT";
    public const string LtDebt = "LT_DEBT";
    public const string TotalEquity = "TOTAL_EQUITY";
    public const string CurAssets = "CUR_ASSETS";
    public const string CurLiab = "CUR_LIAB";
    public const string SharesDiluted = "SHARES_DILUTED";
    public const string SharesOut = "SHARES_OUT";
}
