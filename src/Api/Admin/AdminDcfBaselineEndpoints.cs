using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Valuation;
using Degerli.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Api.Admin;

/// <summary>
/// The builder-only DCF baseline regeneration endpoint (<c>03-api-design.md</c> §8;
/// UC-VAL-003; TKT-val-009): <c>POST /api/v1/admin/dcf-baselines/regenerate</c>.
///
/// Baselines are <i>derived rows</i>, never hand-edited content (<c>02</c> §5.8): the
/// endpoint triggers the C3b <c>dcf-baselines</c> job through the registered runner, so
/// the run is recorded in the ingest ledger and every covered stock's baseline is
/// re-derived from the canonical facts with a version bump — one active version per
/// stock, prior versions retained (BR-VAL-008). The route group carries the shared
/// builder gate and admin rate limit, so anonymous callers get <c>401</c> and
/// signed-in non-builders <c>403</c> (AD-13).
/// </summary>
public static class AdminDcfBaselineEndpoints
{
    public static RouteGroupBuilder MapDegerliAdminDcfBaselineEndpoints(this RouteGroupBuilder admin)
    {
        admin.MapPost("/dcf-baselines/regenerate", RegenerateAsync);
        return admin;
    }

    private static async Task<IResult> RegenerateAsync(
        IIngestionJobRunner runner,
        DegerliDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var target = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var result = await runner
            .RunAsync(DcfBaselineGenerationJob.Code, new IngestionRequest(target), cancellationToken)
            .ConfigureAwait(false);

        // The runner appends the ledger row (FR-MDF-009); surface its id so the admin
        // UI can point at the run that is now visible in the ledger (UXR-MDF-025).
        var runId = await db.IngestRuns
            .Where(run => run.JobCode == DcfBaselineGenerationJob.Code)
            .OrderByDescending(run => run.Id)
            .Select(run => (long?)run.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return Results.Ok(new
        {
            runId,
            job = DcfBaselineGenerationJob.Code,
            status = result.Status,
            written = result.Written,
            unchanged = result.Unchanged,
            quarantined = result.Quarantined,
        });
    }
}
