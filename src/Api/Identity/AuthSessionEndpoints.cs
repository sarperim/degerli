using Degerli.Api.Infrastructure;
using Degerli.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Degerli.Api.Identity;

/// <summary>Sign-in payload (`03` §6). Unknown fields are ignored by the serializer.</summary>
public sealed record LoginRequest(string? Email, string? Password);

/// <summary>
/// Identity endpoint surface owned by TKT-acc-003: sign-in (session + language
/// preference, generic credential failure, per-account lockout) and sign-out
/// (session revocation). Registration/verification/reset/settings belong to other
/// tickets and are not touched here.
/// </summary>
public static class AuthSessionEndpoints
{
    /// <summary>
    /// Makes sign-out an immediate, server-side revocation (`01` FR-ACC-003 "cookie
    /// revoke", `03` §6). ASP.NET Core Identity's security-stamp validator is the
    /// cookie-revocation seam; its default 30-minute revalidation window would let a
    /// signed-out cookie authenticate again in the meantime, so the window is closed.
    /// </summary>
    public static IServiceCollection AddDegerliAuthSessionRevocation(this IServiceCollection services)
    {
        services.Configure<SecurityStampValidatorOptions>(options =>
            options.ValidationInterval = TimeSpan.Zero);
        return services;
    }

    public static IEndpointRouteBuilder MapDegerliAuthSessionEndpoints(this IEndpointRouteBuilder api)
    {
        var auth = api.MapGroup("/auth").RequireRateLimiting(RateLimitingSetup.AuthPolicy);
        auth.MapPost("/login", LoginAsync).AllowAnonymous();
        auth.MapPost("/logout", LogoutAsync).RequireAuthorization();
        return api;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest? request,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IOptions<IdentityOptions> identityOptions,
        TimeProvider clock)
    {
        // Missing input is generic too: no signal that would distinguish an unknown
        // account from a malformed request (NFR-ACC-002, UXR-ACC-008).
        if (request is null
            || string.IsNullOrWhiteSpace(request.Email)
            || string.IsNullOrWhiteSpace(request.Password))
        {
            return InvalidCredentials();
        }

        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            return InvalidCredentials();
        }

        var lockout = identityOptions.Value.Lockout;

        // Lockout is evaluated against the injected clock so the 15-minute window is
        // deterministic in tests (test plan §2 Group B) and identical in production.
        var lockoutEnd = await userManager.GetLockoutEndDateAsync(user);
        var lockoutEnabled = await userManager.GetLockoutEnabledAsync(user);
        if (lockoutEnabled && lockoutEnd.HasValue && lockoutEnd.Value > clock.GetUtcNow())
        {
            return LockedOut();
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            // Identity owns the failed-attempt accumulation; when it crosses the
            // threshold it flips the account into lockout. Ask it directly rather than
            // reading the count, which it resets as part of the transition.
            await userManager.AccessFailedAsync(user);
            if (await userManager.IsLockedOutAsync(user))
            {
                // Anchor the window to the injected clock so it is deterministic and
                // identical in tests and production.
                await userManager.SetLockoutEndDateAsync(user, clock.GetUtcNow() + lockout.DefaultLockoutTimeSpan);
                return LockedOut();
            }

            return InvalidCredentials();
        }

        await userManager.ResetAccessFailedCountAsync(user);
        if (lockoutEnd.HasValue)
        {
            await userManager.SetLockoutEndDateAsync(user, null);
        }

        await signInManager.SignInAsync(user, isPersistent: false);

        return Results.Ok(new { languagePref = user.LanguagePref });
    }

    private static async Task<IResult> LogoutAsync(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        HttpContext context)
    {
        var user = await userManager.GetUserAsync(context.User);

        await signInManager.SignOutAsync();

        // Rotate the security stamp so every cookie issued before this request is
        // revoked server-side (FR-ACC-003).
        if (user is not null)
        {
            await userManager.UpdateSecurityStampAsync(user);
        }

        return Results.NoContent();
    }

    private static IResult InvalidCredentials()
        => Results.Problem(ApiProblem.Create(
            StatusCodes.Status401Unauthorized,
            ApiErrorCodes.InvalidCredentials));

    private static IResult LockedOut()
        => Results.Problem(ApiProblem.Create(
            StatusCodes.Status429TooManyRequests,
            ApiErrorCodes.LockedOut));
}
