using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Degerli.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

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
        var csrfToken = await FetchCsrfTokenAsync(client);
        const string ip = "203.0.113.31";

        for (var i = 1; i <= 5; i++)
        {
            using var allowed = await PostAuthProbeAsync(client, ip, csrfToken);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var rejected = await PostAuthProbeAsync(client, ip, csrfToken);
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
        var csrfToken = await FetchCsrfTokenAsync(client);

        for (var i = 1; i <= 5; i++)
        {
            using var allowed = await PostAuthProbeAsync(client, "203.0.113.41", csrfToken);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        // Exhausted .41? No — a fresh IP still has room inside the window.
        using var other = await PostAuthProbeAsync(client, "203.0.113.42", csrfToken);
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

    [Fact]
    public async Task Production_environment_keys_rate_limiting_on_rightmost_forwarded_hop()
    {
        await using var factory = _factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = factory.CreateClient();
        var csrfToken = await FetchCsrfTokenAsync(client);

        const string clientIp1 = "203.0.113.81";
        const string clientIp2 = "203.0.113.82";

        for (var i = 1; i <= 5; i++)
        {
            using var allowed = await PostAuthProbeAsync(client, $"198.51.100.{i}, {clientIp1}", csrfToken);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        // 5 requests on .81 do not block .82 (independent auth budget)
        using var client2Allowed = await PostAuthProbeAsync(client, clientIp2, csrfToken);
        Assert.Equal(HttpStatusCode.OK, client2Allowed.StatusCode);

        // 6th request with spoofed prefix for clientIp1 is rejected because Production keys on rightmost hop
        using var rejected = await PostAuthProbeAsync(client, $"198.51.100.6, {clientIp1}", csrfToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    [Fact]
    public async Task Session_and_csrf_endpoints_do_not_consume_the_auth_mutation_budget()
    {
        using var client = _factory.CreateClient();
        const string ip = "203.0.113.91";

        for (var i = 1; i <= 10; i++)
        {
            using var sessionRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/session");
            sessionRequest.Headers.Add("X-Forwarded-For", ip);
            using var sessionResponse = await client.SendAsync(sessionRequest);
            Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);
        }

        string csrfToken = string.Empty;
        for (var i = 1; i <= 10; i++)
        {
            csrfToken = await FetchCsrfTokenAsync(client, ip);
        }

        for (var i = 1; i <= 5; i++)
        {
            using var allowed = await PostAuthProbeAsync(client, ip, csrfToken);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var rejected = await PostAuthProbeAsync(client, ip, csrfToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);

        using var document = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        Assert.Equal("RATE_LIMITED", document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Trusted_proxy_forwarded_proto_marks_csrf_cookie_secure_while_untrusted_does_not()
    {
        var trustedContext = await _factory.Server.SendAsync(ctx =>
        {
            ctx.Request.Method = HttpMethods.Get;
            ctx.Request.Path = "/api/v1/auth/csrf-token";
            ctx.Request.Scheme = "http";
            ctx.Connection.RemoteIpAddress = IPAddress.Parse("172.18.0.4");
            ctx.Request.Headers["X-Forwarded-Proto"] = "https";
        });

        Assert.Equal(StatusCodes.Status200OK, trustedContext.Response.StatusCode);
        var trustedCsrfCookie = Assert.Single(
            trustedContext.Response.Headers.SetCookie.ToArray(),
            cookie => cookie is not null && cookie.StartsWith("degerli.csrf=", StringComparison.Ordinal));
        Assert.Contains("secure", trustedCsrfCookie, StringComparison.OrdinalIgnoreCase);

        var untrustedContext = await _factory.Server.SendAsync(ctx =>
        {
            ctx.Request.Method = HttpMethods.Get;
            ctx.Request.Path = "/api/v1/auth/csrf-token";
            ctx.Request.Scheme = "http";
            ctx.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.77");
            ctx.Request.Headers["X-Forwarded-Proto"] = "https";
        });

        Assert.Equal(StatusCodes.Status200OK, untrustedContext.Response.StatusCode);
        var untrustedCsrfCookie = Assert.Single(
            untrustedContext.Response.Headers.SetCookie.ToArray(),
            cookie => cookie is not null && cookie.StartsWith("degerli.csrf=", StringComparison.Ordinal));
        Assert.DoesNotContain("secure", untrustedCsrfCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Production_resolve_client_ip_handles_trusted_proxy_and_untrusted_peers()
    {
        var environment = new StubHostEnvironment { EnvironmentName = Environments.Production };

        // Trusted Docker bridge peer (IPv4)
        var ctx1 = new DefaultHttpContext();
        ctx1.Connection.RemoteIpAddress = IPAddress.Parse("172.18.0.4");
        ctx1.Request.Headers["X-Forwarded-For"] = "1.1.1.1, 203.0.113.88";
        Assert.Equal("203.0.113.88", RateLimitingSetup.ResolveClientIp(ctx1, environment));

        // Trusted Docker bridge peer (IPv4-mapped IPv6)
        var ctx2 = new DefaultHttpContext();
        ctx2.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:172.18.0.4");
        ctx2.Request.Headers["X-Forwarded-For"] = "1.1.1.1, 203.0.113.88";
        Assert.Equal("203.0.113.88", RateLimitingSetup.ResolveClientIp(ctx2, environment));

        // Untrusted public peer ignores header and returns normalized remote IP
        var ctx3 = new DefaultHttpContext();
        ctx3.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.77");
        ctx3.Request.Headers["X-Forwarded-For"] = "1.1.1.1";
        Assert.Equal("198.51.100.77", RateLimitingSetup.ResolveClientIp(ctx3, environment));

        // Trusted proxy peer with missing X-Forwarded-For falls back to remote IP
        var ctx4 = new DefaultHttpContext();
        ctx4.Connection.RemoteIpAddress = IPAddress.Parse("172.18.0.4");
        Assert.Equal("172.18.0.4", RateLimitingSetup.ResolveClientIp(ctx4, environment));

        // Trusted proxy peer with non-IP X-Forwarded-For falls back to remote IP
        var ctx5 = new DefaultHttpContext();
        ctx5.Connection.RemoteIpAddress = IPAddress.Parse("172.18.0.4");
        ctx5.Request.Headers["X-Forwarded-For"] = "not-an-ip";
        Assert.Equal("172.18.0.4", RateLimitingSetup.ResolveClientIp(ctx5, environment));
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Degerli.Api";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static async Task<string> FetchCsrfTokenAsync(HttpClient client, string? ip = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/csrf-token");
        if (!string.IsNullOrWhiteSpace(ip))
        {
            request.Headers.Add("X-Forwarded-For", ip);
        }

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("token").GetString()!;
    }

    private static Task<HttpResponseMessage> PostAuthProbeAsync(HttpClient client, string ip, string csrfToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/forgot-password")
        {
            Content = JsonContent.Create(new { }),
        };
        request.Headers.Add("X-Forwarded-For", ip);
        request.Headers.Add("X-CSRF-Token", csrfToken);
        return client.SendAsync(request);
    }
}
