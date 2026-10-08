namespace Degerli.Core.Valuation;

/// <summary>
/// The 5×5 sensitivity grid (FR-VAL-007, BR-VAL-009, SD-001): fair value per share
/// across axes of <c>discount_rate</c> × <c>terminal_growth</c>, both stepped by 0.01
/// and centered on the user's values (I-VAL-3).
/// </summary>
/// <param name="DiscountRates">Discount-rate axis, ascending (rows of <paramref name="FairValues"/>).</param>
/// <param name="TerminalGrowths">Terminal-growth axis, ascending (columns of <paramref name="FairValues"/>).</param>
/// <param name="FairValues">
/// Fair value per share for each (discount rate, terminal growth) cell, indexed
/// <c>[discountRateIndex][terminalGrowthIndex]</c>. A cell is <c>null</c> when the cell's
/// own assumptions violate a constraint — specifically when <c>r ≤ g_t</c> (I-VAL-1,
/// honest not-computable hole).
/// </param>
public sealed record DcfSensitivityGrid(
    IReadOnlyList<decimal> DiscountRates,
    IReadOnlyList<decimal> TerminalGrowths,
    IReadOnlyList<IReadOnlyList<decimal?>> FairValues);
