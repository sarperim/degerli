using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Statements;
using Degerli.Ingestion.Valuation;
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
/// TKT-val-010 acceptance — the financial-sector DCF verdict (TC-VAL-028, plan §7;
/// FR-VAL-001/003, <c>02</c> §6.4, <c>03</c> §5/§7 v1.4). For an instrument whose
/// <c>accounting_profile ∈ {bank, insurance}</c> a free-cash-flow-to-firm DCF does not
/// apply: baseline generation records the instrument with a <c>sector_not_applicable</c>
/// state (never a spurious FCF-derived baseline), the read endpoint serves that state
/// (distinct from the missing-inputs <c>200</c>), and the compute endpoint returns
/// <c>422 DCF_NOT_COMPUTABLE</c> with <c>params.reason = "financial_sector"</c> and an
/// honest sector explanation. A normal stock (ALFA) is unaffected (control).
///
/// The on-demand <c>BANKA</c> (bank) and <c>SIGORTA</c> (insurance) fixtures (FU §3b)
/// are ingested through the real statements job; complete FCF-capable lines are injected
/// onto BANKA so the exclusion is falsifiable — without the profile gate the baseline
/// job would derive a real <c>base_fcf</c> for it.
/// </summary>
public sealed class DcfFinancialSectorTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    /// <summary>The canonical-fact as-of / baseline build date (FU §11.5 "asOf R−6d").</summary>
    private static DateOnly BuildDate => L2.Anchor.Calendar(-6);

    private const string StatementsPath = "/canned-sources/statements";

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public DcfFinancialSectorTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    // -- TC-VAL-028 — baseline generation records sector_not_applicable -------------

    /// <summary>
    /// The <c>dcf-baselines</c> job records BANKA/SIGORTA with a <c>sector_not_applicable</c>
    /// state and no FCF-derived parameters — even though BANKA carries complete
    /// cash-flow/balance-sheet lines that an industrial treatment would turn into a
    /// baseline. GET serves the state (200), distinct from the missing-inputs state.
    /// </summary>
    [Fact]
    public async Task TC_VAL_028_baseline_generation_records_sector_not_applicable_without_fcf_baseline()
    {
        await SeedAsync();

        await using (var db = _postgres.CreateContext())
        {
            foreach (var symbol in new[] { "BANKA", "SIGORTA" })
            {
                var baseline = await ActiveBaselineAsync(db, symbol);
                using var document = JsonDocument.Parse(baseline.ParamsJson);
                var root = document.RootElement;

                Assert.Equal("sector_not_applicable", root.GetProperty("state").GetString());

                // No FCF-derived baseline row: the spurious/injected inputs never leak
                // into a plausible-looking baseline.
                var parameters = root.GetProperty("params");
                foreach (var fact in new[] { "base_fcf", "debt", "cash", "share_count" })
                {
                    Assert.False(
                        parameters.TryGetProperty(fact, out _),
                        $"expected no '{fact}' in {symbol}'s sector_not_applicable baseline");
                }
            }

            // Control: an industrial stock still gets its FCF-derived baseline.
            var alfa = await ActiveBaselineAsync(db, "ALFA");
            using var alfaDocument = JsonDocument.Parse(alfa.ParamsJson);
            Assert.Equal(100m, alfaDocument.RootElement.GetProperty("params").GetProperty("base_fcf").GetDecimal());
        }

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        foreach (var symbol in new[] { "BANKA", "SIGORTA" })
        {
            using var response = await GetDcfAsync(client, symbol);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = document.RootElement;

            // The not-applicable state — a distinct verdict from the missing-inputs state.
            Assert.Equal("sector_not_applicable", root.GetProperty("state").GetString());
            Assert.Equal("financial_sector", root.GetProperty("reason").GetString());
            Assert.False(root.TryGetProperty("fairValuePerShare", out _));

            // The canonical price is still served so the calculator can show the stock.
            Assert.Equal(T.ToString("yyyy-MM-dd"), root.GetProperty("price").GetProperty("asOf").GetString());
        }
    }

    // -- TC-VAL-028 — compute returns 422 financial_sector (distinct verdicts) -------

    /// <summary>
    /// A valid compute request against a financial-sector instrument returns
    /// <c>422 DCF_NOT_COMPUTABLE</c> with <c>params.reason = "financial_sector"</c> and
    /// the offending profile named — no field highlight (the whole model does not apply)
    /// and no point result. The control stock ALFA computes its unchanged goldens, and a
    /// math-constraint request stays on the distinct math path.
    /// </summary>
    [Fact]
    public async Task TC_VAL_028_compute_returns_422_financial_sector_distinct_from_missing_inputs_and_math()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        foreach (var (symbol, profile) in new[] { ("BANKA", "bank"), ("SIGORTA", "insurance") })
        {
            using var response = await PostComputeAsync(client, symbol, Baseline());

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("DCF_NOT_COMPUTABLE", problem.RootElement.GetProperty("code").GetString());

            var parameters = problem.RootElement.GetProperty("params");
            Assert.Equal("financial_sector", parameters.GetProperty("reason").GetString());
            Assert.Equal(profile, parameters.GetProperty("profile").GetString());

            // The whole model does not apply: no field highlight (the math path's shape).
            Assert.False(parameters.TryGetProperty("field", out _));
            Assert.False(problem.RootElement.TryGetProperty("fairValuePerShare", out _));
        }

        // Control: the industrial stock computes normally — goldens unchanged (TC-VAL-009).
        using var control = await PostComputeAsync(client, "ALFA", Baseline());
        Assert.Equal(HttpStatusCode.OK, control.StatusCode);
        using var controlDocument = JsonDocument.Parse(await control.Content.ReadAsStringAsync());
        Assert.Equal(13.1969m, controlDocument.RootElement.GetProperty("fairValuePerShare").GetDecimal(), 4);
        Assert.Equal(-0.5155m, controlDocument.RootElement.GetProperty("marginOfSafety").GetDecimal(), 4);

        // And the math-constraint verdict remains its own, distinct path.
        var badMath = Baseline();
        badMath["discount_rate"] = 0.03m;
        badMath["terminal_growth"] = 0.04m;
        using var math = await PostComputeAsync(client, "ALFA", badMath);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, math.StatusCode);
        using var mathProblem = JsonDocument.Parse(await math.Content.ReadAsStringAsync());
        Assert.Equal(
            "DISCOUNT_RATE_NOT_GREATER_THAN_TERMINAL_GROWTH",
            mathProblem.RootElement.GetProperty("params").GetProperty("constraint").GetString());
    }

    // ---------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------

    private async Task SeedAsync()
    {
        await using (var db = _postgres.CreateContext())
        {
            await FixtureSeeder.ApplyAsync(db, L2);
            await db.DcfBaselines.ExecuteDeleteAsync();
        }

        var builder = new FixtureBuilder(_postgres.CreateContext());
        var banka = await EnsureInstrumentAsync(builder, "BANKA", AccountingProfiles.Bank);
        var sigorta = await EnsureInstrumentAsync(builder, "SIGORTA", AccountingProfiles.Insurance);

        await EnsurePriceAsync(builder, banka.Id, 30.00m);
        await EnsurePriceAsync(builder, sigorta.Id, 12.00m);

        // The `dcf-baselines` job keys on active instruments; the on-demand fixtures are
        // created outside the universe sync, so mark them active explicitly.
        await using (var db = _postgres.CreateContext())
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE instruments SET status = 'active' WHERE symbol IN ('BANKA', 'SIGORTA')");
        }

        await using (var host = BuildIngestion())
        {
            _wireMock.Server.ResetMappings();
            _wireMock.StubCannedSource(L2, CannedSourceCatalog.StatementsBankBanka, path: StatementsPath);
            Assert.Equal(IngestResult.Succeeded, (await RunAsync(host, StatementsJob.Code)).Status);

            _wireMock.Server.ResetMappings();
            _wireMock.StubCannedSource(L2, CannedSourceCatalog.StatementsInsuranceSigorta, path: StatementsPath);
            Assert.Equal(IngestResult.Succeeded, (await RunAsync(host, StatementsJob.Code)).Status);
        }

        // DR-1 falsifiability: inject complete FCF-capable lines onto BANKA directly into
        // fin_line_items. An industrial treatment would derive a real base_fcf (150) from
        // exactly these lines; the profile exclusion must prevent a baseline entirely.
        var cfLines = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            [FinItemCodes.Ocf] = 250m,
            [FinItemCodes.Capex] = 100m,
        };
        var bsLines = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            [FinItemCodes.Cash] = 500m,
            [FinItemCodes.StDebt] = 2000m,
            [FinItemCodes.LtDebt] = 1000m,
            [FinItemCodes.SharesDiluted] = 300m,
        };
        await builder.UpsertStatementLinesAsync(banka.Id, "CF", new DateOnly(2025, 12, 31), cfLines);
        await builder.UpsertStatementLinesAsync(banka.Id, "BS", new DateOnly(2025, 12, 31), bsLines);

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var scope = factory.Services.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IIngestionJobRunner>();
        var result = await runner.RunAsync(DcfBaselineGenerationJob.Code, new IngestionRequest(BuildDate));
        Assert.Equal(IngestResult.Succeeded, result.Status);
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

    private static async Task<DcfBaseline> ActiveBaselineAsync(DegerliDbContext db, string symbol)
    {
        var instrumentId = await db.Instruments
            .Where(i => i.Symbol == symbol)
            .Select(i => i.Id)
            .SingleAsync();
        return await db.DcfBaselines
            .Where(b => b.InstrumentId == instrumentId && b.IsActive)
            .OrderByDescending(b => b.Version)
            .FirstAsync();
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

    private static Task<HttpResponseMessage> GetDcfAsync(HttpClient client, string symbol)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/stocks/{symbol}/dcf");
        request.Headers.Add("X-Forwarded-For", Guid.NewGuid().ToString("N"));
        return client.SendAsync(request);
    }

    private static Dictionary<string, object?> Baseline() => new()
    {
        ["base_fcf"] = 100m,
        ["growth_rate"] = 0.10m,
        ["horizon_years"] = 5,
        ["terminal_growth"] = 0.03m,
        ["discount_rate"] = 0.12m,
        ["debt"] = 400m,
        ["cash"] = 200m,
        ["share_count"] = 100m,
    };

    private static async Task<HttpResponseMessage> PostComputeAsync(
        HttpClient client,
        string symbol,
        IDictionary<string, object?> payload)
    {
        var csrf = await FetchCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/stocks/{symbol}/dcf/compute")
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Add("X-CSRF-Token", csrf);
        request.Headers.Add("X-Forwarded-For", Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }

    private static async Task<string> FetchCsrfTokenAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/csrf-token");
        request.Headers.Add("X-Forwarded-For", Guid.NewGuid().ToString("N"));
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("token").GetString()!;
    }
}
