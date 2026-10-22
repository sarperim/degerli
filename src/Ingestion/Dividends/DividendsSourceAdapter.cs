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

namespace Degerli.Ingestion.Dividends;

/// <summary>
/// Route configuration for <c>dividends</c> (FR-MDF-003). The shipped routes are the
/// <b>live-verified</b> MKK VYK gateway routes (01 v1.5 §10.9.11/§10.9.15): a dividend is a
/// corporate-action (<c>CA</c>) disclosure whose detail body carries <c>eventType=KP</c>
/// ("Kar Payı", live-verified 2026-10-20 on a real BİM dividend notice). The former placeholder
/// <c>/dividends</c> path never existed on the gateway and is gone.
/// </summary>
public sealed class DividendsSourceOptions
{
    public const string SectionName = "Ingestion:Dividends";

    /// <summary>
    /// The VYK disclosure-list route (live-verified 2026-10-20). When the VYK company-scoped
    /// source is disabled (§10.9.10) the same setting is the single-resource route of the legacy
    /// canonical double, exactly as before this rewire.
    /// </summary>
    public string Path { get; set; } = "/disclosures";

    /// <summary>
    /// The VYK disclosure-detail route (live-verified 2026-10-20); the
    /// <c>{disclosureIndex}</c> token is substituted and <c>?fileType=data</c> appended.
    /// </summary>
    public string DetailPath { get; set; } = "/disclosureDetail/{disclosureIndex}";

    /// <summary>
    /// The index whose current constituents the run loops over (01 v1.5 §10.9.2/§10.9.15) — its
    /// own key, independent of the prices and statements bounds (AD-14). Launch default
    /// <c>XU030</c>.
    /// </summary>
    public string UniverseIndex { get; set; } = "XU030";

    /// <summary>
    /// The disclosure index a company with no persisted cursor starts from (the first pass is the
    /// backfill; the achieved depth is whatever the source returns — never fabricated).
    /// </summary>
    public string EarlyDisclosureIndex { get; set; } = "0";

    /// <summary>
    /// The listed row's <c>disclosureType</c> kept by the walk: <c>CA</c> (dividends are disclosed
    /// in the corporate-actions family, live-verified 2026-10-20). Applied client-side — a
    /// gateway-filtered empty set answers <b>HTTP 400 <c>ER005</c></b> instead of an empty list
    /// (§10.9.15), so filtering on the listed row keeps the honest-empty case honest.
    /// </summary>
    public string DisclosureType { get; set; } = "CA";

    /// <summary>
    /// The gateway <c>eventType</c> that marks a dividend notice (live-verified 2026-10-20:
    /// <c>KP</c> = Kar Payı Dağıtımı, DTO <c>KPY51DTO</c>).
    /// </summary>
    public string DividendEventType { get; set; } = "KP";
}

