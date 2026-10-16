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

    /// <summary>
    /// The cumulative adjustment factor Π applied to a value on
    /// <paramref name="basisDate"/>: the product of the factors of every action dated
    /// strictly after it (the same "actions after the fact" rule as the adjusted close).
    /// Reused for the per-share CAGR basis (FR-MDF-018, 02 §6.2) — a fiscal period end for
    /// EPS, the ex-date for DPS. A rights action needs its cum price via
    /// <paramref name="cumPrice"/> (the last raw close on/before the action); without it a
    /// rights action contributes no factor (never a guess). An empty action set is the
    /// no-adjustment factor <c>1</c>.
    /// </summary>
    public static decimal FactorAfter(
        DateOnly basisDate,
        IReadOnlyList<DatedAction> actions,
        Func<DatedAction, decimal?>? cumPrice = null)
    {
        ArgumentNullException.ThrowIfNull(actions);

        var product = AdjustmentFactors.None;
        foreach (var action in actions)
        {
            if (action.ActionDate <= basisDate)
            {
                continue;
            }

            var factor = AdjustmentFactors.Factor(action.Action, cumPrice?.Invoke(action));
            if (factor is not null)
            {
                product *= factor.Value;
            }
        }

        return product;
    }
}
