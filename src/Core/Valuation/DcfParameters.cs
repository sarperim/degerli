namespace Degerli.Core.Valuation;

/// <summary>
/// The confirmed, fully user-editable DCF parameter set (OQ-UX-001; BR-VAL-002,
/// architecture `02-data-model.md` §6.4). All eight parameters are user input; the
/// model applies no "correctness" validation to the assumptions themselves
/// (RISK-VAL-001) — it only enforces the mathematical domain (see
/// <see cref="DcfModel.Compute"/>).
/// </summary>
/// <param name="BaseFcf">FCF₀, the base free cash flow. May be negative (the user's model).</param>
/// <param name="GrowthRate">g, the explicit-horizon free-cash-flow growth rate.</param>
/// <param name="HorizonYears">N, the explicit projection horizon; valid 1..10.</param>
/// <param name="TerminalGrowth">g_t, the perpetuity terminal growth rate.</param>
/// <param name="DiscountRate">r, the discount rate; the model requires r &gt; g_t.</param>
/// <param name="Debt">
/// Total debt (short-term + long-term), **not** net debt — per the F-VAL-1 builder
/// ruling (2026-10-07). <see cref="Cash"/> is a separate parameter and both affect
/// the equity value (NFR-VAL-004, no black box).
/// </param>
/// <param name="Cash">Cash and equivalents, added to the enterprise value.</param>
/// <param name="ShareCount">Shares diluted, the denominator of fair value per share.</param>
public sealed record DcfParameters(
    decimal BaseFcf,
    decimal GrowthRate,
    int HorizonYears,
    decimal TerminalGrowth,
    decimal DiscountRate,
    decimal Debt,
    decimal Cash,
    decimal ShareCount);
