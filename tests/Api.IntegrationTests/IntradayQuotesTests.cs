using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Api.IntegrationTests.Harness.Mail;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.Intraday;
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
/// TKT-foundation-047 acceptance — the <b>intraday refresh lane</b> (architecture §10.9.18,
/// FR-MDF-001 the new intraday mode): a paced per-symbol session sweep that stores
/// <c>intraday_quotes</c> rows and never touches <c>daily_prices</c>.
/// <para>
/// The lane is <b>probe-gated</b> at the source (01 §10.9.1): the fetch goes through the same
/// envelope detection the EOD adapter uses — the real İş Yatırım <c>HGDG_*</c> body and the
/// documented Yahoo <c>.IS</c> chart contingency are both first-class, and the choice is
/// configuration (<c>Ingestion:Intraday:BaseUrl</c>/<c>Path</c>), never a code fork. Whether
/// the İş endpoint serves a same-day row mid-session is a live probe (the orchestrator's
/// post-merge criterion) — these cases pin the behaviour for <b>both</b> outcomes and for
/// the honest no-data class.
/// </para>
/// Every test runs the real <see cref="IntradayRefreshJob"/> through the production
/// <c>AddMarketDataIngestion</c> composition against a migrated Testcontainers PostgreSQL and
/// the FU §10 WireMock double. Pacing is bound low (a lower-bound assertion only), and no
/// test reaches a live source.
/// </summary>
public sealed class IntradayQuotesTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;
    private static DateOnly TMinus1 => L2.Anchor.TradingDay(-1);

    /// <summary>The three-member sweep bound (a paced sweep is asserted as a 3-symbol loop).</summary>
    private static readonly string[] Bound = ["ALFA", "BETA", "GAMA"];

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public IntradayQuotesTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    // ---------------------------------------------------------------------------------
    // TC-MDF-084 — sweep + honest per-symbol accounting
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// TC-MDF-084 — a paced 3-symbol sweep over the three source shapes: the İş Yatırım
    /// same-day row (primary), the Yahoo delayed-quote chart (the documented contingency,
    /// whose last grid interval carries a null close and must not shadow the last available
    /// price), and an empty same-day payload (the probe's honest "no current row" outcome).
    /// <para>
    /// Expected: one <c>intraday_quotes</c> row per data-carrying symbol with the sweep's
    /// <c>fetched_at</c> (UTC) and the session date as <c>session_date</c>; the no-data symbol
    /// is <b>counted</b> (run <c>skipped</c> counter + a log event naming it), never invisible;
    /// <c>daily_prices</c> is untouched (an intraday value is never a close); the loop is paced
    /// sequentially.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TC_MDF_084_Intraday_sweep_stores_quotes_counts_no_data_and_never_writes_daily_prices()
    {
        await ResetAsync();
        await RestrictUniverseAsync(Bound);
        StubIsSameDayRow("ALFA", lastPrice: 21.50m, volume: 1_234_567);
        StubYahooDelayedQuote("BETA", lastPrice: 33.75m);
        // GAMA: the İş endpoint serves the ticker's history but no row for the session date —
        // the probe's "no same-day row" outcome, counted honestly (never a fabricated quote).
        StubIsWithoutSessionRow("GAMA");

        var clock = new FakeTimeProvider(new DateTimeOffset(T.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero));
        await using var host = BuildIngestion(clock, requestDelayMs: 40);

        var stopwatch = Stopwatch.StartNew();
        var result = await RunAsync(host, T);
        stopwatch.Stop();

        Assert.Equal("succeeded", result.Status);
        Assert.Equal(2, result.Written);
        Assert.Equal(0, result.Unchanged);
        // The no-data symbol is counted, never dropped: it rides the run's skipped counter.
        Assert.Equal(1, result.Skipped);

        await using var db = _postgres.CreateContext();
        var rows = await db.IntradayQuotes
            .Where(quote => quote.SessionDate == T)
            .ToListAsync();
        Assert.Equal(2, rows.Count);

        var symbols = await db.Instruments.ToDictionaryAsync(instrument => instrument.Id, instrument => instrument.Symbol);

        var alfa = rows.Single(row => symbols[row.InstrumentId] == "ALFA");
        Assert.Equal(21.50m, alfa.LastPrice);
        Assert.Equal(1_234_567L, alfa.DayVolume);
        Assert.Equal("isyatirim", alfa.SourceRef);
        Assert.Equal(clock.GetUtcNow(), alfa.FetchedAt.ToUniversalTime());

        var beta = rows.Single(row => symbols[row.InstrumentId] == "BETA");
        // The stub's last interval carries a null close (an interval with no trade): the
        // reading is the last *available* observation, never the hole at the end of the grid.
        Assert.Equal(33.75m, beta.LastPrice);
        // Yahoo's volume is a share count, not the stored ₺ turnover semantic (FLG-09(b)):
        // the honest gap is NULL, never a mislabelled unit.
        Assert.Null(beta.DayVolume);
        Assert.Equal("yahoo", beta.SourceRef);
        Assert.Equal(clock.GetUtcNow(), beta.FetchedAt.ToUniversalTime());

        Assert.DoesNotContain(rows, row => symbols[row.InstrumentId] == "GAMA");

        // An intraday value is never a close: the EOD fact table is untouched.
        Assert.Equal(0, await db.DailyPrices.CountAsync(price => price.PriceDate == T));

        // Honest per-symbol accounting: the no-data symbol is named in the run's log.
        Assert.Contains(host.Logs.Events, evt =>
            evt.MessageTemplate.Text.Contains("NO_DATA", StringComparison.Ordinal)
            && evt.Properties.TryGetValue("Symbol", out var symbol)
            && symbol is Serilog.Events.ScalarValue scalar
            && Equals(scalar.Value, "GAMA"));

        // A paced sequential sweep: exactly one request per bound symbol, and the inter-request
        // waits honour the configured delay (lower bound only — deterministic by construction).
        Assert.Equal(3, _wireMock.RequestsFor("/intraday/ALFA") + _wireMock.RequestsFor("/intraday/BETA") + _wireMock.RequestsFor("/intraday/GAMA"));
        Assert.True(
            stopwatch.Elapsed >= TimeSpan.FromMilliseconds(2 * 40),
            $"Expected the 3-symbol sweep to be paced (>= 80 ms), observed {stopwatch.ElapsedMilliseconds} ms.");
    }

    /// <summary>
    /// The lane's cadence and source are <b>configuration constants</b> (01 §10.9.18): the
    /// shipped <c>appsettings.json</c> carries the four-sweep trading-day cron
    /// (11:15/13:15/15:15/17:15 Europe/Istanbul, expressed in the scheduler's six-field form)
    /// and the intraday source section mirrors the <c>Ingestion:Prices</c> pattern — so the
    /// two-hour interval is user-adjustable without a code change, and the Yahoo contingency
    /// is a one-line repoint.
    /// </summary>
    [Fact]
    public void TC_MDF_084_Intraday_cadence_and_source_are_shipped_config_constants()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Api", "appsettings.json")));
        var ingestion = document.RootElement.GetProperty("Ingestion");

        var schedule = ingestion.GetProperty("Scheduler").GetProperty("JobSchedules")
            .EnumerateArray()
            .Single(entry => entry.GetProperty("JobCode").GetString() == IntradayRefreshJob.Code);
        // Four sweeps at mid-hour+15 on trading days; the six-field form is the scheduler's
        // (Cronos) expression, the same cadence as the ticket's "15 11,13,15,17 * * 1-5".
        Assert.Equal("0 15 11,13,15,17 * * 1-5", schedule.GetProperty("Cron").GetString());

        var intraday = ingestion.GetProperty("Intraday");
        Assert.StartsWith("https://www.isyatirim.com.tr", intraday.GetProperty("BaseUrl").GetString(), StringComparison.Ordinal);
        // The intraday resource is İş Yatırım's delayed chart feed (the 2026-10-23 probe found
        // HisseTekil lagging: no same-day row mid-session, while IntradayDelay served the live
        // session's bars). The EOD prices lane keeps HisseTekil — only this lane moved, and the
        // chart feed needs no {date} token because the sweep narrows by the bars' own stamps.
        Assert.Contains("{symbol}", intraday.GetProperty("Path").GetString()!, StringComparison.Ordinal);
        Assert.Contains("ChartData.aspx/IntradayDelay", intraday.GetProperty("Path").GetString()!, StringComparison.Ordinal);
        // The politeness posture: sequential requests paced between calls (01 §10.9.2).
        Assert.Equal(500, intraday.GetProperty("RequestDelayMs").GetInt32());
        // The sweep bound follows the covered universe (XU100 at launch).
        Assert.Equal("XU100", intraday.GetProperty("UniverseIndex").GetString());
    }

    // ---------------------------------------------------------------------------------
    // TC-MDF-086 — in-place recompute
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// TC-MDF-086 — a re-sweep of the same session is an <b>in-place</b> update: identical
    /// values are an honest <c>unchanged</c> count (the observation timestamp still advances —
    /// it is when the value was last seen), a changed value overwrites the row in place and is
    /// counted as a <c>rewritten</c> stat, and a <b>prior session's</b> rows stay immutable.
    /// </summary>
    [Fact]
    public async Task TC_MDF_086_Intraday_resweep_is_in_place_and_prior_sessions_are_immutable()
    {
        await ResetAsync();
        // Test plan §11 pins this case to the ALFA-only bound: every sweep stubs exactly the
        // one İş route it depends on, so no case can inherit another's stub or lean on an
        // unstubbed route's 404.
        await RestrictUniverseAsync(["ALFA"]);

        // A prior session's sweep first (Monday), so its row exists before the Tuesday sweeps.
        StubIsSameDayRow("ALFA", lastPrice: 19.90m, volume: 999_000, session: TMinus1);
        var mondayClock = new FakeTimeProvider(new DateTimeOffset(TMinus1.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero));
        await using (var monday = BuildIngestion(mondayClock, requestDelayMs: 0))
        {
            var mondayResult = await RunAsync(monday, TMinus1);
            Assert.Equal(1, mondayResult.Written);
        }

        await using var db = _postgres.CreateContext();
        var alfaId = await db.Instruments.Where(instrument => instrument.Symbol == "ALFA").Select(instrument => instrument.Id).SingleAsync();
        var priorSession = await db.IntradayQuotes.AsNoTracking()
            .SingleAsync(quote => quote.InstrumentId == alfaId && quote.SessionDate == TMinus1);
        Assert.Equal(19.90m, priorSession.LastPrice);
        var priorFetchedAt = priorSession.FetchedAt;

        // ── First Tuesday sweep ────────────────────────────────────────────────────────
        StubIsSameDayRow("ALFA", lastPrice: 21.50m, volume: 1_234_567);
        var firstClock = new FakeTimeProvider(new DateTimeOffset(T.ToDateTime(new TimeOnly(11, 15)), TimeSpan.Zero));
        await using (var first = BuildIngestion(firstClock, requestDelayMs: 0))
        {
            var firstResult = await RunAsync(first, T);
            Assert.Equal(1, firstResult.Written);
            Assert.Equal(0, firstResult.Unchanged);
        }

        // ── Identical re-sweep (13:15): an honest unchanged count ─────────────────────
        var secondClock = new FakeTimeProvider(new DateTimeOffset(T.ToDateTime(new TimeOnly(13, 15)), TimeSpan.Zero));
        await using (var second = BuildIngestion(secondClock, requestDelayMs: 0))
        {
            var secondResult = await RunAsync(second, T);
            Assert.Equal(0, secondResult.Written);
            Assert.Equal(1, secondResult.Unchanged);
        }

        await using (var afterSecond = _postgres.CreateContext())
        {
            var row = await afterSecond.IntradayQuotes.AsNoTracking()
                .SingleAsync(quote => quote.InstrumentId == alfaId && quote.SessionDate == T);
            Assert.Equal(21.50m, row.LastPrice);
            // The value did not change; the observation timestamp still records the sweep.
            Assert.Equal(secondClock.GetUtcNow(), row.FetchedAt.ToUniversalTime());
            Assert.True(row.FetchedAt > firstClock.GetUtcNow());
        }

        // ── Changed re-sweep (15:15): overwritten in place, counted as rewritten ──────
        StubIsSameDayRow("ALFA", lastPrice: 22.00m, volume: 2_000_000);
        var thirdClock = new FakeTimeProvider(new DateTimeOffset(T.ToDateTime(new TimeOnly(15, 15)), TimeSpan.Zero));
        await using (var third = BuildIngestion(thirdClock, requestDelayMs: 0))
        {
            var thirdResult = await RunAsync(third, T);
            Assert.Equal(0, thirdResult.Written);
            Assert.Equal(0, thirdResult.Unchanged);

            // The changed row is counted as a rewrite in the run's stat line — the counter's
            // *value* is pinned (the template is emitted on every sweep, including 0).
            Assert.Contains(third.Logs.Events, evt =>
                evt.MessageTemplate.Text.Contains("rewritten", StringComparison.OrdinalIgnoreCase)
                && evt.MessageTemplate.Text.Contains("{Rewritten}", StringComparison.Ordinal)
                && evt.Properties.TryGetValue("Rewritten", out var rewritten)
                && rewritten is Serilog.Events.ScalarValue { Value: int rewrittenCount }
                && rewrittenCount == 1);
        }

        await using (var afterThird = _postgres.CreateContext())
        {
            var rows = await afterThird.IntradayQuotes.AsNoTracking()
                .Where(quote => quote.InstrumentId == alfaId)
                .ToListAsync();
            // Exactly one row per (instrument, session) — the update was in place, no append.
            Assert.Equal(2, rows.Count);

            var today = rows.Single(quote => quote.SessionDate == T);
            Assert.Equal(22.00m, today.LastPrice);
            Assert.Equal(2_000_000L, today.DayVolume);
            Assert.Equal(thirdClock.GetUtcNow(), today.FetchedAt.ToUniversalTime());

            // The prior session's row is untouched by a later sweep (historical immutability).
            var prior = rows.Single(quote => quote.SessionDate == TMinus1);
            Assert.Equal(19.90m, prior.LastPrice);
            Assert.Equal(999_000L, prior.DayVolume);
            Assert.Equal(priorFetchedAt, prior.FetchedAt);
        }
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// The production ingestion composition (the same <c>AddMarketDataIngestion</c> the API
    /// host uses) retargeted at the Testcontainers database and the WireMock intraday source
    /// double. The intraday source is its own configuration section mirroring
    /// <c>Ingestion:Prices</c>; the sweep bound is the FU XU100 subset the test restricts.
    /// </summary>
    private IngestionHost BuildIngestion(FakeTimeProvider clock, int requestDelayMs)
    {
        var mail = new RecordingMailDispatcher();
        var logs = new InMemorySerilogSink();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _postgres.ConnectionString,
                ["Ingestion:Intraday:BaseUrl"] = _wireMock.BaseUrl,
                ["Ingestion:Intraday:Path"] = "/intraday/{symbol}",
                ["Ingestion:Intraday:UniverseIndex"] = "XU100",
                ["Ingestion:Intraday:RequestDelayMs"] = requestDelayMs.ToString(CultureInfo.InvariantCulture),
                // The prices lane's bound is irrelevant to this job, but the shared client's
                // fallback base address must not point anywhere live.
                ["Ingestion:Prices:BaseUrl"] = _wireMock.BaseUrl,
                ["Ingestion:Prices:RequestDelayMs"] = "0",
            })
            .Build();

        // Information-level capture of the intraday lane (the per-symbol accounting events)
        // without the EF noise the harness would otherwise sink.
        var logger = new LoggerConfiguration()
            .MinimumLevel.Warning()
            .MinimumLevel.Override("Degerli.Ingestion.Intraday", Serilog.Events.LogEventLevel.Information)
            .WriteTo.Sink(logs)
            .CreateLogger();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog(logger, dispose: false));
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IIngestionAlerter>(new HarnessMailAlerter(mail));
        services.AddMarketDataIngestion(configuration);

        return new IngestionHost(services.BuildServiceProvider(), mail, logs);
    }

    private static async Task<IngestResult> RunAsync(IngestionHost host, DateOnly sessionDate)
    {
        using var scope = host.Provider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IIngestionJobRunner>();
        return await runner.RunAsync(IntradayRefreshJob.Code, new IngestionRequest(sessionDate));
    }

    /// <summary>
    /// Applies the FU universe, clears the intraday/EOD/ledger state so each case creates its
    /// own world, and resets the WireMock double completely — mappings <b>and</b> request log.
    /// xUnit gives no intra-class ordering, so a leftover stub from another case would be a
    /// silent second source shape; every case stubs exactly the routes it depends on (the
    /// suite's convention, e.g. <c>EodChainTests</c>/<c>MacroIngestionTests</c>).
    /// </summary>
    private async Task ResetAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM intraday_quotes");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM quarantined_facts");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM daily_prices");
        _wireMock.Server.ResetMappings();
        _wireMock.Server.ResetLogEntries();
    }

    /// <summary>Restricts the XU100 fetch bound to the three-symbol sweep the case asserts.</summary>
    private async Task RestrictUniverseAsync(string[] symbols)
    {
        await using var db = _postgres.CreateContext();
        foreach (var symbol in symbols)
        {
            // Guard: every asserted symbol must exist in the seeded fixture universe.
            Assert.True(
                await db.Instruments.AnyAsync(instrument => instrument.Symbol == symbol),
                $"Fixture universe does not contain {symbol}.");
        }

        var keptIds = await db.Instruments
            .Where(instrument => symbols.Contains(instrument.Symbol))
            .Select(instrument => instrument.Id)
            .ToListAsync();
        var xu100Id = await db.Indices
            .Where(index => index.Code == "XU100")
            .Select(index => index.Id)
            .SingleAsync();

        var surplus = await db.IndexConstituents
            .Where(constituent => constituent.IndexId == xu100Id && !keptIds.Contains(constituent.InstrumentId))
            .ToListAsync();
        db.IndexConstituents.RemoveRange(surplus);
        await db.SaveChangesAsync();
    }

    // ── Source doubles (the three envelope shapes the probe can observe) ──────────────

    /// <summary>
    /// The real İş Yatırım <c>HisseTekil</c> envelope (01 §10.9.1) carrying the session's row
    /// plus an older row: the adapter must narrow the range body to the requested day.
    /// </summary>
    private void StubIsSameDayRow(string symbol, decimal lastPrice, long volume, DateOnly? session = null)
    {
        var date = (session ?? T).ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        var older = (session ?? T).AddDays(-1).ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        var body = $$"""
        {
          "ok": true,
          "errorCode": null,
          "value": [
            { "HGDG_HS_KODU": "{{symbol}}", "HGDG_TARIH": "{{older}}", "HGDG_KAPANIS": 19.10, "HGDG_MAX": 19.50, "HGDG_MIN": 18.90, "HGDG_HACIM": 5000000.0 },
            { "HGDG_HS_KODU": "{{symbol}}", "HGDG_TARIH": "{{date}}", "HGDG_KAPANIS": {{lastPrice.ToString(CultureInfo.InvariantCulture)}}, "HGDG_MAX": 21.90, "HGDG_MIN": 21.10, "HGDG_HACIM": {{volume.ToString(CultureInfo.InvariantCulture)}}.0 }
          ]
        }
        """;
        _wireMock.StubJson($"/intraday/{symbol}", body);
    }

    /// <summary>
    /// The İş envelope that serves the ticker's history but <b>no row for the session date</b>
    /// — the probe-gated lane's honest "the EOD-shaped source has nothing current" outcome.
    /// </summary>
    private void StubIsWithoutSessionRow(string symbol)
    {
        var older = T.AddDays(-1).ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        var body = $$"""
        {
          "ok": true,
          "errorCode": null,
          "value": [
            { "HGDG_HS_KODU": "{{symbol}}", "HGDG_TARIH": "{{older}}", "HGDG_KAPANIS": 8.10, "HGDG_MAX": 8.20, "HGDG_MIN": 8.00, "HGDG_HACIM": 4000000.0 }
          ]
        }
        """;
        _wireMock.StubJson($"/intraday/{symbol}", body);
    }

    /// <summary>
    /// The documented Yahoo Finance <c>.IS</c> contingency envelope (01 §10.9.1): a
    /// range-shaped chart carrying the session's observations, the last of which has a
    /// <b>null close</b> — the grid's hole for an interval with no trade, the real-world case
    /// that must not shadow the last available price. The reading is therefore the last
    /// <b>non-null</b> close, and never a null passed off as a price. Volume is deliberately
    /// present in the body so the adapter's unit-honest NULL is observable (FLG-09(b)).
    /// </summary>
    private void StubYahooDelayedQuote(string symbol, decimal lastPrice)
    {
        var morning = new DateTimeOffset(T.ToDateTime(new TimeOnly(7, 15)), TimeSpan.Zero).ToUnixTimeSeconds();
        var midday = new DateTimeOffset(T.ToDateTime(new TimeOnly(9, 45)), TimeSpan.Zero).ToUnixTimeSeconds();
        var last = new DateTimeOffset(T.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero).ToUnixTimeSeconds();
        var body = $$"""
        {
          "chart": {
            "result": [
              {
                "meta": { "symbol": "{{symbol}}.IS", "currency": "TRY" },
                "timestamp": [ {{morning}}, {{midday}}, {{last}} ],
                "indicators": {
                  "quote": [
                    {
                      "open": [ 33.10, 33.40, null ],
                      "high": [ 33.60, 33.90, null ],
                      "low": [ 33.00, 33.30, null ],
                      "close": [ 33.40, {{lastPrice.ToString(CultureInfo.InvariantCulture)}}, null ],
                      "volume": [ 12000, 18000, null ]
                    }
                  ]
                }
              }
            ]
          }
        }
        """;
        _wireMock.StubJson($"/intraday/{symbol}", body);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Degerli.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root (Degerli.sln).");
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

    /// <summary>Funnels the ingestion alert seam into the shared recording mail double.</summary>
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
