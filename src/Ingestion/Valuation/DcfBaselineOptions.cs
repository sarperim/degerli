namespace Degerli.Ingestion.Valuation;

/// <summary>
/// Builder-defined default rules for the DCF baseline's non-fact parameters
/// (<c>02</c> §5.8: "builder default rules for growth/horizon/rates (config)"). The
/// fact-derived parameters (base FCF, total debt, cash, share count) come from the
/// canonical statements; only growth rate, horizon, terminal growth and discount rate
/// are builder policy. The defaults reproduce FU §11.5's ALFA baseline.
/// </summary>
public sealed class DcfBaselineOptions
{
    public const string SectionName = "Valuation:DcfBaseline";

    /// <summary>Explicit-horizon FCF growth rate (g).</summary>
    public decimal GrowthRate { get; set; } = 0.10m;

    /// <summary>Explicit projection horizon in years (N).</summary>
    public int HorizonYears { get; set; } = 5;

    /// <summary>Perpetuity terminal growth rate (g_t).</summary>
    public decimal TerminalGrowth { get; set; } = 0.03m;

    /// <summary>Discount rate (r).</summary>
    public decimal DiscountRate { get; set; } = 0.12m;
}
