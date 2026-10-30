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

namespace Degerli.Ingestion.CorporateActions;

/// <summary>
/// Route configuration for <c>corporate-actions</c> (FR-MDF-004). The shipped routes are the
/// <b>live-verified</b> MKK VYK gateway routes (01 v1.5 §10.9.11/§10.9.15): corporate actions are
/// the "<c>CA</c>" disclosure family listed by <c>GET /disclosures</c>, and each action's
/// structured payload is the disclosure detail (<c>GET /disclosureDetail/{index}?fileType=data</c>,
/// whose <c>eventType</c> names the action). The former placeholder <c>/corporate-actions</c> path
/// never existed on the gateway and is gone.
/// </summary>
public sealed class CorporateActionsSourceOptions
{
    public const string SectionName = "Ingestion:CorporateActions";

    /// <summary>
    /// The VYK disclosure-list route (01 v1.5 §10.9.15) — <b>live-verified</b> 2026-10-20. When the
    /// VYK company-scoped source is disabled (§10.9.10) the same setting is the single-resource
    /// route of the legacy canonical double, exactly as before this rewire.
    /// </summary>
    public string Path { get; set; } = "/disclosures";

    /// <summary>
    /// The VYK disclosure-detail route (live-verified 2026-10-20); the
    /// <c>{disclosureIndex}</c> token is substituted with the listed disclosure's index and
    /// <c>?fileType=data</c> is appended.
    /// </summary>
    public string DetailPath { get; set; } = "/disclosureDetail/{disclosureIndex}";

    /// <summary>
    /// The index whose current constituents the run loops over (01 v1.5 §10.9.2/§10.9.15). Its
    /// own key — independent of the prices and statements bounds (AD-14) — so the three fact
    /// families flip their coverage separately. Launch default <c>XU030</c>.
    /// </summary>
    public string UniverseIndex { get; set; } = "XU030";

    /// <summary>
    /// The disclosure index a company with no persisted cursor starts from (the first pass is the
    /// backfill, so the achieved depth is whatever the source returns — never fabricated).
    /// </summary>
    public string EarlyDisclosureIndex { get; set; } = "0";

    /// <summary>
    /// The listed row's <c>disclosureType</c> this adapter keeps — <c>CA</c> (Çeşitli / corporate
    /// actions family, live-verified 2026-10-20). The family filter is applied <b>client-side</b>
    /// on the listed row: the gateway's <c>disclosureTypes</c> query parameter is verified to work,
    /// but a <i>filtered</i> empty set answers <b>HTTP 400 <c>ER005</c></b> rather than an empty
    /// list, which would turn the ordinary "this company has no corporate actions" into a source
    /// failure. The unfiltered company list always returns the company's rows, so filtering here
    /// keeps the honest-empty case honest.
    /// </summary>
    public string DisclosureType { get; set; } = "CA";
}

