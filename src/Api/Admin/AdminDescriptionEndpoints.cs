using System.Text.Json;
using Degerli.Api.Infrastructure;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Api.Admin;

/// <summary>
/// The TKT-res-006 admin descriptions endpoints (`03` §8 v1.2, AD-13; FR-RES-020/021,
/// UC-RES-004 steps 2–4, UXR-MDF-018..022 API side):
/// <list type="bullet">
/// <item><c>GET /api/v1/admin/descriptions?status=draft|reviewed|published</c> — the
/// review queue that <b>is</b> the editor payload: per entry the stock, status, version,
/// <b>both</b> language texts and the KAP <c>sourceRefs</c> resolved against
/// <c>kap_disclosures</c> (id, type, publish date, title, URL). An id in
/// <c>source_refs_json</c> that no longer resolves is served as a <c>{disclosureId}</c>-only
/// stub — never silently dropped. The wrapped root (<c>descriptions[]</c>) keeps the
/// response additively evolvable (`03` §11).</item>
/// <item><c>PATCH /api/v1/admin/descriptions/{id}</c> <c>{textTr?, textEn?}</c> — edits
/// both languages (FR-RES-021). Provenance is not editable: a <c>sourceRefs</c> write is
/// rejected <c>400 VALIDATION_FAILED</c> (strict editor contract, I-RES-2).</item>
/// <item><c>POST /api/v1/admin/descriptions/{id}/publish</c> — the publication gate: both
/// languages required, a missing one is <c>400 VALIDATION_FAILED</c> naming it (I-RES-1);
/// the DB CHECK (BR-RES-003) is the backstop. Publishing flips the public page from
/// <c>preparing</c> to <c>published</c> (FR-RES-020/026).</item>
/// </list>
/// This file owns only the descriptions endpoints; the shared gated <c>/admin</c> route
/// group lives in <see cref="AdminEndpoints"/> (TKT-mdf-010). The C4 drafting CLI and the
/// admin UI are out of scope.
/// </summary>
public static class AdminDescriptionEndpoints
{
    /// <summary>The publication gate's required fields, in a stable order.</summary>
    private static readonly string[] PublishGateFields = ["textTr", "textEn"];

    public static RouteGroupBuilder MapDegerliAdminDescriptionEndpoints(this RouteGroupBuilder admin)
    {
        admin.MapGet("/descriptions", ListAsync);
        admin.MapPatch("/descriptions/{id:long}", PatchAsync);
        admin.MapPost("/descriptions/{id:long}/publish", PublishAsync);
        return admin;
    }

    // ---------------------------------------------------------------------------------
    // GET /admin/descriptions?status=
    // ---------------------------------------------------------------------------------

    private static async Task<IResult> ListAsync(
        string? status,
        DegerliDbContext db,
        CancellationToken cancellationToken)
    {
        var query =
            from description in db.BusinessDescriptions
            join instrument in db.Instruments on description.InstrumentId equals instrument.Id
            select new { description, instrument };

        if (!string.IsNullOrWhiteSpace(status))
        {
            var requested = status.Trim();
            query = query.Where(row => row.description.Status == requested);
        }

        var rows = await query
            .OrderBy(row => row.instrument.Symbol)
            .ThenBy(row => row.description.Version)
            .Select(row => new DescriptionRow(
                row.description.Id,
                row.instrument.Symbol,
                row.instrument.Name,
                row.description.Version,
                row.description.Status,
                row.description.TextTr,
                row.description.TextEn,
                row.description.SourceRefsJson,
                row.description.LastReviewedAt,
                row.description.PublishedAt))
            .ToListAsync(cancellationToken);

        var refsByDescription = rows
            .Select(row => row.SourceRefsJson)
            .Where(json => !string.IsNullOrWhiteSpace(json))
            .SelectMany(ParseSourceRefIds)
            .Distinct()
            .ToList();

        var disclosures = await db.KapDisclosures
            .Where(disclosure => refsByDescription.Contains(disclosure.Id))
            .ToDictionaryAsync(disclosure => disclosure.Id, cancellationToken);

        var descriptions = rows.Select(row => Project(row, disclosures)).ToList();

        return Results.Ok(new { descriptions });
    }

    // ---------------------------------------------------------------------------------
    // PATCH /admin/descriptions/{id}
    // ---------------------------------------------------------------------------------

    private static async Task<IResult> PatchAsync(
        long id,
        HttpRequest request,
        DegerliDbContext db,
        CancellationToken cancellationToken)
    {
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return Validation([("body", "invalid_json")]);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Validation([("body", "invalid_json")]);
            }

            // Provenance is pipeline-written only: a sourceRefs field is rejected outright
            // and no accompanying text change is applied (I-RES-2, `03` §8 v1.2).
            if (root.TryGetProperty("sourceRefs", out _))
            {
                return Validation([("sourceRefs", "not_editable")]);
            }

            var hasTextTr = root.TryGetProperty("textTr", out var textTrElement);
            var hasTextEn = root.TryGetProperty("textEn", out var textEnElement);

            var fields = new List<(string Field, string Code)>();
            if (!hasTextTr && !hasTextEn)
            {
                fields.Add(("body", "required"));
            }

            var newTextTr = ReadText(textTrElement, hasTextTr, "textTr", fields);
            var newTextEn = ReadText(textEnElement, hasTextEn, "textEn", fields);

            if (fields.Count > 0)
            {
                return Validation(fields);
            }

            var description = await db.BusinessDescriptions
                .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
            if (description is null)
            {
                return Results.Problem(ApiProblem.Create(
                    StatusCodes.Status404NotFound,
                    ApiErrorCodes.NotFound,
                    new Dictionary<string, object?> { ["id"] = id }));
            }

