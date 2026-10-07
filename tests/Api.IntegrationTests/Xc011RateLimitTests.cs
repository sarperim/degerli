using System.Net;
using System.Text.Json;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TC-XC-011 — rate limits and test-controllable client-IP keying (I-XC-2).
/// The 6th request within the window on an auth route → 429 <c>RATE_LIMITED</c>
/// with <c>params.retryAfter</c>; the global and admin limits are enforced at their
/// edges. Per I-XC-2 the limiter honors <c>X-Forwarded-For</c> in test configuration.
/// Traces: `03` §12, `01` §10.1.
/// </summary>
public sealed class Xc011RateLimitTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public Xc011RateLimitTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Auth_route_sixth_request_within_window_is_rate_limited()
    {
        using var client = _factory.CreateClient();
        const string ip = "203.0.113.31";

        for (var i = 1; i <= 5; i++)
        {
            using var allowed = await GetAuthProbeAsync(client, ip);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var rejected = await GetAuthProbeAsync(client, ip);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);

        var payload = await rejected.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        Assert.Equal("RATE_LIMITED", root.GetProperty("code").GetString());
        Assert.True(root.TryGetProperty("params", out var parameters));
        Assert.True(parameters.TryGetProperty("retryAfter", out var retryAfter));
        Assert.True(retryAfter.GetInt32() > 0);
    }

    [Fact]
    public async Task A_different_client_ip_has_its_own_budget()
    {
        using var client = _factory.CreateClient();

        for (var i = 1; i <= 5; i++)
        {
            using var allowed = await GetAuthProbeAsync(client, "203.0.113.41");
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        // Exhausted .31? No — a fresh IP still has room inside the window.
        using var other = await GetAuthProbeAsync(client, "203.0.113.42");
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    [Fact]
    public async Task Global_limit_is_enforced_at_its_edge()
    {
        var overrides = new Dictionary<string, string?> { ["RateLimits:GlobalPermitLimit"] = "3" };
        await using var factory = new ApiFactory(overrides, Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy);
        using var client = factory.CreateClient();
        const string ip = "203.0.113.51";

        for (var i = 1; i <= 3; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/_probe/rate");
            request.Headers.Add("X-Forwarded-For", ip);
            using var allowed = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var rejectedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/_probe/rate");
        rejectedRequest.Headers.Add("X-Forwarded-For", ip);
        using var rejected = await client.SendAsync(rejectedRequest);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    [Fact]
    public async Task Admin_limit_is_enforced_at_its_edge()
    {
        var overrides = new Dictionary<string, string?> { ["RateLimits:AdminPermitLimit"] = "2" };
        await using var factory = new ApiFactory(overrides, Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy);
        using var client = factory.CreateClient();

        for (var i = 1; i <= 2; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/_probe");
            request.Headers.Add("X-Forwarded-For", "203.0.113.61");
            using var allowed = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var sixth = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/_probe");
        sixth.Headers.Add("X-Forwarded-For", "203.0.113.61");
        using var rejected = await client.SendAsync(sixth);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    private static Task<HttpResponseMessage> GetAuthProbeAsync(HttpClient client, string ip)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/csrf-token");
        request.Headers.Add("X-Forwarded-For", ip);
        return client.SendAsync(request);
    }
}
