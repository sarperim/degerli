using System.Globalization;
using System.Text;
using System.Text.Json;
using Degerli.Ingestion.Kap;
using Degerli.Ingestion.Scheduling;
using Degerli.Ingestion.Sources;
using Degerli.Ingestion.Statements;
using Degerli.Ingestion.Universe;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.Disclosures;

/// <summary>
/// Route + document-store configuration for <c>disclosures</c> (FR-MDF-005). The shipped routes
/// are the <b>live-verified</b> MKK VYK gateway routes (01 v1.5 §10.9.11/§10.9.15): the disclosure
/// list is <c>GET /disclosures</c> and each disclosure's metadata is the detail body
/// (<c>GET /disclosureDetail/{index}?fileType=data</c>). The former placeholder <c>/disclosures</c>
/// single-resource assumption (one pre-built body per run) is replaced by the real list+detail
/// contract.
/// </summary>
public sealed class DisclosuresSourceOptions
{
    public const string SectionName = "Ingestion:Disclosures";

    /// <summary>
    /// The VYK disclosure-list route (live-verified 2026-10-20). When the VYK company-scoped source
    /// is disabled (§10.9.10) the same setting is the single-resource route of the legacy canonical
    /// double, exactly as before this rewire.
    /// </summary>
    public string Path { get; set; } = "/disclosures";

    /// <summary>
    /// The VYK disclosure-detail route (live-verified 2026-10-20); the
    /// <c>{disclosureIndex}</c> token is substituted and <c>?fileType=data</c> appended.
    /// </summary>
    public string DetailPath { get; set; } = "/disclosureDetail/{disclosureIndex}";

    /// <summary>
    /// The index whose current constituents the run loops over (01 v1.5 §10.9.2/§10.9.15) — its own
    /// key, independent of the prices and statements bounds (AD-14). Launch default <c>XU030</c>.
    /// It bounds both feeds: the company feed walks these constituents, and the market feed keeps
    /// only rows whose owning company is one of them.
    /// </summary>
    public string UniverseIndex { get; set; } = "XU030";

    /// <summary>
    /// The disclosure index a company with no persisted cursor starts from when the bootstrap
    /// window is disabled (<see cref="BootstrapLookback"/> ≤ 0). It is the pre-078 behaviour —
    /// a walk from the beginning of KAP's platform history — and is kept only as the documented
    /// escape hatch for a deliberate archive re-walk; the shipped default never uses it.
    /// </summary>
    public string EarlyDisclosureIndex { get; set; } = "0";

    /// <summary>
    /// The VYK gateway source selector value (01 v1.5 §10.9.11) — the shipped default.
    /// </summary>
    public const string VykSource = "vyk";

    /// <summary>
    /// The KAP <b>public-site</b> source selector value (01 v1.5.85 §10.9.19, TKT-foundation-084).
    /// </summary>
    public const string KapPublicSource = "kap-public";

    /// <summary>
    /// Which source the disclosures lane reads (<c>Ingestion:Disclosures:Source</c>): the VYK gateway
    /// (<see cref="VykSource"/>, the shipped default) or the KAP public site
    /// (<see cref="KapPublicSource"/>).
    /// <para>
    /// <b>Selecting a source is configuration, never a rewrite</b> (two-source discipline): the VYK
    /// wiring stays intact, the public feed is opt-in, and an unrecognised value fails the run by
    /// name (<see cref="TryParseSource"/>) instead of silently running the other source under the
    /// wrong provenance. The two feeds coexist as separate sources with separate cursors and
    /// separate evidence classes — switching back is a config change.
    /// </para>
    /// </summary>
    public string Source { get; set; } = VykSource;

    /// <summary>
    /// The public-site feed's <b>bootstrap lookback</b> in days (TKT-foundation-084, 01 v1.5.85
    /// §10.9.19): how many days <b>before the run day</b> a pass with <b>no persisted cursor</b>
    /// starts listing the market. The covered span is therefore <c>lookback + 1</c> inclusive days
    /// (the start day through the run day). It is a <b>request</b>, recorded as such — never a claim
    /// that the market published nothing before it (the 078/082 provenance rule, one level down).
    /// </summary>
    public int KapPublicLookbackDays { get; set; } = DefaultKapPublicLookbackDays;

    /// <summary>
    /// The shipped public-site lookback: <b>7</b> days before the run day — an <b>eight-day</b>
    /// inclusive bootstrap span, i.e. one 7-day window plus a 1-day window, not one 7-day window.
    /// The feed is a <i>recent</i>-disclosures
    /// feed (FR-MDF-005), and a week is a handful of pages on the first pass and a single day's window
    /// thereafter. A value ≤ 0 is read as 1.
    /// </summary>
    public const int DefaultKapPublicLookbackDays = 7;

    /// <summary>
    /// How many days one public-site byCriteria list request covers (TKT-foundation-084). The public
    /// list is <b>date-windowed</b>, not index-paged, so the window is the unit of bounded listing: a
    /// narrower window is a smaller answer at the cost of more list requests (each paced at ≥3 s and
    /// counted against the shared pass page ceiling).
    /// </summary>
    public int KapPublicWindowDays { get; set; } = DefaultKapPublicWindowDays;

    /// <summary>The shipped window width: <b>7</b> days. A bootstrap's span is <c>lookback + 1</c>
    /// inclusive days (see <see cref="KapPublicLookbackDays"/>), so the shipped 7 + 1 chunked at 7
    /// takes one 7-day window plus a 1-day window.</summary>
    public const int DefaultKapPublicWindowDays = 7;

