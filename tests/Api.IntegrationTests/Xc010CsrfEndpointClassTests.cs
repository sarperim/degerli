using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TC-XC-010 — the CSRF decision table extended across the endpoint classes the
/// ticket covers: the anonymous dynamic surface (<c>POST /screener/run</c>) and the
/// personal saved-item CRUD (<c>POST /me/screens</c>, <c>PATCH</c>/<c>DELETE
/// /me/screens/{id}</c>). Every unsafe method under <c>/api/v1</c> without a valid
/// <c>X-CSRF-Token</c> is rejected with the antiforgery 400 before any endpoint logic
/// runs; with the token the request proceeds past the middleware (its later status is
/// the endpoint's, not CSRF's). Safe methods are unaffected. Traces: `03` §2,
/// `01` §10.1.
/// </summary>
public sealed class Xc010CsrfEndpointClassTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public Xc010CsrfEndpointClassTests(ApiFactory factory) => _factory = factory;

    public static TheoryData<string, string> UnsafeEndpointClasses => new()
    {
        { "POST", "/api/v1/screener/run" },
        { "POST", "/api/v1/me/screens" },
        { "PATCH", "/api/v1/me/screens/1" },
        { "DELETE", "/api/v1/me/screens/1" },
    };

    [Theory]
    [MemberData(nameof(UnsafeEndpointClasses))]
    public async Task Unsafe_method_without_token_is_rejected_with_the_csrf_400(string method, string path)
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("X-Forwarded-For", "198.51.100.31");
        request.Content = JsonContent.Create(new { });

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await IsCsrfRejectionAsync(response), "expected the antiforgery rejection ProblemDetails");
    }

    [Theory]
    [MemberData(nameof(UnsafeEndpointClasses))]
    public async Task Unsafe_method_with_token_proceeds_past_csrf(string method, string path)
    {
        using var client = _factory.CreateClient();
        var token = await FetchCsrfTokenAsync(client);

        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("X-Forwarded-For", "198.51.100.32");
        request.Headers.Add("X-CSRF-Token", token);
        request.Content = JsonContent.Create(new { });

        using var response = await client.SendAsync(request);

        Assert.False(
            await IsCsrfRejectionAsync(response),
            $"CSRF must not reject a request carrying the token (status {response.StatusCode})");
    }

    [Fact]
    public async Task Safe_get_on_a_personal_surface_is_unaffected_by_csrf()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me/screens");
        request.Headers.Add("X-Forwarded-For", "198.51.100.33");

        using var response = await client.SendAsync(request);

        // No CSRF rejection; without a session the auth layer answers 401.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(await IsCsrfRejectionAsync(response));
    }

    private static async Task<bool> IsCsrfRejectionAsync(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.BadRequest)
        {
            return false;
        }

        var payload = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        if (!root.TryGetProperty("params", out var parameters)
            || !parameters.TryGetProperty("reason", out var reason))
        {
            return false;
        }

        return reason.GetString() == "csrf";
    }

    private static async Task<string> FetchCsrfTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/auth/csrf-token");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("token").GetString()!;
    }
}
