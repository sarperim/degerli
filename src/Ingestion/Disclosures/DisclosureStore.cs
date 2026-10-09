using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.Disclosures;

/// <summary>
/// Fact storage for <c>kap_disclosures</c> (FR-MDF-005, 02 §1). Metadata only: the DB row
/// carries the disclosure fields plus the on-disk <c>document_path</c>; the document itself
/// lives outside the database.
/// </summary>
public sealed class DisclosureStore
{
    private readonly DegerliDbContext _db;
    private readonly DisclosuresSourceOptions _options;

    public DisclosureStore(DegerliDbContext db, IOptions<DisclosuresSourceOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public async Task<DisclosureWriteResult> UpsertAsync(
        string sourceRef,
        DateTimeOffset recordedAt,
        DisclosuresPayload payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRef);

        var symbols = payload.Disclosures.Select(d => d.Symbol).Distinct(StringComparer.Ordinal).ToList();
        var instrumentIds = await _db.Instruments
            .Where(i => symbols.Contains(i.Symbol))
            .ToDictionaryAsync(i => i.Symbol, i => i.Id, cancellationToken)
            .ConfigureAwait(false);

        var instrumentIdList = instrumentIds.Values.ToList();
        var existing = (await _db.KapDisclosures
                .Where(k => instrumentIdList.Contains(k.InstrumentId))
                .Select(k => new { k.InstrumentId, k.PublishDate, k.Title, k.SourceUrl })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .Select(k => Key(k.InstrumentId, k.PublishDate, k.Title, k.SourceUrl))
            .ToHashSet(StringComparer.Ordinal);

        var inserted = 0;
        var unchanged = 0;
        var unknown = new HashSet<string>(StringComparer.Ordinal);

        foreach (var disclosure in payload.Disclosures)
        {
            if (!instrumentIds.TryGetValue(disclosure.Symbol, out var instrumentId))
            {
                unknown.Add(disclosure.Symbol);
                continue;
            }

            if (!existing.Add(Key(instrumentId, disclosure.PublishDate, disclosure.Title, disclosure.SourceUrl)))
            {
                unchanged++;
                continue;
            }

            _db.KapDisclosures.Add(new KapDisclosure
            {
                InstrumentId = instrumentId,
                DisclosureType = disclosure.DisclosureType,
                PublishDate = disclosure.PublishDate,
                Title = disclosure.Title,
                SourceUrl = disclosure.SourceUrl,
                DocumentPath = BuildDocumentPath(disclosure.Id),
                SourceRef = sourceRef,
                RecordedAt = recordedAt,
            });
            inserted++;
        }

        if (inserted > 0)
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new DisclosureWriteResult(inserted, unchanged, unknown.ToList());
    }

    /// <summary>The archived location of a disclosure's document on the configured volume,
    /// or NULL when no document root is configured.</summary>
    private string? BuildDocumentPath(long disclosureId)
    {
        var root = _options.DocumentRoot;
        return string.IsNullOrWhiteSpace(root)
            ? null
            : Path.Combine(root, $"{disclosureId}.pdf");
    }

    private static string Key(long instrumentId, DateOnly? publishDate, string? title, string? sourceUrl) =>
        !string.IsNullOrWhiteSpace(sourceUrl)
            ? sourceUrl
            : string.Join('|', instrumentId, publishDate?.ToString("yyyy-MM-dd") ?? string.Empty, title ?? string.Empty);
}
