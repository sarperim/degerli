using System.Text.Json.Nodes;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Api.IntegrationTests.Harness.Mail;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.Coverage;
using Degerli.Ingestion.Jobs;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Serilog;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-fdf-003 acceptance — ticker-level fund holdings are descoped to a recorded coverage gap
/// (TC-FDF-013). TEFAS exposes NAV, performance and asset-class allocation only, not ticker-level
/// holdings, so the fund ingest stores NAV/performance and records the per-fund holdings gap in
/// <c>coverage_metadata</c> — absence is expected, never a data-quality failure. The
/// <c>fund_holdings</c> schema/columns stay in place for future look-through phases.
/// Every test runs the real jobs against a migrated Testcontainers PostgreSQL and the FU §10
/// WireMock TEFAS source double, composing the same <c>AddMarketDataIngestion</c> registration
/// the API host uses.
/// </summary>
public sealed class FundHoldingsGapTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private const string UniverseRoute = "/canned-sources/tefas-universe";

    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static readonly DateOnly T = L2.Anchor.T;

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public FundHoldingsGapTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    /// <summary>TC-FDF-013 — ticker-level holdings are descoped to a recorded coverage gap:
    /// NAV and performance are stored, no ticker-level <c>fund_holdings</c> rows are ingested,
    /// nothing is quarantined, and the per-fund holdings gap is recorded (BR-FDF-004).</summary>
    [Fact]
    public async Task TC_FDF_013_Ticker_level_holdings_descoped_to_a_recorded_coverage_gap()
    {
        await ResetAsync();
        StubUniverse(Universe("TEF0001", "TEF0002", "TEF0003"));
        await using var host = BuildIngestion();

        await RunAsync(host, "fund-nav");
        await RunAsync(host, "fund-performance");
        var holdingsRun = await RunAsync(host, "fund-holdings");

        Assert.Equal("succeeded", holdingsRun.Status);

        await using var db = _postgres.CreateContext();

        // NAV and performance rows are stored as before (FR-FDF-001/002); the ingest keeps them.
        Assert.Equal(15, await db.FundNavs.CountAsync());
        Assert.Equal(5, await db.FundPerformances.CountAsync());

        // No ticker-level fund_holdings rows are ingested — the source does not expose them.
        Assert.Equal(0, await db.FundHoldings.CountAsync());
        Assert.Equal(0, holdingsRun.Written);

        // Absence is expected, not a data-quality failure: nothing is quarantined for the fund jobs.
        Assert.Equal(0, await db.QuarantinedFacts.CountAsync(q =>
            q.JobCode == "fund-nav" || q.JobCode == "fund-performance" || q.JobCode == "fund-holdings"));

        // The per-fund holdings gap is recorded — never silent (BR-FDF-004). TEF0002 is the
        // canonical case (FU §8): NAV + performance rows, no ticker-level holdings.
        var tef2Holdings = await CoverageRowAsync(db, "TEF0002", CoverageDataTypes.Holdings);
        Assert.Null(tef2Holdings.AvailableFrom);
        Assert.Null(tef2Holdings.AvailableTo);
        Assert.False(
            string.IsNullOrWhiteSpace(tef2Holdings.Notes),
            "the accepted holdings gap must be recorded, never silently ignored");
        Assert.Contains("gap", tef2Holdings.Notes, StringComparison.OrdinalIgnoreCase);

        // TEF0002's NAV + performance coverage is present and unchanged by the descope.
        Assert.Equal(T, (await CoverageRowAsync(db, "TEF0002", CoverageDataTypes.Nav)).AvailableTo);
        Assert.Equal(T, (await CoverageRowAsync(db, "TEF0002", CoverageDataTypes.Performance)).AvailableFrom);

        // The fund_holdings table/columns remain mapped for future look-through phases.
        Assert.NotNull(db.Model.FindEntityType(typeof(FundHolding)));
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    private IngestionHost BuildIngestion()
    {
        var mail = new RecordingMailDispatcher();
        var logs = new InMemorySerilogSink();
        var clock = new FakeTimeProvider(new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _postgres.ConnectionString,
                ["Ingestion:Funds:BaseUrl"] = _wireMock.BaseUrl,
                ["Ingestion:Funds:UniversePath"] = UniverseRoute,
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

    /// <summary>Seeds the FU universe, then clears the fund ingestion and run tables so each
    /// test re-creates its own state.</summary>
    private async Task ResetAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM coverage_metadata");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM fund_holdings");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM fund_performances");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM fund_navs");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM funds");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM quarantined_facts");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
    }

    /// <summary>Composes the TEFAS universe payload from the FU §10 per-fund canned bodies.</summary>
    private static JsonObject Universe(params string[] codes)
    {
        var funds = new JsonArray();
        foreach (var code in codes)
        {
            funds.Add(JsonNode.Parse(CannedSourceCatalog.Get(L2, $"tefas-{code.ToLowerInvariant()}").Body)!);
        }

        return new JsonObject
        {
            ["sourceRef"] = "tefas://funds",
            ["funds"] = funds,
        };
    }

    private void StubUniverse(JsonObject universe) =>
        _wireMock.StubJson(UniverseRoute, universe.ToJsonString());

    private static async Task<CoverageMetadata> CoverageRowAsync(
        Degerli.Persistence.DegerliDbContext db,
        string fundCode,
        string dataType) =>
        await db.CoverageMetadata.SingleAsync(row =>
            row.Scope == "fund" && row.FundId == fundCode && row.DataType == dataType);

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
                new Degerli.Api.Mail.OutboundMail("builder@degerli.test", alert.Subject, alert.BodyTr, alert.BodyEn),
                cancellationToken);
    }
}
