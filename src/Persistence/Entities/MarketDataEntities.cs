namespace Degerli.Persistence.Entities;

/// <summary>Sector / industry with bilingual labels and a 2-level hierarchy (02 §3.1).</summary>
public class Sector
{
    public long Id { get; set; }
    public string Code { get; set; } = null!;
    public string NameTr { get; set; } = null!;
    public string NameEn { get; set; } = null!;
    public long? ParentSectorId { get; set; }
}

/// <summary>BIST instrument (MDF §7). Language-neutral official name.</summary>
public class Instrument
{
    public long Id { get; set; }
    public string Symbol { get; set; } = null!;
    public string? Isin { get; set; }
    public string Name { get; set; } = null!;

    /// <summary>NULL = unclassified (UC-MDF-003a).</summary>
    public long? SectorId { get; set; }

    public DateOnly? ListingDate { get; set; }
    public DateOnly? DelistingDate { get; set; }

    /// <summary>active / delisted.</summary>
    public string? Status { get; set; }

    public string SourceRef { get; set; } = null!;
    public DateTimeOffset RecordedAt { get; set; }
}

/// <summary>Market index (XU100, XU30).</summary>
public class MarketIndex
{
    public long Id { get; set; }
    public string Code { get; set; } = null!;
    public string NameTr { get; set; } = null!;
    public string NameEn { get; set; } = null!;
}

/// <summary>
/// Effective-dated, append-only constituent membership (FR-MDF-007, BR-MDF-005).
/// The current universe is the view <c>v_current_universe</c>.
/// </summary>
public class IndexConstituent
{
    public long Id { get; set; }
    public long IndexId { get; set; }
    public long InstrumentId { get; set; }
    public DateOnly EffectiveFrom { get; set; }

    /// <summary>NULL = currently a member.</summary>
    public DateOnly? EffectiveTo { get; set; }

    public string SourceRef { get; set; } = null!;
    public DateTimeOffset RecordedAt { get; set; }
}

/// <summary>Daily EOD price and volume; one row per instrument per trading day (FR-MDF-001).</summary>
public class DailyPrice
{
    public long InstrumentId { get; set; }
    public DateOnly PriceDate { get; set; }
    public decimal? Open { get; set; }
    public decimal? High { get; set; }
    public decimal? Low { get; set; }

    /// <summary>Raw close — current-display truth.</summary>
    public decimal CloseRaw { get; set; }

    /// <summary>Corporate-action-adjusted close — historical-analysis truth.</summary>
    public decimal? CloseAdjusted { get; set; }

    public long? Volume { get; set; }
    public string SourceRef { get; set; } = null!;
    public DateTimeOffset RecordedAt { get; set; }
}

/// <summary>Daily index level; one row per index per trading day (FR-MDF-006).</summary>
public class IndexLevel
{
    public long IndexId { get; set; }
    public DateOnly LevelDate { get; set; }
    public decimal Close { get; set; }
    public string SourceRef { get; set; } = null!;
    public DateTimeOffset RecordedAt { get; set; }
}

/// <summary>Financial statement, versioned as_reported / restated (FR-MDF-002, BR-MDF-011).</summary>
public class FinancialStatement
{
    public long Id { get; set; }
    public long InstrumentId { get; set; }

    /// <summary>Q or FY.</summary>
    public string PeriodType { get; set; } = null!;

    public DateOnly PeriodEndDate { get; set; }
    public int? FiscalYear { get; set; }

    /// <summary>IS / BS / CF.</summary>
    public string StatementType { get; set; } = null!;

    /// <summary>as_reported or restated.</summary>
    public string Version { get; set; } = null!;

    public DateOnly? RestatementDate { get; set; }
    public DateOnly? PublishedAt { get; set; }
    public string SourceRef { get; set; } = null!;
    public DateTimeOffset RecordedAt { get; set; }
}

/// <summary>Statement line mapped to the canonical chart of accounts (02 §6.1).</summary>
public class FinLineItem
{
    public long StatementId { get; set; }
    public string ItemCode { get; set; } = null!;
    public decimal Value { get; set; }
}

