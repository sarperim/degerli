using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Degerli.Ingestion;

/// <summary>
/// Host wiring for the in-process ingestion/data-platform workers (C3a/C3c). Concrete
/// source adapters and scheduled jobs are added by their domain tickets; this
/// establishes the hosted-service seam so the API container runs them in-process
/// (AD-04, AD-05).
/// </summary>
public sealed class IngestionHostedService : BackgroundService
{
    private readonly ILogger<IngestionHostedService> _logger;

    public IngestionHostedService(ILogger<IngestionHostedService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Data platform workers started; no source adapters registered yet.");
        return Task.CompletedTask;
    }
}
