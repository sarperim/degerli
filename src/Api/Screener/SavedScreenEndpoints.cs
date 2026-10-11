using System.Text.Json;
using Degerli.Api.Infrastructure;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Degerli.Api.Screener;

/// <summary>
/// Saved-screen CRUD (`03-api-design.md` §4; FR-SCR-007..011, UC-SCR-002/003,
/// TKT-scr-004). Screens are persisted server-side per account (SC-003, NFR-SCR-005):
/// <list type="bullet">
/// <item><c>GET /api/v1/me/screens</c> — the caller's screens by name (auth only).</item>
/// <item><c>POST /api/v1/me/screens</c> <c>{name, criteria}</c> — save.</item>
/// <item><c>PATCH /api/v1/me/screens/{id}</c> <c>{name}</c> and/or <c>{criteria}</c> —
/// rename / update the stored criteria.</item>
/// <item><c>DELETE /api/v1/me/screens/{id}</c> — delete.</item>
/// </list>
/// Mutations require an authenticated session and a verified e-mail
/// (<c>403 EMAIL_NOT_VERIFIED</c>, UXR-G-030); reads require only a session. The
/// criteria array is stored verbatim as <c>jsonb</c> and validated with the exact same
/// parser as the ad-hoc run (TC-SCR-016), so a saved screen round-trips and re-runs
/// unchanged. A duplicate name for the same account is <c>409 DUPLICATE_NAME</c> —
/// backed by the DB unique constraint, so it cannot race; unknown/foreign ids are
/// <c>404 NOT_FOUND</c> (no ownership oracle, FR-SCR-011). Results are never stored:
/// a re-run recomputes against the latest <c>derived_metrics</c> (UC-SCR-003).
/// </summary>
public static class SavedScreenEndpoints
{
    public static IEndpointRouteBuilder MapDegerliSavedScreenEndpoints(this IEndpointRouteBuilder api)
    {
        var screens = api.MapGroup("/me/screens");

        screens.MapGet("", ListAsync).AllowAnonymous();
        screens.MapPost("", SaveAsync).AllowAnonymous();
        screens.MapPatch("/{id:long}", PatchAsync).AllowAnonymous();
        screens.MapDelete("/{id:long}", DeleteAsync).AllowAnonymous();

        return api;
    }

    // -- GET /me/screens -------------------------------------------------------

    private static async Task<IResult> ListAsync(
        UserManager<ApplicationUser> userManager,
        DegerliDbContext db,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync(userManager, context);
        if (user is null)
        {
            return Unauthenticated();
        }

        var screens = await db.SavedScreens
            .Where(screen => screen.UserId == user.Id)
            .OrderBy(screen => screen.Name)
            .ToListAsync(cancellationToken);

        return Results.Ok(new { screens = screens.Select(Project).ToArray() });
    }

    // -- POST /me/screens ------------------------------------------------------

    private static async Task<IResult> SaveAsync(
        HttpRequest request,
        UserManager<ApplicationUser> userManager,
        DegerliDbContext db,
        TimeProvider clock,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync(userManager, context);
        if (user is null)
        {
            return Unauthenticated();
        }

        if (!user.EmailConfirmed)
        {
            return EmailNotVerified();
        }

        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return Validation([new ScreenerCriteria.ValidationField("body", "invalid_json")]);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Validation([new ScreenerCriteria.ValidationField("body", "invalid_json")]);
            }

            var fields = new List<ScreenerCriteria.ValidationField>();

            var name = root.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
                ? nameElement.GetString()?.Trim()
                : null;
            if (string.IsNullOrEmpty(name))
            {
                fields.Add(new ScreenerCriteria.ValidationField("name", "required"));
            }

            var catalog = await LoadCatalogAsync(db, cancellationToken);
            var criteria = new List<ScreenerCriteria.Criterion>();
            ScreenerCriteria.ParseRoot(root, catalog, criteria, fields);

            if (ScreenerCriteria.HasMetricNotAvailable(fields))
            {
                return MetricNotAvailable();
            }

            if (fields.Count > 0)
            {
                return Validation(fields);
            }

            // Duplicate name per account: the DB unique constraint is the authority,
            // but reject early with the documented 409 (UXR-G-029).
            var duplicate = await db.SavedScreens.AnyAsync(
                screen => screen.UserId == user.Id && screen.Name == name,
                cancellationToken);
            if (duplicate)
            {
                return DuplicateName(name!);
            }

