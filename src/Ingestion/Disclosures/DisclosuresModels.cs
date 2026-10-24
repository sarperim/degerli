using System.Text.Json.Serialization;
using Degerli.Ingestion.Kap;

namespace Degerli.Ingestion.Disclosures;

/// <summary>The KAP disclosure payload (FU §10 <c>disclosures-ok.json</c>, FR-MDF-005).</summary>
public sealed record DisclosuresPayload(
    [property: JsonPropertyName("sourceRef")] string? SourceRef,
    [property: JsonPropertyName("disclosures")] IReadOnlyList<DisclosureFact> Disclosures) : IKapPayload;

/// <summary>
/// The disclosures lane's <b>bootstrap provenance</b> channel (TKT-foundation-078, 01 §10.9.15).
/// A run that had to seed a cursor from the platform head's window must say so: the seed is a
/// <i>decision</i> about where to start reading, never a claim that the company (or the market)
/// had no disclosures before it. The job logs the count and folds the entries into the run's
/// per-symbol stats with the named reason, so the ledger carries the provenance.
/// </summary>
public interface IDisclosureBootstrapReporter
{
    /// <summary>
    /// The cursors this run bootstrapped: the bound symbol of each company whose walk had no
    /// persisted cursor, or <see cref="DisclosuresSourceAdapter.MarketCursorName"/> when the
    /// market-wide cursor was seeded from the head's window.
    /// </summary>
    IReadOnlyList<string> Bootstrapped { get; }

    /// <summary>
    /// The market rows this run did <b>not</b> fetch because their owning company is outside the
    /// bound universe (§10.9.2/AD-14): named, counted, never fabricated into a row. Zero for the
    /// company feed.
    /// </summary>
    int OutOfBoundRows { get; }

    /// <summary>The list pages the run's walk(s) requested (the pacing cost of the pass).</summary>
    int ListPages { get; }

    /// <summary>
    /// True when a walk stopped at a configured budget rather than at the end of its list: the
    /// cursor names the last index the run completed and the next run resumes there.
    /// </summary>
    bool Truncated { get; }
}

/// <summary>One disclosure's metadata. Documents live on disk outside the DB; the stored
/// row carries only <c>document_path</c>.</summary>
public sealed record DisclosureFact(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("disclosureType")] string? DisclosureType,
    [property: JsonPropertyName("publishDate")] DateOnly? PublishDate,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("sourceUrl")] string? SourceUrl);

/// <summary>Result of one disclosure batch.</summary>
public sealed record DisclosureWriteResult(
    int Inserted,
    int Unchanged,
    IReadOnlyList<string> UnknownSymbols)
{
    public static readonly DisclosureWriteResult Empty = new(0, 0, []);
}
