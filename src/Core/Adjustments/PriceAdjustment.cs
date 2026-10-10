namespace Degerli.Core.Adjustments;

/// <summary>One raw EOD close feeding the adjusted-series derivation.</summary>
public sealed record DatedClose(DateOnly Date, decimal CloseRaw);

/// <summary>One corporate action dated by its effective date.</summary>
public sealed record DatedAction(DateOnly ActionDate, AdjustmentAction Action);

/// <summary>
/// Derives the corporate-action-adjusted close series from raw closes and actions
/// (<c>close_adjusted = close_raw × Π factors of all actions after the price date</c>;
/// FR-MDF-018, BR-MDF-010, 02 §3.1). A rights factor uses the last raw close on/before the
/// action's effective date as its cum price. Degenerate actions contribute no factor — they
/// are rejected upstream and never divided by. Pure and deterministic.
/// </summary>
public static class PriceAdjustment
{
    /// <summary>
    /// The adjusted closes, aligned index-for-index with <paramref name="closes"/>. Actions
    /// dated strictly after a price date adjust it; an action on the price date itself does
    /// not (that day already trades on the post-action basis).
    /// </summary>
    public static IReadOnlyList<decimal> AdjustedCloses(
        IReadOnlyList<DatedClose> closes,
        IReadOnlyList<DatedAction> actions)
    {
        ArgumentNullException.ThrowIfNull(closes);
        ArgumentNullException.ThrowIfNull(actions);

        // A rights factor needs the last raw close on/before the action's effective date as
        // its cum price; resolve each action's factor once, then apply the actions that
        // strictly follow each price date (multiplicative composition, FU §2/§4).
        var byDate = closes.OrderBy(c => c.Date).ToList();
        var dated = new List<(DateOnly Date, decimal Factor)>(actions.Count);
        foreach (var action in actions)
        {
            decimal? cumPrice = null;
            for (var i = byDate.Count - 1; i >= 0; i--)
            {
                if (byDate[i].Date <= action.ActionDate)
                {
                    cumPrice = byDate[i].CloseRaw;
                    break;
                }
            }

            var factor = AdjustmentFactors.Factor(action.Action, cumPrice);
            if (factor is not null)
            {
                dated.Add((action.ActionDate, factor.Value));
            }
        }

        var result = new decimal[closes.Count];
        for (var i = 0; i < closes.Count; i++)
        {
            var product = AdjustmentFactors.None;
            foreach (var (date, factor) in dated)
            {
                if (date > closes[i].Date)
                {
                    product *= factor;
                }
            }

            result[i] = closes[i].CloseRaw * product;
        }

        return result;
    }
}
