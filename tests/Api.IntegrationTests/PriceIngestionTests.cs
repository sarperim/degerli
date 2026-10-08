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
/// TKT-mdf-002 acceptance — the <c>prices</c> EOD job and the fact-storage invariants
/// (TC-MDF-001, TC-MDF-002, TC-MDF-008, TC-MDF-052). Every test runs the real job
/// against a migrated Testcontainers PostgreSQL and the FU §10 WireMock source double,
/// composing the same <c>AddMarketDataIngestion</c> registration the API host uses.
/// TC-MDF-009 (schema invariants) is the structural conformance suite
/// <see cref="SchemaConformanceTests"/> — this ticket's storage invariants rest on it.
/// </summary>
public sealed class PriceIngestionTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public PriceIngestionTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    /// <summary>TC-MDF-001 — EOD price ingest stores dated, provenanced rows.</summary>
    [Fact]
    public async Task TC_MDF_001_Eod_price_ingest_stores_dated_provenanced_rows()
    {
        await ResetAsync(clearPrices: true);
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesOk);
        await using var provider = BuildIngestion("/canned-sources/prices-ok");

        var result = await RunAsync(provider, "prices");

        Assert.Equal("succeeded", result.Status);
        Assert.Equal(16, result.Written);
        Assert.Equal(0, result.Quarantined);

        await using var db = _postgres.CreateContext();
        var rows = await db.DailyPrices.Where(p => p.PriceDate == T).ToListAsync();
        Assert.Equal(16, rows.Count);

        var symbols = await db.Instruments.ToDictionaryAsync(i => i.Id, i => i.Symbol);
        var expected = L2.Prices.Where(p => p.Date == T).ToDictionary(p => p.Symbol, p => p);
        foreach (var row in rows)
        {
            var fact = expected[symbols[row.InstrumentId]];
            Assert.Equal(fact.CloseRaw, row.CloseRaw);
            Assert.Equal(fact.Volume, row.Volume);
            Assert.Equal($"isbank://eod/{T:yyyy-MM-dd}", row.SourceRef);
            Assert.False(string.IsNullOrWhiteSpace(row.SourceRef));
            Assert.NotEqual(default, row.RecordedAt);
        }
    }

    /// <summary>TC-MDF-002 — Price ingest is idempotent.</summary>
    [Fact]
    public async Task TC_MDF_002_Price_ingest_is_idempotent()
    {
        await ResetAsync(clearPrices: true);
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesOk);
        await using var provider = BuildIngestion("/canned-sources/prices-ok");

        var first = await RunAsync(provider, "prices");
        var snapshot = await PriceSnapshotAsync();

        var second = await RunAsync(provider, "prices");
        var after = await PriceSnapshotAsync();

        Assert.Equal(16, first.Written);
        Assert.Equal(0, second.Written);
        Assert.Equal(16, second.Unchanged);
        Assert.Equal(16, snapshot.Count);
        Assert.Equal(snapshot.Count, after.Count);
        Assert.All(snapshot, pair => Assert.Equal(pair.Value, after[pair.Key]));

        await using var db = _postgres.CreateContext();
        var duplicates = await db.DailyPrices
            .Where(p => p.PriceDate == T)
            .GroupBy(p => new { p.InstrumentId, p.PriceDate })
            .Where(g => g.Count() > 1)
            .CountAsync();
        Assert.Equal(0, duplicates);

        // No ledger anomaly: both runs succeeded, no failed/partial rows.
        var runs = await db.IngestRuns.Where(r => r.JobCode == "prices").ToListAsync();
        Assert.Equal(2, runs.Count);
        Assert.All(runs, r => Assert.Equal("succeeded", r.Status));
    }

    /// <summary>TC-MDF-008 — Facts without provenance are refused.</summary>
    [Fact]
    public async Task TC_MDF_008_Facts_without_provenance_are_refused()
    {
        await ResetAsync(clearPrices: true);
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesMissingProvenance);
        await using var provider = BuildIngestion("/canned-sources/prices-missing-provenance");

        var result = await RunAsync(provider, "prices");

        Assert.Equal(0, result.Written);

        await using var db = _postgres.CreateContext();
        Assert.Equal(0, await db.DailyPrices.CountAsync(p => p.PriceDate == T));

        var quarantine = await db.QuarantinedFacts
            .Where(q => q.JobCode == "prices")
            .ToListAsync();
        var entry = Assert.Single(quarantine);
        Assert.Equal("MISSING_PROVENANCE", entry.ReasonCode);
        Assert.Equal("open", entry.Status);
        Assert.False(string.IsNullOrWhiteSpace(entry.PayloadJson));
    }

    /// <summary>TC-MDF-052 — Conflicting price re-publication is quarantined, never
    /// overwritten.</summary>
    [Fact]
    public async Task TC_MDF_052_Conflicting_price_republication_is_quarantined()
    {
        // Precondition: T prices are already ingested (fixture seed stores ALFA close 20.00).
        await ResetAsync(clearPrices: false);
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesConflictingValue);
        await using var provider = BuildIngestion("/canned-sources/prices-conflicting-value");

        await using (var before = _postgres.CreateContext())
        {
            var alfaId = await before.Instruments.Where(i => i.Symbol == "ALFA").Select(i => i.Id).SingleAsync();
            var stored = await before.DailyPrices.SingleAsync(p => p.InstrumentId == alfaId && p.PriceDate == T);
            Assert.Equal(20.00m, stored.CloseRaw);
        }

        var result = await RunAsync(provider, "prices");

        await using var db = _postgres.CreateContext();

        // The stored fact is unchanged — never overwritten (FR-MDF-015).
        var alfa = await db.Instruments.Where(i => i.Symbol == "ALFA").Select(i => i.Id).SingleAsync();
        var kept = await db.DailyPrices.SingleAsync(p => p.InstrumentId == alfa && p.PriceDate == T);
        Assert.Equal(20.00m, kept.CloseRaw);

        // Only the conflicting instrument was quarantined; the identical re-sends were
        // idempotent no-ops.
        Assert.Equal(0, result.Written);
        Assert.Equal(15, result.Unchanged);
        Assert.Equal(1, result.Quarantined);

        var quarantine = await db.QuarantinedFacts.Where(q => q.JobCode == "prices").ToListAsync();
        var entry = Assert.Single(quarantine);
        Assert.Equal("CONFLICTING_VALUE", entry.ReasonCode);
        Assert.Contains("ALFA", entry.PayloadJson);
        Assert.False(string.IsNullOrWhiteSpace(entry.PayloadJson));

        // The builder is alerted: a mail-double message and a log event name the reason.
        var alert = Assert.Single(provider.Mail.Sent);
        Assert.Contains("CONFLICTING_VALUE", alert.Subject);
        Assert.True(provider.Logs.Contains(evt =>
            evt.MessageTemplate.Text.Contains("CONFLICTING_VALUE", StringComparison.Ordinal)));
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// The ingestion composition used in production, retargeted at the Testcontainers
    /// database and the WireMock source double. A recording mail double replaces the
    /// alerting seam; a Serilog test sink captures the log alerts.
    /// </summary>
    private IngestionHost BuildIngestion(string pricesPath)
    {
        var mail = new RecordingMailDispatcher();
        var logs = new InMemorySerilogSink();
        var clock = new FakeTimeProvider(new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _postgres.ConnectionString,
                ["Ingestion:Prices:BaseUrl"] = _wireMock.BaseUrl,
                ["Ingestion:Prices:Path"] = pricesPath,
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

    /// <summary>Applies the FU universe (instruments are the price FKs), clears the
    /// ingestion tables so the test re-creates its own state, optionally clearing prices.</summary>
    private async Task ResetAsync(bool clearPrices)
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM quarantined_facts");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
        if (clearPrices)
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM daily_prices");
        }
    }

    private async Task<Dictionary<string, decimal>> PriceSnapshotAsync()
    {
        await using var db = _postgres.CreateContext();
        var symbols = await db.Instruments.ToDictionaryAsync(i => i.Id, i => i.Symbol);
        var rows = await db.DailyPrices.Where(p => p.PriceDate == T).ToListAsync();
        return rows.ToDictionary(
            r => symbols[r.InstrumentId],
            r => r.CloseRaw,
            StringComparer.Ordinal);
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

    /// <summary>Funnels the ingestion alert seam into the shared recording mail double,
    /// so a quarantine alert is asserted exactly like any other outbound message.</summary>
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