            var now = clock.GetUtcNow();
            var screen = new SavedScreen
            {
                UserId = user.Id,
                Name = name!,
                // Stored verbatim so the criteria round-trip exactly and a re-run reads
                // the same payload the user saved (FR-SCR-007/009).
                CriteriaJson = root.GetProperty("criteria").GetRawText(),
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.SavedScreens.Add(screen);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (IsUniqueViolation(exception))
            {
                return DuplicateName(name!);
            }

            return Results.Created($"/api/v1/me/screens/{screen.Id}", Project(screen));
        }
    }

    // -- PATCH /me/screens/{id} ------------------------------------------------

    private static async Task<IResult> PatchAsync(
        long id,
        HttpRequest request,
        UserManager<ApplicationUser> userManager,
        DegerliDbContext db,
        TimeProvider clock,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync(userManager, context);
        if (user is null)
        {
            return Unauthenticated();
        }

        if (!user.EmailConfirmed)
        {
            return EmailNotVerified();
        }

        // Ownership isolation via 404: a foreign id is indistinguishable from a missing one.
        var screen = await db.SavedScreens
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == user.Id, cancellationToken);
        if (screen is null)
        {
            return NotFoundNaming("id", id);
        }

        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return Validation([new ScreenerCriteria.ValidationField("body", "invalid_json")]);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Validation([new ScreenerCriteria.ValidationField("body", "invalid_json")]);
            }

            var fields = new List<ScreenerCriteria.ValidationField>();

            var hasName = root.TryGetProperty("name", out var nameElement);
            string? newName = null;
            if (hasName)
            {
                newName = nameElement.ValueKind == JsonValueKind.String ? nameElement.GetString()?.Trim() : null;
                if (string.IsNullOrEmpty(newName))
                {
                    fields.Add(new ScreenerCriteria.ValidationField("name", "required"));
                }
            }

            var hasCriteria = root.TryGetProperty("criteria", out var criteriaElement);
            if (hasCriteria)
            {
                var catalog = await LoadCatalogAsync(db, cancellationToken);
                var criteria = new List<ScreenerCriteria.Criterion>();
                ScreenerCriteria.ParseArray(criteriaElement, catalog, criteria, fields);

                if (ScreenerCriteria.HasMetricNotAvailable(fields))
                {
                    return MetricNotAvailable();
                }
            }

            if (!hasName && !hasCriteria)
            {
                fields.Add(new ScreenerCriteria.ValidationField("body", "required"));
            }

            if (fields.Count > 0)
            {
                return Validation(fields);
            }

            if (hasName && newName != screen.Name)
            {
                var collision = await db.SavedScreens.AnyAsync(
                    s => s.UserId == user.Id && s.Name == newName && s.Id != screen.Id,
                    cancellationToken);
                if (collision)
                {
                    return DuplicateName(newName!);
                }

                screen.Name = newName!;
            }

            if (hasCriteria)
            {
                screen.CriteriaJson = criteriaElement.GetRawText();
            }

            screen.UpdatedAt = clock.GetUtcNow();
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (IsUniqueViolation(exception))
            {
                return DuplicateName(screen.Name);
            }

            return Results.Ok(Project(screen));
        }
    }

    // -- DELETE /me/screens/{id} -----------------------------------------------

    private static async Task<IResult> DeleteAsync(
        long id,
        UserManager<ApplicationUser> userManager,
        DegerliDbContext db,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync(userManager, context);
        if (user is null)
        {
            return Unauthenticated();
        }

        if (!user.EmailConfirmed)
        {
            return EmailNotVerified();
        }

        var screen = await db.SavedScreens
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == user.Id, cancellationToken);
        if (screen is null)
        {
            return NotFoundNaming("id", id);
        }

        db.SavedScreens.Remove(screen);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static object Project(SavedScreen screen)
    {
        using var document = JsonDocument.Parse(screen.CriteriaJson);
        return new
        {
            id = screen.Id,
            name = screen.Name,
            criteria = document.RootElement.Clone(),
            createdAt = screen.CreatedAt,
            updatedAt = screen.UpdatedAt,
        };
    }

    private static async Task<IReadOnlyDictionary<string, bool>> LoadCatalogAsync(
        DegerliDbContext db,
        CancellationToken cancellationToken)
    {
        var entries = await db.MetricCatalog
            .Where(entry => entry.IsScreenable)
            .Select(entry => new { entry.MetricCode, entry.IsGrowthCagr })
            .ToListAsync(cancellationToken);

        return entries.ToDictionary(
            entry => entry.MetricCode,
            entry => entry.IsGrowthCagr,
            StringComparer.Ordinal);
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
        => exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

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

    private static IResult EmailNotVerified()
        => Results.Problem(ApiProblem.Create(
            StatusCodes.Status403Forbidden,
            ApiErrorCodes.EmailNotVerified));

    private static IResult MetricNotAvailable()
        => Results.Problem(ApiProblem.Create(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.MetricNotAvailable));

    private static IResult DuplicateName(string name)
        => Results.Problem(ApiProblem.Create(
            StatusCodes.Status409Conflict,
            ApiErrorCodes.DuplicateName,
            new Dictionary<string, object?> { ["name"] = name }));

    private static IResult NotFoundNaming(string field, long id)
        => Results.Problem(ApiProblem.Create(
            StatusCodes.Status404NotFound,
            ApiErrorCodes.NotFound,
            new Dictionary<string, object?> { [field] = id }));

    private static IResult Validation(IReadOnlyList<ScreenerCriteria.ValidationField> fields)
    {
        var payload = fields
            .Select(field => new { field = field.Field, code = field.Code })
            .ToArray();

        return Results.Problem(ApiProblem.Create(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationFailed,
            new Dictionary<string, object?> { ["fields"] = payload }));
    }
}
