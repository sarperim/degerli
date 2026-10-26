using System.Text.Json;
using Degerli.Ingestion.Kap;
using Degerli.Ingestion.Sources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.Statements;

/// <summary>Route configuration for the <c>statements</c> KAP endpoint.</summary>
public sealed class StatementsSourceOptions
{
    public const string SectionName = "Ingestion:Statements";

    /// <summary>
    /// The VYK gateway source selector value (01 v1.5 §10.9.11) — the shipped default.
    /// </summary>
    public const string VykSource = "vyk";

    /// <summary>
    /// The KAP <b>public-site</b> source selector value (01 v1.5.56 §10.9.19, TKT-foundation-082).
    /// </summary>
    public const string KapPublicSource = "kap-public";

    /// <summary>
    /// Which source the statements lane reads (01 v1.5.56 §10.9.19, TKT-foundation-082): the VYK
    /// gateway (<see cref="VykSource"/>, the shipped default) or the KAP public site
    /// (<see cref="KapPublicSource"/>).
    /// <para>
    /// <b>Selecting a source is configuration, never a rewrite.</b> The VYK wiring stays intact and
    /// the public lane is opt-in: switching back is a config change, and an unrecognised value
    /// fails the run by name (<see cref="TryParseSource"/>) instead of silently running the other
    /// source under the wrong provenance.
    /// </para>
    /// </summary>
    public string Source { get; set; } = VykSource;

    /// <summary>
    /// The public-site lane's <b>bootstrap lookback</b> in months (01 v1.5.56 §10.9.19,
    /// TKT-foundation-082): how far back a company with <b>no persisted cursor</b> asks for
    /// reports. It is a <b>request</b>, recorded as such — the depth a pass actually achieved is
    /// recorded beside it (<c>kap-public-statements-depth</c>), so an unwalked range never reads
    /// as "no statements exist" (FR-MDF-010).
    /// </summary>
    public int KapPublicLookbackMonths { get; set; } = 24;

    /// <summary>
    /// How many days one public-site byCriteria list request covers (01 v1.5.56 §10.9.19). The
    /// public list is <b>date-windowed</b>, not index-paged, so the window is the unit of bounded
    /// listing: a narrower window is a smaller answer, at the cost of more list requests (each
    /// paced and counted against the pass's page ceiling).
    /// </summary>
    public int KapPublicWindowDays { get; set; } = 365;

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

    /// <summary>True when the configured <see cref="Source"/> selects the public-site lane.</summary>
    public bool KapPublicSelected => TryParseSource(Source, out var selected)
        && string.Equals(selected, KapPublicSource, StringComparison.Ordinal);

    /// <summary>
    /// Route template of the per-company KAP financial-statement resource. A
    /// <c>{symbol}</c> token is substituted in place by the company-scoped
    /// <see cref="StatementsSourceAdapter.FetchCompanyAsync"/>; a template without the token
    /// gets the symbol appended as a <c>symbol</c> query parameter (so the fixed canonical
    /// L2 double route still resolves).
    /// </summary>
    public string Path { get; set; } = "/statements";

    /// <summary>
    /// The index whose current constituents (<c>index_constituents</c>, <c>effective_to IS NULL</c>)
    /// the production per-company fetch loops over (01 v1.5 §10.9.2, FR-MDF-002). Its own key —
    /// deliberately independent of <c>Ingestion:Prices:UniverseIndex</c> (AD-14) so the two
    /// bounds flip separately as their pacing/coverage is verified. Launch default <c>XU030</c>;
    /// flipping to <c>XU100</c> later is a config-only change — no code.
    /// </summary>
    public string UniverseIndex { get; set; } = "XU030";

    /// <summary>
    /// Inter-request delay (milliseconds) pacing the strictly sequential per-company loop
    /// against the public endpoint (01 v1.5 §10.9.2). <c>0</c> disables the wait; L2 harnesses
    /// bind it to <c>0</c> so the loop is deterministic. For the VYK source (01 v1.5 §10.9.11)
    /// the shipped production value is <b>10000</b> — the free plan allows ~6 calls/min, so
    /// every gateway call in the run (members/list/detail/anchor) is paced, not just the
    /// per-company boundary.
    /// </summary>
    public int RequestDelayMs { get; set; } = 500;

