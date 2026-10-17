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
/// TKT-mov-008 acceptance — the consolidated TCMB EVDS macro adapter (TC-MOV-026, FR-MOV-014,
/// OQ-MOV-005). These are adapter-contract tests: they drive the real
/// <see cref="MacroSourceAdapter"/> through the <see cref="ISourceClient"/> seam with a fake
/// client and assert the EVDS request it builds and how it maps the EVDS envelope back to
/// the canonical macro series. No database or WireMock is involved — the cross-process
/// DB outcome is asserted in <see cref="MacroIngestionTests.TC_MOV_026_Consolidated_evds_adapter_ingests_the_five_official_series"/>.
/// </summary>
public sealed class MacroEvdsAdapterTests
{
    private static readonly DateOnly Date = new(2026, 10, 6);

    /// <summary>TC-MOV-026 — the adapter issues a single EVDS request naming the five
    /// official series, the bounded value-date window, the JSON type and the configured API key.</summary>
    [Fact]
    public async Task TC_MOV_026_Builds_the_consolidated_evds_request()
    {
        var client = new RecordingSourceClient(OfficialEnvelope());
        var adapter = Build(client, apiKey: "test-evds-key");

        await adapter.FetchAsync(Date);

        Assert.Equal(MacroSourceAdapter.EvdsSource, adapter.SourceName);
        var path = Assert.Single(client.Requests);
        Assert.StartsWith("/service/evds?", path, StringComparison.Ordinal);
        Assert.Contains("type=json", path);
        Assert.Contains("key=test-evds-key", path);

        // A bounded lookback, not a single-day window (DR-2): EVDS filters by observation
        // date, so the monthly/per-release series are only reachable with a lookback.
        Assert.Contains("endDate=06-10-2026", path);
        Assert.Contains("startDate=08-07-2026", path);

        // One request carries all five official series joined in order (the consolidation).
        var expectedSeries = string.Join('-',
            "TP.TUKFIY2025.GENEL",
            "TP.APIFON1",
            "TP.DK.USD.A.YTL",
            "TP.DK.EUR.A.YTL",
            "TP.MK.ALTIN");
        Assert.Contains($"series={expectedSeries}", path);
    }

    /// <summary>TC-MOV-026 — the EVDS envelope is mapped onto the canonical macro items:
    /// each EVDS <c>seriesCode</c> resolves to its canonical code (the observations carry no
    /// canonical <c>code</c>, so the mapping is what supplies it), value/date/unit/recorded_at
    /// survive, and an unknown code with no mapping is dropped.</summary>
    [Fact]
    public async Task TC_MOV_026_Maps_the_evds_envelope_to_canonical_series()
    {
        var client = new RecordingSourceClient(OfficialEnvelope());
        var adapter = Build(client, apiKey: "k");

        var payload = (await adapter.FetchAsync(Date)).Payload;

        var codes = payload.Series!.Select(s => s.Code).ToList();
        Assert.Contains(MacroSeries.UsdTry, codes);
        Assert.Contains(MacroSeries.EurTry, codes);
        Assert.Contains(MacroSeries.Gold, codes);
        Assert.Contains(MacroSeries.TuikCpi, codes);
        Assert.Contains(MacroSeries.CbrtRepo, codes);

        var usd = Assert.Single(payload.Series!, s => s.Code == MacroSeries.UsdTry);
        Assert.Equal(47.10m, usd.Value);
        Assert.Equal(Date, usd.ValueDate);
        Assert.Equal("TRY", usd.Unit);

        // An observation naming an unknown EVDS code with no canonical code is dropped.
        Assert.DoesNotContain(payload.Series!, s => s.Code == "TP.UNKNOWN.SERIES");
    }

