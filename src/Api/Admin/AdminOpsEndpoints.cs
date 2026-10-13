using System.Globalization;
using Degerli.Api.Infrastructure;
using Degerli.Ingestion.Coverage;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Macro;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Api.Admin;

/// <summary>Optional body of <c>POST /api/v1/admin/ingest/{job}/run</c> (UC-MDF-002).</summary>
public sealed record IngestTriggerRequest(string? BackfillFrom);

/// <summary>
/// The TKT-mdf-010 admin ops endpoints (`03` §8, AD-13):
/// <list type="bullet">
/// <item><c>GET /api/v1/admin/summary</c> — the ops one-glance: last run per job, per
/// data-type freshness (jobs + macro series, with staleness), description coverage
/// counts and the open-quarantine count (UXR-MDF-003..006).</item>
/// <item><c>GET /api/v1/admin/coverage?scope=instrument|fund</c> — the coverage report:
/// per-instrument/per-fund data types with their from/to dates, listing date and
/// explicit gaps — never a silent omission (FR-MDF-011, UXR-MDF-011/013).</item>
/// <item><c>GET /api/v1/admin/ingest-runs?job=&amp;status=</c> — the run ledger,
/// filterable by job and status conjunctively (UXR-MDF-014).</item>
/// <item><c>POST /api/v1/admin/ingest/{job}/run</c> — trigger a job/backfill; the body
/// may carry <c>{backfillFrom}</c> (UC-MDF-002, UXR-MDF-016/017).</item>
/// <item><c>GET /api/v1/admin/stats</c> — aggregate-only counts, no per-user data
/// (SC-008/OBJ-005, UXR-MDF-026).</item>
/// </list>
/// </summary>
public static class AdminOpsEndpoints
{
    private const string UniverseIndexCode = "XU100";
    private const string OpenStatus = "open";
    private const string PublishedStatus = "published";
    private const string DraftStatus = "draft";
    private const string InstrumentScope = "instrument";
    private const string FundScope = "fund";

    /// <summary>The prefix that marks a macro series as a freshness data type.</summary>
    private const string MacroDataTypePrefix = "macro-";

    public static RouteGroupBuilder MapDegerliAdminOpsEndpoints(this RouteGroupBuilder admin)
    {
        admin.MapGet("/summary", SummaryAsync);
        admin.MapGet("/coverage", CoverageAsync);
        admin.MapGet("/ingest-runs", IngestRunsAsync);
        admin.MapPost("/ingest/{job}/run", TriggerAsync);
        admin.MapGet("/stats", StatsAsync);
        return admin;
    }

    // ---------------------------------------------------------------------------------
    // GET /admin/summary
    // ---------------------------------------------------------------------------------

    private static async Task<IResult> SummaryAsync(
        DegerliDbContext db,
        IIngestionJobRunner runner,
        MacroFreshnessService macroFreshness,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        var runs = await db.IngestRuns
            .Select(run => new RunRow(run.JobCode, run.Status, run.FinishedAt, run.Id))
            .ToListAsync(cancellationToken);

        // Per-job last run (UXR-MDF-003): every registered job is listed, with a null
        // lastRun until it has ever run.
        var lastRunByJob = runs
            .GroupBy(run => run.JobCode, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(run => run.FinishedAt)
                    .ThenByDescending(run => run.Id)
                    .First(),
                StringComparer.Ordinal);

        var jobs = runner.JobCodes
            .Select(code => new
            {
                job = code,
                lastRun = lastRunByJob.TryGetValue(code, out var run)
                    ? new { status = run.Status, finishedAt = run.FinishedAt }
                    : null,
            })
            .ToList();

        // Per-data-type freshness (UXR-MDF-004): the ingest jobs' data types plus the
        // macro series. A job data type is stale when its most recent run did not
        // succeed (the last-known-good value is being served); a macro series carries
        // the macro freshness service's own stale/unavailable verdict.
        var lastSuccessByJob = runs
            .Where(run => run.Status == IngestResult.Succeeded)
            .GroupBy(run => run.JobCode, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Max(run => run.FinishedAt),
                StringComparer.Ordinal);

        var freshness = new List<object>();
        foreach (var code in runner.JobCodes)
        {
            lastSuccessByJob.TryGetValue(code, out var lastSuccess);
            lastRunByJob.TryGetValue(code, out var lastRun);

            freshness.Add(new
            {
                dataType = code,
                lastSuccess = ToDate(lastSuccess),
                stale = lastRun is not null && lastRun.Status != IngestResult.Succeeded,
            });
        }

        var macroCodes = await db.MacroSeries
            .OrderBy(series => series.Code)
            .Select(series => series.Code)
            .ToListAsync(cancellationToken);
        foreach (var code in macroCodes)
        {
            var state = await macroFreshness.GetStateAsync(code, cancellationToken);
            freshness.Add(new
            {
                dataType = MacroDataType(code),
                lastSuccess = ToDate(state.RecordedAt),
                stale = state.Stale,
            });
        }

        var universe = await CurrentUniverseCountAsync(db, cancellationToken);
        var latestDescriptionStatuses = await db.BusinessDescriptions
            .GroupBy(description => description.InstrumentId)
            .Select(group => group
                .OrderByDescending(description => description.Version)
                .First()
                .Status)
            .ToListAsync(cancellationToken);

        return Results.Ok(new
        {
            asOf = today,
            jobs,
            freshness,
            contentCoverage = new
            {
                descriptionsPublished = latestDescriptionStatuses.Count(status => status == PublishedStatus),
                descriptionsDraft = latestDescriptionStatuses.Count(status => status == DraftStatus),
                universe,
            },
            openQuarantineCount = await db.QuarantinedFacts
                .CountAsync(fact => fact.Status == OpenStatus, cancellationToken),
        });
    }

