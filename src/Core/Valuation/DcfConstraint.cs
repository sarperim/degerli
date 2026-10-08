namespace Degerli.Core.Valuation;

/// <summary>
/// The mathematical constraint that makes a DCF not computable (`03-api-design.md`
/// §5: `422 DCF_NOT_COMPUTABLE` with the violated constraint named). Only these two
/// constraints exist; malformed or out-of-domain input is a separate (validation)
/// concern handled by the API layer, not by the pure model.
/// </summary>
public enum DcfConstraint
{
    /// <summary>`discount_rate ≤ terminal_growth` — the perpetuity has no finite value.</summary>
    DiscountRateNotGreaterThanTerminalGrowth,

    /// <summary>`horizon_years` outside the inclusive 1..10 range.</summary>
    HorizonOutOfRange,
}