/// <summary>
/// KAP corporate-actions adapter (FR-MDF-004), rewired onto the MKK VYK gateway
/// (01 v1.5 §10.9.15, the §10.9.11 source contract).
/// <para>
/// The <c>corporate-actions</c> job asks the adapter for one date's payload, so the
/// company-scoped loop lives here (the job files are outside this ticket): the adapter resolves
/// the configured index's current constituents, maps each symbol to the gateway's
/// <c>companyId</c> through the run's <c>/members</c> directory, walks that company's disclosure
/// list from its persisted cursor, fetches each new <c>CA</c> disclosure's detail and decodes the
/// gateway's <c>eventType</c>/<c>time</c>/<c>flatData</c> onto canonical <see cref="CorporateActionFact"/>
/// rows. Every gateway call is paced by the run's <see cref="KapRequestPacer"/>.
/// </para>
/// <para>
/// <b>Failure posture (honest, per-item).</b> A transport failure, an HTTP error or a gateway
/// fault on the <b>list</b> route is a <see cref="SourceFetchException"/> (the run is recorded
/// <c>failed</c> — the failure ledger, no fabricated empty success), except a <c>400</c> on a
/// <b>list</b> request whose body is the gateway's <c>ER005</c> envelope, which is end-of-list (an
/// honest empty, §10.9.15); a structurally malformed detail body is a
/// <see cref="SourcePayloadException"/> (the job quarantines <c>UNPARSEABLE_PAYLOAD</c>).
/// <b>An un-fetchable disclosure <i>detail</i> is isolated per disclosure</b> (01 v1.5.40
/// §10.9.15, TKT-foundation-061 — the 046 per-constituent doctrine one level down): the walk
/// records the skip with the gateway's error verbatim
/// (<see cref="VykDisclosureWalker.DisclosureDetailUnfetchable"/> + the disclosure index), keeps
/// walking, and the job counts and quarantines it so the run ends <c>partial</c> — never a
/// whole-run failure for one bad attachment (the 2026-10-22 HTTP 500 on
/// <c>/disclosureDetail/1185709</c> that fail-stopped the chain), and never a silent omission. A
/// detail that does not answer inside the configured per-item timeout takes the same skip path
/// (the 90-minute hang class). A bound
/// symbol the directory cannot map is <b>isolated per constituent</b> (01 v1.5 §10.9.15, the
/// §10.9.10/§10.9.11 per-company isolation doctrine, FR-MDF-012): the loop names it
/// <see cref="VykCompanyDirectory.CompanyIdentityUnresolved"/>, skips it and continues, and the
/// job records it in the run's per-symbol stats (see <see cref="IVykIdentityIsolationReporter"/>) —
/// never written under a fabricated identity, never a whole-run fail-stop.
/// </para>
/// <para>
/// <b>The cursor is a checkpoint, not a walk product (TKT-foundation-069, §10.9.15).</b> The walk
/// <b>collects</b> each company's next cursor and stops there; the job persists it through
/// <see cref="PersistCursorsAsync"/> only <b>after</b> its fact write (and the skip evidence)
/// succeeded — the 063/statements shape. A store failure therefore leaves every cursor exactly
/// where the last durable write left it, so the next run re-walks the same indexes and re-emits
/// their facts (the idempotent store reports them <c>unchanged</c>). No run can advance a cursor
/// past facts that were never written; the accepted residual this replaces is closed.
/// </para>
/// </summary>
public sealed class CorporateActionsSourceAdapter : ISourceAdapter<CorporateActionsPayload>, IVykIdentityIsolationReporter, IDisclosureIsolationReporter
{
    /// <summary>The <c>coverage_metadata.data_type</c> of this family's per-instrument cursor.</summary>
    internal const string CursorDataType = "kap-corporate-actions-cursor";

    private const string InstrumentScope = "instrument";

    private readonly IKapSourceClient _client;
    private readonly CorporateActionsSourceOptions _options;
    private readonly KapSourceOptions? _kapOptions;
    private readonly VykCompanyDirectory? _directory;
    private readonly KapRequestPacer? _pacer;
    private readonly UniverseSyncStore? _universe;
    private readonly DegerliDbContext? _db;
    private readonly ILogger<CorporateActionsSourceAdapter>? _logger;
    private readonly List<string> _unresolvedSymbols = [];
    private readonly List<VykDetailSkip> _detailSkips = [];
    private readonly List<(string Symbol, string Cursor)> _pendingCursors = [];

    public CorporateActionsSourceAdapter(
        IKapSourceClient client,
        IOptions<CorporateActionsSourceOptions> options)
        : this(client, options, kapOptions: null, directory: null, pacer: null, universe: null, db: null, logger: null)
    {
    }

    public CorporateActionsSourceAdapter(
        IKapSourceClient client,
        IOptions<CorporateActionsSourceOptions> options,
        IOptions<KapSourceOptions>? kapOptions,
        VykCompanyDirectory? directory,
        KapRequestPacer? pacer,
        UniverseSyncStore? universe,
        DegerliDbContext? db,
        ILogger<CorporateActionsSourceAdapter>? logger)
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

    /// <summary>
    /// True when the VYK company-scoped source is active (§10.9.11): a members route is configured
    /// and the loop's seams are wired. Unset (the canonical L2 doubles), the adapter keeps the
    /// single-resource behavior unchanged.
    /// </summary>
    public bool VykEnabled => _directory?.Enabled == true && _universe is not null && _db is not null;

    /// <inheritdoc />
    public string SourceName => "kap";

    /// <inheritdoc />
    public IReadOnlyList<string> UnresolvedSymbols => _unresolvedSymbols;

