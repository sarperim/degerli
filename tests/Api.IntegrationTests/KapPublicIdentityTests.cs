using System.Reflection;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Kap;
using Degerli.Ingestion.KapPublic;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Serilog;
using Serilog.Events;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TC-MDF-105 acceptance — the public-site <b>company identity</b> mapping is <b>stored, never
/// assumed</b> (test plan §21; 01 v1.5.54 §10.9.19, TKT-foundation-080; FR-MDF-002/008/012).
/// <para>
/// <b>Doubles only.</b> The identity answer is 079's committed capture
/// (<see cref="RealSourceCatalog.KapPublicIdentity"/>, captured 2026-10-25) served by the WireMock
/// double; the VYK numeric companyId used in the coexistence case is read out of the committed
/// <c>/memberSecurities</c> capture rather than typed in. <b>No test calls kap.org.tr</b> — this
/// ticket issued zero live requests (politeness directive, 2026-10-25).
/// </para>
/// <para>
/// The three identifier spaces (ours, the public site's oid, the VYK numeric companyId) are pinned
/// apart in code (distinct types with no conversion), in storage (distinct columns with
/// shape-checking constraints) and at the mapping seam (only the stored oid bridges the spaces).
/// </para>
/// </summary>
public sealed class KapPublicIdentityTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
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

    public KapPublicIdentityTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    // =================================================================================
    // (i) resolution binds each bound symbol through the committed capture and persists it
    // =================================================================================

    /// <summary>
    /// TC-MDF-105 (i)/(iii) — the ASELS fixture resolves end-to-end through the committed identity
    /// capture and is <b>persisted</b> with its own provenance and asOf: our instrument id, the
    /// public oid, whence it came (which resolution body) and when. The stored mapping — not an
    /// in-memory assumption — is what a later read uses: the oid read back from the store is the
    /// same oid the byCriteria query is then issued with, and the committed disclosure list answers.
    /// </summary>
    [Fact]
    public async Task TC_MDF_105_a_resolution_persists_the_mapping_with_its_provenance_and_as_of()
    {
        await ResetAsync();
        await BindInstrumentsAsync();
        var bindings = await BindingsAsync("ASELS");
        StubIdentity(RealSourceCatalog.Get(RealSourceCatalog.KapPublicIdentity).Body);
        _wireMock.StubJson(
            ByCriteriaPath,
            RealSourceCatalog.Get(RealSourceCatalog.KapPublicByCriteria).Body,
            method: "POST");

        await using var provider = BuildIngestion();

        var result = await ResolveAsync(provider, bindings);

        Assert.False(result.IsSourceDown, result.Detail);
        Assert.Equal(KapPublicIdentityResolutionOutcome.Ok, result.Outcome);
        Assert.Equal(1, result.Bound);
        Assert.Equal(1, result.Resolved);
        Assert.Equal(1, result.Inserted);
        Assert.Equal(0, result.Updated);
        Assert.Equal(0, result.Unchanged);
        Assert.Empty(result.Unresolved);
        Assert.Empty(result.UnresolvedSymbols);

        await using var db = _postgres.CreateContext();
        var row = Assert.Single(await db.KapPublicCompanyMaps.ToListAsync());
        Assert.Equal(bindings[0].InstrumentId, row.InstrumentId);
        Assert.Equal(AselsPublicOid, row.KapPublicOid);
        Assert.Contains(IdentityPath, row.KapPublicOidSource, StringComparison.Ordinal);
        Assert.Contains("keyword=ASELS", row.KapPublicOidSource, StringComparison.Ordinal);
        Assert.Equal(T, row.KapPublicOidAsOf);
        Assert.Equal(RecordedAt, row.RecordedAt);
        // The VYK space stayed empty: no run held VYK evidence, so no companyId is fabricated.
        Assert.Null(row.VykCompanyId);
        Assert.Null(row.VykCompanyIdSource);
        Assert.Null(row.VykCompanyIdAsOf);

        // The mapping is read back through the store (not assumed from a symbol's spelling) — and the
        // stored oid is the one the disclosure query is then issued with.
        string storedOid;
        using (var scope = provider.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<KapPublicCompanyMapStore>();
            var oid = await store.GetPublicOidAsync(bindings[0].InstrumentId);
            Assert.Equal(AselsPublicOid, oid!.Value.Value);
            Assert.Equal(
                oid.Value,
                Assert.Single((await store.GetPublicOidsAsync([bindings[0].InstrumentId])).Values));
            storedOid = oid.Value.Value;
        }

        // End-to-end: the stored public oid drives the byCriteria list — the committed ASELS FR
        // capture (ten rows) comes back classified as a well-formed list.
        using (var scope = provider.CreateScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<KapPublicSourceReader>();
            var listed = await reader.ReadDisclosureListAsync(
                new KapPublicListQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 10, 25), "FR", [storedOid]));

            Assert.True(listed.IsOk, listed.ReasonCode);
            Assert.Equal(10, listed.Payload!.Count);
            Assert.Contains(listed.Payload, row => row.DisclosureIndex == 1643141L);
        }

        var queryBody = Assert.Single(
                _wireMock.Server.LogEntries,
                entry => entry.RequestMessage?.Path == ByCriteriaPath)
            .RequestMessage!.Body;
        Assert.Contains($"\"mkkMemberOidList\":[\"{AselsPublicOid}\"]", queryBody, StringComparison.Ordinal);
    }

    // =================================================================================
    // (ii) a bound symbol with no mapping is named + counted and never fetched
    // =================================================================================

    /// <summary>
    /// TC-MDF-105 (ii) — a bound symbol the identity answer cannot map is <b>skipped</b>, <b>named</b>
    /// (its own line, with the 044 vocabulary reason), <b>counted</b> in the result, and <b>never
    /// fetched</b>: the loop continues past it, the resolvable company still resolves and persists,
    /// and no byCriteria request is ever issued for the unmapped one. An ambiguous answer (two
    /// distinct oids for one symbol) is never resolved by picking one — it is named under its own
    /// reason.
    /// </summary>
    [Fact]
    public async Task TC_MDF_105_b_an_unmapped_bound_symbol_is_named_counted_and_never_fetched()
    {
        await ResetAsync();
        await BindInstrumentsAsync();
        var bindings = await BindingsAsync("ASELS", "ALFA");

        // The double answers the *captured* ASELS hit for every keyword: ASELS resolves, ALFA gets a
        // well-formed answer whose only company hit is another company — not a match, never a guess.
        StubIdentity(RealSourceCatalog.Get(RealSourceCatalog.KapPublicIdentity).Body);
        var logs = new InMemorySerilogSink();
        await using var provider = BuildIngestion(logs);

        var result = await ResolveAsync(provider, bindings);

        Assert.Equal(KapPublicIdentityResolutionOutcome.Ok, result.Outcome);
        Assert.Equal(2, result.Bound);
        Assert.Equal(1, result.Resolved);
        Assert.Equal(1, result.UnresolvedCount);
        Assert.Equal(1, result.Inserted);

        var unresolved = Assert.Single(result.Unresolved);
        Assert.Equal("ALFA", unresolved.Symbol);
        Assert.Equal(KapPublicReasonCodes.CompanyIdentityUnresolved, unresolved.ReasonCode);
        // The 044 vocabulary word is the same one the VYK identity doctrine records.
        Assert.Equal(VykCompanyDirectory.CompanyIdentityUnresolved, KapPublicReasonCodes.CompanyIdentityUnresolved);

        // Named in the log, counted in the result, and absent from the store (honest absence).
        Assert.Contains(
            logs.Events,
            evt => evt.RenderMessage().Contains("ALFA", StringComparison.Ordinal)
                && evt.RenderMessage().Contains(KapPublicReasonCodes.CompanyIdentityUnresolved, StringComparison.Ordinal));

        await using (var db = _postgres.CreateContext())
        {
            var row = Assert.Single(await db.KapPublicCompanyMaps.ToListAsync());
            Assert.Equal(AselsPublicOid, row.KapPublicOid);
            var alfaId = bindings.Single(binding => binding.Symbol == "ALFA").InstrumentId;
            Assert.NotEqual(alfaId, row.InstrumentId);
        }

        // The unmapped symbol was never fetched: only identity searches ran — no disclosure list at all.
        Assert.Equal(0, _wireMock.RequestsFor(ByCriteriaPath));

        // An ambiguous answer is neither a match nor a guess: two distinct oids for one symbol.
        ResetDoubles();
        StubIdentity(AmbiguousAselsBody());
        await using (var db = _postgres.CreateContext())
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM kap_public_company_map");
        }

        await using var ambiguousProvider = BuildIngestion();
        var ambiguous = await ResolveAsync(ambiguousProvider, await BindingsAsync("ASELS"));

        Assert.Equal(KapPublicIdentityResolutionOutcome.Ok, ambiguous.Outcome);
        Assert.Equal(0, ambiguous.Resolved);
        var ambiguity = Assert.Single(ambiguous.Unresolved);
        Assert.Equal("ASELS", ambiguity.Symbol);
        Assert.Equal(KapPublicReasonCodes.CompanyIdentityAmbiguous, ambiguity.ReasonCode);

        await using (var db = _postgres.CreateContext())
        {
            Assert.Equal(0, await db.KapPublicCompanyMaps.CountAsync());
        }
    }

    // =================================================================================
    // (iii) the space-isolation invariant
    // =================================================================================

    /// <summary>
    /// TC-MDF-105 (iii) — ASELS's public oid and its VYK numeric companyId <b>coexist as recorded
    /// facts about different spaces</b>, never as translations of each other: they live in distinct
    /// columns with their own provenance, the types have no conversion, and the store itself refuses
    /// a VYK id in the oid column (and an oid in the VYK column). The VYK fact is read out of the
    /// committed <c>/memberSecurities</c> capture, not invented.
    /// </summary>
    [Fact]
    public async Task TC_MDF_105_c_the_three_identifier_spaces_coexist_and_are_never_conflated()
    {
        await ResetAsync();
        await BindInstrumentsAsync();
        var vyk = VykAselsFromCommittedCapture();
        Assert.Equal("866", vyk.CompanyId.Value);

        var bindings = await BindingsAsync("ASELS");
        var withVyk = bindings
            .Select(binding => binding with { Vyk = vyk })
            .ToArray();

        StubIdentity(RealSourceCatalog.Get(RealSourceCatalog.KapPublicIdentity).Body);
        await using var provider = BuildIngestion();

        var result = await ResolveAsync(provider, withVyk);
        Assert.Equal(KapPublicIdentityResolutionOutcome.Ok, result.Outcome);
        Assert.Equal(1, result.Inserted);

        await using var db = _postgres.CreateContext();
        var row = Assert.Single(await db.KapPublicCompanyMaps.ToListAsync());

        // Three spaces, three columns, three facts — all recorded for ASELS, none substituted.
        Assert.Equal(withVyk[0].InstrumentId, row.InstrumentId);
        Assert.Equal(AselsPublicOid, row.KapPublicOid);
        Assert.Equal("866", row.VykCompanyId);
        Assert.NotEqual(row.VykCompanyId, row.KapPublicOid);
        Assert.Contains("keyword=ASELS", row.KapPublicOidSource, StringComparison.Ordinal);
        Assert.Contains("memberSecurities", row.VykCompanyIdSource!, StringComparison.Ordinal);
        Assert.Equal(new DateOnly(2026, 10, 22), row.VykCompanyIdAsOf);
        Assert.Equal(T, row.KapPublicOidAsOf);

        // The types cannot stand in for one another: no conversion exists in either direction, and a
        // value of one space is not a parseable value of the other.
        static IEnumerable<MethodInfo> Conversions(Type type) =>
            type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.Name is "op_Implicit" or "op_Explicit");

        Assert.Empty(Conversions(typeof(PublicCompanyOid)));
        Assert.Empty(Conversions(typeof(VykCompanyId)));
        Assert.NotEqual(typeof(PublicCompanyOid), typeof(VykCompanyId));
        Assert.False(PublicCompanyOid.TryParse(row.VykCompanyId, out _));
        Assert.False(VykCompanyId.TryParse(row.KapPublicOid, out _));

        // The store itself enforces the invariant: a VYK id cannot be written where a public oid
        // belongs…
        var oidViolation = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync(
                "UPDATE kap_public_company_map SET kap_public_oid = {0}", row.VykCompanyId!));
        Assert.Equal(PostgresErrorCodes.CheckViolation, oidViolation.SqlState);
        Assert.Equal("ck_kap_public_company_map_oid_shape", oidViolation.ConstraintName);

        // …and an oid cannot be written where the VYK id belongs.
        var vykViolation = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync(
                "UPDATE kap_public_company_map SET vyk_company_id = {0}", row.KapPublicOid));
        Assert.Equal(PostgresErrorCodes.CheckViolation, vykViolation.SqlState);
        Assert.Equal("ck_kap_public_company_map_vyk_shape", vykViolation.ConstraintName);

        // Both refusals left the recorded facts untouched.
        db.ChangeTracker.Clear();
        var after = Assert.Single(await db.KapPublicCompanyMaps.ToListAsync());
        Assert.Equal(AselsPublicOid, after.KapPublicOid);
        Assert.Equal("866", after.VykCompanyId);
    }

    // =================================================================================
    // (iv) idempotent re-resolution
    // =================================================================================

    /// <summary>
    /// TC-MDF-105 (iv) — an identical re-resolution leaves the mapping rows <c>unchanged</c>: no new
    /// row, no write, no timestamp move. A later re-resolution whose evidence date advanced repairs
    /// the same row in place (still exactly one row per instrument) instead of duplicating it.
    /// </summary>
    [Fact]
    public async Task TC_MDF_105_d_rerun_is_idempotent_unchanged_and_never_duplicates_a_row()
    {
        await ResetAsync();
        await BindInstrumentsAsync();
        var bindings = await BindingsAsync("ASELS");
        StubIdentity(RealSourceCatalog.Get(RealSourceCatalog.KapPublicIdentity).Body);

        var clock = new FakeTimeProvider(RecordedAt);
        await using var provider = BuildIngestion(clock: clock);

        var first = await ResolveAsync(provider, bindings);
        Assert.Equal(1, first.Inserted);

        DateTimeOffset recordedAt;
        await using (var db = _postgres.CreateContext())
        {
            recordedAt = (await db.KapPublicCompanyMaps.SingleAsync()).RecordedAt;
        }

        var second = await ResolveAsync(provider, bindings);
        Assert.Equal(KapPublicIdentityResolutionOutcome.Ok, second.Outcome);
        Assert.Equal(0, second.Inserted);
        Assert.Equal(0, second.Updated);
        Assert.Equal(1, second.Unchanged);

        await using (var db = _postgres.CreateContext())
        {
            var row = Assert.Single(await db.KapPublicCompanyMaps.ToListAsync());
            Assert.Equal(AselsPublicOid, row.KapPublicOid);
            Assert.Equal(recordedAt, row.RecordedAt);
            Assert.Equal(T, row.KapPublicOidAsOf);
        }

        // The next day's resolution is the same mapping on new evidence: repaired in place, one row.
        clock.Advance(TimeSpan.FromDays(1));
        var third = await ResolveAsync(provider, bindings);
        Assert.Equal(0, third.Inserted);
        Assert.Equal(1, third.Updated);
        Assert.Equal(0, third.Unchanged);

        await using (var db = _postgres.CreateContext())
        {
            var row = Assert.Single(await db.KapPublicCompanyMaps.ToListAsync());
            Assert.Equal(AselsPublicOid, row.KapPublicOid);
            Assert.Equal(T.AddDays(1), row.KapPublicOidAsOf);
        }
    }

    // =================================================================================
    // (v) an identity-source failure degrades honestly
    // =================================================================================

    /// <summary>
    /// TC-MDF-105 (v) — a transport failure of the identity source ends the pass as <b>source-down</b>
    /// with its named reason: the failure is reported (naming the symbol it stopped at) and
    /// <b>nothing is written</b> — no fabricated mapping, no partial guess. A body that is not the
    /// identity shape degrades the same way, under its own reason.
    /// </summary>
    [Fact]
    public async Task TC_MDF_105_e_an_identity_source_failure_reports_the_failure_and_fabricates_nothing()
    {
        await ResetAsync();
        await BindInstrumentsAsync();
        var bindings = await BindingsAsync("ASELS", "ALFA");

        _wireMock.Server.ResetMappings();
        _wireMock.StubJson(IdentityPath, "{\"error\":\"source unavailable\"}", statusCode: 500, method: "POST");
        var logs = new InMemorySerilogSink();
        await using var provider = BuildIngestion(logs, LogEventLevel.Information);

        var result = await ResolveAsync(provider, bindings);

        Assert.True(result.IsSourceDown);
        Assert.Equal(KapPublicReasonCodes.SourceFetchFailed, result.ReasonCode);
        Assert.Contains("ASELS", result.Detail!, StringComparison.Ordinal);
        Assert.Equal(0, result.Resolved);
        Assert.Equal(0, result.Inserted);

        await using (var db = _postgres.CreateContext())
        {
            Assert.Equal(0, await db.KapPublicCompanyMaps.CountAsync());
        }

        // The pass stops at the failure rather than hammering a failing site.
        Assert.Equal(1, _wireMock.RequestsFor(IdentityPath));
        Assert.Equal(0, _wireMock.RequestsFor(ByCriteriaPath));
        Assert.Contains(
            logs.Events,
            evt => evt.RenderMessage().Contains("ASELS", StringComparison.Ordinal)
                && evt.RenderMessage().Contains(KapPublicReasonCodes.SourceFetchFailed, StringComparison.Ordinal));

        // A body that is not the identity-search shape is the same honesty: reported, nothing stored.
        ResetDoubles();
        _wireMock.StubJson(IdentityPath, "{}", method: "POST");

        await using var malformedProvider = BuildIngestion();
        var malformed = await ResolveAsync(malformedProvider, bindings);

        Assert.True(malformed.IsSourceDown);
        Assert.Equal(KapPublicReasonCodes.UnparseablePayload, malformed.ReasonCode);

        await using (var db = _postgres.CreateContext())
        {
            Assert.Equal(0, await db.KapPublicCompanyMaps.CountAsync());
        }
    }

    // =================================================================================
    // Harness
    // =================================================================================

    /// <summary>
    /// The bound universe (XU100) bindings for the named symbols — the shape a lane passes to the
    /// resolver. Each symbol must exist and be a current XU100 constituent, so the case is about
    /// <b>bound</b> instruments, never about an arbitrary symbol.
    /// </summary>
    private async Task<KapPublicInstrumentBinding[]> BindingsAsync(params string[] symbols)
    {
        await using var db = _postgres.CreateContext();
        var xu100 = await db.Indices.Where(index => index.Code == "XU100").Select(index => index.Id).SingleAsync();
        var members = await db.IndexConstituents
            .Where(constituent => constituent.IndexId == xu100 && constituent.EffectiveTo == null)
            .Select(constituent => constituent.InstrumentId)
            .ToListAsync();
        var rows = await db.Instruments
            .Where(instrument => symbols.Contains(instrument.Symbol))
            .ToListAsync();

        return symbols.Select(symbol =>
        {
            var instrument = Assert.Single(rows, row => row.Symbol == symbol);
            Assert.Contains(instrument.Id, members);
            return new KapPublicInstrumentBinding(instrument.Id, symbol);
        }).ToArray();
    }

    /// <summary>
    /// Binds ASELS into the fixture world: the instrument row and its current XU100 membership. The
    /// fixture universe carries the archetype set; ASELS exists because the committed captures are
    /// the real company's.
    /// </summary>
    private async Task BindInstrumentsAsync()
    {
        await using var db = _postgres.CreateContext();
        var xu100 = await db.Indices.Where(index => index.Code == "XU100").Select(index => index.Id).SingleAsync();
        var asels = await db.Instruments.SingleOrDefaultAsync(instrument => instrument.Symbol == "ASELS");
        if (asels is null)
        {
            asels = new Instrument
            {
                Symbol = "ASELS",
                Isin = "TRAASELS91H2",
                Name = "ASELSAN ELEKTRONİK SANAYİ VE TİCARET A.Ş.",
                Status = "active",
                SourceRef = "test:kap-public-identity",
                RecordedAt = RecordedAt,
            };
            db.Instruments.Add(asels);
            await db.SaveChangesAsync();
        }

        var bound = await db.IndexConstituents.AnyAsync(
            constituent => constituent.IndexId == xu100
                && constituent.InstrumentId == asels.Id
                && constituent.EffectiveTo == null);
        if (!bound)
        {
            db.IndexConstituents.Add(new IndexConstituent
            {
                IndexId = xu100,
                InstrumentId = asels.Id,
                EffectiveFrom = new DateOnly(2025, 1, 2),
                SourceRef = "test:kap-public-identity",
                RecordedAt = RecordedAt,
            });
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// The ASELS VYK numeric companyId, read out of the <b>committed</b> <c>/memberSecurities</c>
    /// capture (the member id of the ASELSAN member) — the VYK space's fact, with its own
    /// provenance. Nothing here derives it from the public oid.
    /// </summary>
    private static KapPublicVykIdentity VykAselsFromCommittedCapture()
    {
        var body = CannedSourceCatalog.Get(L2, CannedSourceCatalog.MemberSecuritiesOk).Body;
        using var document = JsonDocument.Parse(body);
        var member = document.RootElement
            .EnumerateArray()
            .Select(element => element.GetProperty("member"))
            .Single(element => element.GetProperty("sirketUnvan").GetString()!
                .StartsWith("ASELSAN", StringComparison.Ordinal));

        return new KapPublicVykIdentity(
            VykCompanyId.Parse(member.GetProperty("id").GetString()),
            new IdentityEvidence(
                "kap:memberSecurities member id (ASELS, committed capture 2026-10-22)",
                new DateOnly(2026, 10, 22)));
    }

    /// <summary>
    /// A <b>derived</b> identity answer (not a capture): the captured ASELS hit shape, twice, with
    /// two distinct oids for the one symbol — the ambiguity the resolver must refuse to guess at.
    /// </summary>
    private static string AmbiguousAselsBody()
    {
        static string Hit(string oid) =>
            $$"""
            {"searchValue":"ASELSAN ELEKTRONİK SANAYİ VE TİCARET A.Ş.","searchType":"C",
             "actionKey":"companyFundAction","memberOrFundOid":"{{oid}}","subjectOid":null,
             "marketOid":null,"discType":null,"year":-1,"period":-1,"cmpOrFundCode":"asels",
             "indexList":null}
            """;

        return $$"""
        [{"category":"combined","results":[]},{"category":"subjects","results":[]},
         {"category":"companyOrFunds","results":[{{Hit(AselsPublicOid)}},{{Hit("4028e4a1413b7ef401413bc2251e0099")}}]}]
        """;
    }

    private void StubIdentity(string body) => _wireMock.StubJson(IdentityPath, body, method: "POST");

    private void ResetDoubles()
    {
        _wireMock.Server.ResetMappings();
        _wireMock.Server.ResetLogEntries();
    }

    private async Task ResetAsync()
    {
        await using (var db = _postgres.CreateContext())
        {
            await FixtureSeeder.ApplyAsync(db, L2);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM kap_public_company_map");
        }

        _wireMock.Server.ResetMappings();
        _wireMock.Server.ResetLogEntries();
    }

    private static async Task<KapPublicIdentityResolutionResult> ResolveAsync(
        ServiceProvider provider,
        IReadOnlyList<KapPublicInstrumentBinding> bindings)
    {
        await using var scope = provider.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<KapPublicCompanyIdentityResolver>();
        return await resolver.ResolveAsync(bindings);
    }

    /// <summary>
    /// The production composition (<see cref="IngestionServiceCollectionExtensions.AddMarketDataIngestion"/>)
    /// over the Testcontainers database and the public-site double — the lane's own options, pacing
    /// bound to 0 (the 079 harness convention: pacing is not the subject here, and CI never sleeps).
    /// </summary>
    private ServiceProvider BuildIngestion(InMemorySerilogSink? logs = null, LogEventLevel minimumLevel = LogEventLevel.Warning)
    {
        return BuildIngestion(new FakeTimeProvider(RecordedAt), logs, minimumLevel);
    }

    private ServiceProvider BuildIngestion(
        FakeTimeProvider clock,
        InMemorySerilogSink? logs = null,
        LogEventLevel minimumLevel = LogEventLevel.Warning)
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
                .MinimumLevel.Is(minimumLevel)
                .WriteTo.Sink(logs)
                .CreateLogger();
            services.AddLogging(builder => builder.AddSerilog(logger, dispose: false));
        }

        services.AddSingleton<TimeProvider>(clock);
        services.AddMarketDataIngestion(configuration);

        return services.BuildServiceProvider();
    }
}
