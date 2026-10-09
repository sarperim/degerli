using System.Text.Json;
using Degerli.Ingestion.Sources;

namespace Degerli.Ingestion.Kap;

/// <summary>A KAP payload carrying the provenance reference stored with every fact it
/// produces (FR-MDF-008).</summary>
public interface IKapPayload
{
    string? SourceRef { get; }
}

/// <summary>Parses a KAP payload and surfaces malformed bodies as
/// <see cref="SourcePayloadException"/> so the job can quarantine them
/// (<c>UNPARSEABLE_PAYLOAD</c>).</summary>
public static class KapPayload
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static SourcePayload<TPayload> Parse<TPayload>(string raw) where TPayload : class, IKapPayload
    {
        try
        {
            var payload = JsonSerializer.Deserialize<TPayload>(raw, JsonOptions)
                ?? throw new JsonException("KAP payload deserialized to null.");
            return new SourcePayload<TPayload>(payload.SourceRef, payload, raw);
        }
        catch (JsonException exception)
        {
            throw new SourcePayloadException(raw, exception);
        }
    }
}

/// <summary>
/// HTTP seam for the KAP-sourced payloads — financial statements, dividends, corporate
/// actions and disclosures (FR-MDF-002..005). KAP has its own base address, so the
/// adapters read through this client rather than the prices-bound <see cref="ISourceClient"/>
/// (TKT-mdf-002). L2 tests point it at the WireMock source double.
/// </summary>
public interface IKapSourceClient
{
    /// <summary>Fetches a KAP resource as a raw string; throws on a non-success response.</summary>
    Task<string> GetStringAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>Default <see cref="IKapSourceClient"/>. Mirrors <see cref="HttpSourceClient"/>:
/// a non-success status surfaces as <see cref="SourceFetchException"/> so the job can treat
/// it as a source failure.</summary>
public sealed class KapHttpSourceClient : IKapSourceClient
{
    private readonly HttpClient _http;

    public KapHttpSourceClient(HttpClient http) => _http = http;

    /// <inheritdoc />
    public async Task<string> GetStringAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var response = await _http.GetAsync(path, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new SourceFetchException(
                $"KAP source '{_http.BaseAddress}' returned HTTP {(int)response.StatusCode} for '{path}'.");
        }

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Shared KAP source configuration (the client's base address).</summary>
public sealed class KapSourceOptions
{
    public const string SectionName = "Ingestion:Kap";

    /// <summary>KAP (or source-double) base address.</summary>
    public string? BaseUrl { get; set; }
}
