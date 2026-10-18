using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Macro;
using Degerli.Ingestion.Sources;
using Degerli.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Serilog;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-foundation-027 acceptance — the migrated <b>TCMB EVDS 3.0</b> macro source
/// (01 v1.5.6 §10.9.12, FR-MOV-014). These are adapter-contract tests: they drive the real
/// <see cref="MacroSourceAdapter"/> through the <see cref="ISourceClient"/> seam with a fake
/// client and assert the v3 transport it builds — the anonymous catalog/serieList discovery,
/// the <c>POST /fe</c> body shape, the five official series mapped onto the discovered
/// <c>bie_…</c> datagroups, and how the v3 envelope decodes back to the canonical macro
/// series. The canonical <c>sourceRef</c>/<c>series</c> double (the L2 golden contract,
/// §10.9.7) still parses through the same adapter unchanged.
/// </summary>
public sealed class MacroEvdsAdapterTests
{
    private static readonly DateOnly Date = new(2026, 10, 6);

    private const string CatalogPath = "/igmevdsms-dis/categories/withDatagroups/type=json";
    private const string SerieListPath = "/igmevdsms-dis/serieList/fe/type=json&code={code}";
    private const string ValuesPath = "/igmevdsms-dis/fe";

    // ---------------------------------------------------------------------------------
    // (ii) v3 fetch-shape — discovery GETs + the /fe POST body
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// TKT_foundation_027 (criterion (ii), TC-MOV-026 shape) — the adapter resolves the
    /// official series from the anonymous catalog + serieList and constructs the v3
    /// <c>POST /fe</c> body verbatim from the live-captured shape: <c>series</c>/
    /// <c>aggregationTypes</c>/<c>formulas</c> hyphen-joined per series, <c>frequency:"2"</c>
    /// (business day), <c>dateFormat</c>/<c>yon</c>/<c>sira:"0"</c>, <c>ozelFormuller:[]</c>,
    /// and the bounded 90-day value-date window (DR-2). No credential travels (anonymous).
    /// </summary>
    [Fact]
    public async Task TKT_foundation_027_Resolves_series_and_builds_the_v3_fe_request()
    {
        var client = new V3FakeClient();
        var adapter = BuildV3(client);

        await adapter.FetchAsync(Date);

        Assert.Equal(MacroSourceAdapter.EvdsSource, adapter.SourceName);

        // Discovery: the catalog, then one serieList per requested datagroup (anonymous GETs).
        Assert.Contains(CatalogPath, client.Gets);
        Assert.Contains(client.Gets, p => p.Contains("bie_dkdovytl", StringComparison.Ordinal));
        Assert.Contains(client.Gets, p => p.Contains("bie_mkaltytl", StringComparison.Ordinal));
        Assert.Contains(client.Gets, p => p.Contains("bie_bispolfaiz", StringComparison.Ordinal));
        Assert.Contains(client.Gets, p => p.Contains("bie_tukfiy2025", StringComparison.Ordinal));

        // Two value requests: the FX/gold/repo group and the CPI index.
        Assert.Equal(2, client.Posts.Count);
        var daily = client.Posts.Single(p => p.Path == ValuesPath && p.Body.Contains("TP.DK.USD.S.YTL", StringComparison.Ordinal));
        Assert.Equal(ValuesPath, daily.Path);

        using var body = JsonDocument.Parse(daily.Body);
        var root = body.RootElement;
        Assert.Equal("json", root.GetProperty("type").GetString());
        Assert.Equal(
            "TP.DK.USD.S.YTL-TP.DK.EUR.S.YTL-TP.ALTINPIYASA.KAP03-TP.BISPOLFAIZ.TUR",
            root.GetProperty("series").GetString());
        // Aggregation methods are resolved from each datagroup's serieList DEFAULT_AGG_METHOD.
        Assert.Equal("avg-avg-last-last", root.GetProperty("aggregationTypes").GetString());
        Assert.Equal("0-0-0-0", root.GetProperty("formulas").GetString());
        Assert.Equal("2", root.GetProperty("frequency").GetString());
        Assert.Equal("0", root.GetProperty("dateFormat").GetString());
        Assert.Equal("0", root.GetProperty("yon").GetString());
        Assert.Equal("0", root.GetProperty("sira").GetString());
        Assert.Equal("tr", root.GetProperty("lang").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("ozelFormuller").ValueKind);
        Assert.Empty(root.GetProperty("ozelFormuller").EnumerateArray());
        Assert.True(root.GetProperty("groupSeperator").GetBoolean());
        Assert.False(root.GetProperty("isRaporSayfasi").GetBoolean());

        // Bounded value-date lookback (DR-2), not a single-day window.
        Assert.Equal("06-10-2026", root.GetProperty("endDate").GetString());
        Assert.Equal("08-07-2026", root.GetProperty("startDate").GetString());

        // The CPI index is requested separately over a year-plus window (YoY derivation).
        var cpi = client.Posts.Single(p => p.Body.Contains("TP.TUKFIY2025.GENEL", StringComparison.Ordinal));
        using var cpiBody = JsonDocument.Parse(cpi.Body);
        Assert.Equal("TP.TUKFIY2025.GENEL", cpiBody.RootElement.GetProperty("series").GetString());
        Assert.Equal("avg", cpiBody.RootElement.GetProperty("aggregationTypes").GetString());
        Assert.Equal("06-10-2026", cpiBody.RootElement.GetProperty("endDate").GetString());
        Assert.Equal("01-09-2025", cpiBody.RootElement.GetProperty("startDate").GetString());

        // No credential ever travels in either request (the endpoint is anonymous).
        Assert.All(client.Gets, p => Assert.DoesNotContain("key=", p, StringComparison.Ordinal));
        Assert.All(client.Posts, p => Assert.DoesNotContain("key", p.Body, StringComparison.Ordinal));
    }

