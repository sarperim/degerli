using System.Globalization;
using System.Text.Json;
using Degerli.Api.Infrastructure;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Degerli.Api.Valuation;

/// <summary>Save payload for <c>POST /api/v1/me/scenarios</c> (FR-VAL-009).</summary>
public sealed record SaveScenarioRequest(string? Symbol, string? Name, JsonElement? Params);

/// <summary>Rename payload for <c>PATCH /api/v1/me/scenarios/{id}</c> (UXR-VAL-017).</summary>
public sealed record RenameScenarioRequest(string? Name);

/// <summary>
/// DCF scenario CRUD (<c>03-api-design.md</c> §5; FR-VAL-009/010, UC-VAL-002,
/// TKT-val-005). Scenarios are persisted server-side per account and per stock:
/// <list type="bullet">
/// <item><c>GET /api/v1/me/scenarios?symbol=</c> — the caller's scenarios (SCR-011
/// grouped list + SCR-006 per-stock picker; grouping is client-side).</item>
/// <item><c>POST /api/v1/me/scenarios</c> <c>{symbol, name, params}</c> — save.</item>
/// <item><c>PATCH /api/v1/me/scenarios/{id}</c> <c>{name}</c> — rename.</item>
/// <item><c>DELETE /api/v1/me/scenarios/{id}</c> — delete.</item>
/// </list>
/// All mutations require an authenticated session and a verified e-mail
/// (<c>403 EMAIL_NOT_VERIFIED</c>, UXR-G-030); reads require only a session. The
/// 8-parameter set is stored verbatim as <c>jsonb</c> so a reload restores it
/// exactly (FR-VAL-010). A duplicate name for the same (user, stock) is rejected
/// with <c>409 DUPLICATE_NAME</c> — backed by the DB unique constraint, so it
/// cannot race; unknown/foreign ids are <c>404 NOT_FOUND</c> (no ownership oracle).
/// </summary>
public static class DcfScenarioEndpoints
{
    /// <summary>The confirmed, fully user-editable parameter set, wire order.</summary>
    private static readonly string[] ParameterFields =
    {
        "base_fcf",
        "growth_rate",
        "horizon_years",
        "terminal_growth",
        "discount_rate",
        "debt",
        "cash",
        "share_count",
    };

    public static IEndpointRouteBuilder MapDegerliDcfScenarioEndpoints(this IEndpointRouteBuilder api)
    {
        var scenarios = api.MapGroup("/me/scenarios");

        scenarios.MapGet("", ListAsync).AllowAnonymous();
        scenarios.MapPost("", SaveAsync).AllowAnonymous();
        scenarios.MapPatch("/{id:long}", RenameAsync).AllowAnonymous();
        scenarios.MapDelete("/{id:long}", DeleteAsync).AllowAnonymous();

        return api;
    }

    // -- GET /me/scenarios?symbol= ---------------------------------------------

    private static async Task<IResult> ListAsync(
        string? symbol,
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

        var query = db.DcfScenarios.Where(s => s.UserId == user.Id);

        if (!string.IsNullOrWhiteSpace(symbol))
        {
            var normalized = symbol.Trim().ToUpperInvariant();
            var instrumentId = await db.Instruments
                .Where(i => i.Symbol == normalized)
                .Select(i => (long?)i.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (instrumentId is null)
            {
                return NotFoundNaming("symbol", normalized);
            }

            query = query.Where(s => s.InstrumentId == instrumentId.Value);
        }

        var rows = await query
            .Join(
                db.Instruments,
                scenario => scenario.InstrumentId,
                instrument => instrument.Id,
                (scenario, instrument) => new { Scenario = scenario, instrument.Symbol })
            .OrderBy(row => row.Symbol)
            .ThenBy(row => row.Scenario.Name)
            .ToListAsync(cancellationToken);

        var payload = rows.Select(row => Project(row.Scenario, row.Symbol)).ToArray();
        return Results.Ok(new { scenarios = payload });
    }

    // -- POST /me/scenarios ----------------------------------------------------

    private static async Task<IResult> SaveAsync(
        SaveScenarioRequest? request,
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

        var fields = new List<ValidationField>();

        var symbol = request?.Symbol?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(symbol))
        {
            fields.Add(new ValidationField("symbol", "required"));
        }

        var name = request?.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            fields.Add(new ValidationField("name", "required"));
        }

        var paramsValue = request?.Params;
        var values = new Dictionary<string, decimal>(StringComparer.Ordinal);
        if (paramsValue is not { ValueKind: JsonValueKind.Object })
        {
            fields.Add(new ValidationField("params", "required"));
        }
        else
        {
            var parameters = paramsValue.Value;
            foreach (var field in ParameterFields)
            {
                ReadNumber(parameters, field, fields, values);
            }

            ValidateHorizon(values, fields);
            ValidateShareCount(values, fields);
        }

        if (fields.Count > 0)
        {
            return Validation(fields);
        }

