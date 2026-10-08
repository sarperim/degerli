using Degerli.Fixtures;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Degerli.Api.IntegrationTests.Harness;

/// <summary>
/// WireMock mapping helpers for the checked-in canned-source payload catalog
/// (TKT-foundation-008): rather than hand-writing a JSON body into every adapter test,
/// a test names the FU §10 payload it wants the source double to serve. The body comes
/// from <see cref="CannedSourceCatalog"/> so it can never drift from the seeded universe.
/// Built on the TKT-foundation-006 <see cref="WireMockFixture"/>.
/// </summary>
public static class CannedSourceWireMockExtensions
{
    /// <summary>
    /// Stubs one named catalog payload. <paramref name="path"/> overrides the payload's
    /// default route (use this to match the route the adapter under test actually calls);
    /// <paramref name="method"/> overrides the default GET.
    /// </summary>
    public static CannedSourcePayload StubCannedSource(
        this WireMockFixture wireMock,
        FixtureSet fixtures,
        string name,
        string? path = null,
        string? method = null)
    {
        ArgumentNullException.ThrowIfNull(wireMock);
        ArgumentNullException.ThrowIfNull(fixtures);

        var payload = CannedSourceCatalog.Get(fixtures, name);
        var route = path ?? payload.DefaultPath;
        var verb = (method ?? payload.Method).ToUpperInvariant();

        var request = verb switch
        {
            "POST" => Request.Create().WithPath(route).UsingPost(),
            "PUT" => Request.Create().WithPath(route).UsingPut(),
            "DELETE" => Request.Create().WithPath(route).UsingDelete(),
            _ => Request.Create().WithPath(route).UsingGet(),
        };

        var response = Response.Create()
            .WithStatusCode(payload.StatusCode)
            .WithHeader("Content-Type", "application/json")
            .WithBody(payload.Body);
        if (payload.DelayMs > 0)
        {
            response = response.WithDelay(TimeSpan.FromMilliseconds(payload.DelayMs));
        }

        wireMock.Server.Given(request).RespondWith(response);
        return payload;
    }

    /// <summary>Stubs every named catalog payload at its default route.</summary>
    public static void StubAllCannedSources(this WireMockFixture wireMock, FixtureSet fixtures)
    {
        ArgumentNullException.ThrowIfNull(wireMock);
        ArgumentNullException.ThrowIfNull(fixtures);

        foreach (var payload in CannedSourceCatalog.Build(fixtures))
        {
            wireMock.StubCannedSource(fixtures, payload.Name);
        }
    }
}
