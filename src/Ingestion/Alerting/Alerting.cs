using Microsoft.Extensions.Logging;

namespace Degerli.Ingestion.Alerting;

/// <summary>
/// One ingestion alert (C3d, FR-MDF-012): raised when a fact is quarantined (validation
/// failure or a conflicting re-publication). Bilingual bodies match the platform's e-mail
/// convention so no server-side locale guess is needed (01 §10.5); the production SMTP
/// transport is wired by TKT-mdf-005.
/// </summary>
public sealed record IngestionAlert(
    string JobCode,
    string ReasonCode,
    string Subject,
    string BodyTr,
    string BodyEn,
    string? SourceRef = null);

/// <summary>The alerting seam (C3d). A recording double replaces it in L2 tests so the
/// mail-builder and log-alert trigger conditions are asserted without real delivery.</summary>
public interface IIngestionAlerter
{
    Task RaiseAsync(IngestionAlert alert, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default alerter: emits a structured log alert event (queryable in Serilog per
/// FR-MDF-012). The e-mail transport is added by the alerting ticket; this keeps the
/// trigger observable from the ingestion jobs that raise it.
/// </summary>
public sealed class LoggingIngestionAlerter : IIngestionAlerter
{
    private readonly ILogger<LoggingIngestionAlerter> _logger;

    public LoggingIngestionAlerter(ILogger<LoggingIngestionAlerter> logger) => _logger = logger;

    /// <inheritdoc />
    public Task RaiseAsync(IngestionAlert alert, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alert);

        _logger.LogError(
            "Ingestion alert for job {JobCode}: {ReasonCode} (source {SourceRef})",
            alert.JobCode,
            alert.ReasonCode,
            alert.SourceRef ?? "unknown");

        return Task.CompletedTask;
    }
}