    // ---------------------------------------------------------------------------------
    // GET /admin/coverage
    // ---------------------------------------------------------------------------------

    private static async Task<IResult> CoverageAsync(
        string? scope,
        DegerliDbContext db,
        CancellationToken cancellationToken)
    {
        var requested = string.IsNullOrWhiteSpace(scope) ? InstrumentScope : scope.Trim();
        return requested switch
        {
            InstrumentScope => Results.Ok(await InstrumentCoverageAsync(db, cancellationToken)),
            FundScope => Results.Ok(await FundCoverageAsync(db, cancellationToken)),
            _ => Results.Problem(ApiProblem.Create(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationFailed,
                new Dictionary<string, object?>
                {
                    ["fields"] = new[] { new { field = "scope", code = "unsupported" } },
                })),
        };
    }

    private static async Task<object> InstrumentCoverageAsync(
        DegerliDbContext db,
        CancellationToken cancellationToken)
    {
        var universe = await (
                from instrument in db.Instruments
                join constituent in db.IndexConstituents on instrument.Id equals constituent.InstrumentId
                join index in db.Indices on constituent.IndexId equals index.Id
                where index.Code == UniverseIndexCode && constituent.EffectiveTo == null
                orderby instrument.Symbol
                select new { instrument.Id, instrument.Symbol, instrument.Name, instrument.ListingDate })
            .ToListAsync(cancellationToken);

        var instrumentIds = universe.Select(row => row.Id).ToList();
        var coverageRows = await db.CoverageMetadata
            .Where(row => row.Scope == InstrumentScope
                && row.InstrumentId != null
                && instrumentIds.Contains(row.InstrumentId.Value))
            .ToListAsync(cancellationToken);

        var byInstrument = coverageRows
            .GroupBy(row => row.InstrumentId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());

        var instruments = universe
            .Select(instrument => new
            {
                symbol = instrument.Symbol,
                name = instrument.Name,
                listingDate = instrument.ListingDate,
                dataTypes = BuildDataTypes(
                    byInstrument.TryGetValue(instrument.Id, out var rows) ? rows : [],
                    CoverageDataTypes.InstrumentScoped),
            })
            .ToList();

        return new { scope = InstrumentScope, instruments };
    }

    private static async Task<object> FundCoverageAsync(
        DegerliDbContext db,
        CancellationToken cancellationToken)
    {
        var funds = await db.Funds
            .OrderBy(fund => fund.Code)
            .Select(fund => new { fund.Code, fund.Name })
            .ToListAsync(cancellationToken);

        var fundCodes = funds.Select(fund => fund.Code).ToList();
        var coverageRows = await db.CoverageMetadata
            .Where(row => row.Scope == FundScope
                && row.FundId != null
                && fundCodes.Contains(row.FundId))
            .ToListAsync(cancellationToken);

        var byFund = coverageRows
            .GroupBy(row => row.FundId!)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        var covered = funds
            .Select(fund => new
            {
                code = fund.Code,
                name = fund.Name,
                dataTypes = BuildDataTypes(
                    byFund.TryGetValue(fund.Code, out var rows) ? rows : [],
                    CoverageDataTypes.FundScoped),
            })
            .ToList();

        return new { scope = FundScope, funds = covered };
    }

    /// <summary>
    /// Projects coverage rows for one entity into the report's data-type entries and
    /// guarantees every scope-appropriate data type appears — an absent type is rendered
    /// as an explicit gap, never omitted (NFR-MDF-003, NFR-FDF-001, UXR-MDF-011/012).
    /// Extra recorded data types (e.g. <c>cash_flow</c>) ride along too.
    /// </summary>
    private static List<object> BuildDataTypes(
        IReadOnlyList<CoverageMetadata> rows,
        IReadOnlyList<string> guaranteedDataTypes)
    {
        var byType = rows.ToDictionary(row => row.DataType, StringComparer.Ordinal);
        var entries = new List<object>();

        foreach (var dataType in guaranteedDataTypes)
        {
            byType.TryGetValue(dataType, out var row);
            entries.Add(new
            {
                dataType,
                present = row?.AvailableFrom is not null,
                availableFrom = row?.AvailableFrom,
                availableTo = row?.AvailableTo,
                notes = row?.Notes,
            });
        }

        foreach (var row in rows.Where(row =>
                     !guaranteedDataTypes.Contains(row.DataType, StringComparer.Ordinal)))
        {
            entries.Add(new
            {
                dataType = row.DataType,
                present = row.AvailableFrom is not null,
                availableFrom = row.AvailableFrom,
                availableTo = row.AvailableTo,
                notes = row.Notes,
            });
        }

        return entries;
    }