    /// <summary>
    /// Route template of the VYK disclosure-detail resource (01 v1.5 §10.9.11); the
    /// <c>{disclosureIndex}</c> token is substituted with the listed disclosure's index and
    /// <c>?fileType=data</c> is appended.
    /// </summary>
    public string DetailPath { get; set; } = "/disclosureDetail/{disclosureIndex}";

    /// <summary>
    /// The disclosure <b>class</b> filter for the financial-report list (01 v1.5 §10.9.11;
    /// <c>FR</c> = Finansal Raporlar). The OpenAPI's own examples are ambiguous between
    /// <c>disclosureTypes</c> ("FR") and <c>disclosureClass</c> ("ODA"), but the class
    /// parameter is the financial-report filter (<c>FR</c>; <c>ODA</c> = material events,
    /// corroborated by independent KAP clients) — 024's ambiguity resolved by evidence, pinned
    /// live when a key is present.
    /// </summary>
    public string DisclosureClass { get; set; } = "FR";

    /// <summary>
    /// The disclosure index a company with no persisted cursor starts from (01 v1.5 §10.9.11):
    /// the first live pass is the backfill, so the achieved depth is whatever the source
    /// returns — never fabricated. Walked forward when a page fills.
    /// </summary>
    public string EarlyDisclosureIndex { get; set; } = "0";
}

/// <summary>
/// The result of one company-scoped fetch (01 v1.5 §10.9.10/§10.9.11): the decoded payload
/// plus the run's new per-company disclosure cursor (<c>null</c> when the source is not the
/// VYK gateway, or when nothing new was listed this run). The job persists a non-null cursor
/// per company (FR-MDF-010/011).
/// </summary>
public sealed record CompanyStatementFetch(SourcePayload<StatementsPayload> Source, string? Cursor)
{
    /// <summary>
    /// The disclosure details this company's walk isolated (01 v1.5.40/v1.5.42 §10.9.15,
    /// TKT-foundation-063): one un-fetchable attachment yields a recorded skip with the gateway's
    /// error verbatim, never a failed company and never a silent omission.
    /// </summary>
    public IReadOnlyList<VykDetailSkip> DetailSkips { get; init; } = [];

    /// <summary>
    /// The one stale-cursor reset this company's walk performed (01 v1.5.42 §10.9.15), or
    /// <c>null</c>. The job records it and persists its outcome (corrected cursor, or a cleared
    /// row when the re-walk was itself honest-empty).
    /// </summary>
    public VykCursorReset? CursorReset { get; init; }
}

/// <summary>
/// A bound symbol could not be resolved to a VYK <c>companyId</c> through the run's
/// <c>/members</c> directory (FR-MDF-008/FR-MDF-012, 01 v1.5 §10.9.11). The company is
/// marked failed with the named reason <c>COMPANY_IDENTITY_UNRESOLVED</c> — never dropped
/// silently, never written under a fabricated identity — and the loop continues.
/// </summary>
public sealed class CompanyIdentityUnresolvedException : Exception
{
    public CompanyIdentityUnresolvedException(string symbol)
        : base($"VYK company identity for '{symbol}' could not be resolved (COMPANY_IDENTITY_UNRESOLVED).")
        => Symbol = symbol;

    /// <summary>The bound BIST symbol whose VYK <c>companyId</c> could not be resolved.</summary>
    public string Symbol { get; }
}

/// <summary>
/// KAP statements adapter (FR-MDF-002): fetches and parses the financial-statement
/// payload. Two first-class shapes are accepted (01 v1.5 §10.9.5): the <b>real</b> KAP
/// XBRL fact envelope (<c>facts</c> array + <c>companyCode</c>, decoded by
/// <see cref="KapXbrlStatementDecoder"/>) and the canonical <c>statements</c> double
/// envelope (the L2 contract). The ETL mapping to the canonical chart of accounts lives in
/// <see cref="StatementMapper"/> (applied by the decoder) and persistence in
/// <see cref="FinancialStatementStore"/>. Every parsed payload carries provenance
/// <c>kap</c>.
/// </summary>
public sealed class StatementsSourceAdapter : ISourceAdapter<StatementsPayload>
{
    private readonly IKapSourceClient _client;
    private readonly StatementsSourceOptions _options;
    private readonly KapSourceOptions? _kapOptions;
    private readonly VykCompanyDirectory? _directory;
    private readonly KapRequestPacer? _pacer;
    private readonly ILogger<StatementsSourceAdapter>? _logger;

