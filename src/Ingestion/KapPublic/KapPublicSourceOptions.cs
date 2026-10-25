using Degerli.Ingestion.Scheduling;

namespace Degerli.Ingestion.KapPublic;

/// <summary>
/// The recorded <b>access posture</b> of the KAP public site for automated reads
/// (01 v1.5.53 §10.9.19, TKT-foundation-079; the investigation's OQ-2). It is the
/// <b>operator's recorded judgement</b> over the captured evidence, not a guess: the
/// evidence (the verbatim <c>robots.txt</c> probe and the paths probed for a terms
/// document) lives in <c>RealSourceCatalog</c> under the <c>kap-public-*</c> names and in
/// §10.9.19.
/// <para>
/// This is a <b>checkable refusal</b>, never prose: when the posture is
/// <see cref="Forbidden"/> the client refuses to issue a single request —
/// <see cref="KapPublicAccessRefusedException"/> — so a forbidden reading cannot be
/// silently ignored by a walk (TC-MDF-104 (iii)).
/// </para>
/// </summary>
public enum KapPublicAccessPosture
{
    /// <summary>No prohibition is recorded; reads proceed under the politeness constraints.</summary>
    Allowed = 0,

    /// <summary>A prohibition is recorded; the client refuses to issue requests.</summary>
    Forbidden = 1,
}

