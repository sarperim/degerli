using Degerli.Api.IntegrationTests.Harness;
using Degerli.Api.IntegrationTests.Harness.Mail;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.Coverage;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Sources;
using Degerli.Ingestion.Universe;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Serilog;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-foundation-021 acceptance — the <b>real</b> İş Yatırım instrument-universe and
/// index-levels envelopes decoded by the production adapters (01 v1.5 §10.9.8,
/// FR-MDF-006/007, FR-MDF-008). The real-source catalog's bounded live captures are served
/// through the WireMock harness as raw bytes and parsed by <see cref="UniverseSourceAdapter"/>
/// / <see cref="IndexLevelsSourceAdapter"/> in <c>src/Ingestion</c> — never a fixture-owned
/// decoder (DR-01, §10.9.7) — and one case drives the same real envelope through the job's DB
/// ingest path. No TC exists for this lane yet (the ticket's explicit criteria are the
/// contract; the test-planner follow-up is flagged in the ticket) so every test carries the
/// ticket/lane tag <c>TKT-foundation-021</c>. The FU golden values are untouched: these cases
/// assert parse shape/fidelity and job behaviour, never derived numbers.
/// </summary>
public sealed class UniverseRealEnvelopeTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private const string UniversePath = "/real-sources/isyatirim-universe";
    private const string IndexLevelsPath = "/real-sources/isyatirim-index-levels";

    /// <summary>The five instruments in the bounded capture (catalog order).</summary>
    private static readonly string[] CapturedSymbols = ["A1CAP", "A1YEN", "AAGYO", "ACSEL", "ADEL"];

    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public UniverseRealEnvelopeTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    /// <summary>
    /// TKT-foundation-021 — the real İş Yatırım <c>GetHissePerformans</c> envelope (the legacy
    /// ASP.NET AJAX <c>{"d":"…Oran…"}</c> wrapper, POST-only) parses through the production
    /// <see cref="UniverseSourceAdapter"/>: each <c>Oran</c> row becomes one instrument with the
    /// source's trailing padding trimmed, the sub-sector label carried as the classification
    /// (the source publishes no code today), <c>listingDate</c> an honest NULL, the distinct
    /// labels reshaped into sector reference rows, and the whole payload stamped
    /// <c>isyatirim</c> (FR-MDF-008). No effective-dated change set is emitted — the resource
    /// is a full snapshot.
    /// </summary>
    [Fact]
    public async Task TKT_foundation_021_Real_universe_envelope_decodes_through_the_production_adapter()
    {
        _wireMock.Server.ResetMappings();
        _wireMock.StubRealSource(RealSourceCatalog.Universe, path: UniversePath, method: "POST");

        var fetched = await BuildUniverseAdapter(UniversePath, method: "POST").FetchAsync(T);

        // The real envelope carries no provenance field: the adapter stamps the concrete source.
        Assert.Equal(UniverseSourceAdapter.IsYatirimSource, fetched.SourceRef);

        var instruments = fetched.Payload.Instruments!;
        Assert.Equal(CapturedSymbols.Length, instruments.Count);
        var bySymbol = instruments.ToDictionary(i => i.Symbol, StringComparer.Ordinal);

        // Fidelity pins — the captured rows' names (the source pads HISSE_TANIM; the decoder
        // trims) and their sub-sector labels, never derived numbers.
        Assert.Equal("A1 Capital", bySymbol["A1CAP"].Name); // trailing NBSP trimmed
        Assert.Equal("Aracı Kurumlar", bySymbol["A1CAP"].SectorCode);
        Assert.Equal("Kartal Yenilenebili Enerji", bySymbol["A1YEN"].Name);
        Assert.Equal("Elektrik Üretim", bySymbol["A1YEN"].SectorCode);
        Assert.Equal("Ağaoğlu GYO", bySymbol["AAGYO"].Name);
        Assert.Equal("Acıselsan Acıpayam Selüloz", bySymbol["ACSEL"].Name);
        Assert.Equal("Adel Kalemcilik", bySymbol["ADEL"].Name); // trailing spaces trimmed

        // listingDate is an honest gap: the source publishes no listing date.
        Assert.All(instruments, instrument => Assert.Null(instrument.ListingDate));

        // The distinct sub-sector labels become canonical sector reference rows.
        var sectorCodes = fetched.Payload.Sectors!.Select(s => s.Code).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("Aracı Kurumlar", sectorCodes);
        Assert.Contains("Elektrik Üretim", sectorCodes);
        Assert.Contains("GYO", sectorCodes);
        Assert.Contains("Kimyasal Ürün", sectorCodes);
        Assert.Contains("Kırtasiye", sectorCodes);

        // A full snapshot: no effective-dated change set, no index reference rows.
        Assert.Null(fetched.Payload.EffectiveDate);
        Assert.Null(fetched.Payload.Add);
        Assert.Null(fetched.Payload.Remove);
        Assert.Null(fetched.Payload.Indices);
    }

    /// <summary>
    /// TKT-foundation-021 — the canonical <c>sourceRef</c>/<c>instruments</c> double (the L2
    /// golden shape, §10.9.7) still parses through the <b>same</b> production adapter,
    /// unchanged: detection is by shape, never by config. This is the second leg of the
    /// "both shapes parse through one production adapter" contract, so no golden churns.
    /// </summary>
    [Fact]
    public async Task TKT_foundation_021_Canonical_universe_envelope_still_parses_through_the_same_adapter()
    {
        _wireMock.Server.ResetMappings();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.UniverseMissingSector, path: UniversePath);

        var fetched = await BuildUniverseAdapter(UniversePath, method: "GET").FetchAsync(T);

        Assert.False(string.IsNullOrWhiteSpace(fetched.SourceRef));
        Assert.Equal(L2.Instruments.Count, fetched.Payload.Instruments!.Count);
        Assert.Contains(fetched.Payload.Instruments!, i => i.Symbol == "ALFA");
    }

    /// <summary>
    /// TKT-foundation-021 — the real İş Yatırım <c>IndexHistoricalAll</c> chart envelope
    /// (<c>{"data":[[epochMillis,close],…]}</c>) parses through the production
    /// <see cref="IndexLevelsSourceAdapter"/>: each pair becomes one dated close, the chart's
    /// midnight Istanbul timestamps map to the correct calendar date (permanent UTC+3), the
    /// requested index code is stamped on every fact (the body carries none), and the source
    /// is <c>isyatirim</c>.
    /// </summary>
    [Fact]
    public async Task TKT_foundation_021_Real_index_levels_envelope_decodes_through_the_production_adapter()
    {
        _wireMock.Server.ResetMappings();
        _wireMock.StubRealSource(RealSourceCatalog.IndexLevels, path: IndexLevelsPath);

        var fetched = await BuildIndexLevelsAdapter(IndexLevelsPath, indexCode: "XU030").FetchAsync(T);

        Assert.Equal(UniverseSourceAdapter.IsYatirimSource, fetched.SourceRef);

        var levels = fetched.Payload.Levels!;
        Assert.Equal(9, levels.Count);
        Assert.All(levels, level => Assert.Equal("XU030", level.IndexCode));

        // Morning-of-Istanbul timestamps map to the correct trading dates (UTC+3).
        var byDate = levels.ToDictionary(l => l.Date, l => l.Close);
        Assert.Equal(new DateOnly(2026, 10, 6), levels[0].Date);
        Assert.Equal(new DateOnly(2026, 10, 16), levels[^1].Date);

        // Fidelity pins — the captured closes flow through unchanged, never derived.
        Assert.Equal(15381.69043m, byDate[new DateOnly(2026, 10, 6)]);
        Assert.Equal(15013.83008m, byDate[new DateOnly(2026, 10, 7)]);
        Assert.Equal(14558.42969m, byDate[new DateOnly(2026, 10, 16)]);
    }

    /// <summary>
    /// TKT-foundation-021 — the canonical <c>sourceRef</c>/<c>levels</c> double (the L2 golden
    /// shape, §10.9.7) still parses through the same hardened index-levels adapter, unchanged.
    /// </summary>
    [Fact]
    public async Task TKT_foundation_021_Canonical_index_levels_envelope_still_parses_through_the_same_adapter()
    {
        _wireMock.Server.ResetMappings();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.IndexLevelsOk, path: IndexLevelsPath);

        var fetched = await BuildIndexLevelsAdapter(IndexLevelsPath, indexCode: "XU030").FetchAsync(T);

        Assert.False(string.IsNullOrWhiteSpace(fetched.SourceRef));
        Assert.NotNull(fetched.Payload.Levels);
        Assert.NotEmpty(fetched.Payload.Levels!);
        // The canonical body carries its own index codes — the adapter never overwrites them.
        Assert.Contains(fetched.Payload.Levels!, l => l.IndexCode == "XU100");
    }

    /// <summary>
    /// TKT-foundation-021 — the real universe + index-levels envelopes flow end-to-end through
    /// the <c>universe-sync</c> job's DB ingest path (the TC-MOV-028 lane shape): the job
    /// creates the real instruments (append/insert path) with <c>source_ref = isyatirim</c>,
    /// links each to the sector its classification implies, and writes the real index levels
    /// for the seeded index — no run failure.
    /// </summary>
    [Fact]
    public async Task TKT_foundation_021_Real_envelopes_ingest_through_the_job_db_path()
    {
        await ResetAsync();
        // The real universe-sync also fires the checked-in XU100 launch-seed (TKT-foundation-022);
        // in production its members resolve against the ~646 real instruments the sync creates.
        // The bounded CI capture carries only five rows, so stand those members up here so the
        // seed resolves cleanly and this case stays focused on the instrument/level ingest path.
        await CreateXu100InstrumentsAsync();
        _wireMock.Server.ResetMappings();
        _wireMock.StubRealSource(RealSourceCatalog.Universe, path: UniversePath, method: "POST");
        _wireMock.StubRealSource(RealSourceCatalog.IndexLevels, path: IndexLevelsPath);

        await using var host = BuildIngestion(method: "POST", indexLevelsIndex: "XU30");
        var result = await RunAsync(host, "universe-sync");

        Assert.NotEqual(IngestResult.Failed, result.Status);

        await using var db = _postgres.CreateContext();

        // Every captured instrument is stored with the real source provenance (FR-MDF-008).
        foreach (var symbol in CapturedSymbols)
        {
            var instrument = await db.Instruments.SingleAsync(i => i.Symbol == symbol);
            Assert.Equal(UniverseSourceAdapter.IsYatirimSource, instrument.SourceRef);
        }

        // Name fidelity survives the DB path (the source padding is trimmed, not stored).
        var a1cap = await db.Instruments.SingleAsync(i => i.Symbol == "A1CAP");
        Assert.Equal("A1 Capital", a1cap.Name);

        // The classification is linked to a sector whose code is the source's sub-sector label.
        Assert.NotNull(a1cap.SectorId);
        var sector = await db.Sectors.SingleAsync(s => s.Id == a1cap.SectorId);
        Assert.Equal("Aracı Kurumlar", sector.Code);

        // The real index levels are written against the seeded index (XU30).
        var xu30 = await db.Indices.Where(i => i.Code == "XU30").Select(i => i.Id).SingleAsync();
        var levels = await db.IndexLevels.Where(l => l.IndexId == xu30).ToListAsync();
        Assert.Equal(9, levels.Count);
        // The index_levels.close column is numeric(…,4), so the captured 15381.69043 is stored
        // rounded to 15381.6904 — the DB path asserts the persisted value, not the raw string.
        Assert.Contains(levels, l => l.LevelDate == new DateOnly(2026, 10, 6) && l.Close == 15381.6904m);
    }

    /// <summary>
    /// TKT-foundation-021 — the index-levels degradation path (FR-MDF-006, 01 v1.5 §10.9.8):
    /// when the index-levels <b>fetch</b> fails (a source-down, not a parse event), the job
    /// degrades to the non-fatal quarantine + <c>Partial</c> + alert class instead of aborting,
    /// so the instrument/membership sync that already ran is preserved. Before the fix the
    /// <see cref="SourceFetchException"/> propagated and failed the whole run.
    /// </summary>
    [Fact]
    public async Task TKT_foundation_021_Index_levels_fetch_failure_degrades_without_aborting()
    {
        await ResetAsync();
        // Stand up the XU100 launch-seed members so the (TKT-foundation-022) seed resolves and
        // this case asserts exactly the index-levels failure is counted once.
        await CreateXu100InstrumentsAsync();
        _wireMock.Server.ResetMappings();
        _wireMock.StubRealSource(RealSourceCatalog.Universe, path: UniversePath, method: "POST");
        // The index-levels endpoint is down: a non-success response, never a parseable body.
        _wireMock.StubJson(IndexLevelsPath, """{"error":"source unavailable"}""", statusCode: 500);

        await using var host = BuildIngestion(method: "POST", indexLevelsIndex: "XU30");
        var result = await RunAsync(host, "universe-sync");

        // Degraded, not failed: the run is Partial and the failure is counted once.
        Assert.Equal(IngestResult.Partial, result.Status);
        Assert.Equal(1, result.Quarantined);

        await using var db = _postgres.CreateContext();

        // The instrument sync was not aborted by the index-levels failure.
        Assert.True(await db.Instruments.AnyAsync(i => i.Symbol == "A1CAP"));
        Assert.False(await db.IndexLevels.AnyAsync());

        // The failure is quarantined (non-silent) …
        var quarantined = await db.QuarantinedFacts.SingleAsync(q => q.ReasonCode == UniverseSyncJob.SourceFetchFailed);
        Assert.Equal("universe-sync", quarantined.JobCode);

        // … and the builder is alerted with the failure reason.
        Assert.Contains(host.Mail.Sent, m =>
            m.Subject.Contains(UniverseSyncJob.SourceFetchFailed, StringComparison.Ordinal));
    }

    /// <summary>
    /// TKT-foundation-022 — the real İş Yatırım universe snapshot (instruments only, no
    /// membership rows, §10.9.8) flows through the production <c>universe-sync</c> job and the
    /// checked-in <see cref="Xu100LaunchSeed"/> fires exactly once, seeding the real BIST 100
    /// membership (01 v1.5 §10.9.3 amendment, FR-MDF-007). The covered-universe read surface
    /// (FR-RES-001) then serves the real symbols — ASELS in <c>GET /api/v1/stocks</c> and
    /// <c>GET /api/v1/stocks/search?q=ASELS</c> (membership/shape asserted, never derived
    /// numbers). A re-run applies nothing (the seed's own provenance marker gates it).
    /// </summary>
    [Fact]
    public async Task TKT_foundation_022_Real_universe_sync_applies_the_xu100_seed_once_and_serves_real_symbols()
    {
        await ResetAsync();
        await CreateXu100InstrumentsAsync();
        _wireMock.Server.ResetMappings();
        _wireMock.StubRealSource(RealSourceCatalog.Universe, path: UniversePath, method: "POST");
        _wireMock.StubRealSource(RealSourceCatalog.IndexLevels, path: IndexLevelsPath);

        await using var host = BuildIngestion(method: "POST", indexLevelsIndex: "XU30");
        var result = await RunAsync(host, "universe-sync");

        Assert.NotEqual(IngestResult.Failed, result.Status);

        await using (var db = _postgres.CreateContext())
        {
            var xu100 = await db.Indices.Where(i => i.Code == "XU100").Select(i => i.Id).SingleAsync();
            var seedRows = await db.IndexConstituents
                .Where(c => c.IndexId == xu100 && c.SourceRef == Xu100LaunchSeed.SourceRef)
                .ToListAsync();

            Assert.Equal(Xu100LaunchSeed.Symbols.Count, seedRows.Count);
            Assert.All(seedRows, row =>
            {
                Assert.Null(row.EffectiveTo);
                Assert.Equal(Xu100LaunchSeed.AsOfDate, row.EffectiveFrom);
            });
        }

        // The read surface now returns the real covered-universe symbols.
        await using (var factory = new DegerliAppFactory(_postgres.ConnectionString))
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false,
            });

            using var list = await client.GetAsync("/api/v1/stocks");
            Assert.Equal(System.Net.HttpStatusCode.OK, list.StatusCode);
            using var listDocument = System.Text.Json.JsonDocument.Parse(await list.Content.ReadAsStringAsync());
            var listedSymbols = listDocument.RootElement.GetProperty("stocks").EnumerateArray()
                .Select(row => row.GetProperty("symbol").GetString())
                .ToList();
            Assert.Contains("ASELS", listedSymbols);

            using var search = await client.GetAsync("/api/v1/stocks/search?q=ASELS");
            Assert.Equal(System.Net.HttpStatusCode.OK, search.StatusCode);
            using var searchDocument = System.Text.Json.JsonDocument.Parse(await search.Content.ReadAsStringAsync());
            var matches = searchDocument.RootElement.GetProperty("stocks").EnumerateArray().ToList();
            var asels = Assert.Single(matches);
            Assert.Equal("ASELS", asels.GetProperty("symbol").GetString());
            Assert.Equal(
                new[] { "symbol", "name" },
                asels.EnumerateObject().Select(p => p.Name).ToArray());
        }

        // A re-run applies the seed nothing further (once-only, keyed on its provenance marker).
        await using (var db = _postgres.CreateContext())
        {
            var xu100 = await db.Indices.Where(i => i.Code == "XU100").Select(i => i.Id).SingleAsync();
            var before = await db.IndexConstituents.CountAsync(c => c.IndexId == xu100);
            var second = await RunAsync(host, "universe-sync");
            Assert.NotEqual(IngestResult.Failed, second.Status);
            Assert.Equal(before, await db.IndexConstituents.CountAsync(c => c.IndexId == xu100));
        }
    }

    /// <summary>
    /// TKT-foundation-022 — a seed symbol that resolves to no stored instrument is reported,
    /// quarantined and counted, never silently dropped (NFR-MDF-003). One member is
    /// deliberately absent from the instrument base; the other 99 seed.
    /// </summary>
    [Fact]
    public async Task TKT_foundation_022_Unknown_seed_symbol_is_quarantined_never_dropped()
    {
        const string unknown = "YKBNK";
        await ResetAsync();
        await CreateXu100InstrumentsAsync(except: unknown);
        _wireMock.Server.ResetMappings();
        _wireMock.StubRealSource(RealSourceCatalog.Universe, path: UniversePath, method: "POST");
        _wireMock.StubRealSource(RealSourceCatalog.IndexLevels, path: IndexLevelsPath);

        await using var host = BuildIngestion(method: "POST", indexLevelsIndex: "XU30");
        var result = await RunAsync(host, "universe-sync");

        Assert.Equal(IngestResult.Partial, result.Status);
        Assert.Equal(1, result.Quarantined);

        await using var db = _postgres.CreateContext();
        var xu100 = await db.Indices.Where(i => i.Code == "XU100").Select(i => i.Id).SingleAsync();
        Assert.Equal(
            Xu100LaunchSeed.Symbols.Count - 1,
            await db.IndexConstituents.CountAsync(c =>
                c.IndexId == xu100 && c.SourceRef == Xu100LaunchSeed.SourceRef));

        var quarantined = await db.QuarantinedFacts.SingleAsync(q => q.ReasonCode == "UNKNOWN_REFERENCE");
        Assert.Equal("universe-sync", quarantined.JobCode);
        Assert.Contains(unknown, quarantined.PayloadJson);

        // The unresolved symbol is logged, never silent.
        Assert.True(
            host.Logs.Contains(evt => evt.RenderMessage().Contains(unknown, StringComparison.Ordinal)),
            $"no log event naming the unresolved seed symbol {unknown}");
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    private UniverseSourceAdapter BuildUniverseAdapter(string path, string method) =>
        new(
            new HttpSourceClient(new HttpClient { BaseAddress = new Uri(_wireMock.BaseUrl) }),
            Options.Create(new UniverseSourceOptions
            {
                BaseUrl = _wireMock.BaseUrl,
                Method = method,
                Path = path,
                RequestBody = """{"endeksKodu":"09","sektorKodu":"","exchange":""}""",
            }));

    private IndexLevelsSourceAdapter BuildIndexLevelsAdapter(string path, string indexCode) =>
        new(
            new HttpSourceClient(new HttpClient { BaseAddress = new Uri(_wireMock.BaseUrl) }),
            Options.Create(new UniverseSourceOptions
            {
                BaseUrl = _wireMock.BaseUrl,
                IndexLevelsPath = path,
                IndexLevelsIndex = indexCode,
            }));

    private IngestionHost BuildIngestion(string method, string indexLevelsIndex)
    {
        var mail = new RecordingMailDispatcher();
        var logs = new InMemorySerilogSink();
        var clock = new FakeTimeProvider(new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _postgres.ConnectionString,
                ["Ingestion:Universe:BaseUrl"] = _wireMock.BaseUrl,
                ["Ingestion:Universe:Method"] = method,
                ["Ingestion:Universe:Path"] = UniversePath,
                ["Ingestion:Universe:IndexLevelsPath"] = IndexLevelsPath,
                ["Ingestion:Universe:IndexLevelsIndex"] = indexLevelsIndex,
            })
            .Build();

        var logger = new LoggerConfiguration().MinimumLevel.Warning().WriteTo.Sink(logs).CreateLogger();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog(logger, dispose: false));
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IIngestionAlerter>(new HarnessMailAlerter(mail));
        services.AddMarketDataIngestion(configuration);

        return new IngestionHost(services.BuildServiceProvider(), mail, logs);
    }

    private static async Task<IngestResult> RunAsync(IngestionHost host, string jobCode)
    {
        using var scope = host.Provider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IIngestionJobRunner>();
        return await runner.RunAsync(jobCode, new IngestionRequest(T));
    }

    /// <summary>
    /// Restores a clean universe baseline and removes the bounded capture's instruments so the
    /// tests in this class are order-independent (each test's job re-creates them).
    /// </summary>
    private async Task ResetAsync()
    {
        await using var db = _postgres.CreateContext();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM quarantined_facts");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM index_levels");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM index_constituents");
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM instruments WHERE symbol IN ('A1CAP','A1YEN','AAGYO','ACSEL','ADEL')");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM instruments WHERE source_ref = 'test:xu100-launch'");
        await FixtureSeeder.ApplyAsync(db, L2);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM index_levels");
        await db.Database.ExecuteSqlRawAsync("UPDATE instruments SET sector_id = NULL");
    }

    /// <summary>Creates the test-local instrument rows the XU100 launch-seed needs (the real
    /// BIST 100 members are outside the FU universe). <paramref name="except"/> deliberately
    /// omits one member so the unknown-symbol path is exercised.</summary>
    private async Task CreateXu100InstrumentsAsync(string? except = null)
    {
        await using var db = _postgres.CreateContext();
        var recordedAt = new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        foreach (var symbol in Xu100LaunchSeed.Symbols)
        {
            if (string.Equals(symbol, except, StringComparison.Ordinal))
            {
                continue;
            }

            db.Instruments.Add(new Instrument
            {
                Symbol = symbol,
                Name = symbol,
                AccountingProfile = "industrial",
                Status = "active",
                SourceRef = "test:xu100-launch",
                RecordedAt = recordedAt,
            });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>The active ingestion provider plus its alert/mail and log doubles.</summary>
    private sealed class IngestionHost : IAsyncDisposable
    {
        public IngestionHost(ServiceProvider provider, RecordingMailDispatcher mail, InMemorySerilogSink logs)
        {
            Provider = provider;
            Mail = mail;
            Logs = logs;
        }

        public ServiceProvider Provider { get; }

        public RecordingMailDispatcher Mail { get; }

        public InMemorySerilogSink Logs { get; }

        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    /// <summary>Funnels the ingestion alert seam into the shared recording mail double.</summary>
    private sealed class HarnessMailAlerter : IIngestionAlerter
    {
        private readonly RecordingMailDispatcher _mail;

        public HarnessMailAlerter(RecordingMailDispatcher mail) => _mail = mail;

        public Task RaiseAsync(IngestionAlert alert, CancellationToken cancellationToken = default) =>
            _mail.SendAsync(
                new OutboundMail("builder@degerli.test", alert.Subject, alert.BodyTr, alert.BodyEn),
                cancellationToken);
    }
}
