namespace Degerli.Core.Metrics;

/// <summary>
/// One covered-universe constituent's inputs to the market aggregates (02 §6.3,
/// BR-MOV-009): the canonical facts the Metrics Engine already owns — market cap,
/// daily price change, traded volume, trailing net income and trailing dividends.
/// </summary>
/// <param name="Symbol">Instrument ticker.</param>
/// <param name="SectorCode">Sector bucket; NULL when unclassified (excluded from sector performance).</param>
/// <param name="MarketCap">
/// close_raw × shares_diluted (FU §2 money units); NULL when the constituent has no usable
/// share count (null or ≤ 0) — the price-derived aggregates still count it, the
/// market-cap-derived aggregates exclude it and report the exclusion (01 §10.9.14).
/// </param>
/// <param name="ChangePct">Daily price change as a decimal fraction (0.05 = +5%).</param>
/// <param name="Volume">Traded shares on the snapshot day.</param>
/// <param name="NetIncome">Trailing net income; NULL = not available (treated as a loss-maker exclusion).</param>
/// <param name="DividendTtm">Trailing-twelve-month dividends (Σ DPS_TTM × shares); 0 when not computable.</param>
public sealed record MarketConstituent(
    string Symbol,
    string? SectorCode,
    decimal? MarketCap,
    decimal ChangePct,
    long Volume,
    decimal? NetIncome,
    decimal DividendTtm);

/// <summary>One top-movers entry (BR-MOV-011; FR-MOV-021 carries the volume).</summary>
public sealed record MoverEntry(string Symbol, decimal ChangePct, long Volume);

/// <summary>An index level and its daily change.</summary>
public sealed record IndexMove(decimal Level, decimal ChangePct);

/// <summary>
/// The computed market snapshot aggregate (02 §3.2 <c>market_snapshots</c>). The
/// index-level block is nullable: a resolved day whose index facts have not landed yet
/// carries NULL levels/changes (<c>01</c> §10.9.14; TKT-foundation-042) — never a
/// fabricated <c>0.0000</c>.
/// </summary>
public sealed record MarketSnapshotResult(
    decimal? Xu100Level,
    decimal? Xu100ChangePct,
    decimal? Xu30Level,
    decimal? Xu30ChangePct,
    int BreadthAdvancing,
    int BreadthDeclining,
    int BreadthUnchanged,
    decimal VolumeTotal,
    decimal? MarketPe,
    int MarketPeExcludedCount,
    decimal? MarketDivYield,
    IReadOnlyList<MoverEntry> Gainers,
    IReadOnlyList<MoverEntry> Losers,
    IReadOnlyDictionary<string, decimal> SectorPerformance);

/// <summary>
/// The single canonical definition of the market aggregates (02 §6.3; AD-10, AD-06):
/// breadth, movers with the volume-eligibility threshold, total volume, market P/E
/// (loss-makers excluded + counted, D-05) and market dividend yield, plus the
/// cap-weighted sector daily performance. Consumed by the ingestion worker; the
/// API and any future surface read the stored snapshot, never re-derive (BR-MOV-009).
/// </summary>
public static class MarketAggregates
{
    /// <summary>
    /// The mover volume-eligibility threshold default (BR-MOV-011). Production sets
    /// the value from the observed volume distribution; the default is the test value
    /// (FU §2) so a host that configures nothing still behaves deterministically.
    /// </summary>
    public const decimal DefaultVolumeEligibilityThreshold = 1_000_000m;

    /// <summary>Largest gainer/loser list length (BR-MOV-011).</summary>
    public const int MoverCount = 10;

    /// <summary>Rounds a stored aggregate to the data model's numeric(18,6) granularity.</summary>
    private const int Scale = 6;

