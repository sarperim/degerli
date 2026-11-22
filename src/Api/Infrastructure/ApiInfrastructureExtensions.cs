using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Degerli.Api.Infrastructure;

/// <summary>
/// Wires the shared API infrastructure (`01` §10): ProblemDetails + exception handler,
/// antiforgery, health checks, rate limiting, security headers, caching and OpenAPI.
/// Domain tickets build their endpoints on top of these seams.
/// </summary>
public static class ApiInfrastructureExtensions
{
    public static IServiceCollection AddApiInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("10.0.0.0"), 8));
            options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("172.16.0.0"), 12));
            options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("192.168.0.0"), 16));
            options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("fc00::"), 7));
        });

        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-Token";
            options.Cookie.Name = "degerli.csrf";
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", failureStatus: HealthStatus.Unhealthy, tags: new[] { "ready" });

        services.AddApiRateLimiting(configuration, environment);
        services.AddOpenApi();

        return services;
    }

    public static WebApplication UseApiInfrastructure(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseExceptionHandler();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseRouting();
        // Authentication must run before the antiforgery check: an antiforgery
        // request token is bound to the authenticated user, so validating it while
        // the principal is still empty would reject tokens minted after sign-in.
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseMiddleware<CsrfProtectionMiddleware>();
        app.UseMiddleware<CachingMiddleware>();

        return app;
    }
}
