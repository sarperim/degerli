using System.Text.Json;
using Degerli.Ingestion.Kap;
using Degerli.Ingestion.Sources;
using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.Statements;

/// <summary>Route configuration for the <c>statements</c> KAP endpoint.</summary>
public sealed class StatementsSourceOptions
{
    public const string SectionName = "Ingestion:Statements";

    public string Path { get; set; } = "/statements";
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
