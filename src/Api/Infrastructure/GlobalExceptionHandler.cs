using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Degerli.Api.Infrastructure;

/// <summary>
/// Catches unhandled exceptions and returns a ProblemDetails <c>INTERNAL</c> response
/// with a support <c>traceId</c> — the exception itself is logged server-side with the
/// same correlation id and never serialized to the client (`01` §10.2, `03` §7).
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var traceId = ApiProblem.CurrentTraceId(httpContext);

        _logger.LogError(
            exception,
            "Unhandled exception for {Method} {Path}; traceId {TraceId}",
            httpContext.Request.Method,
            httpContext.Request.Path,
            traceId);

        var problem = ApiProblem.Create(
            StatusCodes.Status500InternalServerError,
            ApiErrorCodes.Internal,
            traceId: traceId);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            cancellationToken: cancellationToken);

        return true;
    }
}
