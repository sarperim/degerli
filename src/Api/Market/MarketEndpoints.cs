using System.Text.Json;
using System.Text.Json.Serialization;
using Degerli.Api.Infrastructure;
using Degerli.Ingestion.Macro;
using Degerli.Ingestion.Metrics;
using Degerli.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Api.Market;

/// <summary>
/// The market module's public read surface (`03-api-design.md` §3; FR-MOV-007..010,
/// FR-MOV-021; UC-MOV-001/003):
/// <list type="bullet">
/// <item><c>GET /api/v1/market/overview</c> — the latest completed
/// <c>market_snapshots</c> row (one row per trading day, BR-MOV-009): index levels +
/// daily change, breadth, total volume, market P/E (+ loss-maker excluded count) &
/// dividend yield, gainers/losers with volume, and cap-weighted sector performance —
/// served anonymously under the honest-data envelope. The latest row is the served one;
/// it carries <c>stale: true</c> only when a newer completed session's prices exist in
/// the store and the row lacks them (stored-prices freshness, last-known-good,
/// FR-MOV-010/016).</item>
/// <item><c>GET /api/v1/market/macro</c> — the six V1 macro series, each
/// <c>{code, value, unit, source, asOf, stale, state}</c>, resolved through the same
/// <see cref="MacroFreshnessService"/> the ingestion jobs use so staleness has one
/// definition. The inflation pair is always both entries (BR-MOV-002) and a series with
/// no data is an explicit <c>state: "unavailable"</c> entry, never silently dropped
/// (UXR-MOV-009).</item>
/// </list>
/// The two endpoints are separate because equity and macro have separate failure
/// domains (UC-MOV-002 vs UC-MDF-001): one outage degrades its own payload and must not
/// blank the other (SCR-001 §5). Both are anonymous (BR-MOV-007) and never compute
/// values — they read the canonical stores (BR-MDF-009).
/// </summary>
public static class MarketEndpoints
{
    /// <summary>
    /// The six V1 series in display order (`02` §3.2). Iterating a fixed list — not the
    /// seeded <c>macro_series</c> rows — guarantees the inflation pair is always both
    /// entries even if a definition row were missing (BR-MOV-002).
    /// </summary>
    private static readonly string[] SeriesOrder =
    [
        MacroSeries.TuikCpi,
        MacroSeries.IndepCpi,
        MacroSeries.CbrtRepo,
        MacroSeries.UsdTry,
        MacroSeries.EurTry,
        MacroSeries.Gold,
    ];

