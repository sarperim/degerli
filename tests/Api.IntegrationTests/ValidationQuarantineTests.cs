using Degerli.Api.IntegrationTests.Harness;
using Degerli.Api.IntegrationTests.Harness.Mail;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Quarantine;
using Degerli.Ingestion.Validation;
using Degerli.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Serilog;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-mdf-006 acceptance — the validation &amp; quarantine pipeline (TC-MDF-019,
/// TC-MDF-020). Every invalid fact is quarantined with its reason code and payload
/// retained, is never written to its fact table, and alerts the builder; valid items in
/// the same payload still ingest (partial-batch behavior). The reason-code catalog is
/// FU §2. Runs the real <c>prices</c> job against a migrated Testcontainers PostgreSQL
/// and the FU §10 WireMock source double; the statement schema-mismatch class is driven
/// through the reusable validation framework (the statements adapter is TKT-mdf-003).
/// </summary>
public sealed class ValidationQuarantineTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public ValidationQuarantineTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    /// <summary>TC-MDF-019 — invalid facts are quarantined, never written: a decision
    /// table over the invalid classes (negative price, unparseable payload, schema
    /// mismatch) × (quarantine, write, drop).</summary>
    [Fact]
    public async Task TC_MDF_019_Invalid_facts_are_quarantined_never_written()
    {
        // --- NEGATIVE_PRICE + partial batch: ALFA close −5 among 16 otherwise-valid items.
        await ResetAsync(clearPrices: true);
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesInvalidNegativeClose);
        await using (var provider = BuildIngestion("/canned-sources/prices-invalid-negative-close"))
        {
            var result = await RunAsync(provider, "prices");
            Assert.Equal(15, result.Written);
            Assert.Equal(1, result.Quarantined);
        }

        await using (var db = _postgres.CreateContext())
        {
            // Valid items in the same payload ingest normally (partial-batch behavior).
            Assert.Equal(15, await db.DailyPrices.CountAsync(p => p.PriceDate == T));

            // Zero rows written for the invalid item.
            var alfaId = await db.Instruments.Where(i => i.Symbol == "ALFA").Select(i => i.Id).SingleAsync();
            Assert.Equal(0, await db.DailyPrices.CountAsync(p => p.InstrumentId == alfaId && p.PriceDate == T));

            var entry = Assert.Single(await db.QuarantinedFacts.Where(q => q.JobCode == "prices").ToListAsync());
            Assert.Equal("NEGATIVE_PRICE", entry.ReasonCode);
            Assert.Equal("open", entry.Status);
            Assert.Contains("ALFA", entry.PayloadJson);
        }

        // --- UNPARSEABLE_PAYLOAD: the body cannot be parsed at all.
        await ResetAsync(clearPrices: true);
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesUnparseable);
        await using (var provider = BuildIngestion("/canned-sources/prices-unparseable"))
        {
            var result = await RunAsync(provider, "prices");
            Assert.Equal(0, result.Written);
        }

        await using (var db = _postgres.CreateContext())
        {
            Assert.Equal(0, await db.DailyPrices.CountAsync(p => p.PriceDate == T));
            var entry = Assert.Single(await db.QuarantinedFacts.Where(q => q.JobCode == "prices").ToListAsync());
            Assert.Equal("UNPARSEABLE_PAYLOAD", entry.ReasonCode);
            Assert.False(string.IsNullOrWhiteSpace(entry.PayloadJson));
        }

        // --- SCHEMA_MISMATCH: a statement payload with a non-numeric value. The
        // statements adapter is TKT-mdf-003, so the reusable schema check (the same
        // framework every job validates through) is driven directly and the refusal is
        // persisted through the quarantine write path.
        await ResetAsync(clearPrices: true);
        int statementsBefore;
        await using (var db = _postgres.CreateContext())
        {
            statementsBefore = await db.FinancialStatements.CountAsync();
        }

        const string statementJson = """
            {
              "sourceRef": "kap://statements/ALFA",
              "symbol": "ALFA",
              "statements": [
                { "periodType": "FY", "periodEndDate": "2025-12-31", "statementType": "IS", "version": "as_reported", "revenue": 1000, "netIncome": 200 },
                { "periodType": "FY", "periodEndDate": "2025-12-31", "statementType": "BS", "version": "as_reported", "revenue": 1000, "netIncome": "N/A" }
              ]
            }
            """;

        await using (var provider = BuildIngestion("/canned-sources/prices-ok"))
        using (var scope = provider.Provider.CreateScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IPayloadSchemaValidator>();
            var quarantine = scope.ServiceProvider.GetRequiredService<IQuarantineService>();

            var failures = validator.ValidateNumericFields(statementJson, "statements", ["revenue", "netIncome"]);
            foreach (var failure in failures)
            {
                await quarantine.QuarantineAsync(new QuarantineEntry(
                    "statements",
                    "kap://statements/ALFA",
                    failure.PayloadJson,
                    failure.ReasonCode,
                    provider.Clock.GetUtcNow()));
            }
        }

        await using (var db = _postgres.CreateContext())
        {
            // The invalid item was never written to the fact table.
            Assert.Equal(statementsBefore, await db.FinancialStatements.CountAsync());

            var entry = Assert.Single(await db.QuarantinedFacts.Where(q => q.JobCode == "statements").ToListAsync());
            Assert.Equal("SCHEMA_MISMATCH", entry.ReasonCode);
            Assert.Contains("N/A", entry.PayloadJson);
        }
    }

    /// <summary>TC-MDF-020 — quarantine alerts the builder: a mail-double message plus a
    /// Serilog alert event naming the job and the reason.</summary>
    [Fact]
    public async Task TC_MDF_020_Quarantine_alerts_the_builder()
    {
        await ResetAsync(clearPrices: true);
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesInvalidNegativeClose);
        await using var provider = BuildIngestion("/canned-sources/prices-invalid-negative-close");

        await RunAsync(provider, "prices");

        var alert = Assert.Single(provider.Mail.Sent);
        Assert.Contains("prices", alert.Subject);
        Assert.Contains("NEGATIVE_PRICE", alert.Subject);

        Assert.True(provider.Logs.Contains(evt =>
            evt.RenderMessage().Contains("prices", StringComparison.Ordinal) &&
            evt.RenderMessage().Contains("NEGATIVE_PRICE", StringComparison.Ordinal)));
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

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

        return new IngestionHost(services.BuildServiceProvider(), mail, logs, clock);
    }

    private static async Task<IngestResult> RunAsync(IngestionHost host, string jobCode)
    {
        using var scope = host.Provider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IIngestionJobRunner>();
        return await runner.RunAsync(jobCode, new IngestionRequest(T));
    }

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

    private sealed class IngestionHost : IAsyncDisposable
    {
        public IngestionHost(
            ServiceProvider provider,
            RecordingMailDispatcher mail,
            InMemorySerilogSink logs,
            TimeProvider clock)
        {
            Provider = provider;
            Mail = mail;
            Logs = logs;
            Clock = clock;
        }

        public ServiceProvider Provider { get; }

        public RecordingMailDispatcher Mail { get; }

        public InMemorySerilogSink Logs { get; }

        public TimeProvider Clock { get; }

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