/// <summary>
/// The KAP <b>public-site</b> source's configuration (01 v1.5.53 §10.9.19,
/// TKT-foundation-079) — section <c>Ingestion:KapPublic</c>.
/// <para>
/// This is a <b>website, not a contracted API</b> (the investigation's constraint §1): the
/// shipped posture is one paced request at a time, never parallel, with a realistic
/// User-Agent, bounded work and a wall-clock deadline. It is a <b>separate source</b> from
/// the VYK gateway (§10.9.11): its own base address, its own client, its own pacing, its
/// own options. Nothing here is shared with <c>Ingestion:Kap</c>, and the VYK adapter is
/// untouched by it.
/// </para>
/// <para>
/// The lane's evidence class is <see cref="SourceName"/> = <c>kap-public</c> — never
/// <c>kap</c>. Public-site HTML/JSON is a different evidence class from VYK JSON, so a
/// stored row's provenance always answers which source produced it (FR-MDF-008).
/// </para>
/// <para>
/// <b>Identifier spaces (never mixed).</b> The public site's <c>disclosureIndex</c> is its
/// own current space (ASELS 2026-06 FR = <c>1643141</c> — captured 2026-10-25) and its
/// company identifier is an opaque oid (ASELS <c>4028e4a1413b7ef401413bc2251e0047</c>);
/// neither is the VYK gateway's <c>disclosureIndex</c> space (head <c>1231017</c>) or its
/// numeric <c>companyId</c> (ASELS <c>866</c>). Anything this lane persists is stamped in
/// its own space and its own store.
/// </para>
/// </summary>
public sealed class KapPublicSourceOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Ingestion:KapPublic";

    /// <summary>The DI key of this lane's own <see cref="System.Net.Http.HttpClient"/>.</summary>
    public const string ClientKey = "kap-public";

    /// <summary>The public site's base address (live-verified 2026-10-25).</summary>
    public const string DefaultBaseUrl = "https://www.kap.org.tr";

    /// <summary>
    /// The lane's provenance name — <b>never</b> the VYK lane's <c>kap</c> (01 v1.5.53
    /// §10.9.19). Every fact this lane writes carries it as its source reference.
    /// </summary>
    public const string SourceName = "kap-public";

    /// <summary>
    /// The shipped inter-request delay: <b>3,000 ms</b>. This is a public website: every
    /// request the lane makes waits at least this long after the previous one, and the lane
    /// is strictly single-flight (the client holds one flight gate, so no two requests are
    /// ever in flight together). A value ≤ 0 disables the wait — an L2 harness binding.
    /// </summary>
    public const int DefaultRequestDelayMs = 3_000;

    /// <summary>
    /// The shipped User-Agent. It is <b>browser-shaped</b> (the shape a real browser sends,
    /// which the site's edge expects) with an <b>honest crawler token appended</b> — the lane
    /// identifies itself rather than impersonating a human visitor. Whether the edge accepts
    /// a bare bot-only UA was <b>not</b> probed (probing with a deliberately lying UA is not
    /// an acceptable politeness experiment); the recorded capture set was taken with this
    /// shape.
    /// </summary>
    public const string DefaultUserAgent =
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/124.0 Safari/537.36 DegerliBot/0.1 (kap.org.tr public-site reader; polite, " +
        "paced, single-flight)";

    /// <summary>
    /// The shipped per-walk <b>list-page</b> ceiling: <b>40</b>. 40 pages × 3 s of pacing =
    /// 120 s = <b>2 min</b> of list pacing for one walk. A walk stops at the bound, records
    /// the truncation and resumes from the last <b>completed</b> unit — so a walk is finite
    /// even when the archive (or a date window) is larger than the bound.
    /// </summary>
    public const int DefaultMaxListPages = 40;

    /// <summary>
    /// The shipped per-walk <b>detail</b> (report/detail fetch) ceiling: <b>200</b>.
    /// <b>Nominal</b> cost: 200 × 3 s = 600 s = 10 min. Pages (≤ 2 min) + details (≤ 10 min)
    /// ≈ <b>12 min</b> for one walk at the shipped pace. This bounds <i>work</i>, not wall
    /// clock: a stalled detail is bounded per item at
    /// <see cref="DefaultDetailFetchTimeoutMs"/> (60 s), so a degraded site could drive
    /// 200 × 60 s = 12,000 s ≈ <b>3 h 20 min</b> out of one walk — far past the scheduler's
    /// 120-min per-step bound (<see cref="IngestionSchedulerOptions.DefaultMaxStepDuration"/>).
    /// That is why the count is paired with <see cref="DefaultMaxWalkDuration"/>: the walk
    /// self-truncates at the deadline and resumes, instead of being watchdog-cancelled with
    /// every completed unit discarded (the 078/§10.9.17 class).
    /// </summary>
    public const int DefaultMaxDetailFetches = 200;

    /// <summary>
    /// The shipped per-item <b>detail</b> fetch timeout: <b>60 s</b>. The live full report is
    /// ~1.8 MB of HTML (captured 2026-10-25: 1,819,412 bytes for disclosure 1643141), so 60 s
    /// is ~20× the pace at which the capture arrived while still refusing to let one stalled
    /// detail eat a walk. Pacing is <b>not</b> counted against it — the walker paces first,
    /// then starts the timeout. A non-positive value keeps the shipped default.
    /// </summary>
    public const int DefaultDetailFetchTimeoutMs = 60_000;

    /// <summary>
    /// The shipped general (list/identity) request timeout: <b>30 s</b> — the list body is a
    /// few KiB of JSON (captured: 4,862 bytes for the ASELS FR window).
    /// </summary>
    public const int DefaultRequestTimeoutMs = 30_000;

    /// <summary>
    /// The shipped <b>per-pass wall-clock</b> bound: <b>90 minutes</b>, strictly below the
    /// scheduler's 120-minute per-step bound
    /// (<see cref="IngestionSchedulerOptions.DefaultMaxStepDuration"/>), leaving ≈30 min for
    /// the job's store write inside its step. The deadline is checked before every list and
    /// detail request on the injected <see cref="TimeProvider"/> (no real sleeps, so an L2
    /// fake clock is deterministic). On expiry the walk records <c>truncated</c>, stops at the
    /// last unit it <b>completed</b> and resumes exactly there — an honest bounded pass, never
    /// a failure and never a lost unit. A value ≤ 0 disables the deadline.
    /// </summary>
    public static readonly TimeSpan DefaultMaxWalkDuration = TimeSpan.FromMinutes(90);

    /// <summary>The public-site base address.</summary>
    public string BaseUrl { get; set; } = DefaultBaseUrl;

    /// <summary>The User-Agent sent with every request (see <see cref="DefaultUserAgent"/>).</summary>
    public string UserAgent { get; set; } = DefaultUserAgent;

    /// <summary>The minimum gap between consecutive requests, in ms (0 disables the wait).</summary>
    public int RequestDelayMs { get; set; } = DefaultRequestDelayMs;

    /// <summary>
    /// The disclosure-list route — <c>POST</c> with the byCriteria body
    /// (live-verified 2026-10-25: rich rows for a date window, filterable by
    /// <c>disclosureClass</c> and <c>mkkMemberOidList</c>, where an <b>empty</b>
    /// <c>mkkMemberOidList</c> answers the <b>market-wide</b> list — the OQ-1 probe below).
    /// </summary>
    public string ByCriteriaPath { get; set; } = "/tr/api/disclosure/members/byCriteria";

    /// <summary>
    /// The full-report export route (live-verified 2026-10-25). The response declares
    /// <c>application/vnd.ms-excel</c> but the body is <b>HTML</b> (~1.8 MB of XBRL-tagged
    /// tables) — the lane therefore detects the payload <b>by shape</b>, never by the declared
    /// content type (TC-MDF-103 (iv)).
    /// </summary>
    public string ReportExportPath { get; set; } = "/tr/api/notification/export/excel/{disclosureIndex}";

    /// <summary>
    /// The identity-resolution route — <c>POST</c> <c>{"keyword":"&lt;ticker&gt;"}</c>, answering
    /// the company hit with its public <c>memberOrFundOid</c> (live-verified 2026-10-25;
    /// consumed by TKT-foundation-080, whose mapping store keeps the three identifier spaces
    /// apart).
    /// </summary>
    public string IdentitySearchPath { get; set; } = "/tr/api/search/combined";

    /// <summary>The company page route (identity/directory cross-check; TKT-foundation-080).</summary>
    public string CompanyPagePath { get; set; } = "/tr/sirket/{ticker}";

    /// <summary>The <c>robots.txt</c> path the ToS gate probes (recorded, never trusted blindly).</summary>
    public string RobotsPath { get; set; } = "/robots.txt";

    /// <summary>The per-walk list-page ceiling (TKT-foundation-079; ≤ 0 disables the bound).</summary>
    public int MaxListPages { get; set; } = DefaultMaxListPages;

    /// <summary>The per-walk detail-fetch ceiling (≤ 0 disables the bound).</summary>
    public int MaxDetailFetches { get; set; } = DefaultMaxDetailFetches;

    /// <summary>The per-item detail fetch timeout in ms (≤ 0 keeps the shipped default).</summary>
    public int DetailFetchTimeoutMs { get; set; } = DefaultDetailFetchTimeoutMs;

    /// <summary>The general request timeout in ms (≤ 0 keeps the shipped default).</summary>
    public int RequestTimeoutMs { get; set; } = DefaultRequestTimeoutMs;

    /// <summary>The per-pass wall-clock bound (≤ 0 disables the deadline).</summary>
    public TimeSpan MaxWalkDuration { get; set; } = DefaultMaxWalkDuration;

    /// <summary>
    /// The recorded access posture of the public site (the OQ-2 gate). <see cref="KapPublicAccessPosture.Forbidden"/>
    /// makes the client refuse to issue requests — the gate is behaviour, not prose.
    /// </summary>
    public KapPublicAccessPosture AutomatedAccess { get; set; } = KapPublicAccessPosture.Allowed;

    /// <summary>True when the recorded posture permits automated reads.</summary>
    public bool AllowsAutomatedAccess => AutomatedAccess != KapPublicAccessPosture.Forbidden;

    /// <summary>The configured per-item detail timeout as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan DetailFetchTimeout =>
        TimeSpan.FromMilliseconds(DetailFetchTimeoutMs > 0 ? DetailFetchTimeoutMs : DefaultDetailFetchTimeoutMs);

    /// <summary>The configured general request timeout as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan RequestTimeout =>
        TimeSpan.FromMilliseconds(RequestTimeoutMs > 0 ? RequestTimeoutMs : DefaultRequestTimeoutMs);

    /// <summary>
    /// The report-export route for one public-site disclosure index. The index is the
    /// <b>public site's own</b> space — never a VYK <c>disclosureIndex</c>.
    /// </summary>
    public string ReportExportRoute(long disclosureIndex) =>
        ReportExportPath.Replace(
            "{disclosureIndex}",
            disclosureIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.Ordinal);

    /// <summary>The company page route for one ticker.</summary>
    public string CompanyPageRoute(string ticker) =>
        CompanyPagePath.Replace("{ticker}", ticker, StringComparison.Ordinal);
}
