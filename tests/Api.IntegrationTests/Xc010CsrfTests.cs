using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TC-XC-010 — decision table: unsafe method × token present/absent.
/// A probe POST without <c>X-CSRF-Token</c> → 400; with the token issued by
/// <c>GET /api/v1/auth/csrf-token</c> → proceeds. Safe GETs are unaffected.
/// Traces: `03` §2, `01` §10.1.
/// </summary>
public sealed class Xc010CsrfTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public Xc010CsrfTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Post_without_token_is_rejected_with_400()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/_probe");
        request.Headers.Add("X-Forwarded-For", "198.51.100.21");
        request.Content = JsonContent.Create(new { });

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_with_token_proceeds()
    {
        using var client = _factory.CreateClient();
        var token = await FetchCsrfTokenAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/_probe");
        request.Headers.Add("X-Forwarded-For", "198.51.100.22");
        request.Headers.Add("X-CSRF-Token", token);
        request.Content = JsonContent.Create(new { });

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Get_is_unaffected_by_csrf()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/auth/csrf-token");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