    /// <summary>
    /// Normalizes <paramref name="source"/> to a known selector. Unset/blank binds the shipped
    /// default; an unrecognised value is <b>not</b> mapped onto either source (a typo must fail
    /// loudly instead of reading the wrong source under the right-looking provenance).
    /// </summary>
    public static bool TryParseSource(string? source, out string selected)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            selected = VykSource;
            return true;
        }

        var trimmed = source.Trim();
        if (string.Equals(trimmed, VykSource, StringComparison.OrdinalIgnoreCase))
        {
            selected = VykSource;
            return true;
        }

        if (string.Equals(trimmed, KapPublicSource, StringComparison.OrdinalIgnoreCase))
        {
            selected = KapPublicSource;
            return true;
        }

        selected = trimmed;
        return false;
    }

    /// <summary>True when the configured <see cref="Source"/> selects the public-site feed.</summary>
    public bool KapPublicSelected => TryParseSource(Source, out var selected)
        && string.Equals(selected, KapPublicSource, StringComparison.Ordinal);

    /// <summary>The market feed's name in <see cref="Feed"/> (TKT-foundation-078).</summary>
    public const string FeedMarket = "market";

    /// <summary>The company feed's name in <see cref="Feed"/> (TKT-foundation-078).</summary>
    public const string FeedCompany = "company";

    /// <summary>
    /// Which feed a run walks (TKT-foundation-078, 01 §10.9.15). <see cref="FeedMarket"/> (the
    /// shipped default) pages the <b>market-wide</b> list from one cursor — recent disclosures
    /// across the market, a handful of pages per run. <see cref="FeedCompany"/> is the bounded
    /// first-seed/backfill walk over the bound index's constituents.
    /// </summary>
    public string Feed { get; set; } = FeedMarket;

    /// <summary>
    /// The shipped company bootstrap window, in <b>index positions</b> below the platform head
    /// (<c>/lastDisclosureIndex</c>): <b>5,000</b>. Live arithmetic (2026-10-24 probes):
    /// a company's own rows are <i>sparse</i> in index space — ASELS' 39-row page spanned
    /// 1092199 → 1142913 (50,040 positions), so one page usually covers the whole window
    /// (≈1–2 list calls of ≤50 rows). Before this window existed the walk started at
    /// <see cref="EarlyDisclosureIndex"/> = <c>0</c>, i.e. ~138,818 positions ≈ 39 pages per
    /// company ≈ 4 hours for one XU030 pass at the observed ~12 s pace (run 208:
    /// 10:41:24 → 13:41:24, exactly 3h00m, <c>written=0</c>).
    /// </summary>
    public const long DefaultBootstrapLookback = 5_000;

    /// <summary>
    /// The company bootstrap window in index positions (TKT-foundation-078). A company with
    /// <b>no</b> persisted cursor starts this far below the platform head instead of at
    /// <see cref="EarlyDisclosureIndex"/>; the window is recorded on the run as provenance
    /// (never as "this company has no disclosures"). A value ≤ 0 disables the window and restores
    /// the pre-078 from-zero start.
    /// </summary>
    public long BootstrapLookback { get; set; } = DefaultBootstrapLookback;

    /// <summary>
    /// The shipped <b>market</b> bootstrap window, in index positions below the platform head:
    /// <b>1,000</b>. Market-wide rows are <i>dense</i> (live probe 2026-10-24: the page at index
    /// 1230970 carried 48 rows and reached 1231017 — essentially one row per index), so 1,000
    /// positions ≈ 1,000 rows ≈ 20 pages of 50 ≈ 200 s of list pacing at the shipped 10 s delay
    /// (≈4 min at the observed ~12 s), and immediately covers the newest disclosures (the live
    /// criterion's "within ~50 of <c>lastDisclosureIndex</c>").
    /// </summary>
    public const long DefaultMarketBootstrapLookback = 1_000;

    /// <summary>
    /// The market bootstrap window in index positions (TKT-foundation-078). Used only when the
    /// market feed has no persisted cursor. A value ≤ 0 starts at the platform head itself.
    /// </summary>
    public long MarketBootstrapLookback { get; set; } = DefaultMarketBootstrapLookback;

    /// <summary>
    /// The shipped per-walk list-page ceiling: <b>24</b>. 24 pages × 10 s (the shipped
    /// <c>Ingestion:Statements:RequestDelayMs</c> the shared pacer binds; the lane's live attempts
    /// ran at an observed ~12 s) = 240 s = 4 min of list pacing for one walk — and more than the
    /// 20 pages a 1,000-position market bootstrap needs, so the first run still reaches the head.
    /// </summary>
    public const int DefaultMaxListPages = 24;

    /// <summary>
    /// The <b>per-walk</b> list-page ceiling (TKT-foundation-078). The walk stops at the bound,
    /// records the truncation, persists the last index it completed and the next run resumes
    /// there — so a walk is finite even when the archive is larger than the window.
    /// <para>
    /// It is not a per-<b>run</b> budget: a <c>Feed = company</c> pass builds one walk per
    /// constituent, so that pass costs constituents × (pages + details) — nominal ≈ 37.3 min per
    /// constituent at 10 s, i.e. ≈ 18.7 h over 30 constituents — and is a recorded, resumable
    /// multi-run backfill, not a single step. The shipped <c>market</c> feed is one walk per run.
    /// </para>
    /// </summary>
    public int MaxListPages { get; set; } = DefaultMaxListPages;

    /// <summary>
    /// The shipped per-walk disclosure-detail ceiling: <b>200</b>. <b>Nominal</b> cost: 200 details
    /// × 10 s = 2,000 s ≈ 33.3 min, the dominant term of a walk (pages ≤ 4 min; one walk ≈
    /// 2,240 s ≈ 37.3 min total). This is a bound on <i>work</i>, not wall clock: a detail that
    /// stalls is bounded per item at 180 s
    /// (<see cref="KapSourceOptions.DefaultDetailFetchTimeoutMs"/>), so a degraded gateway can
    /// drive 200 × 180 s = 36,000 s ≈ <b>10 h</b> out of one walk (+ ≤ 12 min of list calls at the
    /// 30 s client timeout) — more than a 120-min step bound. That is why this count is paired with
    /// <see cref="MaxWalkDuration"/> (90 min, strictly inside the 120-min per-step bound): the walk
    /// self-truncates at the deadline and persists the last index it completed, so a degraded
    /// gateway costs a bounded pass with a saved cursor instead of a watchdog cancellation that
    /// discards every completed page (the run-208 class). The watchdog remains the backstop for a
    /// single request that outlives its own per-item timeout.
    /// <para>
    /// In the shipped <c>market</c> feed the detail term cannot bind: 24 pages × 50 rows at the
    /// bound-universe share (~30 of 1,037 companies) is ≈ 36 bound rows, so a pass is bounded by
    /// its pages in practice and spends a handful of details. The remainder is resumed by the
    /// next run, never skipped.
    /// </para>
    /// </summary>
    public const int DefaultMaxDetailFetches = 200;

    /// <summary>
    /// The <b>per-walk</b> disclosure-detail ceiling (TKT-foundation-078): the number of details
    /// one walk may fetch (or isolate). A walk that exhausts it stops at the last index it
    /// <b>completed</b> and records the truncation; nothing is skipped over.
    /// </summary>
    public int MaxDetailFetches { get; set; } = DefaultMaxDetailFetches;

    /// <summary>
    /// The shipped <b>per-pass wall-clock</b> bound: <b>90 minutes</b>. It sits strictly below the
    /// scheduler's <b>120-minute per-step</b> bound (01 v1.5.43 §10.9.17,
    /// <see cref="IngestionSchedulerOptions.DefaultMaxStepDuration"/>), leaving
    /// ≈30 min for the job's store write and the rest of the step — so the walk truncates itself
    /// <i>inside</i> the step and persists the last index it completed, instead of being cancelled
    /// by the watchdog with every completed page discarded (the run-208 class: <c>RUN_HUNG</c>,
    /// no counts, no cursor advance, the identical pass repeated next time).
    /// </summary>
    public static readonly TimeSpan DefaultMaxWalkDuration = TimeSpan.FromMinutes(90);

    /// <summary>
    /// The wall-clock bound of one <b>pass</b> (TKT-foundation-078): one clock is started in
    /// <c>FetchVykAsync</c> and shared by every walk the pass runs. The <c>market</c> feed is one
    /// walk per run, so this is that walk's deadline; the <c>company</c> feed shares it across the
    /// constituent loop, so one pass (and therefore one run) is bounded as a whole — the already
    /// completed companies' cursors are still persisted when it stops.
    /// <para>
    /// The walk checks the deadline before each list and detail request, using the injected
    /// <see cref="TimeProvider"/> (no real sleeps, deterministic on a fake clock). On expiry it
    /// records <c>truncated</c>, stops at the last index it <b>completed</b> and persists that index
    /// — a deadline stop is an honest bounded pass (<c>succeeded</c>/<c>partial</c> with counts),
    /// never a failure and never a lost page. The remaining work resumes exactly there next run.
    /// The worst case of a pass is therefore <c>min(work bound, MaxWalkDuration)</c>; the scheduler
    /// watchdog remains the backstop for a single request that outlives its own per-item timeout.
    /// </para>
    /// <para>
    /// The shipped value is <see cref="DefaultMaxWalkDuration"/> (90 min) and the shipped
    /// <c>appsettings.json</c> carries the identical value, so config and code cannot drift. A
    /// value ≤ 0 disables the deadline (the pre-078 behaviour), just as the other bounds' ≤ 0
    /// values disable theirs.
    /// </para>
    /// </summary>
    public TimeSpan MaxWalkDuration { get; set; } = DefaultMaxWalkDuration;

    /// <summary>
    /// The listed row's <c>disclosureType</c> this adapter keeps. <b>Empty = every type</b> (the
    /// disclosures job is the general disclosure surface, so the live-verified unfiltered company
    /// list is the right universe); a non-empty value narrows the family the same way the
    /// corporate-actions/dividends adapters do. Applied client-side — a gateway-filtered empty set
    /// answers <b>HTTP 400 <c>ER005</c></b> instead of an empty list (§10.9.15).
    /// </summary>
    public string DisclosureType { get; set; } = string.Empty;

    /// <summary>
    /// Directory where KAP documents are archived on disk (outside the DB). When set, each
    /// stored row records its <c>document_path</c> under this root (FR-MDF-005). Empty in
    /// compositions that ingest metadata only.
    /// </summary>
    public string? DocumentRoot { get; set; }
}

