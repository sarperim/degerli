using System.Net;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-foundation-008: the mapping helper can stub the whole catalog in one call, each
/// named payload at its default route with its named status — including the 500
/// source-down class.
/// </summary>
public sealed class CannedSourceWireMockCatalogTests : IClassFixture<WireMockFixture>
{
    private readonly WireMockFixture _wireMock;

    public CannedSourceWireMockCatalogTests(WireMockFixture wireMock) => _wireMock = wireMock;

    [Fact]
    public async Task Stub_all_serves_every_named_payload_at_its_default_route()
    {
        var set = FixtureUniverse.Build(FixtureAnchor.L2);
        _wireMock.StubAllCannedSources(set);

        using var client = new HttpClient();
        foreach (var payload in CannedSourceCatalog.Build(set))
        {
            // The slow payload carries a real 5 s delay; its delay is asserted on the
            // catalog record rather than paid for here.
            if (payload.DelayMs > 0)
            {
                continue;
            }

            using var response = await client.GetAsync($"{_wireMock.BaseUrl}{payload.DefaultPath}");
            Assert.Equal((HttpStatusCode)payload.StatusCode, response.StatusCode);
            Assert.Equal(1, _wireMock.RequestsFor(payload.DefaultPath));
        }
    }
}
