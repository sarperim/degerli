using Degerli.Ingestion.Kap;
using Degerli.Ingestion.Sources;
using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.Disclosures;

/// <summary>Route + document-store configuration for the <c>disclosures</c> KAP endpoint.</summary>
public sealed class DisclosuresSourceOptions
{
    public const string SectionName = "Ingestion:Disclosures";

    public string Path { get; set; } = "/disclosures";

    /// <summary>
    /// Directory where KAP documents are archived on disk (outside the DB). When set, each
    /// stored row records its <c>document_path</c> under this root (FR-MDF-005). Empty in
    /// compositions that ingest metadata only.
    /// </summary>
    public string? DocumentRoot { get; set; }
}

/// <summary>KAP disclosures adapter (FR-MDF-005).</summary>
public sealed class DisclosuresSourceAdapter : ISourceAdapter<DisclosuresPayload>
{
    private readonly IKapSourceClient _client;
    private readonly DisclosuresSourceOptions _options;

    public DisclosuresSourceAdapter(IKapSourceClient client, IOptions<DisclosuresSourceOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    /// <inheritdoc />
    public string SourceName => "kap";

    /// <inheritdoc />
    public async Task<SourcePayload<DisclosuresPayload>> FetchAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var raw = await _client.GetStringAsync(_options.Path, cancellationToken).ConfigureAwait(false);
        return KapPayload.Parse<DisclosuresPayload>(raw);
    }
}
