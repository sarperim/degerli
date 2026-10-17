using Degerli.Api.IntegrationTests.Harness;
using Degerli.Api.IntegrationTests.Harness.Mail;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.Coverage;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Prices;
using Degerli.Ingestion.Sources;
using Degerli.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Serilog;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-mdf-021 acceptance — the price source contract (TC-MDF-061): the daily-price and
/// index-level source is İş Yatırım (primary, <c>source_ref</c> = <c>isyatirim</c>) with
/// the documented Yahoo Finance <c>.IS</c> contingency (<c>source_ref</c> = <c>yahoo</c>),
/// and every stored <c>daily_prices</c>/<c>index_levels</c> row records its concrete
/// source (FR-MDF-001, FR-MDF-008, FR-MDF-018; 03 §1.2). Each test runs the real job
/// against a migrated Testcontainers PostgreSQL and the FU §10 WireMock source double,
/// composing the same <c>AddMarketDataIngestion</c> registration the API host uses.
/// Asserts the DB outcomes (close/volume, FU §4) are unchanged by the source swap.
/// </summary>
public sealed class PriceSourceContractTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    private const string PricesPath = "/canned-sources/prices-eod";
    private const string YahooPath = "/canned-sources/prices-yahoo";
    private const string BackfillPath = "/canned-sources/prices-history";
    private const string UniversePath = "/canned-sources/universe";
    private const string IndexLevelsPath = "/canned-sources/index-levels";

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public PriceSourceContractTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    /// <summary>TC-MDF-061 — the İş Yatırım primary payload stores İş Yatırım-provenanced
    /// rows and the FU §4 close/volume outcomes are unchanged.</summary>
    [Fact]
    public async Task TC_MDF_061_IsYatirim_primary_payload_records_isyatirim_provenance()
    {
        await ResetPricesAsync();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesOk, path: PricesPath);
        await using var host = BuildIngestion(pricePath: PricesPath);

        var result = await RunAsync(host, "prices", new IngestionRequest(T));

        Assert.Equal("succeeded", result.Status);
        Assert.Equal(16, result.Written);

        await using var db = _postgres.CreateContext();
        var rows = await db.DailyPrices.Where(p => p.PriceDate == T).ToListAsync();
        Assert.Equal(16, rows.Count);

        var symbols = await db.Instruments.ToDictionaryAsync(i => i.Id, i => i.Symbol);
        var expected = L2.Prices.Where(p => p.Date == T).ToDictionary(p => p.Symbol, p => p);
        Assert.All(rows, row =>
        {
            // Each row records its concrete source (FR-MDF-008, 03 §1.2).
            Assert.Equal(PricesSourceAdapter.IsYatirimSource, row.SourceRef);

            // The asserted DB outcome (FU §4) is unchanged by the source swap.
            var fact = expected[symbols[row.InstrumentId]];
            Assert.Equal(fact.CloseRaw, row.CloseRaw);
            Assert.Equal(fact.Volume, row.Volume);
        });
    }

    /// <summary>TC-MDF-061 — the Yahoo Finance <c>.IS</c> contingency payload is parsed
    /// through the same adapter and its rows record <c>yahoo</c>.</summary>
    [Fact]
    public async Task TC_MDF_061_Yahoo_contingency_payload_records_yahoo_provenance()
    {
        await ResetPricesAsync();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesYahooContingency, path: PricesPath);
        await using var host = BuildIngestion(pricePath: PricesPath);

        var result = await RunAsync(host, "prices", new IngestionRequest(T));

        Assert.Equal("succeeded", result.Status);
        Assert.Equal(16, result.Written);

        await using var db = _postgres.CreateContext();
        var rows = await db.DailyPrices.Where(p => p.PriceDate == T).ToListAsync();
        Assert.Equal(16, rows.Count);

        var symbols = await db.Instruments.ToDictionaryAsync(i => i.Id, i => i.Symbol);
        var expected = L2.Prices.Where(p => p.Date == T).ToDictionary(p => p.Symbol, p => p);
        Assert.All(rows, row =>
        {
            Assert.Equal(PricesSourceAdapter.YahooSource, row.SourceRef);
            var fact = expected[symbols[row.InstrumentId]];
            Assert.Equal(fact.CloseRaw, row.CloseRaw);
            // TC-MDF-061 (amended v1.5) — FLG-09(b) (architecture v1.5 §10.9.1): Yahoo's chart
            // volume is share count, a different unit from the stored ₺ turnover — it is stored
            // NULL (honest gap).
            Assert.Null(row.Volume);
        });
    }

    /// <summary>TC-MDF-061 — a mixed-source history keeps each row's concrete source, so
    /// coverage/admin views can break coverage down by source (03 §1.2).</summary>
    [Fact]
    public async Task TC_MDF_061_Mixed_source_history_keeps_each_rows_concrete_source()
    {
        await ResetPricesAsync();
        var tMinus1 = L2.Anchor.TradingDay(-1);

        // T−1 arrives from the primary (İş Yatırım) …
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesOkTminus1, path: PricesPath);
        await using (var primary = BuildIngestion(pricePath: PricesPath))
        {
            Assert.Equal("succeeded", (await RunAsync(primary, "prices", new IngestionRequest(tMinus1))).Status);
        }

        // … T arrives from the contingency (Yahoo), through the same adapter.
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesYahooContingency, path: YahooPath);
        await using (var contingency = BuildIngestion(pricePath: YahooPath))
        {
            Assert.Equal("succeeded", (await RunAsync(contingency, "prices", new IngestionRequest(T))).Status);
        }

        await using var db = _postgres.CreateContext();

        var priorDay = await db.DailyPrices.Where(p => p.PriceDate == tMinus1).ToListAsync();
        Assert.Equal(16, priorDay.Count);
        Assert.All(priorDay, row => Assert.Equal(PricesSourceAdapter.IsYatirimSource, row.SourceRef));

        var latestDay = await db.DailyPrices.Where(p => p.PriceDate == T).ToListAsync();
        Assert.Equal(16, latestDay.Count);
        Assert.All(latestDay, row => Assert.Equal(PricesSourceAdapter.YahooSource, row.SourceRef));

        // Both sources' rows coexist — no cross-source overwrite of the same instrument.
        Assert.Equal(32, await db.DailyPrices.CountAsync(p => p.PriceDate == tMinus1 || p.PriceDate == T));
    }

    /// <summary>TC-MDF-061 — index-level rows are provenance-stamped with their concrete
    /// source (İş Yatırım).</summary>
    [Fact]
    public async Task TC_MDF_061_Index_levels_rows_are_provenance_stamped()
    {
        await ResetUniverseAsync();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.UniverseMissingSector, path: UniversePath);
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.IndexLevelsOk, path: IndexLevelsPath);
        await using var host = BuildIngestion(universePath: UniversePath, indexLevelsPath: IndexLevelsPath);

        var result = await RunAsync(host, "universe-sync", new IngestionRequest(T));

        Assert.NotEqual(IngestResult.Failed, result.Status);

        await using var db = _postgres.CreateContext();
        var indexIds = await db.Indices.ToDictionaryAsync(i => i.Code, i => i.Id);

        // The `MISSING_CLASSIFICATION` anomaly (UNSEC) makes the job partial by design;
        // the index levels still sync and are provenance-stamped.
        var xu100 = await db.IndexLevels.SingleAsync(l => l.IndexId == indexIds["XU100"] && l.LevelDate == T);
        Assert.Equal(10200.00m, xu100.Close);
        Assert.Equal(PricesSourceAdapter.IsYatirimSource, xu100.SourceRef);

        var xu30 = await db.IndexLevels.SingleAsync(l => l.IndexId == indexIds["XU30"] && l.LevelDate == T);
        Assert.Equal(30600.00m, xu30.Close);
        Assert.Equal(PricesSourceAdapter.IsYatirimSource, xu30.SourceRef);
    }

    /// <summary>TC-MDF-061 — the backfill run records the achieved depth and stamps the
    /// concrete source on the historical rows.</summary>
    [Fact]
    public async Task TC_MDF_061_Backfill_records_achieved_depth_and_provenance()
    {
        await ResetPricesAsync();
        var sourceLimit = CannedSourceCatalog.BackfillSourceLimit;
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesBackfillSourceLimited, path: BackfillPath);
        await using var host = BuildIngestion(pricePath: PricesPath, backfillPath: BackfillPath);

        var result = await RunAsync(host, "prices", new IngestionRequest(T, new DateOnly(2016, 1, 1)));

        Assert.Equal("succeeded", result.Status);
        Assert.True(result.Written > 0, "the backfill must ingest the history the source provides");

        await using var db = _postgres.CreateContext();

        var atLimit = await db.DailyPrices.Where(p => p.PriceDate == sourceLimit).ToListAsync();
        Assert.Equal(16, atLimit.Count);
        Assert.All(atLimit, row => Assert.Equal(PricesSourceAdapter.IsYatirimSource, row.SourceRef));
        Assert.Equal(0, await db.DailyPrices.CountAsync(p => p.PriceDate < sourceLimit));

        var alfa = await db.Instruments.Where(i => i.Symbol == "ALFA").Select(i => i.Id).SingleAsync();
        var coverage = await db.CoverageMetadata.SingleAsync(
            c => c.Scope == "instrument" && c.InstrumentId == alfa && c.DataType == CoverageDataTypes.Prices);
        Assert.Equal(sourceLimit, coverage.AvailableFrom);
        Assert.Equal(T, coverage.AvailableTo);
    }

    /// <summary>TC-MDF-070 — a real-shaped envelope reporting a transport failure
    /// (<c>ok:false</c>) is a source failure, not a parse event: the job run fails, nothing
    /// is stored and nothing is quarantined (quarantine applies to parse/validation events).</summary>
    [Fact]
    public async Task TC_MDF_070_Transport_failure_job_side_is_a_failed_run_not_a_quarantine()
    {
        await ResetPricesAsync();
        _wireMock.StubRealSource(RealSourceCatalog.PricesOkFalse, path: PricesPath);
        await using var host = BuildIngestion(pricePath: PricesPath);

        await Assert.ThrowsAsync<SourceFetchException>(
            () => RunAsync(host, "prices", new IngestionRequest(T)));

        await using var db = _postgres.CreateContext();
        Assert.Equal(0, await db.QuarantinedFacts.CountAsync());
        Assert.Equal(0, await db.DailyPrices.CountAsync());

        var run = await db.IngestRuns.SingleAsync();
        Assert.Equal(IngestResult.Failed, run.Status);
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// The ingestion composition used in production, retargeted at the Testcontainers
    /// database and the WireMock source double. A recording mail double replaces the
    /// alerting seam; a Serilog test sink captures the log alerts.
    /// </summary>
    private IngestionHost BuildIngestion(
        string? pricePath = null,
        string? backfillPath = null,
        string? universePath = null,
        string? indexLevelsPath = null)
    {
        var mail = new RecordingMailDispatcher();
        var logs = new InMemorySerilogSink();
        var clock = new FakeTimeProvider(new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _postgres.ConnectionString,
            ["Ingestion:Prices:BaseUrl"] = _wireMock.BaseUrl,
            ["Ingestion:Prices:Path"] = pricePath ?? PricesPath,
            ["Ingestion:Prices:BackfillPath"] = backfillPath ?? BackfillPath,
        };
        if (universePath is not null)
        {
            settings["Ingestion:Universe:Path"] = universePath;
        }

        if (indexLevelsPath is not null)
        {
            settings["Ingestion:Universe:IndexLevelsPath"] = indexLevelsPath;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var logger = new LoggerConfiguration().MinimumLevel.Warning().WriteTo.Sink(logs).CreateLogger();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog(logger, dispose: false));
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IIngestionAlerter>(new HarnessMailAlerter(mail));
        services.AddMarketDataIngestion(configuration);

        return new IngestionHost(services.BuildServiceProvider(), mail, logs);
    }

    private static async Task<IngestResult> RunAsync(IngestionHost host, string jobCode, IngestionRequest request)
    {
        using var scope = host.Provider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IIngestionJobRunner>();
        return await runner.RunAsync(jobCode, request);
    }

    /// <summary>Applies the FU universe (instruments are the price FKs) and clears the
    /// price/ingest tables so each test re-creates its own state.</summary>
    private async Task ResetPricesAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM quarantined_facts");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM coverage_metadata");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM daily_prices");
    }

    /// <summary>Applies the FU universe, then clears the index-level/membership rows so
    /// the job's own sync is the only writer (proving the job stamps provenance).</summary>
    private async Task ResetUniverseAsync()
    {
        await using var db = _postgres.CreateContext();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM quarantined_facts");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM index_constituents");
        await FixtureSeeder.ApplyAsync(db, L2);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM index_levels");
    }

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
