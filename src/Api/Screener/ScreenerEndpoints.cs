using System.Globalization;
using System.Text.Json;
using Degerli.Api.Infrastructure;
using Degerli.Core.Metrics;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
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
/// computed on demand — never persisted (UC-SCR-003). A hidden/unknown metric in an
/// ad-hoc submission is <c>400 METRIC_NOT_AVAILABLE</c> (UXR-SCR-011); a run that also
/// names the saved screen it re-runs (<c>savedScreenId</c>, requiring the caller's
/// session) instead drops since-hidden criteria into <c>droppedCriteria</c>
/// (UC-SCR-003a, UXR-SCR-015).</item>
/// </list>
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
        UserManager<ApplicationUser> userManager,
        HttpContext context,
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

            // A saved-screen re-run names the screen it re-runs (`savedScreenId`) as
            // well as the stored criteria. Naming the screen marks the run as a re-run,
            // so a metric hidden since the screen was saved is dropped and reported
            // rather than rejected (UC-SCR-003a, UXR-SCR-015; `03` §4). It is an action
            // on a screen the caller owns, so it requires a session.
            bool reRun;
            if (root.TryGetProperty("savedScreenId", out var savedScreenIdElement))
            {
                if (savedScreenIdElement.ValueKind != JsonValueKind.Number
                    || !savedScreenIdElement.TryGetInt64(out var savedScreenId)
                    || savedScreenId <= 0)
                {
                    return Validation(new[] { new ValidationField("savedScreenId", "invalid") });
                }

                var user = await CurrentUserAsync(userManager, context);
                if (user is null)
                {
                    return Unauthenticated();
                }

                // Ownership isolation via 404: a foreign id is indistinguishable from a
                // missing one (FR-SCR-011).
                var ownsScreen = await db.SavedScreens.AnyAsync(
                    candidate => candidate.Id == savedScreenId && candidate.UserId == user.Id,
                    cancellationToken);
                if (!ownsScreen)
                {
                    return NotFound("id", savedScreenId);
                }

                reRun = true;
            }
            else
            {
                reRun = false;
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
            var dropped = new List<string>();
            var criteria = new List<Criterion>();
            foreach (var element in criteriaElement.EnumerateArray())
            {
                var criterion = ParseCriterion(element, catalog, fields, reRun, dropped);
                if (criterion is not null)
                {
                    criteria.Add(criterion);
                }
            }

            // A hidden/unknown metric is the dedicated availability error on an ad-hoc
            // run (03 §4); other shape problems are ordinary validation failures.
            if (fields.Any(field => field.Code == "metric_not_available"))
            {
                return MetricNotAvailable();
            }

            if (fields.Count > 0)
            {
                return Validation(fields);
            }

            // A re-run whose every criterion has since been hidden has nothing left
            // to screen by; the empty-criteria rule (I-SCR-1) still holds.
            if (criteria.Count == 0)
            {
                return Validation(new[] { new ValidationField("criteria", "required") });
            }

            return await EvaluateAsync(db, clock, criteria, dropped, cancellationToken);
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
        List<ValidationField> fields,
        bool reRun,
        List<string> dropped)
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
            // On a saved-screen re-run a since-hidden metric is dropped and reported
            // (UC-SCR-003a, UXR-SCR-015). The builder never offers a hidden/unknown
            // metric on an ad-hoc run, so that submission is the dedicated availability
            // error (UXR-SCR-011; 03 §4).
            if (reRun && code is not null)
            {
                if (!dropped.Contains(code))
                {
                    dropped.Add(code);
                }

                return null;
            }

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
        IReadOnlyList<string> dropped,
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
            droppedCriteria = dropped,
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

    private static async Task<ApplicationUser?> CurrentUserAsync(
        UserManager<ApplicationUser> userManager,
        HttpContext context)
        => context.User.Identity?.IsAuthenticated == true
            ? await userManager.GetUserAsync(context.User)
            : null;

    private static IResult Unauthenticated()
        => Results.Problem(ApiProblem.Create(
            StatusCodes.Status401Unauthorized,
            ApiErrorCodes.Unauthenticated));

    private static IResult NotFound(string field, long id)
        => Results.Problem(ApiProblem.Create(
            StatusCodes.Status404NotFound,
            ApiErrorCodes.NotFound,
            new Dictionary<string, object?> { [field] = id }));

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
