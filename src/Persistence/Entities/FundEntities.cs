namespace Degerli.Persistence.Entities;

/// <summary>Fund (FDF §7; BR-FDF-006). Ingest bounded to equity + equity-heavy mixed.</summary>
public class Fund
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;

    /// <summary>TEFAS classification.</summary>
    public string? FundType { get; set; }

    public string? Status { get; set; }
    public string SourceRef { get; set; } = null!;
    public DateTimeOffset RecordedAt { get; set; }
}

/// <summary>Fund NAV; one per fund per publication day.</summary>
public class FundNav
{
    public string FundId { get; set; } = null!;
    public DateOnly NavDate { get; set; }
    public decimal NavValue { get; set; }
    public string SourceRef { get; set; } = null!;
    public DateTimeOffset RecordedAt { get; set; }
}

/// <summary>Fund performance as published.</summary>
public class FundPerformance
{
    public string FundId { get; set; } = null!;

    /// <summary>1M / 3M / 1Y / …</summary>
    public string Period { get; set; } = null!;

    public DateOnly AsOfDate { get; set; }
    public decimal? ReturnValue { get; set; }
    public string SourceRef { get; set; } = null!;
    public DateTimeOffset RecordedAt { get; set; }
}

/// <summary>
/// Fund holding snapshot line (FDF §7; FR-FDF-003/004). <c>InstrumentId</c> is the
/// future look-through link, symbol-matched where possible, unused in V1.
/// </summary>
public class FundHolding
{
    public string FundId { get; set; } = null!;
    public DateOnly AsOfDate { get; set; }
    public int LineNo { get; set; }

    public long? InstrumentId { get; set; }
    public string? NameRaw { get; set; }
    public decimal? Weight { get; set; }
    public decimal? Units { get; set; }
    public string SourceRef { get; set; } = null!;
    public DateTimeOffset RecordedAt { get; set; }
}
