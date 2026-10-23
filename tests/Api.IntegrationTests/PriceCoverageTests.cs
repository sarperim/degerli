using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Api.IntegrationTests.Harness.Mail;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Prices;
using Degerli.Ingestion.Sources;
using Degerli.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Serilog;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// <c>chore(prices)</c> — the <b>completeness gate</b> of the EOD <c>prices</c> lane
/// (01 v1.5 §10.9.2 amendment). No test-plan TC id is allocated for this chore; the cases are
/// named here and carry the behaviour the dev-stack defect of 2026-10-23 exposed: a symbol the
/// source had not published yet was recorded as <i>nothing</i> (<c>0/0/0, failed:false</c>) and
/// the run still reported <c>succeeded</c> over a partly-covered day.
/// <para>
/// Every case runs the real job against a migrated Testcontainers PostgreSQL and a
/// <b>scripted in-process source double</b> (<see cref="ISourceClient"/>) — never a live İş
/// Yatırım call — so "this symbol answers EMPTY now and a full OHLCV row later" is exact and
/// deterministic. The double answers per (symbol, call ordinal), which is what distinguishes
/// pass 1 from pass 2.
/// </para>
/// <para>
/// The settle delay is bound to <c>00:00:00</c> (the documented "no wait") everywhere except the
/// settle-path case, which binds a real delay and advances a <see cref="FakeTimeProvider"/> to
/// elapse it; the wall-clock budget is exercised through a stepping fake clock. No test waits on
/// a real timer.
/// </para>
/// </summary>
public sealed class PriceCoverageTests : IClassFixture<PostgresFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);

    private static DateOnly T => L2.Anchor.T;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string PricesPath = "/prices/eod";

    private readonly PostgresFixture _postgres;

    public PriceCoverageTests(PostgresFixture postgres) => _postgres = postgres;

    /// <summary>
    /// The pass-2 case: ALFA answers EMPTY on its first fetch and a full row on the next, while
    /// EPSL publishes immediately. The bounded re-sweep must <b>write</b> ALFA's late row (not
    /// treat it as "unchanged"), and the run reports <c>succeeded</c> only because coverage
    /// completed — <c>2/2</c> stored, one fetch for the published symbol and two for the late one.
    /// </summary>
    [Fact]
    public async Task Prices_coverage_pass2_writes_a_row_the_source_published_late()
    {
        await ResetAsync(["ALFA", "EPSL"]);
        var source = new ScriptedSourceClient((symbol, call) => symbol switch
        {
            "ALFA" => call == 1 ? EmptyBody() : RowBody(symbol),
            _ => RowBody(symbol),
        });

        await using var host = BuildIngestion(source);
        var result = await RunAsync(host);

        Assert.Equal(IngestResult.Succeeded, result.Status);
        Assert.Equal(2, result.Written);
        Assert.Equal(0, result.Unchanged);
        Assert.Equal(0, result.Quarantined);

        // The late row is stored, with the source's own values.
        await using (var db = _postgres.CreateContext())
        {
            var symbols = await db.Instruments.ToDictionaryAsync(i => i.Id, i => i.Symbol);
            var rows = (await db.DailyPrices.Where(p => p.PriceDate == T).ToListAsync())
                .ToDictionary(r => symbols[r.InstrumentId], r => r, StringComparer.Ordinal);
            Assert.Equal(2, rows.Count);
            Assert.Equal(Fixture.Close("ALFA"), rows["ALFA"].CloseRaw);
            Assert.Equal("isyatirim", rows["ALFA"].SourceRef);
        }

        // One fetch for the already-published symbol; two for the symbol that answered empty.
        Assert.Equal(2, source.Calls("ALFA"));
        Assert.Equal(1, source.Calls("EPSL"));

        // The empty answer was visible while it lasted (named, never a silent no-op)…
        Assert.True(
            host.Logs.Contains(evt =>
                evt.RenderMessage().Contains("ALFA", StringComparison.Ordinal)
                && evt.RenderMessage().Contains(PricesJob.NoRowForDate, StringComparison.Ordinal)),
            "no log event named ALFA / NO_ROW_FOR_DATE for the empty pass-1 answer");

        // …and the run's per-symbol stats now speak for the write, in one entry per symbol.
        var symbolStats = await SymbolStatsAsync();
        Assert.Equal(2, symbolStats.Count);
        Assert.Equal(1, symbolStats["ALFA"].GetProperty("written").GetInt32());
        Assert.Equal(JsonValueKind.Null, symbolStats["ALFA"].GetProperty("reason").ValueKind);
    }

    /// <summary>
    /// The symbol that never publishes: bounded re-sweeps, an honest <c>partial</c> terminal
    /// status, the symbol named in the run stats and the summary log, and — because an empty
    /// answer is not a source failure — no alert. The sweep stops at
    /// <c>Ingestion:Prices:CoverageMaxPasses</c> (three fetches, never a spin).
    /// </summary>
    [Fact]
    public async Task Prices_coverage_never_published_symbol_ends_partial_and_is_named()
    {
        await ResetAsync(["ALFA"]);
        var source = new ScriptedSourceClient((_, _) => EmptyBody());

        await using var host = BuildIngestion(source, maxPasses: 3);
        var result = await RunAsync(host);

        Assert.Equal(IngestResult.Partial, result.Status);
        Assert.Equal(0, result.Written);

        // Bounded: exactly the configured number of passes, no infinite loop.
        Assert.Equal(3, source.Calls("ALFA"));

        // Nothing was stored, and the answer is an explicit empty — not "unchanged".
        await using (var db = _postgres.CreateContext())
        {
            Assert.Equal(0, await db.DailyPrices.CountAsync(p => p.PriceDate == T));

            var run = await db.IngestRuns.SingleAsync(r => r.JobCode == "prices");
            Assert.Equal(IngestResult.Partial, run.Status);
            Assert.Contains(PricesJob.NoRowForDate, run.StatsJson!, StringComparison.Ordinal);
            Assert.Contains("ALFA", run.StatsJson!, StringComparison.Ordinal);
        }

        var stat = Assert.Single((await SymbolStatsAsync()).Values);
        Assert.Equal(0, stat.GetProperty("written").GetInt32());
        Assert.Equal(0, stat.GetProperty("unchanged").GetInt32());
        Assert.False(stat.GetProperty("failed").GetBoolean());
        Assert.Equal(PricesJob.NoRowForDate, stat.GetProperty("reason").GetString());

        // The summary line names the uncovered day and the symbol.
        Assert.True(
            host.Logs.Contains(evt =>
                evt.RenderMessage().Contains("NOT fully covered", StringComparison.Ordinal)
                && evt.RenderMessage().Contains("ALFA", StringComparison.Ordinal)),
            "no summary log line naming the uncovered day / ALFA");

        // A genuinely empty symbol is not a source failure: no alert is raised for it.
        Assert.Empty(host.Mail.Sent);
    }

    /// <summary>
    /// The ANSGR-shaped case: a genuinely empty symbol inside an otherwise covered day. It is
    /// distinguishable from an idempotent no-op — <c>NO_ROW_FOR_DATE</c> vs a real write — the
    /// day is <c>partial</c>, and the missing name is the empty one only.
    /// </summary>
    [Fact]
    public async Task Prices_coverage_distinguishes_a_genuinely_empty_symbol_from_unchanged()
    {
        await ResetAsync(["ALFA", "EPSL"]);
        var source = new ScriptedSourceClient((symbol, _) =>
            string.Equals(symbol, "ALFA", StringComparison.Ordinal) ? EmptyBody() : RowBody(symbol));

        await using var host = BuildIngestion(source, maxPasses: 2);
        var result = await RunAsync(host);

        Assert.Equal(IngestResult.Partial, result.Status);
        Assert.Equal(1, result.Written); // EPSL only
        Assert.Equal(0, result.Unchanged);

        var stats = await SymbolStatsAsync();
        Assert.Equal(2, stats.Count);
        Assert.Equal(PricesJob.NoRowForDate, stats["ALFA"].GetProperty("reason").GetString());
        Assert.Equal(0, stats["ALFA"].GetProperty("unchanged").GetInt32());
        Assert.Equal(1, stats["EPSL"].GetProperty("written").GetInt32());
        Assert.Equal(JsonValueKind.Null, stats["EPSL"].GetProperty("reason").ValueKind);
        Assert.NotEqual(
            stats["ALFA"].GetProperty("reason").GetString(),
            stats["EPSL"].GetProperty("reason").GetString());

        // The empty symbol was retried (bounded), the published one was not re-fetched.
        Assert.Equal(2, source.Calls("ALFA"));
        Assert.Equal(1, source.Calls("EPSL"));
    }

    /// <summary>
    /// The wall-clock budget bound: with the clock stepping 10 minutes per reading and a
    /// 5-minute budget, the run does <b>one</b> pass and reports the coverage it achieved — the
    /// budget, not the pass count, is what stops it. No spin, no wait.
    /// </summary>
    [Fact]
    public async Task Prices_coverage_budget_bound_stops_the_sweep_without_spinning()
    {
        await ResetAsync(["ALFA"]);
        var source = new ScriptedSourceClient((_, _) => EmptyBody());
        var steppingClock = new SteppingTimeProvider(
            new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            TimeSpan.FromMinutes(10));

        await using var host = BuildIngestion(
            source,
            maxPasses: 5,
            budget: "00:05:00",
            clock: steppingClock);
        var result = await RunAsync(host);

        Assert.Equal(IngestResult.Partial, result.Status);
        Assert.Equal(1, source.Calls("ALFA"));
        Assert.True(
            host.Logs.Contains(evt =>
                evt.RenderMessage().Contains("budget", StringComparison.OrdinalIgnoreCase)
                && evt.RenderMessage().Contains("ALFA", StringComparison.Ordinal)),
            "no log line naming the spent wall-clock budget / ALFA");
    }

    /// <summary>
    /// The cheap path survives: a complete day is one pass over the bound — no re-sweep, no
    /// settle, no extra call — and still <c>succeeded</c>.
    /// </summary>
    [Fact]
    public async Task Prices_coverage_complete_day_is_a_single_pass()
    {
        await ResetAsync(["ALFA", "EPSL"]);
        var source = new ScriptedSourceClient((symbol, _) => RowBody(symbol));

        await using var host = BuildIngestion(source, maxPasses: 3);
        var result = await RunAsync(host);

        Assert.Equal(IngestResult.Succeeded, result.Status);
        Assert.Equal(2, result.Written);
        Assert.Equal(1, source.Calls("ALFA"));
        Assert.Equal(1, source.Calls("EPSL"));
    }

    /// <summary>
    /// The settle-delay case: a non-zero <c>Ingestion:Prices:CoverageSettleDelay</c> is a real
    /// wait on the <b>injected</b> clock (<c>SettleAsync</c> awaits
    /// <c>Task.Delay(delay, TimeProvider)</c>), so the test pumps a
    /// <see cref="FakeTimeProvider"/> until that wait elapses. Nothing sleeps on a real timer:
    /// the clock is only advanced once the settle is actually pending (the job logs it before
    /// awaiting), and the run then proceeds to its one bounded re-sweep.
    /// </summary>
    [Fact]
    public async Task Prices_coverage_settle_delay_paces_the_re_sweep_on_the_injected_clock()
    {
        await ResetAsync(["ALFA"]);
        var source = new ScriptedSourceClient((_, _) => EmptyBody());
        var start = new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var clock = new FakeTimeProvider(start);

        await using var host = BuildIngestion(
            source,
            maxPasses: 2,
            settle: "00:05:00",
            clock: clock);

        var run = RunAsync(host);
        var guard = System.Diagnostics.Stopwatch.StartNew();
        while (!run.IsCompleted && guard.Elapsed < TimeSpan.FromSeconds(30))
        {
            if (host.Logs.Contains(evt => evt.RenderMessage().Contains("settling", StringComparison.OrdinalIgnoreCase)))
            {
                // The settle is pending on the fake clock — advancing fires it.
                clock.Advance(TimeSpan.FromSeconds(1));
            }

            await Task.Delay(1);
        }

        var result = await run.WaitAsync(TimeSpan.FromSeconds(30));

        // One pass, one settle, one re-sweep: the second fetch is the re-sweep's.
        Assert.Equal(IngestResult.Partial, result.Status);
        Assert.Equal(2, source.Calls("ALFA"));

        // The wait elapsed on the injected clock (>= the configured five minutes), not in real time.
        Assert.True(
            clock.GetUtcNow() - start >= TimeSpan.FromMinutes(5),
            "the configured settle delay did not elapse on the injected clock");
        Assert.True(
            host.Logs.Contains(evt =>
                evt.RenderMessage().Contains("settling", StringComparison.OrdinalIgnoreCase)
                && evt.RenderMessage().Contains("00:05:00", StringComparison.Ordinal)),
            "no settle log line naming the configured delay");
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// The production ingestion composition, retargeted at the Testcontainers database and at a
    /// scripted in-process source double (registered last, so it wins over the HTTP client the
    /// composition installs — <b>no live source is ever called</b>). Pacing is disabled and the
    /// settle delay defaults to zero so the coverage loop is deterministic; a case that exercises
    /// the settle binds a non-zero delay together with the fake clock that elapses it.
    /// </summary>
    private IngestionHost BuildIngestion(
        ScriptedSourceClient source,
        int maxPasses = 3,
        string settle = "00:00:00",
        string budget = "00:25:00",
        TimeProvider? clock = null)
    {
        var mail = new RecordingMailDispatcher();
        var logs = new InMemorySerilogSink();
        var provider = clock ?? new FakeTimeProvider(
            new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _postgres.ConnectionString,
                // A dead base address on purpose: the scripted client below is the only path to
                // the source, so a live call would fail loudly instead of succeeding silently.
                ["Ingestion:Prices:BaseUrl"] = "http://127.0.0.1:1",
                ["Ingestion:Prices:Path"] = PricesPath,
                ["Ingestion:Prices:UniverseIndex"] = "XU100",
                ["Ingestion:Prices:RequestDelayMs"] = "0",
                ["Ingestion:Prices:CoverageMaxPasses"] = maxPasses.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Ingestion:Prices:CoverageSettleDelay"] = settle,
                ["Ingestion:Prices:CoverageBudget"] = budget,
            })
            .Build();

        var logger = new LoggerConfiguration()
            .MinimumLevel.Warning()
            .MinimumLevel.Override("Degerli.Ingestion.Prices", Serilog.Events.LogEventLevel.Information)
            .WriteTo.Sink(logs)
            .CreateLogger();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog(logger, dispose: false));
        services.AddSingleton(provider);
        services.AddSingleton<IIngestionAlerter>(new HarnessMailAlerter(mail));
        services.AddMarketDataIngestion(configuration);
        // Last registration wins: the prices adapter reads the scripted double, never HTTP.
        services.AddSingleton<ISourceClient>(source);

        return new IngestionHost(services.BuildServiceProvider(), mail, logs);
    }

    private static async Task<IngestResult> RunAsync(IngestionHost host)
    {
        using var scope = host.Provider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IIngestionJobRunner>();
        return await runner.RunAsync(PricesJob.Code, new IngestionRequest(T));
    }

    /// <summary>Applies the FU universe and clears this class's state; then narrows the XU100
    /// fetch bound to the symbols under test (the coverage gate is a per-constituent read, so a
    /// two-symbol bound keeps the case exact).</summary>
    private async Task ResetAsync(IReadOnlyList<string> bound)
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM quarantined_facts");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM daily_prices");

        var placeholders = string.Join(", ", bound.Select((_, index) => "{" + index + "}"));
        var arguments = new object[]
        {
            "DELETE FROM index_constituents " +
            "WHERE index_id = (SELECT id FROM indices WHERE code = 'XU100') " +
            $"AND instrument_id NOT IN (SELECT id FROM instruments WHERE symbol IN ({placeholders}))",
        }.Concat(bound).ToArray();
        await db.Database.ExecuteSqlRawAsync((string)arguments[0], arguments.Skip(1).ToArray());
    }

    /// <summary>The run ledger row's per-symbol breakdown, keyed by symbol.</summary>
    private async Task<Dictionary<string, JsonElement>> SymbolStatsAsync()
    {
        await using var db = _postgres.CreateContext();
        var run = await db.IngestRuns.SingleAsync(r => r.JobCode == "prices");
        using var stats = JsonDocument.Parse(run.StatsJson!);
        return stats.RootElement.GetProperty("symbols").EnumerateArray()
            .ToDictionary(element => element.GetProperty("symbol").GetString()!, element => element.Clone(), StringComparer.Ordinal);
    }

    /// <summary>
    /// The canonical EOD double envelope (the L2 golden contract, §10.9.7) carrying one stored
    /// fixture row for <paramref name="symbol"/> — what the source serves once it has published
    /// the session.
    /// </summary>
    private static string RowBody(string symbol)
    {
        var fact = L2.Prices.Single(price => price.Date == T && price.Symbol == symbol);
        return JsonSerializer.Serialize(new
        {
            sourceRef = "isyatirim",
            date = T,
            items = new[]
            {
                new
                {
                    symbol,
                    previousClose = (decimal?)null,
                    open = fact.Open,
                    high = fact.High,
                    low = fact.Low,
                    close = fact.CloseRaw,
                    volume = fact.Volume,
                },
            },
        }, JsonOptions);
    }

    /// <summary>The honest empty: a valid, provenanced payload with no session row for the date.</summary>
    private static string EmptyBody() =>
        JsonSerializer.Serialize(new
        {
            sourceRef = "isyatirim",
            date = T,
            items = Array.Empty<object>(),
        }, JsonOptions);

    /// <summary>Fixture price accessors (the FU §4 goldens the source double serves).</summary>
    private static class Fixture
    {
        public static decimal Close(string symbol) =>
            L2.Prices.Single(price => price.Date == T && price.Symbol == symbol).CloseRaw;
    }

    /// <summary>
    /// A scripted source double: answers per requested symbol and per call ordinal, so "empty
    /// on pass 1, published on pass 2" is exact. Counts calls per symbol (the pacing/bound
    /// assertions read them).
    /// </summary>
    private sealed class ScriptedSourceClient : ISourceClient
    {
        private readonly Func<string, int, string> _respond;
        private readonly Dictionary<string, int> _calls = new(StringComparer.Ordinal);
        private readonly object _gate = new();

        public ScriptedSourceClient(Func<string, int, string> respond) => _respond = respond;

        public int Calls(string symbol)
        {
            lock (_gate)
            {
                return _calls.TryGetValue(symbol, out var calls) ? calls : 0;
            }
        }

        public Task<string> GetStringAsync(string path, CancellationToken cancellationToken = default)
        {
            var symbol = SymbolOf(path);
            int call;
            lock (_gate)
            {
                call = _calls.TryGetValue(symbol, out var previous) ? previous + 1 : 1;
                _calls[symbol] = call;
            }

            return Task.FromResult(_respond(symbol, call));
        }

        /// <summary>The adapter appends the symbol as a query parameter (path-only route).</summary>
        private static string SymbolOf(string path)
        {
            var marker = path.IndexOf("symbol=", StringComparison.Ordinal);
            if (marker < 0)
            {
                throw new InvalidOperationException($"The prices route carried no symbol: '{path}'.");
            }

            var value = path[(marker + "symbol=".Length)..];
            var end = value.IndexOf('&');
            return Uri.UnescapeDataString(end < 0 ? value : value[..end]);
        }
    }

    /// <summary>A fake clock that advances a fixed step on every reading — the deterministic way
    /// to drive the run's wall-clock budget without any real wait.</summary>
    private sealed class SteppingTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _start;
        private readonly TimeSpan _step;
        private long _reads;

        public SteppingTimeProvider(DateTimeOffset start, TimeSpan step)
        {
            _start = start;
            _step = step;
        }

        public override DateTimeOffset GetUtcNow() =>
            _start + TimeSpan.FromTicks(_step.Ticks * Interlocked.Increment(ref _reads));
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
