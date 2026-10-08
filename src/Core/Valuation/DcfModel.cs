namespace Degerli.Core.Valuation;

/// <summary>
/// The canonical DCF fair-value model — the single formula shared by every caller
/// (AD-08, `02-data-model.md` §6.4). Pure and stateless: identical inputs always
/// produce identical outputs; there is no clock, no randomness and no hidden state,
/// and nothing beyond the eight parameters and the observed price affects the result
/// (NFR-VAL-004). This is the one definition of the number (BR-MDF-009); clients and
/// the API never re-implement it.
/// </summary>
/// <remarks>
/// <code>
/// PV_explicit = Σ_{t=1..N}  FCF₀·(1+g)^t / (1+r)^t
/// TV           = FCF₀·(1+g)^N·(1+g_t) / (r − g_t)          [requires r > g_t]
/// EquityValue  = PV_explicit + TV/(1+r)^N + Cash − Debt
/// FairValuePS  = EquityValue / SharesDiluted
/// MOS          = (FairValuePS − Price) / FairValuePS
/// </code>
/// </remarks>
public static class DcfModel
{
    /// <summary>Default sensitivity grid size (5×5, FR-VAL-007).</summary>
    public const int DefaultSensitivitySize = 5;

    /// <summary>Default sensitivity axis step (0.01 on both axes, I-VAL-3).</summary>
    public const decimal DefaultSensitivityStep = 0.01m;

    /// <summary>The inclusive valid explicit-horizon range (UXR-VAL-009 analog; I-VAL-2).</summary>
    public const int MinHorizonYears = 1;
    public const int MaxHorizonYears = 10;

    /// <summary>
    /// Computes the DCF point result for the given parameters and observed price.
    /// Returns a not-computable outcome (never throws for a violated math constraint)
    /// when the horizon is outside 1..10 or the discount rate is not above the
    /// terminal growth rate.
    /// </summary>
    public static DcfComputation Compute(DcfParameters parameters, decimal price)
    {
        var constraint = Validate(parameters);
        if (constraint is not null)
        {
            return new DcfComputation(IsComputable: false, Result: null, constraint);
        }

        var years = parameters.HorizonYears;
        var growthFactor = 1m + parameters.GrowthRate;
        var discountFactor = 1m + parameters.DiscountRate;

        var pvExplicit = 0m;
        for (var t = 1; t <= years; t++)
        {
            pvExplicit += parameters.BaseFcf * Pow(growthFactor, t) / Pow(discountFactor, t);
        }

        var terminalValue = parameters.BaseFcf
            * Pow(growthFactor, years)
            * (1m + parameters.TerminalGrowth)
            / (parameters.DiscountRate - parameters.TerminalGrowth);

        var terminalValuePresentValue = terminalValue / Pow(discountFactor, years);

        var equityValue = pvExplicit + terminalValuePresentValue + parameters.Cash - parameters.Debt;
        var fairValuePerShare = equityValue / parameters.ShareCount;
        var marginOfSafety = (fairValuePerShare - price) / fairValuePerShare;

        var result = new DcfPointResult(
            PvExplicit: pvExplicit,
            TerminalValue: terminalValue,
            TerminalValuePresentValue: terminalValuePresentValue,
            EquityValue: equityValue,
            FairValuePerShare: fairValuePerShare,
            MarginOfSafety: marginOfSafety);

        return new DcfComputation(IsComputable: true, result, Constraint: null);
    }

    /// <summary>
    /// Builds the sensitivity grid (FR-VAL-007, SD-001): a square grid of fair values
    /// across discount-rate (rows) × terminal-growth (columns) axes, each stepped by
    /// <paramref name="step"/> and centered on the user's values, with all other
    /// parameters held at the request values. Cells whose own assumptions violate a
    /// constraint (r ≤ g_t) are <c>null</c> (I-VAL-1).
    /// </summary>
    public static DcfSensitivityGrid ComputeSensitivity(
        DcfParameters parameters,
        int size = DefaultSensitivitySize,
        decimal step = DefaultSensitivityStep)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);

        var discountRates = Axis(parameters.DiscountRate, size, step);
        var terminalGrowths = Axis(parameters.TerminalGrowth, size, step);

        var fairValues = new List<IReadOnlyList<decimal?>>(discountRates.Count);
        foreach (var discountRate in discountRates)
        {
            var row = new List<decimal?>(terminalGrowths.Count);
            foreach (var terminalGrowth in terminalGrowths)
            {
                var cell = Compute(
                    parameters with { DiscountRate = discountRate, TerminalGrowth = terminalGrowth },
                    price: 0m);

                row.Add(cell.IsComputable ? cell.Result!.FairValuePerShare : null);
            }

            fairValues.Add(row);
        }

        return new DcfSensitivityGrid(discountRates, terminalGrowths, fairValues);
    }

    private static DcfConstraint? Validate(DcfParameters parameters)
    {
        if (parameters.HorizonYears < MinHorizonYears || parameters.HorizonYears > MaxHorizonYears)
        {
            return DcfConstraint.HorizonOutOfRange;
        }

        if (parameters.DiscountRate <= parameters.TerminalGrowth)
        {
            return DcfConstraint.DiscountRateNotGreaterThanTerminalGrowth;
        }

        return null;
    }

    /// <summary>Builds an odd <paramref name="size"/>-point axis centered on <paramref name="center"/>.</summary>
    private static IReadOnlyList<decimal> Axis(decimal center, int size, decimal step)
    {
        var half = size / 2;
        var values = new decimal[size];
        for (var i = 0; i < size; i++)
        {
            values[i] = center + (i - half) * step;
        }

        return values;
    }

    /// <summary>Exact integer power by binary exponentiation (decimal, deterministic).</summary>
    private static decimal Pow(decimal value, int exponent)
    {
        var result = 1m;
        var factor = value;
        var e = exponent;
        while (e > 0)
        {
            if ((e & 1) == 1)
            {
                result *= factor;
            }

            e >>= 1;
            if (e > 0)
            {
                factor *= factor;
            }
        }

        return result;
    }
}
