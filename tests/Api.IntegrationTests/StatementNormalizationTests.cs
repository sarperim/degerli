using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Jobs;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-mdf-014 acceptance — statement unit-scale and reporting-currency normalization
/// (TC-MDF-055; FR-MDF-002, FR-MDF-008; 02 §3.1/§5.9). The statement ETL stores every
/// <c>fin_line_items.value</c> as raw single-unit TRY: the payload's declared
/// <c>unitScale</c> is applied to every line and a non-TRY reporting currency is then
/// converted at the period-end FX rate read from <c>macro_values</c>. A payload that omits
/// the declaration is refused (<c>SCHEMA_MISMATCH</c>) and a non-TRY payload with no
/// period-end rate is refused (<c>MISSING_FX_RATE</c>) — never converted at a guessed rate.
///
/// The USDX fixture (FU §3b) is on-demand: it is not part of the seeded 16, so each test
/// creates its own state (the USDX instrument) and relies on the FU §7 statement-FX
/// reference rate (USD_TRY 2025-12-31 = 42.00) that <see cref="FixtureSeeder"/> seeds.
/// </summary>
public sealed class StatementNormalizationTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    private const string UsdxSymbol = "USDX";
    private const string UsdxPath = "/canned-sources/statements-usd-reporter";
    private static readonly DateOnly UsdxPeriodEnd = new(2025, 12, 31);

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public StatementNormalizationTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    /// <summary>
    /// TC-MDF-055 (scale × currency × FX-present) — a USD reporter tabulated in millions is
    /// stored as raw single-unit TRY: NI = 10 × 10⁶ × 42.00 = 420,000,000; the share count
    /// scales but is never FX-converted (20 × 10⁶ = 20,000,000); the declaration is recorded
    /// as provenance (<c>source_unit_scale</c>/<c>source_currency</c>).
    /// </summary>
    [Fact]
    public async Task TC_MDF_055_usd_reporting_currency_is_normalized_to_raw_try_at_the_period_end_rate()
    {
        long usdxId = await ResetAsync();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.StatementsUsdReporter);
        await using var provider = BuildIngestion();

        var result = await RunAsync(provider, "statements");

        Assert.Equal(IngestResult.Succeeded, result.Status);
        Assert.Equal(2, result.Written); // FY IS + FY BS
        Assert.Equal(0, result.Quarantined);

        await using var db = _postgres.CreateContext();

        // Precondition (FU §7): the statement-FX reference rate is seeded in macro_values.
        Assert.True(await db.MacroValues.AnyAsync(v =>
            v.SeriesCode == "USD_TRY" && v.ValueDate == UsdxPeriodEnd && v.Value == 42.00m));

        var fyIs = await db.FinancialStatements.SingleAsync(s =>
            s.InstrumentId == usdxId && s.PeriodType == "FY" && s.StatementType == "IS");
        var fyBs = await db.FinancialStatements.SingleAsync(s =>
            s.InstrumentId == usdxId && s.PeriodType == "FY" && s.StatementType == "BS");

        // The declared scale/currency are recorded as provenance, not the normalized result.
        Assert.Equal("millions", fyIs.SourceUnitScale);
        Assert.Equal("USD", fyIs.SourceCurrency);
        Assert.Equal("millions", fyBs.SourceUnitScale);
        Assert.Equal("USD", fyBs.SourceCurrency);

        // NI = 10 USD M × 10⁶ × 42.00 = 420,000,000 raw TRY.
        Assert.Equal(420_000_000m, await LineAsync(db, fyIs.Id, "NI"));
        // REV = 100 × 10⁶ × 42.00 = 4,200,000,000.
        Assert.Equal(4_200_000_000m, await LineAsync(db, fyIs.Id, "REV"));
        // TOTAL_EQUITY = 60 × 10⁶ × 42.00 = 2,520,000,000.
        Assert.Equal(2_520_000_000m, await LineAsync(db, fyBs.Id, "TOTAL_EQUITY"));
        // A share count scales only: SHARES_DILUTED = 20 × 10⁶ = 20,000,000 (no FX).
        Assert.Equal(20_000_000m, await LineAsync(db, fyBs.Id, "SHARES_DILUTED"));
    }

    /// <summary>
    /// TC-MDF-055 (scale × currency declaration absent) — a statement payload that omits
    /// the <c>unitScale</c>/<c>currency</c> declaration is refused with <c>SCHEMA_MISMATCH</c>
    /// and nothing is written (02 §3.1 — the declaration is mandatory, never guessed).
    /// </summary>
    [Fact]
    public async Task TC_MDF_055_payload_without_scale_or_currency_declaration_is_quarantined_schema_mismatch()
    {
        long usdxId = await ResetAsync();

        // The same USDX statement, with the scale/currency declaration omitted.
        const string body = """
            {
              "sourceRef": "kap://statements/USDX",
              "symbol": "USDX",
              "statements": [
                {
                  "periodType": "FY",
                  "periodEndDate": "2025-12-31",
                  "fiscalYear": 2025,
                  "statementType": "IS",
                  "version": "as_reported",
                  "publishedAt": "2026-03-10",
                  "lines": { "NI": 10, "REV": 100 }
                }
              ]
            }
            """;
        _wireMock.Server.ResetMappings();
        _wireMock.StubJson(UsdxPath, body);
        await using var provider = BuildIngestion();

        var result = await RunAsync(provider, "statements");

        Assert.Equal(IngestResult.Partial, result.Status);
        Assert.Equal(0, result.Written);

        await using var db = _postgres.CreateContext();
        Assert.False(await db.FinancialStatements.AnyAsync(s => s.InstrumentId == usdxId));

        var entry = Assert.Single(await db.QuarantinedFacts.Where(q => q.JobCode == "statements").ToListAsync());
        Assert.Equal("SCHEMA_MISMATCH", entry.ReasonCode);
        Assert.Contains("USDX", entry.PayloadJson);
    }

    /// <summary>
    /// TC-MDF-055 (currency × FX-absent) — a USD payload whose period-end FX rate is absent
    /// from <c>macro_values</c> is refused with <c>MISSING_FX_RATE</c>, never converted at a
    /// guessed rate (02 §3.1/§5.9).
    /// </summary>
    [Fact]
    public async Task TC_MDF_055_usd_payload_without_period_end_fx_rate_is_quarantined_missing_fx_rate()
    {
        long usdxId = await ResetAsync();

        // Remove every USD_TRY rate at or before the period end: the normalization has no
        // period-end rate to use.
        await using (var db = _postgres.CreateContext())
        {
            await db.MacroValues
                .Where(v => v.SeriesCode == "USD_TRY" && v.ValueDate <= UsdxPeriodEnd)
                .ExecuteDeleteAsync();
        }

        _wireMock.StubCannedSource(L2, CannedSourceCatalog.StatementsUsdReporter);
        await using var provider = BuildIngestion();

        var result = await RunAsync(provider, "statements");

        Assert.Equal(IngestResult.Partial, result.Status);
        Assert.Equal(0, result.Written);

        await using (var db = _postgres.CreateContext())
        {
            Assert.False(await db.FinancialStatements.AnyAsync(s => s.InstrumentId == usdxId));

            var entry = Assert.Single(await db.QuarantinedFacts.Where(q => q.JobCode == "statements").ToListAsync());
            Assert.Equal("MISSING_FX_RATE", entry.ReasonCode);
            Assert.Contains("USDX", entry.PayloadJson);
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
                ["Ingestion:Kap:BaseUrl"] = _wireMock.BaseUrl,
                ["Ingestion:Statements:Path"] = UsdxPath,
                // Company-scoped loop bound: the test-local single-member bind keeps the USDX
                // normalization goldens intact (TKT-foundation-024).
                ["Ingestion:Statements:UniverseIndex"] = StatementBound.IndexCode,
                ["Ingestion:Statements:RequestDelayMs"] = "0",
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
    /// Re-seeds the fixture universe, ensures the on-demand USDX instrument exists, and
    /// clears this ticket's mutable state so each test starts clean (FU §12).
    /// </summary>
    private async Task<long> ResetAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);

        if (!await db.Instruments.AnyAsync(i => i.Symbol == UsdxSymbol))
        {
            db.Instruments.Add(new Instrument
            {
                Symbol = UsdxSymbol,
                Name = "Usd Raporlayıcı A.Ş.",
                SectorId = null,
                ListingDate = new DateOnly(2020, 1, 2),
                Status = "active",
                SourceRef = "fixture:universe:instruments",
                RecordedAt = new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            });
            await db.SaveChangesAsync();
        }

        var usdxId = await db.Instruments.Where(i => i.Symbol == UsdxSymbol).Select(i => i.Id).SingleAsync();

        var statementIds = await db.FinancialStatements
            .Where(s => s.InstrumentId == usdxId)
            .Select(s => s.Id)
            .ToListAsync();
        await db.FinLineItems.Where(l => statementIds.Contains(l.StatementId)).ExecuteDeleteAsync();
        await db.FinancialStatements.Where(s => s.InstrumentId == usdxId).ExecuteDeleteAsync();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM quarantined_facts");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");

        await StatementBound.ReplaceAsync(db, UsdxSymbol);

        return usdxId;
    }

    private static async Task<decimal> LineAsync(DegerliDbContext db, long statementId, string itemCode) =>
        await db.FinLineItems
            .Where(l => l.StatementId == statementId && l.ItemCode == itemCode)
            .Select(l => l.Value)
            .SingleAsync();
}
