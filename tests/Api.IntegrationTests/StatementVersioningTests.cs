using Degerli.Api.IntegrationTests.Harness;
using Degerli.Core.Metrics;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Metrics;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-mdf-013 acceptance — multiple restatements of one period are sequence-versioned
/// (TC-MDF-054; FR-MDF-002, FR-MDF-019, BR-MDF-011, 02 §3.1/§5.7). The statement
/// <c>version</c> is an integer sequence: <c>0</c> = as-reported, <c>1, 2, …</c> =
/// restatements in publication order. The wire payload still <em>declares</em>
/// <c>as_reported</c>/<c>restated</c>; the store assigns the sequence.
///
/// The REST2 fixture (FU §3b) publishes one FY2025 period three times: as-reported
/// (NI 60), restated (NI 75, 2026-06-20) and restated a second time (NI 80, later). The
/// served statement must be the highest version (v2, NI 80), the golden <c>pe</c> being
/// price 15.00 ÷ (80 / 50 shares) = <b>9.375</b> — distinct from the as-reported 12.5 and
/// the first-restatement 10.0 — with every earlier version retained.
/// </summary>
public sealed class StatementVersioningTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public StatementVersioningTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    /// <summary>
    /// TC-MDF-054 — multiple restatements of one period are sequence-versioned; the
    /// highest version serves and every earlier version is retained.
    /// </summary>
    [Fact]
    public async Task TC_MDF_054_multiple_restatements_are_sequence_versioned()
    {
        long restId = await ResetAsync();

        _wireMock.StubCannedSource(L2, CannedSourceCatalog.StatementsRestatedRestV2);
        await using var provider = BuildHost();

        // Ingest the three versions in publication order: v0 (NI 60), v1 (NI 75), v2 (NI 80).
        var statements = await RunAsync(provider, "statements");
        Assert.Equal(IngestResult.Succeeded, statements.Status);
        Assert.Equal(9, statements.Written); // 3 statement types × 3 versions

        await using (var db = _postgres.CreateContext())
        {
            var stored = await db.FinancialStatements
                .Where(s => s.InstrumentId == restId && s.PeriodType == "FY")
                .ToListAsync();

            // Three rows per statement type, versions 0, 1, 2 — no unique-constraint
            // violation on the second restatement (02 §3.1, issue 1).
            Assert.Equal(9, stored.Count);
            foreach (var statementType in new[] { "IS", "BS", "CF" })
            {
                var versions = stored
                    .Where(s => s.StatementType == statementType)
                    .Select(s => s.Version)
                    .OrderBy(v => v)
                    .ToList();
                Assert.Equal([0, 1, 2], versions);
            }

            // v0 (as-reported, NI 60), v1 (restated, 2026-06-20) and v2 (restated, later)
            // are all retained with their own NI.
            var income = stored.Where(s => s.StatementType == "IS").ToList();
            Assert.Equal(60m, await LineAsync(db, income.Single(s => s.Version == 0).Id, "NI"));
            Assert.Equal(75m, await LineAsync(db, income.Single(s => s.Version == 1).Id, "NI"));
            Assert.Equal(80m, await LineAsync(db, income.Single(s => s.Version == 2).Id, "NI"));
            Assert.Null(income.Single(s => s.Version == 0).RestatementDate);
            Assert.Equal(new DateOnly(2026, 6, 20), income.Single(s => s.Version == 1).RestatementDate);
            Assert.NotNull(income.Single(s => s.Version == 2).RestatementDate);
        }

        // Serving selects the highest version (v2, NI 80); the derived metrics carry the
        // restatement marker.
        await RunAsync(provider, MetricsRecomputeJob.Code);
        await using (var db = _postgres.CreateContext())
        {
            var pe = await db.DerivedMetrics
                .Where(m => m.InstrumentId == restId && m.MetricCode == MetricCodes.Pe && m.AsOfDate == T)
                .SingleAsync();
            Assert.Equal(9.375m, pe.Value!.Value, 6); // 15 / (80 / 50) — v2 serves, not 12.5 or 10.0
            Assert.True(pe.IsRested);
        }

        // Re-ingesting the identical payload is an idempotent no-op.
        var replay = await RunAsync(provider, "statements");
        Assert.Equal(IngestResult.Succeeded, replay.Status);
        Assert.Equal(0, replay.Written);
        Assert.Equal(9, replay.Unchanged);

        await using (var db = _postgres.CreateContext())
        {
            Assert.Equal(9, await db.FinancialStatements.CountAsync(s => s.InstrumentId == restId && s.PeriodType == "FY"));
        }
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    private ServiceProvider BuildHost()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _postgres.ConnectionString,
                ["Ingestion:Kap:BaseUrl"] = _wireMock.BaseUrl,
                ["Ingestion:Statements:Path"] = "/canned-sources/statements-restated-rest-v2",
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

    private async Task<long> ResetAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM derived_metrics");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");

        var restId = await db.Instruments.Where(i => i.Symbol == "REST").Select(i => i.Id).SingleAsync();

        // Re-create this test's own state: drop REST's seeded statement versions so the
        // REST2 payload publishes all three versions from scratch (no inter-test reads).
        var statementIds = await db.FinancialStatements
            .Where(s => s.InstrumentId == restId)
            .Select(s => s.Id)
            .ToListAsync();
        await db.FinLineItems.Where(l => statementIds.Contains(l.StatementId)).ExecuteDeleteAsync();
        await db.FinancialStatements.Where(s => s.InstrumentId == restId).ExecuteDeleteAsync();

        return restId;
    }

    private static async Task<decimal> LineAsync(DegerliDbContext db, long statementId, string itemCode) =>
        await db.FinLineItems
            .Where(l => l.StatementId == statementId && l.ItemCode == itemCode)
            .Select(l => l.Value)
            .SingleAsync();
}