    // ---------------------------------------------------------------------------------
    // GET /admin/ingest-runs
    // ---------------------------------------------------------------------------------

    private static async Task<IResult> IngestRunsAsync(
        string? job,
        string? status,
        DegerliDbContext db,
        CancellationToken cancellationToken)
    {
        var query = db.IngestRuns.AsQueryable();

        if (!string.IsNullOrWhiteSpace(job))
        {
            var jobCode = job.Trim();
            query = query.Where(run => run.JobCode == jobCode);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var runStatus = status.Trim();
            query = query.Where(run => run.Status == runStatus);
        }

        var runs = await query
            .OrderByDescending(run => run.FinishedAt)
            .ThenByDescending(run => run.Id)
            .Select(run => new
            {
                id = run.Id,
                job = run.JobCode,
                status = run.Status,
                startedAt = run.StartedAt,
                finishedAt = run.FinishedAt,
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(new { runs });
    }

    // ---------------------------------------------------------------------------------
    // POST /admin/ingest/{job}/run
    // ---------------------------------------------------------------------------------

    private static async Task<IResult> TriggerAsync(
        string job,
        IngestTriggerRequest? request,
        IIngestionJobRunner runner,
        DegerliDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        // The job code is a path id drawn from the `03` §8 catalog; an unregistered code
        // is a not-found, not a validation failure (TC-MDF-042).
        if (!runner.JobCodes.Contains(job, StringComparer.Ordinal))
        {
            return Results.Problem(ApiProblem.Create(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.NotFound,
                new Dictionary<string, object?> { ["job"] = job }));
        }

        DateOnly? backfillFrom = null;
        if (!string.IsNullOrWhiteSpace(request?.BackfillFrom))
        {
            if (!DateOnly.TryParseExact(
                    request.BackfillFrom!.Trim(),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsed))
            {
                return Results.Problem(ApiProblem.Create(
                    StatusCodes.Status400BadRequest,
                    ApiErrorCodes.ValidationFailed,
                    new Dictionary<string, object?>
                    {
                        ["fields"] = new[] { new { field = "backfillFrom", code = "invalid-date" } },
                    }));
            }

            backfillFrom = parsed;
        }

        var target = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var result = await runner
            .RunAsync(job, new IngestionRequest(target, backfillFrom), cancellationToken)
            .ConfigureAwait(false);

        var runId = await db.IngestRuns
            .Where(run => run.JobCode == job)
            .OrderByDescending(run => run.Id)
            .Select(run => (long?)run.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return Results.Ok(new
        {
            runId,
            job,
            status = result.Status,
            written = result.Written,
            unchanged = result.Unchanged,
            quarantined = result.Quarantined,
        });
    }

    // ---------------------------------------------------------------------------------
    // GET /admin/stats
    // ---------------------------------------------------------------------------------

    private static async Task<IResult> StatsAsync(
        DegerliDbContext db,
        CancellationToken cancellationToken)
    {
        // Aggregate-only, no per-user surface (KVKK posture, BR-ACC-002/008):
        // exactly these four numbers are ever returned (TC-MDF-044).
        var registered = await db.Users.CountAsync(cancellationToken);
        var verified = await db.Users.CountAsync(user => user.EmailConfirmed, cancellationToken);
        var savedScreens = await db.SavedScreens.CountAsync(cancellationToken);
        var dcfScenarios = await db.DcfScenarios.CountAsync(cancellationToken);

        return Results.Ok(new
        {
            accounts = new { registered, verified },
            savedScreens,
            dcfScenarios,
        });
    }

    // ---------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------

    private static async Task<int> CurrentUniverseCountAsync(
        DegerliDbContext db,
        CancellationToken cancellationToken) =>
        await (
                from instrument in db.Instruments
                join constituent in db.IndexConstituents on instrument.Id equals constituent.InstrumentId
                join index in db.Indices on constituent.IndexId equals index.Id
                where index.Code == UniverseIndexCode && constituent.EffectiveTo == null
                select instrument.Id)
            .CountAsync(cancellationToken);

    /// <summary>The freshness data-type key for a macro series (e.g. <c>macro-indep-cpi</c>).</summary>
    private static string MacroDataType(string seriesCode) =>
        MacroDataTypePrefix + seriesCode.ToLowerInvariant().Replace('_', '-');

    private static DateOnly? ToDate(DateTimeOffset? timestamp) =>
        timestamp is { } value ? DateOnly.FromDateTime(value.UtcDateTime) : null;

    private sealed record RunRow(string JobCode, string? Status, DateTimeOffset? FinishedAt, long Id);
}
