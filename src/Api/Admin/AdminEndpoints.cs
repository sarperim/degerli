using Degerli.Api.Infrastructure;

namespace Degerli.Api.Admin;

/// <summary>
/// The builder-only admin route group (`03` §8, AD-13). One group carries the admin rate
/// limit (`03` §12) and the <see cref="BuilderOnlyFilter"/>, so every endpoint under
/// <c>/api/v1/admin</c> is gated identically. TKT-mdf-010 owns this file and the ops
/// endpoints it maps; later admin tickets (TKT-mdf-011 quarantine, TKT-res-006
/// descriptions, TKT-scr-005 metric visibility, TKT-val-009 baselines) extend the group
/// with their own endpoint files.
/// </summary>
public static class AdminEndpoints
{
    /// <summary>Maps the gated <c>/api/v1/admin</c> group and the TKT-mdf-010 ops endpoints.</summary>
    public static RouteGroupBuilder MapDegerliAdminEndpoints(this IEndpointRouteBuilder api)
    {
        var admin = api
            .MapGroup("/admin")
            .RequireRateLimiting(RateLimitingSetup.AdminPolicy)
            .AddEndpointFilter<BuilderOnlyFilter>();

        admin.MapDegerliAdminOpsEndpoints();
        admin.MapDegerliAdminQuarantineEndpoints();
        admin.MapDegerliAdminDescriptionEndpoints();
        admin.MapDegerliAdminDcfBaselineEndpoints();

        return admin;
    }
}
