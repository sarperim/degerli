using Degerli.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Ingestion.Macro;

/// <summary>
/// Per-series freshness for the macro strip (FR-MOV-010/016, NFR-MOV-001; FU §7). A series
/// is <c>stale</c> when either its <b>own</b> stored data has overrun the tolerable window
/// for its cadence, or its owning job really missed an ingest after that data was written;
/// it is <c>unavailable</c> when no value has ever been stored. The value always remains the
/// last-known one — nothing is blanked by a failure (FR-MOV-016).
/// <para>
/// TKT-foundation-038 (02 §5.6): the rule is <b>per series</b>, never per job alone — the
/// live <c>macro-cpi</c> job lands <c>partial</c> honestly every day (ENAG is admin-entered
/// and never present), so a job-scoped "failure after the last <c>succeeded</c> run" flag was
/// true forever and marked every monthly series stale while it served its latest published
/// value. A series' own <c>recorded_at</c> is the evidence that its ingest happened, and a
/// monthly series' window is measured from its <b>published value date</b>
/// (<see cref="MonthlyPublicationWindowDays"/>), not from the ingest stamp.
/// </para>
/// </summary>
public sealed class MacroFreshnessService
{
    /// <summary>The daily cadence tolerance, in days, measured from the latest ingest.</summary>
    public const int DailyToleranceDays = 1;

    /// <summary>
    /// The publication window of a monthly series (02 §5.6): a value published for month
    /// <c>M</c> stays fresh until its next expected publication window closes — 65 days
    /// covers TÜİK's M+1-early publication and the CBRT's ≤2-month meeting cycle. Measured
    /// from the value date (<c>asOf</c>), never from <c>recorded_at</c> (the ingest
    /// re-records the same rows on every run). This is the one documented knob; changing the
    /// window is this constant, never a semantics change.
    /// </summary>
    public const int MonthlyPublicationWindowDays = 65;

    /// <summary>
    /// The job whose run ledger is the failure clock for each series (`02` §3.2 series set;
    /// TKT-foundation-027's ingest wiring). <c>null</c> = the series has <b>no owning job</b>:
    /// <c>INDEP_CPI</c> is admin-entered (<c>per_release</c>, ENAG is not on EVDS), so a
    /// failure of the <c>macro-cpi</c> job that also carries the monthly pair must never mark
    /// it stale — its honest state is <c>unavailable</c> until a value exists. An
    /// unlisted series is treated the same way: no failure clock rather than a guessed one.
    /// </summary>
    private static readonly Dictionary<string, string?> OwningJobBySeries = new(StringComparer.Ordinal)
    {
        [MacroSeries.UsdTry] = MacroDailyJob.Code,
        [MacroSeries.EurTry] = MacroDailyJob.Code,
        [MacroSeries.Gold] = MacroDailyJob.Code,
        [MacroSeries.TuikCpi] = MacroCpiJob.Code,
        [MacroSeries.CbrtRepo] = MacroCpiJob.Code,
        [MacroSeries.IndepCpi] = null,
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

        var recordedAt = latestIngest?.RecordedAt;
        var value = latestByValue?.Value;
        var asOf = latestByValue?.ValueDate;

        var failureStale = await IsFailureStaleAsync(seriesCode, recordedAt, cancellationToken)
            .ConfigureAwait(false);

        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        var ageStale = IsAgeStale(series.Cadence, recordedAt, asOf, today);

        // An unavailable series carries its honest state, never a stale flag — there is no
        // last-known value to be trailing (02 §5.6, the strip envelope aggregates available
        // series only).
        var stale = value is not null && (failureStale || ageStale);
        var state = value is null ? "unavailable" : stale ? "stale" : "fresh";

        return new MacroSeriesState(
            seriesCode,
            value,
            series.Unit,
            asOf,
            recordedAt,
            stale,
            state);
    }

    /// <summary>
    /// The per-series failure clock: the owning job's latest failure counts only when it is
    /// newer than the evidence that this series' ingest works — the series' own latest
    /// <c>recorded_at</c> (a <c>partial</c> run that wrote the series refreshes it) and the
    /// job's latest <c>succeeded</c> run (a recovered job is not stale). An outright failed
    /// run that wrote nothing leaves both clocks old, so staleness stays honest.
    /// </summary>
    private async Task<bool> IsFailureStaleAsync(
        string seriesCode,
        DateTimeOffset? recordedAt,
        CancellationToken cancellationToken)
    {
        if (!OwningJobBySeries.TryGetValue(seriesCode, out var jobCode) || jobCode is null)
        {
            // No owning job (admin-entered): there is no run ledger to be stale against.
            return false;
        }

        var lastSuccess = await _db.IngestRuns
            .Where(r => r.JobCode == jobCode && r.Status == "succeeded")
            .MaxAsync(r => (DateTimeOffset?)r.FinishedAt, cancellationToken)
            .ConfigureAwait(false);
        var lastFailure = await _db.IngestRuns
            .Where(r => r.JobCode == jobCode && r.Status == "failed")
            .MaxAsync(r => (DateTimeOffset?)r.FinishedAt, cancellationToken)
            .ConfigureAwait(false);

        if (lastFailure is not { } failure)
        {
            return false;
        }

        var evidence = recordedAt is { } ingested && (lastSuccess is null || ingested > lastSuccess)
            ? ingested
            : lastSuccess;

        return evidence is null || failure > evidence;
    }

    /// <summary>
    /// The age clock per cadence: <c>daily</c> keeps measuring from the latest ingest (the
    /// rule that works today); <c>monthly</c> is publication-aware and measured from the
    /// published value date, because the ingest re-records the same monthly rows on every run
    /// (so <c>recorded_at</c> can never reveal a missed publication); <c>per_release</c> has
    /// no fixed window and is stale only on the per-series failure rule.
    /// </summary>
    private static bool IsAgeStale(string? cadence, DateTimeOffset? recordedAt, DateOnly? asOf, DateOnly today) =>
        cadence switch
        {
            "daily" => recordedAt is { } ingest
                && today.DayNumber - DateOnly.FromDateTime(ingest.UtcDateTime).DayNumber > DailyToleranceDays,
            "monthly" => asOf is { } published
                && today.DayNumber - published.DayNumber > MonthlyPublicationWindowDays,
            _ => false,
        };
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
