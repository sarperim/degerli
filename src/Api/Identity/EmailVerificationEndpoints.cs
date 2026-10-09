using Degerli.Api.Infrastructure;
using Degerli.Api.Mail;
using Degerli.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Degerli.Api.Identity;

/// <summary>Verification payload — the single-use token delivered by e-mail (`03` §6).</summary>
public sealed record VerifyEmailRequest(string? Token);

/// <summary>
/// E-mail verification surface owned by TKT-acc-004 (`03` §6):
/// <list type="bullet">
/// <item><c>POST /api/v1/auth/verify-email</c> — consumes the 48h single-use token and
/// flips the account to verified. Session-scoped, so the active session gains the
/// verified state immediately (M-9 groundwork behind UXR-G-030). 410
/// <c>TOKEN_EXPIRED</c> for expired/used/invalid tokens.</item>
/// <item><c>POST /api/v1/auth/resend-verification</c> — session-scoped; dispatches a fresh
/// token for an unverified account, benign no-op for a verified one (I-ACC-2). No
/// enumeration surface: the caller's session identifies the account, never a body.</item>
/// </list>
/// </summary>
public static class EmailVerificationEndpoints
{
    public static IEndpointRouteBuilder MapDegerliEmailVerificationEndpoints(this IEndpointRouteBuilder auth)
    {
        auth.MapPost("/verify-email", VerifyEmailAsync).AllowAnonymous();
        auth.MapPost("/resend-verification", ResendVerificationAsync).AllowAnonymous();
        return auth;
    }

    private static async Task<IResult> VerifyEmailAsync(
        VerifyEmailRequest? request,
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

        if (string.IsNullOrWhiteSpace(request?.Token))
        {
            return Validation("token", "required");
        }

        // Single-use: a token whose account is already verified is consumed — reported
        // exactly like an expired one (`03` §7: TOKEN_EXPIRED covers used tokens).
        if (user.EmailConfirmed)
        {
            return TokenExpired();
        }

        var confirmed = await userManager.ConfirmEmailAsync(user, request!.Token!);
        if (!confirmed.Succeeded)
        {
            return TokenExpired();
        }

        return Results.Ok(new { verified = true });
    }

    private static async Task<IResult> ResendVerificationAsync(
        UserManager<ApplicationUser> userManager,
        IMailDispatcher mail,
        IOptions<AuthOptions> options,
        ILoggerFactory loggerFactory,
        HttpContext context,
        CancellationToken cancellationToken)
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

        // I-ACC-2: already-verified account → benign confirmation; no token, no mail.
        if (user.EmailConfirmed)
        {
            return Results.Ok(new { verified = true });
        }

        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var link = BuildVerificationLink(options.Value.AppUrl, token);
        try
        {
            await mail.SendAsync(VerificationMail(user.Email!, link), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Background e-mail failures never surface mid-flow (`03` §7); they alert.
            loggerFactory.CreateLogger("Degerli.Identity")
                .LogError(exception, "Verification e-mail dispatch failed for {Email}; resend continues.", user.Email);
        }

        return Results.Ok(new { verified = false });
    }

    private static IResult Validation(string field, string code)
        => Results.Problem(ApiProblem.Create(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationFailed,
            new Dictionary<string, object?> { ["fields"] = new[] { new { field, code } } }));

    private static IResult Unauthenticated()
        => Results.Problem(ApiProblem.Create(
            StatusCodes.Status401Unauthorized,
            ApiErrorCodes.Unauthenticated));

    private static IResult TokenExpired()
        => Results.Problem(ApiProblem.Create(
            StatusCodes.Status410Gone,
            ApiErrorCodes.TokenExpired));

    private static string BuildVerificationLink(string appUrl, string token)
        => $"{appUrl.TrimEnd('/')}/verify-email?token={Uri.EscapeDataString(token)}";

    private static OutboundMail VerificationMail(string email, string link)
        => new(
            To: email,
            Subject: "Değerli — E-posta doğrulama / Verify your e-mail",
            BodyTr: $"Değerli'ye hoş geldiniz. Hesabınızı doğrulamak için bağlantıya tıklayın: {link}",
            BodyEn: $"Welcome to Değerli. Click the link to verify your e-mail address: {link}");
}
