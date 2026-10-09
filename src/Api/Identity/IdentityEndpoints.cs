using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Degerli.Api.Infrastructure;
using Degerli.Api.Mail;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Degerli.Api.Identity;

/// <summary>Registration payload — exactly e-mail + password + consent (`03` §6).</summary>
public sealed record ConsentInput(string? NoticeVersion);

/// <summary>
/// Registration request. Unknown fields are ignored by the serializer (payload
/// evolution, `03` §11) and never stored (BR-ACC-001).
/// </summary>
public sealed record RegisterRequest(string? Email, string? Password, ConsentInput? Consent);

/// <summary>
/// Identity endpoint surface owned by TKT-acc-002: registration (account + consent +
/// verification dispatch + session) and the session read used by the header.
/// </summary>
public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapDegerliIdentityEndpoints(this IEndpointRouteBuilder api)
    {
        var auth = api.MapGroup("/auth").RequireRateLimiting(RateLimitingSetup.AuthPolicy);
        auth.MapPost("/register", RegisterAsync).AllowAnonymous();
        auth.MapGet("/session", SessionAsync).AllowAnonymous();

        // Later ACC tickets extend the auth surface with their own endpoint files.
        // TKT-acc-005: reset endpoints own their own /auth group, so map from the root.
        api.MapDegerliPasswordResetEndpoints();
        // TKT-acc-004: verification endpoints map relative paths onto the /auth group.
        auth.MapDegerliEmailVerificationEndpoints();
        return api;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest? request,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        DegerliDbContext db,
        IMailDispatcher mail,
        IOptions<AuthOptions> options,
        TimeProvider clock,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var authOptions = options.Value;

        var fields = new List<ValidationField>();
        if (request is null || string.IsNullOrWhiteSpace(request.Email))
        {
            fields.Add(new ValidationField("email", "required"));
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Password))
        {
            fields.Add(new ValidationField("password", "required"));
        }

        if (request?.Consent is null)
        {
            fields.Add(new ValidationField("consent", "required"));
        }
        else if (string.IsNullOrWhiteSpace(request.Consent.NoticeVersion))
        {
            fields.Add(new ValidationField("consent.noticeVersion", "required"));
        }
        else if (!authOptions.NoticeVersions.Contains(request.Consent.NoticeVersion, StringComparer.Ordinal))
        {
            fields.Add(new ValidationField("consent.noticeVersion", "unknown"));
        }

        if (fields.Count > 0)
        {
            return Validation(fields);
        }

        var email = request!.Email!.Trim();
        var password = request.Password!;
        var noticeVersion = request.Consent!.NoticeVersion!;

        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return EmailTaken(email);
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            LanguagePref = "tr",
        };

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var created = await userManager.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return MapCreateFailure(created, email);
        }

        db.ConsentRecords.Add(new ConsentRecord
        {
            UserId = user.Id,
            UserRefHash = UserRefHash.Compute(authOptions.UserRefHashKey, user.Id),
            NoticeVersion = noticeVersion,
            ConsentedAt = clock.GetUtcNow(),
            Action = "register",
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var link = BuildVerificationLink(authOptions.AppUrl, token);
        try
        {
            await mail.SendAsync(VerificationMail(email, link), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Background e-mail failures never surface mid-flow (`03` §7); they alert.
            loggerFactory.CreateLogger("Degerli.Identity")
                .LogError(exception, "Verification e-mail dispatch failed for {Email}; registration continues.", email);
        }

        await signInManager.SignInAsync(user, isPersistent: false);

        return Results.Created("/api/v1/me", new { email });
    }

    private static async Task<IResult> SessionAsync(
        UserManager<ApplicationUser> userManager,
        HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Results.Ok(new { authenticated = false });
        }

        var user = await userManager.GetUserAsync(context.User);
        if (user is null)
        {
            return Results.Ok(new { authenticated = false });
        }

        var role = await userManager.IsInRoleAsync(user, "builder") ? "builder" : "user";
        return Results.Ok(new
        {
            authenticated = true,
            email = user.Email,
            verified = user.EmailConfirmed,
            languagePref = user.LanguagePref,
            role,
        });
    }

    private static IResult MapCreateFailure(IdentityResult result, string email)
    {
        if (result.Errors.Any(error => error.Code is "DuplicateUserName" or "DuplicateEmail"))
        {
            return EmailTaken(email);
        }

        var passwordErrors = result.Errors
            .Where(error => error.Code.StartsWith("Password", StringComparison.Ordinal))
            .Select(error => new ValidationField("password", error.Code))
            .ToList();

        return passwordErrors.Count > 0
            ? Validation(passwordErrors)
            : Validation(result.Errors.Select(error => new ValidationField("email", error.Code)).ToList());
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

    private static IResult EmailTaken(string email)
        => Results.Problem(ApiProblem.Create(
            StatusCodes.Status409Conflict,
            ApiErrorCodes.EmailTaken,
            new Dictionary<string, object?> { ["email"] = email }));

    private static string BuildVerificationLink(string appUrl, string token)
        => $"{appUrl.TrimEnd('/')}/verify-email?token={Uri.EscapeDataString(token)}";

    private static OutboundMail VerificationMail(string email, string link)
        => new(
            To: email,
            Subject: "Değerli — E-posta doğrulama / Verify your e-mail",
            BodyTr: $"Değerli'ye hoş geldiniz. Hesabınızı doğrulamak için bağlantıya tıklayın: {link}",
            BodyEn: $"Welcome to Değerli. Click the link to verify your e-mail address: {link}");

    private readonly record struct ValidationField(string Field, string Code);
}

/// <summary>
/// Anonymized consent identifier for KVKK evidence (`02` §5.4): a keyed hash of the
/// user id so the retained consent row no longer identifies the deleted account.
/// </summary>
internal static class UserRefHash
{
    public static string Compute(string key, long userId)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var digest = hmac.ComputeHash(Encoding.UTF8.GetBytes(userId.ToString(CultureInfo.InvariantCulture)));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }
}
