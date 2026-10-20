using Degerli.Core.Metrics;
using Degerli.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.Metrics;

/// <summary>
/// The market-snapshot computation (FR-MOV-001..006, 021; BR-MOV-009/010/011): it
/// assembles the covered-universe facts from the canonical MDF tables and delegates
/// every aggregate formula to <see cref="MarketAggregates"/> (one definition per
/// number, AD-06). The mover threshold is configuration, not code.
/// </summary>
public sealed class MarketSnapshotEngine
{
    private const string SharesDiluted = "SHARES_DILUTED";
    private const string NetIncome = "NI";

    private readonly DegerliDbContext _db;
    private readonly MarketSnapshotOptions _options;

    public MarketSnapshotEngine(DegerliDbContext db, IOptions<MarketSnapshotOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    /// <summary>Computes the snapshot for <paramref name="date"/>, or NULL when no price exists.</summary>
    /// <remarks>
    /// Price-derived blocks (indices, breadth, movers, total volume) and share-derived blocks
    /// (market P/E, dividend yield, cap-weighted sector performance) are decoupled
    /// (<c>01</c> §10.9.14; TKT-foundation-031): a constituent without a usable
    /// <c>SHARES_DILUTED</c> still contributes its price move and volume, while the
    /// market-cap-dependent aggregates exclude it and report the exclusion honestly.
    /// </remarks>
    public async Task<MarketSnapshotResult?> ComputeAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        // The covered universe is the current XU100 membership (02 §6.3).
        var xu100Id = await _db.Indices
            .Where(i => i.Code == "XU100")
            .Select(i => i.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (xu100Id == 0)
        {
            return null;
        }

        var memberIds = await _db.IndexConstituents
            .Where(c => c.IndexId == xu100Id && c.EffectiveTo == null)
            .Select(c => c.InstrumentId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (memberIds.Count == 0)
        {
            return null;
        }

        var instruments = await _db.Instruments
            .Where(i => memberIds.Contains(i.Id))
            .Select(i => new { i.Id, i.Symbol, i.SectorId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var today = await _db.DailyPrices
            .Where(p => memberIds.Contains(p.InstrumentId) && p.PriceDate == date)
            .Select(p => new { p.InstrumentId, p.CloseRaw, p.Volume })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (today.Count == 0)
        {
            return null;
        }

        var coveredIds = today.Select(p => p.InstrumentId).ToList();

        // The previous trading day present in the facts (the daily change base).
        var previousDate = await _db.DailyPrices
            .Where(p => coveredIds.Contains(p.InstrumentId) && p.PriceDate < date)
            .MaxAsync(p => (DateOnly?)p.PriceDate, cancellationToken)
            .ConfigureAwait(false);
        var previous = previousDate is null
            ? new Dictionary<long, decimal>()
            : await _db.DailyPrices
                .Where(p => coveredIds.Contains(p.InstrumentId) && p.PriceDate == previousDate)
                .ToDictionaryAsync(p => p.InstrumentId, p => p.CloseRaw, cancellationToken)
                .ConfigureAwait(false);

        // Latest FY statement per instrument: the restated version serves (BR-MDF-011).
        var statements = await _db.FinancialStatements
            .Where(s => coveredIds.Contains(s.InstrumentId) && s.PeriodType == "FY")
            .Select(s => new { s.Id, s.InstrumentId, s.PeriodEndDate, s.StatementType, s.Version })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var statementIds = statements.Select(s => s.Id).ToList();
        var linesByStatement = (await _db.FinLineItems
                .Where(l => statementIds.Contains(l.StatementId))
                .Select(l => new { l.StatementId, l.ItemCode, l.Value })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .GroupBy(l => l.StatementId)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(l => l.ItemCode, l => l.Value, StringComparer.Ordinal));

        var latestStatements = statements
            .GroupBy(s => s.InstrumentId)
            .ToDictionary(g => g.Key, g =>
            {
                var latestPeriod = g.Max(s => s.PeriodEndDate);
                return g.Where(s => s.PeriodEndDate == latestPeriod).ToList();
            });

        decimal? LineFor(long instrumentId, string statementType, string itemCode)
        {
            if (!latestStatements.TryGetValue(instrumentId, out var versions))
            {
                return null;
            }

            var chosen = versions
                .Where(s => s.StatementType == statementType)
                .OrderByDescending(s => s.Version)
                .FirstOrDefault();
            if (chosen is null
                || !linesByStatement.TryGetValue(chosen.Id, out var map)
                || !map.TryGetValue(itemCode, out var value))
            {
                return null;
            }

            return value;
        }

        // TTM dividends per share (ex-date inside the trailing year, boundary inclusive).
        var ttmFrom = date.AddDays(-365);
        var dividendPerShare = (await _db.Dividends
                .Where(d => coveredIds.Contains(d.InstrumentId) && d.ExDate >= ttmFrom && d.ExDate <= date)
                .Select(d => new { d.InstrumentId, d.AmountPerShare })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .GroupBy(d => d.InstrumentId)
            .ToDictionary(g => g.Key, g => g.Sum(d => d.AmountPerShare));

        var sectorCodes = await _db.Sectors
            .ToDictionaryAsync(s => s.Id, s => s.Code, cancellationToken)
            .ConfigureAwait(false);

        var constituents = new List<MarketConstituent>(instruments.Count);
        foreach (var instrument in instruments)
        {
            if (!previous.TryGetValue(instrument.Id, out var previousClose) || previousClose <= 0m)
            {
                continue;
            }

            var quote = today.FirstOrDefault(p => p.InstrumentId == instrument.Id);
            if (quote is null)
            {
                continue;
            }

            // A member without a usable share count is NOT dropped (01 §10.9.14): it joins the
            // price-derived aggregates with no market cap, and the share-derived aggregates
            // exclude it while reporting the exclusion. Null stays null — no fabricated cap.
            var shares = LineFor(instrument.Id, "BS", SharesDiluted);
            decimal? marketCap = null;
            var divTtm = 0m;
            if (shares is > 0m)
            {
                marketCap = quote.CloseRaw * shares.Value;
                divTtm = dividendPerShare.TryGetValue(instrument.Id, out var dps) ? dps * shares.Value : 0m;
            }

            var change = quote.CloseRaw / previousClose - 1m;
            var ni = LineFor(instrument.Id, "IS", NetIncome);
            var sector = instrument.SectorId is long sectorId && sectorCodes.TryGetValue(sectorId, out var code)
                ? code
                : null;

            constituents.Add(new MarketConstituent(
                instrument.Symbol,
                sector,
                marketCap,
                change,
                quote.Volume ?? 0L,
                ni,
                divTtm));
        }

        if (constituents.Count == 0)
        {
            // The engine-level null survives only for its original meanings (01 §10.9.14):
            // no covered universe / no XU100 membership, no stored price for the covered
            // universe on the resolved day (guarded above), or no member with a previous
            // close to measure a daily change against. Missing share counts are NOT such a
            // reason — those members still feed the price-derived aggregates.
            return null;
        }

        var (xu100, xu30) = await ReadIndexMovesAsync(date, cancellationToken).ConfigureAwait(false);

        return MarketAggregates.Compute(xu100, xu30, constituents, _options.VolumeEligibilityThreshold);
    }

    private async Task<(IndexMove Xu100, IndexMove Xu30)> ReadIndexMovesAsync(
        DateOnly date,
        CancellationToken cancellationToken)
    {
        // The BIST 30 code has two spellings in the stored world: the real chain materializes
        // `XU030` (the Xu030LaunchSeed index row; FLG-08), while the FU fixture world uses
        // `XU30`. Resolve by both so a real-only DB never silently degrades the BIST 30 move to
        // (0, 0); in the FU world no `XU030` row exists, so the extra spelling is a no-op and
        // every FU golden re-runs unchanged (01 v1.5 §10.9.7).
        var indexIds = await _db.Indices
            .Where(i => i.Code == "XU100" || i.Code == "XU30" || i.Code == "XU030")
            .ToDictionaryAsync(i => i.Code, i => i.Id, cancellationToken)
            .ConfigureAwait(false);

        var ids = indexIds.Values.ToList();
        var levels = await _db.IndexLevels
            .Where(l => ids.Contains(l.IndexId) && l.LevelDate <= date)
            .Select(l => new { l.IndexId, l.LevelDate, l.Close })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IndexMove? Move(params string[] codes)
        {
            var resolved = codes
                .Select(code => indexIds.TryGetValue(code, out var id) ? id : (long?)null)
                .FirstOrDefault(id => id is not null);
            if (resolved is null)
            {
                return null;
            }

            var indexId = resolved.Value;
            var current = levels.FirstOrDefault(l => l.IndexId == indexId && l.LevelDate == date);
            if (current is null)
            {
                return new IndexMove(0m, 0m);
            }

            var previousDate = levels
                .Where(l => l.IndexId == indexId && l.LevelDate < date)
                .OrderByDescending(l => l.LevelDate)
                .Select(l => (DateOnly?)l.LevelDate)
                .FirstOrDefault();
            var previous = previousDate is null
                ? null
                : levels.FirstOrDefault(l => l.IndexId == indexId && l.LevelDate == previousDate);

            var change = previous is not null && previous.Close != 0m
                ? current.Close / previous.Close - 1m
                : 0m;
            return new IndexMove(current.Close, change);
        }

        return (
            Move("XU100") ?? new IndexMove(0m, 0m),
            Move("XU030", "XU30") ?? new IndexMove(0m, 0m));
    }
}
