using System.Text.Json;
using Degerli.Ingestion.Kap;
using Degerli.Ingestion.Sources;
using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.Statements;

/// <summary>Route configuration for the <c>statements</c> KAP endpoint.</summary>
public sealed class StatementsSourceOptions
{
    public const string SectionName = "Ingestion:Statements";

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
    /// bind it to <c>0</c> so the loop is deterministic.
    /// </summary>
    public int RequestDelayMs { get; set; } = 500;
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

    public StatementsSourceAdapter(IKapSourceClient client, IOptions<StatementsSourceOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

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
    /// Company-scoped fetch (01 v1.5 §10.9.2): one company's KAP statement payload for the
    /// request. The per-company loop lives in <see cref="StatementsJob"/> — the adapter only
    /// translates one company's payload; persistence, validation, quarantine and pacing stay
    /// job-side. The route is resolved from the configured <see cref="StatementsSourceOptions.Path"/>
    /// template (a <c>{symbol}</c> token is substituted; without it the symbol rides as a query
    /// parameter so the fixed canonical L2 double route still resolves). Envelope detection stays
    /// inside the adapter: the real KAP XBRL fact envelope (decoded by
    /// <see cref="KapXbrlStatementDecoder"/>) and the canonical <c>statements</c> double envelope
    /// (the L2 contract) are both first-class. A payload whose company identity does not match the
    /// requested <paramref name="symbol"/> (the canonical double can carry any company's body) is
    /// narrowed to an empty statement set so a bound member can never be written under another
    /// company's identity.
    /// </summary>
    public async Task<SourcePayload<StatementsPayload>> FetchCompanyAsync(
        string symbol,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        var raw = await _client
            .GetStringAsync(ResolveFetchPath(_options.Path, symbol), cancellationToken)
            .ConfigureAwait(false);
        using var document = ParseDocument(raw);

        if (KapXbrlStatementDecoder.IsEnvelope(document))
        {
            // The real KAP XBRL fact envelope (01 v1.5 §10.9.5). A body for a different company
            // is skipped before decode so no cross-company fact is ever produced or quarantined.
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