    /// <summary>Computes the market snapshot aggregate for one trading day.</summary>
    /// <remarks>
    /// <paramref name="xu100"/>/<paramref name="xu30"/> are NULL when the day has no stored
    /// level for that index: the block is honestly empty rather than a fabricated
    /// <c>0.0000</c> reading (<c>01</c> §10.9.14; TKT-foundation-042). Everything else in
    /// this aggregate is price-derived and computes regardless (`01` §10.9.14).
    /// </remarks>
    public static MarketSnapshotResult Compute(
        IndexMove? xu100,
        IndexMove? xu30,
        IReadOnlyList<MarketConstituent> constituents,
        decimal volumeEligibilityThreshold)
    {
        ArgumentNullException.ThrowIfNull(constituents);

        // FR-MOV-003 — breadth over the covered universe.
        var advancing = constituents.Count(c => c.ChangePct > 0m);
        var declining = constituents.Count(c => c.ChangePct < 0m);
        var unchanged = constituents.Count(c => c.ChangePct == 0m);

        // FR-MOV-005 — total traded volume.
        var volumeTotal = constituents.Sum(c => c.Volume);

        // FR-MOV-006 / BR-MOV-010 / D-05 — market P/E = Σmcap / ΣNI over constituents
        // with a share count and NI > 0; loss-makers, missing income AND members without a
        // usable share count (no mcap) are excluded and counted — the exclusion is
        // reported, never silently dropped (01 §10.9.14).
        var profitable = constituents
            .Where(c => c.NetIncome is > 0m && c.MarketCap is not null)
            .ToList();
        var excludedCount = constituents.Count - profitable.Count;
        var totalIncome = profitable.Sum(c => c.NetIncome!.Value);
        var profitableCap = profitable.Sum(c => c.MarketCap!.Value);
        decimal? marketPe = profitable.Count > 0 && totalIncome > 0m
            ? Round(profitableCap / totalIncome)
            : null;

        // FR-MOV-006 — market dividend yield = Σ DIV_TTM / Σ mcap over the share-bearing
        // constituents; NULL when none carries a share count (DPS × shares is not
        // computable without shares — never fabricated).
        var capBearing = constituents.Where(c => c.MarketCap is not null).ToList();
        var totalCap = capBearing.Sum(c => c.MarketCap!.Value);
        decimal? marketDivYield = totalCap > 0m
            ? Round(capBearing.Sum(c => c.DividendTtm) / totalCap)
            : null;

        // FR-MOV-004 / FR-MOV-021 / BR-MOV-011 — top movers among volume-eligible stocks.
        var eligible = constituents.Where(c => c.Volume >= volumeEligibilityThreshold).ToList();
        var gainers = eligible
            .Where(c => c.ChangePct > 0m)
            .OrderByDescending(c => c.ChangePct)
            .ThenBy(c => c.Symbol, StringComparer.Ordinal)
            .Take(MoverCount)
            .Select(c => new MoverEntry(c.Symbol, Round(c.ChangePct), c.Volume))
            .ToList();
        var losers = eligible
            .Where(c => c.ChangePct < 0m)
            .OrderBy(c => c.ChangePct)
            .ThenBy(c => c.Symbol, StringComparer.Ordinal)
            .Take(MoverCount)
            .Select(c => new MoverEntry(c.Symbol, Round(c.ChangePct), c.Volume))
            .ToList();

        // FR-MOV-002 — sector daily performance: cap-weighted average of members' daily
        // changes, Σ(mcap_i × chg_i)/Σmcap_i over the sector's share-bearing members
        // (locked Q2). A member without a usable share count carries no weight and is
        // excluded; a sector with no share-bearing member at all is OMITTED rather than
        // emitted as a fabricated 0.0 reading (01 §10.9.14). Unclassified instruments
        // (NULL sector) belong to no bucket.
        var sectorPerformance = constituents
            .Where(c => c.MarketCap is not null && !string.IsNullOrEmpty(c.SectorCode))
            .GroupBy(c => c.SectorCode!, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var cap = g.Sum(c => c.MarketCap!.Value);
                    return cap == 0m
                        ? 0m
                        : Round(g.Sum(c => c.MarketCap!.Value * c.ChangePct) / cap);
                },
                StringComparer.Ordinal);

        return new MarketSnapshotResult(
            xu100?.Level,
            xu100 is null ? null : Round(xu100.ChangePct),
            xu30?.Level,
            xu30 is null ? null : Round(xu30.ChangePct),
            advancing,
            declining,
            unchanged,
            volumeTotal,
            marketPe,
            excludedCount,
            marketDivYield,
            gainers,
            losers,
            sectorPerformance);
    }

    private static decimal Round(decimal value) => Math.Round(value, Scale, MidpointRounding.AwayFromZero);
}
