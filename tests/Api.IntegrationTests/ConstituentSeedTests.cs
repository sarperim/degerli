using Degerli.Api.IntegrationTests.Harness;
using Degerli.Api.IntegrationTests.Harness.Mail;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.Coverage;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Universe;
using Degerli.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Serilog;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-mdf-022 acceptance — the universe-membership strategy (TC-MDF-062, FR-MDF-007/
/// FR-MDF-011, BR-MDF-005/BR-MDF-007). The current BIST 100 membership is seeded at
/// launch (<c>effective_from</c> = launch, <c>effective_to</c> NULL) append-only; the
/// absent pre-launch constituent history is recorded as an explicit <c>coverage_metadata</c>
/// universe-scope gap (never a silent absence); and subsequent constituent changes keep
/// logging append-only through the daily <c>universe-sync</c> job without mutating the
/// seeded rows. Runs against a migrated Testcontainers PostgreSQL and the FU §10 WireMock
/// source double, composing the same <c>AddMarketDataIngestion</c> registration the API
/// host uses.
/// </summary>
public sealed class ConstituentSeedTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    private const string UniversePath = "/canned-sources/universe";
    private const string IndexPath = "/canned-sources/index-levels";

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public ConstituentSeedTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    /// <summary>TC-MDF-062 — constituents launch-seed &amp; pre-launch coverage gap.</summary>
    [Fact]
    public async Task TC_MDF_062_Constituents_launch_seed_and_pre_launch_coverage_gap()
    {
        var launchDate = T;
        var constituents = L2.Memberships
            .Where(m => m.IndexCode == "XU100")
            .Select(m => m.Symbol)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Precondition: the universe reference data (instruments/indices) is seeded and
        // the membership table is empty — no pre-launch constituent history exists.
        await ResetAsync();

        // Step 1 — run the launch seed: record the current constituents at launch and the
        // accepted pre-launch gap.
        var seedRecordedAt = new DateTimeOffset(launchDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        UniverseLaunchSeedResult seed;
        await using (var db = _postgres.CreateContext())
        {
            seed = await new UniverseSyncStore(db).SeedLaunchConstituentsAsync(
                "kap://universe/launch", launchDate, "XU100", constituents, seedRecordedAt);
            await new CoverageRecorder(db).RecordUniverseSeedGapAsync(launchDate);
        }

        Assert.Equal(constituents.Count, seed.Seeded);
        Assert.Equal(0, seed.AlreadyPresent);
        Assert.Empty(seed.UnknownSymbols);

        await using (var db = _postgres.CreateContext())
        {
            var xu100 = await db.Indices.Where(i => i.Code == "XU100").Select(i => i.Id).SingleAsync();
            var memberships = await db.IndexConstituents.Where(c => c.IndexId == xu100).ToListAsync();

            // Seed writes the current membership (effective_to NULL) append-only at launch.
            Assert.Equal(constituents.Count, memberships.Count);
            Assert.All(memberships, m =>
            {
                Assert.Equal(launchDate, m.EffectiveFrom);
                Assert.Null(m.EffectiveTo);
            });

            // The absent pre-launch constituent history is an explicit universe-scope gap.
            var gap = await db.CoverageMetadata.SingleAsync(c => c.Scope == "universe");
            Assert.Null(gap.InstrumentId);
            Assert.Null(gap.FundId);
            Assert.Equal(CoverageDataTypes.Constituents, gap.DataType);
            Assert.Equal(launchDate, gap.AvailableFrom);
            Assert.Null(gap.AvailableTo);
            Assert.False(
                string.IsNullOrWhiteSpace(gap.Notes),
                "the accepted pre-launch constituent gap must be recorded explicitly (BR-MDF-007)");
        }

        // Re-running the seed is an append-only no-op — a seeded row is never re-written.
        await using (var db = _postgres.CreateContext())
        {
            var again = await new UniverseSyncStore(db).SeedLaunchConstituentsAsync(
                "kap://universe/launch", launchDate, "XU100", constituents, seedRecordedAt);
            Assert.Equal(0, again.Seeded);
            Assert.Equal(constituents.Count, again.AlreadyPresent);
        }

        // Step 2 — apply the change set through the daily universe-sync job.
        _wireMock.Server.ResetMappings();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.UniverseAddRemove, path: UniversePath);
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.IndexLevelsOk, path: IndexPath);
        await using var host = BuildIngestion(UniversePath, IndexPath);
        await RunAsync(host, "universe-sync");

        var effective = T.AddDays(1);
        await using (var db = _postgres.CreateContext())
        {
            var xu100 = await db.Indices.Where(i => i.Code == "XU100").Select(i => i.Id).SingleAsync();

            // Forward logging: the removal closes the seeded interval, the addition appends
            // a new open row — both append-only.
            var newp = await db.Instruments.Where(i => i.Symbol == "NEWP").Select(i => i.Id).SingleAsync();
            var newpRow = await db.IndexConstituents.SingleAsync(c => c.IndexId == xu100 && c.InstrumentId == newp);
            Assert.Equal(launchDate, newpRow.EffectiveFrom);
            Assert.Equal(effective, newpRow.EffectiveTo);

            var sigma = await db.Instruments.Where(i => i.Symbol == "SIGMA").Select(i => i.Id).SingleAsync();
            var sigmaRow = await db.IndexConstituents.SingleAsync(c => c.IndexId == xu100 && c.InstrumentId == sigma);
            Assert.Equal(effective, sigmaRow.EffectiveFrom);
            Assert.Null(sigmaRow.EffectiveTo);

            // Every seeded row is retained with its launch start — no overwrite, no erasure.
            var rows = await db.IndexConstituents.Where(c => c.IndexId == xu100).ToListAsync();
            Assert.Equal(constituents.Count + 1, rows.Count);
            foreach (var symbol in constituents)
            {
                var instrumentId = await db.Instruments.Where(i => i.Symbol == symbol).Select(i => i.Id).SingleAsync();
                var row = rows.Single(c => c.InstrumentId == instrumentId);
                Assert.Equal(launchDate, row.EffectiveFrom);
            }

            // The recorded pre-launch gap is untouched by the daily sync.
            var gap = await db.CoverageMetadata.SingleAsync(c => c.Scope == "universe");
            Assert.Equal(launchDate, gap.AvailableFrom);
        }
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    private IngestionHost BuildIngestion(string universePath, string indexPath)
    {
        var mail = new RecordingMailDispatcher();
        var logs = new InMemorySerilogSink();
        var clock = new FakeTimeProvider(new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _postgres.ConnectionString,
                ["Ingestion:Prices:BaseUrl"] = _wireMock.BaseUrl,
                ["Ingestion:Universe:Path"] = universePath,
                ["Ingestion:Universe:IndexLevelsPath"] = indexPath,
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

    /// <summary>Applies the FU universe (reference data + instruments), then clears the
    /// membership/ledger/coverage tables so the launch seed starts from an empty base.</summary>
    private async Task ResetAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM index_constituents");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM quarantined_facts");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM coverage_metadata");
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
