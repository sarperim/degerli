using System.Net;
using System.Text.Json;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TC-XC-018 — the OpenAPI document is served anonymously under /api/v1 and covers
/// the versioned surface. Traces: `03` header (OpenAPI anonymous).
/// </summary>
public sealed class Xc018OpenApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public Xc018OpenApiTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task OpenApi_document_is_served_anonymously()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/openapi.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("openapi", out var version));
        Assert.False(string.IsNullOrWhiteSpace(version.GetString()));
        Assert.True(root.TryGetProperty("paths", out var paths));
        Assert.Equal(JsonValueKind.Object, paths.ValueKind);
    }

    [Fact]
    public async Task OpenApi_document_exposes_no_fund_facing_public_routes()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/openapi.json");
        var payload = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(payload);

        var pathNames = document.RootElement
            .GetProperty("paths")
            .EnumerateObject()
            .Select(path => path.Name)
            .ToArray();

        Assert.DoesNotContain(pathNames, path => path.Contains("fund", StringComparison.OrdinalIgnoreCase));
    }
}
