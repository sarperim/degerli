using System.Text.Json;
using System.Text.Json.Serialization;
using Degerli.Core.Metrics;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Ingestion.Metrics;

/// <summary>
/// Outcome of a snapshot write pass (rides the run ledger's stats): a fresh insert
/// (<see cref="Written"/>), an identical recompute (<see cref="Unchanged"/>) or an
/// in-place repair of the resolved day's row with changed inputs (<see cref="Rewritten"/>).
/// </summary>
public sealed record MarketSnapshotWriteResult(int Written, int Unchanged, int Rewritten);

/// <summary>
/// Fact storage for the market snapshot (02 §3.2). One row per trading day, idempotent
/// by <c>snapshot_date</c>: a re-run for the same date never inserts a duplicate and
/// never touches another date's row (TC-MOV-005).
/// <para>
/// <b>Input-aware recompute (<c>01</c> §10.9.13; TKT-foundation-042).</b> A completed day
/// is never *blindly* rewritten — the recomputed aggregate is compared to the stored row
/// first: identical inputs give an honest <c>unchanged: 1</c>, and changed inputs (index
/// levels or prices landing after the first compute) repair the row in place and count as
/// a repair. Operational repair therefore needs no SQL surgery. Days other than
/// the resolved one are never addressed by this store at all, so historical rows stay
/// immutable.
/// </para>
/// </summary>
public sealed class MarketSnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // The mover/sector entries use explicit snake_case names (02 §3.2).
        PropertyNamingPolicy = null,
    };

    private readonly DegerliDbContext _db;

    public MarketSnapshotStore(DegerliDbContext db) => _db = db;

    /// <summary>
    /// Resolves the latest stored trading day at or before <paramref name="onOrBefore"/>
    /// over the current XU100 covered universe (`01` §10.9.13): the maximum
    /// <c>daily_prices.price_date</c> ≤ <paramref name="onOrBefore"/> among current XU100
    /// members, or <c>null</c> when no member has a stored price on or before the date.
    /// <para>
    /// Stored prices are the truth — no weekend/holiday assumption (`TradingCalendar.Holidays`
    /// ships empty, so a weekday-backing rule would fabricate a day the market never traded).
    /// This is what lets a Saturday/Sunday (or any cold-start/missed-run) request resolve the
    /// real latest completed trading day (e.g. the preceding Friday) instead of a UTC-today
    /// that has no prices.
    /// </para>
    /// </summary>
    public Task<DateOnly?> ResolveLatestStoredTradingDayAsync(
        DateOnly onOrBefore,
        CancellationToken cancellationToken = default) =>
        ResolveLatestStoredTradingDayAsync(_db, onOrBefore, cancellationToken);

    /// <summary>
    /// The same rule, reachable without composing this store: the latest stored trading day
    /// at or before <paramref name="onOrBefore"/> over the current XU100 covered universe
    /// (<c>01</c> §10.9.13). The <c>metrics-recompute</c> and <c>medians</c> lanes read it
    /// through their engines (TKT-foundation-089) so that <b>one</b> implementation of the
    /// stored-prices-as-truth rule serves every derived-fact lane — no lane re-derives a day
    /// of its own, and none fabricates one from a weekday assumption.
    /// </summary>
    public static async Task<DateOnly?> ResolveLatestStoredTradingDayAsync(
        DegerliDbContext db,
        DateOnly onOrBefore,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var xu100Id = await db.Indices
            .Where(i => i.Code == "XU100")
            .Select(i => i.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (xu100Id == 0)
        {
            return null;
        }

        var memberIds = db.IndexConstituents
            .Where(c => c.IndexId == xu100Id && c.EffectiveTo == null)
            .Select(c => c.InstrumentId);

        return await db.DailyPrices
            .Where(p => memberIds.Contains(p.InstrumentId) && p.PriceDate <= onOrBefore)
            .MaxAsync(p => (DateOnly?)p.PriceDate, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Writes the snapshot row for <paramref name="date"/>: insert when the day has no row,
    /// leave an identical row untouched (honest <c>unchanged</c>), and repair a row whose
    /// stored values no longer match the current facts (<c>rewritten</c>) — always exactly
    /// one row per trading day (`01` §10.9.13; TC-MOV-005).
    /// </summary>
    public async Task<MarketSnapshotWriteResult> UpsertAsync(
        DateOnly date,
        MarketSnapshotResult result,
        DateTimeOffset computedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        var existing = await _db.MarketSnapshots
            .SingleOrDefaultAsync(s => s.SnapshotDate == date, cancellationToken)
            .ConfigureAwait(false);

        var computed = new MarketSnapshot { SnapshotDate = date };
        Apply(computed, date, result, computedAt);

        if (existing is null)
        {
            _db.MarketSnapshots.Add(computed);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new MarketSnapshotWriteResult(Written: 1, Unchanged: 0, Rewritten: 0);
        }

        if (HasSameValues(existing, computed))
        {
            // The recompute agrees with the stored row: nothing to write, and the run
            // reports the honest unchanged count instead of the pre-042 silent 0/0.
            return new MarketSnapshotWriteResult(Written: 0, Unchanged: 1, Rewritten: 0);
        }

        Apply(existing, date, result, computedAt);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new MarketSnapshotWriteResult(Written: 0, Unchanged: 0, Rewritten: 1);
    }

    /// <summary>Copies one computed aggregate onto a snapshot row (the single mapping).</summary>
    private static void Apply(
        MarketSnapshot row,
        DateOnly date,
        MarketSnapshotResult result,
        DateTimeOffset computedAt)
    {
        row.Xu100Level = result.Xu100Level;
        row.Xu100ChangePct = result.Xu100ChangePct;
        row.Xu30Level = result.Xu30Level;
        row.Xu30ChangePct = result.Xu30ChangePct;
        row.BreadthAdvancing = result.BreadthAdvancing;
        row.BreadthDeclining = result.BreadthDeclining;
        row.BreadthUnchanged = result.BreadthUnchanged;
        row.VolumeTotal = result.VolumeTotal;
        row.MarketPe = result.MarketPe;
        row.MarketPeExcludedCount = result.MarketPeExcludedCount;
        row.MarketDivYield = result.MarketDivYield;
        row.GainersJson = SerializeMovers(result.Gainers);
        row.LosersJson = SerializeMovers(result.Losers);
        // Ordered keys: the sector dictionary's iteration order follows the (unordered)
        // fact query, so pinning it keeps the stored bytes deterministic across runs.
        var orderedSectors = new SortedDictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var entry in result.SectorPerformance)
        {
            orderedSectors[entry.Key] = entry.Value;
        }

        row.SectorPerfJson = JsonSerializer.Serialize(orderedSectors, JsonOptions);
        row.AsOfTradingDate = date;
        row.ComputedAt = computedAt;
    }

    /// <summary>
    /// True when the stored row already carries the computed aggregate. Compares the
    /// input-derived values semantics only — never <c>computed_at</c> (which stamps the
    /// write, not the facts) and never raw JSON text (rows written before the sector keys
    /// were pinned carry a different, equally valid key order).
    /// </summary>
    private static bool HasSameValues(MarketSnapshot stored, MarketSnapshot computed) =>
        stored.Xu100Level == computed.Xu100Level
        && stored.Xu100ChangePct == computed.Xu100ChangePct
        && stored.Xu30Level == computed.Xu30Level
        && stored.Xu30ChangePct == computed.Xu30ChangePct
        && stored.BreadthAdvancing == computed.BreadthAdvancing
        && stored.BreadthDeclining == computed.BreadthDeclining
        && stored.BreadthUnchanged == computed.BreadthUnchanged
        && stored.VolumeTotal == computed.VolumeTotal
        && stored.MarketPe == computed.MarketPe
        && stored.MarketPeExcludedCount == computed.MarketPeExcludedCount
        && stored.MarketDivYield == computed.MarketDivYield
        && MoversEqual(stored.GainersJson, computed.GainersJson)
        && MoversEqual(stored.LosersJson, computed.LosersJson)
        && SectorsEqual(stored.SectorPerfJson, computed.SectorPerfJson)
        && stored.AsOfTradingDate == computed.AsOfTradingDate;

    /// <summary>Movers are ranked, so both the entries and their order must match.</summary>
    private static bool MoversEqual(string? stored, string? computed) =>
        DeserializeMovers(stored).SequenceEqual(DeserializeMovers(computed));

    /// <summary>Sector performance is a keyed object: the key order is not meaningful.</summary>
    private static bool SectorsEqual(string? stored, string? computed)
    {
        var left = DeserializeSectors(stored);
        var right = DeserializeSectors(computed);
        return left.Count == right.Count
            && left.All(entry => right.TryGetValue(entry.Key, out var value) && value == entry.Value);
    }

    private static List<MoverJson> DeserializeMovers(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<MoverJson>>(json, JsonOptions) ?? [];

    private static Dictionary<string, decimal> DeserializeSectors(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? new Dictionary<string, decimal>(StringComparer.Ordinal)
            : JsonSerializer.Deserialize<Dictionary<string, decimal>>(json, JsonOptions)
                ?? new Dictionary<string, decimal>(StringComparer.Ordinal);

    private static string SerializeMovers(IReadOnlyList<MoverEntry> movers) =>
        JsonSerializer.Serialize(
            movers.Select(m => new MoverJson(m.Symbol, m.ChangePct, m.Volume)),
            JsonOptions);

    /// <summary>The stored mover entry shape (02 §3.2: symbol, change_pct, volume).</summary>
    private sealed record MoverJson(
        [property: JsonPropertyName("symbol")] string Symbol,
        [property: JsonPropertyName("change_pct")] decimal ChangePct,
        [property: JsonPropertyName("volume")] long Volume);
}
