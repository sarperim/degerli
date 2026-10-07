using System.Net;
using System.Text.Json;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// Unhandled exceptions must surface as RFC 7807 ProblemDetails with a stable
/// <c>INTERNAL</c> code and a support <c>traceId</c> — never a stack trace or raw
/// exception text (`01` §10.2, `03` §7).
/// </summary>
public sealed class InternalErrorTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public InternalErrorTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Unhandled_exception_returns_internal_problem_details()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/_probe/throw");
        request.Headers.Add("X-Forwarded-For", "198.51.100.11");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var payload = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        Assert.Equal("INTERNAL", root.GetProperty("code").GetString());
        Assert.Equal(500, root.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));

        Assert.DoesNotContain("StackTrace", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("   at ", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InvalidOperationException", payload, StringComparison.OrdinalIgnoreCase);
    }
}
