using Degerli.Fixtures;

namespace Degerli.Api.IntegrationTests.Harness;

/// <summary>
/// WireMock mapping helper for the checked-in <b>real-source</b> payload catalog
/// (TKT-foundation-015; 01 v1.5 §10.9.7). A test names the real-source payload it wants
/// the source double to serve; the double returns the catalog's <b>raw bytes</b> with the
/// declared <c>Content-Type; charset=</c> header — never a re-serialized canonical
/// envelope — so the bytes flow through the production <c>ISourceClient</c> exactly as the
/// live source would. Built on the TKT-foundation-006 <see cref="WireMockFixture"/>.
/// </summary>
public static class RealSourceWireMockExtensions
{
    /// <summary>
    /// Stubs one named real-source payload at its raw bytes and default route.
    /// <paramref name="path"/> overrides the payload's default route (use this to match the
    /// route the adapter under test actually calls); <paramref name="method"/> overrides GET.
    /// </summary>
    public static RealSourcePayload StubRealSource(
        this WireMockFixture wireMock,
        string name,
        string? path = null,
        string? method = null)
    {
        ArgumentNullException.ThrowIfNull(wireMock);

        var payload = RealSourceCatalog.Get(name);
        wireMock.StubBytes(
            path ?? payload.DefaultPath,
            payload.RawBytes,
            $"application/json; charset={payload.EncodingName}",
            payload.StatusCode,
            method ?? payload.Method);
        return payload;
    }
}
