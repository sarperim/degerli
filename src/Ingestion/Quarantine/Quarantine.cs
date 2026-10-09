using System.Text.Json;
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
            PayloadJson = NormalizePayloadJson(entry.PayloadJson),
            ReasonCode = entry.ReasonCode,
            QuarantinedAt = entry.QuarantinedAt,
            Status = "open",
        });

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// <c>payload_json</c> is a <c>jsonb</c> column, so an unparseable source body (the
    /// UNPARSEABLE_PAYLOAD class) must still be retained as valid JSON: it is stored as a
    /// JSON string, preserving the exact rejected bytes for inspection and re-ingestion.
    /// Parseable facts are stored verbatim.
    /// </summary>
    private static string? NormalizePayloadJson(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return payloadJson;
        }

        try
        {
            using var _ = JsonDocument.Parse(payloadJson);
            return payloadJson;
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(payloadJson);
        }
    }
}
