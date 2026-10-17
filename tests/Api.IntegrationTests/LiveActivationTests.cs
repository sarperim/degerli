using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Api.IntegrationTests.Harness.Mail;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.Coverage;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Prices;
using Degerli.Ingestion.Universe;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Serilog;
using Serilog.Events;
using WireMock.Logging;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-foundation-018 acceptance — <b>live activation</b>: the production <c>prices</c>
/// path bounded to the configured index (01 v1.5 §10.9.2/§10.9.3). The per-ticker fetch
/// loop resolves the current constituents of <c>Ingestion:Prices:UniverseIndex</c> and
/// iterates them strictly sequentially with <c>Ingestion:Prices:RequestDelayMs</c> pacing,
/// isolating per-symbol failures, and cold-starting the checked-in XU030 launch-seed.
/// <para>
/// The cases here are CI-verifiable against the WireMock source double (TC-MDF-072..076):
/// loop mechanics, the universe bound, pacing, per-symbol failure isolation and the launch
/// seed. The production default bound <c>XU030</c> is asserted as a config value; the
/// bound switch is exercised against the FU XU30 subset and the FU XU100 set (the L2
/// harness binding of §8). The live-only acceptance criteria (the₺-volume threshold
/// recalibration, the bounded live capture re-validated by re-running TC-MDF-063, and the
/// real-world correctness of the XU030 membership list) are observations of the first live
/// pull — recorded, flagged in the PR, not fabricated here.
/// </para>
/// </summary>
public sealed class LiveActivationTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private const string PricesPath = "/canned-sources/prices-ok";

    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    // The FU XU30 subset (5 current members); the per-ticker loop bound under the FU XU30 index.
    private static readonly string[] Xu30Members = ["ALFA", "EPSL", "ETA", "KAPPA", "REST"];

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public LiveActivationTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    /// <summary>
    /// TC-MDF-072 — the per-ticker loop resolves the configured index's current constituents.
    /// <para>
    /// The loop issues exactly one fetch per current XU30 member (5 requests at the
    /// symbol-scoped route); only those 5 instruments receive <c>daily_prices</c> rows at T
    /// and the other 11 seeded instruments are neither requested nor written. Requests are
    /// strictly sequential (no overlapping in-flight requests) and a constituent with a
    /// closed <c>effective_to</c> is never fetched. The canonical double envelope is served
    /// through the symbol-scoped seam (the L2 golden seam; real-envelope decode is separately
    /// pinned by <c>TC-MDF-063</c> and the two compose via TC-MDF-069's detection).
    /// </para>
    /// </summary>
    [Fact]
    public async Task TC_MDF_072_Per_ticker_loop_resolves_the_configured_index_current_constituents()
    {
        await ResetAsync();
        await AddClosedXu30MemberAsync("ZETA");
        StubPrices(serverDelay: TimeSpan.FromMilliseconds(30));
        _wireMock.Server.ResetLogEntries();

        await using var host = BuildIngestion(universeIndex: "XU30", requestDelayMs: 0);
        var result = await RunAsync(host, "prices");

        Assert.Equal("succeeded", result.Status);
        Assert.Equal(5, result.Written);

        // Exactly one fetch per current XU30 member; no request for the closed member.
        Assert.Equal(5, _wireMock.RequestsFor(PricesPath));

        await using (var db = _postgres.CreateContext())
        {
            var symbols = await db.Instruments.ToDictionaryAsync(i => i.Id, i => i.Symbol);
            var rowSymbols = (await db.DailyPrices.Where(p => p.PriceDate == T).ToListAsync())
                .Select(r => symbols[r.InstrumentId])
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(Xu30Members, rowSymbols); // the 5 current members, alphabetically
            Assert.DoesNotContain("ZETA", rowSymbols); // closed member is not fetched

            // The run-ledger row carries the aggregate stats for the run.
            var run = Assert.Single(await db.IngestRuns.Where(r => r.JobCode == "prices").ToListAsync());
            Assert.Equal("succeeded", run.Status);
            using var stats = JsonDocument.Parse(run.StatsJson!);
            Assert.Equal(5, stats.RootElement.GetProperty("written").GetInt32());
        }

        // Strictly sequential: each response completes (>= the server's in-flight time)
        // before the next request is issued, so consecutive arrivals are separated.
        var entries = RequestEntries();
        Assert.Equal(5, entries.Count);
        for (var i = 1; i < entries.Count; i++)
        {
            var gap = entries[i].RequestMessage!.DateTime - entries[i - 1].RequestMessage!.DateTime;
            Assert.True(
                gap >= TimeSpan.FromMilliseconds(25),
                $"requests {i - 1}->{i} overlapped (gap {gap.TotalMilliseconds:0} ms).");
        }

        // Per-symbol stats are emitted for every member. (TKT-foundation-018 scope note: the
        // run-ledger stats_json shape lives in the out-of-scope Jobs module, so the per-symbol
        // breakdown is surfaced as structured logs — flagged in the PR.)
        foreach (var symbol in Xu30Members)
        {
            Assert.True(
                host.Logs.Contains(evt =>
                    evt.RenderMessage().Contains($"symbol \"{symbol}\"", StringComparison.Ordinal)),
                $"no per-symbol stat line for {symbol}");
        }
    }

    /// <summary>
    /// TC-MDF-073 — per-symbol failure isolation within the loop: one member's route returns
    /// 500 for the whole run. The failed symbol's rows are absent, the run does not abort
    /// (the other four ingest), exactly one aggregate alert names the symbol (mail double +
    /// Serilog sink), the ledger reaches a terminal partial status, and no quarantine rows
    /// are written — a transport failure is a source-down, not a parse/validation event
    /// (the TC-MDF-070 class).
    /// </summary>
    [Fact]
    public async Task TC_MDF_073_Per_symbol_failure_isolation_within_the_loop()
    {
        await ResetAsync();
        StubPerSymbol(stalledSymbol: "ALFA");
        _wireMock.Server.ResetLogEntries();

        await using var host = BuildIngestion(universeIndex: "XU30", requestDelayMs: 0);
        var result = await RunAsync(host, "prices");

        // The run does not abort; it reaches a terminal partial status.
        Assert.Equal("partial", result.Status);

        // The failed symbol is absent; the other four ingested.
        await using (var db = _postgres.CreateContext())
        {
            var symbols = await db.Instruments.ToDictionaryAsync(i => i.Id, i => i.Symbol);
            var rowSymbols = (await db.DailyPrices.Where(p => p.PriceDate == T).ToListAsync())
                .Select(r => symbols[r.InstrumentId])
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(["EPSL", "ETA", "KAPPA", "REST"], rowSymbols);
            Assert.DoesNotContain("ALFA", rowSymbols);

            // No quarantine for a transport-level failure.
            Assert.Equal(0, await db.QuarantinedFacts.CountAsync(q => q.JobCode == "prices"));

            var run = Assert.Single(await db.IngestRuns.Where(r => r.JobCode == "prices").ToListAsync());
            Assert.Equal("partial", run.Status);
        }

        // All five members were attempted — the loop continued past the failure.
        Assert.Equal(5, _wireMock.RequestsFor(PricesPath));

        // Exactly one aggregate alert, naming the failed symbol: mail double + Serilog sink.
        var alert = Assert.Single(host.Mail.Sent);
        Assert.Contains("prices", alert.Subject, StringComparison.Ordinal);
        Assert.Contains("ALFA", alert.Subject, StringComparison.Ordinal);
        Assert.True(
            host.Logs.Contains(evt =>
                evt.RenderMessage().Contains("ALFA", StringComparison.Ordinal)
                && evt.RenderMessage().Contains(PricesJob.SourceFetchFailed, StringComparison.Ordinal)),
            "no Serilog alert naming ALFA / SOURCE_FETCH_FAILED");
    }

    /// <summary>
    /// TC-MDF-074 — sequential pacing honors <c>RequestDelayMs</c>. With 40 ms configured,
    /// every consecutive inter-request gap is at least the configured delay and the total
    /// elapsed is at least (N−1) × delay; zero overlapping in-flight requests. With 0 the run
    /// completes with no enforced wait (no gap assertion). Assertions are lower bounds only.
    /// </summary>
    [Fact]
    public async Task TC_MDF_074_Sequential_pacing_honors_request_delay()
    {
        await ResetAsync();
        StubPrices(serverDelay: TimeSpan.Zero);
        _wireMock.Server.ResetLogEntries();

        await using (var paced = BuildIngestion(universeIndex: "XU30", requestDelayMs: 40))
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await RunAsync(paced, "prices");
            stopwatch.Stop();

            Assert.Equal("succeeded", result.Status);
            Assert.Equal(5, _wireMock.RequestsFor(PricesPath));

            var entries = RequestEntries();
            Assert.Equal(5, entries.Count);
            for (var i = 1; i < entries.Count; i++)
            {
                var gap = entries[i].RequestMessage!.DateTime - entries[i - 1].RequestMessage!.DateTime;
                // Lower bound only: the configured wait must be enforced. The 2 ms slack
                // absorbs the source double's millisecond timestamp granularity — a 0 ms
                // run produces sub-millisecond gaps, never a ~40 ms one.
                Assert.True(
                    gap >= TimeSpan.FromMilliseconds(38),
                    $"inter-request gap {gap.TotalMilliseconds:0} ms < configured 40 ms.");
            }

            Assert.True(
                stopwatch.Elapsed >= TimeSpan.FromMilliseconds(160),
                $"elapsed {stopwatch.Elapsed.TotalMilliseconds:0} ms < (N-1) x 40 ms.");
        }

        // RequestDelayMs = 0: the run completes with no enforced wait (no gap assertion).
        _wireMock.Server.ResetLogEntries();
        await using (var unpaced = BuildIngestion(universeIndex: "XU30", requestDelayMs: 0))
        {
            var result = await RunAsync(unpaced, "prices");

            // The re-run is idempotent — the five members were already written above.
            Assert.Equal(0, result.Written);
            Assert.Equal(5, result.Unchanged);
            Assert.Equal(5, _wireMock.RequestsFor(PricesPath));
        }
    }

    /// <summary>
    /// TC-MDF-075 — the fetch bound follows the configured index (config-only flip). XU30 →
    /// 5 per-ticker fetches / 5 instruments' rows; XU100 → 16 fetches / 16 rows (the L2
    /// harness binding that keeps the existing price-path goldens intact, §8 preamble). With
    /// no override the bound resolves to the production default <c>XU030</c>.
    /// </summary>
    [Fact]
    public async Task TC_MDF_075_Fetch_bound_follows_the_configured_index()
    {
        await ResetAsync();
        StubPrices(serverDelay: TimeSpan.Zero);

        _wireMock.Server.ResetLogEntries();
        await using (var xu30 = BuildIngestion(universeIndex: "XU30", requestDelayMs: 0))
        {
            await RunAsync(xu30, "prices");
        }

        Assert.Equal(5, _wireMock.RequestsFor(PricesPath));
        await using (var db = _postgres.CreateContext())
        {
            Assert.Equal(5, await db.DailyPrices.CountAsync(p => p.PriceDate == T));
        }

        _wireMock.Server.ResetLogEntries();
        await using (var xu100 = BuildIngestion(universeIndex: "XU100", requestDelayMs: 0))
        {
            await RunAsync(xu100, "prices");
        }

        Assert.Equal(16, _wireMock.RequestsFor(PricesPath));
        await using (var db = _postgres.CreateContext())
        {
            Assert.Equal(16, await db.DailyPrices.CountAsync(p => p.PriceDate == T));
        }

        // No override configured → the production default XU030 (config-only flip, no code).
        await using (var host = BuildIngestion(universeIndex: null, requestDelayMs: 0))
        {
            using var scope = host.Provider.CreateScope();
            var options = scope.ServiceProvider.GetRequiredService<IOptions<PricesSourceOptions>>().Value;
            Assert.Equal("XU030", options.UniverseIndex);
        }
    }

    /// <summary>
    /// TC-MDF-076 — XU030 membership launch-seed (reuses the <c>TC-MDF-062</c> launch-seed
    /// machinery through <see cref="UniverseSyncStore.SeedLaunchConstituentsAsync"/>). The
    /// seed writes append-only, effective-dated current membership (<c>effective_to</c> NULL,
    /// <c>source_ref</c> recorded); re-application is idempotent (no duplicate rows, no
    /// mutations); the accepted pre-launch constituent-history gap is recorded
    /// (<c>available_from</c> = launch date); and the per-ticker loop bound to XU030 resolves
    /// exactly the seeded current members (composing with TC-MDF-072).
    /// <para>
    /// The checked-in seed's real-world correctness against BIST's actual XU030 at launch is a
    /// live-pull observation (the TKT-foundation-018 criterion), not asserted here.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TC_MDF_076_Xu030_membership_launch_seed()
    {
        await ResetAsync();
        await CreateXu030IndexAndInstrumentsAsync();
        StubPrices(serverDelay: TimeSpan.Zero);

        var recordedAt = new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        UniverseLaunchSeedResult first;
        UniverseLaunchSeedResult second;

        await using (var seedHost = BuildIngestion(universeIndex: null, requestDelayMs: 0))
        {
            using var scope = seedHost.Provider.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<UniverseSyncStore>();
            var coverage = scope.ServiceProvider.GetRequiredService<CoverageRecorder>();

            first = await Xu030LaunchSeed.ApplyAsync(store, coverage, recordedAt);
            second = await Xu030LaunchSeed.ApplyAsync(store, coverage, recordedAt);
        }

        Assert.Equal(Xu030LaunchSeed.Symbols.Count, first.Seeded);
        Assert.Empty(first.UnknownSymbols);
        Assert.Equal(0, second.Seeded);
        Assert.Equal(Xu030LaunchSeed.Symbols.Count, second.AlreadyPresent);

        await using (var db = _postgres.CreateContext())
        {
            var xu030 = await db.Indices.Where(i => i.Code == "XU030").Select(i => i.Id).SingleAsync();
            var rows = await db.IndexConstituents.Where(c => c.IndexId == xu030).ToListAsync();

            // Idempotent re-application: no duplicate rows, no mutations.
            Assert.Equal(Xu030LaunchSeed.Symbols.Count, rows.Count);
            Assert.All(rows, row =>
            {
                Assert.Null(row.EffectiveTo);
                Assert.Equal(Xu030LaunchSeed.AsOfDate, row.EffectiveFrom);
                Assert.Equal(Xu030LaunchSeed.SourceRef, row.SourceRef);
            });

            // The accepted pre-launch constituent-history gap stays an explicit record.
            var gap = await db.CoverageMetadata.SingleAsync(c =>
                c.Scope == "universe" && c.DataType == CoverageDataTypes.Constituents);
            Assert.Equal(Xu030LaunchSeed.AsOfDate, gap.AvailableFrom);
        }

        // The loop bound to XU030 resolves exactly the seeded current members.
        _wireMock.Server.ResetLogEntries();
        await using (var loopHost = BuildIngestion(universeIndex: "XU030", requestDelayMs: 0))
        {
            var result = await RunAsync(loopHost, "prices");

            Assert.Equal("succeeded", result.Status);
            Assert.Equal(Xu030LaunchSeed.Symbols.Count, _wireMock.RequestsFor(PricesPath));

            foreach (var symbol in Xu030LaunchSeed.Symbols)
            {
                Assert.True(
                    loopHost.Logs.Contains(evt =>
                        evt.RenderMessage().Contains($"symbol \"{symbol}\"", StringComparison.Ordinal)),
                    $"the loop did not resolve seeded member {symbol}");
            }
        }
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// The ingestion composition used in production, retargeted at the Testcontainers database
    /// and the WireMock source double. The fetch bound and pacing are the two
    /// <c>Ingestion:Prices</c> knobs (01 §10.4); <paramref name="universeIndex"/> = null omits
    /// the key so the production default resolves. A recording mail double replaces the alerting
    /// seam; a Serilog test sink captures the per-symbol stats and log alerts.
    /// </summary>
    private IngestionHost BuildIngestion(string? universeIndex, int requestDelayMs)
    {
        var mail = new RecordingMailDispatcher();
        var logs = new InMemorySerilogSink();
        var clock = new FakeTimeProvider(new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _postgres.ConnectionString,
            ["Ingestion:Prices:BaseUrl"] = _wireMock.BaseUrl,
            ["Ingestion:Prices:Path"] = PricesPath,
            ["Ingestion:Prices:RequestDelayMs"] = requestDelayMs.ToString(CultureInfo.InvariantCulture),
        };
        if (universeIndex is not null)
        {
            settings["Ingestion:Prices:UniverseIndex"] = universeIndex;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        // Capture the prices job's per-symbol stats (Information) without the EF noise.
        var logger = new LoggerConfiguration()
            .MinimumLevel.Warning()
            .MinimumLevel.Override("Degerli.Ingestion.Prices", LogEventLevel.Information)
            .WriteTo.Sink(logs)
            .CreateLogger();

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

    /// <summary>
    /// Resets the shared container to the FU seeded state and clears everything this class
    /// writes: prices, quarantine, the run ledger, universe scope gaps, any closed/extra XU30
    /// membership and the test-local XU030 index/instruments.
    /// </summary>
    private async Task ResetAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM quarantined_facts");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM daily_prices");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM coverage_metadata WHERE scope = 'universe'");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM index_constituents WHERE effective_to IS NOT NULL");
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM index_constituents WHERE index_id IN (SELECT id FROM indices WHERE code = 'XU030')");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM indices WHERE code = 'XU030'");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM instruments WHERE source_ref = 'test:xu030-launch'");
    }

    /// <summary>Adds a closed (removed) XU30 membership row so TC-MDF-072 can assert
    /// <c>effective_to IS NULL</c> is the loop's boundary.</summary>
    private async Task AddClosedXu30MemberAsync(string symbol)
    {
        await using var db = _postgres.CreateContext();
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO index_constituents (index_id, instrument_id, effective_from, effective_to, source_ref, recorded_at) " +
            "SELECT (SELECT id FROM indices WHERE code = 'XU30'), " +
            "(SELECT id FROM instruments WHERE symbol = {0}), " +
            "DATE '2026-09-01', DATE '2026-10-06', 'fixture:closed-membership', now()",
            symbol);
    }

    /// <summary>Creates the test-local XU030 reference + instrument rows the launch-seed needs
    /// (the real XU030 members are outside the FU universe).</summary>
    private async Task CreateXu030IndexAndInstrumentsAsync()
    {
        await using var db = _postgres.CreateContext();
        var recordedAt = new DateTimeOffset(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        db.Indices.Add(new MarketIndex { Code = "XU030", NameTr = "BIST 30", NameEn = "BIST 30" });
        foreach (var symbol in Xu030LaunchSeed.Symbols)
        {
            db.Instruments.Add(new Instrument
            {
                Symbol = symbol,
                Name = symbol,
                AccountingProfile = "industrial",
                Status = "active",
                SourceRef = "test:xu030-launch",
                RecordedAt = recordedAt,
            });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>Stubs the canonical double envelope at the symbol-scoped route (path-only; the
    /// adapter appends <c>?symbol=</c>). An optional server delay makes in-flight overlap
    /// observable.</summary>
    private void StubPrices(TimeSpan serverDelay)
    {
        _wireMock.Server.ResetMappings();
        var payload = CannedSourceCatalog.Get(L2, CannedSourceCatalog.PricesOk);

        var response = Response.Create()
            .WithStatusCode(200)
            .WithHeader("Content-Type", "application/json")
            .WithBody(payload.Body);
        if (serverDelay > TimeSpan.Zero)
        {
            response = response.WithDelay(serverDelay);
        }

        _wireMock.Server.Given(Request.Create().WithPath(PricesPath).UsingGet()).RespondWith(response);
    }

    /// <summary>Stubs one param-specific route per current XU30 member: the canonical envelope
    /// for all but <paramref name="stalledSymbol"/>, which returns 500 (source-down).</summary>
    private void StubPerSymbol(string stalledSymbol)
    {
        _wireMock.Server.ResetMappings();
        var ok = CannedSourceCatalog.Get(L2, CannedSourceCatalog.PricesOk);
        var down = CannedSourceCatalog.Get(L2, CannedSourceCatalog.PricesSourceDown);

        foreach (var symbol in Xu30Members)
        {
            var payload = string.Equals(symbol, stalledSymbol, StringComparison.Ordinal) ? down : ok;
            _wireMock.Server
                .Given(Request.Create().WithPath(PricesPath).WithParam("symbol", new[] { symbol }).UsingGet())
                .RespondWith(Response.Create()
                    .WithStatusCode(payload.StatusCode)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(payload.Body));
        }
    }

    /// <summary>The price-route request log entries, ordered by arrival time.</summary>
    private List<ILogEntry> RequestEntries() =>
        _wireMock.Server.LogEntries
            .Where(entry => entry.RequestMessage?.Path == PricesPath)
            .OrderBy(entry => entry.RequestMessage!.DateTime)
            .ToList();

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
