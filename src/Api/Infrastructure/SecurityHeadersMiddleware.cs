using Microsoft.AspNetCore.Http;

namespace Degerli.Api.Infrastructure;

/// <summary>
/// Adds the baseline security headers to every response (`01` §10.5):
/// <c>X-Content-Type-Options: nosniff</c> and a CSP allowing only same-origin
/// resources and no framing. HSTS is terminated/added in the TLS posture (Caddy),
/// not in the app (I-XC-1).
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    public const string ContentSecurityPolicy = "default-src 'self'; frame-ancestors 'none'";

    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            var response = ((HttpContext)state).Response;
            response.Headers["X-Content-Type-Options"] = "nosniff";
            response.Headers["Content-Security-Policy"] = ContentSecurityPolicy;
            return Task.CompletedTask;
        }, context);

        await _next(context);
    }
}
