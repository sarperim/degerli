using System.Text;

namespace Degerli.Ingestion.Sources;

/// <summary>
/// Minimal HTTP seam over an external source API (KAP, İşbank, TÜİK, TEFAS …). L2
/// tests point it at the WireMock source double (test strategy §7); production resolves
/// the base address from configuration. Every adapter reads through this one client so
/// no job talks to the network directly. The client honors the response's declared
/// <c>Content-Type; charset=</c> (01 v1.5 §10.9.4) so Turkish code pages decode faithfully.
/// </summary>
public interface ISourceClient
{
    /// <summary>
    /// Fetches a source resource as a raw string, decoded under the response's declared
    /// charset (01 v1.5 §10.9.4); throws on a non-success response.
    /// </summary>
    Task<string> GetStringAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="ISourceClient"/> over a configured <see cref="HttpClient"/>. A
/// non-success status is surfaced as <see cref="SourceFetchException"/> so a job can
/// treat it as a source failure (the retry ladder is C3c's concern, TKT-mdf-005).
/// </summary>
public sealed class HttpSourceClient : ISourceClient
{
    private readonly HttpClient _http;

    public HttpSourceClient(HttpClient http) => _http = http;

    /// <inheritdoc />
    public async Task<string> GetStringAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var response = await _http.GetAsync(path, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new SourceFetchException(
                $"Source '{_http.BaseAddress}' returned HTTP {(int)response.StatusCode} for '{path}'.");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return DecodeBody(bytes, response.Content.Headers.ContentType?.CharSet);
    }

    /// <summary>
    /// Decodes a source body honoring the response's declared <c>Content-Type; charset=</c>
    /// (01 v1.5 §10.9.4). A declared charset is decoded with <see cref="Encoding.GetEncoding(string)"/>
    /// — the .NET code-pages provider is registered once at product startup (AD-14) so the
    /// Turkish code pages (<c>windows-1254</c> / <c>ISO-8859-9</c>) decode without mojibake.
    /// No declared charset, or one the provider cannot resolve, falls back to UTF-8 — the
    /// pre-v1.5 behavior; per-adapter charset sniffing is deliberately not added (§10.9.4).
    /// </summary>
    internal static string DecodeBody(byte[] bytes, string? charset)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (string.IsNullOrWhiteSpace(charset))
        {
            return Encoding.UTF8.GetString(bytes);
        }

        try
        {
            // The header value may arrive quoted (charset="windows-1254").
            return Encoding.GetEncoding(charset.Trim().Trim('"')).GetString(bytes);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return Encoding.UTF8.GetString(bytes);
        }
    }
}

/// <summary>A source returned a non-success response (source-down class).</summary>
public sealed class SourceFetchException : Exception
{
    public SourceFetchException(string message) : base(message) { }

    public SourceFetchException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// A source returned a body that could not be parsed (UNPARSEABLE_PAYLOAD class). The
/// raw body is retained so the job can quarantine it for inspection (FR-MDF-012).
/// </summary>
public sealed class SourcePayloadException : Exception
{
    public SourcePayloadException(string rawJson, Exception inner)
        : base("Source payload could not be parsed (UNPARSEABLE_PAYLOAD).", inner) => RawJson = rawJson;

    /// <summary>The raw, unparsed body — retained as the quarantine payload.</summary>
    public string RawJson { get; }
}

/// <summary>The raw result of one adapter fetch: the parsed payload plus the raw body.</summary>
public sealed record SourcePayload<TPayload>(string? SourceRef, TPayload Payload, string RawJson);

/// <summary>
/// One source adapter (the framework seam every job builds on, TKT-mdf-002). An
/// adapter fetches and parses one payload shape for one source; persistence,
/// validation and quarantine stay in the job so the same adapter can serve ingest
/// and backfill (TKT-mdf-007).
/// </summary>
public interface ISourceAdapter<TPayload> where TPayload : class
{
    /// <summary>Human-readable source name (e.g. <c>isbank</c>), recorded with facts.</summary>
    string SourceName { get; }

    /// <summary>Fetches and parses the payload for the requested date.</summary>
    Task<SourcePayload<TPayload>> FetchAsync(DateOnly date, CancellationToken cancellationToken = default);
}

/// <summary>
/// Backfill capability of a source adapter (TKT-mdf-007, FR-MDF-010). A backfill run
/// asks the source for its historical range starting at <c>from</c>; the source returns
/// whatever history it holds, so the adapter surfaces the payload as-is and the job
/// records the achieved depth (BR-MDF-006 — never fabricate beyond the source). The
/// payload shape (<typeparamref name="TPayload"/>) may differ from the incremental one.
/// </summary>
public interface IBackfillSourceAdapter<TPayload> where TPayload : class
{
    /// <summary>Fetches and parses the source's historical payload from <paramref name="from"/>.</summary>
    Task<SourcePayload<TPayload>> FetchBackfillAsync(DateOnly from, CancellationToken cancellationToken = default);
}