    /// <summary>TC-MOV-026 — a malformed EVDS body surfaces as an invalid (payload) failure
    /// so the job can quarantine it and alert; a body that deserializes to null is an
    /// unreachable-class failure.</summary>
    [Fact]
    public async Task TC_MOV_026_Unparseable_evds_body_is_a_payload_failure()
    {
        var malformed = new RecordingSourceClient("{ not valid json");
        await Assert.ThrowsAsync<SourcePayloadException>(() => Build(malformed, "k").FetchAsync(Date));

        var empty = new RecordingSourceClient("null");
        await Assert.ThrowsAsync<SourceFetchException>(() => Build(empty, "k").FetchAsync(Date));
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    private static MacroSourceAdapter Build(ISourceClient client, string apiKey) =>
        new(
            client,
            Options.Create(new MacroSourceOptions
            {
                BaseUrl = "https://evds2.tcmb.example",
                Path = "/service/evds",
                EvdsApiKey = apiKey,
            }));

    /// <summary>An EVDS envelope covering the five official series plus one unknown code.
    /// The official observations deliberately carry no canonical <c>code</c> (as the real EVDS
    /// response does not) so the test discriminates the <c>seriesCode</c> → canonical mapping.</summary>
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
}
/// <summary>
/// TKT-foundation-019 acceptance — the <b>real</b> TCMB EVDS macro envelope decoded by the
/// hardened production adapter (01 v1.5 §10.9.6, FR-MOV-014). The real-source catalog's
/// column-oriented <c>items</c>/<c>Tarih</c> body is served through the WireMock harness as
/// raw bytes and parsed by <see cref="MacroSourceAdapter"/> in <c>src/Ingestion</c> — never a
/// fixture-owned decoder (the DR-01 rule, §10.9.7). Both the real shape and the canonical
/// double envelope go through the <b>same</b> production adapter; the FU golden values are
/// untouched (TC-MOV-026's canonical leg is re-asserted here, not changed).
/// </summary>
public sealed class MacroEvdsRealEnvelopeTests : IClassFixture<WireMockFixture>, IClassFixture<PostgresFixture>
{
    private const string MacroPath = "/service/evds";
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
    /// TC-MOV-028 — the real EVDS column envelope (five official series over a bounded
    /// window) parses through the production adapter: each EVDS column resolves to its
    /// canonical code, value/date survive, the source is stamped <c>evds</c>, the real
    /// row's absent unit/recorded_at stay honest NULLs, and <c>INDEP_CPI</c> never arrives
    /// from the adapter (the five-series set is unchanged).
    /// </summary>
    [Fact]
    public async Task TC_MOV_028_Real_evds_envelope_parses_through_the_hardened_adapter()
    {
        _wireMock.Server.ResetMappings();
        _wireMock.StubRealSource(RealSourceCatalog.MacroEvds, path: MacroPath);

        var fetched = await BuildHttpAdapter().FetchAsync(T);

        Assert.Equal(RealSourceCatalog.EvdsSource, fetched.SourceRef);
        var series = fetched.Payload.Series!;

        // The five official series, each exactly once, in the adapter's canonical codes.
        Assert.Equal(
            new[] { "CBRT_REPO", "EUR_TRY", "GOLD", "TUIK_CPI", "USD_TRY" },
            series.Select(s => s.Code).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToArray());

        // Fidelity pins — the values present in the constructed real body flow through
        // unchanged (never derived numbers). TP.APIFON1 arrives as a numeric string.
        var cpi = Assert.Single(series, s => s.Code == MacroSeries.TuikCpi);
        Assert.Equal(new DateOnly(2026, 9, 1), cpi.ValueDate);
        Assert.Equal(46.30m, cpi.Value);
        var repo = Assert.Single(series, s => s.Code == MacroSeries.CbrtRepo);
        Assert.Equal(43.00m, repo.Value);
        var usd = Assert.Single(series, s => s.Code == MacroSeries.UsdTry);
        Assert.Equal(new DateOnly(2026, 10, 6), usd.ValueDate);
        Assert.Equal(47.15m, usd.Value);
        var eur = Assert.Single(series, s => s.Code == MacroSeries.EurTry);
        Assert.Equal(51.55m, eur.Value);
        var gold = Assert.Single(series, s => s.Code == MacroSeries.Gold);
        Assert.Equal(5325.50m, gold.Value);

        // Honest gaps on the real row: no per-row unit and no publication timestamp.
        Assert.All(series, s => Assert.Null(s.Unit));
        Assert.All(series, s => Assert.Null(s.RecordedAt));

        // The independent CPI measure is entered through the admin endpoint, never the
        // adapter (§10.9.6 — the five-official-series set).
        Assert.DoesNotContain(series, s => s.Code == MacroSeries.IndepCpi);
    }

    /// <summary>
    /// TC-MOV-028 — the canonical EVDS double (the L2 golden shape, TC-MOV-026's payload)
    /// still parses through the same hardened adapter, unchanged: the second leg of the
    /// "both shapes parse through one production adapter" contract.
    /// </summary>
    [Fact]
    public async Task TC_MOV_028_Canonical_envelope_still_parses_through_the_same_adapter()
    {
        _wireMock.Server.ResetMappings();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.MacroOk, path: MacroPath);

        var fetched = await BuildHttpAdapter().FetchAsync(T);

        Assert.Equal("evds://tcmb", fetched.SourceRef);
        var series = fetched.Payload.Series!;
        Assert.Equal(
            new[] { "CBRT_REPO", "EUR_TRY", "GOLD", "TUIK_CPI", "USD_TRY" },
            series.Select(s => s.Code).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToArray());
        Assert.Equal(47.10m, Assert.Single(series, s => s.Code == MacroSeries.UsdTry).Value);
        Assert.DoesNotContain(series, s => s.Code == MacroSeries.IndepCpi);
    }