    public StatementsSourceAdapter(IKapSourceClient client, IOptions<StatementsSourceOptions> options)
        : this(client, options, kapOptions: null, directory: null, pacer: null, logger: null)
    {
    }

    public StatementsSourceAdapter(
        IKapSourceClient client,
        IOptions<StatementsSourceOptions> options,
        IOptions<KapSourceOptions>? kapOptions,
        VykCompanyDirectory? directory,
        KapRequestPacer? pacer,
        ILogger<StatementsSourceAdapter>? logger)
    {
        _client = client;
        _options = options.Value;
        _kapOptions = kapOptions?.Value;
        _directory = directory;
        _pacer = pacer;
        _logger = logger;
    }

    /// <summary>
    /// True when the VYK company-scoped source is active (01 v1.5 §10.9.11): a members route is
    /// configured and the directory seam is wired. Unset (the canonical/XBRL L2 doubles), the
    /// adapter keeps the single-resource per-company behavior of §10.9.10 unchanged.
    /// </summary>
    public bool VykEnabled => _directory?.Enabled == true;

    /// <inheritdoc />
    public string SourceName => KapXbrlStatementDecoder.SourceRef;

    /// <inheritdoc />
    public async Task<SourcePayload<StatementsPayload>> FetchAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var raw = await _client.GetStringAsync(_options.Path, cancellationToken).ConfigureAwait(false);

        using var document = ParseDocument(raw);
        if (KapXbrlStatementDecoder.IsEnvelope(document))
        {
            // The real KAP XBRL fact envelope (01 v1.5 §10.9.5): collapse the tag variance
            // onto the canonical chart of accounts and carry the provenance `kap`.
            var payload = DecodeEnvelope(raw, document);
            return new SourcePayload<StatementsPayload>(payload.SourceRef, payload, raw);
        }

