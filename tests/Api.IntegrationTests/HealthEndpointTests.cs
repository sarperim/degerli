using System.Net;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// <c>GET /health</c> is the deploy smoke-check and UptimeRobot probe: liveness plus
/// the database dependency (`01` §10.3). Database reachability is stubbed here; the
/// real check runs in the compose/e2e stack (TC-XC-016).
/// </summary>
public sealed class HealthEndpointTests
{
    [Fact]
    public async Task Health_returns_200_when_database_check_is_healthy()
    {
        await using var factory = new ApiFactory(new Dictionary<string, string?>(), HealthStatus.Healthy);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Health_returns_503_when_database_check_is_unhealthy()
    {
        await using var factory = new ApiFactory(new Dictionary<string, string?>(), HealthStatus.Unhealthy);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync());
    }
}
