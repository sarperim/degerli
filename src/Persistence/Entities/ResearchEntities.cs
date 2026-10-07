namespace Degerli.Persistence.Entities;

/// <summary>
/// Bilingual business description version (RES §7; BR-RES-002/003). Serving rule:
/// latest <c>published</c> version per instrument (FR-RES-026).
/// </summary>
public class BusinessDescription
{
    public long Id { get; set; }
    public long InstrumentId { get; set; }

    /// <summary>Draft lineage; one stock has many versions.</summary>
    public int Version { get; set; }

    /// <summary>draft / reviewed / published.</summary>
    public string Status { get; set; } = null!;

    /// <summary>NULLable until complete; the published CHECK requires both (BR-RES-003).</summary>
    public string? TextTr { get; set; }

    /// <summary>NULLable until complete; the published CHECK requires both (BR-RES-003).</summary>
    public string? TextEn { get; set; }

    /// <summary>jsonb — KAP disclosure ids used.</summary>
    public string? SourceRefsJson { get; set; }

    public DateTimeOffset? LastReviewedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

/// <summary>
/// Sector metric median computed by the Metrics Engine (RES §7; UXR-RES-024).
/// Five valuation-family metrics only, current basis.
/// </summary>
public class SectorMetricMedian
{
    public long SectorId { get; set; }
    public string MetricCode { get; set; } = null!;
    public DateOnly AsOfDate { get; set; }
    public decimal? MedianValue { get; set; }
    public int? PeerCount { get; set; }

    /// <summary>P/E loss-maker exclusion disclosure.</summary>
    public int? ExcludedCount { get; set; }

    public bool IsAdjusted { get; set; }
    public DateTimeOffset ComputedAt { get; set; }
}
