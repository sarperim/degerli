using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;

namespace Degerli.Api.Infrastructure;

/// <summary>The caching policy class for a request path (`03` §13).</summary>
public enum CachePolicy
{
    /// <summary>No explicit policy (mutation/vendor surfaces outside the summary table).</summary>
    None,

    /// <summary>Daily public data: <c>public, max-age=300</c> + strong ETag revalidation.</summary>
    PublicRead,

    /// <summary>Personal/mutating surfaces: <c>no-store</c>.</summary>
    NoStore,
}

/// <summary>
/// Caching-policy helpers per `03` §13. Only safe reads (<c>GET</c>/<c>HEAD</c>) on the
/// public daily-data surfaces are cacheable for five minutes with a strong ETag;
/// <c>/me/*</c>, <c>/auth/*</c> and <c>/admin</c> are never stored, and any unsafe
/// (mutating) method under the API prefix is dynamic/user-specific and never stored
/// (e.g. <c>POST …/dcf/compute</c>, <c>POST /screener/run</c>).
/// </summary>
public static class CachingPolicies
{
    public const string PublicReadCacheControl = "public, max-age=300";
    public const string NoStoreCacheControl = "no-store";

    public static CachePolicy Classify(string method, PathString path)
    {
        // Personal, auth and admin surfaces are never stored, for any method.
        if (path.StartsWithSegments("/api/v1/me", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/api/v1/auth", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/api/v1/admin", StringComparison.OrdinalIgnoreCase))
        {
            return CachePolicy.NoStore;
        }

        var safeMethod = IsSafeMethod(method);

        if (safeMethod
            && (path.StartsWithSegments("/api/v1/market", StringComparison.OrdinalIgnoreCase)
                || path.StartsWithSegments("/api/v1/stocks", StringComparison.OrdinalIgnoreCase)
                || path.StartsWithSegments("/api/v1/screener/metrics", StringComparison.OrdinalIgnoreCase)))
        {
            return CachePolicy.PublicRead;
        }

        // Unsafe methods carry dynamic, user-specific intent (compute/run) and must
        // never be publicly cached, even under an otherwise public-read prefix.
        if (!safeMethod
            && path.StartsWithSegments("/api/v1", StringComparison.OrdinalIgnoreCase))
        {
            return CachePolicy.NoStore;
        }

        return CachePolicy.None;
    }

    /// <summary>Only <c>GET</c>/<c>HEAD</c> are cacheable as safe reads (`03` §13).</summary>
    public static bool IsSafeMethod(string method)
        => HttpMethods.IsGet(method) || HttpMethods.IsHead(method);

    public static void ApplyPublicRead(HttpContext context) => context.Response.Headers.CacheControl = PublicReadCacheControl;

    public static void ApplyNoStore(HttpContext context) => context.Response.Headers.CacheControl = NoStoreCacheControl;

    /// <summary>Strong ETag (quoted, no <c>W/</c>) from a response-body digest.</summary>
    public static string ComputeETag(ReadOnlySpan<byte> body)
    {
        var hash = SHA256.HashData(body);
        return '"' + Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant() + '"';
    }
}
