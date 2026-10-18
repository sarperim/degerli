using System.Net;
using System.Text.Json;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-foundation-029 — <c>GET /api/v1/meta/data-sources</c>, the site-wide
/// data-source provenance disclosure ("where the data comes from"). It is
/// anonymous, reads a small static server-side catalog, needs no database, and
/// reports the honest liveness of each data class (<c>live</c> / <c>unavailable</c>)
/// — a class with no live values is never presented as a live source
/// (<c>01</c> §10.7 honest-state posture; <c>03</c> §3).
/// </summary>
public sealed class DataSourcesEndpointsTests
{
    private static readonly string[] ExpectedClasses =
        ["prices", "indexLevels", "universe", "statements", "macro"];

    [Fact]
    public async Task Get_data_sources_is_anonymous_200_with_the_per_class_catalog()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/meta/data-sources");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var sources = body.RootElement.GetProperty("sources").EnumerateArray().ToList();

        // Every disclosed class is present exactly once, in catalog order.
        Assert.Equal(ExpectedClasses, sources.Select(entry => entry.GetProperty("class").GetString()));

        // Each entry carries the honest catalog shape: id, class, source, state, nature.
        foreach (var entry in sources)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("id").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("source").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("nature").GetString()));

            var state = entry.GetProperty("state").GetString();
            Assert.Contains(state, new[] { "live", "unavailable" });
        }
    }

    [Fact]
    public async Task Live_classes_name_the_real_free_public_sources()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/meta/data-sources");
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var byClass = body.RootElement.GetProperty("sources").EnumerateArray()
            .ToDictionary(entry => entry.GetProperty("class").GetString()!, entry => entry);

        // İş Yatırım public EOD endpoints cover prices / index levels / universe.
        foreach (var code in new[] { "prices", "indexLevels", "universe" })
        {
            Assert.Equal("live", byClass[code].GetProperty("state").GetString());
            Assert.Equal("İş Yatırım", byClass[code].GetProperty("source").GetString());
        }

        // Macro series name TCMB EVDS (the pending live activation is honestly unavailable).
        Assert.Equal("TCMB EVDS", byClass["macro"].GetProperty("source").GetString());
    }

    [Fact]
    public async Task Unavailable_classes_reflect_current_liveness_without_fabrication()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/meta/data-sources");
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var byClass = body.RootElement.GetProperty("sources").EnumerateArray()
            .ToDictionary(entry => entry.GetProperty("class").GetString()!, entry => entry);

        // Statements (KAP) and macro (EVDS) have no live values yet: they are reported
        // honestly unavailable, never claimed as a live source (01 §10.7, no fabrication).
        Assert.Equal("unavailable", byClass["statements"].GetProperty("state").GetString());
        Assert.Equal("unavailable", byClass["macro"].GetProperty("state").GetString());

        // Every disclosed class states its EOD / as-of nature (not real-time).
        foreach (var entry in byClass.Values)
        {
            Assert.Equal("eod", entry.GetProperty("nature").GetString());
        }
    }
}
