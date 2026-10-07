using System.Net;
using System.Net.Http.Headers;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TC-XC-015 — caching policy per surface class. Public read paths →
/// <c>public, max-age=300</c> + strong ETag (unchanged revalidation returns 304);
/// personal/mutating paths → <c>no-store</c>. Traces: `03` §13.
/// </summary>
public sealed class Xc015CachingTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public Xc015CachingTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Public_read_path_is_public_max_age_300_with_strong_etag()
    {
        using var client = _factory.CreateClient();

        using var response = await GetCachedAsync(client, "198.51.100.81");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("public", response.Headers.CacheControl!.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-age=300", response.Headers.CacheControl!.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(response.Headers.ETag);
        Assert.False(response.Headers.ETag!.IsWeak);
    }

    [Fact]
    public async Task Unchanged_public_read_revalidation_returns_304()
    {
        using var client = _factory.CreateClient();

        using var first = await GetCachedAsync(client, "198.51.100.82");
        var etag = first.Headers.ETag;
        Assert.NotNull(etag);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/stocks/_probe");
        request.Headers.Add("X-Forwarded-For", "198.51.100.82");
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag!.ToString()));

        using var second = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        Assert.NotNull(second.Headers.ETag);
    }

    [Fact]
    public async Task Personal_path_is_no_store()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me/_probe");
        request.Headers.Add("X-Forwarded-For", "198.51.100.83");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
    }

    private static Task<HttpResponseMessage> GetCachedAsync(HttpClient client, string ip)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/stocks/_probe");
        request.Headers.Add("X-Forwarded-For", ip);
        return client.SendAsync(request);
    }
}
