using Microsoft.AspNetCore.Http;

namespace Degerli.Api.Infrastructure;

/// <summary>
/// Applies the `03` §13 caching policy by path class: public read paths get a strong
/// ETag and <c>public, max-age=300</c> with 304 revalidation; personal/mutating paths
/// get <c>no-store</c>.
/// </summary>
public sealed class CachingMiddleware
{
    private readonly RequestDelegate _next;

    public CachingMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        switch (CachingPolicies.Classify(context.Request.Method, context.Request.Path))
        {
            case CachePolicy.None:
                await _next(context);
                return;

            case CachePolicy.NoStore:
                context.Response.OnStarting(static state =>
                {
                    CachingPolicies.ApplyNoStore((HttpContext)state);
                    return Task.CompletedTask;
                }, context);
                await _next(context);
                return;

            case CachePolicy.PublicRead:
                await InvokePublicReadAsync(context);
                return;

            default:
                await _next(context);
                return;
        }
    }

    private async Task InvokePublicReadAsync(HttpContext context)
    {
        var originalBody = context.Response.Body;
        var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await _next(context);

            var bytes = buffer.ToArray();
            var etag = CachingPolicies.ComputeETag(bytes);

            if (context.Response.StatusCode == StatusCodes.Status200OK)
            {
                context.Response.Headers.ETag = etag;
                CachingPolicies.ApplyPublicRead(context);

                if (MatchesIfNoneMatch(context.Request.Headers.IfNoneMatch.ToString(), etag))
                {
                    context.Response.StatusCode = StatusCodes.Status304NotModified;
                    context.Response.ContentLength = null;
                    return;
                }
            }

            context.Response.ContentLength = bytes.Length;
            await originalBody.WriteAsync(bytes, context.RequestAborted);
        }
        finally
        {
            context.Response.Body = originalBody;
            await buffer.DisposeAsync();
        }
    }

    private static bool MatchesIfNoneMatch(string ifNoneMatch, string etag)
    {
        if (string.IsNullOrWhiteSpace(ifNoneMatch))
        {
            return false;
        }

        return ifNoneMatch
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(candidate => candidate == "*" || candidate == etag);
    }
}
