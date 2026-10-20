using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.CorporateActions;
using Degerli.Ingestion.Coverage;
using Degerli.Ingestion.Disclosures;
using Degerli.Ingestion.Dividends;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Sources;
using Degerli.Ingestion.Statements;
using Degerli.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using WireMock.Types;

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

        // FY2025 FY + Q1..Q4, each IS/BS/CF, version 0 (as-reported).
        Assert.Equal(15, statements.Count);
        Assert.All(statements, s => Assert.Equal(0, s.Version));
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

    // =================================================================================
    // TKT-foundation-034 — the three fact jobs rewired onto the MKK VYK gateway
    // (01 v1.5 §10.9.15). Every double below mirrors a route that was verified live on
    // 2026-10-20 against https://apigwdev.mkk.com.tr/api/vyk (paced, ≥12 s/call):
    //   GET /disclosures?disclosureIndex={n}&companyId={id}   → DisclosureInfo rows
    //   GET /disclosureDetail/{index}?fileType=data           → eventType/time/link/flatData
    // The bodies are trimmed real captures (BİM disclosure 1138986 `eventType=KP`,
    // Tüpraş disclosure 1138796 `eventType=SA`) — the nesting is the gateway's own,
    // never invented; only the company/symbol tokens are re-pointed at the bound ALFA.
    // =================================================================================

    /// <summary>
    /// TKT-foundation-034 (criterion (i), <c>dividends</c>) — a dividend is a <c>CA</c>
    /// disclosure whose detail carries <c>eventType=KP</c>. The adapter walks the bound
    /// company's real list route, fetches the detail and stores <b>one fact per cash
    /// instalment</b> from the detail's own <c>cashDividendList</c> /
    /// <c>cashDividendPaymentDateList</c> tables (the <c>T</c> aggregate row is not a fact),
    /// with provenance <c>kap</c>.
    /// </summary>
    [Fact]
    public async Task TKT_foundation_034_dividends_vyk_ca_route_ingests_kp_installments()
    {
        await ResetAsync();
        StubVykDirectory();
        StubDisclosureList(("1138986", "CA"), ("1138987", "ODA"));
        StubDetail("1138986", DividendDetailBody());
        _wireMock.StubJson(DetailPath("1138987"), DisclosureDetailBody("1138987", "ODA"));
        await using var provider = BuildIngestion(vyk: true);

        var result = await RunAsync(provider, "dividends");

        Assert.Equal(IngestResult.Succeeded, result.Status);

        await using var db = _postgres.CreateContext();
        var alfa = await db.Instruments.Where(i => i.Symbol == "ALFA").Select(i => i.Id).SingleAsync();
        var rows = await db.Dividends.Where(d => d.InstrumentId == alfa).OrderBy(d => d.ExDate).ToListAsync();

        // Two instalments, not three: the `T` (total) row is a summary, never a fact.
        Assert.Equal(2, rows.Count);
        Assert.Equal(new DateOnly(2023, 6, 14), rows[0].ExDate);
        Assert.Equal(new DateOnly(2023, 6, 16), rows[0].PayDate);
        Assert.Equal(2.0000000m, rows[0].AmountPerShare);
        Assert.Equal("TRY", rows[0].Currency);
        Assert.Equal(new DateOnly(2023, 12, 20), rows[1].ExDate);
        Assert.Equal(new DateOnly(2023, 12, 22), rows[1].PayDate);
        Assert.Equal(3.0000000m, rows[1].AmountPerShare);
        Assert.All(rows, row =>
        {
            Assert.Equal("kap", row.SourceRef);
            Assert.NotEqual(default, row.RecordedAt);
        });

        // The non-dividend (ODA) disclosure is listed but contributes no dividend fact.
        Assert.Equal(0, await db.QuarantinedFacts.CountAsync());
    }

    /// <summary>
    /// TKT-foundation-034 (criterion (i), <c>corporate-actions</c>) — the verified <c>CA</c>
    /// family route plus the detail's <c>eventType</c>/<c>time</c>/<c>flatData</c>: the action
    /// type is the gateway's own code (<c>SA</c> = capital increase/decrease, live-verified),
    /// the date is the disclosure's publish stamp, and the structured terms document is
    /// retained verbatim as the terms JSON (<c>02</c> §1). No adjustment factor is invented
    /// from a code the engine does not know.
    /// </summary>
    [Fact]
    public async Task TKT_foundation_034_corporate_actions_vyk_ca_route_ingests_event_type_and_terms()
    {
        await ResetAsync();
        StubVykDirectory();
        StubDisclosureList(("1138796", "CA"));
        StubDetail("1138796", CapitalIncreaseDetailBody());
        await using var provider = BuildIngestion(vyk: true);

        var result = await RunAsync(provider, "corporate-actions");

        Assert.Equal(IngestResult.Succeeded, result.Status);

        await using var db = _postgres.CreateContext();
        var alfa = await db.Instruments.Where(i => i.Symbol == "ALFA").Select(i => i.Id).SingleAsync();
        var action = Assert.Single(await db.CorporateActions.Where(a => a.InstrumentId == alfa).ToListAsync());

        Assert.Equal("SA", action.ActionType);
        Assert.Equal(new DateOnly(2023, 4, 14), action.ActionDate);
        Assert.Equal("kap", action.SourceRef);

        // The terms document is the detail's own `flatData[0].content.object`, retained verbatim
        // (the gateway nests its fields under `void`), so the adjustment inputs are never lost.
        var terms = Terms(action.TermsJson);
        Assert.Equal(
            "600.00000",
            TermsField(terms, "internalResourcesBonusPercentage").GetProperty("string").GetString());
        Assert.Equal(
            "1926795598",
            TermsField(terms, "targetCapital").GetProperty("object").GetProperty("string").GetString());
        Assert.Equal(0, await db.QuarantinedFacts.CountAsync());
    }

    /// <summary>
    /// TKT-foundation-034 (criterion (i), <c>disclosures</c>) — the list row carries only the
    /// disclosure identity/type/company (its <c>title</c> is the <i>company name</i> and it has
    /// no date, live-verified), so the metadata comes from the detail: <c>subject.tr</c>,
    /// <c>time</c> and the public <c>link</c>. Documents stay out of the DB (no document root
    /// configured → <c>document_path</c> NULL).
    /// </summary>
    [Fact]
    public async Task TKT_foundation_034_disclosures_vyk_route_ingests_detail_metadata()
    {
        await ResetAsync();
        StubVykDirectory();
        StubDisclosureList(("1138986", "CA"));
        StubDetail("1138986", DividendDetailBody());
        await using var provider = BuildIngestion(vyk: true);

        var result = await RunAsync(provider, "disclosures");

        Assert.Equal(IngestResult.Succeeded, result.Status);

        await using var db = _postgres.CreateContext();
        var alfa = await db.Instruments.Where(i => i.Symbol == "ALFA").Select(i => i.Id).SingleAsync();
        var row = Assert.Single(await db.KapDisclosures.Where(k => k.InstrumentId == alfa).ToListAsync());

        Assert.Equal("CA", row.DisclosureType);
        Assert.Equal(new DateOnly(2023, 4, 14), row.PublishDate);
        Assert.Equal("Kar Payı Dağıtım İşlemlerine İlişkin Bildirim", row.Title);
        Assert.Equal("https://www.kap.org.tr/Bildirim/1138986", row.SourceUrl);
        Assert.Equal("kap", row.SourceRef);
        Assert.Null(row.DocumentPath);
        Assert.Equal(0, await db.QuarantinedFacts.CountAsync());
    }

    /// <summary>
    /// TKT-foundation-034 (criterion (i), honest failure) — a <b>404</b> from the gateway is a
    /// source failure, never a fabricated empty success: the run is recorded <c>failed</c> in
    /// the run ledger, the exception surfaces, no fact row is written and nothing is
    /// quarantined as a parse event (the <c>statements</c> postures of §10.9.10/§10.9.11).
    /// </summary>
    [Theory]
    [InlineData("dividends", "dividends")]
    [InlineData("corporate-actions", "corporate_actions")]
    [InlineData("disclosures", "kap_disclosures")]
    public async Task TKT_foundation_034_gateway_404_is_an_honest_source_failure(string jobCode, string table)
    {
        await ResetAsync();
        StubVykDirectory();
        _wireMock.StubJson(DisclosuresPath, """{"code":"ER005","message":"Bildirim bulunamadı. Disclosure not found."}""", statusCode: 404);
        await using var provider = BuildIngestion(vyk: true);

        await Assert.ThrowsAsync<SourceFetchException>(() => RunAsync(provider, jobCode));

        await using (var db = _postgres.CreateContext())
        {
            Assert.Equal(0, await CountRowsAsync(db, table));
            Assert.Equal(0, await db.QuarantinedFacts.CountAsync(q => q.JobCode == jobCode));

            var run = Assert.Single(await db.IngestRuns.Where(r => r.JobCode == jobCode).ToListAsync());
            Assert.Equal("failed", run.Status);
        }
    }

    /// <summary>
    /// TKT-foundation-034 (criterion (i), honest failure) — the Apinizer <b>fault envelope</b>
    /// (HTTP 200 with a <c>fault</c> body, which is what the live gateway returns on an
    /// auth/throttle failure) is likewise a transport failure: failed run, no rows, no
    /// quarantine.
    /// </summary>
    [Fact]
    public async Task TKT_foundation_034_gateway_fault_envelope_is_an_honest_source_failure()
    {
        await ResetAsync();
        StubVykDirectory();
        _wireMock.StubJson(
            DisclosuresPath,
            """{"fault":{"faultCode":"OI002","faultString":"Authentication policy failed or rate limit exceeded"}}""");
        await using var provider = BuildIngestion(vyk: true);

        await Assert.ThrowsAsync<SourceFetchException>(() => RunAsync(provider, "corporate-actions"));

        await using var db = _postgres.CreateContext();
        Assert.Equal(0, await db.CorporateActions.CountAsync());
        Assert.Equal(0, await db.QuarantinedFacts.CountAsync(q => q.JobCode == "corporate-actions"));
        Assert.Equal("failed", Assert.Single(await db.IngestRuns.Where(r => r.JobCode == "corporate-actions").ToListAsync()).Status);
    }

    /// <summary>
    /// TKT-foundation-034 (criterion (ii)) — the per-instrument disclosure cursor is persisted in
    /// the existing <c>coverage_metadata</c> table and the next run resumes <b>inclusively</b> at
    /// the last-seen index: the gateway answers HTTP 400 <c>ER005</c> for an index <i>past</i> a
    /// company's last disclosure (live-verified 2026-10-20), so the walk must never step beyond
    /// the tail. The re-run writes nothing new (idempotent).
    /// </summary>
    [Fact]
    public async Task TKT_foundation_034_cursor_resumes_inclusively_and_is_idempotent()
    {
        await ResetAsync();
        StubVykDirectory();
        StubDisclosureList(("1138986", "CA"));
        StubDetail("1138986", DividendDetailBody());
        await using var provider = BuildIngestion(vyk: true);

        _wireMock.Server.ResetLogEntries();
        var first = await RunAsync(provider, "dividends");
        Assert.Equal(IngestResult.Succeeded, first.Status);
        Assert.Equal(2, first.Written);
        Assert.Equal("0", DisclosureRequestIndexes().Single());

        await using (var db = _postgres.CreateContext())
        {
            var alfa = await db.Instruments.Where(i => i.Symbol == "ALFA").Select(i => i.Id).SingleAsync();
            var cursor = await db.CoverageMetadata.SingleAsync(
                c => c.Scope == "instrument" && c.InstrumentId == alfa && c.DataType == "kap-dividends-cursor");
            Assert.Equal("1138986", cursor.Notes);
        }

        // The cursor's data type sits outside the no-silent-gaps reconcile set → never clobbered.
        Assert.DoesNotContain("kap-dividends-cursor", CoverageDataTypes.InstrumentScoped);

        _wireMock.Server.ResetLogEntries();
        var second = await RunAsync(provider, "dividends");

        // Nothing new past the cursor: no detail is re-fetched and no fact is re-emitted.
        Assert.Equal(IngestResult.Succeeded, second.Status);
        Assert.Equal(0, second.Written);
        Assert.Equal(0, second.Unchanged);
        Assert.DoesNotContain(
            _wireMock.Server.LogEntries,
            e => e.RequestMessage is not null
                && e.RequestMessage.Path.StartsWith("/disclosureDetail", StringComparison.Ordinal));

        // Resumed at the last-seen index itself (inclusive), never at index+1 (which 400s).
        Assert.Equal("1138986", DisclosureRequestIndexes().Single());
    }

    /// <summary>
    /// TKT-foundation-034 (criterion (i), config) — the shipped configuration names the
    /// <b>verified</b> VYK routes for all three families, and the code defaults agree with it
    /// (no placeholder <c>/dividends</c>/<c>/corporate-actions</c> route survives).
    /// </summary>
    [Fact]
    public void TKT_foundation_034_config_wiring_ships_vyk_event_routes()
    {
        var appsettingsPath = Path.Combine(FindRepositoryRoot(), "src", "Api", "appsettings.json");
        Assert.True(File.Exists(appsettingsPath), $"appsettings.json missing at {appsettingsPath}");
        using var document = JsonDocument.Parse(File.ReadAllText(appsettingsPath));
        var ingestion = document.RootElement.GetProperty("Ingestion");

        foreach (var section in new[] { "Dividends", "CorporateActions", "Disclosures" })
        {
            var family = ingestion.GetProperty(section);
            Assert.Equal("/disclosures", family.GetProperty("Path").GetString());
            Assert.Contains(
                "{disclosureIndex}",
                family.GetProperty("DetailPath").GetString()!,
                StringComparison.Ordinal);
            Assert.Equal("XU030", family.GetProperty("UniverseIndex").GetString());
            Assert.Equal("0", family.GetProperty("EarlyDisclosureIndex").GetString());
        }

        Assert.Equal("CA", ingestion.GetProperty("Dividends").GetProperty("DisclosureType").GetString());
        Assert.Equal("KP", ingestion.GetProperty("Dividends").GetProperty("DividendEventType").GetString());
        Assert.Equal("CA", ingestion.GetProperty("CorporateActions").GetProperty("DisclosureType").GetString());
        Assert.Equal(string.Empty, ingestion.GetProperty("Disclosures").GetProperty("DisclosureType").GetString());

        // The code defaults are the same verified routes (config and code cannot drift).
        Assert.Equal("/disclosures", new DividendsSourceOptions().Path);
        Assert.Equal("CA", new CorporateActionsSourceOptions().DisclosureType);
        Assert.Equal(string.Empty, new DisclosuresSourceOptions().DisclosureType);
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// The ingestion composition used in production, retargeted at the Testcontainers
    /// database and the WireMock source double. The KAP client points at WireMock; each
    /// job's route selects its named canned payload (FU §10). With <paramref name="vyk"/> the
    /// three fact families switch to their <b>live-verified</b> VYK routes (§10.9.15): the
    /// events jobs walk the bound index through <c>/members</c> + <c>/disclosures</c> +
    /// <c>/disclosureDetail</c>; the canonical (legacy) doubles stay bound otherwise so the
    /// TC-MDF-003..006 goldens are byte-for-byte unchanged.
    /// </summary>
    private IngestionHost BuildIngestion(string? documentRoot = null, bool vyk = false)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _postgres.ConnectionString,
            ["Ingestion:Kap:BaseUrl"] = _wireMock.BaseUrl,
            ["Ingestion:Statements:Path"] = "/canned-sources/statements-ok-alfa-fy2025",
            // The company-scoped statements loop resolves its fetch bound from this index; the
            // test-local single-member bind keeps the ALFA golden intact (TKT-foundation-024).
            ["Ingestion:Statements:UniverseIndex"] = StatementBound.IndexCode,
            ["Ingestion:Statements:RequestDelayMs"] = "0",
            ["Ingestion:Dividends:Path"] = "/canned-sources/dividends-ok",
            ["Ingestion:CorporateActions:Path"] = "/canned-sources/corporate-actions-ok",
            ["Ingestion:Disclosures:Path"] = "/canned-sources/disclosures-ok",
        };
        if (documentRoot is not null)
        {
            settings["Ingestion:Disclosures:DocumentRoot"] = documentRoot;
        }

        if (vyk)
        {
            settings["Ingestion:Kap:MembersPath"] = MembersPath;
            settings["Ingestion:Kap:LastDisclosureIndexPath"] = AnchorPath;
            foreach (var section in new[] { "Dividends", "CorporateActions", "Disclosures" })
            {
                settings[$"Ingestion:{section}:Path"] = DisclosuresPath;
                settings[$"Ingestion:{section}:DetailPath"] = DetailPathTemplate;
                settings[$"Ingestion:{section}:UniverseIndex"] = StatementBound.IndexCode;
                settings[$"Ingestion:{section}:EarlyDisclosureIndex"] = "0";
            }
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
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM coverage_metadata WHERE data_type IN ('kap-dividends-cursor','kap-corporate-actions-cursor','kap-disclosures-cursor')");
        await StatementBound.ReplaceAsync(db, "ALFA");
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

    /// <summary>The named field of a VYK <c>flatData[0].content.object</c> terms document, whose
    /// fields the gateway nests under <c>void</c>.</summary>
    private static JsonElement TermsField(JsonElement terms, string property) =>
        Assert.Single(
            terms.GetProperty("void").EnumerateArray(),
            field => field.GetProperty("property").GetString() == property);

    // ---------------------------------------------------------------------------------
    // TKT-foundation-034 VYK doubles (verified routes, trimmed real captures)
    // ---------------------------------------------------------------------------------

    private const string MembersPath = "/members";
    private const string AnchorPath = "/lastDisclosureIndex";
    private const string DisclosuresPath = "/disclosures";
    private const string DetailPathTemplate = "/disclosureDetail/{disclosureIndex}";

    private static string DetailPath(string disclosureIndex) =>
        DetailPathTemplate.Replace("{disclosureIndex}", disclosureIndex, StringComparison.Ordinal);

    /// <summary>The run's company directory (Şirket Listesi) double — the live row shape.</summary>
    private void StubVykDirectory()
    {
        _wireMock.Server.ResetMappings();
        _wireMock.StubJson(
            MembersPath,
            """[{"id":"1001","title":"ALFA","stockCode":"ALFA","memberType":"stock"}]""");
        _wireMock.StubJson(AnchorPath, """{"lastDisclosureIndex":"1231017"}""");
    }

    /// <summary>
    /// The <c>/disclosures</c> list double: the live-verified row shape (identity, type/class,
    /// owning <c>companyId</c>, the accepted file types) — <b>no</b> date and a <c>title</c> that
    /// is the company name, exactly as the gateway returns it. Ascending by index.
    /// </summary>
    private void StubDisclosureList(params (string Index, string Type)[] rows) =>
        _wireMock.StubJson(
            DisclosuresPath,
            JsonSerializer.Serialize(rows.Select(row => new
            {
                disclosureIndex = row.Index,
                disclosureType = row.Type,
                disclosureClass = "ODA",
                subReportIds = Array.Empty<string>(),
                title = "ALFA",
                companyId = "1001",
                acceptedDataFileTypes = new[] { "html", "flat" },
            })));

    private void StubDetail(string disclosureIndex, string body) =>
        _wireMock.StubJson(DetailPath(disclosureIndex), body);

    /// <summary>
    /// A dividend (<c>eventType=KP</c>) detail body, trimmed from the <b>real</b> capture of BİM
    /// disclosure 1138986 (2026-10-20): <c>flatData[0]</c> is the <c>KPY51DTO</c> terms document,
    /// its <c>content.object.void[]</c> the named fields, <c>currency</c> a keyed
    /// <c>{key,text}</c> list, and the two cash tables nested
    /// <c>object.void[] → object.void[]</c> exactly as the gateway nests them. Values are the
    /// captured ones (gross 2.9999999… — 2.0000000 / 3.0000000 per instalment, `T` = 5.0000000).
    /// </summary>
    private static string DividendDetailBody() =>
        """
        {
          "disclosureIndex": "1138986",
          "disclosureType": "CA",
          "disclosureClass": "ODA",
          "subject": {
            "tr": "Kar Payı Dağıtım İşlemlerine İlişkin Bildirim",
            "en": "Notification Regarding Dividend Payment"
          },
          "time": "14.04.2023 20:45:54",
          "link": "https://www.kap.org.tr/Bildirim/1138986",
          "relatedStocks": [],
          "eventType": "KP",
          "eventId": "44384",
          "flatData": [
            {
              "id": "tr.com.mkk.kap.client.dto.KPY51DTO",
              "content": {
                "object": {
                  "void": [
                    {
                      "property": "currency",
                      "object": {
                        "void": [
                          { "property": "key", "string": "TRY" },
                          { "property": "text", "string": "TRY" }
                        ]
                      },
                      "label": { "tr": "Para Birimi", "en": "Currency Unit" }
                    },
                    {
                      "property": "subProcessName",
                      "string": "NKP",
                      "label": { "tr": "Alt Süreçler", "en": "Sub Processes" }
                    },
                    {
                      "property": "cashDividendList",
                      "object": {
                        "void": [
                          {
                            "object": {
                              "void": [
                                { "property": "exchCode", "string": "ALFA" },
                                { "property": "installmentOrder", "string": "1" },
                                { "property": "grossPercentage", "object": { "string": "200.0000000" } },
                                { "property": "grossTry", "object": { "string": "2.0000000" } },
                                { "property": "netPercentage", "object": { "string": "180.000000000000" } },
                                { "property": "netTry", "object": { "string": "1.8000000" } }
                              ]
                            }
                          },
                          {
                            "object": {
                              "void": [
                                { "property": "exchCode", "string": "ALFA" },
                                { "property": "installmentOrder", "string": "2" },
                                { "property": "grossPercentage", "object": { "string": "300.0000000" } },
                                { "property": "grossTry", "object": { "string": "3.0000000" } },
                                { "property": "netPercentage", "object": { "string": "270.000000000000" } },
                                { "property": "netTry", "object": { "string": "2.7000000" } }
                              ]
                            }
                          },
                          {
                            "object": {
                              "void": [
                                { "property": "exchCode", "string": "ALFA" },
                                { "property": "installmentOrder", "string": "T" },
                                { "property": "grossPercentage", "object": { "string": "500.0000000" } },
                                { "property": "grossTry", "object": { "string": "5.0000000" } },
                                { "property": "netPercentage", "object": { "string": "450.000000000000" } },
                                { "property": "netTry", "object": { "string": "4.5000000" } }
                              ]
                            }
                          }
                        ]
                      },
                      "label": { "tr": "Nakit Kar Payı Tablosu", "en": "Cash Dividend Table" }
                    },
                    {
                      "property": "cashDividendPaymentDateList",
                      "object": {
                        "void": [
                          {
                            "object": {
                              "void": [
                                {
                                  "property": "advisedUsageDate",
                                  "object": { "void": { "property": "date", "object": { "string": "2023-06-14" } } }
                                },
                                { "property": "installmentOrder", "string": "1" },
                                {
                                  "property": "paymentDate",
                                  "object": { "void": { "property": "date", "object": { "string": "2023-06-16" } } }
                                },
                                {
                                  "property": "recordDate",
                                  "object": { "void": { "property": "date", "object": { "string": "2023-06-15" } } }
                                }
                              ]
                            }
                          },
                          {
                            "object": {
                              "void": [
                                {
                                  "property": "advisedUsageDate",
                                  "object": { "void": { "property": "date", "object": { "string": "2023-12-20" } } }
                                },
                                { "property": "installmentOrder", "string": "2" },
                                {
                                  "property": "paymentDate",
                                  "object": { "void": { "property": "date", "object": { "string": "2023-12-22" } } }
                                },
                                {
                                  "property": "recordDate",
                                  "object": { "void": { "property": "date", "object": { "string": "2023-12-21" } } }
                                }
                              ]
                            }
                          }
                        ]
                      },
                      "label": { "tr": "Nakit Kar Payı Ödeme Tarihleri Tablosu", "en": "Cash Dividend Payment Dates" }
                    }
                  ]
                }
              }
            }
          ]
        }
        """;

    /// <summary>
    /// A capital-increase (<c>eventType=SA</c>) detail body, trimmed from the <b>real</b> capture of
    /// Tüpraş disclosure 1138796 (2026-10-20): the <c>KPY52DTO</c> terms document with its real
    /// fields and values — no invented members.
    /// </summary>
    private static string CapitalIncreaseDetailBody() =>
        """
        {
          "disclosureIndex": "1138796",
          "disclosureType": "CA",
          "disclosureClass": "ODA",
          "subject": {
            "tr": "Sermaye Artırımı - Azaltımı İşlemlerine İlişkin Bildirim",
            "en": "Notification Regarding Capital Increase - Decrease"
          },
          "time": "14.04.2023 18:17:12",
          "link": "https://www.kap.org.tr/Bildirim/1138796",
          "relatedStocks": [],
          "eventType": "SA",
          "eventId": "43057",
          "flatData": [
            {
              "id": "tr.com.mkk.kap.client.dto.KPY52DTO",
              "content": {
                "object": {
                  "void": [
                    {
                      "property": "subProcessName",
                      "string": "BDLSZ",
                      "label": { "tr": "Alt Süreçler", "en": "Sub Processes" }
                    },
                    {
                      "property": "useCaseMethodName",
                      "string": "sermayeArtirimi",
                      "label": { "tr": "Kullanım Durumu", "en": "Use Case Method" }
                    },
                    {
                      "property": "internalResourcesBonusPercentage",
                      "string": "600.00000",
                      "label": { "tr": "İç kaynaklardan bedelsiz pay alma oranı.", "en": "Rate Of Bonus Issue From Internal Resources" }
                    },
                    {
                      "property": "internalResourcesBonusAmount",
                      "string": "1651539084.000",
                      "label": { "tr": "İç Kaynaklardan Bedelsiz Pay Alma Tutarı", "en": "Amount Of Bonus Issue From Internal Resources" }
                    },
                    {
                      "property": "paidInCapital",
                      "object": { "string": "275256514" },
                      "label": { "tr": "Mevcut Sermaye (TL)", "en": "Paid-in Capital (TL)" }
                    },
                    {
                      "property": "targetCapital",
                      "object": { "string": "1926795598" },
                      "label": { "tr": "Ulaşılacak Sermaye (TL)", "en": "Target Capital (TL)" }
                    },
                    {
                      "property": "boardDecisionDate",
                      "object": { "void": { "property": "date", "object": { "string": "2023-02-14" } } },
                      "label": { "tr": "Yönetim Kurulu Karar Tarihi", "en": "Board Decision Date" }
                    },
                    {
                      "property": "currency",
                      "object": {
                        "void": [
                          { "property": "key", "string": "TRY" },
                          { "property": "text", "string": "TRY" }
                        ]
                      },
                      "label": { "tr": "Para Birimi", "en": "Currency Unit" }
                    },
                    { "property": "disclosureType", "string": "CA" },
                    { "property": "disclosureClass", "string": "ODA" }
                  ]
                }
              }
            }
          ]
        }
        """;

    /// <summary>
    /// A general (non-dividend, non-action) detail body in the same verified envelope: the list
    /// row's type is <c>ODA</c> and the detail carries only the metadata fields the disclosures
    /// job needs (<c>subject</c>/<c>time</c>/<c>link</c>) plus its identity.
    /// </summary>
    private static string DisclosureDetailBody(string disclosureIndex, string disclosureType) =>
        JsonSerializer.Serialize(new
        {
            disclosureIndex,
            disclosureType,
            disclosureClass = "ODA",
            subject = new { tr = "Özel Durum Açıklaması", en = "Material Event Disclosure" },
            time = "14.04.2023 20:45:54",
            link = $"https://www.kap.org.tr/Bildirim/{disclosureIndex}",
            relatedStocks = Array.Empty<object>(),
            flatData = Array.Empty<object>(),
        });

    /// <summary>The disclosure indexes the run requested from <c>/disclosures</c>, in call order.</summary>
    private List<string?> DisclosureRequestIndexes() =>
        _wireMock.Server.LogEntries
            .Where(e => e.RequestMessage is not null && e.RequestMessage.Path == DisclosuresPath)
            .OrderBy(e => e.RequestMessage!.DateTime)
            .Select(e => FirstValue(e.RequestMessage!.Query, "disclosureIndex"))
            .ToList();

    private static string? FirstValue(IDictionary<string, WireMockList<string>>? map, string key)
    {
        if (map is null)
        {
            return null;
        }

        foreach (var (name, values) in map)
        {
            if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
            {
                return values.FirstOrDefault();
            }
        }

        return null;
    }

    private static async Task<long> CountRowsAsync(DegerliDbContext db, string table) =>
        table switch
        {
            "dividends" => await db.Dividends.LongCountAsync(),
            "corporate_actions" => await db.CorporateActions.LongCountAsync(),
            "kap_disclosures" => await db.KapDisclosures.LongCountAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(table), table, "Unknown fact table."),
        };

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Degerli.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root (Degerli.sln).");
    }

    /// <summary>The active ingestion provider.</summary>
    private sealed class IngestionHost : IAsyncDisposable
    {
        public IngestionHost(ServiceProvider provider) => Provider = provider;

        public ServiceProvider Provider { get; }

        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }
}
