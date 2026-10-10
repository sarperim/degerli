using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Api.Stocks;

/// <summary>
/// The stocks module read surface (`03-api-design.md` §3; FR-RES-001..004,
/// UC-RES-001):
/// <list type="bullet">
/// <item><c>GET /api/v1/stocks?sector=&amp;q=</c> — the current XU100 universe list
/// (symbol, name, sector identity, listingDate) with the honest-data envelope;
/// a sector filter and a case-insensitive ILIKE on name/symbol combine
/// conjunctively. An unknown sector or non-matching query is an empty <c>200</c>
/// (emptiness is data, not an error).</item>
/// <item><c>GET /api/v1/stocks/search?q=</c> — the lightweight header typeahead:
/// symbol + name only (FR-RES-004).</item>
/// </list>
/// The list is the current universe — instruments whose XU100 membership has no end
/// date (<c>v_current_universe</c>, `02` §3.1) — never a delisted or unlisted row.
/// </summary>
public static class StockListEndpoints
{
    /// <summary>The index code whose current constituents form the covered universe.</summary>
    private const string UniverseIndexCode = "XU100";

    public static IEndpointRouteBuilder MapDegerliStockEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/stocks", ListAsync).AllowAnonymous();
        api.MapGet("/stocks/search", SearchAsync).AllowAnonymous();
        return api;
    }

    /// <summary>
    /// The current-universe list (TC-RES-001..003). Rows carry the sector <em>identity</em>
    /// (its stable code) or <c>null</c> when the instrument is unclassified (UNSEC) —
    /// the unclassified state is served explicitly, never guessed.
    /// </summary>
    private static async Task<IResult> ListAsync(
        string? sector,
        string? q,
        DegerliDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var universe = ApplyFilters(db, Universe(db), sector, q);

        var stocks = await universe
            .OrderBy(instrument => instrument.Symbol)
            .Select(instrument => new
            {
                instrument.Symbol,
                instrument.Name,
                Sector = db.Sectors
                    .Where(sectorRow => sectorRow.Id == instrument.SectorId)
                    .Select(sectorRow => sectorRow.Code)
                    .FirstOrDefault(),
                instrument.ListingDate,
            })
            .ToListAsync(cancellationToken);

        var (asOf, stale) = await FreshnessAsync(db, clock, cancellationToken);

        return Results.Ok(new { asOf, stale, stocks });
    }

    /// <summary>
    /// The lightweight header typeahead (TC-RES-004): symbol + name only — a strictly
    /// smaller payload than the list endpoint (no sector/listingDate). No query (or an
    /// empty one) yields no matches rather than dumping the universe.
    /// </summary>
    private static async Task<IResult> SearchAsync(
        string? q,
        DegerliDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var query = string.IsNullOrWhiteSpace(q)
            ? Universe(db).Where(_ => false)
            : ApplyFilters(db, Universe(db), sector: null, q);

        var stocks = await query
            .OrderBy(instrument => instrument.Symbol)
            .Select(instrument => new { instrument.Symbol, instrument.Name })
            .ToListAsync(cancellationToken);

        var (asOf, stale) = await FreshnessAsync(db, clock, cancellationToken);

        return Results.Ok(new { asOf, stale, stocks });
    }

    /// <summary>Instruments with a current XU100 membership — the covered universe.</summary>
    private static IQueryable<Instrument> Universe(DegerliDbContext db) =>
        from instrument in db.Instruments
        join constituent in db.IndexConstituents on instrument.Id equals constituent.InstrumentId
        join index in db.Indices on constituent.IndexId equals index.Id
        where index.Code == UniverseIndexCode && constituent.EffectiveTo == null
        select instrument;

    /// <summary>
    /// Applies the sector and free-text filters conjunctively. The text filter is an
    /// ILIKE substring match on name or symbol (FR-RES-003); LIKE metacharacters in the
    /// user's query are escaped so the input is matched literally.
    /// </summary>
    private static IQueryable<Instrument> ApplyFilters(DegerliDbContext db, IQueryable<Instrument> universe, string? sector, string? q)
    {
        if (!string.IsNullOrWhiteSpace(sector))
        {
            var code = sector.Trim();
            universe = universe.Where(instrument =>
                db.Sectors.Any(sectorRow => sectorRow.Id == instrument.SectorId && EF.Functions.ILike(sectorRow.Code, code)));
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{EscapeLike(q.Trim())}%";
            universe = universe.Where(instrument =>
                EF.Functions.ILike(instrument.Name, pattern) || EF.Functions.ILike(instrument.Symbol, pattern));
        }

        return universe;
    }

    /// <summary>
    /// The list/typeahead freshness: the latest canonical daily-price date serves as the
    /// as-of date; it is stale when that date trails the latest trading day (`03` §1.2).
    /// </summary>
    private static async Task<(DateOnly AsOf, bool Stale)> FreshnessAsync(
        DegerliDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var asOf = await db.DailyPrices
            .MaxAsync(price => (DateOnly?)price.PriceDate, cancellationToken)
            ?? today;

        return (asOf, asOf < LatestTradingDayOnOrBefore(today));
    }

    private static DateOnly LatestTradingDayOnOrBefore(DateOnly date)
    {
        while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            date = date.AddDays(-1);
        }

        return date;
    }

    private static string EscapeLike(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
