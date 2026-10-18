using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Jobs;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Degerli.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-mdf-015 acceptance — the <c>instruments.accounting_profile</c> classification and
/// the financial-statement ingest posture (TC-MDF-056; FR-MDF-002, FR-MDF-012; 02 §3.1/§6.2).
///
/// Bank/insurance statements legitimately lack the industrial lines (<c>REV</c>/<c>COGS</c>/
/// <c>CUR_ASSETS</c> …), so their absence is <em>expected</em>, never a schema failure: the
/// payload ingests without quarantine. The accounting profile is a builder-curated
/// <em>classification</em>, not a source fact — a subsequent <c>universe-sync</c> must not
/// overwrite it (02 §3.1). The BANKA/SIGORTA fixtures are on-demand (FU §3b): they are not
/// one of the seeded 16, so each test creates its own state and never perturbs a
/// universe-wide golden.
/// </summary>
public sealed class AccountingProfileIngestTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    private const string StatementsPath = "/canned-sources/statements";
    private const string UniversePath = "/canned-sources/universe";
    private const string IndexPath = "/canned-sources/index-levels";

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public AccountingProfileIngestTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    /// <summary>
    /// TC-MDF-056 — bank/insurance statements ingest without quarantine; the profile is
    /// recorded and a subsequent universe sync does not overwrite it.
    /// </summary>
    [Fact]
    public async Task TC_MDF_056_bank_and_insurance_statements_ingest_and_profile_is_not_overwritten()
    {
        await ResetAsync();

        // Statements — BANKA (bank-shaped, no REV/COGS/CUR_ASSETS, spurious REV/DEPR_AMORT).
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.StatementsBankBanka, path: StatementsPath);
        await using var host = BuildIngestion();

        var bankaResult = await RunAsync(host, "statements");
        Assert.Equal(IngestResult.Succeeded, bankaResult.Status);
        Assert.Equal(0, bankaResult.Quarantined);

        // SIGORTA (insurance-shaped, same industrial-line absence).
        _wireMock.Server.ResetMappings();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.StatementsInsuranceSigorta, path: StatementsPath);
        var sigortaResult = await RunAsync(host, "statements");
        Assert.Equal(IngestResult.Succeeded, sigortaResult.Status);
        Assert.Equal(0, sigortaResult.Quarantined);

        await using (var db = _postgres.CreateContext())
        {
            // Absent industrial lines are expected, not a schema failure: nothing quarantined.
            Assert.Empty(await db.QuarantinedFacts.Where(q => q.JobCode == "statements").ToListAsync());

            var bankaId = await db.Instruments.Where(i => i.Symbol == "BANKA").Select(i => i.Id).SingleAsync();
            var sigortaId = await db.Instruments.Where(i => i.Symbol == "SIGORTA").Select(i => i.Id).SingleAsync();

            // Both statements were stored (no line-absence refusal).
            Assert.True(await db.FinancialStatements.AnyAsync(s => s.InstrumentId == bankaId));
            Assert.True(await db.FinancialStatements.AnyAsync(s => s.InstrumentId == sigortaId));

            // The stored lines survive as reported (bank: NI 300, the spurious REV 500 and DEPR_AMORT 50).
            var bankaIs = await db.FinancialStatements.SingleAsync(s =>
                s.InstrumentId == bankaId && s.StatementType == "IS");
            Assert.Equal(300m, await LineAsync(db, bankaIs.Id, "NI"));
            Assert.Equal(500m, await LineAsync(db, bankaIs.Id, "REV"));
            Assert.Equal(50m, await LineAsync(db, bankaIs.Id, "DEPR_AMORT"));

            // The profile is recorded as a classification.
            Assert.Equal(AccountingProfiles.Bank, await db.Instruments.Where(i => i.Symbol == "BANKA").Select(i => i.AccountingProfile).SingleAsync());
            Assert.Equal(AccountingProfiles.Insurance, await db.Instruments.Where(i => i.Symbol == "SIGORTA").Select(i => i.AccountingProfile).SingleAsync());
        }

        // Run the universe sync afterwards — its payload carries BANKA/SIGORTA and even moves
        // their sector link, so the sync genuinely updates the rows.
        var universeBody = JsonSerializer.Serialize(new
        {
            sourceRef = "kap://universe",
            instruments = new[]
            {
                new { symbol = "BANKA", name = "Banka Fixture A.Ş.", sectorCode = "B", listingDate = "2010-01-04" },
                new { symbol = "SIGORTA", name = "Sigorta Fixture A.Ş.", sectorCode = "B", listingDate = "2010-01-04" },
            },
        });
        _wireMock.Server.ResetMappings();
        _wireMock.StubJson(UniversePath, universeBody);
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.IndexLevelsOk, path: IndexPath);

        var syncResult = await RunAsync(host, "universe-sync");
        Assert.NotEqual(IngestResult.Failed, syncResult.Status);

        await using (var db = _postgres.CreateContext())
        {
            // The sync updated the instruments (their sector moved) …
            var sectorB = await db.Sectors.Where(s => s.Code == "B").Select(s => s.Id).SingleAsync();
            Assert.Equal(sectorB, await db.Instruments.Where(i => i.Symbol == "BANKA").Select(i => i.SectorId).SingleAsync());

            // … yet the accounting profile is untouched: it is a classification, not a source fact.
            Assert.Equal(AccountingProfiles.Bank, await db.Instruments.Where(i => i.Symbol == "BANKA").Select(i => i.AccountingProfile).SingleAsync());
            Assert.Equal(AccountingProfiles.Insurance, await db.Instruments.Where(i => i.Symbol == "SIGORTA").Select(i => i.AccountingProfile).SingleAsync());

            // The seeded 16 keep the `industrial` default.
            foreach (var instrument in L2.Instruments)
            {
                var profile = await db.Instruments
                    .Where(i => i.Symbol == instrument.Symbol)
                    .Select(i => i.AccountingProfile)
                    .SingleAsync();
                Assert.Equal(AccountingProfiles.Industrial, profile);
            }
        }
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    private ServiceProvider BuildIngestion()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _postgres.ConnectionString,
                ["Ingestion:Prices:BaseUrl"] = _wireMock.BaseUrl,
                ["Ingestion:Kap:BaseUrl"] = _wireMock.BaseUrl,
                ["Ingestion:Statements:Path"] = StatementsPath,
                // Company-scoped loop bound: the test-local bind carries BANKA/SIGORTA so the
                // bank/insurance ingest goldens stay intact (TKT-foundation-024).
                ["Ingestion:Statements:UniverseIndex"] = StatementBound.IndexCode,
                ["Ingestion:Statements:RequestDelayMs"] = "0",
                ["Ingestion:Universe:Path"] = UniversePath,
                ["Ingestion:Universe:IndexLevelsPath"] = IndexPath,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddMarketDataIngestion(configuration);

        return services.BuildServiceProvider();
    }

    private static async Task<IngestResult> RunAsync(IServiceProvider provider, string jobCode)
    {
        using var scope = provider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IIngestionJobRunner>();
        return await runner.RunAsync(jobCode, new IngestionRequest(T));
    }

    /// <summary>
    /// Re-seeds the fixture universe, ensures the on-demand BANKA/SIGORTA instruments exist
    /// (classified from the checked-in list), and clears this ticket's mutable state so each
    /// test starts clean (FU §12).
    /// </summary>
    private async Task ResetAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);

        foreach (var symbol in new[] { "BANKA", "SIGORTA" })
        {
            if (await db.Instruments.AnyAsync(i => i.Symbol == symbol))
            {
                continue;
            }

            db.Instruments.Add(new Instrument
            {
                Symbol = symbol,
                Name = $"{symbol} Fixture A.Ş.",
                SectorId = null,
                // The builder-curated classification for the on-demand financial fixtures
                // (FU §3b): BANKA is a bank, SIGORTA an insurer.
                AccountingProfile = symbol == "BANKA" ? AccountingProfiles.Bank : AccountingProfiles.Insurance,
                ListingDate = new DateOnly(2010, 1, 4),
                Status = "active",
                SourceRef = "fixture:universe:instruments",
                RecordedAt = new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            });
        }

        await db.SaveChangesAsync();

        var ids = await db.Instruments
            .Where(i => i.Symbol == "BANKA" || i.Symbol == "SIGORTA")
            .Select(i => i.Id)
            .ToListAsync();
        var statementIds = await db.FinancialStatements
            .Where(s => ids.Contains(s.InstrumentId))
            .Select(s => s.Id)
            .ToListAsync();
        await db.FinLineItems.Where(l => statementIds.Contains(l.StatementId)).ExecuteDeleteAsync();
        await db.FinancialStatements.Where(s => ids.Contains(s.InstrumentId)).ExecuteDeleteAsync();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM quarantined_facts");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");

        await StatementBound.ReplaceAsync(db, "BANKA", "SIGORTA");
    }

    private static async Task<decimal> LineAsync(DegerliDbContext db, long statementId, string itemCode) =>
        await db.FinLineItems
            .Where(l => l.StatementId == statementId && l.ItemCode == itemCode)
            .Select(l => l.Value)
            .SingleAsync();
}
