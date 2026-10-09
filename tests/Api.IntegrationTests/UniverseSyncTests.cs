using System.Data;
using System.Data.Common;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Api.IntegrationTests.Harness.Mail;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Alerting;
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
/// TKT-mdf-004 acceptance — the <c>universe-sync</c> job: instruments, sectors
/// (bilingual labels, 2-level hierarchy), indices, effective-dated append-only
/// <c>index_constituents</c>, <c>index_levels</c> sync, sector reclassification and
/// unclassified-sector anomaly flagging (TC-MDF-007, TC-MDF-025, TC-MDF-037,
/// TC-MDF-038). Every test runs the real job against a migrated Testcontainers
/// PostgreSQL and the FU §10 WireMock source double, composing the same
/// <c>AddMarketDataIngestion</c> registration the API host uses.
/// </summary>
public sealed class UniverseSyncTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    // Distinct stubbed routes: the job reads one universe path + one index-levels
    // path per run, and a test may re-run the job against a different payload.
    private const string UniversePath = "/canned-sources/universe";
    private const string IndexPath = "/canned-sources/index-levels";

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public UniverseSyncTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    /// <summary>TC-MDF-007 — BIST index levels and sector sync.</summary>
    [Fact]
    public async Task TC_MDF_007_Index_levels_and_sector_sync()
    {
        // Clear the fixture-seeded sector/index reference rows so this test proves the
        // job itself syncs them (F1/F6), not the fixture.
        await ResetUniverseAsync(clearReferenceData: true);
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.UniverseMissingSector, path: UniversePath);
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.IndexLevelsOk, path: IndexPath);
        await using var host = BuildIngestion(UniversePath, IndexPath);

        await RunAsync(host, "universe-sync");

        await using var db = _postgres.CreateContext();

        // FU §3 — bilingual sector labels (02 §3.1).
        var sectors = await db.Sectors.ToDictionaryAsync(s => s.Code);
        Assert.Equal("Teknoloji", sectors["A"].NameTr);
        Assert.Equal("Technology", sectors["A"].NameEn);
        Assert.Equal("Sanayi", sectors["B"].NameTr);
        Assert.Equal("Industry", sectors["B"].NameEn);
        Assert.Equal("Gıda", sectors["C"].NameTr);
        Assert.Equal("Food", sectors["C"].NameEn);

        // FU §3 — instruments linked to their sector (UNSEC unclassified).
        var sectorCodeById = sectors.Values.ToDictionary(s => s.Id, s => s.Code);
        var instruments = await db.Instruments.ToDictionaryAsync(i => i.Symbol);
        foreach (var expected in L2.Instruments)
        {
            var stored = instruments[expected.Symbol];
            if (expected.SectorCode is null)
            {
                Assert.Null(stored.SectorId);
            }
            else
            {
                Assert.NotNull(stored.SectorId);
                Assert.Equal(expected.SectorCode, sectorCodeById[stored.SectorId!.Value]);
            }
        }

        // FU §4 — index levels for T.
        var indexIds = await db.Indices.ToDictionaryAsync(i => i.Code, i => i.Id);
        var xu100 = await db.IndexLevels.SingleAsync(l => l.IndexId == indexIds["XU100"] && l.LevelDate == T);
        Assert.Equal(10200.00m, xu100.Close);
        var xu30 = await db.IndexLevels.SingleAsync(l => l.IndexId == indexIds["XU30"] && l.LevelDate == T);
        Assert.Equal(30600.00m, xu30.Close);
    }

    /// <summary>TC-MDF-025 — missing sector classification is flagged, not dropped.</summary>
    [Fact]
    public async Task TC_MDF_025_Missing_sector_classification_is_flagged_not_dropped()
    {
        await ResetUniverseAsync();
        await using (var setup = _postgres.CreateContext())
        {
            // Give UNSEC a (wrong) classification so the sync must remove it and flag.
            var alfaSector = await setup.Sectors.Where(s => s.Code == "A").Select(s => s.Id).SingleAsync();
            await setup.Database.ExecuteSqlRawAsync(
                "UPDATE instruments SET sector_id = {0} WHERE symbol = 'UNSEC'",
                alfaSector);
        }

        _wireMock.StubCannedSource(L2, CannedSourceCatalog.UniverseMissingSector, path: UniversePath);
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.IndexLevelsOk, path: IndexPath);
        await using var host = BuildIngestion(UniversePath, IndexPath);

        await RunAsync(host, "universe-sync");

        await using var db = _postgres.CreateContext();

        // Stored as unclassified — never dropped.
        var unsec = await db.Instruments.SingleAsync(i => i.Symbol == "UNSEC");
        Assert.Null(unsec.SectorId);

        // Still served: present in the current universe.
        var universeSymbols = (await QueryAsync(db, "SELECT symbol FROM v_current_universe"))
            .Select(r => (string)r[0]!)
            .ToList();
        Assert.Contains("UNSEC", universeSymbols);

        // Anomaly flagged in the quarantine store.
        var quarantined = await db.QuarantinedFacts
            .Where(q => q.ReasonCode == "MISSING_CLASSIFICATION")
            .ToListAsync();
        var entry = Assert.Single(quarantined);
        Assert.Equal("universe-sync", entry.JobCode);
        Assert.Contains("UNSEC", entry.PayloadJson);
        Assert.Equal("open", entry.Status);

        // Builder alerted: mail double + log event naming the reason.
        var alert = Assert.Single(host.Mail.Sent);
        Assert.Contains("MISSING_CLASSIFICATION", alert.Subject);
        Assert.True(host.Logs.Contains(evt =>
            evt.MessageTemplate.Text.Contains("MISSING_CLASSIFICATION", StringComparison.Ordinal)));
    }

    /// <summary>TC-MDF-038 — sector classification stays current per instrument.</summary>
    [Fact]
    public async Task TC_MDF_038_Sector_classification_stays_current_per_instrument()
    {
        await ResetUniverseAsync();

        // A canned reclassification: ALFA moves from sector A to sector B.
        var body = JsonSerializer.Serialize(new
        {
            sourceRef = "kap://universe",
            instruments = new[]
            {
                new
                {
                    symbol = "ALFA",
                    name = "Alfa Teknoloji A.Ş.",
                    sectorCode = "B",
                    listingDate = "2010-01-04",
                },
            },
        }, JsonOptions);
        _wireMock.StubJson(UniversePath, body);
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.IndexLevelsOk, path: IndexPath);
        await using var host = BuildIngestion(UniversePath, IndexPath);

        await RunAsync(host, "universe-sync");

        await using var db = _postgres.CreateContext();
        var sectorIdB = await db.Sectors.Where(s => s.Code == "B").Select(s => s.Id).SingleAsync();
        var alfa = await db.Instruments.SingleAsync(i => i.Symbol == "ALFA");
        Assert.Equal(sectorIdB, alfa.SectorId);

        // Bilingual labels intact after the move.
        var sectorB = await db.Sectors.SingleAsync(s => s.Code == "B");
        Assert.Equal("Sanayi", sectorB.NameTr);
        Assert.Equal("Industry", sectorB.NameEn);
    }

    /// <summary>TC-MDF-037 — constituent changes are effective-dated and
    /// history-preserving (member → removed → re-added).</summary>
    [Fact]
    public async Task TC_MDF_037_Constituent_changes_are_effective_dated_and_history_preserving()
    {
        await ResetUniverseAsync();

        _wireMock.StubCannedSource(L2, CannedSourceCatalog.UniverseAddRemove, path: UniversePath);
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.IndexLevelsOk, path: IndexPath);
        await using var host = BuildIngestion(UniversePath, IndexPath);

        await RunAsync(host, "universe-sync");

        var effective = T.AddDays(1);
        await using (var db = _postgres.CreateContext())
        {
            var xu100 = await db.Indices.Where(i => i.Code == "XU100").Select(i => i.Id).SingleAsync();
            var newp = await db.Instruments.Where(i => i.Symbol == "NEWP").Select(i => i.Id).SingleAsync();
            var sigma = await db.Instruments.Where(i => i.Symbol == "SIGMA").Select(i => i.Id).SingleAsync();

            // The removed member's row is closed, never deleted.
            var newpRows = await db.IndexConstituents
                .Where(c => c.IndexId == xu100 && c.InstrumentId == newp)
                .ToListAsync();
            var closed = Assert.Single(newpRows);
            Assert.Equal(effective, closed.EffectiveTo);

            // The addition is a new open row effective on the change date.
            var sigmaRows = await db.IndexConstituents
                .Where(c => c.IndexId == xu100 && c.InstrumentId == sigma)
                .ToListAsync();
            var added = Assert.Single(sigmaRows);
            Assert.Equal(effective, added.EffectiveFrom);
            Assert.Null(added.EffectiveTo);

            // Current universe reflects the change.
            var current = (await QueryAsync(db, "SELECT symbol FROM v_current_universe"))
                .Select(r => (string)r[0]!)
                .ToList();
            Assert.DoesNotContain("NEWP", current);
            Assert.Contains("SIGMA", current);

            // Historical as-of T membership still includes NEWP.
            var asOfT = await db.IndexConstituents
                .AnyAsync(c => c.IndexId == xu100
                    && c.InstrumentId == newp
                    && c.EffectiveFrom <= T
                    && (c.EffectiveTo == null || c.EffectiveTo > T));
            Assert.True(asOfT);
        }

        // Re-add NEWP at T+5 (a fresh change set; the closed interval is never reopened).
        var reAdd = T.AddDays(5);
        var reAddBody = JsonSerializer.Serialize(new
        {
            sourceRef = "kap://universe/changes",
            effectiveDate = reAdd.ToString("yyyy-MM-dd"),
            instruments = Array.Empty<object>(),
            add = new[] { new { indexCode = "XU100", symbol = "NEWP" } },
            remove = Array.Empty<object>(),
        }, JsonOptions);
        const string reAddPath = "/canned-sources/universe-readd";
        _wireMock.StubJson(reAddPath, reAddBody);
        await using var reAddHost = BuildIngestion(reAddPath, IndexPath);

        await RunAsync(reAddHost, "universe-sync");

        await using (var db = _postgres.CreateContext())
        {
            var xu100 = await db.Indices.Where(i => i.Code == "XU100").Select(i => i.Id).SingleAsync();
            var newp = await db.Instruments.Where(i => i.Symbol == "NEWP").Select(i => i.Id).SingleAsync();

            var newpRows = await db.IndexConstituents
                .Where(c => c.IndexId == xu100 && c.InstrumentId == newp)
                .OrderBy(c => c.EffectiveFrom)
                .ToListAsync();
            Assert.Equal(2, newpRows.Count);
            Assert.Equal(effective, newpRows[0].EffectiveTo);

            var reopened = newpRows[1];
            Assert.Equal(reAdd, reopened.EffectiveFrom);
            Assert.Null(reopened.EffectiveTo);

            var current = (await QueryAsync(db, "SELECT symbol FROM v_current_universe"))
                .Select(r => (string)r[0]!)
                .ToList();
            Assert.Contains("NEWP", current);
        }
    }

    /// <summary>F2 — unknown index/instrument references are quarantined, never dropped.</summary>
    [Fact]
    public async Task Unknown_references_are_quarantined_not_dropped()
    {
        await ResetUniverseAsync();

        var effective = T.AddDays(1);
        var body = JsonSerializer.Serialize(new
        {
            sourceRef = "kap://universe/unknown",
            effectiveDate = effective.ToString("yyyy-MM-dd"),
            instruments = Array.Empty<object>(),
            add = new[]
            {
                new { indexCode = "XU999", symbol = "ALFA" },
                new { indexCode = "XU100", symbol = "ZZZZ" },
            },
            remove = new[] { new { indexCode = "XU100", symbol = "NOPE" } },
        }, JsonOptions);
        const string unknownPath = "/canned-sources/universe-unknown";
        _wireMock.StubJson(unknownPath, body);

        var levelsBody = JsonSerializer.Serialize(new
        {
            sourceRef = "kap://index-levels/unknown",
            levels = new[] { new { indexCode = "XU999", date = T, close = 1.00m } },
        }, JsonOptions);
        const string unknownIndexPath = "/canned-sources/index-levels-unknown";
        _wireMock.StubJson(unknownIndexPath, levelsBody);

        await using var host = BuildIngestion(unknownPath, unknownIndexPath);

        var result = await RunAsync(host, "universe-sync");

        // Two adds + one remove + one index level.
        Assert.Equal(4, result.Quarantined);
        Assert.Equal(IngestResult.Partial, result.Status);

        await using var db = _postgres.CreateContext();
        var quarantined = await db.QuarantinedFacts
            .Where(q => q.ReasonCode == "UNKNOWN_REFERENCE")
            .ToListAsync();
        Assert.Equal(4, quarantined.Count);
        Assert.All(quarantined, q => Assert.Equal("universe-sync", q.JobCode));
        Assert.Contains(quarantined, q => q.PayloadJson!.Contains("XU999"));
        Assert.Contains(quarantined, q => q.PayloadJson!.Contains("ZZZZ"));
        Assert.Contains(quarantined, q => q.PayloadJson!.Contains("NOPE"));
    }

    /// <summary>S1 — one payload's remove+re-add and duplicate adds stay consistent:
    /// no double-open row, no silently-kept-closed membership (BR-MDF-007).</summary>
    [Fact]
    public async Task Batch_remove_readd_and_duplicate_add_are_consistent()
    {
        await ResetUniverseAsync();

        var effective = T.AddDays(1);
        var body = JsonSerializer.Serialize(new
        {
            sourceRef = "kap://universe/batch",
            effectiveDate = effective.ToString("yyyy-MM-dd"),
            instruments = new[]
            {
                new
                {
                    symbol = "SIGMA",
                    name = "Sigma Enerji A.Ş.",
                    sectorCode = "B",
                    listingDate = effective.ToString("yyyy-MM-dd"),
                },
            },
            add = new[]
            {
                new { indexCode = "XU100", symbol = "SIGMA" },
                new { indexCode = "XU100", symbol = "SIGMA" },
                new { indexCode = "XU100", symbol = "NEWP" },
            },
            remove = new[] { new { indexCode = "XU100", symbol = "NEWP" } },
        }, JsonOptions);
        const string batchPath = "/canned-sources/universe-batch";
        _wireMock.StubJson(batchPath, body);
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.IndexLevelsOk, path: IndexPath);
        await using var host = BuildIngestion(batchPath, IndexPath);

        await RunAsync(host, "universe-sync");

        await using var db = _postgres.CreateContext();
        var xu100 = await db.Indices.Where(i => i.Code == "XU100").Select(i => i.Id).SingleAsync();
        var sigma = await db.Instruments.Where(i => i.Symbol == "SIGMA").Select(i => i.Id).SingleAsync();
        var newp = await db.Instruments.Where(i => i.Symbol == "NEWP").Select(i => i.Id).SingleAsync();

        // A duplicate add in one payload inserts exactly one open row.
        var sigmaRows = await db.IndexConstituents
            .Where(c => c.IndexId == xu100 && c.InstrumentId == sigma)
            .ToListAsync();
        var sigmaRow = Assert.Single(sigmaRows);
        Assert.Equal(effective, sigmaRow.EffectiveFrom);
        Assert.Null(sigmaRow.EffectiveTo);

        // Remove + re-add in one payload closes the old interval and opens a new one.
        var newpRows = await db.IndexConstituents
            .Where(c => c.IndexId == xu100 && c.InstrumentId == newp)
            .OrderBy(c => c.EffectiveFrom)
            .ToListAsync();
        Assert.Equal(2, newpRows.Count);
        Assert.Equal(effective, newpRows[0].EffectiveTo);
        Assert.Equal(effective, newpRows[1].EffectiveFrom);
        Assert.Null(newpRows[1].EffectiveTo);

        var current = (await QueryAsync(db, "SELECT symbol FROM v_current_universe"))
            .Select(r => (string)r[0]!)
            .ToList();
        Assert.Contains("SIGMA", current);
        Assert.Contains("NEWP", current);
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// The ingestion composition used in production, retargeted at the Testcontainers
    /// database and the WireMock source double. A recording mail double replaces the
    /// alerting seam; a Serilog test sink captures the log alerts.
    /// </summary>
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

    /// <summary>
    /// Restores a clean universe baseline: clears the ingestion tables, drops the
    /// membership history and any test-added instrument, re-seeds the FU memberships
    /// through the one fixture entry point, then removes the index levels and sector
    /// links so each test's job re-establishes them.
    /// </summary>
    private async Task ResetUniverseAsync(bool clearReferenceData = false)
    {
        await using var db = _postgres.CreateContext();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM quarantined_facts");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM index_constituents");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM instruments WHERE symbol = 'SIGMA'");
        await FixtureSeeder.ApplyAsync(db, L2);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM index_levels");
        await db.Database.ExecuteSqlRawAsync("UPDATE instruments SET sector_id = NULL");

        if (clearReferenceData)
        {
            // Drop the fixture-seeded sectors/indices (and their dependents) so the job's
            // own reference-data upsert is the only writer (F1/F6).
            await db.Database.ExecuteSqlRawAsync("DELETE FROM index_constituents");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM indices");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM sectors");
        }
    }

    private static async Task<List<object?[]>> QueryAsync(DegerliDbContext db, string sql)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var rows = new List<object?[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var values = new object[reader.FieldCount];
            reader.GetValues(values);
            rows.Add(values);
        }

        return rows;
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
