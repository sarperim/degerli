using Degerli.Persistence;
using Degerli.Persistence.Entities;

namespace Degerli.Ingestion.Quarantine;

/// <summary>
/// One rejected fact destined for <c>quarantined_facts</c> (02 §3.1). A fact that fails
/// validation is never written to its fact table; the rejected payload is retained so it
/// can be inspected and re-ingested after a fix (BR-MDF-007).
/// </summary>
public sealed record QuarantineEntry(
    string JobCode,
    string? SourceRef,
    string? PayloadJson,
    string ReasonCode,
    DateTimeOffset QuarantinedAt);

/// <summary>The quarantine write path (TKT-mdf-002). The reason-code catalog and the
/// partial-batch validation pipeline extend this in TKT-mdf-006.</summary>
public interface IQuarantineWriter
{
    Task WriteAsync(QuarantineEntry entry, CancellationToken cancellationToken = default);
}

/// <summary>Writes quarantine rows through the shared context. Rows start <c>open</c>
/// and are never deleted — dismissal records the accepted gap (BR-MDF-007).</summary>
public sealed class QuarantineWriter : IQuarantineWriter
{
    private readonly DegerliDbContext _db;

    public QuarantineWriter(DegerliDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task WriteAsync(QuarantineEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _db.QuarantinedFacts.Add(new QuarantinedFact
        {
            JobCode = entry.JobCode,
            SourceRef = entry.SourceRef,
            PayloadJson = entry.PayloadJson,
            ReasonCode = entry.ReasonCode,
            QuarantinedAt = entry.QuarantinedAt,
            Status = "open",
        });

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
