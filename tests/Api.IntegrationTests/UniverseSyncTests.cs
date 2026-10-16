using System.Data;
using System.Data.Common;
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

    /// <summary>TKT-mdf-022 — the job's cold-start launch seed records the current BIST 100
    /// membership and the pre-launch coverage gap through the production entry point
    /// (FR-MDF-007/011, BR-MDF-005/007), not just the store.</summary>
    [Fact]
    public async Task Launch_seed_and_pre_launch_gap_are_recorded_by_the_job()
    {
        await ColdStartUniverseAsync();

        var constituents = L2.Memberships
            .Where(m => m.IndexCode == "XU100")
            .Select(m => m.Symbol)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        _wireMock.StubCannedSource(L2, CannedSourceCatalog.UniverseMissingSector, path: UniversePath);
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.IndexLevelsOk, path: IndexPath);
        await using var host = BuildIngestion(UniversePath, IndexPath);

        var result = await RunAsync(host, "universe-sync");

        // The covered universe is seeded; the job does not fail.
        Assert.NotEqual(IngestResult.Failed, result.Status);

        await using var db = _postgres.CreateContext();
        var xu100 = await db.Indices.Where(i => i.Code == "XU100").Select(i => i.Id).SingleAsync();
        var rows = await db.IndexConstituents.Where(c => c.IndexId == xu100).ToListAsync();

        // Current membership is seeded append-only at launch.
        Assert.Equal(constituents.Count, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal(T, row.EffectiveFrom);
            Assert.Null(row.EffectiveTo);
        });

        // The pre-launch constituent-history gap is recorded by the job, explicitly.
        var gap = await db.CoverageMetadata.SingleAsync(c => c.Scope == "universe");
        Assert.Equal(CoverageDataTypes.Constituents, gap.DataType);
        Assert.Equal(T, gap.AvailableFrom);
        Assert.Null(gap.AvailableTo);
        Assert.False(
            string.IsNullOrWhiteSpace(gap.Notes),
            "the accepted pre-launch constituent gap must be recorded (BR-MDF-007)");
    }

    /// <summary>TKT-mdf-022 — a snapshot carrying instruments but no XU100 index reference
    /// degrades to "no seed": the job logs and continues (index levels still processed)
    /// instead of throwing out of the seed and failing the whole run (FR-MDF-007).</summary>
    [Fact]
    public async Task Missing_xu100_index_reference_degrades_the_seed_without_failing_the_job()
    {
        await ColdStartUniverseAsync();

        // A full universe snapshot (no effective date) publishing sectors and instruments
        // but deliberately no XU100 index reference.
        var body = JsonSerializer.Serialize(new
        {
            sourceRef = "kap://universe/no-index",
            sectors = L2.Sectors
                .Select(s => new { code = s.Code, nameTr = s.NameTr, nameEn = s.NameEn })
                .ToList(),
            instruments = L2.Instruments
                .Select(i => new { symbol = i.Symbol, name = i.Name, sectorCode = i.SectorCode, listingDate = i.ListingDate })
                .ToList(),
        }, JsonOptions);
        _wireMock.StubJson(UniversePath, body);
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.IndexLevelsOk, path: IndexPath);
        await using var host = BuildIngestion(UniversePath, IndexPath);

        var result = await RunAsync(host, "universe-sync");

        // The missing index reference must not abort the job (it previously propagated out
        // of SeedLaunchConstituentsAsync and failed the whole run).
        Assert.NotEqual(IngestResult.Failed, result.Status);
        Assert.True(
            host.Logs.Contains(evt => evt.MessageTemplate.Text.Contains("launch seed skipped", StringComparison.Ordinal)),
            "the skipped seed must be logged, never silent");

        await using var db = _postgres.CreateContext();
        Assert.False(await db.Indices.AnyAsync(i => i.Code == "XU100"), "the payload stored no XU100 index");
        Assert.Empty(await db.IndexConstituents.ToListAsync());

        // The rest of the job still ran: the index-levels payload was processed (its
        // XU100/XU30 levels rejected as UNKNOWN_REFERENCE), so the run is not a failure.
        Assert.True(await db.QuarantinedFacts.AnyAsync(q => q.ReasonCode == "UNKNOWN_REFERENCE"));
    }

    /// <summary>
    /// TKT-mdf-015 — the accounting profile is a builder-curated classification, not a
    /// source fact (02 §3.1, TC-MDF-056): a newly-seen instrument is classified from the
    /// checked-in seed, and a source sync that updates an existing instrument never
    /// overwrites its profile.
    /// </summary>
    [Fact]
    public async Task Profile_is_classified_from_the_checked_in_seed_and_never_overwritten_by_a_sync()
    {
        await ResetUniverseAsync();
        await using (var setup = _postgres.CreateContext())
        {
            // Clean any prior run's artefacts, then give an existing instrument a curated,
            // non-industrial profile to prove the sync does not reset it.
            await setup.Database.ExecuteSqlRawAsync("DELETE FROM instruments WHERE symbol = 'GARAN'");
            await setup.Database.ExecuteSqlRawAsync(
                "UPDATE instruments SET accounting_profile = 'insurance' WHERE symbol = 'BETA'");
        }

        // The payload: a new financial-sector member (GARAN — a seed-listed bank) and an
        // existing instrument (BETA) whose sector link genuinely moves (A → C).
        var body = JsonSerializer.Serialize(new
        {
            sourceRef = "kap://universe/profiles",
            instruments = new[]
            {
                new { symbol = "GARAN", name = "Garanti Fixture A.Ş.", sectorCode = "A", listingDate = "2010-01-04" },
                new { symbol = "BETA", name = "Beta Yazılım A.Ş.", sectorCode = "C", listingDate = "2012-05-10" },
            },
        }, JsonOptions);
        const string profilePath = "/canned-sources/universe-profiles";
        _wireMock.StubJson(profilePath, body);
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.IndexLevelsOk, path: IndexPath);
        await using var host = BuildIngestion(profilePath, IndexPath);

        await RunAsync(host, "universe-sync");

        await using (var db = _postgres.CreateContext())
        {
            // The new member is classified from the checked-in list.
            Assert.Equal(
                Degerli.Persistence.Seed.AccountingProfiles.Bank,
                await db.Instruments.Where(i => i.Symbol == "GARAN").Select(i => i.AccountingProfile).SingleAsync());

            // The existing member was updated (its sector moved) yet keeps its profile.
            var sectorC = await db.Sectors.Where(s => s.Code == "C").Select(s => s.Id).SingleAsync();
            var beta = await db.Instruments.SingleAsync(i => i.Symbol == "BETA");
            Assert.Equal(sectorC, beta.SectorId);
            Assert.Equal("insurance", beta.AccountingProfile);
        }

        // Restore the shared baseline.
        await using (var cleanup = _postgres.CreateContext())
        {
            await cleanup.Database.ExecuteSqlRawAsync("DELETE FROM instruments WHERE symbol = 'GARAN'");
            await cleanup.Database.ExecuteSqlRawAsync(
                "UPDATE instruments SET accounting_profile = 'industrial' WHERE symbol = 'BETA'");
        }
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

    /// <summary>
    /// Cold-start baseline for the launch-seed tests: the fixture instrument base is
    /// present, but the membership history, index reference rows and coverage rows are
    /// empty, so the job's own cold-start seed path is what runs (FR-MDF-007).
    /// </summary>
    private async Task ColdStartUniverseAsync()
    {
        await using var db = _postgres.CreateContext();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM quarantined_facts");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM coverage_metadata");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM index_levels");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM index_constituents");
        await FixtureSeeder.ApplyAsync(db, L2);

        // FixtureSeeder re-seeds the membership history, the index levels and the index
        // references; clear them again so the job is the only writer and the seed truly
        // starts cold. Delete the indices' dependents first so the FK constraints hold.
        await db.Database.ExecuteSqlRawAsync("DELETE FROM index_constituents");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM index_levels");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM indices");
        await db.Database.ExecuteSqlRawAsync("UPDATE instruments SET sector_id = NULL");
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
