using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Ingestion;
using Degerli.Ingestion.Kap;
using Degerli.Ingestion.Statements;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-foundation-032 acceptance — the KAP VYK gateway's base prefix (<c>/api/vyk</c>) is
/// preserved by the client's base+path join (01 v1.5 §10.9.11). Before the fix the shipped
/// configuration (base <b>without</b> a trailing slash, every path option <b>with</b> a leading
/// slash) made .NET's URI resolution treat the path as <b>root-relative</b>, so every request
/// dropped the <c>/api/vyk</c> prefix and the real gateway answered 404. CI could not catch it:
/// the L2 doubles bind at the server root, where root-relative and base-relative resolution
/// coincide. These doubles therefore bind under the <b>path prefix</b>, which is exactly the
/// regression class this suite exists to hold.
/// </summary>
public sealed class KapUriResolutionTests : IClassFixture<WireMockFixture>
{
    private const string GatewayPrefix = "/api/vyk";

    private const string MembersBody =
        """[{"id":"1001","title":"ALFA","stockCode":"ALFA","memberType":"stock"}]""";

    private const string AnchorBody = """{"lastDisclosureIndex":"1001"}""";

    private const string DisclosuresBody =
        """[{"disclosureIndex":"1001","disclosureType":"FR","disclosureClass":"FR","title":"Finansal Rapor","companyId":"1001","subReportIds":[],"acceptedDataFileTypes":["data"]}]""";

    /// <summary>A minimal shape-faithful VYK disclosure detail (the live-verified
    /// <c>presentation</c> shape, 01 v1.5 §10.9.11) carrying one IS <c>REV</c> line.</summary>
    private const string DetailBody = """
        {
          "disclosureIndex": "1001",
          "year": "2025",
          "period": { "tr": "Yıllık", "en": "Annual" },
          "consolidation": "NC",
          "relatedStocks": [ { "code": "ALFA" } ],
          "presentation": [
            {
              "id": "general_role_IS",
              "content": {
                "id": "general_role_IS",
                "isMultiDimensional": "yes",
                "ContextList": {
                  "Context": [
                    { "id": "2025-01-012025-12-31", "key": "CURR", "Period": { "startDate": "2025-01-01", "endDate": "2025-12-31" } }
                  ]
                },
                "ReportItem": {
                  "name": "StatementOfProfitOrLossAbstract",
                  "abstract": "true",
                  "ReportItem": [
                    {
                      "name": "Revenue",
                      "Values": { "Value": [ { "contextId": "2025-01-012025-12-31", "currency": "TRY", "rounding": "INF", "value": "1.234.567,89" } ] }
                    }
                  ]
                }
              }
            }
          ],
          "attachmentUrls": []
        }
        """;

    private readonly WireMockFixture _wireMock;

    public KapUriResolutionTests(WireMockFixture wireMock) => _wireMock = wireMock;

