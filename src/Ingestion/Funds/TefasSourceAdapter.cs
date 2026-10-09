using System.Text.Json;
using Degerli.Ingestion.Sources;
using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.Funds;

/// <summary>
/// Source wiring for the fund jobs (config section <c>Ingestion:Funds</c>). The base
/// address points at TEFAS in production and at the WireMock source double in L2 tests;
/// the universe path lets a test serve a named canned payload (FU §10).
/// </summary>
public sealed class TefasSourceOptions
{
    public const string SectionName = "Ingestion:Funds";

    /// <summary>The DI key of the TEFAS-bound source client (prices keep their own).</summary>
    public const string ClientKey = "tefas";

    /// <summary>Base address of the TEFAS source (empty → adapter must be configured).</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Path of the fund-universe resource relative to <see cref="BaseUrl"/>.</summary>
    public string UniversePath { get; set; } = "/funds/universe";
}

/// <summary>
/// The TEFAS fund adapter (FR-FDF-001..003). Fetches the universe payload through the
/// TEFAS-bound <see cref="ISourceClient"/> and parses it; validation, the universe bound,
/// persistence and quarantine stay in the job.
/// </summary>
public sealed class TefasSourceAdapter : ISourceAdapter<TefasUniversePayload>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISourceClient _client;
    private readonly TefasSourceOptions _options;

    public TefasSourceAdapter(ISourceClient client, IOptions<TefasSourceOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    /// <inheritdoc />
    public string SourceName => "tefas";

    /// <inheritdoc />
    public async Task<SourcePayload<TefasUniversePayload>> FetchAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var raw = await _client.GetStringAsync(_options.UniversePath, cancellationToken).ConfigureAwait(false);

        TefasUniversePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<TefasUniversePayload>(raw, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new SourcePayloadException(raw, exception);
        }

        if (payload is null)
        {
            throw new SourceFetchException("TEFAS payload deserialized to null.");
        }

        return new SourcePayload<TefasUniversePayload>(payload.SourceRef, payload, raw);
    }
}
