namespace Degerli.Core.Metrics;

/// <summary>
/// One observation in a fiscal-year series (02 §6.2 growth metrics). <see cref="Value"/>
/// is NULL when the fact is honestly missing (e.g. no CF statement → no FCF).
/// </summary>
public sealed record FiscalValue(int FiscalYear, decimal? Value);

/// <summary>
/// The result of a CAGR computation (FR-MDF-014): the value and the number of
/// compounding intervals actually used. <see cref="WindowYears"/> is NULL exactly when
/// <see cref="Value"/> is NULL (not meaningful — never <c>0</c>-for-missing).
/// </summary>
public sealed record CagrOutcome(decimal? Value, int? WindowYears);

/// <summary>
/// The shared CAGR definition (BR-MDF-009; AD-06). Pure and deterministic.
/// <para>
/// Window W (3/5/10Y) = W compounding intervals between endpoint FYs; when history is
/// shorter than the nominal window the computation falls back to the available span and
/// reports the intervals actually used (FR-MDF-014, FU §2, Q4 reading).
/// </para>
/// </summary>
public static class MetricMath
{
    /// <summary>The fallback effective tax rate when PRETAX ≤ 0 or TAX is missing (02 §6.2 roic note).</summary>
    public const decimal EffectiveTaxRateFallback = 0.25m;

    /// <summary>
    /// CAGR over <paramref name="series"/>. Returns a NULL value when fewer than two
    /// points are available, when either endpoint is non-positive (undefined ratio), or
    /// when <paramref name="nullOnSignChange"/> is set and any adjacent non-zero pair in
    /// the used span changes sign (the honest <c>fcf_cagr</c> rule, 02 §6.2).
    /// </summary>
    /// <param name="series">Fiscal-year observations (any order; sorted internally).</param>
    /// <param name="nominalWindowYears">The requested window (3/5/10) in compounding intervals.</param>
    /// <param name="nullOnSignChange">Honest-NULL on a sign change (FCF only).</param>
    public static CagrOutcome Cagr(
        IReadOnlyList<FiscalValue> series,
        int nominalWindowYears,
        bool nullOnSignChange = false)
    {
        ArgumentNullException.ThrowIfNull(series);

        if (series.Count < 2)
        {
            return new CagrOutcome(null, null);
        }

        var ordered = series.OrderBy(p => p.FiscalYear).ToList();

        // Window W = W compounding intervals between endpoint fiscal years (FU §2). When
        // history is shorter, fall back to the earliest available FY and report the
        // intervals actually used (FR-MDF-014).
        var lastYear = ordered[^1].FiscalYear;
        var targetYear = lastYear - nominalWindowYears;
        var start = ordered.FindIndex(p => p.FiscalYear == targetYear);
        if (start < 0)
        {
            start = 0;
        }

        var intervals = lastYear - ordered[start].FiscalYear;
        if (intervals < 1)
        {
            return new CagrOutcome(null, null);
        }

        var first = ordered[start].Value;
        var last = ordered[^1].Value;

        if (first is null || last is null || first.Value <= 0m || last.Value <= 0m)
        {
            return new CagrOutcome(null, null);
        }

        if (nullOnSignChange && HasSignChange(ordered, start))
        {
            return new CagrOutcome(null, null);
        }

        var ratio = last.Value / first.Value;
        var value = intervals == 1
            ? ratio - 1m
            : Pow(ratio, 1.0 / intervals) - 1m;

        return new CagrOutcome(value, intervals);
    }

    /// <summary>
    /// Effective tax rate for ROIC: <c>TAX / PRETAX</c> when PRETAX &gt; 0 and TAX is
    /// present, else the documented 0.25 fallback (02 §6.2).
    /// </summary>
    public static decimal EffectiveTaxRate(decimal? taxExpense, decimal? pretaxIncome)
    {
        if (taxExpense is null || pretaxIncome is null || pretaxIncome.Value <= 0m)
        {
            return EffectiveTaxRateFallback;
        }

        return taxExpense.Value / pretaxIncome.Value;
    }

    /// <summary>Detects a sign change among the non-zero values in the used span.</summary>
    private static bool HasSignChange(IReadOnlyList<FiscalValue> ordered, int start)
    {
        decimal? previous = null;
        for (var i = start; i < ordered.Count; i++)
        {
            var value = ordered[i].Value;
            if (value is null || value.Value == 0m)
            {
                continue;
            }

            if (previous is not null && (previous.Value < 0m) != (value.Value < 0m))
            {
                return true;
            }

            previous = value;
        }

        return false;
    }

    /// <summary>Decimal power via the platform double (deterministic, ~1e-15 relative).</summary>
    private static decimal Pow(decimal value, double exponent) =>
        (decimal)Math.Pow((double)value, exponent);
}
