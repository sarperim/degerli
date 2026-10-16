using System.Net;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Core.Metrics;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Metrics;
using Degerli.Ingestion.Statements;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Degerli.Persistence.Seed;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// L2 acceptance for the accounting-profile metric gating (TKT-mdf-020; TC-MDF-057,
/// FR-MDF-013; <c>02</c> §6.2 v1.3). The on-demand <c>BANKA</c> (bank) and
/// <c>SIGORTA</c> (insurance) fixtures are ingested through the real <c>statements</c>
/// job, priced, and run through <c>metrics-recompute</c> against a migrated Testcontainers
/// PostgreSQL. For both, the ten industrial metric concepts are <b>NULL</b> — even though
/// BANKA carries a spurious <c>REV</c> 500 — while <c>pe</c>/<c>pb</c>/<c>roe</c> compute
/// normally. The same NULLs render through the existing stock sections as the
/// not-meaningful state (SCR-003/SCR-005), never as <c>0</c>-for-missing. Goldens are
/// hand-derived in FU §3b.
/// </summary>
public sealed class ProfileMetricServingTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    private const string StatementsPath = "/canned-sources/statements";

    /// <summary>The twelve profile-gated codes (ten industrial concepts, fcf_cagr ×3).</summary>
    private static readonly string[] GatedCodes =
    [
        MetricCodes.EvEbitda,
        MetricCodes.EvFcf,
        MetricCodes.FcfYield,
        MetricCodes.Roic,
        MetricCodes.GrossMargin,
        MetricCodes.OperatingMargin,
        MetricCodes.NetDebtEbitda,
        MetricCodes.InterestCoverage,
        MetricCodes.CurrentRatio,
        MetricCodes.FcfCagr3y,
        MetricCodes.FcfCagr5y,
        MetricCodes.FcfCagr10y,
    ];

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public ProfileMetricServingTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    /// <summary>
    /// TC-MDF-057 — the engine stores the profile-shaped NULL matrix: BANKA serves
    /// <c>pe</c> 30.0, <c>pb</c> 3.0, <c>roe</c> 0.10; SIGORTA <c>pe</c> 12.0,
    /// <c>pb</c> 1.2, <c>roe</c> 0.10; both write NULL for the twelve gated codes —
    /// no NULL is a <c>0</c>.
    /// </summary>
    [Fact]
    public async Task TC_MDF_057_engine_writes_the_financial_profile_null_matrix()
    {
        await SeedAsync();

        await using var db = _postgres.CreateContext();
        var bySymbol = await LoadMetricsAsync(db);

        var banka = bySymbol["BANKA"];
        AssertValue(banka, MetricCodes.Pe, 30.0m);
        AssertValue(banka, MetricCodes.Pb, 3.0m);
        AssertValue(banka, MetricCodes.Roe, 0.10m);
        AssertGatedNull(banka);

        var sigorta = bySymbol["SIGORTA"];
        AssertValue(sigorta, MetricCodes.Pe, 12.0m);
        AssertValue(sigorta, MetricCodes.Pb, 1.2m);
        AssertValue(sigorta, MetricCodes.Roe, 0.10m);
        AssertGatedNull(sigorta);

        // The seeded 16 stay on the natural industrial rules — ALFA's industrial metrics
        // still compute (the gate is profile-scoped, not global).
        var alfa = bySymbol["ALFA"];
        Assert.NotNull(alfa[MetricCodes.EvEbitda].Value);
        Assert.NotNull(alfa[MetricCodes.GrossMargin].Value);
        Assert.NotNull(alfa[MetricCodes.CurrentRatio].Value);
    }

    /// <summary>
    /// TC-MDF-057 — the served stock sections present the profile-gated metrics as the
    /// existing not-meaningful state: BANKA's profitability section serves <c>roe</c>
    /// (0.10) while <c>roic</c>/<c>gross_margin</c>/<c>operating_margin</c> are NULL with
    /// <c>notMeaningful</c> true; its valuation multiples serve <c>pe</c>/<c>pb</c> while
    /// <c>ev_ebitda</c>/<c>ev_fcf</c>/<c>fcf_yield</c> are not-meaningful — never zero.
    /// </summary>
    [Fact]
    public async Task TC_MDF_057_served_sections_render_profile_gaps_as_not_meaningful()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var profitability = await client.GetAsync("/api/v1/stocks/BANKA/profitability");
        Assert.Equal(HttpStatusCode.OK, profitability.StatusCode);
        using var profitabilityDocument = JsonDocument.Parse(await profitability.Content.ReadAsStringAsync());
        var profitabilityMetrics = profitabilityDocument.RootElement.GetProperty("metrics");

        AssertServedValue(profitabilityMetrics, MetricCodes.Roe, 0.10m, notMeaningful: false);
        foreach (var code in new[] { MetricCodes.Roic, MetricCodes.GrossMargin, MetricCodes.OperatingMargin })
        {
            AssertServedGap(profitabilityMetrics, code);
        }

        using var valuation = await client.GetAsync("/api/v1/stocks/BANKA/valuation");
        Assert.Equal(HttpStatusCode.OK, valuation.StatusCode);
        using var valuationDocument = JsonDocument.Parse(await valuation.Content.ReadAsStringAsync());
        var current = valuationDocument.RootElement.GetProperty("current");
        var strip = valuationDocument.RootElement.GetProperty("strip");

        Assert.Equal(30.0m, current.GetProperty(MetricCodes.Pe).GetDecimal());
        Assert.Equal(3.0m, current.GetProperty(MetricCodes.Pb).GetDecimal());
        foreach (var code in new[] { MetricCodes.EvEbitda, MetricCodes.EvFcf, MetricCodes.FcfYield })
        {
            Assert.Equal(JsonValueKind.Null, current.GetProperty(code).ValueKind);
            Assert.True(strip.GetProperty(code).GetProperty("notMeaningful").GetBoolean(), $"{code} served as meaningful");
            Assert.Equal(JsonValueKind.Null, strip.GetProperty(code).GetProperty("value").ValueKind);
        }

        // The financial-meaningful multiples remain meaningful in the strip.
        Assert.False(strip.GetProperty(MetricCodes.Pe).GetProperty("notMeaningful").GetBoolean());
        Assert.Equal(30.0m, strip.GetProperty(MetricCodes.Pe).GetProperty("value").GetDecimal());
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Seeds the L2 universe, ensures the on-demand BANKA/SIGORTA instruments (profiled
    /// from the checked-in classification), ingests their statements, prices both, and
    /// computes the canonical metrics at T through the registered jobs.
    /// </summary>
    private async Task SeedAsync()
    {
        await using (var db = _postgres.CreateContext())
        {
            await FixtureSeeder.ApplyAsync(db, L2);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM derived_metrics");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
        }

        var builder = new FixtureBuilder(_postgres.CreateContext());
        var banka = await EnsureInstrumentAsync(builder, "BANKA", AccountingProfiles.Bank);
        var sigorta = await EnsureInstrumentAsync(builder, "SIGORTA", AccountingProfiles.Insurance);

        await EnsurePriceAsync(builder, banka.Id, 30.00m);
        await EnsurePriceAsync(builder, sigorta.Id, 12.00m);

        await EnsureConstituentAsync(builder, banka.Id);
        await EnsureConstituentAsync(builder, sigorta.Id);

        await using var host = BuildIngestion();

        // BANKA — bank-shaped with the spurious REV 500 (FU §3b).
        _wireMock.Server.ResetMappings();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.StatementsBankBanka, path: StatementsPath);
        var bankaResult = await RunAsync(host, StatementsJob.Code);
        Assert.Equal(IngestResult.Succeeded, bankaResult.Status);
        Assert.Equal(0, bankaResult.Quarantined);

        // SIGORTA — insurance-shaped (FU §3b).
        _wireMock.Server.ResetMappings();
        _wireMock.StubCannedSource(L2, CannedSourceCatalog.StatementsInsuranceSigorta, path: StatementsPath);
        var sigortaResult = await RunAsync(host, StatementsJob.Code);
        Assert.Equal(IngestResult.Succeeded, sigortaResult.Status);
        Assert.Equal(0, sigortaResult.Quarantined);

        var metrics = await RunAsync(host, MetricsRecomputeJob.Code);
        Assert.Equal(IngestResult.Succeeded, metrics.Status);
    }

    private async Task<Instrument> EnsureInstrumentAsync(FixtureBuilder builder, string symbol, string profile)
    {
        await using var db = _postgres.CreateContext();
        var existing = await db.Instruments.SingleOrDefaultAsync(i => i.Symbol == symbol);
        if (existing is not null)
        {
            return existing;
        }

        return await builder.CreateInstrumentAsync(
            symbol,
            accountingProfile: profile,
            listingDate: new DateOnly(2010, 1, 4));
    }

    private async Task EnsurePriceAsync(FixtureBuilder builder, long instrumentId, decimal close)
    {
        await using var db = _postgres.CreateContext();
        if (await db.DailyPrices.AnyAsync(p => p.InstrumentId == instrumentId && p.PriceDate == T))
        {
            return;
        }

        await builder.CreatePriceAsync(instrumentId, T, close);
    }

    private async Task EnsureConstituentAsync(FixtureBuilder builder, long instrumentId)
    {
        await using var db = _postgres.CreateContext();
        var xu100 = await db.Indices.Where(i => i.Code == "XU100").Select(i => i.Id).SingleAsync();
        if (await db.IndexConstituents.AnyAsync(c => c.IndexId == xu100 && c.InstrumentId == instrumentId && c.EffectiveTo == null))
        {
            return;
        }

        await builder.CreateCurrentConstituentAsync("XU100", instrumentId, new DateOnly(2010, 1, 4));
    }

    private ServiceProvider BuildIngestion()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _postgres.ConnectionString,
                ["Ingestion:Prices:BaseUrl"] = _wireMock.BaseUrl,
                ["Ingestion:Kap:BaseUrl"] = _wireMock.BaseUrl,
                ["Ingestion:Statements:Path"] = StatementsPath,
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

    private static HttpClient CreateClient(DegerliAppFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    private static async Task<Dictionary<string, Dictionary<string, DerivedMetric>>> LoadMetricsAsync(DegerliDbContext db)
    {
        var symbols = await db.Instruments.ToDictionaryAsync(i => i.Id, i => i.Symbol);
        var rows = await db.DerivedMetrics.Where(m => m.AsOfDate == T).ToListAsync();
        return rows
            .GroupBy(r => symbols[r.InstrumentId])
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(r => r.MetricCode, r => r, StringComparer.Ordinal),
                StringComparer.Ordinal);
    }

    private static void AssertValue(
        IReadOnlyDictionary<string, DerivedMetric> rows,
        string code,
        decimal expected)
    {
        var value = rows[code].Value;
        Assert.NotNull(value);
        Assert.True(
            Math.Abs(value!.Value - expected) <= 0.00001m,
            $"{code}: expected {expected} (±1e-5) but was {value.Value}");
    }

    private static void AssertGatedNull(IReadOnlyDictionary<string, DerivedMetric> rows)
    {
        foreach (var code in GatedCodes)
        {
            var row = rows[code];
            Assert.Null(row.Value);
            Assert.Null(row.WindowYears);
            Assert.Null(row.RegimeBreak);
        }
    }

    private static void AssertServedValue(JsonElement metrics, string code, decimal expected, bool notMeaningful)
    {
        var entry = metrics.GetProperty(code);
        Assert.Equal(notMeaningful, entry.GetProperty("notMeaningful").GetBoolean());
        Assert.Equal(expected, entry.GetProperty("value").GetDecimal());
    }

    private static void AssertServedGap(JsonElement metrics, string code)
    {
        var entry = metrics.GetProperty(code);
        Assert.True(entry.GetProperty("notMeaningful").GetBoolean(), $"{code} served as meaningful");
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("value").ValueKind);
    }
}