            if (hasTextTr)
            {
                description.TextTr = newTextTr;
            }

            if (hasTextEn)
            {
                description.TextEn = newTextEn;
            }

            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(await ProjectByIdAsync(db, id, cancellationToken));
        }
    }

    // ---------------------------------------------------------------------------------
    // POST /admin/descriptions/{id}/publish
    // ---------------------------------------------------------------------------------

    private static async Task<IResult> PublishAsync(
        long id,
        DegerliDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var description = await db.BusinessDescriptions
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (description is null)
        {
            return Results.Problem(ApiProblem.Create(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.NotFound,
                new Dictionary<string, object?> { ["id"] = id }));
        }

        // The gate: both languages must be present and non-empty (FR-RES-020/BR-RES-003).
        // The DB CHECK is the backstop, but empty strings are NOT NULL to the database, so
        // the API is the authority on "empty means missing" (I-RES-1).
        var missing = PublishGateFields
            .Where(field => string.IsNullOrWhiteSpace(TextOf(description, field)))
            .Select(field => (Field: field, Code: "required"))
            .ToList();

        if (missing.Count > 0)
        {
            return Validation(missing);
        }

        var now = clock.GetUtcNow();
        description.Status = "published";
        description.PublishedAt = now;
        description.LastReviewedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(await ProjectByIdAsync(db, id, cancellationToken));
    }

    // ---------------------------------------------------------------------------------
    // Projection
    // ---------------------------------------------------------------------------------

    private static async Task<object> ProjectByIdAsync(
        DegerliDbContext db,
        long id,
        CancellationToken cancellationToken)
    {
        var row = await (
                from description in db.BusinessDescriptions
                join instrument in db.Instruments on description.InstrumentId equals instrument.Id
                where description.Id == id
                select new DescriptionRow(
                    description.Id,
                    instrument.Symbol,
                    instrument.Name,
                    description.Version,
                    description.Status,
                    description.TextTr,
                    description.TextEn,
                    description.SourceRefsJson,
                    description.LastReviewedAt,
                    description.PublishedAt))
            .SingleAsync(cancellationToken);

        var ids = ParseSourceRefIds(row.SourceRefsJson).Distinct().ToList();
        var disclosures = await db.KapDisclosures
            .Where(disclosure => ids.Contains(disclosure.Id))
            .ToDictionaryAsync(disclosure => disclosure.Id, cancellationToken);

        return Project(row, disclosures);
    }

    /// <summary>
    /// Projects one description for the queue/editor payload. Source refs are resolved
    /// against <c>kap_disclosures</c>; an unresolvable id becomes a <c>{disclosureId}</c>-only
    /// stub so a broken reference is visible, never dropped (`03` §8 v1.2, `01` §10.7).
    /// </summary>
    private static object Project(DescriptionRow row, IReadOnlyDictionary<long, KapDisclosure> disclosures)
    {
        var sourceRefs = new List<object>();
        foreach (var disclosureId in ParseSourceRefIds(row.SourceRefsJson).Distinct())
        {
            sourceRefs.Add(disclosures.TryGetValue(disclosureId, out var disclosure)
                ? new
                {
                    disclosureId,
                    disclosureType = disclosure.DisclosureType,
                    publishDate = disclosure.PublishDate,
                    title = disclosure.Title,
                    sourceUrl = disclosure.SourceUrl,
                }
                : new { disclosureId });
        }

        return new
        {
            id = row.Id,
            symbol = row.Symbol,
            name = row.Name,
            version = row.Version,
            status = row.Status,
            textTr = row.TextTr,
            textEn = row.TextEn,
            sourceRefs,
            lastReviewedAt = row.LastReviewedAt,
            publishedAt = row.PublishedAt,
        };
    }

    /// <summary>Parses the stored <c>source_refs_json</c> (a JSON array of ids) tolerantly;
    /// malformed/absent storage yields no refs rather than failing the read.</summary>
    private static IEnumerable<long> ParseSourceRefIds(string? sourceRefsJson)
    {
        if (string.IsNullOrWhiteSpace(sourceRefsJson))
        {
            yield break;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(sourceRefsJson);
        }
        catch (JsonException)
        {
            yield break;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                yield break;
            }

            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var id))
                {
                    yield return id;
                }
            }
        }
    }

    private static string? ReadText(
        JsonElement element,
        bool present,
        string field,
        List<(string Field, string Code)> fields)
    {
        if (!present)
        {
            return null;
        }

        // An explicit JSON null is not a string: reject it as invalid rather than writing
        // NULL, which on a published row violates ck_business_descriptions_published_texts
        // and surfaced as an unhandled 500 from a well-formed `{textTr?}` request (I-RES-2).
        if (element.ValueKind != JsonValueKind.String)
        {
            fields.Add((field, "invalid"));
            return null;
        }

        return element.GetString();
    }

    private static string? TextOf(BusinessDescription description, string field) => field switch
    {
        "textTr" => description.TextTr,
        "textEn" => description.TextEn,
        _ => null,
    };

    private static IResult Validation(IReadOnlyList<(string Field, string Code)> fields)
    {
        var payload = fields
            .Select(field => new { field = field.Field, code = field.Code })
            .ToArray();

        return Results.Problem(ApiProblem.Create(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationFailed,
            new Dictionary<string, object?> { ["fields"] = payload }));
    }

    private sealed record DescriptionRow(
        long Id,
        string Symbol,
        string Name,
        int Version,
        string Status,
        string? TextTr,
        string? TextEn,
        string? SourceRefsJson,
        DateTimeOffset? LastReviewedAt,
        DateTimeOffset? PublishedAt);
}