/// <summary>
/// KAP disclosures adapter (FR-MDF-005), rewired onto the MKK VYK gateway
/// (01 v1.5 §10.9.15, the §10.9.11 source contract).
/// <para>
/// The <c>disclosures</c> job asks the adapter for one date's payload, so the company-scoped loop
/// lives here (the job files are outside this ticket): the adapter resolves the configured index's
/// current constituents, maps each symbol to the gateway's <c>companyId</c>, walks that company's
/// disclosure list from its persisted cursor and fetches each new disclosure's detail for the
/// metadata the list does not carry — the list row's <c>title</c> is the <i>company name</i> and
/// carries no date (live-verified 2026-10-20), while the detail carries <c>subject{tr,en}</c>,
/// <c>time</c> and <c>link</c>. Metadata only: documents stay on disk (§10.9.11's attachment layer
/// is a separate follow-up), so the stored row keeps the contract's fields plus the configured
/// <c>document_path</c>. Every gateway call is paced by the run's <see cref="KapRequestPacer"/>.
/// </para>
/// <para>
/// <b>Failure posture (honest, per-item)</b> — identical to the corporate-actions rewire: a
/// transport failure/HTTP error/gateway fault on the <b>list</b> route is a
/// <see cref="SourceFetchException"/> (the run is recorded <c>failed</c>, never a fabricated empty
/// success), except a <c>400</c> on a <b>list</b> request whose body is the gateway's <c>ER005</c>
/// envelope, which is end-of-list (an honest empty, §10.9.15); a malformed body is a
/// <see cref="SourcePayloadException"/> (the job quarantines <c>UNPARSEABLE_PAYLOAD</c>). An
/// <b>un-fetchable disclosure detail is isolated per disclosure</b> (01 v1.5.40 §10.9.15,
/// TKT-foundation-061): the walk records the skip with the gateway's error verbatim
/// (<see cref="VykDisclosureWalker.DisclosureDetailUnfetchable"/>), keeps walking, and the job
/// counts and quarantines it so the run ends <c>partial</c> — never a whole-run failure for one
/// bad attachment, and never a silent omission; a detail that outlives the configured per-item
/// timeout takes the same skip path. A bound
/// symbol the directory cannot map is <b>isolated per constituent</b> (01 v1.5 §10.9.15, the
/// §10.9.10/§10.9.11 per-company isolation doctrine, FR-MDF-012): the loop names it
/// <see cref="VykCompanyDirectory.CompanyIdentityUnresolved"/>, skips it and continues, and the
/// job records it in the run's per-symbol stats (see <see cref="IVykIdentityIsolationReporter"/>) —
/// never written under a fabricated identity, never a whole-run fail-stop.
/// </para>
/// <para>
/// <b>Every cursor is a checkpoint, not a walk product (TKT-foundation-069, §10.9.15).</b> Both
/// feeds <b>collect</b> their cursor work — the per-instrument advances and clears of the
/// company-scoped pass, the single market-wide row of the market pass — and stop there; the job
/// persists it through <see cref="PersistCursorsAsync"/> only <b>after</b> its fact write (and the
/// skip evidence) succeeded, the 063/statements shape. A store failure therefore leaves every
/// cursor exactly where the last durable write left it, so the next run re-walks the same indexes
/// and re-emits their facts (the idempotent store reports them <c>unchanged</c>). No run can
/// advance a cursor past facts that were never written; the accepted residual this replaces is
/// closed.
/// </para>
/// </summary>
public sealed class DisclosuresSourceAdapter : ISourceAdapter<DisclosuresPayload>, IVykIdentityIsolationReporter, IDisclosureIsolationReporter, IDisclosureBootstrapReporter
{
    /// <summary>The <c>coverage_metadata.data_type</c> of this family's per-instrument cursor.</summary>
    internal const string CursorDataType = "kap-disclosures-cursor";

