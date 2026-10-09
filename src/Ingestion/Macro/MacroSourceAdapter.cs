using System.Text.Json;
using Degerli.Ingestion.Sources;
using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.Macro;

/// <summary>
/// The macro source adapter (FR-MOV-014). Fetches the series payload through the
/// macro-bound <see cref="ISourceClient"/> and parses it; persistence, validation and
/// alerting stay in the jobs. A non-success response surfaces as
/// <see cref="SourceFetchException"/> (the unreachable class) and a malformed body as
/// <see cref="SourcePayloadException"/> (the invalid class).
/// </summary>
public sealed class MacroSourceAdapter : ISourceAdapter<MacroPayload>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISourceClient _client;
    private readonly MacroSourceOptions _options;

    public MacroSourceAdapter(ISourceClient client, IOptions<MacroSourceOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    /// <inheritdoc />
    public string SourceName => "macro";

    /// <inheritdoc />
    public async Task<SourcePayload<MacroPayload>> FetchAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var raw = await _client.GetStringAsync(_options.Path, cancellationToken).ConfigureAwait(false);

        MacroPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<MacroPayload>(raw, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new SourcePayloadException(raw, exception);
        }

        if (payload is null)
        {
            throw new SourceFetchException("Macro payload deserialized to null.");
        }

        return new SourcePayload<MacroPayload>(payload.SourceRef, payload, raw);
    }
}