    /// <summary>
    /// TKT_foundation_027 (criterion (iii)) — the v3 envelope parses through the production
    /// adapter: every underscored column key resolves to its canonical macro code, the
    /// thousands-grouping commas are stripped, the CPI index is converted to the canonical
    /// YoY, and a column naming no known series is dropped.
    /// </summary>
    [Fact]
    public async Task TKT_foundation_027_Decodes_the_v3_envelope_to_canonical_series()
    {
        var client = new V3FakeClient();
        var payload = (await BuildV3(client).FetchAsync(Date)).Payload;

        var codes = payload.Series!.Select(s => s.Code).Distinct().ToList();
        Assert.Contains(MacroSeries.UsdTry, codes);
        Assert.Contains(MacroSeries.EurTry, codes);
        Assert.Contains(MacroSeries.Gold, codes);
        Assert.Contains(MacroSeries.CbrtRepo, codes);
        Assert.Contains(MacroSeries.TuikCpi, codes);

        var usd = Assert.Single(payload.Series!, s => s.Code == MacroSeries.UsdTry);
        Assert.Equal(48.10m, usd.Value);
        Assert.Equal(Date, usd.ValueDate);

        // Gold carries a thousands-grouping comma in the payload — stripped before parse.
        var gold = Assert.Single(payload.Series!, s => s.Code == MacroSeries.Gold);
        Assert.Equal(4200.00m, gold.Value);

        // The monthly policy rate resolves to the month's first day.
        var repo = Assert.Single(payload.Series!, s => s.Code == MacroSeries.CbrtRepo);
        Assert.Equal(40.50m, repo.Value);
        Assert.Equal(new DateOnly(2026, 9, 1), repo.ValueDate);

        // The CPI index is converted to the canonical YoY measure.
        var cpi = Assert.Single(payload.Series!, s => s.Code == MacroSeries.TuikCpi);
        Assert.Equal(new DateOnly(2026, 9, 1), cpi.ValueDate);
        Assert.Equal((137.23m / 105.78m - 1m) * 100m, cpi.Value);
    }

