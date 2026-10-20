using System.Globalization;
using System.Text;
using System.Text.Json;
using Degerli.Ingestion.Kap;
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
    /// </summary>
    public string UniverseIndex { get; set; } = "XU030";

    /// <summary>
    /// The disclosure index a company with no persisted cursor starts from (the first pass is the
    /// backfill; the achieved depth is whatever the source returns — never fabricated).
    /// </summary>
    public string EarlyDisclosureIndex { get; set; } = "0";

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
/// <b>Failure posture (fail-fast, honest)</b> — identical to the corporate-actions rewire: a
/// transport failure/HTTP error/gateway fault is a <see cref="SourceFetchException"/> (the run is
/// recorded <c>failed</c>, never a fabricated empty success), except a <c>400</c> on a <b>list</b>
/// request whose body is the gateway's <c>ER005</c> envelope, which is end-of-list (an honest
/// empty, §10.9.15); a malformed body is a
/// <see cref="SourcePayloadException"/> (the job quarantines <c>UNPARSEABLE_PAYLOAD</c>); an
/// unmapped bound symbol is a <see cref="CompanyIdentityUnresolvedException"/>. Cursors are
/// persisted at the end of the adapter loop, before the job's store write (§10.9.15's accepted
/// residual: a post-fetch store failure can skip the indexes the cursor covers).
/// </para>
/// </summary>
public sealed class DisclosuresSourceAdapter : ISourceAdapter<DisclosuresPayload>
{
    /// <summary>The <c>coverage_metadata.data_type</c> of this family's per-instrument cursor.</summary>
    internal const string CursorDataType = "kap-disclosures-cursor";

    private const string InstrumentScope = "instrument";

    private readonly IKapSourceClient _client;
    private readonly DisclosuresSourceOptions _options;
    private readonly KapSourceOptions? _kapOptions;
    private readonly VykCompanyDirectory? _directory;
    private readonly KapRequestPacer? _pacer;
    private readonly UniverseSyncStore? _universe;
    private readonly DegerliDbContext? _db;
    private readonly ILogger<DisclosuresSourceAdapter>? _logger;

    public DisclosuresSourceAdapter(IKapSourceClient client, IOptions<DisclosuresSourceOptions> options)
        : this(client, options, kapOptions: null, directory: null, pacer: null, universe: null, db: null, logger: null)
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
        ILogger<DisclosuresSourceAdapter>? logger)
    {
        _client = client;
        _options = options.Value;
        _kapOptions = kapOptions?.Value;
        _directory = directory;
        _pacer = pacer;
        _universe = universe;
        _db = db;
        _logger = logger;
    }

    /// <summary>True when the VYK company-scoped source is active (§10.9.11).</summary>
    public bool VykEnabled => _directory?.Enabled == true && _universe is not null && _db is not null;

    /// <inheritdoc />
    public string SourceName => "kap";

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

    /// <summary>The VYK run (§10.9.15): one paced pass over the bound index's constituents.</summary>
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

        if (_logger is not null)
        {
            _logger.LogInformation(
                "disclosures: walking {Count} current constituents of {Index} via the VYK gateway",
                symbols.Count,
                _options.UniverseIndex);
        }

        var disclosures = new List<DisclosureFact>();
        var raw = new StringBuilder();
        var cursors = new List<(string Symbol, string Cursor)>();

        foreach (var symbol in symbols)
        {
            if (!snapshot.BySymbol.TryGetValue(symbol, out var companyId))
            {
                throw new CompanyIdentityUnresolvedException(symbol);
            }

            var cursor = await GetCursorAsync(symbol, cancellationToken).ConfigureAwait(false);
            var (companyDisclosures, nextCursor, companyRaw) = await FetchCompanyAsync(symbol, companyId, cursor, cancellationToken)
                .ConfigureAwait(false);

            disclosures.AddRange(companyDisclosures);
            raw.Append(companyRaw);
            if (nextCursor is not null)
            {
                cursors.Add((symbol, nextCursor));
            }
        }

        foreach (var (symbol, cursor) in cursors)
        {
            await SetCursorAsync(symbol, cursor, cancellationToken).ConfigureAwait(false);
        }

        var payload = new DisclosuresPayload(SourceName, disclosures);
        return new SourcePayload<DisclosuresPayload>(SourceName, payload, raw.ToString());
    }

    /// <summary>
    /// One company's walk (cursor <b>inclusive</b>; the shared <see cref="VykDisclosureWalker"/>
    /// owns the ER005 honest-empty, stale-cursor reset, non-list body and strict-cursor semantics —
    /// 01 v1.5 §10.9.15).
    /// </summary>
    private async Task<(List<DisclosureFact> Disclosures, string? Cursor, string Raw)> FetchCompanyAsync(
        string symbol,
        string companyId,
        string? cursor,
        CancellationToken cancellationToken)
    {
        var walker = new VykDisclosureWalker(_client, _pacer, _logger, CursorDataType, Redact);
        var walk = await walker.WalkAsync<DisclosureFact>(
                symbol,
                companyId,
                cursor,
                _options.EarlyDisclosureIndex,
                _options.Path,
                _options.DetailPath,
                IsFamily,
                (detailRaw, walkedSymbol, listed) => [DecodeDetail(detailRaw, walkedSymbol, listed)],
                cancellationToken)
            .ConfigureAwait(false);

        return (walk.Facts.ToList(), walk.NextCursor, walk.Raw);
    }

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
