using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Degerli.Ingestion.Scheduling;

/// <summary>
/// In-process production loop for the C3c scheduler (AD-04/AD-05): it sleeps until the
/// engine's next wake (schedule or retry) using the host <see cref="TimeProvider"/> and
/// then drives one <see cref="IngestionScheduler.TickAsync"/> pass. Keeping the loop in
/// a background service and the decisions in the engine is what lets tests advance a
/// fake clock instead of waiting for wall-clock time.
/// </summary>
public sealed class IngestionSchedulerHostedService : BackgroundService
{
    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromMinutes(1);

    private readonly IngestionScheduler _scheduler;
    private readonly TimeProvider _clock;
    private readonly ILogger<IngestionSchedulerHostedService> _logger;

    public IngestionSchedulerHostedService(
        IngestionScheduler scheduler,
        TimeProvider clock,
        ILogger<IngestionSchedulerHostedService> logger)
    {
        _scheduler = scheduler;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_scheduler.Enabled)
        {
            _logger.LogInformation("Ingestion scheduler is disabled by configuration; not starting.");
            return;
        }

        _logger.LogInformation("Ingestion scheduler started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = _clock.GetUtcNow();
            var next = _scheduler.NextWake(now);
            var delay = next is null ? IdlePollInterval : next.Value - now;
            if (delay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(delay, _clock, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await _scheduler.TickAsync(_clock.GetUtcNow(), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Ingestion scheduler tick failed; will retry on the next wake.");
            }
        }
    }
}
