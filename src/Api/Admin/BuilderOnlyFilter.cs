using Degerli.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;

namespace Degerli.Api.Admin;

/// <summary>
/// Builder-only gate for the admin surface (`03` §8, `01` §10.1): every endpoint on the
/// <c>/admin</c> route group is callable only by an authenticated user in the
/// <c>builder</c> role. Anonymous callers get <c>401 UNAUTHENTICATED</c>; authenticated
/// non-builders get <c>403 FORBIDDEN</c> — both as RFC 7807 problems carrying the stable
/// machine <c>code</c> the SPA renders (TC-MDF-039).
/// <para>
/// Endpoints that opt out with <see cref="AllowAnonymousAttribute"/> (the non-production
/// infrastructure probe) bypass the gate so the mechanism tests can exercise rate
/// limiting without a builder session.
/// </para>
/// </summary>
public sealed class BuilderOnlyFilter : IEndpointFilter
{
    /// <summary>The role that owns the admin surface.</summary>
    public const string BuilderRole = "builder";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        if (httpContext.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            return await next(context);
        }

        if (httpContext.User.Identity?.IsAuthenticated != true)
        {
            return Results.Problem(ApiProblem.Create(
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.Unauthenticated));
        }

        if (!httpContext.User.IsInRole(BuilderRole))
        {
            return Results.Problem(ApiProblem.Create(
                StatusCodes.Status403Forbidden,
                ApiErrorCodes.Forbidden));
        }

        return await next(context);
    }
}
