using System.Net;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TC-XC-009 — representative responses carry the security headers:
/// <c>X-Content-Type-Options: nosniff</c> and a CSP with <c>default-src 'self'</c>
/// and <c>frame-ancestors 'none'</c> (HSTS is asserted in the TLS posture only —
/// I-XC-1). Traces: `01` §10.5.
/// </summary>
public sealed class Xc009SecurityHeadersTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public Xc009SecurityHeadersTests(ApiFactory factory) => _factory = factory;

    [Theory]
    [InlineData("/api/v1/openapi.json")]
    [InlineData("/health")]
    public async Task Representative_responses_carry_security_headers(string path)
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Forwarded-For", "198.51.100.71");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());

        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("default-src 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", csp, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", csp, StringComparison.OrdinalIgnoreCase);
    }
}