/// <summary>
/// KAP dividends adapter (FR-MDF-003), rewired onto the MKK VYK gateway
/// (01 v1.5 §10.9.15, the §10.9.11 source contract).
/// <para>
/// The <c>dividends</c> job asks the adapter for one date's payload, so the company-scoped loop
/// lives here (the job files are outside this ticket): the adapter resolves the configured index's
/// current constituents, maps each symbol to the gateway's <c>companyId</c>, walks that company's
/// disclosure list from its persisted cursor, fetches each new <c>CA</c> detail and decodes the
/// <c>eventType=KP</c> bodies into canonical <see cref="DividendFact"/> rows — one row per cash
/// installment, from the detail's own <c>cashDividendList</c> /
/// <c>cashDividendPaymentDateList</c> tables (real shape captured 2026-10-20). Every gateway call
/// is paced by the run's <see cref="KapRequestPacer"/>.
/// </para>
/// <para>
/// <b>Failure posture (fail-fast, honest)</b> — identical to the corporate-actions rewire: a
/// transport failure/HTTP error/gateway fault is a <see cref="SourceFetchException"/> (the run is
/// recorded <c>failed</c>, never a fabricated empty success), except a <c>400</c> on a <b>list</b>
/// request whose body is the gateway's <c>ER005</c> envelope, which is end-of-list (an honest
/// empty, §10.9.15); a malformed detail body is a
/// <see cref="SourcePayloadException"/> (the job quarantines <c>UNPARSEABLE_PAYLOAD</c>). A
/// <b>bound symbol the directory cannot map is isolated per constituent</b> — the walk skips it,
/// names it <see cref="VykCompanyDirectory.CompanyIdentityUnresolved"/> and continues, and the
/// job records it in the run's per-symbol stats (01 v1.5 §10.9.15, the §10.9.10/§10.9.11
/// per-company isolation doctrine, FR-MDF-012; see
/// <see cref="IVykIdentityIsolationReporter"/>) — never a whole-run fail-stop. Cursors are
/// persisted at the end of the adapter loop, before the job's store write (§10.9.15's accepted
/// residual: a post-fetch store failure can skip the indexes the cursor covers).
/// </para>
/// </summary>
public sealed class DividendsSourceAdapter : ISourceAdapter<DividendsPayload>, IVykIdentityIsolationReporter
{
    /// <summary>The <c>coverage_metadata.data_type</c> of this family's per-instrument cursor.</summary>
    internal const string CursorDataType = "kap-dividends-cursor";

    private const string InstrumentScope = "instrument";

    /// <summary>The installment marker the gateway uses for the aggregate row (live-verified: the
    /// per-installment rows carry <c>1</c>, <c>2</c>, … and the sum row carries <c>T</c>) — the
    /// aggregate is skipped so a dividend is never counted twice.</summary>
    private const string TotalInstallmentOrder = "T";

    private readonly IKapSourceClient _client;
    private readonly DividendsSourceOptions _options;
    private readonly KapSourceOptions? _kapOptions;
    private readonly VykCompanyDirectory? _directory;
    private readonly KapRequestPacer? _pacer;
    private readonly UniverseSyncStore? _universe;
    private readonly DegerliDbContext? _db;
    private readonly ILogger<DividendsSourceAdapter>? _logger;
    private readonly List<string> _unresolvedSymbols = [];

    public DividendsSourceAdapter(IKapSourceClient client, IOptions<DividendsSourceOptions> options)
        : this(client, options, kapOptions: null, directory: null, pacer: null, universe: null, db: null, logger: null)
    {
    }

    public DividendsSourceAdapter(
        IKapSourceClient client,
        IOptions<DividendsSourceOptions> options,
        IOptions<KapSourceOptions>? kapOptions,
        VykCompanyDirectory? directory,
        KapRequestPacer? pacer,
        UniverseSyncStore? universe,
        DegerliDbContext? db,
        ILogger<DividendsSourceAdapter>? logger)
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
    public IReadOnlyList<string> UnresolvedSymbols => _unresolvedSymbols;

    /// <inheritdoc />
    public async Task<SourcePayload<DividendsPayload>> FetchAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        if (!VykEnabled)
        {
            var legacyRaw = await _client.GetStringAsync(_options.Path, cancellationToken).ConfigureAwait(false);
            return KapPayload.Parse<DividendsPayload>(legacyRaw);
        }

        return await FetchVykAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The VYK run (§10.9.15): one paced pass over the bound index's constituents.</summary>
    private async Task<SourcePayload<DividendsPayload>> FetchVykAsync(CancellationToken cancellationToken)
    {
        var symbols = await _universe!.GetCurrentConstituentSymbolsAsync(_options.UniverseIndex, cancellationToken)
            .ConfigureAwait(false);
        if (symbols.Count == 0)
        {
            throw new SourceFetchException(
                $"Index '{_options.UniverseIndex}' has no current constituents; a dividends run over an empty bound is a failure, not an empty success.");
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
                "dividends: walking {Count} current constituents of {Index} via the VYK gateway",
                symbols.Count,
                _options.UniverseIndex);
        }

