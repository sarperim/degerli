using System.Text.Json.Serialization;
using Degerli.Persistence.Entities;

namespace Degerli.ContentPipeline.Drafting;

/// <summary>
/// The drafting request sent to the evren API: the stock symbol plus the KAP
/// disclosure metadata the draft must be grounded in (UC-RES-004 step 1 — KAP
/// filings are the factual source, NFR-RES-006 governance).
/// </summary>
public sealed record EvrenDraftRequest(
    string Symbol,
    IReadOnlyList<EvrenDisclosureRef> Disclosures);

/// <summary>One KAP disclosure reference as sent to evren (metadata only).</summary>
public sealed record EvrenDisclosureRef(
    long Id,
    string? DisclosureType,
    DateOnly? PublishDate,
    string? Title,
    string? SourceUrl);

/// <summary>
/// The recorded evren drafting response (FU §10 <c>evren-draft-ok</c>): a
/// <c>sourceRef</c> provenance tag and one drafted bilingual description per stock.
/// </summary>
public sealed record EvrenDraftResponse(
    [property: JsonPropertyName("sourceRef")] string? SourceRef,
    IReadOnlyList<EvrenDraftDescription> Descriptions);

/// <summary>
/// One drafted bilingual description. The evren double echoes the requested
/// <see cref="SourceRefs"/> (the disclosure ids used), but the pipeline records
/// provenance from the disclosures it actually sent — never from AI-authored ids.
/// </summary>
public sealed record EvrenDraftDescription(
    string Symbol,
    int Version,
    string Status,
    string? TextTr,
    string? TextEn,
    IReadOnlyList<long> SourceRefs);

/// <summary>Maps platform disclosure rows onto their evren request shape.</summary>
public static class EvrenDisclosureRefMapping
{
    /// <summary>Projects a stored KAP disclosure to the evren request shape.</summary>
    public static EvrenDisclosureRef ToEvrenRef(this KapDisclosure disclosure)
    {
        ArgumentNullException.ThrowIfNull(disclosure);

        return new EvrenDisclosureRef(
            disclosure.Id,
            disclosure.DisclosureType,
            disclosure.PublishDate,
            disclosure.Title,
            disclosure.SourceUrl);
    }
}
