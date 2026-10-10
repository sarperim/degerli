using System.Globalization;
using System.Text.Json;
using Degerli.Api.Infrastructure;
using Degerli.Core.Metrics;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Api.Screener;

/// <summary>
/// The screener core module (`03-api-design.md` §4; UC-SCR-001):
/// <list type="bullet">
/// <item><c>GET /api/v1/screener/metrics</c> — the criteria-builder's visible metric
/// catalog (five families, unit, TR/EN labels, <c>isCagr</c>, sort order). Serves only
/// <c>is_screenable = true</c> rows (FR-SCR-001, BR-SCR-002, UXR-SCR-011).</item>
/// <item><c>POST /api/v1/screener/run</c> — the ad-hoc run: criteria combine with AND
/// logic (BR-SCR-003) over the current universe's canonical <c>derived_metrics</c>; a
/// stock missing data for a criterion is excluded and counted, never zero-filled
/// (BR-SCR-009/FR-SCR-014); an empty result set is a normal <c>200</c> (FR-SCR-006);
/// an empty criteria array is <c>400 VALIDATION_FAILED</c> (I-SCR-1). Results are
/// computed on demand — never persisted (UC-SCR-003).</item>
/// </list>
/// This ticket covers the core only; validation breadth/staleness/recompute and the
/// saved-screen endpoints are later tickets (TKT-scr-003/004/005).
/// </summary>
public static class ScreenerEndpoints
{
    /// <summary>The index code whose current constituents form the covered universe.</summary>
    private const string UniverseIndexCode = "XU100";

    /// <summary>The nominal CAGR windows selectable per growth criterion (FR-SCR-015).</summary>
    private static readonly IReadOnlySet<int> CagrWindows = new HashSet<int>(MetricCodes.CagrWindows);

