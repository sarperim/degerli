using Degerli.Api.Infrastructure;
using Degerli.Persistence.Entities;
using Microsoft.AspNetCore.Identity;

namespace Degerli.Api.Identity;

/// <summary>Language-preference update payload for <c>PATCH /api/v1/me</c>.</summary>
public sealed record LanguagePreferenceRequest(string? LanguagePref);

/// <summary>Password-change payload for <c>PATCH /api/v1/me/password</c>.</summary>
public sealed record PasswordChangeRequest(string? Current, string? NewPassword);

/// <summary>
/// Account-settings endpoints (TKT-acc-006; `03` §6, UC-ACC-003):
/// <list type="bullet">
/// <item><c>GET /api/v1/me</c> — the minimal account read (email, languagePref,
/// verification status; BR-ACC-001/008, NFR-ACC-001).</item>
/// <item><c>PATCH /api/v1/me</c> <c>{languagePref}</c> — persisted and applied at
/// sign-in; invalid value → 400 (FR-ACC-004, BR-ACC-004).</item>
/// <item><c>PATCH /api/v1/me/password</c> <c>{current, newPassword}</c> — session
/// preserved on success; wrong current → 400 field <c>current</c> (I-ACC-1);
/// policy enforced on the new password (FR-ACC-007).</item>
/// <item><c>DELETE /api/v1/me</c> — hard delete + cascade (screens, scenarios);
/// consent retained anonymized (`02` §5.4); session revoked (FR-ACC-006,
/// BR-ACC-005, NFR-ACC-004).</item>
/// </list>
/// </summary>
public static class MeEndpoints
{
    private static readonly string[] SupportedLanguages = ["tr", "en"];

    public static IEndpointRouteBuilder MapDegerliMeEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/me", MeAsync).AllowAnonymous();
        api.MapPatch("/me", UpdateLanguageAsync).AllowAnonymous();
        api.MapPatch("/me/password", ChangePasswordAsync).AllowAnonymous();
        api.MapDelete("/me", DeleteAsync).AllowAnonymous();
        return api;
    }

    private static async Task<IResult> MeAsync(
        UserManager<ApplicationUser> userManager,
        HttpContext context)
    {
        var user = await CurrentUserAsync(userManager, context);
        if (user is null)
        {
            return Unauthenticated();
        }

        return Results.Ok(Me(user));
    }

    private static async Task<IResult> UpdateLanguageAsync(
        LanguagePreferenceRequest? request,
        UserManager<ApplicationUser> userManager,
        HttpContext context)
    {
        var user = await CurrentUserAsync(userManager, context);
        if (user is null)
        {
            return Unauthenticated();
        }

        var language = request?.LanguagePref?.Trim();
        if (string.IsNullOrEmpty(language))
        {
            return Validation([new ValidationField("languagePref", "required")]);
        }

        if (!SupportedLanguages.Contains(language, StringComparer.Ordinal))
        {
            return Validation([new ValidationField("languagePref", "unsupported")]);
        }

        user.LanguagePref = language;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return Internal();
        }

        return Results.Ok(Me(user));
    }

    private static async Task<IResult> ChangePasswordAsync(
        PasswordChangeRequest? request,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        HttpContext context)
    {
        var user = await CurrentUserAsync(userManager, context);
        if (user is null)
        {
            return Unauthenticated();
        }

        var fields = new List<ValidationField>();
        if (string.IsNullOrWhiteSpace(request?.Current))
        {
            fields.Add(new ValidationField("current", "required"));
        }

        if (string.IsNullOrWhiteSpace(request?.NewPassword))
        {
            fields.Add(new ValidationField("newPassword", "required"));
        }

        if (fields.Count > 0)
        {
            return Validation(fields);
        }

        var result = await userManager.ChangePasswordAsync(user, request!.Current!, request.NewPassword!);
        if (!result.Succeeded)
        {
            // I-ACC-1: the caller is already authenticated, so a wrong current
            // password is a validation failure on `current` — not 401.
            if (result.Errors.Any(error => error.Code == nameof(IdentityErrorDescriber.PasswordMismatch)))
            {
                return Validation([new ValidationField("current", "mismatch")]);
            }

            var passwordErrors = result.Errors
                .Where(error => error.Code.StartsWith("Password", StringComparison.Ordinal))
                .Select(error => new ValidationField("newPassword", error.Code))
                .ToList();

            return passwordErrors.Count > 0
                ? Validation(passwordErrors)
                : Validation([new ValidationField("newPassword", "invalid")]);
        }

        // ChangePasswordAsync does not disturb the security stamp, but re-issuing the
        // cookie makes the "session preserved on success" contract explicit.
        await signInManager.RefreshSignInAsync(user);
        return Results.Ok(new { email = user.Email });
    }

    private static async Task<IResult> DeleteAsync(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        HttpContext context)
    {
        var user = await CurrentUserAsync(userManager, context);
        if (user is null)
        {
            return Unauthenticated();
        }

        // Hard delete; the schema cascades saved_screens/dcf_scenarios and nulls
        // consent_records.user_id while keeping user_ref_hash (`02` §5.4).
        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            return Internal();
        }

        await signInManager.SignOutAsync();
        return Results.NoContent();
    }

    private static async Task<ApplicationUser?> CurrentUserAsync(
        UserManager<ApplicationUser> userManager,
        HttpContext context)
        => context.User.Identity?.IsAuthenticated == true
            ? await userManager.GetUserAsync(context.User)
            : null;

    private static object Me(ApplicationUser user) => new
    {
        email = user.Email,
        languagePref = user.LanguagePref,
        verified = user.EmailConfirmed,
    };

    private static IResult Unauthenticated()
        => Results.Problem(ApiProblem.Create(
            StatusCodes.Status401Unauthorized,
            ApiErrorCodes.Unauthenticated));

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

    private static IResult Internal()
        => Results.Problem(ApiProblem.Create(
            StatusCodes.Status500InternalServerError,
            ApiErrorCodes.Internal));

    private readonly record struct ValidationField(string Field, string Code);
}
