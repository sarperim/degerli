using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Degerli.ContentPipeline.IntegrationTests;

/// <summary>
/// A per-class WireMock server doubling the evren AI drafting API at the HTTP boundary
/// (test strategy §7). The body served is the checked-in recorded catalog payload
/// (<c>evren-draft-ok</c>, FU §10); the real evren API is never reached.
/// </summary>
public sealed class EvrenWireMockFixture : IAsyncLifetime
{
    public WireMockServer Server { get; private set; } = null!;

    public string BaseUrl => Server.Url!;

    public Task InitializeAsync()
    {
        Server = WireMockServer.Start();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        Server.Stop();
        Server.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>Stubs the drafting route with a recorded JSON body.</summary>
    public void StubDraft(string path, string json)
    {
        // Each test re-registers the recorded double; reset first so repeated registration
        // never leaves an ambiguous mapping.
        Server.ResetMappings();

        Server
            .Given(Request.Create().WithPath(path).UsingPost())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(json));
    }

    /// <summary>Requests seen at the drafting route (request-count assertions).</summary>
    public int RequestsFor(string path) =>
        Server.LogEntries.Count(entry => entry.RequestMessage?.Path == path);
}
