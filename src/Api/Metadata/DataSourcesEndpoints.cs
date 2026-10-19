namespace Degerli.Api.Metadata;

/// <summary>
/// The metadata module's public read surface (`03-api-design.md` §3;
/// FR-MDF-008, FR-MOV-020): <c>GET /api/v1/meta/data-sources</c> — the per-class
/// data-source provenance catalog behind the site-wide disclosure surface.
///
/// It is anonymous (research surfaces are public, BR-ACC-003), reads a small
/// static product-side catalog (<see cref="DataSourceCatalog"/>) and needs no
/// database or migration. The response is language-neutral: stable class codes and
/// honest state markers (<c>state: live|unavailable</c>, the §10.7 vocabulary) plus
/// the concrete source name; all user-facing prose (TR/EN) is rendered by the SPA
/// from the i18n catalogs (`03` §1.1, `01` §10.6).
///
/// The response is served under the global API rate limit (`03` §12); it is a static
/// catalog, not a per-request data payload, so it carries no honest-data envelope.
/// </summary>
public static class DataSourcesEndpoints
{
    public static IEndpointRouteBuilder MapDegerliDataSourcesEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/meta/data-sources", GetDataSources).AllowAnonymous();
        return api;
    }

    private static IResult GetDataSources()
        => Results.Ok(new
        {
            sources = DataSourceCatalog.All.Select(entry => new
            {
                id = entry.Id,
                @class = entry.Class,
                source = entry.Source,
                state = entry.State,
                nature = entry.Nature,
            }),
        });
}
