using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Kap;
using Degerli.Ingestion.Shares;
using Degerli.Ingestion.Sources;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Serilog;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TC-MDF-092 — the <c>shares</c> lane's decode + honest store (TKT-foundation-058,
/// 01 v1.5.36 §10.9.11). The double is
/// <see cref="CannedSourceCatalog.MemberSecuritiesOk"/>, committed to the repo and derived from the
/// live 2026-10-22 dev-gateway <c>/memberSecurities</c> capture; the capture itself is an ephemeral
/// <c>/tmp</c> snapshot and is never referenced. The cases pin:
/// <list type="bullet">
/// <item>per-instrument matching by <c>borsaKodu</c> (primary) / <c>isin</c> (fallback), keyed on a
/// trading-open line;</item>
/// <item>tertip aggregation (ASELS A+B = the real 4.56B count);</item>
/// <item>honest absence — an instrument with no line (or only closed lines) gets <b>no row</b>,
/// never a zero;</item>
/// <item>a missing <c>currentCapital</c> is skipped, counted, never zero-filled — and the matched
/// group it belonged to is withheld rather than served as a partial tertip sum;</item>
/// <item>the input-aware re-run (unchanged writes nothing; a changed count repairs in place);</item>
/// <item>the failure posture (transport → failed run, malformed → quarantine) and one paced call
/// per run.</item>
/// </list>
/// </summary>
public sealed class MemberSecuritiesIngestTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    /// <summary>The shipped VYK <c>/memberSecurities</c> route (01 v1.5.36 §10.9.11).</summary>
    private const string MemberSecuritiesPath = "/memberSecurities";

    private static readonly DateTimeOffset RecordedAt = new(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public MemberSecuritiesIngestTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    // ---------------------------------------------------------------------------------
    // (i) Decode + store
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// TC-MDF-092 — the run writes <b>one row per matched instrument</b>, the value being the sum
    /// of the instrument's security lines' <c>currentCapital</c> (the tertip-aggregation rule the
    /// live ASELS/SDMR counts evidence), with the source provenance and the run instant recorded.
    /// </summary>
    [Fact]
    public async Task TC_MDF_092_ingest_writes_one_row_per_matched_instrument_with_tertip_aggregation()
    {
        await ResetAsync();
        await BindInstrumentsAsync();
        StubBody(CannedSourceCatalog.Get(L2, CannedSourceCatalog.MemberSecuritiesOk).Body);

        var host = BuildIngestion();
        var result = await RunAsync(host);

        Assert.Equal(IngestResult.Succeeded, result.Status);
        Assert.Equal(7, result.Written);
        Assert.Equal(0, result.Unchanged);
        Assert.Equal(0, result.Quarantined);
        Assert.Equal(2, result.Skipped);

        await using var db = _postgres.CreateContext();
        var rows = await RowsBySymbolAsync(db);

        // ASELS A (closed, 2,421,818,181.816) + B (open, 2,138,181,818.184) = the real
        // 4.56B count — the aggregation is the point, not an artefact of one tertip.
        Assert.Equal(4_560_000_000m, rows["ASELS"].SharesOutstanding);
        // THYAO C (closed, 0.01) + A (open, 1,379,999,999.99) = 1.38B.
        Assert.Equal(1_380_000_000m, rows["THYAO"].SharesOutstanding);
        Assert.Equal(1_926_795_598m, rows["TUPRS"].SharesOutstanding);
        Assert.Equal(4_200_000_000m, rows["GARAN"].SharesOutstanding);
        Assert.Equal(592_105_263m, rows["AEFES"].SharesOutstanding);
        // ISDMR's two tertips are both trading-open: A 20 + B 2,899,999,980 = 2.9B.
        Assert.Equal(2_900_000_000m, rows["ISDMR"].SharesOutstanding);

        foreach (var row in rows.Values)
        {
            Assert.Equal("kap", row.SourceRef);
            Assert.Equal(RecordedAt, row.RecordedAt);
        }
    }

    /// <summary>
    /// TC-MDF-092 — matching is <c>borsaKodu</c>-primary / <c>isin</c>-fallback: an instrument
    /// whose symbol matches a line's trading code is matched by it (a different instrument sharing
    /// the line's ISIN is <b>not</b> matched), while a line whose trading code matches no
    /// instrument resolves through its ISIN.
    /// </summary>
    [Fact]
    public async Task TC_MDF_092_matching_is_borsa_kodu_primary_and_isin_fallback()
    {
        await ResetAsync();
        await BindInstrumentsAsync();
        StubBody(CannedSourceCatalog.Get(L2, CannedSourceCatalog.MemberSecuritiesOk).Body);

        var host = BuildIngestion();
        await RunAsync(host);

        await using var db = _postgres.CreateContext();
        var rows = await RowsBySymbolAsync(db);

        // DRVFM carries the DRVFB line's ISIN but not its trading code: the fallback resolves it.
        Assert.True(rows.ContainsKey("DRVFM"));
        Assert.Equal(1_234_567m, rows["DRVFM"].SharesOutstanding);

        // OTHER carries ASELS's ISIN — but the ASELS line matches ASELS by trading code first, so
        // OTHER is never a candidate (primary wins; no second row for one line).
        Assert.False(rows.ContainsKey("OTHER"));
    }

    /// <summary>
    /// TC-MDF-092 — honest absence: an instrument with no security line and one whose only lines
    /// are closed both stay <b>absent</b> from the store. No row is ever written as a zero.
    /// </summary>
    [Fact]
    public async Task TC_MDF_092_unmatched_or_closed_instruments_stay_absent_never_zero()
    {
        await ResetAsync();
        await BindInstrumentsAsync();
        StubBody(CannedSourceCatalog.Get(L2, CannedSourceCatalog.MemberSecuritiesOk).Body);

        var host = BuildIngestion();
        await RunAsync(host);

        await using var db = _postgres.CreateContext();
        var rows = await RowsBySymbolAsync(db);

        // GLRMK is an archive-absent member: no line at all.
        Assert.False(rows.ContainsKey("GLRMK"));
        // CLSDR's lines exist but none is trading-open: a closed line never seeds a count.
        Assert.False(rows.ContainsKey("CLSDR"));

        Assert.Equal(7, rows.Count);
        Assert.DoesNotContain(rows.Values, row => row.SharesOutstanding == 0m);
    }

    /// <summary>
    /// TC-MDF-092 — the trading-open flag is honoured across its observed/tolerated spellings: a
    /// string <c>"1"</c> and a numeric <c>1</c> open the line's group (a row is written), while a
    /// string <c>"0"</c> / numeric <c>0</c> leave it closed (no row). Before the tolerant decode
    /// the <c>"1"</c>/numeric spellings read as closed and both instruments silently vanished.
    /// An unreadable line on a member we do not cover is out of scope, not a skip.
    /// </summary>
    [Fact]
    public async Task TC_MDF_092_trading_open_flag_spelling_variants_are_honoured()
    {
        await ResetAsync();
        await BindInstrumentsAsync();
        StubBody(
            """
            [
              {
                "member": { "id": "9101", "sirketUnvan": "STRING FLAG A.Ş. (derived spelling variant)" },
                "securities": [
                  { "isin": "TRESPE100011", "borsaKodu": "SPELL1", "currentCapital": 100, "borsadaIslemeAcik": "1" }
                ]
              },
              {
                "member": { "id": "9102", "sirketUnvan": "NUMERIC FLAG A.Ş. (derived spelling variant)" },
                "securities": [
                  { "isin": "TRESPE200018", "borsaKodu": "SPELL2", "currentCapital": 200, "borsadaIslemeAcik": 1 }
                ]
              },
              {
                "member": { "id": "9103", "sirketUnvan": "CLOSED FLAGS A.Ş. (derived spelling variant)" },
                "securities": [
                  { "isin": "TRESPE000012", "borsaKodu": "SPELL0", "currentCapital": 300, "borsadaIslemeAcik": "0" },
                  { "isin": "TRESPE000020", "borsaKodu": "SPELL0", "currentCapital": 400, "borsadaIslemeAcik": 0 }
                ]
              },
              {
                "member": { "id": "9104", "sirketUnvan": "NOT OURS A.Ş." },
                "securities": [
                  { "isin": "TRENOTOU00011", "borsaKodu": "NOTOURS", "borsadaIslemeAcik": true }
                ]
              }
            ]
            """);

        var host = BuildIngestion();
        var result = await RunAsync(host);

        Assert.Equal(IngestResult.Succeeded, result.Status);
        Assert.Equal(2, result.Written);
        // NOTOURS's unreadable line belongs to a member we do not cover: out of scope, no skip.
        Assert.Equal(0, result.Skipped);

        await using var db = _postgres.CreateContext();
        var rows = await RowsBySymbolAsync(db);

        Assert.Equal(100m, rows["SPELL1"].SharesOutstanding);
        Assert.Equal(200m, rows["SPELL2"].SharesOutstanding);
        Assert.False(rows.ContainsKey("SPELL0"));
        Assert.DoesNotContain(rows.Values, row => row.SharesOutstanding == 0m);
    }

    /// <summary>
    /// TC-MDF-092 — the schema's declared single-object shape (one member object carrying
    /// <c>member</c>/<c>securities</c>) decodes as <b>one member</b>, not as its own security
    /// lines: before the fix the root object was unwrapped by its first array property, so every
    /// line became an empty member and the body silently produced zero rows.
    /// </summary>
    [Fact]
    public async Task TC_MDF_092_single_object_body_decodes_as_one_member()
    {
        await ResetAsync();
        await BindInstrumentsAsync();
        StubBody(
            """
            {
              "member": { "id": "866", "sirketUnvan": "ASELSAN ELEKTRONİK SANAYİ VE TİCARET A.Ş." },
              "securities": [
                {
                  "isin": "TREASLS00018", "borsaKodu": "ASELS", "tertipGroup": " A  ",
                  "capital": 2421818181.816, "currentCapital": 2421818181.816,
                  "borsadaIslemeAcik": false
                },
                {
                  "isin": "TRAASELS91H2", "borsaKodu": "ASELS", "tertipGroup": " B  ",
                  "capital": 2138181818.184, "currentCapital": 2138181818.184,
                  "borsadaIslemeAcik": true
                }
              ]
            }
            """);

        var host = BuildIngestion();
        var result = await RunAsync(host);

        Assert.Equal(IngestResult.Succeeded, result.Status);
        Assert.Equal(1, result.Written);
        Assert.Equal(0, result.Skipped);

        await using var db = _postgres.CreateContext();
        var rows = await RowsBySymbolAsync(db);
        Assert.Equal(4_560_000_000m, rows["ASELS"].SharesOutstanding);
    }

    /// <summary>
    /// TC-MDF-092 — a group withheld this run yields <b>no row</b> even when a previous run stored
    /// one: the stale count is removed rather than left served under its old <c>asOf</c>.
    /// </summary>
    [Fact]
    public async Task TC_MDF_092_withheld_group_removes_a_previously_stored_row()
    {
        await ResetAsync();
        await BindInstrumentsAsync();

        // A complete DRVE row is stored first.
        StubBody(
            """
            [
              {
                "member": { "id": "9001", "sirketUnvan": "DERİVE HOLDİNG A.Ş." },
                "securities": [
                  { "isin": "TREDRVE00011", "borsaKodu": "DRVE", "tertipGroup": "A", "currentCapital": 1000000000, "borsadaIslemeAcik": true }
                ]
              }
            ]
            """);

        var host = BuildIngestion();
        await RunAsync(host);

        await using (var db = _postgres.CreateContext())
        {
            Assert.Equal(1_000_000_000m, (await RowsBySymbolAsync(db))["DRVE"].SharesOutstanding);
        }

        // The next run withholds the group (one tertip lost its currentCapital): no row may remain.
        StubBody(
            """
            [
              {
                "member": { "id": "9001", "sirketUnvan": "DERİVE HOLDİNG A.Ş." },
                "securities": [
                  { "isin": "TREDRVE00011", "borsaKodu": "DRVE", "tertipGroup": "A", "currentCapital": 1000000000, "borsadaIslemeAcik": true },
                  { "isin": "TREDRVE00029", "borsaKodu": "DRVE", "tertipGroup": "B", "capital": 500000000, "borsadaIslemeAcik": true }
                ]
              }
            ]
            """);

        var withheldRun = await RunAsync(host);
        Assert.Equal(IngestResult.Succeeded, withheldRun.Status);
        Assert.Equal(1, withheldRun.Written);
        Assert.Equal(1, withheldRun.Skipped);

        await using (var db = _postgres.CreateContext())
        {
            Assert.False((await RowsBySymbolAsync(db)).ContainsKey("DRVE"));
            Assert.Equal(0, await db.ShareCounts.CountAsync());
        }
    }

    /// <summary>
    /// TC-MDF-092 — a security line without <c>currentCapital</c> is <b>skipped and counted</b>,
    /// never written as a zero: the sole-line case yields no row, and a matched group with one
    /// unreadable tertip is withheld (a partial sum would be a fabricated count).
    /// </summary>
    [Fact]
    public async Task TC_MDF_092_missing_current_capital_is_skipped_counted_and_never_zero_filled()
    {
        await ResetAsync();
        await BindInstrumentsAsync();
        StubBody(CannedSourceCatalog.Get(L2, CannedSourceCatalog.MemberSecuritiesOk).Body);

        var host = BuildIngestion();
        var result = await RunAsync(host);

        // Two derived anomaly lines carry no currentCapital: DRVE's B tertip and NOCAP's only line.
        Assert.Equal(2, result.Skipped);

        await using var db = _postgres.CreateContext();
        var rows = await RowsBySymbolAsync(db);

        // NOCAP's only line is unreadable → no count at all, never 0.
        Assert.False(rows.ContainsKey("NOCAP"));
        // DRVE has a readable A line but its B tertip is unreadable: the group is withheld.
        Assert.False(rows.ContainsKey("DRVE"));
        Assert.DoesNotContain(rows.Values, row => row.SharesOutstanding == 0m);

        // The skip rides the run ledger's stats, so the gap is visible operationally.
        var run = await db.IngestRuns.SingleAsync(r => r.JobCode == SharesJob.Code);
        Assert.Equal("succeeded", run.Status);
        using var stats = JsonDocument.Parse(run.StatsJson!);
        Assert.Equal(2, stats.RootElement.GetProperty("skipped").GetInt32());
    }

    /// <summary>
    /// TC-MDF-092 — the re-run is input-aware: an identical re-run writes nothing and reports the
    /// honest <c>unchanged: 7</c> (the served <c>asOf</c> does not move); a re-run over a changed
    /// count repairs exactly that row in place and counts it as written.
    /// </summary>
    [Fact]
    public async Task TC_MDF_092_rerun_is_input_aware_unchanged_then_repairs_in_place()
    {
        await ResetAsync();
        await BindInstrumentsAsync();
        var body = CannedSourceCatalog.Get(L2, CannedSourceCatalog.MemberSecuritiesOk).Body;
        StubBody(body);

        var host = BuildIngestion();
        await RunAsync(host);

        // Identical re-run: no write, no as-of movement.
        var rerun = await RunAsync(host);
        Assert.Equal(IngestResult.Succeeded, rerun.Status);
        Assert.Equal(0, rerun.Written);
        Assert.Equal(7, rerun.Unchanged);
        Assert.Equal(2, rerun.Skipped);

        await using (var db = _postgres.CreateContext())
        {
            var asels = (await RowsBySymbolAsync(db))["ASELS"];
            Assert.Equal(RecordedAt, asels.RecordedAt);
        }

        // Changed input: ASELS's B tertip grows by one share (4.56B → 4,560,000,001).
        Assert.Contains("2138181818.184", body, StringComparison.Ordinal);
        StubBody(body.Replace("2138181818.184", "2138181819.184", StringComparison.Ordinal));
        var later = new DateTimeOffset(T.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        host.Clock.SetUtcNow(later);

        var repair = await RunAsync(host);
        Assert.Equal(IngestResult.Succeeded, repair.Status);
        Assert.Equal(1, repair.Written);
        Assert.Equal(6, repair.Unchanged);

        await using (var db = _postgres.CreateContext())
        {
            var rows = await RowsBySymbolAsync(db);
            Assert.Equal(7, rows.Count);
            Assert.Equal(4_560_000_001m, rows["ASELS"].SharesOutstanding);
            Assert.Equal(later, rows["ASELS"].RecordedAt);
            // The untouched rows keep their original instant.
            Assert.Equal(RecordedAt, rows["GARAN"].RecordedAt);
        }
    }

    /// <summary>
    /// TC-MDF-092 — one paced <c>/memberSecurities</c> call per run (the 046 members-snapshot
    /// precedent): no per-instrument loop, no second request, no request on a re-run beyond the
    /// one snapshot it needs.
    /// </summary>
    [Fact]
    public async Task TC_MDF_092_one_paced_call_per_run()
    {
        await ResetAsync();
        await BindInstrumentsAsync();
        StubBody(CannedSourceCatalog.Get(L2, CannedSourceCatalog.MemberSecuritiesOk).Body);

        var host = BuildIngestion();
        Assert.Equal(0, _wireMock.RequestsFor(MemberSecuritiesPath));

        await RunAsync(host);
        Assert.Equal(1, _wireMock.RequestsFor(MemberSecuritiesPath));

        await RunAsync(host);
        Assert.Equal(2, _wireMock.RequestsFor(MemberSecuritiesPath));

        // The code default is the shipped route; the L2 harness binds the same path explicitly.
        Assert.Equal(MemberSecuritiesPath, new KapSourceOptions().MemberSecuritiesPath);
        Assert.True(new KapSourceOptions().HasMemberSecuritiesPath);
    }

    /// <summary>
    /// TC-MDF-092 — the failure posture is honest: a transport failure is a <b>failed run</b>
    /// (rethrow through the runner) with no rows and no quarantine; a body that is not the
    /// memberSecurities shape quarantines <c>UNPARSEABLE_PAYLOAD</c> and writes nothing.
    /// </summary>
    [Fact]
    public async Task TC_MDF_092_fetch_failure_and_malformed_body_are_honest()
    {
        await ResetAsync();
        await BindInstrumentsAsync();

        // (a) HTTP 500 → failed ledger row, no share_counts rows, no quarantine.
        _wireMock.Server.ResetMappings();
        _wireMock.StubJson(MemberSecuritiesPath, "{\"error\":\"source unavailable\"}", statusCode: 500);

        await using (var db = _postgres.CreateContext())
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
        }

        var host = BuildIngestion();
        await Assert.ThrowsAsync<SourceFetchException>(() => RunAsync(host));

        await using (var db = _postgres.CreateContext())
        {
            Assert.Equal(0, await db.ShareCounts.CountAsync());
            Assert.Equal(0, await db.QuarantinedFacts.CountAsync());
            var run = await db.IngestRuns.SingleAsync(r => r.JobCode == SharesJob.Code);
            Assert.Equal("failed", run.Status);
        }

        // (b) A body that is not the memberSecurities shape → UNPARSEABLE_PAYLOAD, no rows.
        _wireMock.Server.ResetMappings();
        _wireMock.StubJson(MemberSecuritiesPath, "{}");

        var malformed = await RunAsync(host);
        Assert.Equal(IngestResult.Failed, malformed.Status);
        Assert.Equal(1, malformed.Quarantined);

        await using (var db = _postgres.CreateContext())
        {
            Assert.Equal(0, await db.ShareCounts.CountAsync());
            var quarantined = await db.QuarantinedFacts.SingleAsync();
            Assert.Equal("UNPARSEABLE_PAYLOAD", quarantined.ReasonCode);
            Assert.Equal(SharesJob.Code, quarantined.JobCode);
        }
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    private static readonly string[] TestSymbols =
    [
        "ASELS", "THYAO", "TUPRS", "GARAN", "AEFES", "ISDMR", "DRVFM", "OTHER", "GLRMK", "CLSDR",
        // The withheld-group class must be *matched* to be exercised: DRVE's B tertip and
        // NOCAP's sole line carry no currentCapital, so binding them makes the skip count real.
        "DRVE", "NOCAP",
        // Spelling-variant instruments for the tolerant trading-open flag decode.
        "SPELL1", "SPELL2", "SPELL0",
    ];

    /// <summary>
    /// Seeds the instruments the double's lines are matched against. They are not FU instruments
    /// (the double is the real VYK archive), so each case creates them idempotently; DRVFM carries
    /// the isin-fallback line's ISIN without its trading code, OTHER carries ASELS's ISIN as a
    /// decoy (never a candidate), and GLRMK/CLSDR are the honest-absence classes.
    /// </summary>
    private async Task BindInstrumentsAsync()
    {
        await using var db = _postgres.CreateContext();
        var existing = await db.Instruments
            .Where(instrument => TestSymbols.Contains(instrument.Symbol))
            .Select(instrument => instrument.Symbol)
            .ToListAsync();

        foreach (var symbol in TestSymbols.Except(existing, StringComparer.Ordinal))
        {
            db.Instruments.Add(new Instrument
            {
                Symbol = symbol,
                Isin = symbol switch
                {
                    "DRVFM" => "TREDRVF00019",
                    "OTHER" => "TRAASELS91H2",
                    "GLRMK" => "TREGLRM00018",
                    "CLSDR" => "TRECLSD00011",
                    _ => null,
                },
                Name = symbol,
                Status = "active",
                SourceRef = "test:member-securities",
                RecordedAt = RecordedAt,
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task<Dictionary<string, ShareCount>> RowsBySymbolAsync(
        Degerli.Persistence.DegerliDbContext db) =>
        await (from row in db.ShareCounts
               join instrument in db.Instruments on row.InstrumentId equals instrument.Id
               select new { instrument.Symbol, Row = row })
            .ToDictionaryAsync(pair => pair.Symbol, pair => pair.Row);

    private void StubBody(string body)
    {
        _wireMock.Server.ResetMappings();
        _wireMock.StubJson(MemberSecuritiesPath, body);
    }

    private async Task ResetAsync()
    {
        await using (var db = _postgres.CreateContext())
        {
            await FixtureSeeder.ApplyAsync(db, L2);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM share_counts");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM quarantined_facts");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
        }

        _wireMock.Server.ResetMappings();
        _wireMock.Server.ResetLogEntries();
    }

    private TestHost BuildIngestion()
    {
        var clock = new FakeTimeProvider(RecordedAt);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _postgres.ConnectionString,
                ["Ingestion:Kap:BaseUrl"] = _wireMock.BaseUrl,
                ["Ingestion:Kap:MemberSecuritiesPath"] = MemberSecuritiesPath,
                ["Ingestion:Statements:RequestDelayMs"] = "0",
            })
            .Build();

        var logger = new LoggerConfiguration().MinimumLevel.Warning().CreateLogger();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog(logger, dispose: false));
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IIngestionAlerter, NoOpAlerter>();
        services.AddMarketDataIngestion(configuration);

        return new TestHost(services.BuildServiceProvider(), clock);
    }

    private static async Task<IngestResult> RunAsync(TestHost host)
    {
        using var scope = host.Provider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IIngestionJobRunner>();
        return await runner.RunAsync(SharesJob.Code, new IngestionRequest(T));
    }

    private sealed class TestHost : IAsyncDisposable
    {
        public TestHost(ServiceProvider provider, FakeTimeProvider clock)
        {
            Provider = provider;
            Clock = clock;
        }

        public ServiceProvider Provider { get; }

        public FakeTimeProvider Clock { get; }

        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    private sealed class NoOpAlerter : IIngestionAlerter
    {
        public Task RaiseAsync(IngestionAlert alert, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