    // ---------------------------------------------------------------------------------
    // (i) Prefix-preservation regression — the shipped spellings against a prefixed double
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// TKT-foundation-032 (criterion (i)) — with <b>exactly the shipped</b>
    /// <c>appsettings.json</c> spellings (base <c>…/api/vyk</c> with no trailing slash, every
    /// path option leading-slash), every configured route resolves <b>under the prefix</b>: the
    /// client's fetch succeeds because the request lands on the prefixed route, never on the
    /// bare host. Red before the fix (HTTP 404 → <see cref="SourceFetchException"/>).
    /// </summary>
    [Fact]
    public async Task TKT_foundation_032_shipped_base_and_paths_preserve_the_api_vyk_prefix()
    {
        var shipped = ShippedRoutes.FromAppSettings();

        // The defect precondition is the shipped configuration itself: a base with no trailing
        // slash plus paths that carry a leading slash.
        Assert.False(shipped.Base.EndsWith("/", StringComparison.Ordinal), $"shipped base '{shipped.Base}' has a trailing slash");
        Assert.StartsWith(GatewayPrefix, new Uri(shipped.Base).AbsolutePath, StringComparison.Ordinal);
        Assert.StartsWith("/", shipped.Members, StringComparison.Ordinal);
        Assert.StartsWith("/", shipped.Anchor, StringComparison.Ordinal);
        Assert.StartsWith("/", shipped.Disclosures, StringComparison.Ordinal);
        Assert.StartsWith("/", shipped.Detail, StringComparison.Ordinal);

        StubPrefixedGateway(shipped);
        _wireMock.Server.ResetLogEntries();

        // The double is bound under the prefix; the base is configured exactly as shipped
        // (no trailing slash) and only the host is retargeted at the double.
        using var host = BuildIngestion(_wireMock.BaseUrl + GatewayPrefix, shipped);
        using var scope = host.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IKapSourceClient>();

        var members = await client.GetStringAsync(shipped.Members);
        var anchor = await client.GetStringAsync(shipped.Anchor);
        var disclosures = await client.GetStringAsync(shipped.Disclosures);
        var detail = await client.GetStringAsync(shipped.Detail.Replace("{disclosureIndex}", "1001", StringComparison.Ordinal));

        Assert.Contains("ALFA", members, StringComparison.Ordinal);
        Assert.Contains("lastDisclosureIndex", anchor, StringComparison.Ordinal);
        Assert.Contains("disclosureIndex", disclosures, StringComparison.Ordinal);
        Assert.Contains("presentation", detail, StringComparison.Ordinal);

        Assert.Equal(
            new[]
            {
                "/api/vyk/members",
                "/api/vyk/lastDisclosureIndex",
                "/api/vyk/disclosures",
                "/api/vyk/disclosureDetail/1001",
            },
            RequestPaths().ToArray());
    }

    /// <summary>
    /// TKT-foundation-032 (criterion (i), robustness matrix) — the code is robust to both
    /// spellings on both halves: base with/without a trailing slash × path with/without a
    /// leading slash all resolve to the same prefixed URL. Only the prefixed route is ever hit.
    /// </summary>
    [Theory]
    [InlineData("", "/members")]
    [InlineData("", "members")]
    [InlineData("/", "/members")]
    [InlineData("/", "members")]
    public async Task TKT_foundation_032_base_and_path_spellings_all_resolve_under_the_prefix(
        string baseSuffix,
        string path)
    {
        var shipped = ShippedRoutes.FromAppSettings();
        StubPrefixedGateway(shipped);
        _wireMock.Server.ResetLogEntries();

        using var host = BuildIngestion(_wireMock.BaseUrl + GatewayPrefix + baseSuffix, shipped);
        using var scope = host.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IKapSourceClient>();

        var body = await client.GetStringAsync(path);

        Assert.Contains("ALFA", body, StringComparison.Ordinal);
        Assert.Equal(new[] { "/api/vyk/members" }, RequestPaths().ToArray());
    }

