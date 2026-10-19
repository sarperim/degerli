namespace Degerli.Ingestion.Macro;

/// <summary>
/// Deterministic conversion of the TÜİK CPI <b>index level</b> into the canonical annual
/// (% YoY) measure (01 v1.5.6 §10.9.12). The EVDS 3.0 platform publishes the CPI only as an
/// index (the <c>bie_tukfiy2025</c> datagroup carries 349 monthly index series and no
/// published annual-change series — live-verified 2026-10-18), so the adapter pairs each
/// month's index with the year-prior month's index and computes
/// <c>YoY = (indexₘ / indexₘ₋₁₂ − 1) × 100</c>. This is a <b>deterministic derivation from
/// two real index observations</b> — never an invented value — recorded as the ticket's CPI
/// code-pin resolution.
/// </summary>
public static class EvdsCpiYoY
{
    /// <summary>
    /// Computes the annual % change for every observation month inside
    /// <paramref name="from"/>..<paramref name="to"/> that has a year-prior month present in
    /// <paramref name="indexLevels"/> (the CPI index rows decoded from the wider fetch window).
    /// </summary>
    public static IReadOnlyList<MacroItem> Compute(
        IReadOnlyList<MacroItem> indexLevels,
        DateOnly from,
        DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(indexLevels);

        if (indexLevels.Count == 0)
        {
            return [];
        }

        // Index the monthly levels by their month (a monthly observation is the month itself;
        // the decoder resolves a yyyy-MM date to the month's first day).
        var byMonth = new Dictionary<DateOnly, decimal>();
        foreach (var level in indexLevels)
        {
            var month = new DateOnly(level.ValueDate.Year, level.ValueDate.Month, 1);
            byMonth[month] = level.Value;
        }

        var result = new List<MacroItem>();
        foreach (var month in byMonth.Keys.OrderBy(m => m))
        {
            if (month < from || month > to)
            {
                continue;
            }

            if (!byMonth.TryGetValue(month.AddMonths(-12), out var priorYear) || priorYear == 0m)
            {
                continue;
            }

            var yoy = ComputeYoY(byMonth[month], priorYear);
            if (yoy is null)
            {
                continue;
            }

            result.Add(new MacroItem(MacroSeries.TuikCpi, month, yoy.Value, Unit: null, RecordedAt: null));
        }

        return result;
    }

    /// <summary>
    /// <c>(indexₘ / indexₘ₋₁₂ − 1) × 100</c> for two untrusted upstream index levels. A
    /// ratio outside the <see cref="decimal"/> range (a near-zero baseline against a huge
    /// index) yields <c>null</c> — the month is skipped rather than crashing the run with an
    /// <see cref="OverflowException"/> outside the source-failure taxonomy.
    /// </summary>
    private static decimal? ComputeYoY(decimal current, decimal priorYear)
    {
        try
        {
            return (current / priorYear - 1m) * 100m;
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}