    /// <summary>
    /// The <c>coverage_metadata.data_type</c> of the <b>market-wide</b> cursor (TKT-foundation-078):
    /// one row at <c>market</c> scope, no instrument, advancing with each paged pass over
    /// <c>GET /disclosures?disclosureIndex={n}</c>.
    /// </summary>
    internal const string MarketCursorDataType = "kap-disclosures-market-cursor";

    /// <summary>The market scope name of the market-wide cursor row.</summary>
    internal const string MarketCursorScope = "market";

    /// <summary>
    /// The bootstrap provenance entry of the market feed (see <see cref="IDisclosureBootstrapReporter"/>):
    /// the market-wide lane has one name, used both as the cursor row's
    /// <c>coverage_metadata.scope</c> and as the provenance entry — so the two literals cannot
    /// drift apart (review DR-8).
    /// </summary>
    public const string MarketCursorName = MarketCursorScope;

    private const string InstrumentScope = "instrument";

    /// <summary>
    /// The named reason recorded for a market row whose owning company is outside the bound
    /// universe (TKT-foundation-078): counted in the run's <c>skipped</c> stat and logged, never
    /// fetched and never written under a fabricated identity.
    /// </summary>
    public const string OutsideBoundUniverse = "OUTSIDE_BOUND_UNIVERSE";

    /// <summary>The named reason recorded for the bootstrap provenance entries (TKT-foundation-078).</summary>
    public const string BootstrappedCursor = "BOOTSTRAPPED";

    private readonly IKapSourceClient _client;
    private readonly DisclosuresSourceOptions _options;
    private readonly KapSourceOptions? _kapOptions;
    private readonly VykCompanyDirectory? _directory;
    private readonly KapRequestPacer? _pacer;
    private readonly UniverseSyncStore? _universe;
    private readonly DegerliDbContext? _db;
    private readonly ILogger<DisclosuresSourceAdapter>? _logger;
    private readonly TimeProvider _clock;
    private readonly List<string> _unresolvedSymbols = [];
    private readonly List<VykDetailSkip> _detailSkips = [];
    private readonly List<string> _bootstrapped = [];
    private readonly List<(string Symbol, string Cursor)> _pendingCursors = [];
    private readonly List<string> _pendingClearedCursors = [];
    private string? _pendingMarketCursor;
    private int _outOfBoundRows;
    private int _listPages;
    private bool _truncated;

    public DisclosuresSourceAdapter(IKapSourceClient client, IOptions<DisclosuresSourceOptions> options)
        : this(client, options, kapOptions: null, directory: null, pacer: null, universe: null, db: null, logger: null, clock: null)
    {
    }