    /// <summary>
    /// TKT-foundation-032 (criterion (i)) — the whole VYK company-scoped fetch
    /// (<c>/members</c> → <c>/lastDisclosureIndex</c> → <c>/disclosures</c> →
    /// <c>/disclosureDetail/{disclosureIndex}</c>) resolves under the configured prefix, so
    /// every existing path option is covered, the detail included. The decoded payload keeps
    /// its provenance and the cursor.
    /// </summary>
    [Fact]
    public async Task TKT_foundation_032_vyk_company_fetch_resolves_every_route_under_the_prefix()
    {
        var shipped = ShippedRoutes.FromAppSettings();
        StubPrefixedGateway(shipped);
        _wireMock.Server.ResetLogEntries();

        using var host = BuildIngestion(_wireMock.BaseUrl + GatewayPrefix, shipped);
        using var scope = host.CreateScope();
        var adapter = scope.ServiceProvider.GetRequiredService<StatementsSourceAdapter>();

        Assert.True(adapter.VykEnabled);
        var fetched = await adapter.FetchCompanyAsync("ALFA", fromDisclosureIndex: "0");

        Assert.Equal("kap", fetched.Source.SourceRef);
        Assert.Equal("ALFA", fetched.Source.Payload.Symbol);
        Assert.Equal("1001", fetched.Cursor);
        var statement = Assert.Single(fetched.Source.Payload.Statements);
        Assert.Equal(1_234_567.89m, statement.Lines["REV"]);

        var paths = RequestPaths();
        Assert.Equal("/api/vyk/members", paths[0]);
        Assert.Equal("/api/vyk/lastDisclosureIndex", paths[1]);
        Assert.StartsWith("/api/vyk/disclosures", paths[2], StringComparison.Ordinal);
        Assert.StartsWith("/api/vyk/disclosureDetail/1001", paths[3], StringComparison.Ordinal);
        Assert.Equal(4, paths.Count);
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    /// <summary>Binds the VYK routes <b>under the gateway path prefix</b> — the binding the
    /// root-bound L2 doubles lack, and the reason this defect class reached main.</summary>
    private void StubPrefixedGateway(ShippedRoutes routes)
    {
        _wireMock.Server.ResetMappings();
        _wireMock.StubJson(GatewayPrefix + routes.Members, MembersBody);
        _wireMock.StubJson(GatewayPrefix + routes.Anchor, AnchorBody);
        _wireMock.StubJson(GatewayPrefix + routes.Disclosures, DisclosuresBody);
        _wireMock.StubJson(GatewayPrefix + "/disclosureDetail/1001", DetailBody);
    }

    private static ServiceProvider BuildIngestion(string kapBaseUrl, ShippedRoutes routes)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Ingestion:Kap:BaseUrl"] = kapBaseUrl,
                ["Ingestion:Kap:MembersPath"] = routes.Members,
                ["Ingestion:Kap:LastDisclosureIndexPath"] = routes.Anchor,
                ["Ingestion:Statements:Path"] = routes.Disclosures,
                ["Ingestion:Statements:DetailPath"] = routes.Detail,
                ["Ingestion:Statements:DisclosureClass"] = "FR",
                ["Ingestion:Statements:EarlyDisclosureIndex"] = "0",
                ["Ingestion:Statements:RequestDelayMs"] = "0",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMarketDataIngestion(configuration);
        return services.BuildServiceProvider();
    }

    private List<string> RequestPaths() =>
        _wireMock.Server.LogEntries
            .Where(e => e.RequestMessage is not null)
            .OrderBy(e => e.RequestMessage!.DateTime)
            .Select(e => e.RequestMessage!.Path)
            .ToList();

    /// <summary>The shipped <c>Ingestion:Kap</c> / <c>Ingestion:Statements</c> route spellings,
    /// read from the real <c>appsettings.json</c> so the regression tracks production config
    /// (a config-value "fix" would turn these tests red, which is the required posture).</summary>
    private sealed record ShippedRoutes(string Base, string Members, string Anchor, string Disclosures, string Detail)
    {
        public static ShippedRoutes FromAppSettings()
        {
            var path = Path.Combine(FindRepositoryRoot(), "src", "Api", "appsettings.json");
            Assert.True(File.Exists(path), $"appsettings.json missing at {path}");

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var ingestion = document.RootElement.GetProperty("Ingestion");
            var kap = ingestion.GetProperty("Kap");
            var statements = ingestion.GetProperty("Statements");

            return new ShippedRoutes(
                kap.GetProperty("BaseUrl").GetString()!,
                kap.GetProperty("MembersPath").GetString()!,
                kap.GetProperty("LastDisclosureIndexPath").GetString()!,
                statements.GetProperty("Path").GetString()!,
                statements.GetProperty("DetailPath").GetString()!);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Degerli.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root (Degerli.sln).");
    }
}