    public static IEndpointRouteBuilder MapDegerliScreenerEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/screener/metrics", MetricsAsync).AllowAnonymous();
        api.MapPost("/screener/run", RunAsync).AllowAnonymous();
        return api;
    }

    // ── Catalog ──────────────────────────────────────────────────────────────

    private static async Task<IResult> MetricsAsync(
        DegerliDbContext db,
        CancellationToken cancellationToken)
    {
        // Visible rows only (UXR-SCR-011); grouped by family in catalog sort order.
        var entries = await db.MetricCatalog
            .Where(entry => entry.IsScreenable)
            .OrderBy(entry => entry.SortOrder)
            .Select(entry => new
            {
                entry.MetricCode,
                entry.Family,
                entry.Unit,
                entry.LabelTr,
                entry.LabelEn,
                IsCagr = entry.IsGrowthCagr,
                entry.SortOrder,
            })
            .ToListAsync(cancellationToken);

        var families = entries
            .GroupBy(entry => entry.Family)
            .Select(group => new
            {
                family = group.Key,
                metrics = group.Select(entry => new
                {
                    entry.MetricCode,
                    entry.Unit,
                    entry.LabelTr,
                    entry.LabelEn,
                    entry.IsCagr,
                    entry.SortOrder,
                }).ToArray(),
            })
            .ToArray();

        return Results.Ok(new { families });
    }

    // ── Run ──────────────────────────────────────────────────────────────────

    private static async Task<IResult> RunAsync(
        HttpRequest request,
        DegerliDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return Validation(new[] { new ValidationField("body", "invalid_json") });
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Validation(new[] { new ValidationField("body", "invalid_json") });
            }

            if (!root.TryGetProperty("criteria", out var criteriaElement)
                || criteriaElement.ValueKind != JsonValueKind.Array
                || criteriaElement.GetArrayLength() == 0)
            {
                // At least one criterion is required (I-SCR-1, plan-specified).
                return Validation(new[] { new ValidationField("criteria", "required") });
            }

            var catalogEntries = await db.MetricCatalog
                .Where(entry => entry.IsScreenable)
                .Select(entry => new { entry.MetricCode, entry.IsGrowthCagr })
                .ToListAsync(cancellationToken);
            var catalog = catalogEntries.ToDictionary(
                entry => entry.MetricCode,
                entry => entry.IsGrowthCagr,
                StringComparer.Ordinal);

            var fields = new List<ValidationField>();
            var criteria = new List<Criterion>();
            foreach (var element in criteriaElement.EnumerateArray())
            {
                var criterion = ParseCriterion(element, catalog, fields);
                if (criterion is not null)
                {
                    criteria.Add(criterion);
                }
            }

            // A hidden/unknown metric is the dedicated availability error (03 §4);
            // other shape problems are ordinary validation failures.
            if (fields.Any(field => field.Code == "metric_not_available"))
            {
                return MetricNotAvailable();
            }

            if (fields.Count > 0)
            {
                return Validation(fields);
            }

            return await EvaluateAsync(db, clock, criteria, cancellationToken);
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

    private static async Task<IResult> EvaluateAsync(
        DegerliDbContext db,
        TimeProvider clock,
        IReadOnlyList<Criterion> criteria,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var asOf = await db.DerivedMetrics.MaxAsync(metric => (DateOnly?)metric.AsOfDate, cancellationToken) ?? today;
        var stale = asOf < LatestTradingDayOnOrBefore(today);

        var universe = await (
                from instrument in db.Instruments
                join constituent in db.IndexConstituents on instrument.Id equals constituent.InstrumentId
                join index in db.Indices on constituent.IndexId equals index.Id
                where index.Code == UniverseIndexCode && constituent.EffectiveTo == null
                orderby instrument.Symbol
                select new
                {
                    instrument.Id,
                    instrument.Symbol,
                    instrument.Name,
                    Sector = db.Sectors
                        .Where(sector => sector.Id == instrument.SectorId)
                        .Select(sector => sector.Code)
                        .FirstOrDefault(),
                })
            .ToListAsync(cancellationToken);

        var instrumentIds = universe.Select(row => row.Id).ToArray();
        var metricRows = await db.DerivedMetrics
            .Where(metric => metric.AsOfDate == asOf && instrumentIds.Contains(metric.InstrumentId))
            .Select(metric => new { metric.InstrumentId, metric.MetricCode, metric.Value })
            .ToListAsync(cancellationToken);

        var valuesByInstrument = metricRows
            .GroupBy(row => row.InstrumentId)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(row => row.MetricCode, row => row.Value, StringComparer.Ordinal));

        var matchCount = 0;
        var excludedCount = 0;
        var rows = new List<RunRow>();
        foreach (var instrument in universe)
        {
            valuesByInstrument.TryGetValue(instrument.Id, out var values);

            var matched = true;
            var excluded = false;
            var rowValues = new Dictionary<string, decimal?>(StringComparer.Ordinal);
            foreach (var criterion in criteria)
            {
                if (values is null
                    || !values.TryGetValue(criterion.StoredCode, out var value)
                    || value is null)
                {
                    // Missing data → excluded and counted; never a passing value (BR-SCR-009).
                    excluded = true;
                    break;
                }

                if (!WithinBound(value.Value, criterion))
                {
                    // Data is present but the stock simply does not match.
                    matched = false;
                    break;
                }

                rowValues[criterion.ConceptCode] = value;
            }

            if (excluded)
            {
                excludedCount++;
            }
            else if (matched)
            {
                matchCount++;
                rows.Add(new RunRow(instrument.Symbol, instrument.Name, instrument.Sector, rowValues));
            }
        }

        return Results.Ok(new
        {
            asOf,
            stale,
            matchCount,
            excludedCount,
            droppedCriteria = Array.Empty<string>(),
            rows,
        });
    }

    /// <summary>Inclusive bounds: <c>min ≤ v ≤ max</c> at the edges (I-SCR-2, TC-SCR-003).</summary>
    private static bool WithinBound(decimal value, Criterion criterion) => criterion.Bound switch
    {
        "min" => value >= criterion.MinValue!.Value,
        "max" => value <= criterion.MaxValue!.Value,
        "range" => value >= criterion.MinValue!.Value && value <= criterion.MaxValue!.Value,
        _ => false,
    };

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

    /// <summary>Ad-hoc submission of a hidden/unknown metric (UXR-SCR-011, `03` §4).</summary>
    private static IResult MetricNotAvailable()
        => Results.Problem(ApiProblem.Create(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.MetricNotAvailable));

    private static IResult Validation(IReadOnlyList<ValidationField> fields)
    {
        var payload = fields
            .Select(field => new { field = field.Field, code = field.Code })
            .ToArray();

        return Results.Problem(ApiProblem.Create(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationFailed,
            new Dictionary<string, object?> { ["fields"] = payload }));
    }

    /// <summary>The latest Mon–Fri trading day at or before <paramref name="date"/>.</summary>
    private static DateOnly LatestTradingDayOnOrBefore(DateOnly date)
    {
        while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            date = date.AddDays(-1);
        }

        return date;
    }

    private sealed record Criterion(
        string ConceptCode,
        string StoredCode,
        string Bound,
        decimal? MinValue,
        decimal? MaxValue);

    private sealed record RunRow(
        string Symbol,
        string Name,
        string? Sector,
        IReadOnlyDictionary<string, decimal?> Values);

    private readonly record struct ValidationField(string Field, string Code);
}
