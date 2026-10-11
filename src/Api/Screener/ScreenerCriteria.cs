using System.Globalization;
using System.Text.Json;
using Degerli.Core.Metrics;

namespace Degerli.Api.Screener;

/// <summary>
/// Shared criterion parsing/validation for the screener (`03-api-design.md` §4). A
/// criterion references a metric the builder currently offers (unknown/hidden →
/// <c>metric_not_available</c>), carries a well-formed bound, and — for growth metrics
/// only — one of the selectable CAGR windows (BR-SCR-005, I-SCR-4). This lives in one
/// place so a saved screen is validated exactly like an ad-hoc run: both reject the
/// same malformed payloads with the same <c>params.fields[]</c> detail (TC-SCR-016).
/// </summary>
internal static class ScreenerCriteria
{
    /// <summary>The nominal CAGR windows selectable per growth criterion (FR-SCR-015).</summary>
    internal static readonly IReadOnlySet<int> CagrWindows = new HashSet<int>(MetricCodes.CagrWindows);

    /// <summary>A single field-level validation failure, surfaced as <c>params.fields[]</c>.</summary>
    internal readonly record struct ValidationField(string Field, string Code);

    /// <summary>
    /// One parsed, validated criterion. <see cref="ConceptCode"/> is the wire metric
    /// code (<c>rev_cagr</c>); <see cref="StoredCode"/> is the window-suffixed
    /// <c>derived_metrics</c> key the run reads (<c>rev_cagr_5y</c>, Q5).
    /// </summary>
    internal sealed record Criterion(
        string ConceptCode,
        string StoredCode,
        string Bound,
        decimal? MinValue,
        decimal? MaxValue);

    /// <summary>True when the failures include the dedicated hidden/unknown-metric code.</summary>
    internal static bool HasMetricNotAvailable(IEnumerable<ValidationField> fields)
        => fields.Any(field => field.Code == "metric_not_available");

    /// <summary>
    /// Parses the required <c>criteria</c> array from a request root. A missing or
    /// empty array is a single <c>criteria: required</c> failure (I-SCR-1).
    /// </summary>
    internal static void ParseRoot(
        JsonElement root,
        IReadOnlyDictionary<string, bool> catalog,
        List<Criterion> criteria,
        List<ValidationField> fields)
    {
        if (!root.TryGetProperty("criteria", out var criteriaElement))
        {
            fields.Add(new ValidationField("criteria", "required"));
            return;
        }

        ParseArray(criteriaElement, catalog, criteria, fields);
    }

    /// <summary>
    /// Parses a criterion array element-by-element. Non-array or empty input yields the
    /// single <c>criteria: required</c> failure; every element must validate.
    /// </summary>
    internal static void ParseArray(
        JsonElement criteriaElement,
        IReadOnlyDictionary<string, bool> catalog,
        List<Criterion> criteria,
        List<ValidationField> fields)
    {
        if (criteriaElement.ValueKind != JsonValueKind.Array || criteriaElement.GetArrayLength() == 0)
        {
            fields.Add(new ValidationField("criteria", "required"));
            return;
        }

        foreach (var element in criteriaElement.EnumerateArray())
        {
            var criterion = ParseCriterion(element, catalog, fields);
            if (criterion is not null)
            {
                criteria.Add(criterion);
            }
        }
    }

    /// <summary>
    /// Parses one criterion against the visible catalog: the metric must be offered
    /// (unknown/hidden → <c>METRIC_NOT_AVAILABLE</c>), the bound must be well-formed,
    /// and the CAGR window must be one of 3/5/10 on a growth metric (and absent otherwise).
    /// </summary>
    private static Criterion? ParseCriterion(
        JsonElement element,
        IReadOnlyDictionary<string, bool> catalog,
        List<ValidationField> fields)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            fields.Add(new ValidationField("criteria", "invalid_json"));
            return null;
        }

        var code = element.TryGetProperty("metricCode", out var codeElement) && codeElement.ValueKind == JsonValueKind.String
            ? codeElement.GetString()!
            : null;
        if (code is null || !catalog.TryGetValue(code, out var isGrowth))
        {
            // The builder never offers a hidden/unknown metric (UXR-SCR-011); surfaced
            // to the caller through the dedicated code (03 §4).
            fields.Add(new ValidationField(code ?? "metricCode", "metric_not_available"));
            return null;
        }

        var bound = element.TryGetProperty("bound", out var boundElement) && boundElement.ValueKind == JsonValueKind.String
            ? boundElement.GetString()
            : null;
        if (bound is not ("min" or "max" or "range"))
        {
            fields.Add(new ValidationField("bound", "invalid"));
            return null;
        }

        var hasMin = TryReadNumber(element, "minValue", out var minValue);
        var hasMax = TryReadNumber(element, "maxValue", out var maxValue);

        if (bound is "min" && !hasMin)
        {
            fields.Add(new ValidationField("minValue", "required"));
            return null;
        }

        if (bound is "max" && !hasMax)
        {
            fields.Add(new ValidationField("maxValue", "required"));
            return null;
        }

        if (bound is "range" && (!hasMin || !hasMax))
        {
            fields.Add(new ValidationField(hasMin ? "maxValue" : "minValue", "required"));
            return null;
        }

        if (bound is "range" && minValue > maxValue)
        {
            fields.Add(new ValidationField("minValue", "out_of_range"));
            return null;
        }

        int? window = null;
        if (isGrowth)
        {
            if (!TryReadInt(element, "window", out var parsedWindow) || !CagrWindows.Contains(parsedWindow))
            {
                fields.Add(new ValidationField("window", "invalid"));
                return null;
            }

            window = parsedWindow;
        }
        else if (element.TryGetProperty("window", out _))
        {
            // Windows tie to growth metrics only (BR-SCR-005 / I-SCR-4).
            fields.Add(new ValidationField("window", "invalid"));
            return null;
        }

        // The stored code is window-suffixed for CAGRs (Q5: rev_cagr_3y/5y/10y).
        var storedCode = isGrowth ? $"{code}_{window}y" : code;
        return new Criterion(code, storedCode, bound, hasMin ? minValue : null, hasMax ? maxValue : null);
    }

    private static bool TryReadNumber(JsonElement element, string name, out decimal value)
    {
        value = default;
        return element.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && decimal.TryParse(property.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadInt(JsonElement element, string name, out int value)
    {
        value = default;
        return element.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out value);
    }
}
