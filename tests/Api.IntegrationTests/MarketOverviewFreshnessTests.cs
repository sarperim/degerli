using System.Net;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
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
/// TKT-foundation-037 acceptance — TC-MOV-032: the <c>GET /api/v1/market/overview</c>
/// display-freshness flag is <b>stored-prices-based</b>, the same doctrine §10.9.13
/// applies to the write path (stored prices are the truth, never a weekday rule).
/// <para>
/// The defect this pins: the old rule was
/// <c>stale = snapshot.SnapshotDate &lt; LatestTradingDayOnOrBefore(today)</c> — a pure
/// weekday-backing helper — so a Tuesday clock flagged Monday's row (the freshest
/// completed session, since Tuesday's EOD ingests at 20:30 Istanbul) as <c>Bayat</c>.
/// The honest direction must survive: <c>stale: true</c> only when a <b>newer
/// completed session's prices exist</b> in the store that the snapshot row lacks
/// (a genuinely missed <c>market-snapshot</c> run).
/// </para>
/// Snapshot rows are produced by the real <c>market-snapshot</c> job, never
/// hand-written; the stored-prices world is the L2 fixture universe (Tuesday anchor
/// T = 2026-10-06, so Monday = 2026-10-05 is the preceding completed session).
/// </summary>
public sealed class MarketOverviewFreshnessTests : IClassFixture<PostgresFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);

    /// <summary>The latest fixture trading day — Tuesday 2026-10-06.</summary>
    private static DateOnly Tuesday => L2.Anchor.T;

    /// <summary>The preceding completed session — Monday 2026-10-05.</summary>
    private static DateOnly Monday => L2.Anchor.TradingDay(-1);

    /// <summary>
    /// The Tuesday clock (21:00 Istanbul — the harness clock's fixed instant, which
    /// cannot be moved backwards). The calendar day is Tuesday and the store holds no
    /// Tuesday prices: Monday is the freshest completed session that exists, exactly the
    /// window the reported defect served as "Bayat".
    /// </summary>
    private static readonly DateTimeOffset TuesdayClock =
        new(2026, 10, 6, 18, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _postgres;

    public MarketOverviewFreshnessTests(PostgresFixture postgres) => _postgres = postgres;

    // ---------------------------------------------------------------------------------
    // TC-MOV-032 — stored-prices display freshness
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// TC-MOV-032 (i) — a Monday snapshot row + Monday-only stored prices served on a
    /// Tuesday clock is <b>fresh</b>: Monday is the freshest completed session that
    /// exists (Tuesday's EOD only lands at 20:30 Istanbul). Red on the weekday rule,
    /// which compared against Tuesday (a weekday, but not a completed session).
    /// </summary>
    [Fact]
    public async Task TC_MOV_032_monday_snapshot_over_monday_prices_served_tuesday_is_fresh()
    {
        await ResetAsync();
        await DeletePricesAfterAsync(Monday);

        // The real job resolves the requested Tuesday to the latest stored trading day
        // (Monday) — the write path's §10.9.13 rule.
        await RunSnapshotAsync(Tuesday);

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        factory.Clock.SetUtcNow(TuesdayClock);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/market/overview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal("available", root.GetProperty("state").GetString());
        Assert.Equal(Monday.ToString("yyyy-MM-dd"), root.GetProperty("asOf").GetString());
        // No newer completed session's data exists → the badge would be a lie.
        Assert.False(root.GetProperty("stale").GetBoolean());
    }

    /// <summary>
    /// TC-MOV-032 (ii) — the same world plus Tuesday's prices stored (a completed newer
    /// session the snapshot row lacks, i.e. the <c>market-snapshot</c> run was missed)
    /// serves <c>stale: true</c>: the honest missed-run signal must survive the fix.
    /// </summary>
    [Fact]
    public async Task TC_MOV_032_newer_stored_session_missing_from_snapshot_is_stale()
    {
        await ResetAsync();
        await DeletePricesAfterAsync(Monday);
        await RunSnapshotAsync(Tuesday);

        // Tuesday's EOD prices land but the snapshot run never follows: newer data exists.
        await using (var db = _postgres.CreateContext())
        {
            await FixtureSeeder.ApplyAsync(db, L2);
        }

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        factory.Clock.SetUtcNow(TuesdayClock);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/market/overview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        // Last-known-good is still served (FR-MOV-016) — marked stale honestly.
        Assert.Equal("available", root.GetProperty("state").GetString());
        Assert.Equal(Monday.ToString("yyyy-MM-dd"), root.GetProperty("asOf").GetString());
        Assert.True(root.GetProperty("stale").GetBoolean());
    }

    /// <summary>
    /// TC-MOV-032 (iii) — a Tuesday snapshot over Tuesday prices is fresh: the served
    /// row already covers the latest stored trading day (the ordinary trading-day case,
    /// unchanged by the fix).
    /// </summary>
    [Fact]
    public async Task TC_MOV_032_tuesday_snapshot_over_tuesday_prices_is_fresh()
    {
        await ResetAsync();
        await RunSnapshotAsync(Tuesday);

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        factory.Clock.SetUtcNow(TuesdayClock);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/market/overview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal("available", root.GetProperty("state").GetString());
        Assert.Equal(Tuesday.ToString("yyyy-MM-dd"), root.GetProperty("asOf").GetString());
        Assert.False(root.GetProperty("stale").GetBoolean());
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    /// <summary>Applies the fixture universe and clears the mutable snapshot state.</summary>
    private async Task ResetAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM market_snapshots");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
    }

    /// <summary>
    /// Removes every stored price after <paramref name="day"/> — the world before that
    /// session's EOD landed (the store is the truth the freshness rule reads).
    /// </summary>
    private async Task DeletePricesAfterAsync(DateOnly day)
    {
        await using var db = _postgres.CreateContext();
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM daily_prices WHERE price_date > {0}",
            day);
    }

    /// <summary>Runs the real <c>market-snapshot</c> job for <paramref name="date"/>.</summary>
    private async Task RunSnapshotAsync(DateOnly date)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
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
        var result = await runner.RunAsync(MarketSnapshotJob.Code, new IngestionRequest(date));
        Assert.Equal("succeeded", result.Status);
    }
}
