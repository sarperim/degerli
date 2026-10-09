using Degerli.Ingestion.Kap;
using Degerli.Ingestion.Sources;
using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.Dividends;

/// <summary>Route configuration for the <c>dividends</c> KAP endpoint.</summary>
public sealed class DividendsSourceOptions
{
    public const string SectionName = "Ingestion:Dividends";

    public string Path { get; set; } = "/dividends";
}

/// <summary>KAP dividends adapter (FR-MDF-003): fetches and parses the dividend payload.</summary>
public sealed class DividendsSourceAdapter : ISourceAdapter<DividendsPayload>
{
    private readonly IKapSourceClient _client;
    private readonly DividendsSourceOptions _options;

    public DividendsSourceAdapter(IKapSourceClient client, IOptions<DividendsSourceOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    /// <inheritdoc />
    public string SourceName => "kap";

    /// <inheritdoc />
    public async Task<SourcePayload<DividendsPayload>> FetchAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var raw = await _client.GetStringAsync(_options.Path, cancellationToken).ConfigureAwait(false);
        return KapPayload.Parse<DividendsPayload>(raw);
    }
}
