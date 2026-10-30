using System.Net;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Disclosures;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.KapPublic;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// <b>TC-MDF-112 (i)–(vi)</b>, plus the two named-refusal paths — the market-wide <b>recent-disclosures</b> feed of the KAP public site
/// (test plan §25; <c>01</c> v1.5.85 §10.9.19, TKT-foundation-084; FR-MDF-005/008/011/012,
/// NFR-MDF-001).
/// <para>
/// <b>Doubles only, zero live requests.</b> The only payload this class feeds the lane is the
/// <b>committed</b> OQ-1 market-wide byCriteria probe body
/// (<see cref="RealSourceCatalog.KapPublicMarketProbe"/> — the 2026-10-25 capture of
/// <c>POST /tr/api/disclosure/members/byCriteria</c> with an empty <c>mkkMemberOidList</c>: HTTP 200,
/// 12 market rows, newest public <c>disclosureIndex</c> 1687047), served by WireMock. Runs go through
/// the production <c>disclosures</c> job runner, the production composition
/// (<c>AddMarketDataIngestion</c>) and 079's production paced, single-flight client — retargeted at
/// WireMock with pacing bound to <c>0</c> (the 079 harness convention: the pace is not the subject
/// here and CI never sleeps). No test in this file reaches kap.org.tr; this ticket issued
/// <b>zero</b> live requests.
/// </para>
/// <para>
/// <b>The gate this file is the record of.</b> The market-wide shape of byCriteria was OQ-1's
/// unverified precondition; the committed probe shows it <b>is</b> served (12 rows over
/// 2026-10-24..2026-10-25), so the feed is implemented and the evidence above is what it is asserted
/// against. Every absolute value below (indices, dates, classes, codes) is the capture's own.
/// </para>
/// </summary>
public sealed class KapPublicDisclosuresFeedTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);

    /// <summary>
    /// The run day. The probe was captured covering <c>2026-10-24..2026-10-25</c>, so a pass run on
    /// <b>2026-10-24</b> with a one-day bootstrap lookback lists exactly <c>2026-10-23..2026-10-24</c>
    /// — one window that contains every captured row, and nothing invented.
    /// </summary>
    private static readonly DateOnly RunDay = new(2026, 10, 24);
    private static readonly DateTimeOffset RecordedAt = new(RunDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private const string ByCriteriaPath = "/tr/api/disclosure/members/byCriteria";

    /// <summary>The VYK disclosures lane's own market cursor data type — never this feed's row.</summary>
    private const string VykMarketCursorDataType = "kap-disclosures-market-cursor";

    /// <summary>The public site's own disclosure-page route, as the committed capture spells it.</summary>
    private const string PublicDisclosureOrigin = "https://www.kap.org.tr/Bildirim/";

    /// <summary>
    /// The <b>synthetic</b> public-site oids the stored identity map holds for the bound companies
    /// (080's map). They are test values only — no captured oid is reused for another issuer
    /// (TKT-foundation-084 review DR-7) — and no assertion depends on the values.
    /// </summary>
    private const string RodrgPublicOid = "0dd0c0de000000000000000000000001";
    private const string GedzaPublicOid = "0dd0c0de000000000000000000000002";
    private const string KtestPublicOid = "0dd0c0de000000000000000000000003";

    // ---------------------------------------------------------------------------------------------
    // The committed market-wide probe body's own rows (2026-10-25 capture, HTTP 200, 12 rows).
    // ---------------------------------------------------------------------------------------------

    /// <summary>The three RODRG rows of the capture (subject/summary/type mapping is asserted on these).</summary>
    private static readonly long[] RodrgIndices = [1687047, 1687046, 1687045];

    /// <summary>The four GEDZA rows of the capture.</summary>
    private static readonly long[] GedzaIndices = [1687041, 1687040, 1687039, 1687038];

    /// <summary>The newest public index the capture carries.</summary>
    private const long NewestPublicIndex = 1687047;

    /// <summary>Every index the capture lists — the pass's <b>seen</b> set after one completed window.</summary>
    private static readonly long[] AllProbeIndices =
    [
        1687047, 1687046, 1687045, 1687044, 1687043, 1687042,
        1687041, 1687040, 1687039, 1687038, 1687037, 1687036,
    ];

    /// <summary>
    /// The rows no bound company owns: the two <c>KTEST</c> test-entity rows (an instrument the
    /// fixture world has no identity for here would still be out of bound if it is not bound), the
    /// <c>MARBL</c> buy-back row and the two code-less BISTECH circuit-breaker notices.
    /// </summary>
    private const int OutOfBoundRowCount = 5;

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public KapPublicDisclosuresFeedTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    // =============================================================================================
    // TC-MDF-112 (i) — a date-watermarked cursor, its own coverage row, its own index space
    // =============================================================================================

    /// <summary>
    /// TC-MDF-112 (i) — the feed pages recent market disclosures from <b>one date-watermarked
    /// cursor</b>: the pass's only list request is a <b>date window</b> derived from the
    /// watermark (never an index cursor), the market-wide query is the empty
    /// <c>mkkMemberOidList</c> the probe recorded, and the pass persists its <b>own</b>
    /// <c>coverage_metadata</c> row (scope <c>market</c>, no instrument, its own data type) whose
    /// watermark is the window's last day and whose seen set is the capture's public indices —
    /// deduped within the source's <b>own</b> index space.
    /// </summary>
    [Fact]
    public async Task TC_MDF_112_i_date_watermarked_market_cursor_routes_the_pass_and_persists_its_own_row()
    {
        await ResetAsync();
        await BindAsync(["RODRG", "GEDZA"], boundInIndex: true);
        StubMarketProbe();

        await using var host = BuildIngestion();
        var result = await RunAsync(host);

        Assert.Equal(IngestResult.Succeeded, result.Status);
        Assert.True(result.Written > 0, "the feed must write the bound constituents' disclosures");

        // One list request, and its window is the watermark-derived date range — the public list is
        // date-windowed (there is no index to walk), and the market query is the empty member list.
        var listRequest = Assert.Single(
            _wireMock.Server.LogEntries,
            entry => entry.RequestMessage?.Path == ByCriteriaPath);
        var body = listRequest.RequestMessage!.Body!;
        Assert.Contains("\"fromDate\":\"2026-10-23\"", body, StringComparison.Ordinal);
        Assert.Contains("\"toDate\":\"2026-10-24\"", body, StringComparison.Ordinal);
        Assert.Contains("\"mkkMemberOidList\":[]", body, StringComparison.Ordinal);

        // Its own cursor row: market scope, no instrument, this feed's data type — and a watermark
        // that is the completed window's last day with the capture's indices as the seen set.
        var cursor = await ReadCursorAsync();
        Assert.NotNull(cursor);
        Assert.Equal(RunDay, cursor!.Watermark);
        Assert.Equal(AllProbeIndices.Order().ToArray(), cursor.Seen.Order().ToArray());

        var row = await ReadCursorRowAsync();
        Assert.Equal("market", row.Scope);
        Assert.Null(row.InstrumentId);
        Assert.Null(row.FundId);

        // The seen set is the public site's own index space: the newest captured index is present.
        Assert.Contains(NewestPublicIndex, cursor.Seen);
    }

    // =============================================================================================
    // TC-MDF-112 (ii) — bound-only writes; the rest counted and never fetched further
    // =============================================================================================

    /// <summary>
    /// TC-MDF-112 (ii) — <b>only bound constituents are written</b>. A market row names the
    /// companies it concerns through <c>stockCodes</c>; the rows whose codes belong to the bound
    /// (and whose companies have a stored public-site oid) become facts, every other row is
    /// <b>counted</b> with the out-of-bound reason and <b>never fetched further</b> — the pass makes
    /// no per-disclosure request at all, because the byCriteria row is the metadata.
    /// <para>
    /// The mapping is asserted on the capture's own rows: title = <c>subject</c> (never the issuer's
    /// <c>kapTitle</c>), type = <c>disclosureType</c>, publish date from the site's stamp.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TC_MDF_112_ii_only_bound_constituents_are_written_and_the_rest_are_counted()
    {
        await ResetAsync();
        // KTEST's test-entity rows are deliberately *not* bound: the same codes the capture lists
        // stay out of bound and are counted, never written.
        await BindAsync(["RODRG", "GEDZA"], boundInIndex: true);
        StubMarketProbe();

        await using var host = BuildIngestion();
        var result = await RunAsync(host);

        var rows = await ReadDisclosuresAsync();
        Assert.Equal(RodrgIndices.Length + GedzaIndices.Length, rows.Count);
        Assert.Equal(
            RodrgIndices.Length,
            rows.Count(row => row.InstrumentId == SymbolId("RODRG")));
        Assert.Equal(
            GedzaIndices.Length,
            rows.Count(row => row.InstrumentId == SymbolId("GEDZA")));

        // Out-of-bound rows are counted in `Skipped`; the out-of-bound reason is logged, not carried
        // in the result's counters.
        Assert.Equal(OutOfBoundRowCount, result.Skipped);

        // No per-disclosure request anywhere: the pass reads list pages only.
        Assert.All(
            _wireMock.Server.LogEntries,
            entry => Assert.Equal(ByCriteriaPath, entry.RequestMessage?.Path));

        // The rich row is mapped as the capture spells it — subject as the title, the issuer's
        // kapTitle never presented as a disclosure title.
        var responsibility = Assert.Single(rows, row => row.SourceUrl == $"{PublicDisclosureOrigin}{1687047}");
        Assert.Equal("Sorumluluk Beyanı (Konsolide)", responsibility.Title);
        Assert.Equal("FR", responsibility.DisclosureType);
        Assert.Equal(new DateOnly(2026, 10, 24), responsibility.PublishDate);
        Assert.All(rows, row => Assert.DoesNotContain("RODRİGO TEKSTİL", row.Title ?? string.Empty, StringComparison.Ordinal));

        // An out-of-bound code (the buy-back notice's MARBL) is never written.
        Assert.DoesNotContain(rows, row => row.SourceUrl == $"{PublicDisclosureOrigin}{1687037}");
    }

    /// <summary>
    /// TC-MDF-112 (ii) — <b>a filing that names two bound constituents writes one row per
    /// constituent</b> (TKT-foundation-084 review DR-1). The committed capture carries no
    /// multi-code row, so the capture's newest row has its <c>stockCodes</c> widened to the two
    /// bound codes <i>in memory</i> — a structural variation of the committed body, not an invented
    /// one. The store's identity must include the instrument: keyed on the shared <c>SourceUrl</c>
    /// alone, the second constituent's fact was counted <c>unchanged</c> and never written.
    /// </summary>
    [Fact]
    public async Task TC_MDF_112_ii_a_two_bound_code_row_writes_one_row_per_constituent()
    {
        await ResetAsync();
        await BindAsync(["RODRG", "GEDZA"], boundInIndex: true);

        const string capturedRow = "\"disclosureIndex\":1687047,\"isLate\":false,\"stockCodes\":\"RODRG\"";
        var body = RealSourceCatalog.Get(RealSourceCatalog.KapPublicMarketProbe).Body
            .Replace(
                capturedRow,
                "\"disclosureIndex\":1687047,\"isLate\":false,\"stockCodes\":\"RODRG,GEDZA\"",
                StringComparison.Ordinal);
        Assert.Contains("\"stockCodes\":\"RODRG,GEDZA\"", body, StringComparison.Ordinal);
        StubJsonArray(body);

        await using var host = BuildIngestion();
        var result = await RunAsync(host);

        Assert.Equal(IngestResult.Succeeded, result.Status);
        Assert.Equal(RodrgIndices.Length + GedzaIndices.Length + 1, result.Written);
        Assert.Equal(0, result.Unchanged);

        var rows = await ReadDisclosuresAsync();
        var shared = $"{PublicDisclosureOrigin}{1687047}";
        Assert.Equal(2, rows.Count(row => row.SourceUrl == shared));
        Assert.Single(rows, row => row.SourceUrl == shared && row.InstrumentId == SymbolId("RODRG"));
        Assert.Single(rows, row => row.SourceUrl == shared && row.InstrumentId == SymbolId("GEDZA"));
    }

    // =============================================================================================
    // TC-MDF-112 (iii) — honest empty advances forward; a source-down is a failed run
    // =============================================================================================

    /// <summary>
    /// TC-MDF-112 (iii) — an <b>honest empty</b> window persists <b>no backwards cursor</b>: the
    /// watermark moves <i>forward</i> past the empty window it covered, and the run is a success
    /// with zero writes (an empty answer is a statement about the window, never an error).
    /// </summary>
    [Fact]
    public async Task TC_MDF_112_iii_an_honest_empty_window_advances_the_watermark_forward_and_never_backwards()
    {
        await ResetAsync();
        await BindAsync(["RODRG", "GEDZA"], boundInIndex: true);
        // A stored cursor from an earlier day: the pass must advance it forward, never reset it.
        await SeedCursorAsync(new DateOnly(2026, 10, 22), []);
        StubJsonArray("[]");

        await using var host = BuildIngestion();
        var result = await RunAsync(host);

        Assert.Equal(IngestResult.Succeeded, result.Status);
        Assert.Equal(0, result.Written);

        var windowRequest = Assert.Single(
            _wireMock.Server.LogEntries,
            entry => entry.RequestMessage?.Path == ByCriteriaPath);
        Assert.Contains("\"fromDate\":\"2026-10-22\"", windowRequest.RequestMessage!.Body!, StringComparison.Ordinal);

        var cursor = await ReadCursorAsync();
        Assert.NotNull(cursor);
        Assert.Equal(RunDay, cursor!.Watermark);
        Assert.Empty(cursor.Seen);
        Assert.Empty(await ReadDisclosuresAsync());
    }

    /// <summary>
    /// TC-MDF-112 (iii) — a <b>source-down</b> list is a <c>failed</c> run with the named reason and
    /// <b>nothing fabricated</b>: no rows written, no cursor advanced (the watermark stays where the
    /// last durable write left it, so the next pass re-lists the same window).
    /// </summary>
    [Fact]
    public async Task TC_MDF_112_iii_a_source_down_list_fails_the_run_and_advances_nothing()
    {
        await ResetAsync();
        await BindAsync(["RODRG", "GEDZA"], boundInIndex: true);
        await SeedCursorAsync(new DateOnly(2026, 10, 22), []);

        _wireMock.Server
            .Given(Request.Create().WithPath(ByCriteriaPath).UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(500)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"error":"list unavailable"}"""));

        await using var host = BuildIngestion();
        var result = await RunAsync(host);

        Assert.Equal(IngestResult.Failed, result.Status);
        Assert.Equal(0, result.Written);
        Assert.Contains(KapPublicReasonCodes.SourceFetchFailed, result.FailureReason ?? string.Empty, StringComparison.Ordinal);
        Assert.Empty(await ReadDisclosuresAsync());

        // The watermark did not move: the failed window is re-listed by the next pass.
        var cursor = await ReadCursorAsync();
        Assert.NotNull(cursor);
        Assert.Equal(new DateOnly(2026, 10, 22), cursor!.Watermark);
    }

    // =============================================================================================
    // TC-MDF-112 (iv) — provenance `kap-public` + the public index; the VYK lane untouched
    // =============================================================================================

    /// <summary>
    /// TC-MDF-112 (iv) — every written row carries <c>SourceRef = kap-public</c> and the
    /// <b>public</b> <c>disclosureIndex</c> in its source URL; no VYK index ever appears and the VYK
    /// lane's rows and cursors are untouched. The coexistence is asserted directly: a VYK market
    /// cursor seeded before the run is <b>unchanged</b> by it, the public feed's cursor sits in its own
    /// row, and the run issues no VYK request.
    /// </summary>
    [Fact]
    public async Task TC_MDF_112_iv_rows_carry_the_public_evidence_class_and_the_vyk_lane_is_untouched()
    {
        await ResetAsync();
        await BindAsync(["RODRG", "GEDZA"], boundInIndex: true);
        // The VYK market feed's own cursor (078), seeded as the lane leaves it: head 1231017.
        const string vykCursorJson = """{"watermark":"1231017"}""";
        await SeedCoverageRowAsync(VykMarketCursorDataType, vykCursorJson);
        StubMarketProbe();

        await using (var host = BuildIngestion())
        {
            var result = await RunAsync(host);
            Assert.Equal(IngestResult.Succeeded, result.Status);
        }

        var rows = await ReadDisclosuresAsync();
        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.Equal(KapPublicSourceOptions.SourceName, row.SourceRef));
        Assert.All(rows, row => Assert.StartsWith(PublicDisclosureOrigin, row.SourceUrl ?? string.Empty, StringComparison.Ordinal));

        // The stamped indices are the public site's own — every one of them the capture's, and the
        // VYK gateway's head index never appears anywhere in this lane's output.
        var stamped = rows
            .Select(row => long.Parse(row.SourceUrl!.Split('/')[^1], System.Globalization.CultureInfo.InvariantCulture))
            .ToHashSet();
        Assert.All(stamped, index => Assert.Contains(index, AllProbeIndices));
        Assert.DoesNotContain(1231017L, stamped);

        // The VYK lane's cursor row is byte-for-byte what it was: this feed never writes it.
        await using (var db = _postgres.CreateContext())
        {
            var vykRow = await db.CoverageMetadata.SingleAsync(
                candidate => candidate.DataType == VykMarketCursorDataType);
            Assert.Equal(vykCursorJson, vykRow.Notes);
        }

        // No VYK request of any kind: the two feeds are separate clients with separate routes.
        Assert.Equal(0, _wireMock.RequestsFor("/disclosures"));
        Assert.Equal(0, _wireMock.Server.LogEntries.Count(entry =>
            entry.RequestMessage?.Path?.StartsWith("/disclosureDetail", StringComparison.Ordinal) == true));
    }

    // =============================================================================================
    // TC-MDF-112 (v) — a re-run is incremental from the watermark
    // =============================================================================================

    /// <summary>
    /// TC-MDF-112 (v) — the second pass is <b>incremental from the watermark</b>: it lists from the
    /// stored watermark's day (inclusive), the capture's rows are all already seen and therefore not
    /// re-written, and the stored rows are untouched.
    /// </summary>
    [Fact]
    public async Task TC_MDF_112_v_a_rerun_is_incremental_from_the_watermark_with_no_rewrite()
    {
        await ResetAsync();
        await BindAsync(["RODRG", "GEDZA"], boundInIndex: true);
        StubMarketProbe();

        int firstWritten;
        DateTimeOffset firstRecordedAt;
        await using (var host = BuildIngestion())
        {
            var first = await RunAsync(host);
            Assert.Equal(IngestResult.Succeeded, first.Status);
            firstWritten = first.Written;
            Assert.Equal(RodrgIndices.Length + GedzaIndices.Length, firstWritten);
        }

        await using (var db = _postgres.CreateContext())
        {
            firstRecordedAt = await db.KapDisclosures.Select(row => row.RecordedAt).FirstAsync();
        }

        _wireMock.Server.ResetLogEntries();

        await using (var host = BuildIngestion())
        {
            var second = await RunAsync(host);
            Assert.Equal(IngestResult.Succeeded, second.Status);
            Assert.Equal(0, second.Written);
            Assert.Equal(0, second.Quarantined);
        }

        // The second pass listed from the watermark, not from its bootstrap lookback.
        var windowRequest = Assert.Single(
            _wireMock.Server.LogEntries,
            entry => entry.RequestMessage?.Path == ByCriteriaPath);
        Assert.Contains("\"fromDate\":\"2026-10-24\"", windowRequest.RequestMessage!.Body!, StringComparison.Ordinal);

        // Nothing was re-written: same row count, same recorded timestamps.
        var rows = await ReadDisclosuresAsync();
        Assert.Equal(firstWritten, rows.Count);
        Assert.All(rows, row => Assert.Equal(firstRecordedAt, row.RecordedAt));

        var cursor = await ReadCursorAsync();
        Assert.NotNull(cursor);
        Assert.Equal(RunDay, cursor!.Watermark);
        Assert.Equal(AllProbeIndices.Order().ToArray(), cursor.Seen.Order().ToArray());
    }

    // =============================================================================================
    // TC-MDF-112 (vi) — the watermark is a checkpoint, not a walk product (the 069 property)
    // =============================================================================================

    /// <summary>
    /// TC-MDF-112 (vi) — the watermark is persisted only <b>after</b> the fact write, so a store
    /// failure cannot advance it past facts that never landed (069's TC-MDF-130 property, applied
    /// to this lane; TKT-foundation-084 review DR-3). The failure is injected where it happens in
    /// production — a PostgreSQL trigger that raises on the <c>kap_disclosures</c> INSERT — so the
    /// pass really emits its facts before the store refuses them.
    /// </summary>
    [Fact]
    public async Task TC_MDF_112_vi_a_store_failure_never_advances_the_watermark_past_unwritten_facts()
    {
        await ResetAsync();
        await BindAsync(["RODRG", "GEDZA"], boundInIndex: true);
        // A persisted cursor, as a second-day pass carries: the failure must leave it exactly here.
        await SeedCursorAsync(new DateOnly(2026, 10, 22), []);
        StubMarketProbe();

        await using var host = BuildIngestion();

        await BlockFactWritesAsync();
        try
        {
            // The run fails loudly (an exception, never a fabricated success) — the ledger-posting
            // posture of this in-flight failure class is 069's recorded observation, not re-pinned.
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => RunAsync(host));
            Assert.Contains(StoreFailureMarker, failure.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            await UnblockFactWritesAsync();
        }

        // No fact landed, and the watermark never advanced past them.
        Assert.Empty(await ReadDisclosuresAsync());
        var held = await ReadCursorAsync();
        Assert.NotNull(held);
        Assert.Equal(new DateOnly(2026, 10, 22), held!.Watermark);

        // The clean replay re-lists the same window, writes the facts, and only then moves the cursor.
        var replay = await RunAsync(host);
        Assert.Equal(IngestResult.Succeeded, replay.Status);
        Assert.Equal(RodrgIndices.Length + GedzaIndices.Length, replay.Written);

        var cursor = await ReadCursorAsync();
        Assert.NotNull(cursor);
        Assert.Equal(RunDay, cursor!.Watermark);
        Assert.Equal(RodrgIndices.Length + GedzaIndices.Length, (await ReadDisclosuresAsync()).Count);
    }

    // =============================================================================================
    // TC-MDF-112 — the two cheapest named-refusal paths (review DR-4)
    // =============================================================================================

    /// <summary>
    /// TC-MDF-112 — an <b>unrecognised <c>Ingestion:Disclosures:Source</c></b> fails the run
    /// <b>by name</b> and fetches <b>nothing</b>: a typo must never silently run the other source
    /// under the wrong provenance (review DR-4).
    /// </summary>
    [Fact]
    public async Task TC_MDF_112_an_unrecognised_source_fails_by_name_and_fetches_nothing()
    {
        await ResetAsync();
        await BindAsync(["RODRG", "GEDZA"], boundInIndex: true);
        StubMarketProbe();

        await using var host = BuildIngestion(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Ingestion:Disclosures:Source"] = "vyk-typo",
        });
        var result = await RunAsync(host);

        Assert.Equal(IngestResult.Failed, result.Status);
        Assert.Contains(DisclosuresJob.InvalidSourceSelection, result.FailureReason ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(0, result.Written);
        Assert.Empty(_wireMock.Server.LogEntries);
        Assert.Empty(await ReadDisclosuresAsync());
    }

    /// <summary>
    /// TC-MDF-112 — an <b>empty bound</b> is a named failure (<c>NO_BOUND_CONSTITUENTS</c>), never
    /// an empty success: a feed over nothing proves nothing (review DR-4). Zero requests are issued.
    /// </summary>
    [Fact]
    public async Task TC_MDF_112_an_empty_bound_is_a_named_failure_and_fetches_nothing()
    {
        await ResetAsync();
        await BindAsync([], boundInIndex: true);
        StubMarketProbe();

        await using var host = BuildIngestion();
        var result = await RunAsync(host);

        Assert.Equal(IngestResult.Failed, result.Status);
        Assert.Contains(DisclosuresJob.NoBoundConstituents, result.FailureReason ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(0, result.Written);
        Assert.Empty(_wireMock.Server.LogEntries);
        Assert.Empty(await ReadDisclosuresAsync());
    }

    // =============================================================================================
    // The production composition over the Testcontainers database and the committed probe double
    // =============================================================================================

    /// <summary>
    /// The production ingestion composition (the 079/082 harness convention): pacing bound to <c>0</c>
    /// because the pace is not the subject here and CI never sleeps; the posture selected explicitly
    /// because the code default is a refusal; the VYK client pointed at the same double so that any
    /// VYK request would be visible in the journal.
    /// </summary>
    private IngestionHost BuildIngestion(IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ConnectionStrings:Default"] = _postgres.ConnectionString,
            ["Ingestion:Disclosures:UniverseIndex"] = StatementBound.IndexCode,
            ["Ingestion:Disclosures:Source"] = DisclosuresSourceOptions.KapPublicSource,
            ["Ingestion:Disclosures:KapPublicLookbackDays"] = "1",
            ["Ingestion:Disclosures:KapPublicWindowDays"] = "7",
            ["Ingestion:KapPublic:BaseUrl"] = _wireMock.BaseUrl,
            ["Ingestion:KapPublic:RequestDelayMs"] = "0",
            ["Ingestion:KapPublic:ByCriteriaPath"] = ByCriteriaPath,
            ["Ingestion:KapPublic:AutomatedAccess"] = nameof(KapPublicAccessPosture.Allowed),
            ["Ingestion:Kap:BaseUrl"] = _wireMock.BaseUrl,
        };

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                settings[key] = value;
            }
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(RecordedAt));
        services.AddMarketDataIngestion(configuration);

        return new IngestionHost(services.BuildServiceProvider());
    }

    private static async Task<IngestResult> RunAsync(IngestionHost host)
    {
        using var scope = host.Provider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IIngestionJobRunner>();
        return await runner.RunAsync(DisclosuresJob.Code, new IngestionRequest(RunDay));
    }

    /// <summary>Makes every insert into <c>kap_disclosures</c> raise — the injected store failure a
    /// checkpoint-ordering case needs (the 069 harness pattern, test-local trigger name).</summary>
    private async Task BlockFactWritesAsync()
    {
        const string sql = """
            CREATE OR REPLACE FUNCTION tkt084_block_kap_disclosures() RETURNS trigger AS $tkt$
            BEGIN
                RAISE EXCEPTION 'TKT084_STORE_FAILURE on kap_disclosures';
            END;
            $tkt$ LANGUAGE plpgsql;
            CREATE TRIGGER tkt084_block_kap_disclosures BEFORE INSERT ON kap_disclosures
                FOR EACH ROW EXECUTE FUNCTION tkt084_block_kap_disclosures();
            """;

        await using var db = _postgres.CreateContext();
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    /// <summary>Removes the injected failure so the same world can be re-run cleanly.</summary>
    private async Task UnblockFactWritesAsync()
    {
        const string sql = """
            DROP TRIGGER IF EXISTS tkt084_block_kap_disclosures ON kap_disclosures;
            DROP FUNCTION IF EXISTS tkt084_block_kap_disclosures();
            """;

        await using var db = _postgres.CreateContext();
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    private const string StoreFailureMarker = "TKT084_STORE_FAILURE";

    // =============================================================================================
    // Doubles
    // =============================================================================================

    /// <summary>
    /// The <b>committed</b> market-wide byCriteria probe body (the OQ-1 evidence, 2026-10-25 capture)
    /// served for any list window: the pass's only request is the market-wide one, so a single
    /// double is the whole world this feed can see.
    /// </summary>
    private void StubMarketProbe() =>
        _wireMock.StubJson(
            ByCriteriaPath,
            RealSourceCatalog.Get(RealSourceCatalog.KapPublicMarketProbe).Body,
            method: "POST");

    private void StubJsonArray(string json) =>
        _wireMock.StubJson(ByCriteriaPath, json, (int)HttpStatusCode.OK, method: "POST");

    // =============================================================================================
    // Seed / read helpers (the §23 harness convention: a test-local bound, the L2 world)
    // =============================================================================================

    private async Task ResetAsync()
    {
        await using (var db = _postgres.CreateContext())
        {
            await FixtureSeeder.ApplyAsync(db, L2);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM kap_public_company_map");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM kap_disclosures");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM quarantined_facts");
            await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM coverage_metadata WHERE data_type IN ({0}, {1})",
                KapPublicDisclosuresCursorStore.DataType,
                VykMarketCursorDataType);
        }

        _wireMock.Server.ResetMappings();
        _wireMock.Server.ResetLogEntries();
    }

    /// <summary>
    /// Binds <paramref name="symbols"/>: their instrument rows, the test-local <c>STMT</c> fetch bound
    /// (the disclosures lane's own <c>UniverseIndex</c>), and — when
    /// <paramref name="boundInIndex"/> — a stored public-site oid per symbol (080's map, which is what
    /// makes a constituent part of <i>this</i> source).
    /// </summary>
    private async Task BindAsync(string[] symbols, bool boundInIndex)
    {
        await using var db = _postgres.CreateContext();
        foreach (var symbol in symbols)
        {
            if (await db.Instruments.AnyAsync(instrument => instrument.Symbol == symbol))
            {
                continue;
            }

            db.Instruments.Add(new Instrument
            {
                Symbol = symbol,
                Isin = $"TRA{symbol}00001",
                Name = symbol,
                Status = "active",
                SourceRef = "test:kap-public-disclosures-feed",
                RecordedAt = RecordedAt,
            });
        }

        await db.SaveChangesAsync();

        if (!boundInIndex)
        {
            return;
        }

        await StatementBound.ReplaceAsync(db, symbols);

        var oids = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["RODRG"] = RodrgPublicOid,
            ["GEDZA"] = GedzaPublicOid,
            ["KTEST"] = KtestPublicOid,
        };

        foreach (var symbol in symbols)
        {
            var instrument = await db.Instruments.SingleAsync(candidate => candidate.Symbol == symbol);
            db.KapPublicCompanyMaps.Add(new KapPublicCompanyMap
            {
                InstrumentId = instrument.Id,
                KapPublicOid = oids[symbol],
                KapPublicOidSource = "test: synthetic public oid (not a capture)",
                KapPublicOidAsOf = RunDay,
                RecordedAt = RecordedAt,
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task SeedCursorAsync(DateOnly watermark, IReadOnlyList<long> seen)
    {
        await using var db = _postgres.CreateContext();
        db.CoverageMetadata.Add(new CoverageMetadata
        {
            Scope = KapPublicDisclosuresCursorStore.Scope,
            InstrumentId = null,
            FundId = null,
            DataType = KapPublicDisclosuresCursorStore.DataType,
            Notes = new KapPublicDisclosuresCursor(watermark, seen).ToJson(),
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedCoverageRowAsync(string dataType, string notes)
    {
        await using var db = _postgres.CreateContext();
        db.CoverageMetadata.Add(new CoverageMetadata
        {
            Scope = "market",
            InstrumentId = null,
            FundId = null,
            DataType = dataType,
            Notes = notes,
        });
        await db.SaveChangesAsync();
    }

    private async Task<KapPublicDisclosuresCursor?> ReadCursorAsync()
    {
        await using var db = _postgres.CreateContext();
        var notes = await db.CoverageMetadata
            .Where(row => row.DataType == KapPublicDisclosuresCursorStore.DataType
                && row.Scope == KapPublicDisclosuresCursorStore.Scope)
            .Select(row => row.Notes)
            .SingleOrDefaultAsync();
        return KapPublicDisclosuresCursor.TryParse(notes);
    }

    private async Task<CoverageMetadata> ReadCursorRowAsync()
    {
        await using var db = _postgres.CreateContext();
        return await db.CoverageMetadata.SingleAsync(
            row => row.DataType == KapPublicDisclosuresCursorStore.DataType
                && row.Scope == KapPublicDisclosuresCursorStore.Scope);
    }

    private async Task<List<KapDisclosure>> ReadDisclosuresAsync()
    {
        await using var db = _postgres.CreateContext();
        return await db.KapDisclosures.AsNoTracking().ToListAsync();
    }

    private long SymbolId(string symbol)
    {
        using var db = _postgres.CreateContext();
        return db.Instruments.Single(instrument => instrument.Symbol == symbol).Id;
    }

    private sealed class IngestionHost : IAsyncDisposable
    {
        public IngestionHost(ServiceProvider provider) => Provider = provider;

        public ServiceProvider Provider { get; }

        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }
}
