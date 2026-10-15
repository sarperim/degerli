using System.Net;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TC-XC-015 — caching-policy decision table per surface class (`03` §13).
/// Public daily-data reads → <c>public, max-age=300</c> + strong ETag; personal
/// (<c>/me/*</c>), auth (<c>/auth/*</c>) and admin (<c>/admin</c>) surfaces →
/// <c>no-store</c>. The mutating half of the table (POST run/compute never public)
/// is covered by <see cref="Xc015MutationNoStoreTests"/>. Traces: `03` §13.
/// </summary>
public sealed class Xc015CachingSurfaceTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public Xc015CachingSurfaceTests(ApiFactory factory) => _factory = factory;

    public static TheoryData<string> PublicReadSurfaces => new()
    {
        "/api/v1/market/_probe",
        "/api/v1/stocks/_probe",
        "/api/v1/screener/metrics/_probe",
    };

    public static TheoryData<string> NoStoreSurfaces => new()
    {
        "/api/v1/me/_probe",
        "/api/v1/auth/csrf-token",
        "/api/v1/admin/_probe",
    };

    [Theory]
    [MemberData(nameof(PublicReadSurfaces))]
    public async Task Public_read_surface_is_public_max_age_300_with_a_strong_etag(string path)
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Forwarded-For", "198.51.100.101");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=300", response.Headers.CacheControl!.ToString());
        Assert.NotNull(response.Headers.ETag);
        Assert.False(response.Headers.ETag!.IsWeak);
    }

    [Theory]
    [MemberData(nameof(NoStoreSurfaces))]
    public async Task Personal_auth_and_admin_surfaces_are_no_store(string path)
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Forwarded-For", "198.51.100.102");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.Null(response.Headers.ETag);
    }
}
