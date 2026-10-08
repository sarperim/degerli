using System.Net;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-foundation-008 acceptance: a sample stubbed ingest consumes <c>prices-ok.json</c>
/// end-to-end through the TKT-foundation-006 harness — the canned-source mapping helper
/// serves the payload over HTTP and the fact it delivers matches the seeded
/// <c>daily_prices</c> rows.
/// </summary>
public sealed class CannedSourceWireMockSampleTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private static readonly DateOnly AnchorT = new(2026, 10, 6);

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public CannedSourceWireMockSampleTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    [Fact]
    public async Task Sample_ingest_consumes_prices_ok_through_the_harness()
    {
        var set = FixtureUniverse.Build(FixtureAnchor.L2);
        await using (var seedDb = _postgres.CreateContext())
        {
            await FixtureSeeder.ApplyAsync(seedDb, set);
        }

        var payload = _wireMock.StubCannedSource(set, CannedSourceCatalog.PricesOk);

        using var client = new HttpClient();
        using var response = await client.GetAsync($"{_wireMock.BaseUrl}{payload.DefaultPath}");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(body);
        var items = doc.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(16, items.Count);

        await using var db = _postgres.CreateContext();
        var instrumentIds = await db.Instruments.ToDictionaryAsync(i => i.Symbol, i => i.Id);
        foreach (var item in items)
        {
            var symbol = item.GetProperty("symbol").GetString()!;
            var stored = await db.DailyPrices.SingleAsync(p => p.InstrumentId == instrumentIds[symbol] && p.PriceDate == AnchorT);
            Assert.Equal(stored.CloseRaw, item.GetProperty("close").GetDecimal());
            Assert.Equal(stored.Volume, item.GetProperty("volume").GetInt64());
        }

        Assert.Equal(1, _wireMock.RequestsFor(payload.DefaultPath));
    }

    [Fact]
    public async Task Helper_stubs_a_named_payload_at_an_overridden_route_and_reproduces_the_error_class()
    {
        var set = FixtureUniverse.Build(FixtureAnchor.L2);

        _wireMock.StubCannedSource(set, CannedSourceCatalog.PricesSourceDown, path: "/isbank/prices/eod", method: "GET");

        using var client = new HttpClient();
        using var response = await client.GetAsync($"{_wireMock.BaseUrl}/isbank/prices/eod");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, _wireMock.RequestsFor("/isbank/prices/eod"));
    }
}
