namespace Degerli.Core.Valuation;

/// <summary>
/// The single-point DCF result (architecture `02-data-model.md` §6.4; AD-08). All
/// figures are exact decimal arithmetic — the model is stateless and deterministic
/// (NFR-VAL-004: nothing beyond the parameters and the observed price affects them).
/// </summary>
/// <param name="PvExplicit">PV of the explicit-horizon cash flows.</param>
/// <param name="TerminalValue">Undiscounted terminal value TV.</param>
/// <param name="TerminalValuePresentValue">Terminal value discounted to today, TV/(1+r)^N.</param>
/// <param name="EquityValue">PV_explicit + TV_pv + Cash − Debt (Debt = total debt).</param>
/// <param name="FairValuePerShare">EquityValue / ShareCount.</param>
/// <param name="MarginOfSafety">(FairValuePerShare − Price) / FairValuePerShare.</param>
public sealed record DcfPointResult(
    decimal PvExplicit,
    decimal TerminalValue,
    decimal TerminalValuePresentValue,
    decimal EquityValue,
    decimal FairValuePerShare,
    decimal MarginOfSafety);

/// <summary>
/// Outcome of a DCF point computation: either a computable <see cref="Result"/> or the
/// <see cref="Constraint"/> that makes it not computable. The API layer maps the
/// not-computable case to `422 DCF_NOT_COMPUTABLE` (I-VAL-2).
/// </summary>
public sealed record DcfComputation(
    bool IsComputable,
    DcfPointResult? Result,
    DcfConstraint? Constraint);
