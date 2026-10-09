using System.Text.Json;
using System.Text.Json.Nodes;
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
/// TKT-fdf-001 acceptance — the TEFAS ingestion adapters and the three fund jobs
/// (TC-FDF-001, TC-FDF-002, TC-FDF-003, TC-FDF-004, TC-FDF-009, TC-FDF-011). Every test
/// runs the real job against a migrated Testcontainers PostgreSQL and the FU §10 WireMock
/// TEFAS source double, composing the same <c>AddMarketDataIngestion</c> registration the
/// API host uses. The per-fund <c>tefas-*</c> canned bodies are composed into the
/// universe payload the adapter consumes, so the goldens are FU §8-exact.
/// </summary>
public sealed class FundIngestionTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private const string UniverseRoute = "/canned-sources/tefas-universe";

    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static readonly DateOnly T = L2.Anchor.T;

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public FundIngestionTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    /// <summary>TC-FDF-001 — NAV history ingest is dated and idempotent.</summary>
    [Fact]
    public async Task TC_FDF_001_Nav_history_ingest_is_dated_and_idempotent()
    {
        await ResetAsync();
        StubUniverse(Universe("TEF0001", "TEF0002", "TEF0003"));
        await using var provider = BuildIngestion();

        var first = await RunAsync(provider, "fund-nav");

        Assert.Equal("succeeded", first.Status);
        Assert.Equal(15, first.Written);
        Assert.Equal(0, first.Quarantined);
        Assert.Equal(0, first.Skipped);

        await using var db = _postgres.CreateContext();
        Assert.Equal(15, await db.FundNavs.CountAsync());

        foreach (var expected in L2.FundNavs.Where(n => n.FundCode == "TEF0001"))
        {
            var row = await db.FundNavs.SingleAsync(n => n.FundId == "TEF0001" && n.NavDate == expected.NavDate);
            Assert.Equal(expected.NavValue, row.NavValue);
            Assert.False(string.IsNullOrWhiteSpace(row.SourceRef));
            Assert.NotEqual(default, row.RecordedAt);
        }

        var snapshot = await db.FundNavs.AsNoTracking().ToDictionaryAsync(n => (n.FundId, n.NavDate), n => n.NavValue);

        // Re-run: one row per fund per day, append-only (no updates).
        var second = await RunAsync(provider, "fund-nav");
        Assert.Equal(0, second.Written);
        Assert.Equal(15, second.Unchanged);

        await using var after = _postgres.CreateContext();
        var afterRows = await after.FundNavs.AsNoTracking().ToListAsync();
        Assert.Equal(15, afterRows.Count);
        Assert.All(afterRows, row => Assert.Equal(snapshot[(row.FundId, row.NavDate)], row.NavValue));

        var duplicates = afterRows
            .GroupBy(n => new { n.FundId, n.NavDate })
            .Count(g => g.Count() > 1);
        Assert.Equal(0, duplicates);
    }

    /// <summary>TC-FDF-002 — performance ingest stores rows as published; absence is a
    /// recorded gap, not an error.</summary>
    [Fact]
    public async Task TC_FDF_002_Performance_ingest_stores_rows_as_published()
    {
        await ResetAsync();
        StubUniverse(Universe("TEF0001", "TEF0002", "TEF0003"));
        await using var provider = BuildIngestion();

        var result = await RunAsync(provider, "fund-performance");

        Assert.Equal("succeeded", result.Status);
        Assert.Equal(5, result.Written);
        Assert.Equal(0, result.Quarantined);

        await using var db = _postgres.CreateContext();

        var tef1 = await db.FundPerformances.Where(p => p.FundId == "TEF0001").OrderBy(p => p.Period).ToListAsync();
        Assert.Equal(3, tef1.Count);
        Assert.Equal(0.025m, tef1.Single(p => p.Period == "1M").ReturnValue);
        Assert.Equal(0.060m, tef1.Single(p => p.Period == "3M").ReturnValue);
        Assert.Equal(0.180m, tef1.Single(p => p.Period == "1Y").ReturnValue);
        Assert.All(tef1, p => Assert.Equal(T, p.AsOfDate));

        var tef2 = await db.FundPerformances.Where(p => p.FundId == "TEF0002").ToListAsync();
        Assert.Equal(2, tef2.Count);
        Assert.Equal(0.012m, tef2.Single(p => p.Period == "1M").ReturnValue);
        Assert.Equal(0.090m, tef2.Single(p => p.Period == "1Y").ReturnValue);

        // TEF0003 publishes no performance: a recorded gap, not an error.
        Assert.Equal(0, await db.FundPerformances.CountAsync(p => p.FundId == "TEF0003"));
    }

    /// <summary>TC-FDF-003 — holdings ingest with partial coverage and symbol matching.</summary>
    [Fact]
    public async Task TC_FDF_003_Holdings_ingest_with_partial_coverage_and_symbol_matching()
    {
        await ResetAsync();
        StubUniverse(Universe("TEF0001", "TEF0002", "TEF0003"));
        await using var provider = BuildIngestion();

        var result = await RunAsync(provider, "fund-holdings");

        Assert.Equal("succeeded", result.Status);
        Assert.Equal(4, result.Written);

        await using var db = _postgres.CreateContext();
        var alfaId = await db.Instruments.Where(i => i.Symbol == "ALFA").Select(i => i.Id).SingleAsync();
        var epslId = await db.Instruments.Where(i => i.Symbol == "EPSL").Select(i => i.Id).SingleAsync();

        var tef1 = await db.FundHoldings.Where(h => h.FundId == "TEF0001").OrderBy(h => h.LineNo).ToListAsync();
        Assert.Equal(3, tef1.Count);
        Assert.Equal(alfaId, tef1[0].InstrumentId);
        Assert.Equal(0.05m, tef1[0].Weight);
        Assert.Equal(250_000m, tef1[0].Units);
        Assert.Equal(epslId, tef1[1].InstrumentId);
        Assert.Equal(0.03m, tef1[1].Weight);
        Assert.Equal(120_000m, tef1[1].Units);
        Assert.Null(tef1[2].InstrumentId);
        Assert.Equal("Yabancı Hisse X", tef1[2].NameRaw);

        // TEF0002 publishes no holdings: gap recorded, no rows.
        Assert.False(await db.FundHoldings.AnyAsync(h => h.FundId == "TEF0002"));

        // TEF0003's single partial line is stored honestly (NULL weight/units).
        var tef3 = Assert.Single(await db.FundHoldings.Where(h => h.FundId == "TEF0003").ToListAsync());
        Assert.Equal("Hisse Y", tef3.NameRaw);
        Assert.Null(tef3.Weight);
        Assert.Null(tef3.Units);

        Assert.All(
            await db.FundHoldings.AsNoTracking().ToListAsync(),
            h => Assert.Equal(T, h.AsOfDate));
    }

    /// <summary>TC-FDF-004 — the fund universe is bounded to equity and equity-heavy mixed
    /// funds; out-of-scope types are skipped-and-counted (never quarantined).</summary>
    [Fact]
    public async Task TC_FDF_004_Fund_universe_is_bounded_to_equity_and_equity_heavy_mixed()
    {
        await ResetAsync();

        var universe = Universe("TEF0001", "TEF0002", "TEF0003");
        universe["funds"]!.AsArray().Add(OutOfScopeFund("TEF0004", "Fon Borçlanma", "bond"));
        universe["funds"]!.AsArray().Add(OutOfScopeFund("TEF0005", "Fon Para Piyasası", "money market"));
        StubUniverse(universe);

        await using var provider = BuildIngestion();
        var result = await RunAsync(provider, "fund-nav");

        Assert.Equal("succeeded", result.Status);
        Assert.Equal(2, result.Skipped);
        Assert.Equal(0, result.Quarantined);

        await using var db = _postgres.CreateContext();
        var fundCodes = await db.Funds.Select(f => f.Code).OrderBy(c => c).ToListAsync();
        Assert.Equal(new[] { "TEF0001", "TEF0002", "TEF0003" }, fundCodes);
        Assert.Equal(0, await db.Funds.CountAsync(f => f.Code == "TEF0004" || f.Code == "TEF0005"));
        Assert.Equal(0, await db.FundNavs.CountAsync(n => n.FundId == "TEF0004" || n.FundId == "TEF0005"));

        // The skip is visible in the run ledger's stats_json.
        var run = await db.IngestRuns.SingleAsync(r => r.JobCode == "fund-nav");
        using var stats = JsonDocument.Parse(run.StatsJson!);
        Assert.Equal(2, stats.RootElement.GetProperty("skipped").GetInt32());

        // Out-of-scope funds are a scope bound, not a data-quality failure: nothing quarantined.
        Assert.Equal(0, await db.QuarantinedFacts.CountAsync(q => q.JobCode == "fund-nav"));
    }

    /// <summary>TC-FDF-009 — append-only retention: identical re-publication is an idempotent
    /// no-op; a conflicting NAV follows the Q1 quarantine rule.</summary>
    [Fact]
    public async Task TC_FDF_009_Append_only_retention()
    {
        await ResetAsync();
        StubUniverse(Universe("TEF0001", "TEF0002", "TEF0003"));
        await using var provider = BuildIngestion();

        var first = await RunAsync(provider, "fund-nav");
        Assert.Equal(15, first.Written);

        // Identical re-publication: idempotent no-op, no row touched.
        var second = await RunAsync(provider, "fund-nav");
        Assert.Equal(0, second.Written);
        Assert.Equal(15, second.Unchanged);

        // Conflicting value for TEF0001's T NAV: stored fact is kept, conflict quarantined.
        var conflicting = Universe("TEF0001", "TEF0002", "TEF0003");
        var tef1Navs = conflicting["funds"]!.AsArray()[0]!["navs"]!.AsArray();
        var target = tef1Navs.Single(n => n!["date"]!.GetValue<string>() == T.ToString("yyyy-MM-dd"))!;
        target["value"] = 99.99m;
        StubUniverse(conflicting);

        var third = await RunAsync(provider, "fund-nav");
        Assert.Equal(0, third.Written);
        Assert.Equal(14, third.Unchanged);
        Assert.Equal(1, third.Quarantined);

        await using var db = _postgres.CreateContext();

        // The stored fact is unchanged — never overwritten (BR-FDF-003, Q1).
        var kept = await db.FundNavs.SingleAsync(n => n.FundId == "TEF0001" && n.NavDate == T);
        Assert.Equal(10.25m, kept.NavValue);

        // Append-only: no deletion, still one row per fund per day.
        Assert.Equal(15, await db.FundNavs.CountAsync());

        var quarantine = await db.QuarantinedFacts.Where(q => q.JobCode == "fund-nav").ToListAsync();
        var entry = Assert.Single(quarantine);
        Assert.Equal("CONFLICTING_VALUE", entry.ReasonCode);
        Assert.Contains("TEF0001", entry.PayloadJson);
        Assert.False(string.IsNullOrWhiteSpace(entry.PayloadJson));

        var alert = Assert.Single(provider.Mail.Sent);
        Assert.Contains("CONFLICTING_VALUE", alert.Subject);
    }

    /// <summary>TC-FDF-011 — holdings→instrument link integrity holds both ways.</summary>
    [Fact]
    public async Task TC_FDF_011_Holdings_to_instrument_link_integrity()
    {
        await ResetAsync();
        StubUniverse(Universe("TEF0001", "TEF0002", "TEF0003"));
        await using var provider = BuildIngestion();

        await RunAsync(provider, "fund-holdings");

        await using var db = _postgres.CreateContext();
        var holdings = await db.FundHoldings.AsNoTracking().ToListAsync();

        // Every non-NULL instrument_id resolves to an existing MDF instrument.
        var instruments = await db.Instruments.AsNoTracking().ToDictionaryAsync(i => i.Id, i => i.Symbol);
        foreach (var holding in holdings.Where(h => h.InstrumentId is not null))
        {
            Assert.True(instruments.ContainsKey(holding.InstrumentId!.Value));
        }

        // The matched TEF0001 lines carry the expected symbols.
        var matchedSymbols = holdings
            .Where(h => h.FundId == "TEF0001" && h.InstrumentId is not null)
            .Select(h => instruments[h.InstrumentId!.Value])
            .OrderBy(s => s)
            .ToList();
        Assert.Equal(new[] { "ALFA", "EPSL" }, matchedSymbols);

        // The unmatched line keeps name_raw with a NULL id — never dropped.
        var unmatched = holdings.Where(h => h.InstrumentId is null).ToList();
        Assert.Contains(unmatched, h => h.NameRaw == "Yabancı Hisse X");
        Assert.All(unmatched, h => Assert.False(string.IsNullOrWhiteSpace(h.NameRaw)));
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

    /// <summary>Seeds the FU universe (instruments are the holdings FKs), then clears the
    /// fund ingestion tables so each test re-creates its own state.</summary>
    private async Task ResetAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM fund_holdings");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM fund_performances");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM fund_navs");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM funds");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM quarantined_facts");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
    }

    /// <summary>Composes the TEFAS universe payload from the FU §10 per-fund canned bodies,
    /// so the fixture goldens stay the single source of truth.</summary>
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

    /// <summary>A fund outside the V1 universe bound, carrying an in-scope-looking NAV so a
    /// bound leak would be observable.</summary>
    private static JsonObject OutOfScopeFund(string code, string name, string type) =>
        new()
        {
            ["sourceRef"] = $"tefas://funds/{code}",
            ["fund"] = new JsonObject { ["code"] = code, ["name"] = name, ["type"] = type },
            ["navs"] = new JsonArray(
                new JsonObject { ["date"] = T.ToString("yyyy-MM-dd"), ["value"] = 12.34m }),
            ["performance"] = new JsonArray(),
            ["holdings"] = new JsonArray(),
        };

    private void StubUniverse(JsonObject universe) =>
        _wireMock.StubJson(UniverseRoute, universe.ToJsonString());

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
