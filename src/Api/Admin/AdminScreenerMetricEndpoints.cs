using System.Text.Json;
using Degerli.Api.Infrastructure;
using Degerli.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Api.Admin;

/// <summary>
/// The TKT-scr-005 admin metric-visibility endpoints (`03` §8; FR-SCR-017, UC-SCR-004,
/// UXR-MDF-023 API side):
/// <list type="bullet">
/// <item><c>GET /api/v1/admin/screener-metrics</c> — the visibility panel's data: every
/// catalog row (all 18 concepts) with its family, labels, unit and current
/// <c>isScreenable</c> state. The public catalog serves the visible subset only
/// (UXR-SCR-011); the builder needs the hidden rows too, so this read surface is
/// builder-only.</item>
/// <item><c>PATCH /api/v1/admin/screener-metrics/{code}</c> <c>{isScreenable}</c> — flips a
/// metric's visibility with no code change (FR-SCR-017). The flag is the single source
/// of truth: the catalog endpoint, ad-hoc runs and saved-screen re-runs all read it.</item>
/// </list>
/// This file owns only the screener-metric endpoints; the shared gated <c>/admin</c> route
/// group lives in <see cref="AdminEndpoints"/> (TKT-mdf-010), so the builder role gate and
/// the admin rate limit apply uniformly.
/// </summary>
public static class AdminScreenerMetricEndpoints
{
    public static RouteGroupBuilder MapDegerliAdminScreenerMetricEndpoints(this RouteGroupBuilder admin)
    {
        admin.MapGet("/screener-metrics", ListAsync);
        admin.MapPatch("/screener-metrics/{code}", PatchAsync);
        return admin;
    }

    // ---------------------------------------------------------------------------------
    // GET /admin/screener-metrics
    // ---------------------------------------------------------------------------------

    private static async Task<IResult> ListAsync(
        DegerliDbContext db,
        CancellationToken cancellationToken)
    {
        var metrics = await db.MetricCatalog
            .OrderBy(entry => entry.SortOrder)
            .Select(entry => new MetricView(
                entry.MetricCode,
                entry.Family,
                entry.Unit,
                entry.LabelTr,
                entry.LabelEn,
                entry.IsScreenable,
                entry.IsGrowthCagr,
                entry.SortOrder))
            .ToListAsync(cancellationToken);

        return Results.Ok(new { metrics });
    }

    // ---------------------------------------------------------------------------------
    // PATCH /admin/screener-metrics/{code}
    // ---------------------------------------------------------------------------------

    private static async Task<IResult> PatchAsync(
        string code,
        HttpRequest request,
        DegerliDbContext db,
        CancellationToken cancellationToken)
    {
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return Validation([("body", "invalid_json")]);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Validation([("body", "invalid_json")]);
            }

            if (!root.TryGetProperty("isScreenable", out var isScreenableElement))
            {
                return Validation([("isScreenable", "required")]);
            }

            if (isScreenableElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return Validation([("isScreenable", "invalid")]);
            }

            var entry = await db.MetricCatalog
                .FirstOrDefaultAsync(candidate => candidate.MetricCode == code, cancellationToken);
            if (entry is null)
            {
                return Results.Problem(ApiProblem.Create(
                    StatusCodes.Status404NotFound,
                    ApiErrorCodes.NotFound,
                    new Dictionary<string, object?> { ["code"] = code }));
            }

            entry.IsScreenable = isScreenableElement.GetBoolean();
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new MetricView(
                entry.MetricCode,
                entry.Family,
                entry.Unit,
                entry.LabelTr,
                entry.LabelEn,
                entry.IsScreenable,
                entry.IsGrowthCagr,
                entry.SortOrder));
        }
    }

    private static IResult Validation(IReadOnlyList<(string Field, string Code)> fields)
    {
        var payload = fields
            .Select(field => new { field = field.Field, code = field.Code })
            .ToArray();

        return Results.Problem(ApiProblem.Create(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationFailed,
            new Dictionary<string, object?> { ["fields"] = payload }));
    }

    /// <summary>One catalog row as the visibility panel reads and the toggle returns it.</summary>
    private sealed record MetricView(
        string MetricCode,
        string Family,
        string? Unit,
        string LabelTr,
        string LabelEn,
        bool IsScreenable,
        bool IsCagr,
        int SortOrder);
}
