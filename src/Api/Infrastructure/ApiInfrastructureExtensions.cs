using Microsoft.AspNetCore.Builder;
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

        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-Token";
            options.Cookie.Name = "degerli.csrf";
        });

        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", failureStatus: HealthStatus.Unhealthy, tags: new[] { "ready" });

        services.AddApiRateLimiting(configuration, environment);
        services.AddOpenApi();

        return services;
    }

    public static WebApplication UseApiInfrastructure(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseRouting();
        app.UseRateLimiter();
        app.UseMiddleware<CsrfProtectionMiddleware>();
        app.UseMiddleware<CachingMiddleware>();

        return app;
    }
}
