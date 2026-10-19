using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Api.IntegrationTests.Harness.Mail;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.Jobs;
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

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-foundation-024 acceptance — the company-scoped <c>statements</c> loop (01 v1.5
/// §10.9.2). The real KAP financial-statement resource is <b>per-company</b>: the job resolves
/// the current constituents of <c>Ingestion:Statements:UniverseIndex</c> and iterates them
/// strictly sequentially, paced by <c>Ingestion:Statements:RequestDelayMs</c>, isolating
/// per-company failures. The case shapes mirror the prices-loop lane (TC-MDF-072..075) applied
/// to companies — the test-planner follow-up the ticket flags. Live activation of the real
/// endpoint is a post-merge orchestrator criterion (the endpoint could not be verified live
/// from this environment; the attempt is recorded in the ticket/PR), never fabricated here.
/// </summary>
public sealed class StatementsLoopTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    // The per-company resource is `{symbol}`-templated (01 v1.5 §10.9.2): each company has its
    // own route. Distinct paths keep the WireMock double honest per company (a param-only
    // double would let one company's canonical body answer another's request).
    private const string StatementsPathTemplate = "/canned-sources/statements-loop/{symbol}";
    private const string StatementsPathPrefix = "/canned-sources/statements-loop/";

    private static string StatementsPath(string symbol) => StatementsPathPrefix + symbol;

    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    // The FU XU30 subset (5 current members): the per-company loop's bind under the FU XU30 index.
    private static readonly string[] Xu30Members = ["ALFA", "EPSL", "ETA", "KAPPA", "REST"];

    // A period none of the FU-seeded statements carry, so each company's payload is a real
    // insert rather than an idempotent no-op.
    private const string FreshPeriod = "2023-03-31";

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public StatementsLoopTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    /// <summary>
    /// TKT-foundation-024 (the TC-MDF-072 lane shape for companies) — the per-company loop
    /// resolves the configured index's current constituents: exactly one fetch per current XU30
    /// member, only those instruments receive rows, a closed-membership constituent is never
    /// fetched, and the run-ledger row carries one per-company entry. Requests are strictly
    /// sequential.
    /// </summary>
    [Fact]
    public async Task TKT_foundation_024_company_loop_resolves_the_configured_index_current_constituents()
    {
        await ResetAsync();
        await AddClosedXu30MemberAsync("ZETA");
        StubPerSymbol(stalledSymbol: null, serverDelay: TimeSpan.FromMilliseconds(30));
        _wireMock.Server.ResetLogEntries();

        await using var host = BuildIngestion(universeIndex: "XU30", requestDelayMs: 0);
        var result = await RunAsync(host, StatementsJob.Code);

        Assert.Equal(IngestResult.Succeeded, result.Status);
        Assert.Equal(5, RequestsSeen());
        Assert.Equal(5, result.Written);

        // Exactly one fetch per current XU30 member; no request for the closed member.

        await using (var db = _postgres.CreateContext())
        {
            var symbols = await db.Instruments.ToDictionaryAsync(i => i.Id, i => i.Symbol);
            var rowSymbols = (await db.FinancialStatements
                    .Where(s => s.PeriodEndDate == new DateOnly(2023, 3, 31))
                    .ToListAsync())
                .Select(s => symbols[s.InstrumentId])
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(Xu30Members, rowSymbols);
            Assert.DoesNotContain("ZETA", rowSymbols);

            // The run-ledger row carries the per-company breakdown.
            var run = Assert.Single(await db.IngestRuns.Where(r => r.JobCode == "statements").ToListAsync());
            Assert.Equal("succeeded", run.Status);
            using var stats = JsonDocument.Parse(run.StatsJson!);
            Assert.Equal(5, stats.RootElement.GetProperty("written").GetInt32());
            var perSymbol = stats.RootElement.GetProperty("symbols").EnumerateArray()
                .ToDictionary(e => e.GetProperty("symbol").GetString()!, StringComparer.Ordinal);
            Assert.Equal(
                Xu30Members.OrderBy(s => s, StringComparer.Ordinal).ToArray(),
                perSymbol.Keys.OrderBy(s => s, StringComparer.Ordinal).ToArray());
            foreach (var symbol in Xu30Members)
            {
                Assert.Equal(1, perSymbol[symbol].GetProperty("written").GetInt32());
                Assert.False(perSymbol[symbol].GetProperty("failed").GetBoolean());
            }
        }

        // Strictly sequential: each response completes before the next request is issued.
        var entries = RequestEntries();
        Assert.Equal(5, entries.Count);
        for (var i = 1; i < entries.Count; i++)
        {
            var gap = entries[i].RequestMessage!.DateTime - entries[i - 1].RequestMessage!.DateTime;
            Assert.True(
                gap >= TimeSpan.FromMilliseconds(25),
                $"requests {i - 1}->{i} overlapped (gap {gap.TotalMilliseconds:0} ms).");
        }
    }

    /// <summary>
    /// TKT-foundation-024 (the TC-MDF-073 lane shape for companies) — per-company failure
    /// isolation: one member's route returns 500 for the whole run. The failed company's rows
    /// are absent, the run does not abort (the others ingest), exactly one aggregate alert names
    /// the company, the ledger reaches a terminal partial status with the per-company breakdown,
    /// and no quarantine rows are written — a transport failure is a source-down, not a
    /// parse/validation event.
    /// </summary>
    [Fact]
    public async Task TKT_foundation_024_per_company_failure_isolation_within_the_loop()
    {
        await ResetAsync();
        StubPerSymbol(stalledSymbol: "EPSL", serverDelay: TimeSpan.Zero);
        _wireMock.Server.ResetLogEntries();

        await using var host = BuildIngestion(universeIndex: "XU30", requestDelayMs: 0);
        var result = await RunAsync(host, StatementsJob.Code);

        Assert.Equal(IngestResult.Partial, result.Status);

        await using (var db = _postgres.CreateContext())
        {
            var symbols = await db.Instruments.ToDictionaryAsync(i => i.Id, i => i.Symbol);
            var rowSymbols = (await db.FinancialStatements
                    .Where(s => s.PeriodEndDate == new DateOnly(2023, 3, 31))
                    .ToListAsync())
                .Select(s => symbols[s.InstrumentId])
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(["ALFA", "ETA", "KAPPA", "REST"], rowSymbols);
            Assert.DoesNotContain("EPSL", rowSymbols);

            // No quarantine for a transport-level failure.
            Assert.Equal(0, await db.QuarantinedFacts.CountAsync(q => q.JobCode == "statements"));

            // The run ledger reaches a terminal partial status and its per-company breakdown
            // records the failure.
            var run = Assert.Single(await db.IngestRuns.Where(r => r.JobCode == "statements").ToListAsync());
            Assert.Equal("partial", run.Status);
            using var stats = JsonDocument.Parse(run.StatsJson!);
            var perSymbol = stats.RootElement.GetProperty("symbols").EnumerateArray()
                .ToDictionary(e => e.GetProperty("symbol").GetString()!, StringComparer.Ordinal);
            Assert.True(perSymbol["EPSL"].GetProperty("failed").GetBoolean());
            Assert.Equal(0, perSymbol["EPSL"].GetProperty("written").GetInt32());
            foreach (var symbol in new[] { "ALFA", "ETA", "KAPPA", "REST" })
            {
                Assert.False(perSymbol[symbol].GetProperty("failed").GetBoolean());
                Assert.Equal(1, perSymbol[symbol].GetProperty("written").GetInt32());
            }
        }

        // All five members were attempted — the loop continued past the failure.
        Assert.Equal(5, RequestsSeen());

        // Exactly one aggregate alert, naming the failed company.
        var alert = Assert.Single(host.Mail.Sent);
        Assert.Contains("statements", alert.Subject, StringComparison.Ordinal);
        Assert.Contains("EPSL", alert.Subject, StringComparison.Ordinal);
        Assert.True(
            host.Logs.Contains(evt =>
                evt.RenderMessage().Contains("EPSL", StringComparison.Ordinal)
                && evt.RenderMessage().Contains(StatementsJob.SourceFetchFailed, StringComparison.Ordinal)),
            "no Serilog alert naming EPSL / SOURCE_FETCH_FAILED");
    }

    /// <summary>
    /// TKT-foundation-024 (the TC-MDF-074 lane shape for companies) — sequential pacing honors
    /// <c>RequestDelayMs</c>. With 40 ms configured every consecutive inter-request gap is at
    /// least the configured delay and the total elapsed is at least (N−1) × delay. Lower-bound
    /// assertions only.
    /// </summary>
    [Fact]
    public async Task TKT_foundation_024_sequential_pacing_honors_request_delay()
    {
        await ResetAsync();
        StubPerSymbol(stalledSymbol: null, serverDelay: TimeSpan.Zero);
        _wireMock.Server.ResetLogEntries();

        await using (var paced = BuildIngestion(universeIndex: "XU30", requestDelayMs: 40))
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await RunAsync(paced, StatementsJob.Code);
            stopwatch.Stop();

            Assert.Equal(IngestResult.Succeeded, result.Status);
            Assert.Equal(5, RequestsSeen());

            var entries = RequestEntries();
            Assert.Equal(5, entries.Count);
            for (var i = 1; i < entries.Count; i++)
            {
                var gap = entries[i].RequestMessage!.DateTime - entries[i - 1].RequestMessage!.DateTime;
                Assert.True(
                    gap >= TimeSpan.FromMilliseconds(38),
                    $"inter-request gap {gap.TotalMilliseconds:0} ms < configured 40 ms.");
            }

            Assert.True(
                stopwatch.Elapsed >= TimeSpan.FromMilliseconds(160),
                $"elapsed {stopwatch.Elapsed.TotalMilliseconds:0} ms < (N-1) x 40 ms.");
        }
    }

    /// <summary>
    /// TKT-foundation-024 (the TC-MDF-075 lane shape for companies) — config wiring. The bound
    /// resolves to the production default <c>XU030</c> with the default 500 ms pacing when no
    /// override is configured, and the shipped <c>src/Api/appsettings.json</c> carries the real
    /// <c>Ingestion:Kap</c> base URL and the templated <c>Ingestion:Statements</c> section.
    /// </summary>
    [Fact]
    public async Task TKT_foundation_024_statements_config_wiring()
    {
        // No override configured → the production defaults (config-only flip, no code).
        await using (var host = BuildIngestion(universeIndex: null, requestDelayMs: 0))
        {
            using var scope = host.Provider.CreateScope();
            var options = scope.ServiceProvider.GetRequiredService<IOptions<StatementsSourceOptions>>().Value;
            Assert.Equal("XU030", options.UniverseIndex);
        }

        // The template-defaults are the code defaults; the production path is bound from
        // appsettings.json (asserted below), never hard-coded in the adapter.
        var defaults = new StatementsSourceOptions();
        Assert.Equal("XU030", defaults.UniverseIndex);
        Assert.Equal(500, defaults.RequestDelayMs);

        // The shipped production config carries the real KAP base URL + the templated path.
        var appsettingsPath = Path.Combine(FindRepositoryRoot(), "src", "Api", "appsettings.json");
        Assert.True(File.Exists(appsettingsPath), $"appsettings.json missing at {appsettingsPath}");
        using var document = JsonDocument.Parse(File.ReadAllText(appsettingsPath));
        var ingestion = document.RootElement.GetProperty("Ingestion");

        // TKT-foundation-026 (permitted delta — the shipped config values of criterion (i)):
        // the KAP source rewired from the website-proxy path to the MKK VYK gateway, so the
        // shipped base URL and statements path are the VYK ones. The default bound/pacing
        // assertions above stay unchanged.
        Assert.Equal("https://apigwdev.mkk.com.tr/api/vyk", ingestion.GetProperty("Kap").GetProperty("BaseUrl").GetString());

        var statements = ingestion.GetProperty("Statements");
        Assert.Equal("XU030", statements.GetProperty("UniverseIndex").GetString());
        Assert.Equal("/disclosures", statements.GetProperty("Path").GetString());
        Assert.True(statements.GetProperty("RequestDelayMs").GetInt32() > 0);
    }

    /// <summary>
    /// TKT-foundation-024 (FR-MDF-008, §10.9.10; regression for DR-1) — the cross-company
    /// identity guard narrows a body whose identity does not match the bound symbol to an
    /// <b>empty statement set</b>. A deliberate narrowing is a silent no-op: the run writes
    /// zero rows under either identity and quarantines <b>nothing</b> — it never refuses the
    /// payload as <c>SCHEMA_MISMATCH</c> and quarantines another company's body. Both adapter
    /// shapes are exercised: the real KAP XBRL fact envelope (<c>companyCode</c> mismatch) and
    /// the canonical double (<c>FilterBySymbol</c>, the foreign symbol carrying no instrument
    /// row).
    /// </summary>
    [Fact]
    public async Task TKT_foundation_024_cross_company_identity_guard_narrows_to_a_noop()
    {
        // (1) Real KAP XBRL envelope: every bound member's route serves a body for ZZZZ.
        await ResetAsync();
        StubPerSymbolForeignRealEnvelope("ZZZZ");
        _wireMock.Server.ResetLogEntries();

        await using (var host = BuildIngestion(universeIndex: "XU30", requestDelayMs: 0))
        {
            var result = await RunAsync(host, StatementsJob.Code);

            Assert.Equal(IngestResult.Succeeded, result.Status);
            Assert.Equal(5, RequestsSeen());
            Assert.Equal(0, result.Written);
            Assert.Equal(0, result.Quarantined);
        }

        await using (var db = _postgres.CreateContext())
        {
            Assert.Equal(0, await db.FinancialStatements.CountAsync());
            Assert.Equal(0, await db.QuarantinedFacts.CountAsync(q => q.JobCode == "statements"));
        }

        // (2) Canonical double: the foreign symbol (no instrument row) is narrowed to an
        // empty set, never SCHEMA_MISMATCH-quarantined.
        await ResetAsync();
        StubPerSymbolForeignCanonical("ZZZZ");
        _wireMock.Server.ResetLogEntries();

        await using (var host = BuildIngestion(universeIndex: "XU30", requestDelayMs: 0))
        {
            var result = await RunAsync(host, StatementsJob.Code);

            Assert.Equal(IngestResult.Succeeded, result.Status);
            Assert.Equal(5, RequestsSeen());
            Assert.Equal(0, result.Written);
            Assert.Equal(0, result.Quarantined);
        }

        await using (var db = _postgres.CreateContext())
        {
            Assert.Equal(0, await db.FinancialStatements.CountAsync());
            Assert.Equal(0, await db.QuarantinedFacts.CountAsync(q => q.JobCode == "statements"));
        }
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// The ingestion composition used in production, retargeted at the Testcontainers database
    /// and the WireMock source double. <paramref name="universeIndex"/> = null omits the key so
    /// the production default resolves.
    /// </summary>
    private IngestionHost BuildIngestion(string? universeIndex, int requestDelayMs)
    {
        var mail = new RecordingMailDispatcher();
        var logs = new InMemorySerilogSink();
        var clock = new FakeTimeProvider(new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _postgres.ConnectionString,
            ["Ingestion:Kap:BaseUrl"] = _wireMock.BaseUrl,
            ["Ingestion:Statements:Path"] = StatementsPathTemplate,
            ["Ingestion:Statements:RequestDelayMs"] = requestDelayMs.ToString(CultureInfo.InvariantCulture),
        };
        if (universeIndex is not null)
        {
            settings["Ingestion:Statements:UniverseIndex"] = universeIndex;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var logger = new LoggerConfiguration()
            .MinimumLevel.Warning()
            .MinimumLevel.Override("Degerli.Ingestion.Statements", LogEventLevel.Information)
            .WriteTo.Sink(logs)
            .CreateLogger();

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

    private async Task ResetAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
        // The class shares one database; clear the statement rows every test writes at the
        // fresh period so each run's per-company inserts are genuinely new (deterministic,
        // order-independent).
        await db.Database.ExecuteSqlRawAsync("DELETE FROM fin_line_items");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM financial_statements");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM quarantined_facts");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM index_constituents WHERE effective_to IS NOT NULL");
    }

    /// <summary>Adds a closed (removed) XU30 membership row so the loop's
    /// <c>effective_to IS NULL</c> boundary is asserted.</summary>
    private async Task AddClosedXu30MemberAsync(string symbol)
    {
        await using var db = _postgres.CreateContext();
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO index_constituents (index_id, instrument_id, effective_from, effective_to, source_ref, recorded_at) " +
            "SELECT (SELECT id FROM indices WHERE code = 'XU30'), " +
            "(SELECT id FROM instruments WHERE symbol = {0}), " +
            "DATE '2026-09-01', DATE '2026-10-06', 'fixture:closed-membership', now()",
            symbol);
    }

    /// <summary>
    /// Stubs one route per current XU30 member on that company's own path: a fresh-period
    /// canonical envelope carrying that member's own symbol, except <paramref name="stalledSymbol"/>
    /// which returns 500 (source-down).
    /// </summary>
    private void StubPerSymbol(string? stalledSymbol, TimeSpan serverDelay)
    {
        _wireMock.Server.ResetMappings();

        foreach (var symbol in Xu30Members)
        {
            var stalled = string.Equals(symbol, stalledSymbol, StringComparison.Ordinal);
            var response = stalled
                ? Response.Create().WithStatusCode(500).WithHeader("Content-Type", "application/json").WithBody("{}")
                : Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(Body(symbol));

            if (!stalled && serverDelay > TimeSpan.Zero)
            {
                response = response.WithDelay(serverDelay);
            }

            _wireMock.Server
                .Given(Request.Create().WithPath(StatementsPath(symbol)).UsingGet())
                .RespondWith(response);
        }
    }

    /// <summary>
    /// Stubs one route per current XU30 member serving a <b>real KAP XBRL fact envelope</b>
    /// whose <c>companyCode</c> is <paramref name="foreignCompanyCode"/> — a body for a
    /// company other than the requested member (the identity-guard case).
    /// </summary>
    private void StubPerSymbolForeignRealEnvelope(string foreignCompanyCode)
    {
        _wireMock.Server.ResetMappings();

        foreach (var symbol in Xu30Members)
        {
            _wireMock.Server
                .Given(Request.Create().WithPath(StatementsPath(symbol)).UsingGet())
                .RespondWith(Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(RealEnvelopeBody(foreignCompanyCode)));
        }
    }

    /// <summary>
    /// Stubs one route per current XU30 member serving a <b>canonical double</b> whose
    /// <c>symbol</c> is <paramref name="foreignSymbol"/> — the <c>FilterBySymbol</c> narrowing
    /// case (the foreign symbol carries no instrument row).
    /// </summary>
    private void StubPerSymbolForeignCanonical(string foreignSymbol)
    {
        _wireMock.Server.ResetMappings();

        foreach (var symbol in Xu30Members)
        {
            _wireMock.Server
                .Given(Request.Create().WithPath(StatementsPath(symbol)).UsingGet())
                .RespondWith(Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(Body(foreignSymbol)));
        }
    }

    /// <summary>A well-formed real KAP XBRL fact envelope carrying <paramref name="companyCode"/>.</summary>
    private static string RealEnvelopeBody(string companyCode) => $$"""
        {"companyCode":"{{companyCode}}","unitScale":"units","currency":"TRY","facts":[
          {"fiscalYear":2023,"periodType":"Q","periodEndDate":"{{FreshPeriod}}","statementType":"IS",
           "version":"as_reported","concept":"ifrs-full:Revenue","value":"100"}]}
        """;

    /// <summary>One company's canonical statement payload at a fresh (unseeded) period.</summary>
    private static string Body(string symbol) => $$"""
        {"sourceRef":"kap://statements/{{symbol}}","symbol":"{{symbol}}","unitScale":"units","currency":"TRY",
         "statements":[{"periodType":"Q","periodEndDate":"{{FreshPeriod}}","fiscalYear":2023,"statementType":"IS",
           "version":"as_reported","publishedAt":"2023-05-10","lines":{"NI": 100} } ] }
        """;

    /// <summary>The per-company request log entries, ordered by arrival time.</summary>
    private List<ILogEntry> RequestEntries() =>
        _wireMock.Server.LogEntries
            .Where(entry => entry.RequestMessage?.Path?.StartsWith(StatementsPathPrefix, StringComparison.Ordinal) == true)
            .OrderBy(entry => entry.RequestMessage!.DateTime)
            .ToList();

    /// <summary>Number of per-company requests seen so far.</summary>
    private int RequestsSeen() => RequestEntries().Count;

    /// <summary>Walks up from the test bin directory to the checkout holding the solution.</summary>
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
