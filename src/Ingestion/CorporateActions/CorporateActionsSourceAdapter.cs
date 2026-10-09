using Degerli.Ingestion.Kap;
using Degerli.Ingestion.Sources;
using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.CorporateActions;

/// <summary>Route configuration for the <c>corporate-actions</c> KAP endpoint.</summary>
public sealed class CorporateActionsSourceOptions
{
    public const string SectionName = "Ingestion:CorporateActions";

    public string Path { get; set; } = "/corporate-actions";
}

/// <summary>KAP corporate-actions adapter (FR-MDF-004).</summary>
public sealed class CorporateActionsSourceAdapter : ISourceAdapter<CorporateActionsPayload>
{
    private readonly IKapSourceClient _client;
    private readonly CorporateActionsSourceOptions _options;

    public CorporateActionsSourceAdapter(IKapSourceClient client, IOptions<CorporateActionsSourceOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    /// <inheritdoc />
    public string SourceName => "kap";

    /// <inheritdoc />
    public async Task<SourcePayload<CorporateActionsPayload>> FetchAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var raw = await _client.GetStringAsync(_options.Path, cancellationToken).ConfigureAwait(false);
        return KapPayload.Parse<CorporateActionsPayload>(raw);
    }
}
