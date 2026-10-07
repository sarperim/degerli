using System.Net.Http.Json;
using System.Text.Json;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TC-XC-015 — method-aware caching decision. Mutations under an otherwise
/// public-read prefix (<c>POST /stocks/{symbol}/dcf/compute</c>) and mutation
/// surfaces outside it (<c>POST /screener/run</c>) must be <c>no-store</c>, never
/// <c>public</c>. Traces: `03` §13.
/// </summary>
public sealed class Xc015MutationNoStoreTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public Xc015MutationNoStoreTests(ApiFactory factory) => _factory = factory;

    [Theory]
    [InlineData("/api/v1/stocks/_probe/dcf/compute")]
    [InlineData("/api/v1/screener/run")]
    public async Task Post_to_dynamic_surface_is_no_store(string path)
    {
        using var client = _factory.CreateClient();
        var token = await FetchCsrfTokenAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("X-Forwarded-For", "198.51.100.91");
        request.Headers.Add("X-CSRF-Token", token);
        request.Content = JsonContent.Create(new { });

        using var response = await client.SendAsync(request);

        // The endpoint need not exist; the middleware classifies on method + path.
        Assert.NotNull(response.Headers.CacheControl);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.Null(response.Headers.ETag);
    }

    private static async Task<string> FetchCsrfTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/auth/csrf-token");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(payload);
        var token = document.RootElement.GetProperty("token").GetString();

        Assert.False(string.IsNullOrWhiteSpace(token));
        return token!;
    }
}
