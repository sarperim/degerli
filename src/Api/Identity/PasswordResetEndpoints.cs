using System.Globalization;
using Degerli.Api.Infrastructure;
using Degerli.Api.Mail;
using Degerli.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Degerli.Api.Identity;

/// <summary>Forgot-password request (`03` §6).</summary>
public sealed record ForgotPasswordRequest(string? Email);

/// <summary>Reset-password request (`03` §6).</summary>
public sealed record ResetPasswordRequest(string? Token, string? NewPassword);

/// <summary>
/// Password-reset endpoints owned by TKT-acc-005 (FR-ACC-009, UC-ACC-004;
/// `03` §6):
/// <list type="bullet">
/// <item><c>POST /api/v1/auth/forgot-password {email}</c> — always a 200 neutral
/// confirmation (enumeration-neutral; NFR-ACC-002); a 2h single-use reset e-mail is
/// dispatched only when the address is registered.</item>
/// <item><c>POST /api/v1/auth/reset-password {token, newPassword}</c> — 200 (may
/// establish a session) / 410 <c>TOKEN_EXPIRED</c> on expired or reused token / 400
/// <c>VALIDATION_FAILED</c> when the password policy fails.</item>
/// </list>
/// The e-mail-verification and account-settings endpoints are owned by other tickets
/// (TKT-acc-004 / TKT-acc-006) and are deliberately not touched here.
/// </summary>
public static class PasswordResetEndpoints
{
    /// <summary>
    /// Byte-identical neutral confirmation returned for registered and unregistered
    /// addresses alike (UXR-ACC-012): the response must never reveal whether the
    /// address exists.
    /// </summary>
    private static readonly object NeutralConfirmation = new { status = "accepted" };

    public static IEndpointRouteBuilder MapDegerliPasswordResetEndpoints(this IEndpointRouteBuilder api)
    {
        var auth = api.MapGroup("/auth").RequireRateLimiting(RateLimitingSetup.AuthPolicy);
        auth.MapPost("/forgot-password", ForgotPasswordAsync).AllowAnonymous();
        auth.MapPost("/reset-password", ResetPasswordAsync).AllowAnonymous();
        return api;
    }

    private static async Task<IResult> ForgotPasswordAsync(
        ForgotPasswordRequest? request,
        UserManager<ApplicationUser> userManager,
        IMailDispatcher mail,
        IOptions<AuthOptions> options,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var email = request?.Email?.Trim();
        if (!string.IsNullOrWhiteSpace(email))
        {
            var user = await userManager.FindByEmailAsync(email);
            if (user is not null)
            {
                var token = await userManager.GeneratePasswordResetTokenAsync(user);
                var link = BuildResetLink(options.Value.AppUrl, token);
                try
                {
                    await mail.SendAsync(ResetMail(email, link), cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // Background e-mail failures never surface mid-flow (`03` §7); alert.
                    loggerFactory.CreateLogger("Degerli.Identity")
                        .LogError(exception, "Reset e-mail dispatch failed for {Email}.", email);
                }
            }
        }

        return Results.Ok(NeutralConfirmation);
    }

    private static async Task<IResult> ResetPasswordAsync(
        ResetPasswordRequest? request,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ResetPasswordTokenProvider tokenProvider)
    {
        if (request is null
            || !tokenProvider.TryReadUserId(request.Token, out var userId))
        {
            return TokenExpired();
        }

        var user = await userManager.FindByIdAsync(userId.ToString(CultureInfo.InvariantCulture));
        if (user is null)
        {
            return TokenExpired();
        }

        var result = await userManager.ResetPasswordAsync(user, request.Token!, request.NewPassword ?? string.Empty);
        if (result.Succeeded)
        {
            // UC-ACC-004 step 3 / `03` §6: a session may be established on success.
            await signInManager.SignInAsync(user, isPersistent: false);
            return Results.Ok(new { status = "reset" });
        }

        if (result.Errors.Any(error => error.Code == "InvalidToken"))
        {
            return TokenExpired();
        }

        var passwordErrors = result.Errors
            .Where(error => error.Code.StartsWith("Password", StringComparison.Ordinal))
            .Select(error => new ValidationField("password", error.Code))
            .ToList();

        return passwordErrors.Count > 0
            ? Validation(passwordErrors)
            : Validation(new List<ValidationField> { new("token", "invalid") });
    }

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

    private static IResult TokenExpired()
        => Results.Problem(ApiProblem.Create(
            StatusCodes.Status410Gone,
            ApiErrorCodes.TokenExpired));

    private static string BuildResetLink(string appUrl, string token)
        => $"{appUrl.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(token)}";

    private static OutboundMail ResetMail(string email, string link)
        => new(
            To: email,
            Subject: "Değerli — Şifre sıfırlama / Reset your password",
            BodyTr: $"Değerli hesabınız için şifre sıfırlama isteği aldık. Yeni şifrenizi belirlemek için bağlantıya tıklayın (2 saat geçerlidir): {link}",
            BodyEn: $"We received a password reset request for your Değerli account. Click the link to set a new password (valid for 2 hours): {link}");

    private readonly record struct ValidationField(string Field, string Code);
}
