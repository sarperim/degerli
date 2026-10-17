using System.Text.Json;
using Degerli.Api.Infrastructure;
using Degerli.Core.Valuation;
using Degerli.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Api.Valuation;

/// <summary>
/// The DCF baseline read endpoint (<c>03-api-design.md</c> §5; UC-VAL-001 step 1–2,
/// TKT-val-003): an anonymous <c>GET /api/v1/stocks/{symbol}/dcf</c> that serves the
/// stock's active baseline — its version/build date, the eight-parameter defaults, the
/// canonical-fact provenance refs (as-of + restated flags per fact, UXR-VAL-010) — and
/// the canonical EOD price (NFR-VAL-003, the same <c>daily_prices</c> row every surface
/// derives from). When a statement-derived input is missing the payload is <c>200</c>
/// with <c>state: "missing_inputs"</c> naming what is missing instead of computing on
/// partial data (UXR-VAL-009); an unknown symbol is <c>404 NOT_FOUND</c>.
/// </summary>
public static class DcfBaselineEndpoints
{
    public static IEndpointRouteBuilder MapDegerliDcfBaselineEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/stocks/{symbol}/dcf", GetAsync).AllowAnonymous();
        return api;
    }

    private static async Task<IResult> GetAsync(
        string symbol,
        DegerliDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var normalized = symbol.ToUpperInvariant();

        var instrumentId = await db.Instruments
            .Where(i => i.Symbol == normalized)
            .Select(i => (long?)i.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (instrumentId is null)
        {
            return NotFound(normalized);
        }

        // The active baseline is the served one (one active per stock, 02 §3.5).
        var baseline = await db.DcfBaselines
            .Where(b => b.InstrumentId == instrumentId && b.IsActive)
            .OrderByDescending(b => b.Version)
            .FirstOrDefaultAsync(cancellationToken);
        if (baseline is null)
        {
            // A covered calculator always has an active baseline (BR-VAL-001).
            return NotFound(normalized);
        }

        var price = await db.DailyPrices
            .Where(p => p.InstrumentId == instrumentId)
            .OrderByDescending(p => p.PriceDate)
            .Select(p => new { p.CloseRaw, p.PriceDate })
            .FirstOrDefaultAsync(cancellationToken);
        if (price is null)
        {
            return NotFound(normalized);
        }

        using var document = JsonDocument.Parse(baseline.ParamsJson);
        var root = document.RootElement;

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var stale = price.PriceDate < LatestTradingDayOnOrBefore(today);

        // Financial-sector exclusion (02 §6.4, 03 §5 v1.4): the generation job records a
        // `sector_not_applicable` state instead of an FCF-derived baseline; serve that
        // not-applicable verdict — distinct from the missing-inputs `200` state.
        if (root.TryGetProperty("state", out var stateElement)
            && stateElement.ValueKind == JsonValueKind.String
            && stateElement.GetString() == DcfSectorApplicability.SectorNotApplicableState)
        {
            return Results.Ok(new Dictionary<string, object?>
            {
                ["symbol"] = normalized,
                ["state"] = DcfSectorApplicability.SectorNotApplicableState,
                ["reason"] = DcfSectorApplicability.FinancialSectorReason,
                ["price"] = new
                {
                    value = price.CloseRaw,
                    asOf = price.PriceDate,
                    stale,
                },
            });
        }

        var parameters = root.TryGetProperty("params", out var paramsElement)
            ? paramsElement.Clone()
            : EmptyObject();
        var refs = root.TryGetProperty("canonicalFactRefs", out var refsElement)
            ? refsElement.Clone()
            : EmptyObject();
        var missing = root.TryGetProperty("missingInputs", out var missingElement)
            && missingElement.ValueKind == JsonValueKind.Array
                ? missingElement.EnumerateArray().Select(e => e.GetString()!).ToArray()
                : [];

        var response = new Dictionary<string, object?>
        {
            ["symbol"] = normalized,
            ["baseline"] = new Dictionary<string, object?>
            {
                ["version"] = baseline.Version,
                ["buildDate"] = baseline.BuildDate,
                ["params"] = parameters,
                ["canonicalFactRefs"] = refs,
            },
            ["price"] = new
            {
                value = price.CloseRaw,
                asOf = price.PriceDate,
                stale,
            },
        };

        if (missing.Length > 0)
        {
            response["state"] = "missing_inputs";
            response["missingInputs"] = missing;
        }

        return Results.Ok(response);
    }

    private static JsonElement EmptyObject()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    private static IResult NotFound(string symbol)
        => Results.Problem(ApiProblem.Create(
            StatusCodes.Status404NotFound,
            ApiErrorCodes.NotFound,
            new Dictionary<string, object?> { ["symbol"] = symbol }));

    /// <summary>The latest Mon–Fri trading day at or before <paramref name="date"/>.</summary>
    private static DateOnly LatestTradingDayOnOrBefore(DateOnly date)
    {
        while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            date = date.AddDays(-1);
        }

        return date;
    }
}
