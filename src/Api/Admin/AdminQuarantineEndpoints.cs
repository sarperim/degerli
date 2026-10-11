using System.Text.Json;
using Degerli.Api.Infrastructure;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Api.Admin;

/// <summary>Body of <c>POST /api/v1/admin/quarantine/{id}/dismiss</c> — the resolution
/// note that records the accepted gap (BR-MDF-007).</summary>
public sealed record DismissQuarantineRequest(string? Note);

/// <summary>
/// The TKT-mdf-011 quarantine review endpoints (`03` §8, AD-13, FR-MDF-012):
/// <list type="bullet">
/// <item><c>GET /api/v1/admin/quarantine?status=open|dismissed</c> — the validation-failure
/// review queue: rejected payloads with their job, source reference, reason code,
/// quarantine date and the inspectable payload (UXR-MDF-007). The <c>status</c> filter
/// scopes the queue to the open review set or the retained dismissals (UXR-MDF-009).</item>
/// <item><c>POST /api/v1/admin/quarantine/{id}/dismiss</c> <c>{note}</c> — records the
/// accepted gap and moves the item to <c>dismissed</c>; the note is required (inline
/// validation, UXR-MDF-008) and nothing is ever deleted (BR-MDF-007). Re-ingestion is
/// re-triggering the job after a fix (<c>POST /admin/ingest/{job}/run</c>), not a
/// dismissal.</item>
/// </list>
/// This file owns only the quarantine endpoints; the shared <c>/admin</c> route group and
/// its role gate live in <see cref="AdminEndpoints"/> (TKT-mdf-010).
/// </summary>
public static class AdminQuarantineEndpoints
{
    private const string OpenStatus = "open";
    private const string DismissedStatus = "dismissed";

    public static RouteGroupBuilder MapDegerliAdminQuarantineEndpoints(this RouteGroupBuilder admin)
    {
        admin.MapGet("/quarantine", ListAsync);
        admin.MapPost("/quarantine/{id}/dismiss", DismissAsync);
        return admin;
    }

    // ---------------------------------------------------------------------------------
    // GET /admin/quarantine?status=open|dismissed
    // ---------------------------------------------------------------------------------

    private static async Task<IResult> ListAsync(
        string? status,
        DegerliDbContext db,
        CancellationToken cancellationToken)
    {
        var query = db.QuarantinedFacts.AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var requested = status.Trim();
            query = query.Where(fact => fact.Status == requested);
        }

        var facts = await query
            .OrderByDescending(fact => fact.QuarantinedAt)
            .ThenByDescending(fact => fact.Id)
            .ToListAsync(cancellationToken);

        return Results.Ok(new { items = facts.Select(Project).ToList() });
    }

    // ---------------------------------------------------------------------------------
    // POST /admin/quarantine/{id}/dismiss
    // ---------------------------------------------------------------------------------

    private static async Task<IResult> DismissAsync(
        long id,
        DismissQuarantineRequest? request,
        DegerliDbContext db,
        CancellationToken cancellationToken)
    {
        // The note is required (UXR-MDF-008): a dismissal must record the accepted gap,
        // never a silent drop (BR-MDF-007).
        var note = request?.Note?.Trim();
        if (string.IsNullOrEmpty(note))
        {
            return Results.Problem(ApiProblem.Create(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationFailed,
                new Dictionary<string, object?>
                {
                    ["fields"] = new[] { new { field = "note", code = "required" } },
                }));
        }

        var fact = await db.QuarantinedFacts
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (fact is null)
        {
            return Results.Problem(ApiProblem.Create(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.NotFound,
                new Dictionary<string, object?> { ["id"] = id }));
        }

        // open → dismissed is the terminal transition; re-dismissing a retained item leaves
        // its recorded gap intact rather than overwriting it.
        if (fact.Status == OpenStatus)
        {
            fact.Status = DismissedStatus;
            fact.ResolutionNote = note;
            await db.SaveChangesAsync(cancellationToken);
        }

        return Results.Ok(Project(fact));
    }

    // ---------------------------------------------------------------------------------
    // Projection
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Projects a quarantine row for the review queue: the rejected payload is parsed back
    /// from its <c>jsonb</c> storage so it is served as inspectable JSON (UXR-MDF-007),
    /// matching how the other jsonb-backed read endpoints surface stored JSON.
    /// </summary>
    private static object Project(QuarantinedFact fact) => new
    {
        id = fact.Id,
        jobCode = fact.JobCode,
        sourceRef = fact.SourceRef,
        reasonCode = fact.ReasonCode,
        quarantinedAt = fact.QuarantinedAt,
        payloadJson = ParsePayload(fact.PayloadJson),
        status = fact.Status,
        resolutionNote = fact.ResolutionNote,
    };

    private static JsonElement? ParsePayload(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        using var document = JsonDocument.Parse(payloadJson);
        return document.RootElement.Clone();
    }
}
