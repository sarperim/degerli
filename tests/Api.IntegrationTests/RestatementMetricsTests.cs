using Degerli.Api.Infrastructure;
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
/// TKT-mdf-009 acceptance — restatement handling in the metrics pipeline (TC-MDF-032;
/// FR-MDF-019, BR-MDF-011, 02 §5.7). REST carries an <c>as_reported</c> FY2025 version
/// (NI 60) and a later <c>restated</c> version (NI 75, restatement date 2026-06-20); the
/// metrics engine must serve the latest restated value and flag it while retaining both
/// versions for point-in-time integrity.
///
/// The golden <c>pe</c>: price 15.00 ÷ (restated NI 75 / 50 shares) = <b>10.0</b>;
/// the as-reported basis gives 15.00 ÷ (60 / 50) = 12.5 — asserted before the restated
/// version is (re-)ingested to observe the as_reported-only → restated-arrives transition.
/// </summary>
public sealed class RestatementMetricsTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public RestatementMetricsTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    /// <summary>TC-MDF-032 — latest restated serves, marked; as-reported retained.</summary>
    [Fact]
    public async Task TC_MDF_032_latest_restated_serves_marked_as_reported_retained()
    {
        await ResetAsync();

        // Reach the as_reported-only state: keep REST's original version, drop the restated one.
        long restId;
        await using (var db = _postgres.CreateContext())
        {
            restId = await db.Instruments.Where(i => i.Symbol == "REST").Select(i => i.Id).SingleAsync();
            var restatedIds = await db.FinancialStatements
                .Where(s => s.InstrumentId == restId && s.Version == "restated")
                .Select(s => s.Id)
                .ToListAsync();
            Assert.NotEmpty(restatedIds);
            await db.FinLineItems
                .Where(l => restatedIds.Contains(l.StatementId))
                .ExecuteDeleteAsync();
            await db.FinancialStatements
                .Where(s => restatedIds.Contains(s.Id))
                .ExecuteDeleteAsync();
        }

        await using var provider = BuildHost();

        // As-reported-only: the original NI 60 basis serves (pe = 15 / 1.2 = 12.5), unflagged.
        await RunAsync(provider, MetricsRecomputeJob.Code);
        await using (var db = _postgres.CreateContext())
        {
            var pe = await MetricAsync(db, restId, MetricCodes.Pe);
            Assert.Equal(12.5m, pe.Value!.Value, 6);
            Assert.False(pe.IsRested);
        }

        // The restated KAP payload arrives (FU §10 statements-restated-rest).
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.StatementsRestatedRest);
        var statements = await RunAsync(provider, "statements");
        Assert.Equal("succeeded", statements.Status);

        // Recompute: the latest restated version serves and is marked.
        await RunAsync(provider, MetricsRecomputeJob.Code);
        await using (var db = _postgres.CreateContext())
        {
            var pe = await MetricAsync(db, restId, MetricCodes.Pe);
            Assert.Equal(10.0m, pe.Value!.Value, 6); // 15 / (75/50)
            Assert.True(pe.IsRested);

            // Every REST metric carries the restatement marker (is_rested propagation),
            // which is what the honest-data envelope renders as `restated: true`.
            var rows = await db.DerivedMetrics
                .Where(m => m.InstrumentId == restId && m.AsOfDate == T)
                .ToListAsync();
            Assert.Equal(MetricCodes.All.Count, rows.Count);
            Assert.All(rows, r => Assert.True(r.IsRested));
            var envelope = DataEnvelope.Figure(pe.Value, T, restated: pe.IsRested);
            Assert.True(envelope.Restated);

            // Both statement versions remain stored and are queryable by the version filter.
            var restatements = await db.FinancialStatements
                .Where(s => s.InstrumentId == restId && s.PeriodType == "FY")
                .ToListAsync();

            var restated = restatements.Where(s => s.Version == "restated").ToList();
            var asReported = restatements.Where(s => s.Version == "as_reported").ToList();
            Assert.Equal(3, restated.Count);   // IS / BS / CF
            Assert.Equal(3, asReported.Count); // IS / BS / CF

            // The restated income statement reports NI 75 (as-reported retains 60), and the
            // restatement date is recorded (FU §5 footnote; BR-MDF-011).
            var restatedIs = restated.Single(s => s.StatementType == "IS");
            var asReportedIs = asReported.Single(s => s.StatementType == "IS");
            Assert.Equal(new DateOnly(2026, 6, 20), restatedIs.RestatementDate);
            Assert.Null(asReportedIs.RestatementDate);
            Assert.Equal(75m, await LineAsync(db, restatedIs.Id, "NI"));
            Assert.Equal(60m, await LineAsync(db, asReportedIs.Id, "NI"));
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
                ["Ingestion:Statements:Path"] = "/canned-sources/statements-restated-rest",
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
        await db.Database.ExecuteSqlRawAsync("DELETE FROM derived_metrics");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
    }

    private static async Task<DerivedMetric> MetricAsync(DegerliDbContext db, long instrumentId, string metricCode) =>
        await db.DerivedMetrics
            .Where(m => m.InstrumentId == instrumentId && m.MetricCode == metricCode && m.AsOfDate == T)
            .SingleAsync();

    private static async Task<decimal> LineAsync(DegerliDbContext db, long statementId, string itemCode) =>
        await db.FinLineItems
            .Where(l => l.StatementId == statementId && l.ItemCode == itemCode)
            .Select(l => l.Value)
            .SingleAsync();
}
