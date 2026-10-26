using Degerli.Api.IntegrationTests.Harness;
using System.Text.Json;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Kap;
using Degerli.Ingestion.KapPublic;
using Degerli.Ingestion.Scheduling;
using Degerli.Ingestion.Statements;
using Degerli.Ingestion.Universe;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Serilog;
using Serilog.Events;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TC-MDF-116 acceptance — the public-site company-identity map's <b>production invocation
/// surface</b> (test plan §27; 01 v1.5.61 §10.9.19, TKT-foundation-087; FR-MDF-002/008/012/016,
/// UC-MDF-002/004).
/// <para>
/// The recorded 080/082 blocker: <c>KapPublicCompanyIdentityResolver.ResolveAsync</c> had <b>no
/// production caller</b>, so <c>kap_public_company_map</c> never populated and a live
/// <c>Source = kap-public</c> statements walk isolated every company as
/// <c>COMPANY_IDENTITY_UNRESOLVED</c>. This suite pins the missing middle — one registered
/// <see cref="IIngestJob"/> (<c>kap-public-identity</c>) that binds the statements lane's configured
/// universe, runs the merged resolver unchanged through <b>079's</b> paced single-flight client and
/// maps its honest statuses onto the run ledger.
/// </para>
/// <para>
/// <b>Doubles only.</b> The identity answer is the committed
/// <see cref="RealSourceCatalog.KapPublicIdentity"/> capture (2026-10-25) served by the WireMock
/// double; the universe is the fixture store's own index membership. <b>No test calls
/// kap.org.tr</b> — the lane issued zero live requests (politeness directive), and every request
/// this job makes is 079's client's (the job adds no <c>HttpClient</c>, no pacer, no parallelism).
/// </para>
/// </summary>
public sealed class KapPublicIdentityJobTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    private const string IdentityPath = "/tr/api/search/combined";
    private const string ByCriteriaPath = "/tr/api/disclosure/members/byCriteria";

    /// <summary>The ASELS public-site company oid (the public site's own space — never VYK's 866).</summary>
    private const string AselsPublicOid = "4028e4a1413b7ef401413bc2251e0047";

    private static readonly DateTimeOffset RecordedAt = new(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public KapPublicIdentityJobTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    // =================================================================================
    // (a) the invocation surface: registered, admin-triggerable, never a chain step
    // =================================================================================

    /// <summary>
    /// TC-MDF-116 (a) — the job is a <b>registered</b> production job: the composition root
    /// registers it, so <see cref="IIngestionJobRunner.JobCodes"/> lists it — the same registry the
    /// admin summary enumerates (UXR-MDF-003: every registered job, even one that has never run) and
    /// the admin trigger <c>POST /api/v1/admin/ingest/{job}/run</c> resolves a run request through
    /// (UC-MDF-002). And it is <b>not</b> a chain step: both shipped chains are byte-unchanged, so the
    /// job cannot silently become a scheduled EOD/morning cost (the shares-lane scheduling
    /// precedent, 01 §10.9.11 — a rare-changing fact, admin-triggerable only).
    /// </summary>
    [Fact]
    public async Task TC_MDF_116_a_the_job_is_a_registered_admin_triggerable_surface_and_never_a_chain_step()
    {
        await ResetAsync();

        await using var provider = BuildIngestion("XU030");

        var runner = provider.GetRequiredService<IIngestionJobRunner>();

        // Registered exactly once, under its stable code — the key the ledger, the admin
        // summary's job list and the admin trigger all speak.
        Assert.Equal("kap-public-identity", KapPublicIdentitySyncJob.Code);
        Assert.Single(runner.JobCodes, code => code == KapPublicIdentitySyncJob.Code);
        await using (var scope = provider.CreateAsyncScope())
        {
            Assert.Equal(
                KapPublicIdentitySyncJob.Code,
                Assert.Single(
                    scope.ServiceProvider.GetServices<IIngestJob>(),
                    job => job.JobCode == KapPublicIdentitySyncJob.Code).JobCode);
        }

        // Not a scheduled step of either shipped chain (asserted, not assumed): the chains are
        // byte-unchanged, and a company's public oid changes rarely, so a cadence is a recorded
        // later decision.
        Assert.DoesNotContain(KapPublicIdentitySyncJob.Code, EodIngestChain.JobCodes);
        Assert.DoesNotContain(KapPublicIdentitySyncJob.Code, MorningIngestChain.JobCodes);
        // The precedent the posture is copied from: `shares` is admin-triggerable and unchained too.
        Assert.DoesNotContain("shares", EodIngestChain.JobCodes);
    }

    // =================================================================================
    // (b) one paced pass populates the map through the production runner
    // =================================================================================

    /// <summary>
    /// TC-MDF-116 (b) — the blocker, closed: one runner pass over the bound universe resolves the
    /// company through the committed identity capture and <b>stores</b> its mapping with provenance
    /// and as-of, so the statements lane's identity precondition is satisfied by production code. The
    /// pass is the merged resolver's own (079's client: one paced, single-flight request per bound
    /// symbol; the job adds no request path of its own — zero byCriteria requests are issued), and the
    /// run ledger records the row under the job's code with the run's counts (FR-MDF-016,
    /// UC-MDF-004) and the admin trigger's trigger value (UC-MDF-002 — the admin route passes no run
    /// context).
    /// </summary>
    [Fact]
    public async Task TC_MDF_116_b_one_runner_pass_populates_the_bound_universes_map()
    {
        await ResetAsync(("XU030", ["ASELS"]));
        StubIdentity(RealSourceCatalog.Get(RealSourceCatalog.KapPublicIdentity).Body);

        await using var provider = BuildIngestion("XU030");
        var result = await RunAsync(provider);

        Assert.Equal(IngestResult.Succeeded, result.Status);
        Assert.Equal(1, result.Written);      // inserted
        Assert.Equal(0, result.Rewritten);    // updated
        Assert.Equal(0, result.Unchanged);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(0, result.Quarantined);

        // The mapping is stored — the fact the statements lane reads, never re-derives.
        await using (var db = _postgres.CreateContext())
        {
            var row = Assert.Single(await db.KapPublicCompanyMaps.ToListAsync());
            var asels = await db.Instruments.SingleAsync(instrument => instrument.Symbol == "ASELS");
            Assert.Equal(asels.Id, row.InstrumentId);
            Assert.Equal(AselsPublicOid, row.KapPublicOid);
            Assert.Contains(IdentityPath, row.KapPublicOidSource, StringComparison.Ordinal);
            Assert.Contains("keyword=ASELS", row.KapPublicOidSource, StringComparison.Ordinal);
            Assert.Equal(T, row.KapPublicOidAsOf);
            Assert.Equal(RecordedAt, row.RecordedAt);
            // No VYK evidence was held by this run, so no VYK id is fabricated (space isolation).
            Assert.Null(row.VykCompanyId);

            // The run ledger carries the row (FR-MDF-016): the admin summary and the ledger
            // endpoint read exactly this.
            var ledger = Assert.Single(await db.IngestRuns.ToListAsync());
            Assert.Equal(KapPublicIdentitySyncJob.Code, ledger.JobCode);
            Assert.Equal(IngestResult.Succeeded, ledger.Status);
            Assert.Null(ledger.Error);
            using var stats = JsonDocument.Parse(ledger.StatsJson!);
            Assert.Equal(1, stats.RootElement.GetProperty("written").GetInt32());
            Assert.Equal(0, stats.RootElement.GetProperty("skipped").GetInt32());
            Assert.Equal(IngestionTrigger.Admin, stats.RootElement.GetProperty("trigger").GetString());
        }

        // One identity request per bound symbol — the resolver's, through the lane's client; and
        // no other route touched (this job has no fetch path of its own).
        Assert.Equal(1, _wireMock.RequestsFor(IdentityPath));
        Assert.Equal(0, _wireMock.RequestsFor(ByCriteriaPath));
    }

    // =================================================================================
    // (c) an unresolved symbol is named, counted and the pass continues
    // =================================================================================

    /// <summary>
    /// TC-MDF-116 (c) — a bound symbol the identity answer cannot map is <b>named</b> (its own
    /// per-symbol ledger stat carrying the 044 vocabulary reason), <b>counted</b> (the run's
    /// <c>skipped</c> stat) and the pass continues past it: the resolvable company still resolves and
    /// persists, and the run is honestly <c>partial</c> — never <c>failed</c> (the source answered)
    /// and never a green run that hides a company with no identity (FR-MDF-012, NFR-MDF-001).
    /// </summary>
    [Fact]
    public async Task TC_MDF_116_c_an_unresolved_symbol_is_named_counted_and_the_pass_continues()
    {
        await ResetAsync(("XU030", ["ASELS", "ALFA"]));
        // The double answers the *captured* ASELS hit for every keyword: ASELS resolves, ALFA gets a
        // well-formed answer whose only company hit is another company — not a match, never a guess.
        StubIdentity(RealSourceCatalog.Get(RealSourceCatalog.KapPublicIdentity).Body);
        var logs = new InMemorySerilogSink();

        await using var provider = BuildIngestion("XU030", logs);
        var result = await RunAsync(provider);

        Assert.Equal(IngestResult.Partial, result.Status);
        Assert.Equal(1, result.Written);
        Assert.Equal(1, result.Skipped);

        var skipped = Assert.Single(result.SymbolStats);
        Assert.Equal("ALFA", skipped.Symbol);
        Assert.Equal(KapPublicReasonCodes.CompanyIdentityUnresolved, skipped.Reason);
        Assert.Equal(0, skipped.Written);
        Assert.False(skipped.Failed);

        await using (var db = _postgres.CreateContext())
        {
            var row = Assert.Single(await db.KapPublicCompanyMaps.ToListAsync());
            Assert.Equal(AselsPublicOid, row.KapPublicOid);
            var alfaId = (await db.Instruments.SingleAsync(instrument => instrument.Symbol == "ALFA")).Id;
            Assert.NotEqual(alfaId, row.InstrumentId);

            var ledger = Assert.Single(await db.IngestRuns.ToListAsync());
            Assert.Equal(IngestResult.Partial, ledger.Status);
            using var stats = JsonDocument.Parse(ledger.StatsJson!);
            Assert.Equal(1, stats.RootElement.GetProperty("skipped").GetInt32());
            Assert.Equal(1, stats.RootElement.GetProperty("written").GetInt32());
            // The unresolved company is *named* in the ledger, not just counted.
            var stat = Assert.Single(stats.RootElement.GetProperty("symbols").EnumerateArray());
            Assert.Equal("ALFA", stat.GetProperty("symbol").GetString());
            Assert.Equal(KapPublicReasonCodes.CompanyIdentityUnresolved, stat.GetProperty("reason").GetString());
        }

        // Named in the log as well as the ledger — and never fetched beyond its identity search.
        Assert.Contains(
            logs.Events,
            evt => evt.RenderMessage().Contains("ALFA", StringComparison.Ordinal)
                && evt.RenderMessage().Contains(KapPublicReasonCodes.CompanyIdentityUnresolved, StringComparison.Ordinal));
        Assert.Equal(2, _wireMock.RequestsFor(IdentityPath));
        Assert.Equal(0, _wireMock.RequestsFor(ByCriteriaPath));

        // The 044 vocabulary word is one spelling across the three identity doctrines.
        Assert.Equal(VykCompanyDirectory.CompanyIdentityUnresolved, KapPublicReasonCodes.CompanyIdentityUnresolved);
        Assert.Equal(StatementsJob.CompanyIdentityUnresolved, KapPublicReasonCodes.CompanyIdentityUnresolved);
    }

    // =================================================================================
    // (d) a source-down pass is failed, names its reason and stores nothing
    // =================================================================================

    /// <summary>
    /// TC-MDF-116 (d) — the identity source failing is an honest <c>failed</c> run whose ledger row
    /// names the reason (the resolver's own reason plus the symbol it stopped at) and stores
    /// <b>nothing</b>: no partial mapping, no fabricated oid, no empty success. The pass stops at the
    /// failure instead of hammering a failing site (one request only).
    /// </summary>
    [Fact]
    public async Task TC_MDF_116_d_a_source_down_pass_is_failed_names_its_reason_and_stores_nothing()
    {
        await ResetAsync(("XU030", ["ASELS", "ALFA"]));
        _wireMock.StubJson(IdentityPath, "{\"error\":\"source unavailable\"}", statusCode: 500, method: "POST");

        await using var provider = BuildIngestion("XU030");
        var result = await RunAsync(provider);

        Assert.Equal(IngestResult.Failed, result.Status);
        Assert.Equal(0, result.Written);
        Assert.Equal(0, result.Unchanged);
        Assert.NotNull(result.FailureReason);
        Assert.Contains(KapPublicReasonCodes.SourceFetchFailed, result.FailureReason!, StringComparison.Ordinal);
        // The reason names the symbol the pass stopped at — the first of the bound universe the
        // resolver walks (the universe store returns constituents symbol-ascending), so the
        // operator can tell which company it died on.
        string stoppedAt;
        await using (var scope = provider.CreateAsyncScope())
        {
            var constituents = await scope.ServiceProvider
                .GetRequiredService<UniverseSyncStore>()
                .GetCurrentConstituentSymbolsAsync("XU030");
            stoppedAt = Assert.Single(constituents, symbol => result.FailureReason!.Contains(symbol, StringComparison.Ordinal));
            Assert.Equal(constituents[0], stoppedAt);
        }
        Assert.Contains(stoppedAt, result.FailureReason!, StringComparison.Ordinal);

        await using (var db = _postgres.CreateContext())
        {
            Assert.Equal(0, await db.KapPublicCompanyMaps.CountAsync());

            var ledger = Assert.Single(await db.IngestRuns.ToListAsync());
            Assert.Equal(KapPublicIdentitySyncJob.Code, ledger.JobCode);
            Assert.Equal(IngestResult.Failed, ledger.Status);
            Assert.NotNull(ledger.Error);
            Assert.Contains(KapPublicReasonCodes.SourceFetchFailed, ledger.Error!, StringComparison.Ordinal);
            Assert.Contains(stoppedAt, ledger.Error!, StringComparison.Ordinal);
        }

        Assert.Equal(1, _wireMock.RequestsFor(IdentityPath));
    }

    // =================================================================================
    // (e) an empty bound is the honest named empty
    // =================================================================================

    /// <summary>
    /// TC-MDF-116 (e) — an empty bound (the configured index holds no current constituent: a
    /// cold-start/misconfiguration gap) is <b>named</b> — the run logs it under its own reason
    /// (<c>EMPTY_BOUND_UNIVERSE</c>, with the index code) — and issues <b>zero</b> requests and stores
    /// nothing. It is not an error status (nothing failed; the stream that owns the cold start is the
    /// EOD/morning chain, and the statements lane names the same gap on its own walk) and it is never
    /// a fabricated resolution zero: no company was resolved, and the run says exactly that.
    /// </summary>
    [Fact]
    public async Task TC_MDF_116_e_an_empty_bound_is_the_honest_named_empty_with_zero_requests()
    {
        await ResetAsync(("XU060", []));
        StubIdentity(RealSourceCatalog.Get(RealSourceCatalog.KapPublicIdentity).Body);
        var logs = new InMemorySerilogSink();

        await using var provider = BuildIngestion("XU060", logs);
        var result = await RunAsync(provider);

        Assert.Equal(IngestResult.Succeeded, result.Status);
        Assert.Equal(0, result.Written);
        Assert.Equal(0, result.Unchanged);
        Assert.Equal(0, result.Skipped);
        Assert.Empty(result.SymbolStats);

        Assert.Equal(0, _wireMock.RequestsFor(IdentityPath));
        Assert.Equal(0, _wireMock.RequestsFor(ByCriteriaPath));

        Assert.Contains(
            logs.Events,
            evt => evt.RenderMessage().Contains("XU060", StringComparison.Ordinal)
                && evt.RenderMessage().Contains(KapPublicIdentitySyncJob.EmptyBoundUniverse, StringComparison.Ordinal));

        await using var db = _postgres.CreateContext();
        Assert.Equal(0, await db.KapPublicCompanyMaps.CountAsync());
    }

    // =================================================================================
    // (f) idempotent re-run, and the bound is the configured index (never a hardcoded list)
    // =================================================================================

    /// <summary>
    /// TC-MDF-116 (f) — a re-run over the same bound is <b>idempotent</b>: the mapping is
    /// <c>unchanged</c> (one row per instrument, <c>recorded_at</c> unmoved — 080's contract, not
    /// re-implemented here), so an operator can re-trigger freely. And the bound is the
    /// <b>statements lane's configured index</b>, read from the universe store: switching
    /// <c>Ingestion:Statements:UniverseIndex</c> to a wider index changes what the pass binds with no
    /// code change and no hardcoded symbol list.
    /// </summary>
    [Fact]
    public async Task TC_MDF_116_f_a_rerun_is_idempotent_and_the_bound_follows_the_configured_index()
    {
        await ResetAsync(("XU030", ["ASELS"]), ("XU050", ["ASELS", "ALFA"]));
        StubIdentity(RealSourceCatalog.Get(RealSourceCatalog.KapPublicIdentity).Body);

        DateTimeOffset recordedAt;
        await using (var provider = BuildIngestion("XU030"))
        {
            var first = await RunAsync(provider);
            Assert.Equal(IngestResult.Succeeded, first.Status);
            Assert.Equal(1, first.Written);

            var second = await RunAsync(provider);
            Assert.Equal(IngestResult.Succeeded, second.Status);
            Assert.Equal(0, second.Written);
            Assert.Equal(0, second.Rewritten);
            Assert.Equal(1, second.Unchanged);

            await using var db = _postgres.CreateContext();
            var row = Assert.Single(await db.KapPublicCompanyMaps.ToListAsync());
            Assert.Equal(AselsPublicOid, row.KapPublicOid);
            recordedAt = row.RecordedAt;
        }

        await using (var db = _postgres.CreateContext())
        {
            var row = Assert.Single(await db.KapPublicCompanyMaps.ToListAsync());
            Assert.Equal(recordedAt, row.RecordedAt);
            // Exactly two distinct requests per pass, three in total — nothing duplicated.
            Assert.Equal(2, _wireMock.RequestsFor(IdentityPath));
        }

        // The wider configured index binds one more company — configuration, never a code fork.
        ResetDoubles();
        StubIdentity(RealSourceCatalog.Get(RealSourceCatalog.KapPublicIdentity).Body);
        await using (var wider = BuildIngestion("XU050"))
        {
            var result = await RunAsync(wider);

            Assert.Equal(IngestResult.Partial, result.Status);   // ALFA still unresolved
            Assert.Equal(1, result.Skipped);
            Assert.Equal("ALFA", Assert.Single(result.SymbolStats).Symbol);
            Assert.Equal(2, _wireMock.RequestsFor(IdentityPath));
        }
    }

    // =================================================================================
    // Harness
    // =================================================================================

    /// <summary>The production job runner — the exact path the admin trigger (<c>POST
    /// /api/v1/admin/ingest/{job}/run</c>) calls, with the trigger value an operator's run carries
    /// (no run context).</summary>
    private static Task<IngestResult> RunAsync(ServiceProvider provider) =>
        provider.GetRequiredService<IIngestionJobRunner>()
            .RunAsync(KapPublicIdentitySyncJob.Code, new IngestionRequest(T));

    private void StubIdentity(string body) => _wireMock.StubJson(IdentityPath, body, method: "POST");

    private void ResetDoubles()
    {
        _wireMock.Server.ResetMappings();
        _wireMock.Server.ResetLogEntries();
    }

    /// <summary>
    /// The fixture world plus the test-local bound(s): each named index with its current
    /// (<c>effective_to</c> null) membership, the instruments created when the fixture universe does
    /// not carry them. No membership is hardcoded in the job or in its assertions — the journal of
    /// requests is what proves which symbols the pass bound.
    /// </summary>
    private async Task ResetAsync(params (string Index, string[] Symbols)[] indices)
    {
        await using (var db = _postgres.CreateContext())
        {
            await FixtureSeeder.ApplyAsync(db, L2);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM kap_public_company_map");
            // The run ledger is the assertion surface for the counts/status of *this* pass: clear it
            // so each case reads exactly the row its own run wrote.
            await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");

            foreach (var (code, symbols) in indices)
            {
                var index = await db.Indices.SingleOrDefaultAsync(row => row.Code == code);
                if (index is null)
                {
                    index = new MarketIndex { Code = code, NameTr = code, NameEn = code };
                    db.Indices.Add(index);
                    await db.SaveChangesAsync();
                }

                foreach (var symbol in symbols)
                {
                    var instrument = await db.Instruments.SingleOrDefaultAsync(row => row.Symbol == symbol);
                    if (instrument is null)
                    {
                        instrument = new Instrument
                        {
                            Symbol = symbol,
                            Name = symbol,
                            AccountingProfile = "industrial",
                            Status = "active",
                            SourceRef = "test:kap-public-identity-job",
                            RecordedAt = RecordedAt,
                        };
                        db.Instruments.Add(instrument);
                        await db.SaveChangesAsync();
                    }

                    var bound = await db.IndexConstituents.AnyAsync(row =>
                        row.IndexId == index.Id && row.InstrumentId == instrument.Id && row.EffectiveTo == null);
                    if (!bound)
                    {
                        db.IndexConstituents.Add(new IndexConstituent
                        {
                            IndexId = index.Id,
                            InstrumentId = instrument.Id,
                            EffectiveFrom = new DateOnly(2025, 1, 2),
                            SourceRef = "test:kap-public-identity-job",
                            RecordedAt = RecordedAt,
                        });
                        await db.SaveChangesAsync();
                    }
                }
            }
        }

        ResetDoubles();
    }

    /// <summary>
    /// The production composition (<see cref="IngestionServiceCollectionExtensions.AddMarketDataIngestion"/>)
    /// over the Testcontainers database and the public-site double — the lane's own options, pacing
    /// bound to 0 (the 079 harness convention: pacing is not the subject here, and CI never sleeps),
    /// and the statements lane's configured universe as the job's bound.
    /// </summary>
    private ServiceProvider BuildIngestion(string universeIndex, InMemorySerilogSink? logs = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _postgres.ConnectionString,
                ["Ingestion:KapPublic:BaseUrl"] = _wireMock.BaseUrl,
                ["Ingestion:KapPublic:IdentitySearchPath"] = IdentityPath,
                ["Ingestion:KapPublic:RequestDelayMs"] = "0",
                // The L2 harness selects the ratified posture explicitly; the shipped value is
                // pinned by TC-MDF-103(i), not assumed here.
                ["Ingestion:KapPublic:AutomatedAccess"] = nameof(KapPublicAccessPosture.Allowed),
                // The bind this job walks: the statements lane's own key (01 §10.9.19) — never a
                // job-local symbol list and never a new configuration key.
                ["Ingestion:Statements:UniverseIndex"] = universeIndex,
            })
            .Build();

        var services = new ServiceCollection();
        if (logs is null)
        {
            services.AddLogging();
        }
        else
        {
            var logger = new LoggerConfiguration()
                .MinimumLevel.Is(LogEventLevel.Warning)
                .WriteTo.Sink(logs)
                .CreateLogger();
            services.AddLogging(builder => builder.AddSerilog(logger, dispose: false));
        }

        services.AddSingleton<TimeProvider>(new FakeTimeProvider(RecordedAt));
        services.AddMarketDataIngestion(configuration);

        return services.BuildServiceProvider();
    }
}
