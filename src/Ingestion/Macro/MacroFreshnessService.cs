using Degerli.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Ingestion.Macro;

/// <summary>
/// Per-series freshness for the macro strip (FR-MOV-010/016, NFR-MOV-001; FU §7). A series
/// is <c>stale</c> when either its latest ingest (<c>recorded_at</c>) has overrun the
/// tolerable window for its cadence, or the owning job's most recent run failed after its
/// most recent success; it is <c>unavailable</c> when no value has ever been stored. The
/// value always remains the last-known one — nothing is blanked by a failure (FR-MOV-016).
/// </summary>
public sealed class MacroFreshnessService
{
    // FU §7 / 02 §5.6 — the tolerable window per cadence. per_release has no fixed window,
    // so it is stale only on an explicit ingest failure.
    private static readonly Dictionary<string, int?> ToleranceDays = new(StringComparer.Ordinal)
    {
        ["daily"] = 1,
        ["monthly"] = 31,
        ["per_release"] = null,
    };

    private readonly DegerliDbContext _db;
    private readonly TimeProvider _clock;

    public MacroFreshnessService(DegerliDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>The serving state of one series (value + as-of + stale/state markers).</summary>
    public async Task<MacroSeriesState> GetStateAsync(
        string seriesCode,
        CancellationToken cancellationToken = default)
    {
        var series = await _db.MacroSeries
            .SingleOrDefaultAsync(s => s.Code == seriesCode, cancellationToken)
            .ConfigureAwait(false);

        if (series is null)
        {
            return new MacroSeriesState(seriesCode, null, null, null, null, Stale: false, State: "unavailable");
        }

        var latestByValue = await _db.MacroValues
            .Where(v => v.SeriesCode == seriesCode)
            .OrderByDescending(v => v.ValueDate)
            .ThenByDescending(v => v.RecordedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var latestIngest = await _db.MacroValues
            .Where(v => v.SeriesCode == seriesCode)
            .OrderByDescending(v => v.RecordedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var jobCode = string.Equals(series.Cadence, "daily", StringComparison.Ordinal)
            ? MacroDailyJob.Code
            : MacroCpiJob.Code;

        var lastSuccess = await _db.IngestRuns
            .Where(r => r.JobCode == jobCode && r.Status == "succeeded")
            .MaxAsync(r => (DateTimeOffset?)r.FinishedAt, cancellationToken)
            .ConfigureAwait(false);
        var lastFailure = await _db.IngestRuns
            .Where(r => r.JobCode == jobCode && r.Status == "failed")
            .MaxAsync(r => (DateTimeOffset?)r.FinishedAt, cancellationToken)
            .ConfigureAwait(false);

        var failureStale = lastFailure is { } failure
            && (lastSuccess is null || failure > lastSuccess);

        var tolerance = series.Cadence is { } cadence && ToleranceDays.TryGetValue(cadence, out var days)
            ? days
            : null;
        var ageStale = tolerance is { } maxAge
            && latestIngest is { } ingest
            && DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime).DayNumber
                - DateOnly.FromDateTime(ingest.RecordedAt.UtcDateTime).DayNumber > maxAge;

        var stale = failureStale || ageStale;

        var value = latestByValue?.Value;
        var asOf = latestByValue?.ValueDate;
        var state = value is null ? "unavailable" : stale ? "stale" : "fresh";

        return new MacroSeriesState(
            seriesCode,
            value,
            series.Unit,
            asOf,
            latestIngest?.RecordedAt,
            stale,
            state);
    }
}

/// <summary>The observable serving state of one macro series (03 §3 macro payload shape).</summary>
public sealed record MacroSeriesState(
    string Code,
    decimal? Value,
    string? Unit,
    DateOnly? AsOf,
    DateTimeOffset? RecordedAt,
    bool Stale,
    string State);
