using Degerli.Ingestion.Kap;
using Degerli.Ingestion.Sources;
using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.Statements;

/// <summary>Route configuration for the <c>statements</c> KAP endpoint.</summary>
public sealed class StatementsSourceOptions
{
    public const string SectionName = "Ingestion:Statements";

    public string Path { get; set; } = "/statements";
}

/// <summary>
/// KAP statements adapter (FR-MDF-002): fetches and parses the financial-statement
/// payload. The ETL mapping to the canonical chart of accounts lives in
/// <see cref="StatementMapper"/> and persistence in <see cref="FinancialStatementStore"/>.
/// </summary>
public sealed class StatementsSourceAdapter : ISourceAdapter<StatementsPayload>
{
    private readonly IKapSourceClient _client;
    private readonly StatementsSourceOptions _options;

    public StatementsSourceAdapter(IKapSourceClient client, IOptions<StatementsSourceOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    /// <inheritdoc />
    public string SourceName => "kap";

    /// <inheritdoc />
    public async Task<SourcePayload<StatementsPayload>> FetchAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var raw = await _client.GetStringAsync(_options.Path, cancellationToken).ConfigureAwait(false);
        return KapPayload.Parse<StatementsPayload>(raw);
    }
}