    /// <summary>
    /// The disclosure details the last walk isolated (01 v1.5.40 §10.9.15, the per-disclosure
    /// half of the family-isolation doctrine): one un-fetchable attachment yields a recorded skip
    /// with the gateway's error, never a failed run (TKT-foundation-061).
    /// </summary>
    public IReadOnlyList<VykDetailSkip> DetailSkips => _detailSkips;

    /// <summary>
    /// True when the last walk collected cursor work that is not yet durably persisted
    /// (TKT-foundation-069): the job calls <see cref="PersistCursorsAsync"/> after its fact write.
    /// </summary>
    public bool HasPendingCursors => _pendingCursors.Count > 0;

    /// <summary>
    /// Persists the walk's per-instrument cursors (01 v1.5 §10.9.11, TKT-foundation-069) —
    /// <b>called by the job after the fact write succeeded</b>, never by the walk. The pending set
    /// is taken and cleared before the writes, so a second call is a no-op; a failure surfaces out
    /// of the job and fails the run, and the cursors it did not reach stay where they were — the
    /// next run re-walks those indexes and the already-written facts come back `unchanged`. An
    /// unresolvable symbol is a no-op in the setter.
    /// </summary>
    public async Task PersistCursorsAsync(CancellationToken cancellationToken = default)
    {
        var pending = _pendingCursors.ToList();
        _pendingCursors.Clear();

        foreach (var (symbol, cursor) in pending)
        {
            await SetCursorAsync(symbol, cursor, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<SourcePayload<CorporateActionsPayload>> FetchAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        if (!VykEnabled)
        {
            var legacyRaw = await _client.GetStringAsync(_options.Path, cancellationToken).ConfigureAwait(false);
            return KapPayload.Parse<CorporateActionsPayload>(legacyRaw);
        }

        return await FetchVykAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The VYK run (§10.9.15): one paced pass over the bound index's current constituents, each
    /// company walked from its own persisted cursor. Cursors advance only once every company
    /// succeeded, so a failed run leaves no company skipped by a later run.
    /// </summary>
    private async Task<SourcePayload<CorporateActionsPayload>> FetchVykAsync(CancellationToken cancellationToken)
    {
        var symbols = await _universe!.GetCurrentConstituentSymbolsAsync(_options.UniverseIndex, cancellationToken)
            .ConfigureAwait(false);
        if (symbols.Count == 0)
        {
            // An empty bound is not an empty success: nothing was fetched, so nothing may be
            // reported as ingested (01 v1.5 §10.9.10).
            throw new SourceFetchException(
                $"Index '{_options.UniverseIndex}' has no current constituents; a corporate-actions run over an empty bound is a failure, not an empty success.");
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
                "corporate-actions: walking {Count} current constituents of {Index} via the VYK gateway",
                symbols.Count,
                _options.UniverseIndex);
        }

        var actions = new List<CorporateActionFact>();
        var raw = new StringBuilder();
        _unresolvedSymbols.Clear();
        _detailSkips.Clear();
        _pendingCursors.Clear();

        foreach (var symbol in symbols)
        {
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
            var (companyActions, nextCursor, companyRaw) = await FetchCompanyAsync(symbol, companyId, cursor, cancellationToken)
                .ConfigureAwait(false);

            actions.AddRange(companyActions);
            raw.Append(companyRaw);
            if (nextCursor is not null)
            {
                _pendingCursors.Add((symbol, nextCursor));
            }
        }

        // No cursor is persisted here (TKT-foundation-069): the job writes the facts first and
        // only then calls PersistCursorsAsync, so a store failure can never skip an index.

        var payload = new CorporateActionsPayload(SourceName, actions);
        return new SourcePayload<CorporateActionsPayload>(SourceName, payload, raw.ToString());
    }

    /// <summary>
    /// One company's walk: list its disclosures from <paramref name="cursor"/> (inclusive), keep
    /// the <c>CA</c> rows and decode each new row's detail. The shared
    /// <see cref="VykDisclosureWalker"/> owns the ER005 honest-empty / stale-cursor-reset /
    /// non-list-body / strict-cursor semantics (01 v1.5 §10.9.15).
    /// </summary>
    private async Task<(List<CorporateActionFact> Actions, string? Cursor, string Raw)> FetchCompanyAsync(
        string symbol,
        string companyId,
        string? cursor,
        CancellationToken cancellationToken)
    {
        var walker = new VykDisclosureWalker(_client, _pacer, _logger, CursorDataType, Redact, DetailFetchTimeout);
        var walk = await walker.WalkAsync(
                symbol,
                companyId,
                cursor,
                _options.EarlyDisclosureIndex,
                _options.Path,
                _options.DetailPath,
                IsFamily,
                (detailRaw, walkedSymbol, _) => DecodeDetail(detailRaw, walkedSymbol),
                cancellationToken)
            .ConfigureAwait(false);

        // Per-disclosure isolation (01 v1.5.40 §10.9.15): the details the walk could not fetch
        // ride out with the walk's facts so the job can count and quarantine them.
        _detailSkips.AddRange(walk.Skips);

        return (walk.Facts.ToList(), walk.NextCursor, walk.Raw);
    }

    /// <summary>
    /// The configured per-item detail-fetch bound (TKT-foundation-061). Unset — the legacy
    /// single-resource doubles — it is the shared shipped default, so a hang cannot stall a walk
    /// even without configuration.
    /// </summary>
    private TimeSpan DetailFetchTimeout =>
        _kapOptions?.DetailFetchTimeout ?? TimeSpan.FromMilliseconds(KapSourceOptions.DefaultDetailFetchTimeoutMs);

    /// <summary>
    /// Decodes one VYK disclosure detail into canonical corporate actions. The gateway's
    /// <c>eventType</c> is the action's type code (live-verified 2026-10-20: <c>SPAF</c> debt issue,
    /// <c>GAI</c> buy-back, <c>SA</c> capital increase/decrease, <c>IT</c> issue limit, <c>GK</c>
    /// general assembly, <c>KP</c> dividend), the publish <c>time</c> places it on the calendar, and
    /// <c>flatData[0].content.object</c> is the structured terms document retained verbatim.
    /// </summary>
    private IEnumerable<CorporateActionFact> DecodeDetail(string raw, string symbol)
    {
        using var document = ParseDocument(raw);
        var root = document.RootElement;

        var fault = VykFault.TryRead(root);
        if (fault is not null)
        {
            throw new SourceFetchException($"VYK /disclosureDetail returned a fault ({fault.Code}): {Redact(fault.Message)}");
        }

        var eventType = ReadString(root, "eventType");
        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new SourcePayloadException(raw, new JsonException("VYK disclosure detail carries no eventType."));
        }

        var publishedOn = ParseDisclosureDate(ReadString(root, "time"));
        if (publishedOn is null)
        {
            // ActionDate is part of the canonical contract; inventing one from the run date would
            // fabricate a calendar position, so an undated action is an unparseable payload.
            throw new SourcePayloadException(raw, new JsonException("VYK disclosure detail carries no parseable time."));
        }

        yield return new CorporateActionFact(symbol, eventType.Trim(), publishedOn.Value, ReadTerms(root));
    }

    /// <summary>The structured terms document (<c>flatData[0].content.object</c>), or an
    /// <see cref="JsonValueKind.Undefined"/> element when the gateway returned none (stored as
    /// NULL — never an invented empty object).</summary>
    private static JsonElement ReadTerms(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("flatData", out var flatData)
            || flatData.ValueKind != JsonValueKind.Array)
        {
            return default;
        }

        foreach (var entry in flatData.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.Object
                || !content.TryGetProperty("object", out var terms))
            {
                continue;
            }

            return terms.Clone();
        }

        return default;
    }

    /// <summary>True when the listed row belongs to this adapter's disclosure family.</summary>
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
            "corporate-actions: company {Symbol} could not be mapped to a VYK companyId; isolated and the loop continues ({ReasonCode})",
            symbol,
            VykCompanyDirectory.CompanyIdentityUnresolved);

    /// <summary>The per-instrument disclosure cursor (01 v1.5 §10.9.15), stored in the existing
    /// <c>coverage_metadata</c> table — outside <c>CoverageDataTypes.InstrumentScoped</c>, so the
    /// no-silent-gaps reconcile never clobbers it.</summary>
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
