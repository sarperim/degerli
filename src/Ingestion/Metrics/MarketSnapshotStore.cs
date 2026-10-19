using System.Text.Json;
using System.Text.Json.Serialization;
using Degerli.Core.Metrics;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Ingestion.Metrics;

/// <summary>Outcome of a snapshot write pass (rides the run ledger's stats).</summary>
public sealed record MarketSnapshotWriteResult(int Written, int Unchanged);

/// <summary>
/// Fact storage for the market snapshot (02 §3.2). One row per trading day, idempotent
/// by <c>snapshot_date</c>: a re-run for the same date updates the existing row in place
/// rather than inserting a duplicate, and never touches another date's row (TC-MOV-005).
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
    public async Task<DateOnly?> ResolveLatestStoredTradingDayAsync(
        DateOnly onOrBefore,
        CancellationToken cancellationToken = default)
    {
        var xu100Id = await _db.Indices
            .Where(i => i.Code == "XU100")
            .Select(i => i.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (xu100Id == 0)
        {
            return null;
        }

        var memberIds = _db.IndexConstituents
            .Where(c => c.IndexId == xu100Id && c.EffectiveTo == null)
            .Select(c => c.InstrumentId);

        return await _db.DailyPrices
            .Where(p => memberIds.Contains(p.InstrumentId) && p.PriceDate <= onOrBefore)
            .MaxAsync(p => (DateOnly?)p.PriceDate, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// True when a completed snapshot row already exists for <paramref name="date"/> — the
    /// idempotency guard that makes a re-run for an already-snapshotted trading day a no-op
    /// (`01` §10.9.13; TC-MOV-005 one-row-per-trading-day): the job never recomputes or
    /// rewrites a completed day.
    /// </summary>
    public Task<bool> HasSnapshotAsync(DateOnly date, CancellationToken cancellationToken = default) =>
        _db.MarketSnapshots.AnyAsync(s => s.SnapshotDate == date, cancellationToken);

    /// <summary>Upserts the snapshot row for <paramref name="date"/>.</summary>
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

        var row = existing ?? new MarketSnapshot { SnapshotDate = date };
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
        row.SectorPerfJson = JsonSerializer.Serialize(result.SectorPerformance, JsonOptions);
        row.AsOfTradingDate = date;
        row.ComputedAt = computedAt;

        if (existing is null)
        {
            _db.MarketSnapshots.Add(row);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return existing is null
            ? new MarketSnapshotWriteResult(Written: 1, Unchanged: 0)
            : new MarketSnapshotWriteResult(Written: 0, Unchanged: 1);
    }

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
