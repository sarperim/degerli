namespace Degerli.Ingestion.Metrics;

/// <summary>
/// Configuration for the nightly market snapshot (BR-MOV-011; 02 §6.3). The mover
/// volume-eligibility threshold is a config value set from the observed volume
/// distribution during V0 — never hard-coded; the default is the fixture/test value.
/// </summary>
public sealed class MarketSnapshotOptions
{
    public const string SectionName = "Ingestion:MarketSnapshot";

    /// <summary>Minimum traded volume (shares) for a stock to be mover-eligible.</summary>
    public decimal VolumeEligibilityThreshold { get; set; } =
        Degerli.Core.Metrics.MarketAggregates.DefaultVolumeEligibilityThreshold;
}
