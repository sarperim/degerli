namespace Degerli.Persistence.Entities;

/// <summary>Macro indicator series definition (MOV §7). Six rows in V1 (BR-MOV-008).</summary>
public class MacroSeries
{
    public string Code { get; set; } = null!;
    public string NameTr { get; set; } = null!;
    public string NameEn { get; set; } = null!;
    public string? Unit { get; set; }
    public string? SourceName { get; set; }

    /// <summary>daily / monthly / per_release.</summary>
    public string? Cadence { get; set; }
}

/// <summary>
/// Macro indicator value; revisions append as new <c>recorded_at</c> rows and the
/// canonical value is the latest (FR-MOV-014, 02 §5.6).
/// </summary>
public class MacroValue
{
    public string SeriesCode { get; set; } = null!;
    public DateOnly ValueDate { get; set; }
    public decimal Value { get; set; }
    public string SourceRef { get; set; } = null!;
    public DateTimeOffset RecordedAt { get; set; }
}

/// <summary>Market overview snapshot, one per trading day (MOV §7; BR-MOV-009).</summary>
public class MarketSnapshot
{
    public DateOnly SnapshotDate { get; set; }
    public decimal? Xu100Level { get; set; }
    public decimal? Xu100ChangePct { get; set; }
    public decimal? Xu30Level { get; set; }
    public decimal? Xu30ChangePct { get; set; }
    public int? BreadthAdvancing { get; set; }
    public int? BreadthDeclining { get; set; }
    public int? BreadthUnchanged { get; set; }
    public decimal? VolumeTotal { get; set; }
    public decimal? MarketPe { get; set; }
    public int? MarketPeExcludedCount { get; set; }
    public decimal? MarketDivYield { get; set; }

    /// <summary>jsonb — top-10 gainer entries.</summary>
    public string? GainersJson { get; set; }

    /// <summary>jsonb — top-10 loser entries.</summary>
    public string? LosersJson { get; set; }

    /// <summary>jsonb — sector to daily change.</summary>
    public string? SectorPerfJson { get; set; }

    /// <summary>Honesty stamp — trading date the snapshot describes.</summary>
    public DateOnly AsOfTradingDate { get; set; }

    public DateTimeOffset ComputedAt { get; set; }
}