        var instrumentId = await db.Instruments
            .Where(i => i.Symbol == symbol)
            .Select(i => (long?)i.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (instrumentId is null)
        {
            return NotFoundNaming("symbol", symbol!);
        }

        // Duplicate name per (user, stock): the DB unique constraint is the authority,
        // but reject early with the documented 409 (UXR-G-029).
        var duplicate = await db.DcfScenarios.AnyAsync(
            s => s.UserId == user.Id && s.InstrumentId == instrumentId.Value && s.Name == name,
            cancellationToken);
        if (duplicate)
        {
            return DuplicateName(name!);
        }

        var now = clock.GetUtcNow();
        var scenario = new DcfScenario
        {
            UserId = user.Id,
            InstrumentId = instrumentId.Value,
            Name = name!,
            // Stored verbatim so loading restores every parameter exactly (FR-VAL-010).
            // Reaching here means `params` was a valid object (fields.Count == 0).
            ParamsJson = paramsValue!.Value.GetRawText(),
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.DcfScenarios.Add(scenario);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return DuplicateName(name!);
        }

        return Results.Created(
            $"/api/v1/me/scenarios/{scenario.Id}",
            Project(scenario, symbol!));
    }

    // -- PATCH /me/scenarios/{id} ----------------------------------------------

    private static async Task<IResult> RenameAsync(
        long id,
        RenameScenarioRequest? request,
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
        var scenario = await db.DcfScenarios
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == user.Id, cancellationToken);
        if (scenario is null)
        {
            return NotFoundNaming("id", id.ToString(CultureInfo.InvariantCulture));
        }

        var name = request?.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return Validation([new ValidationField("name", "required")]);
        }

        if (name != scenario.Name)
        {
            var collision = await db.DcfScenarios.AnyAsync(
                s => s.UserId == user.Id
                    && s.InstrumentId == scenario.InstrumentId
                    && s.Name == name
                    && s.Id != scenario.Id,
                cancellationToken);
            if (collision)
            {
                return DuplicateName(name);
            }

            scenario.Name = name;
            scenario.UpdatedAt = clock.GetUtcNow();
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (IsUniqueViolation(exception))
            {
                return DuplicateName(name);
            }
        }

        var symbol = await db.Instruments
            .Where(i => i.Id == scenario.InstrumentId)
            .Select(i => i.Symbol)
            .SingleAsync(cancellationToken);

        return Results.Ok(Project(scenario, symbol));
    }

    // -- DELETE /me/scenarios/{id} ---------------------------------------------

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

        var scenario = await db.DcfScenarios
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == user.Id, cancellationToken);
        if (scenario is null)
        {
            return NotFoundNaming("id", id.ToString(CultureInfo.InvariantCulture));
        }

        db.DcfScenarios.Remove(scenario);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static object Project(DcfScenario scenario, string symbol)
    {
        using var document = JsonDocument.Parse(scenario.ParamsJson);
        return new
        {
            id = scenario.Id,
            symbol,
            name = scenario.Name,
            @params = document.RootElement.Clone(),
            createdAt = scenario.CreatedAt,
            updatedAt = scenario.UpdatedAt,
        };
    }

    /// <summary>
    /// Reads one parameter with strict typing (`01` §10.5): missing/null is
    /// <c>required</c>, a wrong JSON kind is <c>not_a_number</c>, and a number that
    /// cannot be represented as a finite decimal is <c>out_of_range</c>. Mirrors the
    /// compute endpoint's contract so saved scenarios are always computable inputs.
    /// </summary>
    private static void ReadNumber(
        JsonElement root,
        string name,
        List<ValidationField> fields,
        Dictionary<string, decimal> values)
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            fields.Add(new ValidationField(name, "required"));
            return;
        }

        if (element.ValueKind != JsonValueKind.Number)
        {
            fields.Add(new ValidationField(name, "not_a_number"));
            return;
        }

        if (!decimal.TryParse(element.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            fields.Add(new ValidationField(name, "out_of_range"));
            return;
        }

        values[name] = value;
    }

    private static void ValidateHorizon(Dictionary<string, decimal> values, List<ValidationField> fields)
    {
        if (!values.TryGetValue("horizon_years", out var horizon))
        {
            return;
        }

        if (horizon != decimal.Truncate(horizon))
        {
            fields.Add(new ValidationField("horizon_years", "not_a_number"));
        }
        else if (horizon < int.MinValue || horizon > int.MaxValue)
        {
            fields.Add(new ValidationField("horizon_years", "out_of_range"));
        }
    }

    private static void ValidateShareCount(Dictionary<string, decimal> values, List<ValidationField> fields)
    {
        if (values.TryGetValue("share_count", out var shareCount) && shareCount <= 0m)
        {
            fields.Add(new ValidationField("share_count", "out_of_range"));
        }
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

    private static IResult DuplicateName(string name)
        => Results.Problem(ApiProblem.Create(
            StatusCodes.Status409Conflict,
            ApiErrorCodes.DuplicateName,
            new Dictionary<string, object?> { ["name"] = name }));

    private static IResult NotFoundNaming(string field, string value)
        => Results.Problem(ApiProblem.Create(
            StatusCodes.Status404NotFound,
            ApiErrorCodes.NotFound,
            new Dictionary<string, object?> { [field] = value }));

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

    private readonly record struct ValidationField(string Field, string Code);
}
