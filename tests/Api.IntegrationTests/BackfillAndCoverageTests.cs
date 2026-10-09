using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Api.IntegrationTests.Harness.Mail;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.Coverage;
using Degerli.Ingestion.Jobs;
using Degerli.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Serilog;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-mdf-007 acceptance — backfill mode and coverage-metadata recording
/// (TC-MDF-016, TC-MDF-018). Every test runs the real job against a migrated
/// Testcontainers PostgreSQL and the FU §10 WireMock source double, composing the same
/// <c>AddMarketDataIngestion</c> registration the API host uses. The admin HTTP trigger
/// (TC-MDF-015/043) is deliberately out of scope — this ticket owns the job-level
/// backfill semantics and the recording side of FR-MDF-011.
/// </summary>
public sealed class BackfillAndCoverageTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    /// <summary>The source's real history limit — reached before the 10Y target.</summary>
    private static readonly DateOnly SourceLimit = new(2021, 1, 1);

    /// <summary>The requested 10Y backfill target (as far back as the window wants).</summary>
    private static readonly DateOnly Target10Y = new(2016, 1, 1);

    private const string BackfillPath = "/prices/history";

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public BackfillAndCoverageTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    /// <summary>TC-MDF-016 — Source-limited history is recorded, not fabricated.</summary>
    [Fact]
    public async Task TC_MDF_016_Source_limited_history_is_recorded_not_fabricated()
    {
        await ResetAsync();
        _wireMock.Server.ResetMappings();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesBackfillSourceLimited, path: BackfillPath);
        await using var host = BuildIngestion(BackfillPath);

        // Backfill prices with a 10Y target; the source only reaches back to 2021-01-01.
        var result = await RunAsync(host, "prices", new IngestionRequest(T, Target10Y));

        Assert.Equal("succeeded", result.Status);
        Assert.True(result.Written > 0, "the backfill must ingest the history the source provides");

        await using var db = _postgres.CreateContext();

        // History from the source limit forward is stored …
        Assert.True(
            await db.DailyPrices.AnyAsync(p => p.PriceDate == SourceLimit),
            "rows at the source limit must be ingested");

        // … and nothing is fabricated before the limit (the target was NOT reached).
        Assert.Equal(0, await db.DailyPrices.CountAsync(p => p.PriceDate < SourceLimit));

        // Achieved depth is recorded per instrument, with the limitation noted.
        var alfa = await db.Instruments.Where(i => i.Symbol == "ALFA").Select(i => i.Id).SingleAsync();
        var coverage = await db.CoverageMetadata.SingleAsync(
            c => c.Scope == "instrument" && c.InstrumentId == alfa && c.DataType == CoverageDataTypes.Prices);
        Assert.Equal(SourceLimit, coverage.AvailableFrom);
        Assert.Equal(T, coverage.AvailableTo);
        Assert.False(string.IsNullOrWhiteSpace(coverage.Notes), "the source limitation must be recorded as a coverage note");
        Assert.Contains("2021-01-01", coverage.Notes);
        Assert.Contains("2016-01-01", coverage.Notes);

        // Every instrument the source returned history for records its achieved depth.
        Assert.Equal(
            16,
            await db.CoverageMetadata.CountAsync(
                c => c.Scope == "instrument"
                    && c.DataType == CoverageDataTypes.Prices
                    && c.AvailableFrom == SourceLimit));

        // The ledger row carries the backfill mode and its start date (job-level
        // {backfillFrom} semantics; the admin trigger TC-MDF-043 exposes this).
        var run = await db.IngestRuns.SingleAsync(r => r.JobCode == "prices");
        using var stats = JsonDocument.Parse(run.StatsJson!);
        Assert.Equal("backfill", stats.RootElement.GetProperty("mode").GetString());
        Assert.Equal(Target10Y.ToString("yyyy-MM-dd"), stats.RootElement.GetProperty("backfillFrom").GetString());
    }

    /// <summary>TC-MDF-018 — No silent gaps invariant.</summary>
    [Fact]
    public async Task TC_MDF_018_No_silent_gaps_invariant()
    {
        await ResetAsync();

        await using (var write = _postgres.CreateContext())
        {
            // Measurement/recording side of NFR-MDF-003: coverage is measured for every
            // universe instrument across every data type (present depth or explicit gap).
            var recorder = new CoverageRecorder(write);
            await recorder.ReconcileUniverseAsync();
        }

        await using var db = _postgres.CreateContext();

        // The set (instrument × data_type) lacking both data and an explicit
        // coverage/gap record is empty.
        var missing = await MissingPairsAsync(db);
        Assert.Empty(missing);

        // Spot-check the known source gaps are explicit records, never silent drops.
        var lamda = await db.Instruments.Where(i => i.Symbol == "LAMDA").Select(i => i.Id).SingleAsync();
        Assert.True(
            await db.CoverageMetadata.AnyAsync(c =>
                c.Scope == "instrument"
                && c.InstrumentId == lamda
                && c.DataType == CoverageDataTypes.Dividends
                && c.AvailableFrom == null
                && c.Notes != null),
            "LAMDA pays no dividends — an explicit gap record must exist");
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// The <c>(instrument, data_type)</c> pairs that have neither a fact row nor an
    /// explicit coverage/gap record — the no-silent-gaps consistency query
    /// (NFR-MDF-003, TC-MDF-018).
    /// </summary>
    private static async Task<List<(string Symbol, string DataType)>> MissingPairsAsync(DegerliDbContext db)
    {
        var instruments = await db.Instruments
            .Select(i => new { i.Id, i.Symbol })
            .ToListAsync();

        var covered = (await db.CoverageMetadata
                .Where(c => c.Scope == "instrument" && c.InstrumentId != null)
                .Select(c => new { c.InstrumentId, c.DataType })
                .ToListAsync())
            .Select(c => (c.InstrumentId!.Value, c.DataType))
            .ToHashSet();

        var dataByType = new Dictionary<string, HashSet<long>>(StringComparer.Ordinal)
        {
            [CoverageDataTypes.Prices] = (await db.DailyPrices.Select(p => p.InstrumentId).Distinct().ToListAsync()).ToHashSet(),
            [CoverageDataTypes.Statements] = (await db.FinancialStatements.Select(s => s.InstrumentId).Distinct().ToListAsync()).ToHashSet(),
            [CoverageDataTypes.Dividends] = (await db.Dividends.Select(d => d.InstrumentId).Distinct().ToListAsync()).ToHashSet(),
            [CoverageDataTypes.CorporateActions] = (await db.CorporateActions.Select(a => a.InstrumentId).Distinct().ToListAsync()).ToHashSet(),
            [CoverageDataTypes.Disclosures] = (await db.KapDisclosures.Where(k => k.InstrumentId != 0).Select(k => k.InstrumentId).Distinct().ToListAsync()).ToHashSet(),
        };

        var missing = new List<(string Symbol, string DataType)>();
        foreach (var instrument in instruments)
        {
            foreach (var dataType in CoverageDataTypes.InstrumentScoped)
            {
                var hasData = dataByType[dataType].Contains(instrument.Id);
                var hasRecord = covered.Contains((instrument.Id, dataType));
                if (!hasData && !hasRecord)
                {
                    missing.Add((instrument.Symbol, dataType));
                }
            }
        }

        return missing;
    }

    private IngestionHost BuildIngestion(string backfillPath)
    {
        var mail = new RecordingMailDispatcher();
        var logs = new InMemorySerilogSink();
        var clock = new FakeTimeProvider(new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _postgres.ConnectionString,
                ["Ingestion:Prices:BaseUrl"] = _wireMock.BaseUrl,
                ["Ingestion:Prices:Path"] = "/prices/eod",
                ["Ingestion:Prices:BackfillPath"] = backfillPath,
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

    private static async Task<IngestResult> RunAsync(IngestionHost host, string jobCode, IngestionRequest request)
    {
        using var scope = host.Provider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IIngestionJobRunner>();
        return await runner.RunAsync(jobCode, request);
    }

    /// <summary>Applies the FU universe (instruments are the fact FKs), then clears the
    /// ingestion/coverage tables so each test re-creates its own state.</summary>
    private async Task ResetAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM quarantined_facts");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM coverage_metadata");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM daily_prices");
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
