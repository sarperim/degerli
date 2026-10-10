namespace Degerli.Core.Adjustments;

/// <summary>The corporate-action classes that carry an adjustment factor (02 §3.1).</summary>
public enum AdjustmentActionType
{
    /// <summary>Split 1-for-n (n new shares per 1 held).</summary>
    Split,

    /// <summary>Bonus issue of b new shares per 1 held.</summary>
    BonusIssue,

    /// <summary>Rights issue of q new shares per 1 held at a subscription price.</summary>
    RightsIssue,

    /// <summary>Any other action class — carries no adjustment factor.</summary>
    Other,
}

/// <summary>
/// One corporate action in the adjustment-factor system: its class and terms. For a
/// split, <see cref="Ratio"/> is n (1-for-n); for a bonus, b new shares per 1 held; for a
/// rights issue, q new shares per 1 held at <see cref="SubscriptionPrice"/>.
/// </summary>
public sealed record AdjustmentAction(
    AdjustmentActionType Type,
    decimal Ratio,
    decimal? SubscriptionPrice = null);

/// <summary>
/// The adjustment-factor system (BR-MDF-010; 02 §3.1, Q3 resolution 2026-10-07). Pure and
/// deterministic. A factor scales a pre-action raw price onto the post-action basis; the
/// adjusted close is the raw close multiplied by every factor of the actions that follow
/// it. Degenerate rights inputs are not a factor at all — <see cref="Factor"/> returns
/// <c>null</c> and downstream computation never divides by zero.
/// </summary>
public static class AdjustmentFactors
{
    /// <summary>The no-adjustment factor (a price already on the current basis).</summary>
    public const decimal None = 1m;

    /// <summary>Split 1-for-n → <c>1/n</c>; invalid (NULL) when n ≤ 0.</summary>
    public static decimal? Split(decimal n) => n > 0m ? 1m / n : null;

    /// <summary>Bonus of b new per 1 held → <c>1/(1+b)</c>; invalid (NULL) when b &lt; 0.</summary>
    public static decimal? Bonus(decimal b) => b >= 0m ? 1m / (1m + b) : null;

    /// <summary>
    /// Rights issue of q new per 1 held at subscription <paramref name="subscriptionPrice"/>
    /// with cum price <paramref name="cumPrice"/> (the last raw close on/before the action's
    /// effective date) → <c>(P_C + q·P_S)/((1+q)·P_C)</c> — the theoretical ex-rights price
    /// over the cum price. Invalid (NULL) when q ≤ 0 or P_C ≤ 0 (degenerate input: never a
    /// factor, never a division by zero).
    /// </summary>
    public static decimal? Rights(decimal q, decimal subscriptionPrice, decimal cumPrice)
    {
        if (q <= 0m || cumPrice <= 0m)
        {
            return null;
        }

        return (cumPrice + q * subscriptionPrice) / ((1m + q) * cumPrice);
    }

    /// <summary>
    /// The factor for <paramref name="action"/>; <paramref name="cumPrice"/> is required for
    /// a rights issue and ignored otherwise. Returns <c>null</c> when the action is
    /// degenerate (invalid) — the action is rejected by validation and never becomes a factor.
    /// </summary>
    public static decimal? Factor(AdjustmentAction action, decimal? cumPrice = null)
    {
        ArgumentNullException.ThrowIfNull(action);

        return action.Type switch
        {
            AdjustmentActionType.Split => Split(action.Ratio),
            AdjustmentActionType.BonusIssue => Bonus(action.Ratio),
            AdjustmentActionType.RightsIssue => cumPrice is null
                ? null
                : Rights(action.Ratio, action.SubscriptionPrice ?? 0m, cumPrice.Value),
            _ => None,
        };
    }

    /// <summary>Product of the factors; an empty sequence is the no-adjustment factor <c>1</c>.</summary>
    public static decimal Compose(IEnumerable<decimal> factors)
    {
        ArgumentNullException.ThrowIfNull(factors);

        var product = None;
        foreach (var factor in factors)
        {
            product *= factor;
        }

        return product;
    }
}