    /// <summary>
    /// TKT_foundation_027 (criterion (iii)) — the adapter stamps the concrete EVDS source
    /// (<c>evds</c>) on the real v3 payload (there is no provenance field on the wire), and a
    /// malformed body surfaces as an invalid (payload) failure so the job can quarantine it.
    /// </summary>
    [Fact]
    public async Task TKT_foundation_027_Stamps_evds_provenance_and_quarantines_malformed_bodies()
    {
        var client = new V3FakeClient();
        var fetched = await BuildV3(client).FetchAsync(Date);
        Assert.Equal(MacroSourceAdapter.EvdsSource, fetched.SourceRef);

        var malformed = new V3FakeClient(values: "{ not valid json");
        await Assert.ThrowsAsync<SourcePayloadException>(() => BuildV3(malformed).FetchAsync(Date));
    }

    /// <summary>
    /// TKT_foundation_027 (criterion (ii)) — a datagroup that disappeared from the live
    /// catalog is <b>skipped</b>, never guessed: its series is simply absent from the request
    /// and the job marks it LATE (honest unavailable state).
    /// </summary>
    [Fact]
    public async Task TKT_foundation_027_A_missing_datagroup_is_skipped_never_guessed()
    {
        var client = new V3FakeClient(catalog: "[{\"CATEGORY_ID\":1,\"DATAGROUPS\":[]}]");
        var payload = (await BuildV3(client).FetchAsync(Date)).Payload;

        Assert.Empty(payload.Series!);
        Assert.Empty(client.Posts); // nothing to request
    }

    /// <summary>
    /// TKT_foundation_027 (§10.9.7) — the canonical <c>sourceRef</c>/<c>series</c> double (the
    /// L2 golden shape) still parses through the same production adapter in the legacy
    /// single-GET lane, unchanged: the second leg of "the fixture world keeps its codes".
    /// </summary>
    [Fact]
    public async Task TKT_foundation_027_Canonical_double_still_parses_unchanged()
    {
        var client = new RecordingSourceClient(OfficialEnvelope());
        var adapter = new MacroSourceAdapter(
            client,
            Options.Create(new MacroSourceOptions { Path = "/service/evds", EvdsApiKey = "k" }));

        var payload = (await adapter.FetchAsync(Date)).Payload;

        Assert.Equal("evds://tcmb", (await adapter.FetchAsync(Date)).SourceRef);
        var codes = payload.Series!.Select(s => s.Code).ToList();
        Assert.Contains(MacroSeries.UsdTry, codes);
        Assert.Contains(MacroSeries.CbrtRepo, codes);
        Assert.Contains(MacroSeries.Gold, codes);
        Assert.Equal(47.10m, Assert.Single(payload.Series!, s => s.Code == MacroSeries.UsdTry).Value);
        Assert.DoesNotContain(payload.Series!, s => s.Code == "TP.UNKNOWN.SERIES");
    }

    // ---------------------------------------------------------------------------------
    // (i) config wiring + redaction posture
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// TKT_foundation_027 (criterion (i), the TC-MDF-075 / TKT-foundation-026 precedent) — the
    /// shipped <c>Ingestion:Macro</c> section carries the live-verified v3 BaseUrl, the v3
    /// values route, the additive catalog/serieList discovery routes with a <c>{code}</c>
    /// template, and an empty <c>EvdsApiKey</c> default; the code-level defaults keep discovery
    /// <b>off</b> (empty paths) so the legacy canonical-double lane is the default.
    /// </summary>
    [Fact]
    public void TKT_foundation_027_config_wiring_ships_the_v3_platform()
    {
        var appsettingsPath = Path.Combine(FindRepositoryRoot(), "src", "Api", "appsettings.json");
        Assert.True(File.Exists(appsettingsPath), $"appsettings.json missing at {appsettingsPath}");
        using var document = JsonDocument.Parse(File.ReadAllText(appsettingsPath));
        var macro = document.RootElement.GetProperty("Ingestion").GetProperty("Macro");

        Assert.Equal("https://evds3.tcmb.gov.tr", macro.GetProperty("BaseUrl").GetString());
        Assert.Equal("/igmevdsms-dis/fe", macro.GetProperty("Path").GetString());
        Assert.Equal("/igmevdsms-dis/categories/withDatagroups/type=json", macro.GetProperty("CatalogPath").GetString());
        Assert.Equal("/igmevdsms-dis/serieList/fe/type=json&code={code}", macro.GetProperty("SerieListPath").GetString());
        Assert.Equal(string.Empty, macro.GetProperty("EvdsApiKey").GetString());

        // Code defaults: discovery off (the L2 golden lane), the v3 values route as a hint.
        var defaults = new MacroSourceOptions();
        Assert.False(defaults.UsesV3Discovery);
        Assert.Equal("/igmevdsms-dis/fe", defaults.Path);
        Assert.Equal(string.Empty, defaults.EvdsApiKey);

        // The shipped section enables discovery.
        var shipped = new MacroSourceOptions
        {
            CatalogPath = macro.GetProperty("CatalogPath").GetString() ?? string.Empty,
            SerieListPath = macro.GetProperty("SerieListPath").GetString() ?? string.Empty,
        };
        Assert.True(shipped.UsesV3Discovery);
        Assert.Equal(
            "/igmevdsms-dis/serieList/fe/type=json&code=bie_dkdovytl",
            shipped.SerieListRoute("bie_dkdovytl"));
    }

