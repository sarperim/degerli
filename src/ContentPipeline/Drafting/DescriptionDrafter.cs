using System.Text.Json;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.ContentPipeline.Drafting;

/// <summary>
/// The build-time AI drafting stage (FR-RES-019, UC-RES-004 step 1): for each target
/// stock it reads the stock's KAP disclosures, asks evren for a bilingual draft, and
/// appends a new <c>business_descriptions</c> row — always <c>draft</c>, never
/// <c>published</c> (BR-RES-002/NFR-RES-006; the builder review + publish gate live in
/// the API, FR-RES-020). Provenance (<c>source_refs_json</c>) is the disclosure ids the
/// draft was actually grounded in, and version lineage is continued per instrument
/// (02 §3.4).
/// </summary>
public sealed class DescriptionDrafter
{
    public const string DraftStatus = "draft";

    private readonly DegerliDbContext _db;
    private readonly EvrenDraftClient _client;
    private readonly TextWriter _log;

    public DescriptionDrafter(DegerliDbContext db, EvrenDraftClient client, TextWriter log)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// Drafts descriptions and returns the number of rows written. A <paramref name="symbol"/>
    /// of <c>null</c> drafts every instrument that has KAP disclosures; a named symbol that
    /// is not in the covered universe throws <see cref="UnknownSymbolException"/>.
    /// </summary>
    public async Task<int> DraftAsync(string? symbol, CancellationToken cancellationToken = default)
    {
        var targets = await ResolveTargetsAsync(symbol, cancellationToken).ConfigureAwait(false);

        var written = 0;
        foreach (var instrument in targets)
        {
            if (await DraftInstrumentAsync(instrument, cancellationToken).ConfigureAwait(false))
            {
                written++;
            }
        }

        return written;
    }

    private async Task<IReadOnlyList<Instrument>> ResolveTargetsAsync(
        string? symbol,
        CancellationToken cancellationToken)
    {
        if (symbol is null)
        {
            return await _db.Instruments
                .OrderBy(i => i.Symbol)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var instrument = await _db.Instruments
            .SingleOrDefaultAsync(i => i.Symbol == symbol, cancellationToken)
            .ConfigureAwait(false);

        return instrument is null
            ? throw new UnknownSymbolException(symbol)
            : [instrument];
    }

    private async Task<bool> DraftInstrumentAsync(Instrument instrument, CancellationToken cancellationToken)
    {
        var disclosures = await _db.KapDisclosures
            .Where(k => k.InstrumentId == instrument.Id)
            .OrderBy(k => k.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (disclosures.Count == 0)
        {
            await _log.WriteLineAsync(
                $"draft: {instrument.Symbol} has no KAP disclosures; skipped.").ConfigureAwait(false);
            return false;
        }

        var response = await _client.DraftAsync(instrument.Symbol, disclosures, cancellationToken).ConfigureAwait(false);
        var description = response.Descriptions
            .FirstOrDefault(d => string.Equals(d.Symbol, instrument.Symbol, StringComparison.Ordinal));

        if (description is null)
        {
            await _log.WriteLineAsync(
                $"draft: evren returned no description for {instrument.Symbol}; skipped.").ConfigureAwait(false);
            return false;
        }

        var nextVersion = (await _db.BusinessDescriptions
            .Where(b => b.InstrumentId == instrument.Id)
            .MaxAsync(b => (int?)b.Version, cancellationToken)
            .ConfigureAwait(false) ?? 0) + 1;

        var sourceRefs = disclosures.Select(d => d.Id).ToList();

        _db.BusinessDescriptions.Add(new BusinessDescription
        {
            InstrumentId = instrument.Id,
            Version = nextVersion,
            Status = DraftStatus,
            TextTr = description.TextTr,
            TextEn = description.TextEn,
            SourceRefsJson = JsonSerializer.Serialize(sourceRefs),
            LastReviewedAt = null,
            PublishedAt = null,
        });

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _log.WriteLineAsync(
            $"draft: {instrument.Symbol} v{nextVersion} status={DraftStatus} sourceRefs=[{string.Join(",", sourceRefs)}].")
            .ConfigureAwait(false);
        return true;
    }
}

/// <summary>Raised when a requested symbol is outside the covered universe.</summary>
public sealed class UnknownSymbolException(string symbol) : Exception($"Unknown symbol '{symbol}'.")
{
    public string Symbol { get; } = symbol;
}
