using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.Quarantine;
using Microsoft.Extensions.Logging;

namespace Degerli.Ingestion.Validation;

/// <summary>
/// The quarantine pipeline (C3a). Refusing a fact is one operation with three effects
/// (FR-MDF-012): the rejected fact is written to <c>quarantined_facts</c> with its reason
/// code and payload retained (BR-MDF-007), a structured alert event is logged, and the
/// builder is e-mailed. Jobs call this instead of the raw write path so no quarantine can
/// be silent.
/// </summary>
public interface IQuarantineService
{
    Task QuarantineAsync(QuarantineEntry entry, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default pipeline: persists through <see cref="IQuarantineWriter"/> (TKT-mdf-002's write
/// path), logs the alert event, and raises the builder alert through
/// <see cref="IIngestionAlerter"/>. The log is emitted here — not only in the alerter — so
/// the queryable Serilog alert (FR-MDF-012) is present regardless of the mail transport
/// wired by TKT-mdf-005.
/// </summary>
public sealed class QuarantineService : IQuarantineService
{
    private readonly IQuarantineWriter _writer;
    private readonly IIngestionAlerter _alerter;
    private readonly ILogger<QuarantineService> _logger;

    public QuarantineService(
        IQuarantineWriter writer,
        IIngestionAlerter alerter,
        ILogger<QuarantineService> logger)
    {
        _writer = writer;
        _alerter = alerter;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task QuarantineAsync(QuarantineEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await _writer.WriteAsync(entry, cancellationToken).ConfigureAwait(false);

        // Queryable log alert: names the job and the reason (FR-MDF-012, TC-MDF-020).
        _logger.LogError(
            "Ingestion alert for job {JobCode}: {ReasonCode} (source {SourceRef})",
            entry.JobCode,
            entry.ReasonCode,
            entry.SourceRef ?? "unknown");

        await _alerter.RaiseAsync(
            new IngestionAlert(
                entry.JobCode,
                entry.ReasonCode,
                Subject: $"Degerli alert: {entry.JobCode} {entry.ReasonCode}",
                BodyTr: $"Veri doğrulama hatası: {entry.JobCode} işi bir kaydı reddetti ({entry.ReasonCode}). Ayrıntı için karantina kuyruğuna bakın.",
                BodyEn: $"Data validation failure: the {entry.JobCode} job rejected a fact ({entry.ReasonCode}). See the quarantine queue for details.",
                SourceRef: entry.SourceRef),
            cancellationToken).ConfigureAwait(false);
    }
}