    /// <summary>
    /// TKT_foundation_027 (criterion (i), TC-MOV-028 precedent / SEC-1) — even with a key
    /// configured, the v3 request stays anonymous (the key is never placed on the wire), and a
    /// configured key never survives in an adapter failure message (raw or URL-encoded).
    /// </summary>
    [Fact]
    public async Task TKT_foundation_027_Key_never_travels_and_is_redacted_from_failures()
    {
        const string key = "top-secret-evds-key";
        var client = new V3FakeClient();
        var adapter = new MacroSourceAdapter(
            client,
            Options.Create(new MacroSourceOptions
            {
                Path = ValuesPath,
                CatalogPath = CatalogPath,
                SerieListPath = SerieListPath,
                EvdsApiKey = key,
            }));

        await adapter.FetchAsync(Date);

        Assert.All(client.Gets, p => Assert.DoesNotContain(key, p, StringComparison.Ordinal));
        Assert.All(client.Posts, p => Assert.DoesNotContain(key, p.Body, StringComparison.Ordinal));

        // A legacy GET failure that echoes the key is redacted before it can reach a log/quarantine.
        var failing = new FailingGetClient($"{ValuesPath}?series=x&key={Uri.EscapeDataString(key)}");
        var legacy = new MacroSourceAdapter(
            failing,
            Options.Create(new MacroSourceOptions { Path = "/service/evds", EvdsApiKey = key }));
        var thrown = await Assert.ThrowsAsync<SourceFetchException>(() => legacy.FetchAsync(Date));
        Assert.DoesNotContain(key, thrown.Message, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    private static MacroSourceAdapter BuildV3(ISourceClient client) =>
        new(
            client,
            Options.Create(new MacroSourceOptions
            {
                BaseUrl = "https://evds3.tcmb.example",
                Path = ValuesPath,
                CatalogPath = CatalogPath,
                SerieListPath = SerieListPath,
                EvdsApiKey = string.Empty,
            }));

    /// <summary>An EVDS envelope covering the five official series plus one unknown code.
    /// The official observations deliberately carry no canonical <c>code</c> (as the real
    /// EVDS response does not) so the test discriminates the <c>seriesCode</c> → canonical
    /// mapping.</summary>
    private static string OfficialEnvelope() =>
        JsonSerializer.Serialize(new
        {
            sourceRef = "evds://tcmb",
            totalCount = 6,
            series = new object[]
            {
                new { seriesCode = "TP.TUKFIY2025.GENEL", valueDate = "2026-09-01", value = 45.20m, unit = "% YoY", recordedAt = "2026-10-03T10:00:00+00:00" },
                new { seriesCode = "TP.APIFON1", valueDate = "2026-09-15", value = 42.50m, unit = "%", recordedAt = "2026-09-15T14:00:00+00:00" },
                new { seriesCode = "TP.DK.USD.A.YTL", valueDate = "2026-10-06", value = 47.10m, unit = "TRY", recordedAt = "2026-10-06T21:30:00+00:00" },
                new { seriesCode = "TP.DK.EUR.A.YTL", valueDate = "2026-10-06", value = 51.40m, unit = "TRY", recordedAt = "2026-10-06T21:30:00+00:00" },
                new { seriesCode = "TP.MK.ALTIN", valueDate = "2026-09-26", value = 5200.00m, unit = "USD/oz", recordedAt = "2026-09-26T21:30:00+00:00" },
                new { seriesCode = "TP.UNKNOWN.SERIES", code = (string?)null, valueDate = "2026-10-06", value = 1m, unit = "x", recordedAt = (string?)null },
            },
        });

    /// <summary>A recording v3 client: serves the catalog + serieList on GET and the values
    /// body on POST, and records both.</summary>
    private sealed class V3FakeClient : ISourceClient
    {
        private readonly string _catalog;
        private readonly string _serieList;
        private readonly string _values;

        public V3FakeClient(
            string? catalog = null,
            string? serieList = null,
            string? values = null)
        {
            _catalog = catalog ?? RealSourceCatalog.Get(RealSourceCatalog.MacroEvdsV3Catalog).Body;
            _serieList = serieList ?? RealSourceCatalog.Get(RealSourceCatalog.MacroEvdsV3SerieList).Body;
            _values = values ?? RealSourceCatalog.Get(RealSourceCatalog.MacroEvdsV3).Body;
        }

        public List<string> Gets { get; } = [];

        public List<(string Path, string Body)> Posts { get; } = [];

        public Task<string> GetStringAsync(string path, CancellationToken cancellationToken = default)
        {
            Gets.Add(path);
            return Task.FromResult(path.Contains("serieList", StringComparison.Ordinal) ? _serieList : _catalog);
        }

        public Task<string> PostJsonAsync(string path, string jsonBody, CancellationToken cancellationToken = default)
        {
            Posts.Add((path, jsonBody));
            return Task.FromResult(_values);
        }
    }

    private sealed class RecordingSourceClient : ISourceClient
    {
        private readonly string _body;

        public RecordingSourceClient(string body) => _body = body;

        public List<string> Requests { get; } = [];

        public Task<string> GetStringAsync(string path, CancellationToken cancellationToken = default)
        {
            Requests.Add(path);
            return Task.FromResult(_body);
        }
    }

    /// <summary>A client whose GET always fails, echoing the path into the message (redaction probe).</summary>
    private sealed class FailingGetClient : ISourceClient
    {
        private readonly string _path;

        public FailingGetClient(string path) => _path = path;

        public Task<string> GetStringAsync(string path, CancellationToken cancellationToken = default) =>
            throw new SourceFetchException($"Source returned HTTP 500 for '{_path}'.");
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

/// <summary>
/// TKT-foundation-027 acceptance — the <b>EVDS 3.0</b> macro source through the WireMock source
/// double and a migrated Testcontainers PostgreSQL (01 v1.5.6 §10.9.12, FR-MOV-014). The v3
/// discovery routes and the anonymous <c>POST /fe</c> body are served as raw bytes and parsed
/// by <see cref="MacroSourceAdapter"/> in <c>src/Ingestion</c> — never a fixture-owned decoder
/// (the §10.9.7 rule). The canonical double goes through the same production adapter; the FU
/// golden values are untouched.
/// </summary>
public sealed class MacroEvdsRealEnvelopeTests : IClassFixture<WireMockFixture>, IClassFixture<PostgresFixture>
{
    private const string CatalogPath = "/igmevdsms-dis/categories/withDatagroups/type=json";
    private const string SerieListPath = "/igmevdsms-dis/serieList/fe/type=json&code={code}";
    private const string ValuesPath = "/igmevdsms-dis/fe";
    private const string TestApiKey = "test-evds-key";
    private static readonly DateOnly T = new(2026, 10, 6);
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);

    private readonly WireMockFixture _wireMock;
    private readonly PostgresFixture _postgres;

    public MacroEvdsRealEnvelopeTests(WireMockFixture wireMock, PostgresFixture postgres)
    {
        _wireMock = wireMock;
        _postgres = postgres;
    }

    /// <summary>
    /// TKT_foundation_027 / TC-MOV-028 — the v3 envelope (all five official series over the
    /// bounded window) parses through the production adapter: each underscored column resolves
    /// to its canonical code, the CPI index becomes YoY, the source is stamped <c>evds</c>,
    /// the real row's absent unit/recorded_at stay honest NULLs, and <c>INDEP_CPI</c> never
    /// arrives from the adapter.
    /// </summary>
    [Fact]
    public async Task TKT_foundation_027_Real_v3_envelope_parses_through_the_production_adapter()
    {
        StubV3Discovery();
        StubV3Values();

        var fetched = await BuildHttpAdapter().FetchAsync(T);

        Assert.Equal(RealSourceCatalog.EvdsSource, fetched.SourceRef);
        var series = fetched.Payload.Series!;

        Assert.Equal(
            new[] { "CBRT_REPO", "EUR_TRY", "GOLD", "TUIK_CPI", "USD_TRY" },
            series.Select(s => s.Code).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToArray());

        Assert.Equal(48.10m, Assert.Single(series, s => s.Code == MacroSeries.UsdTry).Value);
        Assert.Equal(53.20m, Assert.Single(series, s => s.Code == MacroSeries.EurTry).Value);
        Assert.Equal(4200.00m, Assert.Single(series, s => s.Code == MacroSeries.Gold).Value);
        Assert.Equal(40.50m, Assert.Single(series, s => s.Code == MacroSeries.CbrtRepo).Value);
        Assert.Equal(
            (137.23m / 105.78m - 1m) * 100m,
            Assert.Single(series, s => s.Code == MacroSeries.TuikCpi).Value);

        // Honest gaps on the real row: no per-row unit and no publication timestamp.
        Assert.All(series, s => Assert.Null(s.Unit));
        Assert.All(series, s => Assert.Null(s.RecordedAt));
        Assert.DoesNotContain(series, s => s.Code == MacroSeries.IndepCpi);
    }

    /// <summary>
    /// TKT_foundation_027 — the bounded 90-day value-date window is enforced: an observation
    /// the source returns outside the FX/gold/repo request window is dropped, so an ignoring
    /// source cannot smuggle an out-of-bound value into <c>macro_values</c>.
    /// </summary>
    [Fact]
    public void TKT_foundation_027_Decoder_drops_observations_outside_the_bounded_window()
    {
        const string body = """{"totalCount":2,"items":[{"Tarih":"06-10-2026","TP_DK_USD_S_YTL":"48.10"},{"Tarih":"01-01-2020","TP_DK_USD_S_YTL":"9.99"}]}""";
        using var document = JsonDocument.Parse(body);

        var rows = EvdsMacroDecoder.DecodeRows(document, T.AddDays(-90), T);

        var row = Assert.Single(rows);
        Assert.Equal(MacroSeries.UsdTry, row.Code);
        Assert.Equal(new DateOnly(2026, 10, 6), row.ValueDate);
        Assert.Equal(48.10m, row.Value);
    }

    /// <summary>
    /// TKT_foundation_027 — the bounded live capture (the anonymous v3 <c>POST /fe</c>,
    /// 2026-10-18) parses through the production adapter: the underscored USD/TRY + EUR/TRY
    /// selling columns resolve to their canonical codes and the values flow through unchanged.
    /// </summary>
    [Fact]
    public async Task TKT_foundation_027_Bounded_live_capture_parses()
    {
        _wireMock.Server.ResetMappings();
        StubV3Discovery();
        // Only the verbatim capture is served for both v3 value requests: the CPI request finds
        // no CPI column (the capture carries FX only) and correctly yields nothing.
        _wireMock.StubBytes(
            ValuesPath,
            RealSourceCatalog.Get(RealSourceCatalog.MacroEvdsV3Capture).RawBytes,
            "application/json; charset=utf-8",
            method: "POST");

        var fetched = await BuildHttpAdapter().FetchAsync(new DateOnly(2026, 10, 16));
        var series = fetched.Payload.Series!;

        var usd = Assert.Single(series, s => s.Code == MacroSeries.UsdTry && s.ValueDate == new DateOnly(2026, 10, 16));
        Assert.Equal(49.42m, usd.Value);
        Assert.Equal(
            55.41m,
            Assert.Single(series, s => s.Code == MacroSeries.EurTry && s.ValueDate == new DateOnly(2026, 10, 16)).Value);
    }

    /// <summary>
    /// TKT_foundation_027 — running the macro jobs against the v3 stubs ingests the five
    /// official series into <c>macro_values</c> with <c>source_ref = evds</c>, records the
    /// runs in the ledger, and never leaks the API key into a log or quarantine record.
    /// </summary>
    [Fact]
    public async Task TKT_foundation_027_V3_envelope_ingests_the_five_official_series()
    {
        await ResetAsync();
        StubV3Discovery();
        StubV3Values();

        var host = BuildIngestion();

        var daily = await RunAsync(host.Provider, MacroDailyJob.Code);
        Assert.Equal("succeeded", daily.Status);
        Assert.Equal(3, daily.Written);

        // The CPI job owns INDEP_CPI too; the independent measure is admin-entered and never
        // arrives from the adapter, so the run is partial (LATE for INDEP_CPI) but ingests the
        // two official series it carries (TUIK_CPI YoY + CBRT_REPO).
        var cpi = await RunAsync(host.Provider, MacroCpiJob.Code);
        Assert.NotEqual("failed", cpi.Status);
        Assert.Equal(2, cpi.Written);

        await using (var db = _postgres.CreateContext())
        {
            var official = await db.MacroValues
                .Where(v => v.SeriesCode == "TUIK_CPI" || v.SeriesCode == "CBRT_REPO" || v.SeriesCode == "USD_TRY"
                    || v.SeriesCode == "EUR_TRY" || v.SeriesCode == "GOLD")
                .ToListAsync();

            Assert.Equal(
                new[] { "CBRT_REPO", "EUR_TRY", "GOLD", "TUIK_CPI", "USD_TRY" },
                official.Select(v => v.SeriesCode).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToArray());

            Assert.All(official, row => Assert.Equal(RealSourceCatalog.EvdsSource, row.SourceRef));
            Assert.Equal(48.10m, official.Single(v => v.SeriesCode == "USD_TRY").Value);
            Assert.Equal(53.20m, official.Single(v => v.SeriesCode == "EUR_TRY").Value);
            Assert.Equal(4200.00m, official.Single(v => v.SeriesCode == "GOLD").Value);
            Assert.Equal(40.50m, official.Single(v => v.SeriesCode == "CBRT_REPO").Value);
            // macro_values stores numeric(18,6): compare at the column's precision.
            Assert.Equal(
                Math.Round((137.23m / 105.78m - 1m) * 100m, 6),
                official.Single(v => v.SeriesCode == "TUIK_CPI").Value);

            var ledger = await db.IngestRuns
                .Where(r => r.JobCode == MacroDailyJob.Code || r.JobCode == MacroCpiJob.Code)
                .ToListAsync();
            Assert.Contains(ledger, r => r.JobCode == MacroDailyJob.Code && r.Status == IngestResult.Succeeded);
            Assert.Contains(ledger, r => r.JobCode == MacroCpiJob.Code && r.Status == IngestResult.Partial);

            // No API-key leak into a quarantine record.
            var quarantine = await db.QuarantinedFacts.ToListAsync();
            Assert.DoesNotContain(quarantine, f =>
                (f.PayloadJson ?? string.Empty).Contains(TestApiKey, StringComparison.Ordinal));
        }

        // No API-key leak into the logs.
        Assert.DoesNotContain(host.Logs.Events, e =>
            e.RenderMessage().Contains(TestApiKey, StringComparison.Ordinal));

        await host.Provider.DisposeAsync();
    }

    /// <summary>
    /// TKT_foundation_027 (§10.9.7) — the canonical EVDS double (the L2 golden shape) still
    /// parses through the same hardened adapter, unchanged: the second leg of the "both shapes
    /// parse through one production adapter" contract. The legacy single-GET lane is preserved.
    /// </summary>
    [Fact]
    public async Task TKT_foundation_027_Canonical_double_still_parses_through_the_same_adapter()
    {
        _wireMock.Server.ResetMappings();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.MacroOk, path: "/service/evds");

        var http = new HttpClient { BaseAddress = new Uri(_wireMock.BaseUrl) };
        var adapter = new MacroSourceAdapter(
            new HttpSourceClient(http),
            Options.Create(new MacroSourceOptions { BaseUrl = _wireMock.BaseUrl, Path = "/service/evds", EvdsApiKey = TestApiKey }));

        var fetched = await adapter.FetchAsync(T);

        Assert.Equal("evds://tcmb", fetched.SourceRef);
        var series = fetched.Payload.Series!;
        Assert.Equal(
            new[] { "CBRT_REPO", "EUR_TRY", "GOLD", "TUIK_CPI", "USD_TRY" },
            series.Select(s => s.Code).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToArray());
        Assert.Equal(47.10m, Assert.Single(series, s => s.Code == MacroSeries.UsdTry).Value);
        Assert.DoesNotContain(series, s => s.Code == MacroSeries.IndepCpi);
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    private MacroSourceAdapter BuildHttpAdapter()
    {
        var http = new HttpClient { BaseAddress = new Uri(_wireMock.BaseUrl) };
        return new MacroSourceAdapter(
            new HttpSourceClient(http),
            Options.Create(new MacroSourceOptions
            {
                BaseUrl = _wireMock.BaseUrl,
                Path = ValuesPath,
                CatalogPath = CatalogPath,
                SerieListPath = SerieListPath,
                EvdsApiKey = TestApiKey,
            }));
    }

    /// <summary>Stubs the anonymous discovery routes: the catalog plus each datagroup's serieList.</summary>
    private void StubV3Discovery()
    {
        _wireMock.StubJson(CatalogPath, RealSourceCatalog.Get(RealSourceCatalog.MacroEvdsV3Catalog).Body);
        foreach (var datagroup in new[] { "bie_dkdovytl", "bie_mkaltytl", "bie_bispolfaiz", "bie_tukfiy2025" })
        {
            _wireMock.StubJson(SerieListPath.Replace("{code}", datagroup, StringComparison.Ordinal),
                RealSourceCatalog.Get(RealSourceCatalog.MacroEvdsV3SerieList).Body);
        }
    }

    private void StubV3Values() =>
        _wireMock.StubJson(ValuesPath, RealSourceCatalog.Get(RealSourceCatalog.MacroEvdsV3).Body, method: "POST");

    private IngestionHost BuildIngestion()
    {
        var logs = new InMemorySerilogSink();
        var clock = new FakeTimeProvider(new DateTimeOffset(T.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero));
        var logger = new LoggerConfiguration().MinimumLevel.Warning().WriteTo.Sink(logs).CreateLogger();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _postgres.ConnectionString,
                ["Ingestion:Macro:BaseUrl"] = _wireMock.BaseUrl,
                ["Ingestion:Macro:Path"] = ValuesPath,
                ["Ingestion:Macro:CatalogPath"] = CatalogPath,
                ["Ingestion:Macro:SerieListPath"] = SerieListPath,
                ["Ingestion:Macro:EvdsApiKey"] = TestApiKey,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog(logger, dispose: false));
        services.AddSingleton<TimeProvider>(clock);
        services.AddMarketDataIngestion(configuration);

        return new IngestionHost(services.BuildServiceProvider(), logs);
    }

    private static async Task<IngestResult> RunAsync(IServiceProvider provider, string jobCode)
    {
        using var scope = provider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IIngestionJobRunner>();
        return await runner.RunAsync(jobCode, new IngestionRequest(T));
    }

    private async Task ResetAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM quarantined_facts");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM macro_values");
    }

    private sealed class IngestionHost
    {
        public IngestionHost(ServiceProvider provider, InMemorySerilogSink logs)
        {
            Provider = provider;
            Logs = logs;
        }

        public ServiceProvider Provider { get; }

        public InMemorySerilogSink Logs { get; }
    }
}
