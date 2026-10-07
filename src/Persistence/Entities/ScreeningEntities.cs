namespace Degerli.Persistence.Entities;

/// <summary>
/// Screenable metric catalog (SCR §7; BR-SCR-002). Serves plain-language TR/EN
/// labels and visibility flags; never stores values (02 §5.3).
/// </summary>
public class MetricCatalogEntry
{
    public string MetricCode { get; set; } = null!;

    /// <summary>valuation / quality / growth / financial_health / dividends.</summary>
    public string Family { get; set; } = null!;

    public string? Unit { get; set; }
    public string LabelTr { get; set; } = null!;
    public string LabelEn { get; set; } = null!;
    public string? DescriptionTr { get; set; }
    public string? DescriptionEn { get; set; }

    /// <summary>Builder toggle, no code change (FR-SCR-017).</summary>
    public bool IsScreenable { get; set; }

    /// <summary>Growth metrics carry a window selector (FR-SCR-015).</summary>
    public bool IsGrowthCagr { get; set; }

    public int SortOrder { get; set; }
}

/// <summary>Named saved screen; results are never persisted (SCR §7, UC-SCR-003).</summary>
public class SavedScreen
{
    public long Id { get; set; }

    /// <summary>FK to <c>asp_net_users</c>, cascade delete (BR-ACC-005).</summary>
    public long UserId { get; set; }

    public string Name { get; set; } = null!;

    /// <summary>jsonb — ordered array of criteria.</summary>
    public string CriteriaJson { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
