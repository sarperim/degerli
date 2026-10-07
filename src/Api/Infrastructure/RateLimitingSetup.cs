using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Degerli.Api.Infrastructure;

/// <summary>
/// Fixed-window, client-IP-keyed rate limiting (`03` §12): <c>auth</c> 5/min,
/// <c>admin</c> 60/min and a global 600/min limiter. Per I-XC-2 the client IP is
/// test-controllable via <c>X-Forwarded-For</c> outside production. Rejections are
/// RFC 7807 <c>RATE_LIMITED</c> with <c>params.retryAfter</c>.
/// </summary>
public static class RateLimitingSetup
{
    public const string AuthPolicy = "auth";
    public const string AdminPolicy = "admin";

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<RateLimitOptions>()
            .Bind(configuration.GetSection(RateLimitOptions.SectionName));

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = (context, _) => WriteRejectionAsync(context);

            limiter.AddPolicy(AuthPolicy, httpContext =>
                FixedWindow(httpContext, Scope.Auth, environment));

            limiter.AddPolicy(AdminPolicy, httpContext =>
                FixedWindow(httpContext, Scope.Admin, environment));

            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                FixedWindow(httpContext, Scope.Global, environment));
        });

        return services;
    }

    private enum Scope
    {
        Auth,
        Admin,
        Global,
    }

    private static RateLimitPartition<string> FixedWindow(HttpContext context, Scope scope, IHostEnvironment environment)
    {
        var limits = context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
        var (name, permitLimit) = scope switch
        {
            Scope.Auth => (AuthPolicy, limits.AuthPermitLimit),
            Scope.Admin => (AdminPolicy, limits.AdminPermitLimit),
            _ => ("global", limits.GlobalPermitLimit),
        };

        var key = $"{name}:{permitLimit}:{limits.WindowSeconds}:{ResolveClientIp(context, environment)}";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromSeconds(limits.WindowSeconds),
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    }

    private static string ResolveClientIp(HttpContext context, IHostEnvironment environment)
    {
        if (!environment.IsProduction())
        {
            var forwarded = context.Request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrWhiteSpace(forwarded))
            {
                var first = forwarded.Split(',')[0].Trim();
                if (!string.IsNullOrWhiteSpace(first))
                {
                    return first;
                }
            }
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static async ValueTask WriteRejectionAsync(OnRejectedContext context)
    {
        var httpContext = context.HttpContext;
        var retryAfter = 0;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var metadata))
        {
            retryAfter = (int)Math.Ceiling(metadata.TotalSeconds);
        }

        var problem = ApiProblem.Create(
            StatusCodes.Status429TooManyRequests,
            ApiErrorCodes.RateLimited,
            new Dictionary<string, object?> { ["retryAfter"] = retryAfter },
            ApiProblem.CurrentTraceId(httpContext));

        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await httpContext.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            cancellationToken: httpContext.RequestAborted);
    }
}
