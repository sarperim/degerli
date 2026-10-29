using Degerli.Ingestion.Scheduling;

namespace Degerli.Ingestion.KapPublic;

/// <summary>
/// The recorded <b>access posture</b> of the KAP public site for automated reads
/// (01 v1.5.53 §10.9.19, TKT-foundation-079; the investigation's OQ-2). It is the
/// <b>operator's recorded decision</b> over the captured evidence, not a guess: the
/// evidence (both <c>robots.txt</c> probe responses and the paths probed for a terms
/// document) lives in <c>RealSourceCatalog</c> under the <c>kap-public-*</c> names and in
/// §10.9.19.
/// <para>
/// This is a <b>checkable refusal</b>, never prose: the client refuses to issue a single
/// request unless the posture is <see cref="Allowed"/> — <see cref="KapPublicAccessRefusedException"/> —
/// so a posture cannot be silently ignored by a walk (TC-MDF-104 (iii)).
/// </para>
/// </summary>
public enum KapPublicAccessPosture
{
    /// <summary>
    /// No posture has been recorded (the code default). The client refuses to issue requests
    /// with <c>ACCESS_POSTURE_NOT_VERIFIED</c> — a posture nobody chose is not a permission.
    /// </summary>
    NotVerified = 0,

    /// <summary>
    /// The operator's recorded decision permits automated reads under the politeness
    /// contract. The shipped configuration chooses this <b>explicitly</b>; it is never
    /// inherited from the code default.
    /// </summary>
    Allowed = 1,

    /// <summary>A prohibition is recorded; the client refuses to issue requests.</summary>
    Forbidden = 2,
}

