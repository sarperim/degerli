using System.Text.Json;
using Degerli.Ingestion.Sources;
using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.Universe;

/// <summary>
/// Source wiring for the <c>universe-sync</c> job (config section
/// <c>Ingestion:Universe</c>). <see cref="Path"/> is the instrument/classification
/// (or membership change) resource; <see cref="IndexLevelsPath"/> is the BIST index
/// levels resource (FR-MDF-006). The base address comes from the shared
/// <see cref="ISourceClient"/>.
/// </summary>
public sealed class UniverseSourceOptions
{
    public const string SectionName = "Ingestion:Universe";

    /// <summary>Path of the universe resource relative to the source base address.</summary>
    public string Path { get; set; } = "/universe";

    /// <summary>Path of the index-levels resource relative to the source base address.</summary>
    public string IndexLevelsPath { get; set; } = "/index-levels";
}

/// <summary>
/// The KAP universe adapter (FR-MDF-006/007): fetches the instrument/classification
/// (or membership change) payload through the shared <see cref="ISourceClient"/> and
/// parses it; persistence and validation are the job's.
/// </summary>
public sealed class UniverseSourceAdapter : ISourceAdapter<UniversePayload>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISourceClient _client;
    private readonly UniverseSourceOptions _options;

    public UniverseSourceAdapter(ISourceClient client, IOptions<UniverseSourceOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    /// <inheritdoc />
    public string SourceName => "kap";

    /// <inheritdoc />
    public async Task<SourcePayload<UniversePayload>> FetchAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var raw = await _client.GetStringAsync(_options.Path, cancellationToken).ConfigureAwait(false);

        UniversePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<UniversePayload>(raw, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new SourcePayloadException(raw, exception);
        }

        if (payload is null)
        {
            throw new SourceFetchException("Universe payload deserialized to null.");
        }

        return new SourcePayload<UniversePayload>(payload.SourceRef, payload, raw);
    }
}

/// <summary>
/// The BIST index-levels adapter (FR-MDF-006): fetches the index level payload
/// through the shared <see cref="ISourceClient"/> and parses it.
/// </summary>
public sealed class IndexLevelsSourceAdapter : ISourceAdapter<IndexLevelsPayload>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISourceClient _client;
    private readonly UniverseSourceOptions _options;

    public IndexLevelsSourceAdapter(ISourceClient client, IOptions<UniverseSourceOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    /// <inheritdoc />
    public string SourceName => "kap";

    /// <inheritdoc />
    public async Task<SourcePayload<IndexLevelsPayload>> FetchAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var raw = await _client.GetStringAsync(_options.IndexLevelsPath, cancellationToken).ConfigureAwait(false);

        IndexLevelsPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<IndexLevelsPayload>(raw, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new SourcePayloadException(raw, exception);
        }

        if (payload is null)
        {
            throw new SourceFetchException("Index-levels payload deserialized to null.");
        }

        return new SourcePayload<IndexLevelsPayload>(payload.SourceRef, payload, raw);
    }
}