        var dividends = new List<DividendFact>();
        var raw = new StringBuilder();
        var cursors = new List<(string Symbol, string Cursor)>();
        _unresolvedSymbols.Clear();

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
            var (companyDividends, nextCursor, companyRaw) = await FetchCompanyAsync(symbol, companyId, cursor, cancellationToken)
                .ConfigureAwait(false);

            dividends.AddRange(companyDividends);
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

        var payload = new DividendsPayload(SourceName, dividends);
        return new SourcePayload<DividendsPayload>(SourceName, payload, raw.ToString());
    }

    /// <summary>
    /// One company's walk (cursor <b>inclusive</b>; the shared
    /// <see cref="VykDisclosureWalker"/> owns the ER005 honest-empty, stale-cursor reset, non-list
    /// body and strict-cursor semantics — 01 v1.5 §10.9.15).
    /// </summary>
    private async Task<(List<DividendFact> Dividends, string? Cursor, string Raw)> FetchCompanyAsync(
        string symbol,
        string companyId,
        string? cursor,
        CancellationToken cancellationToken)
    {
        var walker = new VykDisclosureWalker(_client, _pacer, _logger, CursorDataType, Redact);
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

        return (walk.Facts.ToList(), walk.NextCursor, walk.Raw);
    }

    /// <summary>
    /// Decodes one VYK corporate-action detail. Only a <c>eventType=KP</c> body is a dividend
    /// notice; the other corporate-action kinds (capital changes, buy-backs, general assemblies …)
    /// carry no cash-dividend tables and yield nothing. The real <c>flatData[0]</c> shape
    /// (captured 2026-10-20 from BİM disclosure 1138986) is
    /// <c>content.object.void[]</c> of named fields; <c>currency</c> is a keyed
    /// <c>{key,text}</c> list, <c>cashDividendList</c> rows carry
    /// <c>exchCode/installmentOrder/grossTry/netTry/grossPercentage/netPercentage</c> and
    /// <c>cashDividendPaymentDateList</c> rows carry
    /// <c>installmentOrder/advisedUsageDate/recordDate/paymentDate</c> date wrappers.
    /// </summary>
    private IEnumerable<DividendFact> DecodeDetail(string raw, string symbol)
    {
        using var document = ParseDocument(raw);
        var root = document.RootElement;

        var fault = VykFault.TryRead(root);
        if (fault is not null)
        {
            throw new SourceFetchException($"VYK /disclosureDetail returned a fault ({fault.Code}): {Redact(fault.Message)}");
        }

        var eventType = ReadString(root, "eventType");
        if (string.IsNullOrWhiteSpace(eventType)
            || !string.Equals(eventType.Trim(), _options.DividendEventType, StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        var fields = ReadFields(root);
        if (fields.Count == 0)
        {
            // A KP notice without the structured terms document is a malformed body, not an
            // empty dividend: an absent table is never reported as "no dividend".
            throw new SourcePayloadException(raw, new JsonException("VYK dividend detail carries no flatData terms document."));
        }

        // The detail names its own currency; absent, the fact carries none (never an assumed TRY).
        var currency = ReadKeyedValue(fields.GetValueOrDefault("currency"), "key");
        var paymentDates = ReadRows(fields.GetValueOrDefault("cashDividendPaymentDateList"))
            .Select(row => (Installment: Text(row, "installmentOrder"), Row: row))
            .Where(row => row.Installment is not null)
            .GroupBy(row => row.Installment!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Row, StringComparer.OrdinalIgnoreCase);

        foreach (var row in ReadRows(fields.GetValueOrDefault("cashDividendList")))
        {
            var installment = Text(row, "installmentOrder");
            if (installment is null || string.Equals(installment, TotalInstallmentOrder, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // The row is this company's cash dividend; a row naming another exchange code is not
            // this instrument's fact (FR-MDF-008).
            var exchangeCode = Text(row, "exchCode");
            if (exchangeCode is not null && !string.Equals(exchangeCode.Trim(), symbol, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var gross = Text(row, "grossTry");
            if (gross is null
                || !decimal.TryParse(gross.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            {
                throw new SourcePayloadException(raw, new JsonException("VYK dividend row carries no parseable gross amount."));
            }

            // Without the proposed ex-date the instalment has no calendar position: leave it out
            // rather than invent one (the source simply did not place it yet).
            if (!paymentDates.TryGetValue(installment, out var payment))
            {
                continue;
            }

            var exDate = ParseDate(Text(payment, "advisedUsageDate"));
            if (exDate is null)
            {
                continue;
            }

            yield return new DividendFact(
                symbol,
                exDate.Value,
                ParseDate(Text(payment, "paymentDate")),
                amount,
                currency);
        }
    }

    /// <summary>The named fields of the detail's <c>flatData[0].content.object.void[]</c>.</summary>
    private static Dictionary<string, JsonElement> ReadFields(JsonElement root)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("flatData", out var flatData)
            || flatData.ValueKind != JsonValueKind.Array)
        {
            return fields;
        }

        foreach (var entry in flatData.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.Object
                || !content.TryGetProperty("object", out var document)
                || document.ValueKind != JsonValueKind.Object
                || !document.TryGetProperty("void", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in items.EnumerateArray())
            {
                var name = ReadString(item, "property");
                if (name is not null)
                {
                    fields[name] = item.Clone();
                }
            }
        }

        return fields;
    }

    /// <summary>The row list of a table field (<c>{object:{void:[{object:{void:[…fields]}}]}}</c>).</summary>
    private static IEnumerable<Dictionary<string, JsonElement>> ReadRows(JsonElement table)
    {
        if (table.ValueKind != JsonValueKind.Object
            || !table.TryGetProperty("object", out var wrapper)
            || wrapper.ValueKind != JsonValueKind.Object
            || !wrapper.TryGetProperty("void", out var rows)
            || rows.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object
                || !row.TryGetProperty("object", out var body)
                || body.ValueKind != JsonValueKind.Object
                || !body.TryGetProperty("void", out var fields)
                || fields.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var map = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var field in fields.EnumerateArray())
            {
                var name = ReadString(field, "property");
                if (name is not null)
                {
                    map[name] = field.Clone();
                }
            }

            yield return map;
        }
    }

    /// <summary>One named field's scalar text (the deepest <c>string</c> member of its wrapper).</summary>
    private static string? Text(Dictionary<string, JsonElement> fields, string name) =>
        fields.TryGetValue(name, out var field) ? FirstString(field) : null;

    /// <summary>
    /// The value of a keyed list field (<c>{object:{void:[{property:"key",string:"TRY"}, …]}}</c>),
    /// e.g. the dividend currency.
    /// </summary>
    private static string? ReadKeyedValue(JsonElement field, string key)
    {
        if (field.ValueKind != JsonValueKind.Object
            || !field.TryGetProperty("object", out var wrapper)
            || wrapper.ValueKind != JsonValueKind.Object
            || !wrapper.TryGetProperty("void", out var entries)
            || entries.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            if (string.Equals(ReadString(entry, "property"), key, StringComparison.OrdinalIgnoreCase))
            {
                return FirstString(entry);
            }
        }

        return null;
    }

    /// <summary>The deepest <c>string</c> member of a field wrapper (handles the date wrapper
    /// <c>{object:{void:{property:date, object:{string:…}}}}</c>).</summary>
    private static string? FirstString(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (element.TryGetProperty("string", out var value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var found = FirstString(property.Value);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
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
            "dividends: company {Symbol} could not be mapped to a VYK companyId; isolated and the loop continues ({ReasonCode})",
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

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

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