/// <summary>Dividend (FR-MDF-003).</summary>
public class Dividend
{
    public long Id { get; set; }
    public long InstrumentId { get; set; }
    public DateOnly ExDate { get; set; }
    public DateOnly? PayDate { get; set; }
    public decimal AmountPerShare { get; set; }
    public string? Currency { get; set; }
    public string SourceRef { get; set; } = null!;
    public DateTimeOffset RecordedAt { get; set; }
}

/// <summary>Corporate action feeding the adjustment-factor computation (FR-MDF-004, BR-MDF-010).</summary>
public class CorporateAction
{
    public long Id { get; set; }
    public long InstrumentId { get; set; }

    /// <summary>split / rights_issue / bonus_issue / other.</summary>
    public string ActionType { get; set; } = null!;

    public DateOnly ActionDate { get; set; }

    /// <summary>jsonb — ratio and terms.</summary>
    public string? TermsJson { get; set; }

    public string SourceRef { get; set; } = null!;
    public DateTimeOffset RecordedAt { get; set; }
}

/// <summary>KAP disclosure metadata; documents live outside the DB (FR-MDF-005).</summary>
public class KapDisclosure
{
    public long Id { get; set; }
    public long InstrumentId { get; set; }
    public string? DisclosureType { get; set; }
    public DateOnly? PublishDate { get; set; }
    public string? Title { get; set; }
    public string? SourceUrl { get; set; }

    /// <summary>On-disk/VPS path; documents live outside the DB to keep it lean.</summary>
    public string? DocumentPath { get; set; }

    public string SourceRef { get; set; } = null!;
    public DateTimeOffset RecordedAt { get; set; }
}

/// <summary>
/// Derived metric written only by the Metrics Engine (MDF §7, FR-MDF-013/014/018/019).
/// Append-only by date; windowed CAGRs are window-suffixed codes (Q5, 02 §6.2).
/// </summary>
public class DerivedMetric
{
    public long InstrumentId { get; set; }
    public string MetricCode { get; set; } = null!;
    public DateOnly AsOfDate { get; set; }

    /// <summary>NULL = not meaningful (never 0-for-missing).</summary>
    public decimal? Value { get; set; }

    /// <summary>Actual window used for CAGRs (FR-MDF-014).</summary>
    public int? WindowYears { get; set; }

    public bool IsAdjusted { get; set; }
    public bool IsRested { get; set; }
    public DateTimeOffset ComputedAt { get; set; }
}

/// <summary>Coverage metadata per instrument/fund/universe data type (FR-MDF-011, FR-FDF-004).</summary>
public class CoverageMetadata
{
    public long Id { get; set; }

    /// <summary>instrument / fund / universe.</summary>
    public string Scope { get; set; } = null!;

    public long? InstrumentId { get; set; }

    /// <summary>Fund code (text) — funds are keyed by code (02 §3.7).</summary>
    public string? FundId { get; set; }

    public string DataType { get; set; } = null!;
    public DateOnly? AvailableFrom { get; set; }
    public DateOnly? AvailableTo { get; set; }
    public string? Notes { get; set; }
}

/// <summary>Scheduler run ledger (UC-MDF-001 alternates; backs <c>v_data_freshness</c>).</summary>
public class IngestRun
{
    public long Id { get; set; }
    public string JobCode { get; set; } = null!;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>succeeded / failed / partial.</summary>
    public string? Status { get; set; }

    /// <summary>jsonb counts.</summary>
    public string? StatsJson { get; set; }

    public string? Error { get; set; }
}

/// <summary>
/// Validation-failure store (v1.1; FR-MDF-012, BR-MDF-007). A fact failing
/// validation is never written to a fact table.
/// </summary>
public class QuarantinedFact
{
    public long Id { get; set; }
    public string JobCode { get; set; } = null!;
    public string? SourceRef { get; set; }

    /// <summary>jsonb — the rejected fact, retained for inspection and re-ingestion.</summary>
    public string? PayloadJson { get; set; }

    public string ReasonCode { get; set; } = null!;
    public DateTimeOffset QuarantinedAt { get; set; }

    /// <summary>open / dismissed.</summary>
    public string Status { get; set; } = null!;

    public string? ResolutionNote { get; set; }
}
