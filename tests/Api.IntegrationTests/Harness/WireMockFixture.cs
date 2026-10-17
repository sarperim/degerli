using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Degerli.Api.IntegrationTests.Harness;

/// <summary>
/// WireMock.Net server helper (test strategy §7): a per-test-class HTTP double for the
/// external source APIs (KAP, İşbank, TÜİK, TEFAS …). Canned payloads are added by
/// TKT-foundation-008 through <see cref="StubJson"/>; no test ever reaches a real
/// source. Use as an xUnit class fixture.
/// </summary>
public sealed class WireMockFixture : IAsyncLifetime
{
    public WireMockServer Server { get; private set; } = null!;

    /// <summary>Base URL to hand to the application's source-client configuration.</summary>
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

    /// <summary>Stub a route returning a raw JSON body.</summary>
    public void StubJson(string path, string json, int statusCode = 200, string method = "GET")
    {
        var request = RequestFor(path, method);

        Server
            .Given(request)
            .RespondWith(
                Response.Create()
                    .WithStatusCode(statusCode)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(json));
    }

    /// <summary>
    /// Stub a route returning raw <b>bytes</b> with an explicit <c>Content-Type</c> (including
    /// any <c>charset=</c> parameter). Used by the real-source catalog so a code-page body is
    /// served byte-for-byte, exactly as the live source would (TKT-foundation-015).
    /// </summary>
    public void StubBytes(
        string path,
        byte[] body,
        string contentType,
        int statusCode = 200,
        string method = "GET")
    {
        var request = RequestFor(path, method);

        Server
            .Given(request)
            .RespondWith(
                Response.Create()
                    .WithStatusCode(statusCode)
                    .WithHeader("Content-Type", contentType)
                    .WithBody(body));
    }

    private static IRequestBuilder RequestFor(string path, string method) =>
        method.ToUpperInvariant() switch
        {
            "POST" => Request.Create().WithPath(path).UsingPost(),
            "PUT" => Request.Create().WithPath(path).UsingPut(),
            "DELETE" => Request.Create().WithPath(path).UsingDelete(),
            _ => Request.Create().WithPath(path).UsingGet(),
        };

    /// <summary>Number of matching requests seen so far (request-count assertions).</summary>
    public int RequestsFor(string path) =>
        Server.LogEntries.Count(entry => entry.RequestMessage?.Path == path);
}