/// <summary>
/// The KAP <b>public-site</b> source's configuration (01 v1.5.53 §10.9.19,
/// TKT-foundation-079) — section <c>Ingestion:KapPublic</c>.
/// <para>
/// This is a <b>website, not a contracted API</b> (the investigation's constraint §1): the
/// shipped posture is one paced request at a time, never parallel, with a realistic
/// User-Agent, bounded work and a wall-clock deadline. It is a <b>separate source</b> from
/// the VYK gateway (§10.9.11): its own base address, its own client, its own pacing, its
/// own options. Nothing here is shared with <c>Ingestion:Kap</c> — even the URI-join
/// helpers are lane-local (<c>KapPublicUri</c>) — and the VYK adapter is untouched by it.
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
    /// request the lane makes starts at least this long after the previous request started
    /// (start-to-start, on the injected clock — a slow response does not let the next start
    /// come any sooner), and the lane is strictly single-flight (the client holds one flight
    /// gate, so no two requests are ever in flight together). A value ≤ 0 disables the wait —
    /// an L2 harness binding.
    /// </summary>
    public const int DefaultRequestDelayMs = 3_000;

    /// <summary>
    /// The shipped User-Agent. It is <b>browser-shaped</b> (the shape a real browser sends,
    /// which the site's edge expects) with an <b>honest self-identification token appended</b>
    /// (<c>Degerli/0.1</c>) — the lane identifies itself rather than impersonating a human
    /// visitor. Whether the edge accepts a bare bot-only UA was <b>not</b> probed (probing with
    /// a deliberately lying UA is not an acceptable politeness experiment); the recorded capture
    /// set was taken with this shape.
    /// <para>
    /// <b>The parenthetical is WAF-blocked (087 live finding, probe-verified).</b> The site's
    /// edge answers <c>…DegerliBot/0.1 (kap.org.tr public-site reader; polite, paced,
    /// single-flight)</c> with <b>HTTP 666 on every route</b>, while this comment-free form —
    /// the same honest token, no parentheses — passes (HTTP 200 on <c>search/combined</c> and
    /// <c>byCriteria</c>, one request at a time). The value below is the live-verified passing
    /// string: this pin is why the blocked spelling cannot silently return
    /// (<c>TC-MDF-103(a)</c>/<c>(d)</c>). The politeness posture (≥ 3 s pacing, single-flight)
    /// is unaffected by the UA shape.
    /// </para>
    /// </summary>
    public const string DefaultUserAgent =
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/124.0 Safari/537.36 Degerli/0.1";

    /// <summary>
    /// The shipped <b>export-endpoint</b> pacing floor: <b>12,000 ms</b> (TKT-foundation-100,
    /// 01 v1.5.76 §10.9.19). The excel export route carries a rolling per-IP request quota —
    /// HTTP <c>429</c> "Request Limit Exceeded", empty body, <b>no</b> <c>Retry-After</c> — that
    /// accumulates under heavy use, while the <c>byCriteria</c>/<c>search</c> routes are
    /// unaffected. The floor was verified live by the 2026-10-28/29 relay runs, which serialized
    /// export fetches at exactly this interval: the general pace
    /// (<see cref="DefaultRequestDelayMs"/>) is a politeness measure for a website, this one is
    /// the source's own quota being honored.
    /// <para>
    /// It applies to <b>export/detail</b> requests only, <b>raises</b> the general pace on that
    /// path (never lowers it) and never touches the list/identity routes. The lane stays
    /// single-flight, and the quota is treated as <b>authoritative</b>: this is a longer wait,
    /// never a way around the limit (no rotation, no parallel clients, no header games).
    /// </para>
    /// <para>
    /// <c>≤ 0</c> disables the floor, and so does a general <see cref="RequestDelayMs"/> of
    /// <c>≤ 0</c> (the L2 harness binding that turns pacing off altogether): a harness must not
    /// be slowed by a floor whose pacing contract it has just disabled.
    /// </para>
    /// </summary>
    public const int DefaultExportRequestDelayMs = 12_000;

    /// <summary>
    /// The shipped <b>quota backoff</b>: <b>8,000 ms</b> — the conservative fixed wait used when a
    /// <c>429</c> carries <b>no</b> usable <c>Retry-After</c> header (the observed answer: empty
    /// body, no header). A <c>Retry-After</c> that <i>is</i> present is honored verbatim instead.
    /// <para>
    /// The wait is bounded by the caller's own per-item bound (<see cref="RequestTimeout"/> or
    /// <see cref="DetailFetchTimeout"/>): a wait that is not shorter than that bound is never
    /// slept — the request stops there and the walk truncates honestly under
    /// <see cref="KapPublicReasonCodes.WalkQuotaBackoff"/> instead of outliving its caller. One
    /// bounded re-issue per request, never a retry ladder. <c>≤ 0</c> keeps the shipped value.
    /// </para>
    /// </summary>
    public const int DefaultQuotaBackoffMs = 8_000;

    /// <summary>
    /// Whether the lane's <see cref="System.Net.Http.HttpClient"/> is built over a
    /// <b>browser-shaped</b> TLS handler (<see cref="DefaultTlsMinimumVersion"/>,
    /// <see cref="DefaultTlsCipherSuites"/>, <see cref="DefaultTlsAlpnProtocols"/>) — the durable
    /// half of the 2026-10-28 <b>TLS-ClientHello fingerprint</b> finding: after a burst the edge
    /// refused the .NET stack at the transport level while curl and python urllib succeeded from
    /// the same host, so the discriminator is the runtime's fingerprint, not the honest UA.
    /// <para>
    /// <b>An honest fingerprint, not a disguise:</b> the UA token (<see cref="DefaultUserAgent"/>)
    /// is unchanged, no header lies, the lane stays single-flight and paced, and the quota is
    /// honored. Configurable so an operator can stand the shaping down without a rebuild;
    /// <c>false</c> reproduces the pre-ticket stack exactly.
    /// </para>
    /// </summary>
    public const bool DefaultTlsShapeEnabled = true;

    /// <summary>
    /// The shipped TLS floor: <b>Tls12</b> — a browser's floor. The floor is a <i>minimum</i>: the
    /// enabled set is the named protocol <b>and everything above it</b> (TLS 1.2 + 1.3 shipped), so
    /// TLS 1.0/1.1/SSL3 are never offered. See <see cref="DefaultTlsShapeEnabled"/>.
    /// </summary>
    public const string DefaultTlsMinimumVersion = "Tls12";

    /// <summary>
    /// The shipped <b>browser-shaped</b> ALPN list: <c>h2</c> then <c>http/1.1</c> — the protocol
    /// order a current browser offers, and part of what makes the ClientHello browser-shaped.
    /// See <see cref="DefaultTlsShapeEnabled"/>.
    /// </summary>
    public const string DefaultTlsAlpnProtocols = "h2,http/1.1";

    /// <summary>
    /// The shipped <b>browser-shaped</b> cipher-suite order (a current browser's ClientHello
    /// order: the three TLS 1.3 suites first, then the ECDHE AEAD TLS 1.2 suites, then the two
    /// ECDHE CBC suites; key agreement is ECDHE throughout, so there is no RSA key transport).
    /// <para>
    /// Presented only where the platform allows a cipher-suite policy (.NET supports
    /// <see cref="System.Net.Security.CipherSuitesPolicy"/> on Linux; elsewhere the property is
    /// not supported and the platform's own default order stands — recorded, never faked).
    /// </para>
    /// </summary>
    public const string DefaultTlsCipherSuites =
        "TLS_AES_128_GCM_SHA256,TLS_AES_256_GCM_SHA384,TLS_CHACHA20_POLY1305_SHA256," +
        "TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256,TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256," +
        "TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384,TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384," +
        "TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256," +
        "TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256," +
        "TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA,TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA";

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
    /// The minimum gap between consecutive <b>export/detail</b> requests, in ms — the quota floor
    /// (see <see cref="DefaultExportRequestDelayMs"/>). It raises, never lowers, the general pace
    /// on the export path; list/identity requests keep <see cref="RequestDelayMs"/>. A value
    /// <c>≤ 0</c> — or a <see cref="RequestDelayMs"/> of <c>≤ 0</c> — disables it.
    /// </summary>
    public int ExportRequestDelayMs { get; set; } = DefaultExportRequestDelayMs;

    /// <summary>
    /// The conservative fixed wait used when a <c>429</c> carries no usable <c>Retry-After</c>
    /// (see <see cref="DefaultQuotaBackoffMs"/>). <c>≤ 0</c> keeps the shipped value.
    /// </summary>
    public int QuotaBackoffMs { get; set; } = DefaultQuotaBackoffMs;

    /// <summary>Whether the browser-shaped TLS handler is used (see <see cref="DefaultTlsShapeEnabled"/>).</summary>
    public bool TlsShapeEnabled { get; set; } = DefaultTlsShapeEnabled;

    /// <summary>The TLS minimum version (see <see cref="DefaultTlsMinimumVersion"/>).</summary>
    public string TlsMinimumVersion { get; set; } = DefaultTlsMinimumVersion;

    /// <summary>The ALPN offer, comma separated (see <see cref="DefaultTlsAlpnProtocols"/>).</summary>
    public string TlsAlpnProtocols { get; set; } = DefaultTlsAlpnProtocols;

    /// <summary>The cipher-suite order, comma separated (see <see cref="DefaultTlsCipherSuites"/>).</summary>
    public string TlsCipherSuites { get; set; } = DefaultTlsCipherSuites;

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
    /// The recorded access posture of the public site (the OQ-2 gate). The <b>code default is
    /// <see cref="KapPublicAccessPosture.NotVerified"/></b> — a posture nobody chose is not a
    /// permission — and the client refuses to issue requests unless the posture is
    /// <see cref="KapPublicAccessPosture.Allowed"/>; the shipped <c>appsettings.json</c> selects
    /// <c>Allowed</c> <b>explicitly</b> (the user-ratified 2026-10-25 decision). The gate is
    /// behaviour, not prose.
    /// </summary>
    public KapPublicAccessPosture AutomatedAccess { get; set; } = KapPublicAccessPosture.NotVerified;

    /// <summary>True only when the recorded posture permits automated reads (<c>Allowed</c>).</summary>
    public bool AllowsAutomatedAccess => AutomatedAccess == KapPublicAccessPosture.Allowed;

    /// <summary>The configured per-item detail timeout as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan DetailFetchTimeout =>
        TimeSpan.FromMilliseconds(DetailFetchTimeoutMs > 0 ? DetailFetchTimeoutMs : DefaultDetailFetchTimeoutMs);

    /// <summary>The configured general request timeout as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan RequestTimeout =>
        TimeSpan.FromMilliseconds(RequestTimeoutMs > 0 ? RequestTimeoutMs : DefaultRequestTimeoutMs);

    /// <summary>
    /// The configured quota backoff as a <see cref="TimeSpan"/> (<c>≤ 0</c> keeps the shipped value).
    /// </summary>
    public TimeSpan QuotaBackoff =>
        TimeSpan.FromMilliseconds(QuotaBackoffMs > 0 ? QuotaBackoffMs : DefaultQuotaBackoffMs);

    /// <summary>
    /// The gap that applies to one request of <paramref name="kind"/> — the general pace, or the
    /// export quota floor when that is longer (see <see cref="DefaultExportRequestDelayMs"/>).
    /// <para>
    /// A disabled general pace (<see cref="RequestDelayMs"/> <c>≤ 0</c>) disables the floor too:
    /// that binding is the L2 harness's "no pacing at all", and a floor surviving it would slow
    /// every harness run by 12 s per export for no politeness gain (the lane issues requests as
    /// fast as it can either way).
    /// </para>
    /// </summary>
    public int DelayMsFor(KapPublicRequestKind kind)
    {
        if (RequestDelayMs <= 0)
        {
            return 0;
        }

        return kind == KapPublicRequestKind.Export && ExportRequestDelayMs > RequestDelayMs
            ? ExportRequestDelayMs
            : RequestDelayMs;
    }


    /// <summary>
    /// The report-export route for one public-site disclosure index. The index is the
    /// <b>public site's own</b> space — never a VYK <c>disclosureIndex</c>.
    /// </summary>
    public string ReportExportRoute(long disclosureIndex) =>
        ReportExportPath.Replace(
            "{disclosureIndex}",
            disclosureIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
}
