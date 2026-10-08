using Degerli.Api.Infrastructure;
using Degerli.Persistence.Entities;
using Microsoft.AspNetCore.Identity;

namespace Degerli.Api.Identity;

/// <summary>
/// The minimal account-read endpoint needed to assert the minimal-data posture
/// (BR-ACC-001/008, NFR-ACC-001; TC-ACC-005): a fresh account exposes only e-mail,
/// language preference and verification status — no name/profile/demographic fields.
/// The mutating account-settings endpoints (PATCH/PATCH password/DELETE) are
/// TKT-acc-006's; this file is deliberately the seed it extends.
/// </summary>
public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapDegerliMeEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/me", MeAsync).AllowAnonymous();
        return api;
    }

    private static async Task<IResult> MeAsync(
        UserManager<ApplicationUser> userManager,
        HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Unauthenticated();
        }

        var user = await userManager.GetUserAsync(context.User);
        if (user is null)
        {
            return Unauthenticated();
        }

        return Results.Ok(new
        {
            email = user.Email,
            languagePref = user.LanguagePref,
            verified = user.EmailConfirmed,
        });
    }

    private static IResult Unauthenticated()
        => Results.Problem(ApiProblem.Create(
            StatusCodes.Status401Unauthorized,
            ApiErrorCodes.Unauthenticated));
}
