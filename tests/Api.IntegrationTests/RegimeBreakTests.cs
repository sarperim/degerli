using System.Net;
using System.Text;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Core.Metrics;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Metrics;
using Degerli.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-mdf-018 acceptance — the TMS 29 regime-break flag on spanning CAGRs
/// (TC-MDF-060; FR-MDF-013/014, UC-MDF-005; architecture <c>02</c> §3.1/§6.2,
/// <c>03</c> §4). The TMS 29 inflation-accounting transition took effect for fiscal
/// periods ending <b>2023-12-31</b>; a CAGR whose compounding window spans that
/// boundary (min FY period-end &lt; 2023-12-31 ≤ max FY period-end) is flagged
/// <c>regime_break = TRUE</c>.
/// <list type="bullet">
/// <item>ALFA (deep history FY2015…FY2025) — every CAGR window spans → TRUE.</item>
/// <item>ZETA (FY2024–FY2025, entirely after the boundary) → FALSE, un-flagged.</item>
/// <item>NEWP (a single FY) — every CAGR is NULL with no flag.</item>
/// </list>
/// Every test runs against the real migrated Testcontainers PostgreSQL seeded with the
/// L2 fixture universe and computes the canonical metrics at T through the registered
/// job; the flag is asserted both in <c>derived_metrics</c> and in the served
/// <c>metricFlags</c> object of the screener and stock-page payloads.
/// </summary>
public sealed class RegimeBreakTests : IClassFixture<PostgresFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    /// <summary>
    /// The panel's four CAGRs the L2 fixture world cannot compute: its statements carry no
    /// multi-year <c>EBIT</c> or <c>TOTAL_EQUITY</c> series (only the current FY), so
    /// <c>ebit_cagr_*</c> and <c>bvps_cagr_*</c> (TKT-foundation-096, <c>02</c> §6.2 v2) are
    /// honestly NULL everywhere — asserted separately below, never folded into the
    /// spanning-window loop. The panel's <c>fcf_ps_cagr_*</c> pair <em>does</em> compute (the
    /// fixture has a deep OCF/CAPEX history over a constant share count), so it stays in the
    /// loop with every other window-suffixed code.
    /// </summary>
    private static readonly string[] UnavailableCagrCodes =
    [
        MetricCodes.EbitCagr3y,
        MetricCodes.EbitCagr5y,
        MetricCodes.BvpsCagr3y,
        MetricCodes.BvpsCagr5y,
    ];

    /// <summary>The fourteen window-suffixed CAGR codes the fixture computes (FU §11.1 / Q5;
    /// 12 through v1, plus the panel's <c>fcf_ps_cagr_3y/5y</c> — TKT-foundation-096).</summary>
    private static readonly string[] CagrCodes =
        MetricCodes.All.Where(MetricCodes.IsGrowth).Except(UnavailableCagrCodes, StringComparer.Ordinal).ToArray();

    private readonly PostgresFixture _postgres;

    public RegimeBreakTests(PostgresFixture postgres) => _postgres = postgres;

    // -- Server-side storage -----------------------------------------------

    /// <summary>
    /// TC-MDF-060 — the engine writes <c>regime_break</c> per spanning CAGR: ALFA's
    /// windows (min FY 2015 &lt; 2023-12-31 ≤ max FY 2025) are TRUE; ZETA's
    /// (FY2024–FY2025) are FALSE; NEWP's NULL CAGRs carry no flag.
    /// </summary>
    [Fact]
    public async Task TC_MDF_060_engine_flags_spanning_cagrs_and_leaves_others_unflagged()
    {
        await SeedAsync();

        await using var db = _postgres.CreateContext();
        var symbols = await db.Instruments.ToDictionaryAsync(i => i.Id, i => i.Symbol);

        var rows = await db.DerivedMetrics.Where(m => m.AsOfDate == T).ToListAsync();
        var bySymbol = rows
            .GroupBy(r => symbols[r.InstrumentId], StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(r => r.MetricCode, r => r, StringComparer.Ordinal),
                StringComparer.Ordinal);

        // ALFA — every computed CAGR spans 2023-12-31 → TRUE.
        foreach (var code in CagrCodes)
        {
            var row = bySymbol["ALFA"][code];
            Assert.NotNull(row.Value);
            Assert.True(row.RegimeBreak, $"ALFA {code} should be regime-broken (spanning)");
        }

        // ZETA — exactly two FYs (FY2024/FY2025), entirely after the boundary → FALSE.
        // (ZETA never paid a dividend, so its div_cagr_* are NULL with no flag.)
        foreach (var code in CagrCodes)
        {
            var row = bySymbol["ZETA"][code];
            if (code.StartsWith("div_", StringComparison.Ordinal))
            {
                Assert.Null(row.Value);
                Assert.Null(row.RegimeBreak);
                continue;
            }

            Assert.NotNull(row.Value);
            Assert.False(row.RegimeBreak, $"ZETA {code} is entirely after the boundary");
        }

        // NEWP — a single FY → no window → NULL value with no flag (never fabricated).
        foreach (var code in CagrCodes)
        {
            var row = bySymbol["NEWP"][code];
            Assert.Null(row.Value);
            Assert.Null(row.RegimeBreak);
        }

        // The panel's EBIT / BVPS CAGR families: the L2 fixture has no multi-year EBIT or
        // equity series, so they are honestly NULL on every instrument — and a NULL value
        // never carries a regime-break flag (TKT-foundation-096).
        foreach (var symbol in new[] { "ALFA", "ZETA", "NEWP" })
        {
            foreach (var code in UnavailableCagrCodes)
            {
                Assert.Null(bySymbol[symbol][code].Value);
                Assert.Null(bySymbol[symbol][code].RegimeBreak);
            }
        }

        // The flag is CAGR-only: no non-growth metric carries it.
        Assert.All(
            rows.Where(r => !MetricCodes.IsGrowth(r.MetricCode)),
            r => Assert.Null(r.RegimeBreak));
    }

    // -- Screener payload ---------------------------------------------------

    /// <summary>
    /// TC-MDF-060 — the screener result row serves the flag in its <c>metricFlags</c>
    /// object: ALFA's rev_cagr is regime-broken, ZETA's is not (03 §4).
    /// </summary>
    [Fact]
    public async Task TC_MDF_060_screener_serves_regime_break_flag_on_matched_rows()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await RunAsync(client, """
            { "criteria": [ { "metricCode": "rev_cagr", "bound": "min", "minValue": 0.15, "window": 5 } ] }
            """);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var rows = document.RootElement.GetProperty("rows").EnumerateArray()
            .ToDictionary(row => row.GetProperty("symbol").GetString()!, StringComparer.Ordinal);

        Assert.Equal(new[] { "ALFA", "ZETA" }, rows.Keys.OrderBy(s => s, StringComparer.Ordinal).ToArray());

        var alfaFlags = rows["ALFA"].GetProperty("metricFlags").GetProperty("rev_cagr");
        Assert.True(alfaFlags.GetProperty("regimeBreak").GetBoolean());
        Assert.Equal(5, alfaFlags.GetProperty("windowYears").GetInt32());

        var zetaFlags = rows["ZETA"].GetProperty("metricFlags").GetProperty("rev_cagr");
        Assert.False(zetaFlags.GetProperty("regimeBreak").GetBoolean());
    }

    // -- Stock-page payload -------------------------------------------------

    /// <summary>
    /// TC-MDF-060 — the stock-page Growth section serves the flag in its
    /// <c>metricFlags</c> object keyed by the window-suffixed metric code; an absent
    /// CAGR carries no marker (03 §4, UXR-RES-026/027).
    /// </summary>
    [Fact]
    public async Task TC_MDF_060_stock_growth_serves_regime_break_flag_per_window()
    {
        await SeedAsync();
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = CreateClient(factory);

        using var alfa = await client.GetAsync("/api/v1/stocks/ALFA/growth");
        Assert.Equal(HttpStatusCode.OK, alfa.StatusCode);
        using var alfaDocument = JsonDocument.Parse(await alfa.Content.ReadAsStringAsync());
        var alfaFlags = alfaDocument.RootElement.GetProperty("metricFlags");
        Assert.True(alfaFlags.GetProperty("rev_cagr_3y").GetProperty("regimeBreak").GetBoolean());
        Assert.True(alfaFlags.GetProperty("eps_cagr_5y").GetProperty("regimeBreak").GetBoolean());
        Assert.True(alfaFlags.GetProperty("fcf_cagr_10y").GetProperty("regimeBreak").GetBoolean());
        Assert.True(alfaFlags.GetProperty("div_cagr_3y").GetProperty("regimeBreak").GetBoolean());
        Assert.Equal(3, alfaFlags.GetProperty("rev_cagr_3y").GetProperty("windowYears").GetInt32());

        using var zeta = await client.GetAsync("/api/v1/stocks/ZETA/growth");
        Assert.Equal(HttpStatusCode.OK, zeta.StatusCode);
        using var zetaDocument = JsonDocument.Parse(await zeta.Content.ReadAsStringAsync());
        var zetaFlags = zetaDocument.RootElement.GetProperty("metricFlags");
        Assert.False(zetaFlags.GetProperty("rev_cagr_3y").GetProperty("regimeBreak").GetBoolean());

        // NEWP has no CAGR at all → the honest no-data state, no flags object.
        using var newp = await client.GetAsync("/api/v1/stocks/NEWP/growth");
        Assert.Equal(HttpStatusCode.OK, newp.StatusCode);
        using var newpDocument = JsonDocument.Parse(await newp.Content.ReadAsStringAsync());
        Assert.Equal("no-data", newpDocument.RootElement.GetProperty("state").GetString());
        Assert.False(newpDocument.RootElement.TryGetProperty("metricFlags", out _));
    }

    // ---------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------

    /// <summary>Seeds the L2 fixture universe and computes the canonical metrics at T.</summary>
    private async Task SeedAsync()
    {
        await using (var db = _postgres.CreateContext())
        {
            await FixtureSeeder.ApplyAsync(db, L2);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM derived_metrics");
        }

        var clock = new FakeTimeProvider(new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _postgres.ConnectionString,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddMarketDataIngestion(configuration);
        await using var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IIngestionJobRunner>();
        var result = await runner.RunAsync(MetricsRecomputeJob.Code, new IngestionRequest(T));
        Assert.Equal("succeeded", result.Status);
    }

    private static HttpClient CreateClient(DegerliAppFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    private static async Task<HttpResponseMessage> RunAsync(HttpClient client, string json)
    {
        var csrf = await FetchCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/screener/run")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
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
