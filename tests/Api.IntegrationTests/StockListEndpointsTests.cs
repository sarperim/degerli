using System.Net;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;
using Degerli.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-res-002 acceptance — the stocks module read surface (Group A of
/// <c>.pipeline/testing/stock-research.md</c>, TC-RES-001..004):
/// <list type="bullet">
/// <item><c>GET /api/v1/stocks?sector=&amp;q=</c> — the current-universe list
/// (symbol, name, sector identity, listingDate; FR-RES-001..003, UC-RES-001).</item>
/// <item><c>GET /api/v1/stocks/search?q=</c> — the lightweight header typeahead
/// (symbol + name only; FR-RES-004).</item>
/// </list>
/// Each test runs against the real, migrated Testcontainers PostgreSQL seeded with
/// the L2 fixture universe (16 instruments; sector A holds five). The sector field is
/// asserted as an <em>identity</em>, not a serialization shape (I-RES-4): UNSEC's
/// unclassified state is asserted as absent/null and never guessed.
/// </summary>
public sealed class StockListEndpointsTests : IClassFixture<PostgresFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);

    /// <summary>The 16 fixture-universe symbols (FU §3).</summary>
    private static readonly string[] AllSymbols =
    [
        "ALFA", "BETA", "GAMA", "DELTA", "REST", "EPSL", "ZETA", "ETA",
        "THETA", "PART", "NEWP", "IOTA", "KAPPA", "LAMDA", "NU", "UNSEC",
    ];

    /// <summary>The five SEC-A (Teknoloji/Technology) instruments.</summary>
    private static readonly string[] SectorASymbols = ["ALFA", "BETA", "GAMA", "DELTA", "REST"];

    private readonly PostgresFixture _postgres;

    public StockListEndpointsTests(PostgresFixture postgres) => _postgres = postgres;

    // -- TC-RES-001 — Universe list --------------------------------------------

    [Fact]
    public async Task TC_RES_001_list_returns_all_16_with_symbol_name_sector_identity_and_listing_date()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/stocks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Anonymous research surface (BR-RES-001): no session is established.
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.True(response.Headers.CacheControl?.Public);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        // Honest-data envelope (`03` §1.2): the current-universe read carries asOf/stale.
        Assert.Equal(L2.Anchor.T.ToString("yyyy-MM-dd"), root.GetProperty("asOf").GetString());
        Assert.False(root.GetProperty("stale").GetBoolean());

        var stocks = Stocks(root);
        Assert.Equal(AllSymbols.Length, stocks.Count);
        Assert.Equal(
            AllSymbols.OrderBy(symbol => symbol, StringComparer.Ordinal),
            stocks.Select(row => row.GetProperty("symbol").GetString()!).OrderBy(symbol => symbol, StringComparer.Ordinal));

        // ALFA — classification asserted as identity (code "A"), not shape (I-RES-4).
        var alfa = Row(stocks, "ALFA");
        Assert.Equal("Alfa Teknoloji A.Ş.", alfa.GetProperty("name").GetString());
        Assert.Equal("A", alfa.GetProperty("sector").GetString());
        Assert.Equal(new DateOnly(2010, 1, 4).ToString("yyyy-MM-dd"), alfa.GetProperty("listingDate").GetString());

        // UNSEC — unclassified is served explicitly as null, never guessed.
        var unsec = Row(stocks, "UNSEC");
        Assert.Equal(JsonValueKind.Null, unsec.GetProperty("sector").ValueKind);
        Assert.Equal(new DateOnly(2011, 7, 19).ToString("yyyy-MM-dd"), unsec.GetProperty("listingDate").GetString());
    }

    // -- TC-RES-002 — Sector filter --------------------------------------------

    [Fact]
    public async Task TC_RES_002_sector_filter_returns_only_that_sector_and_unknown_sector_is_empty_200()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var filtered = await client.GetAsync("/api/v1/stocks?sector=A");
        Assert.Equal(HttpStatusCode.OK, filtered.StatusCode);
        using var filteredDocument = JsonDocument.Parse(await filtered.Content.ReadAsStringAsync());
        var filteredStocks = Stocks(filteredDocument.RootElement);

        Assert.Equal(SectorASymbols.Length, filteredStocks.Count);
        Assert.Equal(
            SectorASymbols.OrderBy(symbol => symbol, StringComparer.Ordinal),
            filteredStocks.Select(row => row.GetProperty("symbol").GetString()!).OrderBy(symbol => symbol, StringComparer.Ordinal));
        Assert.All(filteredStocks, row => Assert.Equal("A", row.GetProperty("sector").GetString()));

        // An unknown sector is a well-formed query with no matches — data, not an error.
        using var unknown = await client.GetAsync("/api/v1/stocks?sector=ZZZ");
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        using var unknownDocument = JsonDocument.Parse(await unknown.Content.ReadAsStringAsync());
        Assert.Empty(Stocks(unknownDocument.RootElement));
    }

    // -- TC-RES-003 — Search by name/code --------------------------------------

    [Fact]
    public async Task TC_RES_003_search_matches_name_and_symbol_case_insensitively_and_miss_is_empty_200()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        // Case-insensitive ILIKE on symbol.
        using var bySymbol = await client.GetAsync("/api/v1/stocks?q=alfa");
        Assert.Equal(HttpStatusCode.OK, bySymbol.StatusCode);
        using var bySymbolDocument = JsonDocument.Parse(await bySymbol.Content.ReadAsStringAsync());
        var symbolMatches = Stocks(bySymbolDocument.RootElement);
        Assert.Single(symbolMatches);
        Assert.Equal("ALFA", symbolMatches[0].GetProperty("symbol").GetString());

        // Case-insensitive ILIKE on the (long) name.
        using var byName = await client.GetAsync("/api/v1/stocks?q=" + Uri.EscapeDataString("Alfa Teknoloji"));
        Assert.Equal(HttpStatusCode.OK, byName.StatusCode);
        using var byNameDocument = JsonDocument.Parse(await byName.Content.ReadAsStringAsync());
        var nameMatches = Stocks(byNameDocument.RootElement);
        Assert.Single(nameMatches);
        Assert.Equal("ALFA", nameMatches[0].GetProperty("symbol").GetString());

        // A non-matching query is a normal empty result.
        using var miss = await client.GetAsync("/api/v1/stocks?q=zzz");
        Assert.Equal(HttpStatusCode.OK, miss.StatusCode);
        using var missDocument = JsonDocument.Parse(await miss.Content.ReadAsStringAsync());
        Assert.Empty(Stocks(missDocument.RootElement));
    }

    [Fact]
    public async Task TC_RES_003_sector_and_query_apply_conjunctively()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        // ALFA is in sector A — the combined query matches it once.
        using var both = await client.GetAsync("/api/v1/stocks?sector=A&q=alfa");
        Assert.Equal(HttpStatusCode.OK, both.StatusCode);
        using var bothDocument = JsonDocument.Parse(await both.Content.ReadAsStringAsync());
        var bothStocks = Stocks(bothDocument.RootElement);
        Assert.Single(bothStocks);
        Assert.Equal("ALFA", bothStocks[0].GetProperty("symbol").GetString());

        // ALFA is not in sector B — the conjunction therefore matches nothing.
        using var conflicting = await client.GetAsync("/api/v1/stocks?sector=B&q=alfa");
        Assert.Equal(HttpStatusCode.OK, conflicting.StatusCode);
        using var conflictingDocument = JsonDocument.Parse(await conflicting.Content.ReadAsStringAsync());
        Assert.Empty(Stocks(conflictingDocument.RootElement));
    }

    // -- TC-RES-004 — Header search endpoint -----------------------------------

    [Fact]
    public async Task TC_RES_004_search_endpoint_returns_lightweight_symbol_name_matches_only()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/stocks/search?q=alfa");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var matches = Stocks(document.RootElement);

        Assert.Single(matches);
        var alfa = matches[0];
        Assert.Equal("ALFA", alfa.GetProperty("symbol").GetString());
        Assert.Equal("Alfa Teknoloji A.Ş.", alfa.GetProperty("name").GetString());

        // Lightweight: no sector/listingDate (only the two typeahead fields).
        var properties = alfa.EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(new[] { "symbol", "name" }, properties);

        using var miss = await client.GetAsync("/api/v1/stocks/search?q=zzz");
        Assert.Equal(HttpStatusCode.OK, miss.StatusCode);
        using var missDocument = JsonDocument.Parse(await miss.Content.ReadAsStringAsync());
        Assert.Empty(Stocks(missDocument.RootElement));
    }

    // ---------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------

    private async Task SeedAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
    }

    private static HttpClient CreateClient(DegerliAppFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    private static List<JsonElement> Stocks(JsonElement root)
        => root.GetProperty("stocks").EnumerateArray().ToList();

    private static JsonElement Row(List<JsonElement> stocks, string symbol)
        => stocks.Single(row => row.GetProperty("symbol").GetString() == symbol);
}
