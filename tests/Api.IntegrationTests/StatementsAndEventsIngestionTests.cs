using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Statements;
using Degerli.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-mdf-003 acceptance — the KAP-sourced fact jobs built on the TKT-mdf-002 framework:
/// <c>statements</c> (TC-MDF-003), <c>dividends</c> (TC-MDF-004), <c>corporate-actions</c>
/// (TC-MDF-005) and <c>disclosures</c> (TC-MDF-006). Every test runs the real job against a
/// migrated Testcontainers PostgreSQL and the FU §10 WireMock source double, composing the
/// same <c>AddMarketDataIngestion</c> registration the API host uses.
/// </summary>
public sealed class StatementsAndEventsIngestionTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public StatementsAndEventsIngestionTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    /// <summary>TC-MDF-003 — Statement ingest with canonical mapping and versioned uniqueness.</summary>
    [Fact]
    public async Task TC_MDF_003_Statement_ingest_maps_canonically_and_is_versioned()
    {
        await ResetAsync();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.StatementsOkAlfaFy2025);
        await using var provider = BuildIngestion();

        var result = await RunAsync(provider, "statements");

        Assert.Equal("succeeded", result.Status);

        await using var db = _postgres.CreateContext();
        var alfa = await db.Instruments.Where(i => i.Symbol == "ALFA").Select(i => i.Id).SingleAsync();

        var statements = await db.FinancialStatements.Where(s => s.InstrumentId == alfa).ToListAsync();

        // FY2025 FY + Q1..Q4, each IS/BS/CF, version as_reported.
        Assert.Equal(15, statements.Count);
        Assert.All(statements, s => Assert.Equal("as_reported", s.Version));
        Assert.Equal(3, statements.Count(s => s.PeriodType == "FY"));
        Assert.Equal(4, statements.Count(s => s.PeriodType == "Q" && s.StatementType == "IS"));
        Assert.Equal(4, statements.Count(s => s.PeriodType == "Q" && s.StatementType == "BS"));
        Assert.Equal(4, statements.Count(s => s.PeriodType == "Q" && s.StatementType == "CF"));
        Assert.All(statements, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.SourceRef));
            Assert.NotEqual(default, s.RecordedAt);
        });

        // The FY income statement reports GROSS_PROFIT and EBITDA explicitly.
        var fyIs = statements.Single(s => s.PeriodType == "FY" && s.StatementType == "IS");
        var fyLines = await LinesAsync(db, fyIs.Id);
        Assert.Equal(400m, fyLines["GROSS_PROFIT"]);
        Assert.Equal(400m, fyLines["EBITDA"]);
        Assert.Equal(1000m, fyLines["REV"]);
        Assert.Equal(200m, fyLines["NI"]);

        // Q1 does not report them: they are derived (GROSS_PROFIT = REV − COGS; EBITDA = EBIT + D&A).
        var q1 = statements.Single(s =>
            s.PeriodType == "Q" && s.PeriodEndDate == new DateOnly(2025, 3, 31) && s.StatementType == "IS");
        var q1Lines = await LinesAsync(db, q1.Id);
        Assert.Equal(90m, q1Lines["GROSS_PROFIT"]);
        Assert.Equal(80m, q1Lines["EBITDA"]);

        // All stored item codes are canonical (no unmapped source code leaked through).
        Assert.All(q1Lines.Keys, code => Assert.Contains(code, StatementMapper.CanonicalCodes));

        // UNIQUE (instrument, period, end date, type, version) makes a re-run a no-op.
        var second = await RunAsync(provider, "statements");
        Assert.Equal(0, second.Written);
        Assert.Equal(15, second.Unchanged);
        Assert.Equal(15, await db.FinancialStatements.CountAsync(s => s.InstrumentId == alfa));
    }

    /// <summary>TC-MDF-003 — <c>OTHER_*</c> passthrough is stored under a distinct code,
    /// leaving the canonical vocabulary (and therefore the metrics) untouched.</summary>
    [Fact]
    public void TC_MDF_003_unmapped_source_items_pass_through_as_other_codes()
    {
        var mapped = StatementMapper.Map(new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            ["REV"] = 100m,
            ["KAP_CUSTOM_TAG"] = 7m,
            ["OTHER_ALREADY"] = 9m,
        });

        Assert.Equal(100m, mapped["REV"]);
        Assert.Equal(7m, mapped["OTHER_KAP_CUSTOM_TAG"]);
        Assert.Equal(9m, mapped["OTHER_ALREADY"]);
        Assert.DoesNotContain("KAP_CUSTOM_TAG", mapped.Keys);

        // Derived values are never invented without their inputs.
        Assert.False(mapped.ContainsKey("GROSS_PROFIT"));
        Assert.False(mapped.ContainsKey("EBITDA"));
    }

    /// <summary>TC-MDF-004 — Dividend ingest.</summary>
    [Fact]
    public async Task TC_MDF_004_Dividend_ingest_stores_per_instrument_rows()
    {
        await ResetAsync();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.DividendsOk);
        await using var provider = BuildIngestion();

        var result = await RunAsync(provider, "dividends");

        Assert.Equal("succeeded", result.Status);

        await using var db = _postgres.CreateContext();
        var alfa = await db.Instruments.Where(i => i.Symbol == "ALFA").Select(i => i.Id).SingleAsync();

        // The four TTM quarterly rows FU §6 names: 0.25 TRY each, pay date = ex date + 20d.
        var alfaRows = await db.Dividends.Where(d => d.InstrumentId == alfa).ToListAsync();
        var quarterly = alfaRows.Where(d => d.AmountPerShare == 0.25m).OrderBy(d => d.ExDate).ToList();
        Assert.Equal(4, quarterly.Count);
        Assert.Equal(
            new[]
            {
                new DateOnly(2026, 1, 15),
                new DateOnly(2026, 4, 15),
                new DateOnly(2026, 7, 15),
                new DateOnly(2026, 10, 1),
            },
            quarterly.Select(d => d.ExDate).ToArray());
        Assert.All(quarterly, d =>
        {
            Assert.Equal("TRY", d.Currency);
            Assert.Equal(d.ExDate.AddDays(20), d.PayDate);
        });

        // The other dividend-paying stocks' FU §6 rows are ingested.
        var others = new (string Symbol, decimal Amount)[]
        {
            ("DELTA", 0.20m),
            ("REST", 0.60m),
            ("EPSL", 0.30m),
            ("THETA", 0.4286m),
            ("IOTA", 0.05m),
            ("KAPPA", 0.1333m),
            ("NU", 0.6667m),
        };
        foreach (var (symbol, amount) in others)
        {
            var id = await db.Instruments.Where(i => i.Symbol == symbol).Select(i => i.Id).SingleAsync();
            Assert.True(
                await db.Dividends.AnyAsync(d => d.InstrumentId == id && d.AmountPerShare == amount),
                $"{symbol} dividend {amount} missing");
        }

        // LAMDA has no dividend rows at all (FU §6).
        var lamda = await db.Instruments.Where(i => i.Symbol == "LAMDA").Select(i => i.Id).SingleAsync();
        Assert.Equal(0, await db.Dividends.CountAsync(d => d.InstrumentId == lamda));
    }

    /// <summary>TC-MDF-005 — Corporate action ingest.</summary>
    [Fact]
    public async Task TC_MDF_005_Corporate_action_ingest_stores_terms_json()
    {
        await ResetAsync();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.CorporateActionsOk);
        await using var provider = BuildIngestion();

        var result = await RunAsync(provider, "corporate-actions");

        Assert.Equal("succeeded", result.Status);

        await using var db = _postgres.CreateContext();
        var epsl = await db.Instruments.Where(i => i.Symbol == "EPSL").Select(i => i.Id).SingleAsync();
        var actions = await db.CorporateActions.Where(a => a.InstrumentId == epsl).ToListAsync();

        Assert.Equal(2, actions.Count);

        var split = Assert.Single(actions, a => a.ActionType == "split");
        Assert.Equal(new DateOnly(2025, 6, 2), split.ActionDate);
        Assert.Equal(5m, Terms(split.TermsJson).GetProperty("n").GetDecimal());

        var bonus = Assert.Single(actions, a => a.ActionType == "bonus_issue");
        Assert.Equal(new DateOnly(2026, 3, 2), bonus.ActionDate);
        Assert.Equal(0.1m, Terms(bonus.TermsJson).GetProperty("b").GetDecimal());

        Assert.All(actions, a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.SourceRef));
            Assert.NotEqual(default, a.RecordedAt);
        });
    }

    /// <summary>TC-MDF-006 — KAP disclosure ingest (metadata only; documents on disk).</summary>
    [Fact]
    public async Task TC_MDF_006_Kap_disclosure_ingest_stores_metadata_and_document_path()
    {
        await ResetAsync();
        var documentRoot = CreateFixtureDocumentVolume();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.DisclosuresOk);
        await using var provider = BuildIngestion(documentRoot);

        var result = await RunAsync(provider, "disclosures");

        Assert.Equal("succeeded", result.Status);

        await using var db = _postgres.CreateContext();
        var rows = await db.KapDisclosures.ToListAsync();
        Assert.Equal(3, rows.Count);

        Assert.All(rows, row =>
        {
            Assert.False(string.IsNullOrWhiteSpace(row.DisclosureType));
            Assert.NotNull(row.PublishDate);
            Assert.False(string.IsNullOrWhiteSpace(row.Title));
            Assert.False(string.IsNullOrWhiteSpace(row.SourceUrl));
            Assert.False(string.IsNullOrWhiteSpace(row.SourceRef));
            Assert.NotEqual(default, row.RecordedAt);

            // Metadata only: the document itself is on the fixture volume, referenced by path.
            Assert.False(string.IsNullOrWhiteSpace(row.DocumentPath));
            Assert.True(File.Exists(row.DocumentPath), $"document missing on disk: {row.DocumentPath}");
        });

        // The ingested metadata matches the packaged source payload.
        var alfa = await db.Instruments.Where(i => i.Symbol == "ALFA").Select(i => i.Id).SingleAsync();
        var annual = Assert.Single(rows, r => r.InstrumentId == alfa);
        Assert.Equal("annual_report", annual.DisclosureType);
        Assert.Equal("2025 Faaliyet Raporu", annual.Title);
        Assert.Equal("https://www.kap.org.tr/tr/Bildirim/8841", annual.SourceUrl);
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// The ingestion composition used in production, retargeted at the Testcontainers
    /// database and the WireMock source double. The KAP client points at WireMock; each
    /// job's route selects its named canned payload (FU §10).
    /// </summary>
    private IngestionHost BuildIngestion(string? documentRoot = null)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _postgres.ConnectionString,
            ["Ingestion:Kap:BaseUrl"] = _wireMock.BaseUrl,
            ["Ingestion:Statements:Path"] = "/canned-sources/statements-ok-alfa-fy2025",
            ["Ingestion:Dividends:Path"] = "/canned-sources/dividends-ok",
            ["Ingestion:CorporateActions:Path"] = "/canned-sources/corporate-actions-ok",
            ["Ingestion:Disclosures:Path"] = "/canned-sources/disclosures-ok",
        };
        if (documentRoot is not null)
        {
            settings["Ingestion:Disclosures:DocumentRoot"] = documentRoot;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddMarketDataIngestion(configuration);

        return new IngestionHost(services.BuildServiceProvider());
    }

    private static async Task<IngestResult> RunAsync(IngestionHost host, string jobCode)
    {
        using var scope = host.Provider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IIngestionJobRunner>();
        return await runner.RunAsync(jobCode, new IngestionRequest(T));
    }

    private async Task ResetAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM fin_line_items");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM financial_statements");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM dividends");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM corporate_actions");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM kap_disclosures");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM quarantined_facts");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
    }

    /// <summary>Creates the fixture document volume the disclosures job points at: one
    /// archived PDF per packaged disclosure (FU §10).</summary>
    private string CreateFixtureDocumentVolume()
    {
        var root = Path.Combine(Path.GetTempPath(), $"degerli-disclosure-fixtures-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        foreach (var id in new[] { 8841L, 8842L, 8843L })
        {
            File.WriteAllText(Path.Combine(root, $"{id}.pdf"), $"%PDF-1.4 fixture document {id}");
        }

        return root;
    }

    private static async Task<Dictionary<string, decimal>> LinesAsync(DegerliDbContext db, long statementId) =>
        await db.FinLineItems
            .Where(l => l.StatementId == statementId)
            .ToDictionaryAsync(l => l.ItemCode, l => l.Value, StringComparer.Ordinal);

    private static JsonElement Terms(string? termsJson)
    {
        Assert.False(string.IsNullOrWhiteSpace(termsJson));
        using var document = JsonDocument.Parse(termsJson!);
        return document.RootElement.Clone();
    }

    /// <summary>The active ingestion provider.</summary>
    private sealed class IngestionHost : IAsyncDisposable
    {
        public IngestionHost(ServiceProvider provider) => Provider = provider;

        public ServiceProvider Provider { get; }

        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }
}
