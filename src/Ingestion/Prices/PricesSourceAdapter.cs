using System.Text.Json;
using Degerli.Ingestion.Sources;
using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.Prices;

/// <summary>
/// Source wiring for the <c>prices</c> job (config section <c>Ingestion:Prices</c>).
/// The base address points at İşbank in production and at the WireMock source double in
/// L2 tests; the path lets a test select a named canned payload (FU §10).
/// </summary>
public sealed class PricesSourceOptions
{
    public const string SectionName = "Ingestion:Prices";

    /// <summary>Base address of the price source (empty → adapter must be configured).</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Path of the EOD price resource relative to <see cref="BaseUrl"/>.</summary>
    public string Path { get; set; } = "/prices/eod";

    /// <summary>Path of the historical price resource used in backfill mode (TKT-mdf-007).</summary>
    public string BackfillPath { get; set; } = "/prices/history";
}

/// <summary>
/// The İşbank EOD price adapter (FR-MDF-001). Fetches the raw payload through the shared
/// <see cref="ISourceClient"/> and parses it; persistence and validation are the job's.
/// It is also the backfill adapter (FR-MDF-010): the same client reads the historical
/// resource, which returns as far back as the source allows.
/// </summary>
public sealed class PricesSourceAdapter : ISourceAdapter<PricesPayload>, IBackfillSourceAdapter<PricesHistoryPayload>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISourceClient _client;
    private readonly PricesSourceOptions _options;

    public PricesSourceAdapter(ISourceClient client, IOptions<PricesSourceOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    /// <inheritdoc />
    public string SourceName => "isbank";

    /// <inheritdoc />
    public async Task<SourcePayload<PricesPayload>> FetchAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var raw = await _client.GetStringAsync(_options.Path, cancellationToken).ConfigureAwait(false);

        PricesPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<PricesPayload>(raw, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new SourcePayloadException(raw, exception);
        }

        if (payload is null)
        {
            throw new SourceFetchException("Price payload deserialized to null.");
        }

        return new SourcePayload<PricesPayload>(payload.SourceRef, payload, raw);
    }

    /// <inheritdoc />
    public async Task<SourcePayload<PricesHistoryPayload>> FetchBackfillAsync(
        DateOnly from,
        CancellationToken cancellationToken = default)
    {
        // The requested range is passed to the source as a query hint, but the source
        // returns whatever history it holds — the job records the achieved depth.
        var path = $"{_options.BackfillPath}?from={from:yyyy-MM-dd}";
        var raw = await _client.GetStringAsync(path, cancellationToken).ConfigureAwait(false);

        PricesHistoryPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<PricesHistoryPayload>(raw, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new SourcePayloadException(raw, exception);
        }

        if (payload is null)
        {
            throw new SourceFetchException("Price history payload deserialized to null.");
        }

        return new SourcePayload<PricesHistoryPayload>(payload.SourceRef, payload, raw);
    }
}