    public static IEndpointRouteBuilder MapDegerliMarketEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/market/overview", OverviewAsync).AllowAnonymous();
        api.MapGet("/market/macro", MacroAsync).AllowAnonymous();
        return api;
    }

    // ── Overview ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The equity blocks in one payload (TC-MOV-012): the latest completed snapshot row,
    /// or an honest <c>unavailable</c> state when no snapshot exists at all. The
    /// <c>stale</c> flag is resolved from the stored prices (TC-MOV-032): fresh unless a
    /// newer completed trading day has prices the served row does not cover.
    ///
    /// <paramref name="period"/> is the Could-level sector-performance period selector
    /// (FR-MOV-017/UXR-MOV-012, TC-MOV-015): <c>1w</c>/<c>1m</c>/<c>ytd</c> are the
    /// supported tokens and anything else is <c>400 VALIDATION_FAILED</c>. The
    /// period-aggregate formula is undefined (F-MOV-1, accepted 2026-10-07) and no period
    /// aggregates are computed or stored in V1, so a supported period serves the sector
    /// block as an honest empty "not yet computed" state rather than mislabelling the
    /// daily figure; when the feature is promoted the snapshot gains the aggregates and
    /// this branch reads them. The envelope (asOf/stale) is unchanged either way.
    /// </summary>
    private static async Task<IResult> OverviewAsync(
        DegerliDbContext db,
        MarketSnapshotStore store,
        TimeProvider clock,
        CancellationToken cancellationToken,
        string? period = null)
    {
        if (!TryNormalizePeriod(period, out var normalizedPeriod))
        {
            // An unsupported period is never silently ignored nor served as the daily
            // default (TC-MOV-015); a validation problem names the parameter (`03` §7).
            return Results.Problem(ApiProblem.Create(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationFailed,
                new Dictionary<string, object?>
                {
                    ["fields"] = new[] { new { field = "period", code = "unsupported" } },
                }));
        }

        var snapshot = await db.MarketSnapshots
            .OrderByDescending(row => row.SnapshotDate)
            .FirstOrDefaultAsync(cancellationToken);

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        if (snapshot is null)
        {
            // No completed snapshot exists: the equity pipeline is unavailable. The
            // response stays 200 with an explicit state and an empty shape — never an
            // error, never blank-as-zero (M-1, BR-RES-008).
            return Results.Ok(new
            {
                asOf = today,
                stale = false,
                state = DataStates.Unavailable,
                indices = (object?)null,
                breadth = (object?)null,
                volumeTotal = (decimal?)null,
                marketPe = (decimal?)null,
                marketPeExcludedCount = (int?)null,
                marketDivYield = (decimal?)null,
                gainers = Array.Empty<MoverPayload>(),
                losers = Array.Empty<MoverPayload>(),
                sectorPerformance = new Dictionary<string, decimal>(StringComparer.Ordinal),
            });
        }

        // Last-known-good (FR-MOV-010/016) — display freshness is stored-prices-based,
        // the same doctrine the write path applies (`01` §10.9.13, TKT-foundation-028):
        // the served row is stale only when a NEWER completed session's prices exist in
        // the store and the row lacks them (a genuinely missed `market-snapshot` run).
        // Stored prices are the truth, never a weekday rule: on a Tuesday whose EOD has
        // not landed, Monday IS the freshest completed session, so Monday's row is fresh.
        var latestStoredTradingDay = await store
            .ResolveLatestStoredTradingDayAsync(today, cancellationToken);
        var stale = latestStoredTradingDay is not null
            && snapshot.SnapshotDate < latestStoredTradingDay.Value;

        // Daily (no period) reads the locked cap-weighted daily aggregate; a supported
        // period serves its precomputed aggregate — empty until the feature is promoted
        // (F-MOV-1), never the daily figure relabelled as a period figure.
        var sectorPerformance = normalizedPeriod is null
            ? ReadSectors(snapshot.SectorPerfJson)
            : new Dictionary<string, decimal>(StringComparer.Ordinal);

        return Results.Ok(new
        {
            asOf = snapshot.SnapshotDate,
            stale,
            state = "available",
            indices = new
            {
                xu100 = new { level = snapshot.Xu100Level, changePct = snapshot.Xu100ChangePct },
                xu30 = new { level = snapshot.Xu30Level, changePct = snapshot.Xu30ChangePct },
            },
            breadth = new
            {
                advancing = snapshot.BreadthAdvancing,
                declining = snapshot.BreadthDeclining,
                unchanged = snapshot.BreadthUnchanged,
            },
            volumeTotal = snapshot.VolumeTotal,
            marketPe = snapshot.MarketPe,
            marketPeExcludedCount = snapshot.MarketPeExcludedCount,
            marketDivYield = snapshot.MarketDivYield,
            gainers = ReadMovers(snapshot.GainersJson),
            losers = ReadMovers(snapshot.LosersJson),
            sectorPerformance,
        });
    }

    /// <summary>
    /// The supported sector-performance period tokens (`03` §3, FR-MOV-017 Could).
    /// </summary>
    private static readonly string[] SupportedPeriods = ["1w", "1m", "ytd"];

    /// <summary>
    /// Resolves the optional sector-performance <paramref name="period"/> query value.
    /// Absent/blank means the daily view (<paramref name="normalized"/> is <c>null</c>);
    /// a supported token is returned lower-cased; anything else is rejected (TC-MOV-015).
    /// </summary>
    private static bool TryNormalizePeriod(string? period, out string? normalized)
    {
        if (string.IsNullOrWhiteSpace(period))
        {
            normalized = null;
            return true;
        }

        normalized = SupportedPeriods.FirstOrDefault(
            candidate => string.Equals(candidate, period, StringComparison.OrdinalIgnoreCase));
        return normalized is not null;
    }

    // ── Macro ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The macro strip (TC-MOV-013): six series, each value + freshness markers. An
    /// absent series is an explicit unavailable entry (UXR-MOV-009/FR-MOV-016).
    /// </summary>
    private static async Task<IResult> MacroAsync(
        DegerliDbContext db,
        MacroFreshnessService freshness,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        // Unit and source are properties of the series definition (FR-MOV-009, FR-MOV-014).
        var definitions = await db.MacroSeries
            .Select(series => new SeriesDefinition(series.Code, series.Unit, series.SourceName))
            .ToDictionaryAsync(row => row.Code, StringComparer.Ordinal, cancellationToken);

        var series = new List<MacroSeriesPayload>(SeriesOrder.Length);
        foreach (var code in SeriesOrder)
        {
            var state = await freshness.GetStateAsync(code, cancellationToken);
            definitions.TryGetValue(code, out var definition);

            // The definition owns the label; the freshness read owns value/as-of/stale.
            // `state` is availability, never freshness — staleness is the boolean flag.
            var available = state.Value is not null;
            series.Add(new MacroSeriesPayload(
                code,
                state.Value,
                definition?.Unit ?? state.Unit,
                definition?.SourceName,
                state.AsOf,
                state.Stale,
                available ? "available" : DataStates.Unavailable));
        }

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        // The strip is stale as a whole when any series is serving last-known-good data.
        var stale = series.Any(entry => entry.Stale);

        return Results.Ok(new { asOf = today, stale, series });
    }

    // ── Stored-shape decoding ────────────────────────────────────────────────

    /// <summary>The stored mover entry shape (`02` §3.2: symbol, change_pct, volume).</summary>
    private sealed record StoredMover(
        [property: JsonPropertyName("symbol")] string Symbol,
        [property: JsonPropertyName("change_pct")] decimal ChangePct,
        [property: JsonPropertyName("volume")] long Volume);

    /// <summary>The served mover entry (camelCase web shape).</summary>
    private sealed record MoverPayload(string Symbol, decimal ChangePct, long Volume);

    /// <summary>The served macro entry (`03` §3 macro payload shape).</summary>
    private sealed record MacroSeriesPayload(
        string Code,
        decimal? Value,
        string? Unit,
        string? Source,
        DateOnly? AsOf,
        bool Stale,
        string State);

    /// <summary>A macro series definition's serving metadata.</summary>
    private sealed record SeriesDefinition(string Code, string? Unit, string? SourceName);

    private static IReadOnlyList<MoverPayload> ReadMovers(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        var stored = JsonSerializer.Deserialize<List<StoredMover>>(json) ?? [];
        return stored.Select(row => new MoverPayload(row.Symbol, row.ChangePct, row.Volume)).ToList();
    }

    private static Dictionary<string, decimal> ReadSectors(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, decimal>(StringComparer.Ordinal);
        }

        return JsonSerializer.Deserialize<Dictionary<string, decimal>>(json)
            ?? new Dictionary<string, decimal>(StringComparer.Ordinal);
    }
}
