using Degerli.Ingestion.KapPublic;
using Degerli.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Ingestion.Disclosures;

/// <summary>
/// The <b>bound</b> universe of the public-site market feed (01 v1.5.85 §10.9.19,
/// TKT-foundation-084): which of the configured index's constituents this source may write, and
/// which symbols it holds no public-site identity for.
/// <para>
/// A symbol belongs to the bound only when <b>both</b> halves hold: it resolves to an instrument
/// row (the store's own key) <b>and</b> that instrument has a stored public-site oid (080's map).
/// The second half is what makes it <i>this</i> source: a public-site disclosure can only be
/// attributed to a company whose public identity the lane actually holds, and the lane never
/// derives an oid from a symbol's spelling (a guess, not an identity).
/// </para>
/// <para>
/// The read is one query for the instrument ids and one for the oids, so a pass never fans out per
/// symbol. Symbols with no instrument and symbols with no stored oid are reported separately as
/// <see cref="UnresolvedSymbols"/> — both are isolated with the same named reason
/// (<see cref="KapPublicReasonCodes.CompanyIdentityUnresolved"/>), but they are different facts
/// and the caller decides how to log them.
/// </para>
/// </summary>
public sealed class KapPublicBoundUniverse
{
    private readonly DegerliDbContext _db;
    private readonly KapPublicCompanyMapStore _maps;

    public KapPublicBoundUniverse(DegerliDbContext db, KapPublicCompanyMapStore maps)
    {
        _db = db;
        _maps = maps;
    }

    /// <summary>
    /// The bound: the symbols of <paramref name="symbols"/> that have both an instrument row and a
    /// stored public-site oid, plus the symbols that do not.
    /// </summary>
    /// <param name="symbols">The configured index's current constituent symbols.</param>
    /// <param name="cancellationToken">Run cancellation.</param>
    public async Task<KapPublicBoundSelection> SelectAsync(
        IReadOnlyCollection<string> symbols,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        if (symbols.Count == 0)
        {
            return new KapPublicBoundSelection(
                new HashSet<string>(StringComparer.Ordinal),
                []);
        }

        var requested = symbols
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var instrumentIds = await _db.Instruments
            .Where(instrument => requested.Contains(instrument.Symbol))
            .Select(instrument => new { instrument.Symbol, instrument.Id })
            .ToDictionaryAsync(row => row.Symbol, row => row.Id, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        var publicOids = await _maps
            .GetPublicOidsAsync(instrumentIds.Values.Distinct().ToList(), cancellationToken)
            .ConfigureAwait(false);

        var bound = new HashSet<string>(StringComparer.Ordinal);
        var unresolved = new List<string>();
        foreach (var symbol in requested)
        {
            if (instrumentIds.TryGetValue(symbol, out var instrumentId) && publicOids.ContainsKey(instrumentId))
            {
                bound.Add(symbol);
                continue;
            }

            unresolved.Add(symbol);
        }

        return new KapPublicBoundSelection(bound, unresolved);
    }
}

/// <summary>
/// One bound selection: the symbols this source may write (<see cref="Bound"/>) and the constituent
/// symbols it holds no public-site identity for (<see cref="UnresolvedSymbols"/>).
/// </summary>
/// <param name="Bound">Constituents with an instrument row and a stored public-site oid.</param>
/// <param name="UnresolvedSymbols">Constituents the lane cannot attribute to a public company.</param>
public sealed record KapPublicBoundSelection(
    IReadOnlySet<string> Bound,
    IReadOnlyList<string> UnresolvedSymbols);
