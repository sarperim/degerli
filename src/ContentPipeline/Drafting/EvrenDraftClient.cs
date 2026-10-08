using System.Net.Http.Json;
using Degerli.Persistence.Entities;

namespace Degerli.ContentPipeline.Drafting;

/// <summary>
/// Thin HTTP client for the evren AI drafting API (C4). The drafting logic and
/// provenance recording live in <see cref="DescriptionDrafter"/>; this type only
/// speaks the wire contract. In CI the base URL points at a WireMock double serving
/// recorded fixtures — the real evren API is never called (test strategy §7).
/// </summary>
public sealed class EvrenDraftClient
{
    private readonly HttpClient _http;
    private readonly string? _apiKey;
    private readonly string _draftPath;

    public EvrenDraftClient(HttpClient http, string? apiKey, string draftPath)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _apiKey = apiKey;
        _draftPath = string.IsNullOrWhiteSpace(draftPath)
            ? ContentPipelineSettings.DefaultEvrenDraftPath
            : draftPath;
    }

    /// <summary>
    /// Requests a bilingual draft for <paramref name="symbol"/> grounded in the given
    /// KAP disclosures.
    /// </summary>
    public async Task<EvrenDraftResponse> DraftAsync(
        string symbol,
        IReadOnlyList<KapDisclosure> disclosures,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentNullException.ThrowIfNull(disclosures);

        var request = new EvrenDraftRequest(
            symbol,
            disclosures.Select(d => d.ToEvrenRef()).ToList());

        using var message = new HttpRequestMessage(HttpMethod.Post, _draftPath)
        {
            Content = JsonContent.Create(request),
        };

        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        }

        using var response = await _http
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content
            .ReadFromJsonAsync<EvrenDraftResponse>(cancellationToken)
            .ConfigureAwait(false);

        return payload
            ?? throw new InvalidOperationException($"evren returned an empty draft response for '{symbol}'.");
    }
}
