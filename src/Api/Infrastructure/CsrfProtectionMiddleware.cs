using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;

namespace Degerli.Api.Infrastructure;

/// <summary>
/// Enforces the antiforgery token on unsafe <c>/api/v1</c> requests (`03` §2,
/// `01` §10.1): the <c>X-CSRF-Token</c> header must validate against the cookie
/// issued by <c>GET /api/v1/auth/csrf-token</c>. Missing/invalid → 400 ProblemDetails.
/// Safe methods (GET/HEAD/OPTIONS/TRACE) are unaffected.
/// </summary>
public sealed class CsrfProtectionMiddleware
{
    private readonly RequestDelegate _next;

    public CsrfProtectionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (RequiresValidation(context))
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                await WriteRejectionAsync(context);
                return;
            }
        }

        await _next(context);
    }

    private static bool RequiresValidation(HttpContext context)
    {
        var method = context.Request.Method;
        var safe = HttpMethods.IsGet(method)
            || HttpMethods.IsHead(method)
            || HttpMethods.IsOptions(method)
            || HttpMethods.IsTrace(method);

        return !safe
            && context.Request.Path.StartsWithSegments("/api/v1", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WriteRejectionAsync(HttpContext context)
    {
        var problem = ApiProblem.Create(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationFailed,
            new Dictionary<string, object?> { ["reason"] = "csrf" },
            ApiProblem.CurrentTraceId(context),
            "Antiforgery token missing or invalid");

        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            cancellationToken: context.RequestAborted);
    }
}
