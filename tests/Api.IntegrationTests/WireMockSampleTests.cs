using Degerli.Api.IntegrationTests.Harness;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// Sample proof that the WireMock.Net source-double helper works: the canned payload
/// catalog (TKT-foundation-008) is served through exactly this helper, so no adapter
/// test ever reaches a real source API.
/// </summary>
public sealed class WireMockSampleTests : IClassFixture<WireMockFixture>
{
    private readonly WireMockFixture _wireMock;

    public WireMockSampleTests(WireMockFixture wireMock) => _wireMock = wireMock;

    [Fact]
    public async Task Wiremock_helper_serves_a_stubbed_json_response_and_counts_requests()
    {
        _wireMock.StubJson("/prices/HRNST", "{\"symbol\":\"HRNST\",\"close\":42.5}");

        using var client = new HttpClient();
        using var response = await client.GetAsync($"{_wireMock.BaseUrl}/prices/HRNST");

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"symbol\":\"HRNST\"", json, StringComparison.Ordinal);
        Assert.Equal(1, _wireMock.RequestsFor("/prices/HRNST"));
    }

    [Fact]
    public async Task Wiremock_helper_can_stub_an_error_status()
    {
        _wireMock.StubJson("/prices/DOWN", "{\"error\":\"source down\"}", statusCode: 500);

        using var client = new HttpClient();
        using var response = await client.GetAsync($"{_wireMock.BaseUrl}/prices/DOWN");

        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);
    }
}