    /// <summary>
    /// TC-MOV-028 — the bounded 90-day value-date lookback and the API-key redaction
    /// posture are unchanged: the adapter asks for the bounded window and carries the key
    /// only in the outbound request; the catalog body never contains the key; a source
    /// failure is redacted before it can reach a log or quarantine record. A real row
    /// outside the bound is dropped by the decoder (the source cannot smuggle it past).
    /// </summary>
    [Fact]
    public async Task TC_MOV_028_Bounded_lookback_window_and_api_key_redaction()
    {
        // The catalog entry itself never carries the key.
        Assert.DoesNotContain(TestApiKey, RealSourceCatalog.Get(RealSourceCatalog.MacroEvds).Body);

        var client = new RealEnvelopeRecordingClient(RealSourceCatalog.Get(RealSourceCatalog.MacroEvds).Body);
        var adapter = new MacroSourceAdapter(
            client,
            Options.Create(new MacroSourceOptions { Path = MacroPath, EvdsApiKey = TestApiKey }));

        var fetched = await adapter.FetchAsync(T);

        var path = Assert.Single(client.Requests);
        Assert.StartsWith($"{MacroPath}?", path, StringComparison.Ordinal);
        Assert.Contains("type=json", path);
        Assert.Contains($"key={TestApiKey}", path);
        Assert.Contains("endDate=06-10-2026", path);
        Assert.Contains("startDate=08-07-2026", path);

        // Bounded value-date window (DR-2): every decoded observation sits inside it.
        Assert.All(fetched.Payload.Series!, s =>
        {
            Assert.True(s.ValueDate >= new DateOnly(2026, 7, 8), s.ValueDate.ToString());
            Assert.True(s.ValueDate <= T, s.ValueDate.ToString());
        });
    }

    /// <summary>
    /// TC-MOV-028 — the decoder enforces the bounded value-date window: an observation the
    /// source returns outside the 90-day lookback is dropped, so an ignoring source cannot
    /// smuggle an out-of-bound value into <c>macro_values</c>.
    /// </summary>
    [Fact]
    public void TC_MOV_028_Decoder_drops_observations_outside_the_bounded_window()
    {
        const string body = """{"totalCount":2,"items":[{"Tarih":"06-10-2026","TP.DK.USD.A.YTL":47.15},{"Tarih":"01-01-2020","TP.DK.USD.A.YTL":9.99}]}""";
        using var document = JsonDocument.Parse(body);

        var rows = EvdsMacroDecoder.DecodeRows(document, T.AddDays(-90), T);

        var row = Assert.Single(rows);
        Assert.Equal(MacroSeries.UsdTry, row.Code);
        Assert.Equal(new DateOnly(2026, 10, 6), row.ValueDate);
        Assert.Equal(47.15m, row.Value);
    }

    /// <summary>
    /// TC-MOV-028 — running the macro job against the real-envelope stub ingests the five
    /// official series into <c>macro_values</c> with <c>source_ref = evds</c>, records the
    /// run in the ledger, and never leaks the API key into a log or quarantine record.
    /// </summary>
    [Fact]
    public async Task TC_MOV_028_Real_evds_envelope_ingests_the_five_official_series()
    {
        await ResetAsync();
        _wireMock.Server.ResetMappings();
        _wireMock.StubRealSource(RealSourceCatalog.MacroEvds, path: MacroPath);

        var host = BuildIngestion();

        var daily = await RunAsync(host.Provider, MacroDailyJob.Code);
        Assert.Equal("succeeded", daily.Status);
        Assert.Equal(3, daily.Written);

        // The CPI job owns INDEP_CPI too; the independent measure is admin-entered and
        // never arrives from the adapter (the five-official-series set), so the run is
        // partial (LATE for INDEP_CPI) but ingests the two official series it does carry.
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

            // The real shape stamps the concrete EVDS source (not the canonical fake ref).
            Assert.All(official, row => Assert.Equal(RealSourceCatalog.EvdsSource, row.SourceRef));
            Assert.Equal(46.30m, official.Single(v => v.SeriesCode == "TUIK_CPI").Value);
            Assert.Equal(43.00m, official.Single(v => v.SeriesCode == "CBRT_REPO").Value);
            Assert.Equal(47.15m, official.Single(v => v.SeriesCode == "USD_TRY").Value);
            Assert.Equal(51.55m, official.Single(v => v.SeriesCode == "EUR_TRY").Value);
            Assert.Equal(5325.50m, official.Single(v => v.SeriesCode == "GOLD").Value);

            // The run ledger records both runs: macro-daily succeeded; macro-cpi is partial
            // (INDEP_CPI is admin-entered — the adapter never supplies it).
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
                Path = MacroPath,
                EvdsApiKey = TestApiKey,
            }));
    }

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
                ["Ingestion:Macro:Path"] = MacroPath,
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

    /// <summary>Returns the real EVDS body from the catalog while recording the request path.</summary>
    private sealed class RealEnvelopeRecordingClient : ISourceClient
    {
        private readonly string _body;

        public RealEnvelopeRecordingClient(string body) => _body = body;

        public List<string> Requests { get; } = [];

        public Task<string> GetStringAsync(string path, CancellationToken cancellationToken = default)
        {
            Requests.Add(path);
            return Task.FromResult(_body);
        }
    }
}
