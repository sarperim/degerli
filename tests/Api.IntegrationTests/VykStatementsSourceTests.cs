using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.Coverage;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Kap;
using Degerli.Ingestion.Sources;
using Degerli.Ingestion.Statements;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Serilog;
using Serilog.Events;
using WireMock.Logging;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Types;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-foundation-026 acceptance — the <c>statements</c> source rewired from the blocked
/// KAP website-proxy path to the MKK "KAP VYK API" gateway (01 v1.5 §10.9.11). The
/// company-scoped loop (TKT-foundation-024) stays; this rewires the <b>source</b>: a
/// per-run <c>/members</c> directory resolves symbol → <c>companyId</c>, each company's
/// financial-report (<c>disclosureClass=FR</c>) disclosures are listed from a persisted
/// cursor and each new disclosure's XBRL-style <c>presentation</c> detail (live-verified
/// 2026-10-18; the OpenAPI's <c>flatData</c> is not returned) is decoded onto the canonical
/// chart of accounts. The member/list doubles are shape-faithful constructed bodies; the
/// detail is a bounded live capture — served through the L2 harness; the FU goldens are
/// untouched.
/// </summary>
public sealed class VykStatementsSourceTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    // Route constants — the shipped VYK routes (appsettings.json) that the harness binds.
    private const string MembersPath = "/members";
    private const string AnchorPath = "/lastDisclosureIndex";
    private const string DisclosuresPath = "/disclosures";
    private const string DetailPathTemplate = "/disclosureDetail/{disclosureIndex}";

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public VykStatementsSourceTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    // ---------------------------------------------------------------------------------
    // (i) Config wiring + auth
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// TKT-foundation-026 (criterion (i), the TC-MDF-075 precedent) — the production
    /// <c>Ingestion:Kap</c> section ships the VYK gateway base URL, an <b>empty-string</b>
    /// <c>ApiKey</c> default (the <c>EvdsApiKey</c> precedent), the <c>/members</c> route;
    /// the shipped <c>Ingestion:Statements</c> pacing is <c>10000</c> and <c>UniverseIndex</c>
    /// stays <c>XU030</c>.
    /// </summary>
    [Fact]
    public void TKT_foundation_026_config_wiring_ships_vyk_gateway_and_pacing()
    {
        var appsettingsPath = Path.Combine(FindRepositoryRoot(), "src", "Api", "appsettings.json");
        Assert.True(File.Exists(appsettingsPath), $"appsettings.json missing at {appsettingsPath}");
        using var document = JsonDocument.Parse(File.ReadAllText(appsettingsPath));
        var ingestion = document.RootElement.GetProperty("Ingestion");

        var kap = ingestion.GetProperty("Kap");
        Assert.Equal("https://apigwdev.mkk.com.tr/api/vyk", kap.GetProperty("BaseUrl").GetString());
        Assert.Equal(string.Empty, kap.GetProperty("ApiKey").GetString());
        Assert.Equal(string.Empty, kap.GetProperty("ApiSecret").GetString());
        Assert.Equal("/members", kap.GetProperty("MembersPath").GetString());

        var statements = ingestion.GetProperty("Statements");
        Assert.Equal("XU030", statements.GetProperty("UniverseIndex").GetString());
        Assert.Equal("/disclosures", statements.GetProperty("Path").GetString());
        Assert.Contains("{disclosureIndex}", statements.GetProperty("DetailPath").GetString()!, StringComparison.Ordinal);
        Assert.Equal("FR", statements.GetProperty("DisclosureClass").GetString());
        Assert.Equal(10000, statements.GetProperty("RequestDelayMs").GetInt32());

        // The code defaults stay the L2 binding (0 overrides in the harness) — the production
        // 10000 is a config flip, never a code default.
        var defaults = new StatementsSourceOptions();
        Assert.Equal(500, defaults.RequestDelayMs);
        Assert.Equal("XU030", defaults.UniverseIndex);
    }

    /// <summary>
    /// TKT-foundation-026 (criterion (i)) — the HTTP Basic credential is attached to the KAP
    /// client <b>iff</b> a key is configured (empty in local/test wiring), and a configured
    /// secret is redacted from any message that could echo it (the Macro-adapter precedent).
    /// </summary>
    [Fact]
    public async Task TKT_foundation_026_basic_auth_attached_only_when_key_present()
    {
        const string key = "vyk-api-key";
        const string secret = "vyk-api-secret";

        // Credential present → Basic auth header carrying ApiKey:ApiSecret (live-verified pair).
        await using (var host = BuildIngestion(new Dictionary<string, string?>
        {
            ["Ingestion:Kap:ApiKey"] = key,
            ["Ingestion:Kap:ApiSecret"] = secret,
        }))
        {
            _wireMock.Server.ResetMappings();
            _wireMock.StubJson(MembersPath, "[]");
            _wireMock.Server.ResetLogEntries();

            using var scope = host.Provider.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<IKapSourceClient>();
            await client.GetStringAsync(MembersPath);

            var authorization = HeaderOf(RequestEntries().Single(), "Authorization");
            var expected = "Basic " + Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{key}:{secret}"));
            Assert.Equal(expected, authorization);
        }

        // No credential → no Authorization header at all.
        await using (var host = BuildIngestion(new Dictionary<string, string?>()))
        {
            _wireMock.Server.ResetMappings();
            _wireMock.StubJson(MembersPath, "[]");
            _wireMock.Server.ResetLogEntries();

            using var scope = host.Provider.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<IKapSourceClient>();
            await client.GetStringAsync(MembersPath);

            Assert.Null(HeaderOf(RequestEntries().Single(), "Authorization"));
        }

        // Redaction: every configured secret never survives in an echoed message (raw or URL-encoded).
        var options = new KapSourceOptions { ApiKey = key, ApiSecret = secret };
        Assert.DoesNotContain(key, options.Redact($"failed for /members?key={key}"), StringComparison.Ordinal);
        Assert.DoesNotContain(secret, options.Redact($"failed for /members?secret={secret}"), StringComparison.Ordinal);
        Assert.DoesNotContain(Uri.EscapeDataString(secret), options.Redact($"x={Uri.EscapeDataString(secret)}"), StringComparison.Ordinal);
        Assert.True(options.HasCredential);
        Assert.False(new KapSourceOptions().HasCredential);
    }

    // ---------------------------------------------------------------------------------
    // (ii) Company mapping
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// TKT-foundation-026 (criterion (ii), (iii)) — the symbol → <c>companyId</c> resolution
    /// consumes the <c>/members</c> shape, the company's FR disclosure list is fetched from the
    /// configured early index, and each new disclosure's detail decodes through the production
    /// adapter and the job's DB ingest path: the fiscal period/year map from the detail fields,
    /// the <c>presentation</c> facts collapse onto <c>02</c> §6.1 item codes, and provenance stays
    /// <c>kap</c>.
    /// </summary>
    [Fact]
    public async Task TKT_foundation_026_members_mapping_and_detail_decode_ingest()
    {
        await ResetAsync();
        await BindAsync("ALFA");
        StubDirectory();
        StubDisclosures(companyId: "1001", disclosureIndex: "1001");
        StubDetail("1001", VykDetail("1001", ("Revenue", "1.234.567,89", "IS"), ("ProfitLoss", "123.456,78", "IS"), ("Equity", "5.000.000,00", "BS")));

        await using var host = BuildIngestion(new Dictionary<string, string?>());
        var result = await RunAsync(host);

        Assert.Equal(IngestResult.Succeeded, result.Status);
        Assert.Equal(2, result.Written);
        Assert.Equal(0, result.Quarantined);

        await using (var db = _postgres.CreateContext())
        {
            var alfa = await db.Instruments.Where(i => i.Symbol == "ALFA").Select(i => i.Id).SingleAsync();

            var isStatement = await db.FinancialStatements.SingleAsync(s => s.InstrumentId == alfa && s.StatementType == "IS");
            Assert.Equal("FY", isStatement.PeriodType);
            Assert.Equal(new DateOnly(2025, 12, 31), isStatement.PeriodEndDate);
            Assert.Equal(2025, isStatement.FiscalYear);
            Assert.Equal("kap", isStatement.SourceRef);

            var isLines = await db.FinLineItems.Where(l => l.StatementId == isStatement.Id).ToDictionaryAsync(l => l.ItemCode, l => l.Value);
            Assert.Equal(1_234_567.89m, isLines["REV"]);
            Assert.Equal(123_456.78m, isLines["NI"]);

            var bs = await db.FinancialStatements.SingleAsync(s => s.InstrumentId == alfa && s.StatementType == "BS");
            var bsLines = await db.FinLineItems.Where(l => l.StatementId == bs.Id).ToDictionaryAsync(l => l.ItemCode, l => l.Value);
            Assert.Equal(5_000_000.00m, bsLines["TOTAL_EQUITY"]);

            Assert.Equal(0, await db.QuarantinedFacts.CountAsync());
        }
    }

    /// <summary>
    /// TKT-foundation-026 (criterion (ii)) — an unmapped bound symbol is marked <b>failed</b>
    /// with the named reason <c>COMPANY_IDENTITY_UNRESOLVED</c>, writes no rows, is counted in
    /// the run-ledger per-company stats, and never crashes the run (the loop continues past it).
    /// </summary>
    [Fact]
    public async Task TKT_foundation_026_unmapped_symbol_is_failed_with_named_reason()
    {
        await ResetAsync();
        await BindAsync("ALFA", "MISSING");
        StubDirectory();
        StubDisclosures(companyId: "1001", disclosureIndex: "1001");
        StubDetail("1001", VykDetail("1001", ("Revenue", "1.234.567,89", "IS")));

        await using var host = BuildIngestion(new Dictionary<string, string?>());
        var result = await RunAsync(host);

        // The mapped company ingested; the unmapped one failed → partial, never a crash.
        Assert.Equal(IngestResult.Partial, result.Status);

        await using (var db = _postgres.CreateContext())
        {
            var symbols = await db.Instruments.ToDictionaryAsync(i => i.Id, i => i.Symbol);
            var rowSymbols = (await db.FinancialStatements.ToListAsync()).Select(s => symbols[s.InstrumentId]).ToArray();
            Assert.Contains("ALFA", rowSymbols);
            Assert.DoesNotContain("MISSING", rowSymbols);

            var run = Assert.Single(await db.IngestRuns.Where(r => r.JobCode == "statements").ToListAsync());
            using var stats = JsonDocument.Parse(run.StatsJson!);
            var perSymbol = stats.RootElement.GetProperty("symbols").EnumerateArray()
                .ToDictionary(e => e.GetProperty("symbol").GetString()!, StringComparer.Ordinal);
            Assert.True(perSymbol["MISSING"].GetProperty("failed").GetBoolean());
            Assert.Equal(0, perSymbol["MISSING"].GetProperty("written").GetInt32());
            Assert.False(perSymbol["ALFA"].GetProperty("failed").GetBoolean());

            Assert.Equal(0, await db.QuarantinedFacts.CountAsync(q => q.JobCode == "statements"));
        }

        Assert.True(
            host.Logs.Contains(evt =>
                evt.RenderMessage().Contains("MISSING", StringComparison.Ordinal)
                && evt.RenderMessage().Contains(StatementsJob.CompanyIdentityUnresolved, StringComparison.Ordinal)),
            "no log naming MISSING / COMPANY_IDENTITY_UNRESOLVED");
    }

    /// <summary>
    /// TKT-foundation-026 (criterion (ii)) — a <c>/members</c> transport failure degrades
    /// honestly (§10.9.10): every company is marked failed with <c>SOURCE_FETCH_FAILED</c>, no
    /// rows are written, no fabricated mapping is produced, and an aggregate alert is raised.
    /// </summary>
    [Fact]
    public async Task TKT_foundation_026_members_transport_failure_degrades_without_fabrication()
    {
        await ResetAsync();
        await BindAsync("ALFA", "EPSL");
        _wireMock.Server.ResetMappings();
        _wireMock.StubJson(MembersPath, "{}", statusCode: 500);
        _wireMock.Server.ResetLogEntries();

        await using var host = BuildIngestion(new Dictionary<string, string?>());
        var result = await RunAsync(host);

        Assert.Equal(IngestResult.Failed, result.Status);

        await using (var db = _postgres.CreateContext())
        {
            Assert.Equal(0, await db.FinancialStatements.CountAsync());
            Assert.Equal(0, await db.QuarantinedFacts.CountAsync(q => q.JobCode == "statements"));
        }

        var alert = Assert.Single(host.Alerts);
        Assert.Equal(StatementsJob.SourceFetchFailed, alert.ReasonCode);
    }

    // ---------------------------------------------------------------------------------
    // (iii) Identity guard + duplicate-concept rule
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// TKT-foundation-026 (criterion (iii)) — the identity guard runs <b>before</b> decode: a
    /// listed disclosure whose <c>companyId</c> does not match the bound company is skipped, so
    /// the result is an empty statement set with no detail fetch — another company's facts are
    /// never decoded or quarantined under this company's identity (FR-MDF-008).
    /// </summary>
    [Fact]
    public async Task TKT_foundation_026_identity_guard_skips_mismatched_company_before_decode()
    {
        await ResetAsync();
        await BindAsync("ALFA");
        StubDirectory();
        // The list is served for companyId 1001 (the request filter) but the row names 9999.
        StubDisclosures(companyId: "1001", disclosureIndex: "1001", rowCompanyId: "9999");
        _wireMock.Server.ResetLogEntries();

        await using var host = BuildIngestion(new Dictionary<string, string?>());
        var result = await RunAsync(host);

        Assert.Equal(IngestResult.Succeeded, result.Status);
        Assert.Equal(0, result.Written);

        // No detail fetch: the mismatched disclosure was skipped before decode.
        Assert.DoesNotContain(RequestEntries(), e => e.RequestMessage!.Path.StartsWith("/disclosureDetail", StringComparison.Ordinal));

        await using (var db = _postgres.CreateContext())
        {
            Assert.Equal(0, await db.FinancialStatements.CountAsync());
            Assert.Equal(0, await db.QuarantinedFacts.CountAsync(q => q.JobCode == "statements"));
        }
    }

    /// <summary>
    /// TKT-foundation-026 (criterion (iii)) — the §10.9.5 duplicate-concept rule carries over to
    /// the VYK <c>presentation</c> layer: equal duplicates collapse idempotently; unequal
    /// duplicates quarantine <c>CONFLICTING_VALUE</c> with both values retained, while the
    /// non-conflicting sibling group still ingests (partial batch).
    /// </summary>
    [Fact]
    public async Task TKT_foundation_026_duplicate_concept_rule_preserved_in_presentation()
    {
        await ResetAsync();
        await BindAsync("ALFA");
        StubDirectory();
        StubDisclosures(companyId: "1001", disclosureIndex: "1001");
        StubDetail("1001", VykDetail(
            "1001",
            ("Revenue", "1.234.567,89", "IS"),
            ("Revenue", "9.999,99", "IS"),
            ("Equity", "5.000.000,00", "BS")));

        await using var host = BuildIngestion(new Dictionary<string, string?>());
        var result = await RunAsync(host);

        Assert.Equal(IngestResult.Partial, result.Status);
        Assert.Equal(1, result.Written);
        Assert.Equal(1, result.Quarantined);

        await using (var db = _postgres.CreateContext())
        {
            var alfa = await db.Instruments.Where(i => i.Symbol == "ALFA").Select(i => i.Id).SingleAsync();
            Assert.Equal(0, await db.FinancialStatements.CountAsync(s => s.InstrumentId == alfa && s.StatementType == "IS"));

            var bs = await db.FinancialStatements.SingleAsync(s => s.InstrumentId == alfa && s.StatementType == "BS");
            Assert.Equal(5_000_000.00m, (await db.FinLineItems.SingleAsync(l => l.StatementId == bs.Id && l.ItemCode == "TOTAL_EQUITY")).Value);

            var quarantine = await db.QuarantinedFacts.SingleAsync(q => q.JobCode == "statements");
            Assert.Equal("CONFLICTING_VALUE", quarantine.ReasonCode);
            Assert.Contains("1.234.567,89", quarantine.PayloadJson);
            Assert.Contains("9.999,99", quarantine.PayloadJson);
        }
    }

    // ---------------------------------------------------------------------------------
    // (iv) Incremental cursor
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// TKT-foundation-026 (criterion (iv)) — a cursor-less company starts from the configured
    /// early index; the run persists the last-processed <c>disclosureIndex</c> in
    /// <c>coverage_metadata</c> (Scope <c>instrument</c>, data type <c>kap-disclosure-cursor</c>);
    /// a re-run resumes from that cursor and writes nothing new (idempotent). The cursor's data
    /// type is outside <c>InstrumentScoped</c>, so the no-silent-gaps reconcile never clobbers it.
    /// </summary>
    [Fact]
    public async Task TKT_foundation_026_incremental_cursor_persists_and_resumes()
    {
        await ResetAsync();
        await BindAsync("ALFA");
        StubDirectory();
        StubDisclosures(companyId: "1001", disclosureIndex: "1001");
        StubDetail("1001", VykDetail("1001", ("Revenue", "1.234.567,89", "IS")));

        await using var host = BuildIngestion(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Ingestion:Statements:EarlyDisclosureIndex"] = "0",
        });

        // First run: cursor-less → starts from the early index "0".
        _wireMock.Server.ResetLogEntries();
        var first = await RunAsync(host);
        Assert.Equal(IngestResult.Succeeded, first.Status);
        Assert.Equal(1, first.Written);
        Assert.Equal("0", QueryParam(DisclosureRequests().Single(), "disclosureIndex"));

        await using (var db = _postgres.CreateContext())
        {
            var alfa = await db.Instruments.Where(i => i.Symbol == "ALFA").Select(i => i.Id).SingleAsync();
            var cursor = await db.CoverageMetadata.SingleAsync(c => c.Scope == "instrument" && c.InstrumentId == alfa && c.DataType == KapDisclosureCursorStore.DataType);
            Assert.Equal("1001", cursor.Notes);
        }

        // The cursor data type is deliberately outside the reconcile set → never clobbered.
        Assert.DoesNotContain(KapDisclosureCursorStore.DataType, CoverageDataTypes.InstrumentScoped);

        // Second run: resumes from the persisted cursor "1001" and writes nothing new.
        _wireMock.Server.ResetLogEntries();
        var second = await RunAsync(host);
        Assert.Equal(0, second.Written);
        Assert.Equal(1, second.Unchanged);
        Assert.Equal("1001", QueryParam(DisclosureRequests().Single(), "disclosureIndex"));
    }

    // ---------------------------------------------------------------------------------
    // (v) Pacing + throttle
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// TKT-foundation-026 (criterion (v), the TC-MDF-074 posture) — every VYK call in the run
    /// (members, anchor, list, detail) is paced: requests are strictly sequential and each
    /// consecutive inter-request gap is at least the configured delay. Lower-bound only.
    /// </summary>
    [Fact]
    public async Task TKT_foundation_026_pacing_applies_to_every_vyk_call()
    {
        await ResetAsync();
        await BindAsync("ALFA");
        StubDirectory();
        StubDisclosures(companyId: "1001", disclosureIndex: "1001");
        StubDetail("1001", VykDetail("1001", ("Revenue", "1.234.567,89", "IS")));
        _wireMock.Server.ResetLogEntries();

        await using (var host = BuildIngestion(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Ingestion:Statements:RequestDelayMs"] = "30",
        }))
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await RunAsync(host);
            stopwatch.Stop();

            Assert.Equal(IngestResult.Succeeded, result.Status);

            // members, anchor, list, detail — four gateway calls, all paced.
            var entries = RequestEntries();
            Assert.Equal(4, entries.Count);
            Assert.Equal(MembersPath, entries[0].RequestMessage!.Path);
            Assert.Equal(AnchorPath, entries[1].RequestMessage!.Path);
            Assert.Equal(DisclosuresPath, entries[2].RequestMessage!.Path);
            Assert.StartsWith("/disclosureDetail", entries[3].RequestMessage!.Path, StringComparison.Ordinal);

            for (var i = 1; i < entries.Count; i++)
            {
                var gap = entries[i].RequestMessage!.DateTime - entries[i - 1].RequestMessage!.DateTime;
                Assert.True(
                    gap >= TimeSpan.FromMilliseconds(28),
                    $"inter-request gap {gap.TotalMilliseconds:0} ms < configured 30 ms.");
            }

            Assert.True(
                stopwatch.Elapsed >= TimeSpan.FromMilliseconds(90),
                $"elapsed {stopwatch.Elapsed.TotalMilliseconds:0} ms < 3 x 30 ms.");
        }
    }

    /// <summary>
    /// TKT-foundation-026 (criterion (v)) — a throttle fault (the Apinizer fault envelope, or an
    /// HTTP 429) is a <b>retryable transport failure</b>: it follows the §10.9.10 posture — no
    /// quarantine row, the company marked failed, one aggregate alert, loop continues (the
    /// scheduler's retry ladder applies to the failed run). Only the transport-failure half is
    /// asserted here; the retry-with-backoff half of criterion (v) rides the existing scheduler
    /// retry lane (no per-adapter backoff) and is covered there, not re-asserted per adapter.
    /// </summary>
    [Theory]
    [InlineData(200, """{"fault":{"faultCode":"OI002","faultString":"Authentication policy failed or rate limit exceeded"}}""")]
    [InlineData(429, "{}")]
    public async Task TKT_foundation_026_throttle_fault_is_a_transport_failure(int statusCode, string body)
    {
        await ResetAsync();
        await BindAsync("ALFA");
        StubDirectory();
        _wireMock.Server.ResetMappings();
        _wireMock.StubJson(MembersPath, MembersBody());
        _wireMock.StubJson(AnchorPath, """{"lastDisclosureIndex":"9999"}""");
        _wireMock.StubJson(DisclosuresPath, body, statusCode: statusCode);
        _wireMock.Server.ResetLogEntries();

        await using var host = BuildIngestion(new Dictionary<string, string?>());
        var result = await RunAsync(host);

        Assert.Equal(IngestResult.Failed, result.Status);

        await using (var db = _postgres.CreateContext())
        {
            Assert.Equal(0, await db.FinancialStatements.CountAsync());
            Assert.Equal(0, await db.QuarantinedFacts.CountAsync(q => q.JobCode == "statements"));
        }

        Assert.Equal(StatementsJob.SourceFetchFailed, Assert.Single(host.Alerts).ReasonCode);
    }

    // ---------------------------------------------------------------------------------
    // (vi) Catalog shapes through the production client (TC-MDF-063 lane)
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// TKT-foundation-026 — the catalog bodies (<c>/members</c>, <c>/disclosures</c>, and the
    /// <b>bounded live capture</b> of <c>/disclosureDetail</c>) flow through the production VYK
    /// parsers: the members snapshot maps symbol → <c>companyId</c>, the list parses into
    /// <see cref="DisclosureInfo"/> rows, and the real XBRL <c>presentation</c> detail decodes
    /// onto canonical item codes (real values from the 2026-10-18 capture).
    /// </summary>
    [Fact]
    public async Task TKT_foundation_026_vyk_catalog_shapes_parse_through_the_production_decoders()
    {
        var http = new HttpClient { BaseAddress = new Uri(_wireMock.BaseUrl) };
        var client = new KapHttpSourceClient(http);
        var kapOptions = Options.Create(new KapSourceOptions { MembersPath = MembersPath, LastDisclosureIndexPath = string.Empty });
        var statementsOptions = Options.Create(new StatementsSourceOptions { Path = DisclosuresPath, DetailPath = DetailPathTemplate, DisclosureClass = "FR" });
        var pacer = new KapRequestPacer(Options.Create(new StatementsSourceOptions { RequestDelayMs = 0 }), TimeProvider.System);
        var directory = new VykCompanyDirectory(client, kapOptions, pacer, Microsoft.Extensions.Logging.Abstractions.NullLogger<VykCompanyDirectory>.Instance);
        var adapter = new StatementsSourceAdapter(
            client, statementsOptions, kapOptions, directory, pacer,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<StatementsSourceAdapter>.Instance);

        _wireMock.Server.ResetMappings();
        _wireMock.StubRealSource(RealSourceCatalog.VykMembers, path: MembersPath);
        _wireMock.StubRealSource(RealSourceCatalog.VykDisclosures, path: DisclosuresPath);
        _wireMock.StubRealSource(RealSourceCatalog.VykDisclosureDetail, path: "/disclosureDetail/1001");

        Assert.True(adapter.VykEnabled);
        var fetched = await adapter.FetchCompanyAsync("ALFA", fromDisclosureIndex: null);

        Assert.Equal("kap", fetched.Source.SourceRef);
        Assert.Equal("ALFA", fetched.Source.Payload.Symbol);
        Assert.Equal("1001", fetched.Cursor);
        Assert.Equal("TRY", fetched.Source.Payload.Currency);

        // The capture is disclosure 1226255's FY2023 9-month report: periodType Q, 2023-09-30.
        var fy2023 = fetched.Source.Payload.Statements.Single(s => s.StatementType == "IS");
        Assert.Equal("Q", fy2023.PeriodType);
        Assert.Equal(new DateOnly(2023, 9, 30), fy2023.PeriodEndDate);
        Assert.Equal(2023, fy2023.FiscalYear);
        Assert.Equal(651_945_115m, fy2023.Lines["REV"]);
        Assert.Equal(3_566_458_220m, fy2023.Lines["NI"]);
        Assert.Equal(651_945_115m, fy2023.Lines["GROSS_PROFIT"]);

        var bs = fetched.Source.Payload.Statements.Single(s => s.StatementType == "BS");
        Assert.Equal(7_434_520_966m, bs.Lines["TOTAL_EQUITY"]);
        Assert.Equal(355_754m, bs.Lines["CASH"]);
        Assert.Equal(256_707_489m, bs.Lines["CUR_ASSETS"]);
        Assert.Equal(263_190_349m, bs.Lines["CUR_LIAB"]);
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    private static string MembersBody() =>
        """[{"id":"1001","title":"ALFA","stockCode":"ALFA","memberType":"stock"},{"id":"1002","title":"EPSL","stockCode":"EPSL","memberType":"stock"}]""";

    private static string DisclosuresBody(string disclosureIndex, string rowCompanyId) =>
        $$"""[{"disclosureIndex":"{{disclosureIndex}}","disclosureType":"FR","disclosureClass":"FR","title":"Finansal Rapor","companyId":"{{rowCompanyId}}","subReportIds":[],"acceptedDataFileTypes":["data"]}]""";

    /// <summary>
    /// A shape-faithful VYK disclosure-detail body in the live-verified <c>presentation</c>
    /// shape: rows are grouped into one sub-report per statement type, each with a single
    /// <c>CURR</c> context (instant for <c>BS</c>, duration otherwise) carrying the row's
    /// value as a Turkish-locale number. The fiscal period is FY2025 (aligned with the FU
    /// anchor the loop tests assert).
    /// </summary>
    private static string VykDetail(string disclosureIndex, params (string Concept, string Value, string StatementType)[] rows)
    {
        var presentations = rows
            .GroupBy(r => r.StatementType)
            .Select(group =>
            {
                var isInstant = string.Equals(group.Key, "BS", StringComparison.Ordinal);
                var contextId = isInstant ? "2025-12-31" : "2025-01-012025-12-31";
                object period = isInstant
                    ? new { instant = "2025-12-31" }
                    : new { startDate = "2025-01-01", endDate = "2025-12-31" };
                return new
                {
                    id = "general_role_" + group.Key,
                    content = new
                    {
                        id = "general_role_" + group.Key,
                        isMultiDimensional = isInstant ? "no" : "yes",
                        ContextList = new
                        {
                            Context = new[] { new { id = contextId, key = "CURR", Period = period } },
                        },
                        ReportItem = new
                        {
                            name = RootReportItemName(group.Key),
                            @abstract = "true",
                            ReportItem = group.Select(r => new
                            {
                                name = r.Concept,
                                Values = new { Value = new[] { new { contextId, currency = "TRY", rounding = "INF", value = r.Value } } },
                            }).ToArray(),
                        },
                    },
                };
            })
            .ToArray();

        return JsonSerializer.Serialize(new
        {
            disclosureIndex,
            year = "2025",
            period = new { tr = "Yıllık", en = "Annual" },
            consolidation = "NC",
            relatedStocks = new[] { new { code = "ALFA" } },
            presentation = presentations,
            attachmentUrls = Array.Empty<object>(),
        });
    }

    private static string RootReportItemName(string statementType) => statementType switch
    {
        "BS" => "StatementOfFinancialPositionBalanceSheetAbstract",
        "CF" => "StatementOfCashFlowsIndirectMethodAbstract",
        "EQ" => "StatementOfChangesInEquityAbstract",
        _ => "StatementOfProfitOrLossAbstract",
    };

    private void StubDirectory()
    {
        _wireMock.Server.ResetMappings();
        _wireMock.StubJson(MembersPath, MembersBody());
        _wireMock.StubJson(AnchorPath, """{"lastDisclosureIndex":"9999"}""");
    }

    private void StubDisclosures(string companyId, string disclosureIndex, string? rowCompanyId = null) =>
        _wireMock.StubJson(DisclosuresPath, DisclosuresBody(disclosureIndex, rowCompanyId ?? companyId));

    private void StubDetail(string disclosureIndex, string body) =>
        _wireMock.StubJson(DetailPathTemplate.Replace("{disclosureIndex}", disclosureIndex, StringComparison.Ordinal), body);

    /// <summary>The ingestion composition retargeted at the Testcontainers DB + the WireMock double.</summary>
    private IngestionHost BuildIngestion(IReadOnlyDictionary<string, string?> overrides)
    {
        var logs = new InMemorySerilogSink();
        var alerts = new List<IngestionAlert>();
        var clock = new FakeTimeProvider(new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ConnectionStrings:Default"] = _postgres.ConnectionString,
            ["Ingestion:Kap:BaseUrl"] = _wireMock.BaseUrl,
            ["Ingestion:Kap:MembersPath"] = MembersPath,
            ["Ingestion:Kap:LastDisclosureIndexPath"] = AnchorPath,
            ["Ingestion:Statements:Path"] = DisclosuresPath,
            ["Ingestion:Statements:DetailPath"] = DetailPathTemplate,
            ["Ingestion:Statements:DisclosureClass"] = "FR",
            ["Ingestion:Statements:EarlyDisclosureIndex"] = "0",
            ["Ingestion:Statements:UniverseIndex"] = StatementBound.IndexCode,
            ["Ingestion:Statements:RequestDelayMs"] = "0",
        };
        foreach (var (key, value) in overrides)
        {
            settings[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var logger = new LoggerConfiguration()
            .MinimumLevel.Warning()
            .MinimumLevel.Override("Degerli.Ingestion.Statements", LogEventLevel.Information)
            .MinimumLevel.Override("Degerli.Ingestion.Kap", LogEventLevel.Information)
            .WriteTo.Sink(logs)
            .CreateLogger();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog(logger, dispose: false));
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IIngestionAlerter>(new CaptureAlerter(alerts));
        services.AddMarketDataIngestion(configuration);

        return new IngestionHost(services.BuildServiceProvider(), alerts, logs);
    }

    private static async Task<IngestResult> RunAsync(IngestionHost host)
    {
        using var scope = host.Provider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IIngestionJobRunner>();
        return await runner.RunAsync(StatementsJob.Code, new IngestionRequest(T));
    }

    private async Task ResetAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM fin_line_items");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM financial_statements");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM quarantined_facts");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM coverage_metadata WHERE data_type = 'kap-disclosure-cursor'");
    }

    /// <summary>Binds the statements fetch bound to <paramref name="symbols"/>, creating an
    /// instrument row for any symbol the FU universe does not seed.</summary>
    private async Task BindAsync(params string[] symbols)
    {
        await using var db = _postgres.CreateContext();
        var recordedAt = new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        foreach (var symbol in symbols)
        {
            if (await db.Instruments.AnyAsync(i => i.Symbol == symbol))
            {
                continue;
            }

            db.Instruments.Add(new Instrument
            {
                Symbol = symbol,
                Name = symbol,
                Status = "active",
                SourceRef = "test:vyk-bound",
                RecordedAt = recordedAt,
            });
        }

        await db.SaveChangesAsync();
        await StatementBound.ReplaceAsync(db, symbols);
    }

    private List<ILogEntry> RequestEntries() =>
        _wireMock.Server.LogEntries
            .Where(e => e.RequestMessage is not null)
            .OrderBy(e => e.RequestMessage!.DateTime)
            .ToList();

    private List<ILogEntry> DisclosureRequests() =>
        RequestEntries().Where(e => e.RequestMessage!.Path == DisclosuresPath).ToList();

    private static string? QueryParam(ILogEntry entry, string key) =>
        FirstValue(entry.RequestMessage?.Query, key);

    private static string? HeaderOf(ILogEntry entry, string name) =>
        FirstValue(entry.RequestMessage?.Headers, name);

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

    private sealed class IngestionHost : IAsyncDisposable
    {
        public IngestionHost(ServiceProvider provider, List<IngestionAlert> alerts, InMemorySerilogSink logs)
        {
            Provider = provider;
            Alerts = alerts;
            Logs = logs;
        }

        public ServiceProvider Provider { get; }

        public List<IngestionAlert> Alerts { get; }

        public InMemorySerilogSink Logs { get; }

        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    private sealed class CaptureAlerter : IIngestionAlerter
    {
        private readonly List<IngestionAlert> _alerts;

        public CaptureAlerter(List<IngestionAlert> alerts) => _alerts = alerts;

        public Task RaiseAsync(IngestionAlert alert, CancellationToken cancellationToken = default)
        {
            _alerts.Add(alert);
            return Task.CompletedTask;
        }
    }
}
