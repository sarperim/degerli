using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Degerli.Api.Infrastructure;

/// <summary>
/// Builds RFC 7807 <see cref="ProblemDetails"/> bodies with the stable machine
/// <c>code</c>, i18n-neutral <c>params</c> and support <c>traceId</c> (`03` §7).
/// </summary>
public static class ApiProblem
{
    public static ProblemDetails Create(
        int status,
        string code,
        IReadOnlyDictionary<string, object?>? parameters = null,
        string? traceId = null,
        string? title = null)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title ?? ApiErrorTitles.For(code),
            Type = $"/api/v1/errors/{Slug(code)}",
        };

        problem.Extensions["code"] = code;
        if (parameters is not null)
        {
            problem.Extensions["params"] = parameters;
        }

        problem.Extensions["traceId"] = traceId ?? CurrentTraceId();
        return problem;
    }

    /// <summary>Server-side correlation id for support (never a raw exception).</summary>
    public static string CurrentTraceId(HttpContext? context = null)
        => Activity.Current?.Id ?? context?.TraceIdentifier ?? Guid.NewGuid().ToString("N");

    public static string Slug(string code) => code.ToLowerInvariant().Replace('_', '-');
}

/// <summary>Human-readable ProblemDetails titles; the SPA still renders copy from <c>code</c>.</summary>
public static class ApiErrorTitles
{
    public static string For(string code) => code switch
    {
        ApiErrorCodes.ValidationFailed => "Validation failed",
        ApiErrorCodes.MetricNotAvailable => "Metric not available",
        ApiErrorCodes.Unauthenticated => "Authentication required",
        ApiErrorCodes.InvalidCredentials => "Invalid credentials",
        ApiErrorCodes.Forbidden => "Forbidden",
        ApiErrorCodes.EmailNotVerified => "E-mail not verified",
        ApiErrorCodes.NotFound => "Not found",
        ApiErrorCodes.EmailTaken => "E-mail already registered",
        ApiErrorCodes.DuplicateName => "Duplicate name",
        ApiErrorCodes.DcfNotComputable => "DCF not computable",
        ApiErrorCodes.TokenExpired => "Token expired",
        ApiErrorCodes.LockedOut => "Account locked",
        ApiErrorCodes.RateLimited => "Too many requests",
        ApiErrorCodes.Internal => "Internal server error",
        ApiErrorCodes.Conflict => "Conflict",
        _ => "Request failed",
    };
}