        // The canonical source-double envelope (the L2 golden contract, §10.9.7).
        return KapPayload.Parse<StatementsPayload>(raw);
    }

    /// <summary>
    /// Company-scoped fetch (01 v1.5 §10.9.2/§10.9.10). When the VYK source is enabled
    /// (01 v1.5 §10.9.11) this resolves the symbol → <c>companyId</c>, fetches the company's
    /// financial-report disclosure list and each new disclosure detail, decodes them, and
    /// reports the run's new per-company disclosure cursor. Otherwise it keeps the
    /// single-resource behavior (a <c>{symbol}</c> token substituted in the configured path, or
    /// the symbol appended as a query parameter so the fixed canonical L2 double route still
    /// resolves); envelope detection stays inside the adapter. This overload reports no cursor.
    /// </summary>
    public async Task<SourcePayload<StatementsPayload>> FetchCompanyAsync(
        string symbol,
        CancellationToken cancellationToken = default) =>
        (await FetchCompanyAsync(symbol, fromDisclosureIndex: null, cancellationToken).ConfigureAwait(false)).Source;

    /// <summary>
    /// Company-scoped fetch with the incremental cursor (01 v1.5 §10.9.11). The returned
    /// <see cref="CompanyStatementFetch.Cursor"/> is the highest disclosure index seen this run
    /// (<c>null</c> when nothing new was listed), which the job persists as the per-company
    /// cursor. The persistence, validation, quarantine and per-company isolation stay job-side.
    /// </summary>
    public async Task<CompanyStatementFetch> FetchCompanyAsync(
        string symbol,
        string? fromDisclosureIndex,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        if (VykEnabled)
        {
            return await FetchVykCompanyAsync(symbol, fromDisclosureIndex, cancellationToken).ConfigureAwait(false);
        }

        var raw = await _client
            .GetStringAsync(ResolveFetchPath(_options.Path, symbol), cancellationToken)
            .ConfigureAwait(false);
        return new CompanyStatementFetch(DecodeCompanyPayload(raw, symbol), Cursor: null);
    }

    /// <summary>
    /// Decodes one company's single-resource body (the real KAP XBRL fact envelope or the
    /// canonical double). A body for a different company is skipped before decode so no
    /// cross-company fact is ever produced or quarantined.
    /// </summary>
    private static SourcePayload<StatementsPayload> DecodeCompanyPayload(string raw, string symbol)
    {
        using var document = ParseDocument(raw);

        if (KapXbrlStatementDecoder.IsEnvelope(document))
        {
            // The real KAP XBRL fact envelope (01 v1.5 §10.9.5).
            if (!CompanyMatches(document, symbol))
            {
                return new SourcePayload<StatementsPayload>(
                    KapXbrlStatementDecoder.SourceRef,
                    EmptyPayload(symbol),
                    raw);
            }

            var payload = DecodeEnvelope(raw, document);
            return new SourcePayload<StatementsPayload>(payload.SourceRef, payload, raw);
        }

        // The canonical source-double envelope (the L2 golden contract, §10.9.7).
        var parsed = KapPayload.Parse<StatementsPayload>(raw);
        return new SourcePayload<StatementsPayload>(parsed.SourceRef, FilterBySymbol(parsed.Payload, symbol), raw);
    }

    /// <summary>
    /// The VYK company-scoped fetch (01 v1.5 §10.9.11): resolve the company, walk its
    /// financial-report disclosures from the cursor through the <b>shared</b>
    /// <see cref="VykDisclosureWalker"/> (ER005 honest-empty, one stale-cursor reset, per-detail
    /// isolation and the bounded per-item fetch — TKT-foundation-063, the 061 semantics this lane
    /// now reuses instead of a second walk), decode each new disclosure's detail and advance the
    /// cursor. A source downtime is a <see cref="SourceFetchException"/> (no quarantine); a
    /// malformed body is a <see cref="SourcePayloadException"/>.
    /// </summary>
    private async Task<CompanyStatementFetch> FetchVykCompanyAsync(
        string symbol,
        string? fromDisclosureIndex,
        CancellationToken cancellationToken)
    {
        var snapshot = await _directory!.GetAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot.Failed)
        {
            // A mapping-transport failure degrades honestly: no fabricated mapping, no rows.
            throw new SourceFetchException(
                $"VYK members directory unavailable; cannot resolve companyId for '{symbol}' ({snapshot.FailureReason}).");
        }

        if (!snapshot.BySymbol.TryGetValue(symbol, out var companyId))
        {
            throw new CompanyIdentityUnresolvedException(symbol);
        }

        var walker = new VykDisclosureWalker(
            _client,
            _pacer,
            _logger,
            StatementsJob.Code,
            Redact,
            DetailFetchTimeout);
        var walk = await walker.WalkAsync(
                symbol,
                companyId,
                fromDisclosureIndex,
                _options.EarlyDisclosureIndex,
                BuildDisclosureListRoute(),
                _options.DetailPath,
                // The list route is already filtered to financial reports (disclosureClass=FR):
                // every listed row is this family's (01 v1.5 §10.9.11).
                _ => true,
                (detailRaw, walkedSymbol, _) => DecodeVykDetail(detailRaw, walkedSymbol),
                cancellationToken,
                // The statements cursor names the last index whose facts were persisted, so the
                // inclusive re-decode keeps an identical re-run an honest `unchanged` (the
                // 026/024 idempotency golden) without a second walk.
                replayPersistedIndex: true)
            .ConfigureAwait(false);

        var payload = new StatementsPayload(
            KapXbrlStatementDecoder.SourceRef,
            symbol,
            VykStatementDecoder.DefaultUnitScale,
            VykStatementDecoder.DefaultCurrency,
            walk.Facts.ToList());
        return new CompanyStatementFetch(
            new SourcePayload<StatementsPayload>(KapXbrlStatementDecoder.SourceRef, payload, walk.Raw),
            walk.NextCursor)
        {
            DetailSkips = walk.Skips,
            CursorReset = walk.Reset,
        };
    }

    /// <summary>
    /// The configured per-item detail-fetch bound (TKT-foundation-061/063). Unset — the legacy
    /// single-resource doubles — it is the shared shipped default, so a hang cannot stall a walk
    /// even without configuration.
    /// </summary>
    private TimeSpan DetailFetchTimeout =>
        _kapOptions?.DetailFetchTimeout ?? TimeSpan.FromMilliseconds(KapSourceOptions.DefaultDetailFetchTimeoutMs);

    /// <summary>
    /// The statements disclosure-list route with its <c>disclosureClass</c> filter applied
    /// (01 v1.5 §10.9.11). The shared walker appends <c>disclosureIndex</c> and <c>companyId</c>.
    /// </summary>
    private string BuildDisclosureListRoute() =>
        string.IsNullOrWhiteSpace(_options.DisclosureClass)
            ? _options.Path
            : AppendQuery(_options.Path, "disclosureClass", _options.DisclosureClass);

    /// <summary>
    /// Decodes one VYK disclosure detail into canonical statement facts (01 v1.5 §10.9.11).
    /// A gateway <b>fault</b> is a <see cref="SourceFetchException"/> — the shared walker isolates
    /// it as a per-detail skip (01 v1.5.40/v1.5.42 §10.9.15); a structurally malformed body stays
    /// a <see cref="SourcePayloadException"/> (the <c>UNPARSEABLE_PAYLOAD</c> class), which is
    /// never isolated.
    /// </summary>
    private IEnumerable<StatementFact> DecodeVykDetail(string raw, string symbol)
    {
        using var document = ParseDocument(raw);
        var root = document.RootElement;

        var fault = VykFault.TryRead(root);
        if (fault is not null)
        {
            throw new SourceFetchException($"VYK /disclosureDetail returned a fault ({fault.Code}): {Redact(fault.Message)}");
        }

        if (!VykStatementDecoder.IsEnvelope(document))
        {
            throw new SourcePayloadException(raw, new JsonException("Body is not a VYK disclosure-detail envelope."));
        }

        try
        {
            return VykStatementDecoder.Decode(document, symbol).Statements;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException or OverflowException)
        {
            throw new SourcePayloadException(raw, exception);
        }
    }


    /// <summary>
    /// Pipes a gateway-supplied fault string through the configured credential redaction
    /// (SEC-1): a future fault echoing the credential must not survive into a thrown message
    /// or a log line.
    /// </summary>
    private string Redact(string? message) => _kapOptions?.Redact(message) ?? message ?? string.Empty;

    /// <summary>True when the real envelope's <c>companyCode</c> names the requested company.</summary>
    private static bool CompanyMatches(JsonDocument document, string symbol) =>
        document.RootElement.TryGetProperty("companyCode", out var code)
        && code.ValueKind == JsonValueKind.String
        && string.Equals(code.GetString(), symbol, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Narrows a canonical double payload to the requested company. The double is a single
    /// company's body, so a bound member whose request returns another company's body yields an
    /// empty statement set (never another company's facts under this member's identity).
    /// </summary>
    private static StatementsPayload FilterBySymbol(StatementsPayload payload, string symbol) =>
        string.Equals(payload.Symbol, symbol, StringComparison.OrdinalIgnoreCase)
            ? payload
            : payload with { Statements = [] };

    private static StatementsPayload EmptyPayload(string symbol) =>
        new(KapXbrlStatementDecoder.SourceRef, symbol, null, null, []);

    /// <summary>
    /// Resolves the per-company route: a <c>{symbol}</c> token in the configured path template
    /// is substituted in place; a template without it gets the symbol appended as a <c>symbol</c>
    /// query parameter (the fixed canonical double route is still reachable). Mirrors the prices
    /// adapter's resolution posture (§10.9.2).
    /// </summary>
    private static string ResolveFetchPath(string template, string symbol)
    {
        var hasSymbol = template.Contains("{symbol}", StringComparison.Ordinal);
        var path = template.Replace("{symbol}", Uri.EscapeDataString(symbol), StringComparison.Ordinal);
        return hasSymbol ? path : AppendQuery(path, "symbol", symbol);
    }

    private static string AppendQuery(string path, string key, string value)
    {
        var separator = path.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{path}{separator}{key}={Uri.EscapeDataString(value)}";
    }

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

    /// <summary>
    /// Decodes the real KAP XBRL fact envelope, surfacing a structurally malformed body as
    /// <see cref="SourcePayloadException"/> (the <c>UNPARSEABLE_PAYLOAD</c> class,
    /// FR-MDF-012) exactly as <see cref="KapPayload.Parse{TPayload}"/> does for the canonical
    /// envelope. The decode reads required coordinates, parses Turkish-locale numbers and
    /// dates, so a missing field (<see cref="JsonException"/>), an unparseable number/date
    /// (<see cref="FormatException"/>, <see cref="OverflowException"/>) or an empty argument
    /// (<see cref="ArgumentException"/>) is a payload failure — never an unhandled crash that
    /// aborts the run without a quarantine row.
    /// </summary>
    private static StatementsPayload DecodeEnvelope(string raw, JsonDocument document)
    {
        try
        {
            return KapXbrlStatementDecoder.Decode(document);
        }
        catch (Exception exception) when (
            exception is JsonException or FormatException or ArgumentException or OverflowException)
        {
            throw new SourcePayloadException(raw, exception);
        }
    }
}