    public DisclosuresSourceAdapter(
        IKapSourceClient client,
        IOptions<DisclosuresSourceOptions> options,
        IOptions<KapSourceOptions>? kapOptions,
        VykCompanyDirectory? directory,
        KapRequestPacer? pacer,
        UniverseSyncStore? universe,
        DegerliDbContext? db,
        ILogger<DisclosuresSourceAdapter>? logger,
        TimeProvider? clock = null)
    {
        _client = client;
        _options = options.Value;
        _kapOptions = kapOptions?.Value;
        _directory = directory;
        _pacer = pacer;
        _universe = universe;
        _db = db;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>True when the VYK company-scoped source is active (§10.9.11).</summary>
    public bool VykEnabled => _directory?.Enabled == true && _universe is not null && _db is not null;

    /// <inheritdoc />
    public string SourceName => "kap";

    /// <inheritdoc />
    public IReadOnlyList<string> UnresolvedSymbols => _unresolvedSymbols;

    /// <summary>The disclosure details the last walk isolated (01 v1.5.40 §10.9.15): one un-fetchable
    /// attachment yields a recorded skip with the gateway's error, never a failed run
    /// (TKT-foundation-061).
    /// </summary>
    public IReadOnlyList<VykDetailSkip> DetailSkips => _detailSkips;

    /// <inheritdoc />
    public IReadOnlyList<string> Bootstrapped => _bootstrapped;

    /// <inheritdoc />
    public int OutOfBoundRows => _outOfBoundRows;

    /// <inheritdoc />
    public int ListPages => _listPages;

    /// <inheritdoc />
    public bool Truncated => _truncated;

    /// <summary>
    /// True when the last walk collected cursor work that is not yet durably persisted
    /// (TKT-foundation-069): the job calls <see cref="PersistCursorsAsync"/> after its fact write.
    /// Both feeds' work counts — the per-instrument advances, the clears and the market row.
    /// </summary>
    public bool HasPendingCursors =>
        _pendingCursors.Count > 0 || _pendingClearedCursors.Count > 0 || _pendingMarketCursor is not null;

    /// <summary>
    /// Persists the walk's cursor work (01 v1.5 §10.9.11, TKT-foundation-069) —
    /// <b>called by the job after the fact write succeeded</b>, never by the walk: the market row,
    /// then the company-scoped advances, then the clears of the resets whose re-walk was honestly
    /// empty. The pending sets are taken and cleared before the writes, so a second call is a
    /// no-op; a failure surfaces out of the job and fails the run, and the cursor work it did not
    /// reach stays where it was — the next run re-walks those indexes and the already-written facts
    /// come back `unchanged`.
    /// </summary>
    public async Task PersistCursorsAsync(CancellationToken cancellationToken = default)
    {
        var market = _pendingMarketCursor;
        _pendingMarketCursor = null;
        if (market is not null)
        {
            await SetMarketCursorAsync(market, cancellationToken).ConfigureAwait(false);
        }

        var pending = _pendingCursors.ToList();
        _pendingCursors.Clear();
        foreach (var (symbol, cursor) in pending)
        {
            await SetCursorAsync(symbol, cursor, cancellationToken).ConfigureAwait(false);
        }

        var cleared = _pendingClearedCursors.ToList();
        _pendingClearedCursors.Clear();
        foreach (var symbol in cleared)
        {
            await ClearCursorAsync(symbol, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<SourcePayload<DisclosuresPayload>> FetchAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        if (!VykEnabled)
        {
            var legacyRaw = await _client.GetStringAsync(_options.Path, cancellationToken).ConfigureAwait(false);
            return KapPayload.Parse<DisclosuresPayload>(legacyRaw);
        }

        return await FetchVykAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The VYK run (§10.9.15): one paced pass over the configured feed. <b>Market</b> (the shipped
    /// default, TKT-foundation-078) pages the market-wide list from one cursor — recent
    /// disclosures across the market, bounded and incremental; <b>company</b> is the bounded
    /// first-seed/backfill walk over the bound index's constituents.
    /// </summary>
    private async Task<SourcePayload<DisclosuresPayload>> FetchVykAsync(CancellationToken cancellationToken)
    {
        var symbols = await _universe!.GetCurrentConstituentSymbolsAsync(_options.UniverseIndex, cancellationToken)
            .ConfigureAwait(false);
        if (symbols.Count == 0)
        {
            throw new SourceFetchException(
                $"Index '{_options.UniverseIndex}' has no current constituents; a disclosures run over an empty bound is a failure, not an empty success.");
        }

        var snapshot = await _directory!.GetAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot.Failed)
        {
            throw new SourceFetchException(
                $"VYK members directory unavailable; cannot resolve company identities for '{_options.UniverseIndex}' ({snapshot.FailureReason}).");
        }

        _unresolvedSymbols.Clear();
        _detailSkips.Clear();
        _bootstrapped.Clear();
        _pendingCursors.Clear();
        _pendingClearedCursors.Clear();
        _pendingMarketCursor = null;
        _outOfBoundRows = 0;
        _listPages = 0;
        _truncated = false;

        // One clock per pass (TKT-foundation-078): the deadline is computed once, here, and shared
        // by every walk of the pass. The market feed is one walk; the company feed shares it across
        // the constituent loop, so a pass (and therefore a run) has one wall-clock bound. A value
        // ≤ 0 disables it (the pre-078 behaviour).
        var deadline = _options.MaxWalkDuration > TimeSpan.Zero
            ? _clock.GetUtcNow() + _options.MaxWalkDuration
            : (DateTimeOffset?)null;

        var market = string.Equals(_options.Feed, DisclosuresSourceOptions.FeedMarket, StringComparison.OrdinalIgnoreCase);

        if (_logger is not null)
        {
            _logger.LogInformation(
                "disclosures: {Feed} feed over {Count} current constituents of {Index} via the VYK gateway (list pages ≤ {MaxPages}, details ≤ {MaxDetails})",
                market ? DisclosuresSourceOptions.FeedMarket : DisclosuresSourceOptions.FeedCompany,
                symbols.Count,
                _options.UniverseIndex,
                _options.MaxListPages,
                _options.MaxDetailFetches);
        }

        return market
            ? await FetchMarketAsync(symbols, snapshot, deadline, cancellationToken).ConfigureAwait(false)
            : await FetchCompaniesAsync(symbols, snapshot, deadline, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The <b>market-wide</b> feed (TKT-foundation-078, 01 §10.9.15): one paged pass over
    /// <c>GET /disclosures?disclosureIndex={cursor}</c>. The cursor is the persisted market row;
    /// with none the walk bootstraps from the platform head's window
    /// (<see cref="DisclosuresSourceOptions.MarketBootstrapLookback"/>) — recorded as provenance,
    /// never as an empty market. Only rows whose owning company is a bound constituent are fetched
    /// and stored; the rest are counted as <see cref="OutsideBoundUniverse"/>.
    /// </summary>
    private async Task<SourcePayload<DisclosuresPayload>> FetchMarketAsync(
        IReadOnlyCollection<string> symbols,
        VykDirectorySnapshot snapshot,
        DateTimeOffset? deadline,
        CancellationToken cancellationToken)
    {
        var anchor = ParseIndex(snapshot.LastDisclosureIndex);
        var persisted = await GetMarketCursorAsync(cancellationToken).ConfigureAwait(false);
        long cursor;
        if (persisted is not null)
        {
            // A persisted cursor always wins: the run is incremental, exactly as before.
            cursor = persisted.Value;
        }
        else
        {
            if (anchor is null)
            {
                // Without the platform head there is no honest place to start, and the only
                // alternative — index 0 — is precisely the ~1.2 million -position re-walk that
                // never completed (run 208). Fail loudly instead of walking from zero.
                throw new SourceFetchException(
                    "VYK lastDisclosureIndex anchor unavailable; cannot bootstrap the market-wide disclosure cursor from the platform head (refusing to walk the archive from index 0).");
            }

            cursor = BootstrapIndex(anchor.Value, _options.MarketBootstrapLookback);
            _bootstrapped.Add(MarketCursorName);
            _logger?.LogInformation(
                "disclosures: market cursor bootstrapped at index {Cursor} (head {Head} − window {Window}); this is a start-of-read decision, not a claim that earlier disclosures do not exist (TKT-foundation-078)",
                cursor,
                anchor.Value,
                _options.MarketBootstrapLookback);
        }

        var byCompanyId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var symbol in symbols)
        {
            if (!snapshot.BySymbol.TryGetValue(symbol, out var companyId))
            {
                // Per-constituent identity isolation (FR-MDF-008/FR-MDF-012): a bound symbol the
                // directory cannot map is named and counted — it simply never matches a market row.
                LogIdentityUnresolved(symbol);
                _unresolvedSymbols.Add(symbol);
                continue;
            }

            byCompanyId[companyId] = symbol;
        }

        var walker = new VykDisclosureWalker(_client, _pacer, _logger, CursorDataType, Redact, DetailFetchTimeout, _clock);
        var walk = await walker.WalkMarketAsync<DisclosureFact>(
                persisted,
                cursor,
                anchor,
                _options.Path,
                _options.DetailPath,
                byCompanyId,
                IsFamily,
                (detailRaw, walkedSymbol, listed) => [DecodeDetail(detailRaw, walkedSymbol, listed)],
                WalkBudget,
                cancellationToken,
                deadline)
            .ConfigureAwait(false);

        _detailSkips.AddRange(walk.Skips);
        _outOfBoundRows = walk.OutOfBoundRows;
        _listPages += walk.PagesFetched;
        _truncated |= walk.Truncated;

        if (_outOfBoundRows > 0)
        {
            _logger?.LogInformation(
                "disclosures: {Count} market row(s) belong to companies outside the '{Index}' bound and were counted, not fetched ({ReasonCode})",
                _outOfBoundRows,
                _options.UniverseIndex,
                OutsideBoundUniverse);
        }

        if (walk.NextCursor is not null)
        {
            // Collected, not persisted (TKT-foundation-069): the job writes the facts first and
            // then calls PersistCursorsAsync, so a store failure cannot skip these indexes.
            _pendingMarketCursor = walk.NextCursor;
        }

        if (_logger is not null)
        {
            _logger.LogInformation(
                "disclosures: market walk done — {Facts} fact(s), {Pages} page(s), {Details} detail fetch(es), {OutOfBound} out-of-bound row(s), truncated {Truncated}, cursor {Cursor}",
                walk.Facts.Count,
                walk.PagesFetched,
                walk.DetailsFetched,
                walk.OutOfBoundRows,
                walk.Truncated,
                walk.NextCursor ?? "(unchanged)");
        }

        var payload = new DisclosuresPayload(SourceName, walk.Facts.ToList());
        return new SourcePayload<DisclosuresPayload>(SourceName, payload, walk.Raw);
    }

    /// <summary>
    /// The <b>company-scoped</b> feed (§10.9.15, pre-078 semantics): one bounded walk per bound
    /// constituent. A company with <b>no</b> persisted cursor bootstraps from the platform head's
    /// window (<see cref="DisclosuresSourceOptions.BootstrapLookback"/>) — recorded as provenance —
    /// instead of re-reading the archive from <see cref="DisclosuresSourceOptions.EarlyDisclosureIndex"/>;
    /// a persisted cursor is honoured unchanged (one incremental page), and a stale one resets once
    /// to the <b>same bounded window</b>, never to index 0.
    /// </summary>
    private async Task<SourcePayload<DisclosuresPayload>> FetchCompaniesAsync(
        IReadOnlyCollection<string> symbols,
        VykDirectorySnapshot snapshot,
        DateTimeOffset? deadline,
        CancellationToken cancellationToken)
    {
        var anchor = ParseIndex(snapshot.LastDisclosureIndex);
        var disclosures = new List<DisclosureFact>();
        var raw = new StringBuilder();

        foreach (var symbol in symbols)
        {
            // The pass's one wall clock (TKT-foundation-078): when it expires, stop starting new
            // companies. Every cursor collected so far is persisted after the loop, so completed
            // work is never lost — the next run resumes from the last started company's cursor.
            if (IsDeadlineExpired(deadline))
            {
                _truncated = true;
                _logger?.LogWarning(
                    "disclosures: company pass reached its {Duration} wall-clock bound after {Completed} cursor(s); the remaining constituents resume next run (TKT-foundation-078)",
                    _options.MaxWalkDuration,
                    _pendingCursors.Count);
                break;
            }

            if (!snapshot.BySymbol.TryGetValue(symbol, out var companyId))
            {
                // Per-constituent identity isolation (FR-MDF-008/FR-MDF-012, §10.9.10/§10.9.11):
                // the unmapped bound symbol is named, counted and skipped — never written under a
                // fabricated identity, and never a fail-stop that aborts the whole family walk.
                LogIdentityUnresolved(symbol);
                _unresolvedSymbols.Add(symbol);
                continue;
            }

            var cursor = await GetCursorAsync(symbol, cancellationToken).ConfigureAwait(false);

            // The walk's early index is the reset target *and* the start of a first pass. Both are
            // the bounded window whenever one is configured, so no path of this lane can start (or
            // restart) at the beginning of platform history (TKT-foundation-078). The anchor is
            // resolved differently per path: a first pass *needs* it, a persisted cursor does not
            // (the directory treats `/lastDisclosureIndex` as a secondary read, §10.9.11) — review
            // DR-3.
            string early;
            if (cursor is null)
            {
                early = ResolveBootstrapIndex(symbol, anchor);
                if (_options.BootstrapLookback > 0)
                {
                    _bootstrapped.Add(symbol);
                    _logger?.LogInformation(
                        "disclosures: {Symbol} had no persisted cursor; bootstrapping at index {Early} (head {Head} − window {Window}) — a start-of-read decision, not a claim that earlier disclosures do not exist (TKT-foundation-078)",
                        symbol,
                        early,
                        anchor,
                        _options.BootstrapLookback);
                }
            }
            else
            {
                early = ResolveResetIndex(symbol, anchor, cursor);
            }

            var (companyDisclosures, nextCursor, companyRaw, truncated, reset) = await FetchCompanyAsync(
                    symbol,
                    companyId,
                    cursor,
                    early,
                    deadline,
                    cancellationToken)
                .ConfigureAwait(false);

            _truncated |= truncated;
            disclosures.AddRange(companyDisclosures);
            raw.Append(companyRaw);
            if (nextCursor is not null)
            {
                _pendingCursors.Add((symbol, nextCursor));
            }
            else if (reset is not null)
            {
                // The re-walk was itself honest-empty (the gateway answered ER005 at the window
                // too). Leaving the stale value would repeat the identical two-call reset on every
                // run — the ticket's "must not trigger an infinite or repeated reset" pin. The
                // statements lane (063) clears the row; this lane does the same (review DR-5).
                _pendingClearedCursors.Add(symbol);
            }
        }

        // No cursor is persisted here (TKT-foundation-069): the job writes the facts first and
        // only then calls PersistCursorsAsync, so a store failure can never skip an index.

        var payload = new DisclosuresPayload(SourceName, disclosures);
        return new SourcePayload<DisclosuresPayload>(SourceName, payload, raw.ToString());
    }

    /// <summary>
    /// The start-of-read index of a <b>first pass</b> (a company with no persisted cursor): the
    /// bounded bootstrap window when one is configured, otherwise the configured
    /// <see cref="DisclosuresSourceOptions.EarlyDisclosureIndex"/> (the pre-078 behaviour).
    /// <para>
    /// A first pass <b>needs</b> the anchor: the only fallback, index 0, is the walk that never
    /// finishes, so a missing anchor fails loudly instead of starting at zero. A company that
    /// already has a cursor never reaches this method (review DR-3).
    /// </para>
    /// </summary>
    private string ResolveBootstrapIndex(string symbol, long? anchor)
    {
        if (_options.BootstrapLookback <= 0)
        {
            return _options.EarlyDisclosureIndex;
        }

        if (anchor is null)
        {
            // The anchor is a secondary read (01 §10.9.11) and may be unavailable. A first pass
            // cannot be bounded without it — and the only fallback, index 0, is the walk that
            // never finishes. Fail loudly rather than start at zero.
            _logger?.LogWarning(
                "disclosures: {Symbol} has no persisted cursor and the lastDisclosureIndex anchor is unavailable; the bootstrap window cannot be computed ({ReasonCode})",
                symbol,
                IngestFailureReasonForBootstrap);

            throw new SourceFetchException(
                $"VYK lastDisclosureIndex anchor unavailable; cannot bootstrap the disclosure cursor for '{symbol}' from the platform head (refusing to walk the archive from index 0).");
        }

        return BootstrapIndex(anchor.Value, _options.BootstrapLookback).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The <b>reset target</b> of a persisted cursor (the walk's one stale-cursor recovery): the
    /// bounded window when it can be computed, else the cursor's own index.
    /// <para>
    /// A persisted cursor must never depend on the anchor — the directory treats
    /// <c>/lastDisclosureIndex</c> as a secondary read (01 §10.9.11) and answers <c>null</c> when it
    /// is unavailable. Without the anchor the reset re-reads the cursor's own index, which is the
    /// gateway's honest end-of-list; the walk then ends honest-empty and the stale row is cleared,
    /// so the run neither fails nor repeats the reset (review DR-3/DR-5).
    /// </para>
    /// </summary>
    private string ResolveResetIndex(string symbol, long? anchor, string cursor)
    {
        if (_options.BootstrapLookback <= 0)
        {
            return _options.EarlyDisclosureIndex;
        }

        if (anchor is null)
        {
            _logger?.LogWarning(
                "disclosures: {Symbol} has a persisted cursor ({Cursor}) but the lastDisclosureIndex anchor is unavailable; a stale-cursor reset will re-read that index and, if the gateway answers ER005, clear the row — the anchor is a secondary read and never fails the run ({ReasonCode})",
                symbol,
                cursor,
                IngestFailureReasonForBootstrap);

            return cursor;
        }

        return BootstrapIndex(anchor.Value, _options.BootstrapLookback).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The named reason of an unbootstrappable first pass (see <see cref="ResolveBootstrapIndex"/>).</summary>
    private const string IngestFailureReasonForBootstrap = "BOOTSTRAP_ANCHOR_UNAVAILABLE";

    /// <summary>
    /// The window start: <paramref name="lookback"/> index positions below
    /// <paramref name="anchor"/> (never below zero). With a non-positive window the anchor itself is
    /// the start — "from the head", still never the beginning of history.
    /// </summary>
    internal static long BootstrapIndex(long anchor, long lookback)
    {
        var start = lookback > 0 ? anchor - lookback : anchor;
        return start > 0 ? start : 0;
    }

    /// <summary>
    /// One company's walk (cursor <b>inclusive</b>; the shared <see cref="VykDisclosureWalker"/>
    /// owns the ER005 honest-empty, stale-cursor reset, non-list body and strict-cursor semantics —
    /// 01 v1.5 §10.9.15), bounded by this walk's budget (TKT-foundation-078). The walk's
    /// <see cref="VykCompanyWalk{TFact}.Reset"/> rides back out so the caller can clear a stale
    /// cursor whose re-walk was honest-empty (review DR-5).
    /// </summary>
    private async Task<(List<DisclosureFact> Disclosures, string? Cursor, string Raw, bool Truncated, VykCursorReset? Reset)> FetchCompanyAsync(
        string symbol,
        string companyId,
        string? cursor,
        string earlyDisclosureIndex,
        DateTimeOffset? deadline,
        CancellationToken cancellationToken)
    {
        var walker = new VykDisclosureWalker(_client, _pacer, _logger, CursorDataType, Redact, DetailFetchTimeout, _clock);
        var walk = await walker.WalkAsync<DisclosureFact>(
                symbol,
                companyId,
                cursor,
                earlyDisclosureIndex,
                _options.Path,
                _options.DetailPath,
                IsFamily,
                (detailRaw, walkedSymbol, listed) => [DecodeDetail(detailRaw, walkedSymbol, listed)],
                cancellationToken,
                replayPersistedIndex: false,
                budget: WalkBudget,
                deadline: deadline)
            .ConfigureAwait(false);

        // Per-disclosure isolation (01 v1.5.40 §10.9.15): the details the walk could not fetch
        // ride out with the walk's facts so the job can count and quarantine them.
        _detailSkips.AddRange(walk.Skips);
        _listPages += walk.PagesFetched;

        if (walk.Truncated && _logger is not null)
        {
            _logger.LogWarning(
                "disclosures: {Symbol} walk stopped at the configured budget after {Pages} page(s) / {Details} detail fetch(es); the cursor resumes at {Cursor} (TKT-foundation-078)",
                symbol,
                walk.PagesFetched,
                walk.DetailsFetched,
                walk.NextCursor ?? "(unchanged)");
        }

        return (walk.Facts.ToList(), walk.NextCursor, walk.Raw, walk.Truncated, walk.Reset);
    }

    /// <summary>
    /// This walk's budget (TKT-foundation-078) — per <b>walk</b>, not per run: a
    /// <c>Feed = company</c> pass builds one per constituent, so its cost is
    /// constituents × (pages + details) and it is a multi-run backfill, not a single step (see the
    /// option docs, review DR-2).
    /// </summary>
    private VykWalkBudget WalkBudget => new(_options.MaxListPages, _options.MaxDetailFetches);

    /// <summary>
    /// True when the pass's wall-clock deadline has been reached. Read from the injected
    /// <see cref="TimeProvider"/>, so the deadline is deterministic on a fake clock and no real
    /// sleep is involved; a null deadline (a non-positive <see cref="DisclosuresSourceOptions.MaxWalkDuration"/>)
    /// never expires.
    /// </summary>
    private bool IsDeadlineExpired(DateTimeOffset? deadline) =>
        deadline is { } bound && _clock.GetUtcNow() >= bound;

    /// <summary>
    /// The configured per-item detail-fetch bound (TKT-foundation-061). Unset — the legacy
    /// single-resource doubles — it is the shared shipped default, so a hang cannot stall a walk
    /// even without configuration.
    /// </summary>
    private TimeSpan DetailFetchTimeout =>
        _kapOptions?.DetailFetchTimeout ?? TimeSpan.FromMilliseconds(KapSourceOptions.DefaultDetailFetchTimeoutMs);

    /// <summary>
    /// Decodes one VYK disclosure detail into the canonical metadata fact. The list row carries the
    /// disclosure's identity/type/company; the detail carries the <b>real</b> subject
    /// (<c>subject.tr</c>/<c>subject.en</c>), the publish stamp (<c>time</c>,
    /// <c>dd.MM.yyyy HH:mm:ss</c>, live-verified 2026-10-20) and the public <c>link</c>
    /// (<c>https://www.kap.org.tr/Bildirim/{index}</c>).
    /// </summary>
    private DisclosureFact DecodeDetail(string raw, string symbol, DisclosureInfo listed)
    {
        using var document = ParseDocument(raw);
        var root = document.RootElement;

        var fault = VykFault.TryRead(root);
        if (fault is not null)
        {
            throw new SourceFetchException($"VYK /disclosureDetail returned a fault ({fault.Code}): {Redact(fault.Message)}");
        }

        var index = ParseIndex(ReadString(root, "disclosureIndex")) ?? ParseIndex(listed.DisclosureIndex);
        if (index is null)
        {
            throw new SourcePayloadException(raw, new JsonException("VYK disclosure detail carries no disclosureIndex."));
        }

        var subject = ReadSubject(root);
        if (string.IsNullOrWhiteSpace(subject))
        {
            // Title is part of the canonical contract; a detail without it is malformed, not a
            // row with a null title (mirrors the corporate-actions time/eventType posture).
            throw new SourcePayloadException(raw, new JsonException("VYK disclosure detail carries no subject."));
        }

        var publishedOn = ParseDisclosureDate(ReadString(root, "time"));
        if (publishedOn is null)
        {
            throw new SourcePayloadException(raw, new JsonException("VYK disclosure detail carries no parseable time."));
        }

        var link = ReadString(root, "link");
        if (string.IsNullOrWhiteSpace(link))
        {
            throw new SourcePayloadException(raw, new JsonException("VYK disclosure detail carries no link."));
        }

        var disclosureType = ReadString(root, "disclosureType") ?? listed.DisclosureType;
        if (string.IsNullOrWhiteSpace(disclosureType))
        {
            throw new SourcePayloadException(raw, new JsonException("VYK disclosure detail carries no disclosureType."));
        }

        return new DisclosureFact(index.Value, symbol, disclosureType, publishedOn, subject, link);
    }

    /// <summary>The disclosure subject — <c>subject.tr</c>, falling back to <c>subject.en</c>
    /// (the list row's <c>title</c> is the company name, not the subject).</summary>
    private static string? ReadSubject(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("subject", out var subject)
            || subject.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var turkish = ReadString(subject, "tr");
        return string.IsNullOrWhiteSpace(turkish) ? ReadString(subject, "en") : turkish;
    }

    private bool IsFamily(DisclosureInfo disclosure) =>
        string.IsNullOrWhiteSpace(_options.DisclosureType)
        || string.Equals(disclosure.DisclosureType, _options.DisclosureType, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Logs one isolated bound symbol with the named reason (FR-MDF-008/FR-MDF-012): the symbol is
    /// listed and the run continues, so the skip is visible in the run's log trail even before the
    /// job folds it into the per-symbol stats.
    /// </summary>
    private void LogIdentityUnresolved(string symbol) =>
        _logger?.LogWarning(
            "disclosures: company {Symbol} could not be mapped to a VYK companyId; isolated and the loop continues ({ReasonCode})",
            symbol,
            VykCompanyDirectory.CompanyIdentityUnresolved);

    private async Task<string?> GetCursorAsync(string symbol, CancellationToken cancellationToken)
    {
        var instrumentId = await _db!.Instruments
            .Where(i => i.Symbol == symbol)
            .Select(i => (long?)i.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (instrumentId is null)
        {
            return null;
        }

        return await _db.CoverageMetadata
            .Where(c => c.Scope == InstrumentScope && c.InstrumentId == instrumentId && c.DataType == CursorDataType)
            .Select(c => c.Notes)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The persisted <b>market-wide</b> cursor (TKT-foundation-078): the highest market index the
    /// last completed pass reached, read from one <c>market</c>-scope row. Null when the lane has
    /// never completed a market pass — that is the only case in which the walk bootstraps.
    /// </summary>
    private async Task<long?> GetMarketCursorAsync(CancellationToken cancellationToken)
    {
        var notes = await _db!.CoverageMetadata
            .Where(c => c.Scope == MarketCursorScope
                && c.InstrumentId == null
                && c.FundId == null
                && c.DataType == MarketCursorDataType)
            .Select(c => c.Notes)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return ParseIndex(notes);
    }

    /// <summary>Persists the market-wide cursor in place (one row per lane, idempotent upsert).</summary>
    private async Task SetMarketCursorAsync(string cursor, CancellationToken cancellationToken)
    {
        var row = await _db!.CoverageMetadata
            .FirstOrDefaultAsync(
                c => c.Scope == MarketCursorScope
                    && c.InstrumentId == null
                    && c.FundId == null
                    && c.DataType == MarketCursorDataType,
                cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            _db.CoverageMetadata.Add(new CoverageMetadata
            {
                Scope = MarketCursorScope,
                InstrumentId = null,
                FundId = null,
                DataType = MarketCursorDataType,
                Notes = cursor,
            });
        }
        else
        {
            row.Notes = cursor;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SetCursorAsync(string symbol, string cursor, CancellationToken cancellationToken)
    {
        var instrumentId = await _db!.Instruments
            .Where(i => i.Symbol == symbol)
            .Select(i => (long?)i.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (instrumentId is null)
        {
            return;
        }

        var row = await _db.CoverageMetadata
            .FirstOrDefaultAsync(
                c => c.Scope == InstrumentScope && c.InstrumentId == instrumentId && c.DataType == CursorDataType,
                cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            _db.CoverageMetadata.Add(new CoverageMetadata
            {
                Scope = InstrumentScope,
                InstrumentId = instrumentId,
                DataType = CursorDataType,
                Notes = cursor,
            });
        }
        else
        {
            row.Notes = cursor;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Clears a bound symbol's persisted disclosures cursor (TKT-foundation-078 review DR-5): the
    /// outcome of a stale-cursor reset whose re-walk was itself honest-empty. Removing the row —
    /// rather than leaving the stale value — makes the next run bootstrap afresh instead of
    /// repeating the identical ER005 reset, exactly as 063's statements lane does
    /// (<c>KapDisclosureCursorStore.ClearAsync</c>). An unresolvable symbol, or one with no row,
    /// is a no-op.
    /// </summary>
    private async Task ClearCursorAsync(string symbol, CancellationToken cancellationToken)
    {
        var instrumentId = await _db!.Instruments
            .Where(i => i.Symbol == symbol)
            .Select(i => (long?)i.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (instrumentId is null)
        {
            return;
        }

        var row = await _db.CoverageMetadata
            .FirstOrDefaultAsync(
                c => c.Scope == InstrumentScope && c.InstrumentId == instrumentId && c.DataType == CursorDataType,
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return;
        }

        _db.CoverageMetadata.Remove(row);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger?.LogWarning(
            "disclosures: cleared the stale cursor of {Symbol} — its reset re-walk was honest-empty, so the row would otherwise repeat the same ER005 every run (TKT-foundation-078)",
            symbol);
    }

    private string Redact(string? message) => _kapOptions?.Redact(message) ?? message ?? string.Empty;

    private static long? ParseIndex(string? value) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ? index : null;

    /// <summary>The disclosure publish stamp <c>dd.MM.yyyy HH:mm:ss</c> (live-verified 2026-10-20).</summary>
    private static DateOnly? ParseDisclosureDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string[] formats = ["dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy"];
        return DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? DateOnly.FromDateTime(parsed)
            : null;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static JsonDocument ParseDocument(string raw)
    {
        try
        {
            return JsonDocument.Parse(raw);
        }
        catch (JsonException exception)
        {
            throw new SourcePayloadException(raw, exception);
        }
    }
}
