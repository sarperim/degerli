using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Kap;
using Degerli.Ingestion.Statements;
using Degerli.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-foundation-017 acceptance — the <b>real</b> KAP XBRL financial-statement fact
/// envelope decoded by the production decoder (01 v1.5 §10.9.5, FR-MDF-002). The
/// real-source catalog's constructed-but-shape-faithful KAP bodies are served through the
/// WireMock harness and parsed by <see cref="StatementsSourceAdapter"/> /
/// <see cref="KapXbrlStatementDecoder"/> in <c>src/Ingestion</c> — never a fixture-owned
/// decoder (the DR-01 rule, §10.9.7). The FU golden values are untouched: these cases
/// assert parse shape/fidelity and the duplicate-concept rule, never derived numbers.
/// </summary>
public sealed class RealSourceStatementsTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public RealSourceStatementsTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    /// <summary>
    /// TC-MDF-065 — Turkish-locale KAP numbers parse culture-free through the production
    /// statement decoder: the five tickers' fact bodies decode to their pinned FY2025
    /// revenues and the scalar tolerance edges (en-dash minus, accounting parentheses,
    /// trailing <c>%</c>, NBSP grouping) hold exactly.
    /// </summary>
    [Fact]
    public async Task TC_MDF_065_Turkish_locale_kap_numbers_parse_culture_free_through_the_production_decoder()
    {
        // The production parser: '.' is thousands, ',' the decimal; tolerance edges.
        Assert.Equal(-1_234.50m, KapXbrlStatementDecoder.ParseTurkishNumber("-1.234,50"));
        Assert.Equal(-1_234.50m, KapXbrlStatementDecoder.ParseTurkishNumber("–1.234,50")); // en-dash (U+2013)
        Assert.Equal(-1_234.50m, KapXbrlStatementDecoder.ParseTurkishNumber("(1.234,50)")); // accounting parentheses
        Assert.Equal(456_000_000m, KapXbrlStatementDecoder.ParseTurkishNumber("456.000.000"));
        Assert.Equal(12.5m, KapXbrlStatementDecoder.ParseTurkishNumber("12,5%")); // trailing %
        Assert.Equal(1_234_567.89m, KapXbrlStatementDecoder.ParseTurkishNumber("1\u00A0234\u00A0567,89")); // NBSP grouping

        var expected = new (string Payload, decimal Revenue)[]
        {
            (RealSourceCatalog.StatementsAsels, 1_234_567.89m),
            (RealSourceCatalog.StatementsThyao, 2_345_678_901.23m),
            (RealSourceCatalog.StatementsGaran, 3_456_789_012.34m),
            (RealSourceCatalog.StatementsFroto, 4_567_890_123.45m),
            (RealSourceCatalog.StatementsEbebk, 5_678_901_234.56m),
        };

        foreach (var (payloadName, revenue) in expected)
        {
            var payload = _wireMock.StubRealSource(payloadName);
            var fetched = await BuildAdapter(payload.DefaultPath).FetchAsync(T);

            var fy2025 = GroupFor(fetched.Payload, "IS", 2025);
            Assert.Equal(revenue, fy2025.Lines["REV"]);
        }

        // The tolerance-edge variant flows through the decoder: one distinct exemplar per edge.
        var edges = _wireMock.StubRealSource(RealSourceCatalog.StatementsToleranceEdges);
        var edgesFetched = await BuildAdapter(edges.DefaultPath).FetchAsync(T);
        var lines = GroupFor(edgesFetched.Payload, "IS", 2025).Lines;

        Assert.Equal(-1_234.50m, lines["OTHER_kap:EdgeParentheses"]);
        Assert.Equal(-1_234.50m, lines["OTHER_kap:EdgeEnDash"]);
        Assert.Equal(-1_234.50m, lines["OTHER_kap:EdgeHyphen"]);
        Assert.Equal(1_234_567.89m, lines["OTHER_kap:EdgeNbsp"]);
        Assert.Equal(12.5m, lines["OTHER_kap:EdgePercent"]);
        Assert.Equal(456_000_000m, lines["OTHER_kap:EdgePlain"]);
    }

    /// <summary>
    /// TC-MDF-067 — the KAP XBRL concept map, fact grouping and <c>OTHER_*</c> passthrough
    /// through the production decoder: inconsistent IFRS/TR/KAP spellings collapse onto one
    /// canonical item code, <c>GROSS_PROFIT</c> is derived when not reported, facts group by
    /// (periodType, periodEndDate, statementType, version), unmapped concepts pass through as
    /// <c>OTHER_*</c> lines, and the envelope carries <c>unitScale</c>/<c>currency</c>.
    /// </summary>
    [Fact]
    public async Task TC_MDF_067_Kap_xbrl_concept_map_grouping_and_other_passthrough()
    {
        // The three inconsistent spellings for the same line all map to REV.
        Assert.Equal("REV", KapXbrlStatementDecoder.ConceptMap["ifrs-full:Revenue"]);
        Assert.Equal("REV", KapXbrlStatementDecoder.ConceptMap["tr:Hasilat"]);
        Assert.Equal("REV", KapXbrlStatementDecoder.ConceptMap["kap:NetSatisTutari"]);

        var payload = _wireMock.StubRealSource(RealSourceCatalog.StatementsAsels);
        var fetched = await BuildAdapter(payload.DefaultPath).FetchAsync(T);

        Assert.Equal("kap", fetched.SourceRef);
        Assert.Equal("ASELS", fetched.Payload.Symbol);
        Assert.Equal("units", fetched.Payload.UnitScale);
        Assert.Equal("TRY", fetched.Payload.Currency);

        // `ifrs-full:Revenue` (FY2025) and `tr:Hasilat` (FY2024) collapse onto REV.
        var fy2025 = GroupFor(fetched.Payload, "IS", 2025);
        var fy2024 = GroupFor(fetched.Payload, "IS", 2024);
        Assert.Equal(1_234_567.89m, fy2025.Lines["REV"]);
        Assert.Equal(1_100_000.00m, fy2024.Lines["REV"]);

        // Grouping coordinates are intact for the store's integer version sequencing.
        Assert.Equal("FY", fy2025.PeriodType);
        Assert.Equal(new DateOnly(2025, 12, 31), fy2025.PeriodEndDate);
        Assert.Equal("IS", fy2025.StatementType);
        Assert.Equal("as_reported", fy2025.Version);

        // GROSS_PROFIT is derived from REV − COGS when the source does not report it.
        Assert.Equal(246_913.57m, fy2025.Lines["GROSS_PROFIT"]);

        // An unmapped KAP concept passes through under OTHER_* — recorded, never dropped.
        var balanceSheet = GroupFor(fetched.Payload, "BS", 2025);
        Assert.True(balanceSheet.Lines.TryGetValue("OTHER_kap:OzkaynakDegisimTablosu", out var passthrough));
        Assert.Equal(42_000.00m, passthrough);

        // The KAP spelling decodes onto REV too (GARAN FY2024 carries `kap:NetSatisTutari`).
        var garan = _wireMock.StubRealSource(RealSourceCatalog.StatementsGaran);
        var garanFetched = await BuildAdapter(garan.DefaultPath).FetchAsync(T);
        Assert.Equal(3_000_000_000.00m, GroupFor(garanFetched.Payload, "IS", 2024).Lines["REV"]);
    }

    /// <summary>
    /// TC-MDF-071 — the KAP duplicate-concept rule (DR-05 overturned by §10.9.5): within one
    /// group, the same canonical code appearing twice with <b>equal</b> values collapses
    /// idempotently (re-ingesting the identical payload is a no-op, no quarantine); with
    /// <b>unequal</b> values it is a <c>CONFLICTING_VALUE</c> quarantine with both values
    /// retained — never a silent latest-wins — while the group's non-conflicting siblings
    /// still ingest (partial batch).
    /// </summary>
    [Fact]
    public async Task TC_MDF_071_Duplicate_concepts_equal_collapse_unequal_quarantine()
    {
        // (1) Equal duplicates collapse; re-ingesting is an idempotent no-op.
        await ResetAsync();
        var equal = _wireMock.StubRealSource(RealSourceCatalog.StatementsDuplicateEqual);
        await using (var provider = BuildIngestion(equal.DefaultPath))
        {
            var first = await RunAsync(provider, "statements");
            Assert.Equal(IngestResult.Succeeded, first.Status);
            Assert.Equal(1, first.Written);
            Assert.Equal(0, first.Quarantined);

            var second = await RunAsync(provider, "statements");
            Assert.Equal(0, second.Written);
            Assert.Equal(1, second.Unchanged);
            Assert.Equal(0, second.Quarantined);
        }

        await using (var db = _postgres.CreateContext())
        {
            var alfa = await db.Instruments.Where(i => i.Symbol == "ALFA").Select(i => i.Id).SingleAsync();
            var isStatement = await db.FinancialStatements
                .SingleAsync(s => s.InstrumentId == alfa && s.StatementType == "IS");
            var lines = await db.FinLineItems
                .Where(l => l.StatementId == isStatement.Id)
                .ToDictionaryAsync(l => l.ItemCode, l => l.Value);
            Assert.Equal(1_234_567.89m, lines["REV"]);
            Assert.Equal(0, await db.QuarantinedFacts.CountAsync());
        }

        // (2) Unequal duplicates quarantine CONFLICTING_VALUE with both values retained; the
        // non-conflicting BS group ingests normally.
        await ResetAsync();
        var unequal = _wireMock.StubRealSource(RealSourceCatalog.StatementsDuplicateUnequal);
        await using (var provider = BuildIngestion(unequal.DefaultPath))
        {
            var result = await RunAsync(provider, "statements");
            Assert.Equal(IngestResult.Partial, result.Status);
            Assert.Equal(1, result.Written);
            Assert.Equal(1, result.Quarantined);
        }

        await using (var db = _postgres.CreateContext())
        {
            var alfa = await db.Instruments.Where(i => i.Symbol == "ALFA").Select(i => i.Id).SingleAsync();

            // The conflicting IS group is never stored (no silent latest-wins)…
            Assert.Equal(0, await db.FinancialStatements.CountAsync(s => s.InstrumentId == alfa && s.StatementType == "IS"));

            // …while the sibling BS group ingests normally.
            var bs = await db.FinancialStatements
                .SingleAsync(s => s.InstrumentId == alfa && s.StatementType == "BS");
            var bsLines = await db.FinLineItems
                .Where(l => l.StatementId == bs.Id)
                .ToDictionaryAsync(l => l.ItemCode, l => l.Value);
            Assert.Equal(5_000_000.00m, bsLines["TOTAL_EQUITY"]);

            var quarantine = await db.QuarantinedFacts.SingleAsync();
            Assert.Equal("CONFLICTING_VALUE", quarantine.ReasonCode);
            Assert.Contains("1.234.567,89", quarantine.PayloadJson);
            Assert.Contains("9.999,99", quarantine.PayloadJson);
        }
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    private StatementsSourceAdapter BuildAdapter(string path)
    {
        var http = new HttpClient { BaseAddress = new Uri(_wireMock.BaseUrl) };
        return new StatementsSourceAdapter(
            new KapHttpSourceClient(http),
            Options.Create(new StatementsSourceOptions { Path = path }));
    }

    private static StatementFact GroupFor(StatementsPayload payload, string statementType, int periodEndYear) =>
        payload.Statements.Single(s => s.StatementType == statementType && s.PeriodEndDate.Year == periodEndYear);

    private ServiceProvider BuildIngestion(string statementsPath)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _postgres.ConnectionString,
                ["Ingestion:Kap:BaseUrl"] = _wireMock.BaseUrl,
                ["Ingestion:Statements:Path"] = statementsPath,
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

    private async Task ResetAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM fin_line_items");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM financial_statements");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM quarantined_facts");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
    }
}
