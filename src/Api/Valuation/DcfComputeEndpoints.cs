using System.Globalization;
using System.Text.Json;
using Degerli.Api.Infrastructure;
using Degerli.Core.Valuation;
using Degerli.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Api.Valuation;

/// <summary>
/// The DCF compute endpoint (<c>03-api-design.md</c> §5; UC-VAL-001 steps 3–4,
/// TKT-val-004): a pure, anonymous, stateless <c>POST /api/v1/stocks/{symbol}/dcf/compute</c>
/// returning the single point result **and** the sensitivity grid in one response.
/// Nothing is persisted, no auth is required, and the canonical price is read from
/// the same <c>daily_prices</c> row every other surface uses (NFR-VAL-003, AD-08).
/// </summary>
public static class DcfComputeEndpoints
{
    /// <summary>The confirmed, fully user-editable parameter set (OQ-UX-001), wire order.</summary>
    private static readonly string[] ParameterFields =
    {
        "base_fcf",
        "growth_rate",
        "horizon_years",
        "terminal_growth",
        "discount_rate",
        "debt",
        "cash",
        "share_count",
    };

    public static IEndpointRouteBuilder MapDegerliDcfEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapPost("/stocks/{symbol}/dcf/compute", ComputeAsync).AllowAnonymous();
        return api;
    }

    private static async Task<IResult> ComputeAsync(
        string symbol,
        HttpRequest request,
        DegerliDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return Validation(new[] { new ValidationField("body", "invalid_json") });
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Validation(new[] { new ValidationField("body", "invalid_json") });
            }

            var fields = new List<ValidationField>();
            var values = new Dictionary<string, decimal>(StringComparer.Ordinal);
            foreach (var name in ParameterFields)
            {
                ReadNumber(root, name, fields, values);
            }

            // horizon_years is an integer field; a fractional value is a type error, and
            // a value outside the int range is a magnitude error. The 1..10 domain is a
            // math constraint (422), not validation (I-VAL-2), so it is not checked here.
            if (values.TryGetValue("horizon_years", out var horizon))
            {
                if (horizon != decimal.Truncate(horizon))
                {
                    fields.Add(new ValidationField("horizon_years", "not_a_number"));
                }
                else if (horizon < int.MinValue || horizon > int.MaxValue)
                {
                    fields.Add(new ValidationField("horizon_years", "out_of_range"));
                }
            }

            // A non-positive share count is an unusable divisor, never a computable model.
            if (values.TryGetValue("share_count", out var shareCount) && shareCount <= 0m)
            {
                fields.Add(new ValidationField("share_count", "out_of_range"));
            }

            if (fields.Count > 0)
            {
                return Validation(fields);
            }

            var parameters = new DcfParameters(
                BaseFcf: values["base_fcf"],
                GrowthRate: values["growth_rate"],
                HorizonYears: (int)values["horizon_years"],
                TerminalGrowth: values["terminal_growth"],
                DiscountRate: values["discount_rate"],
                Debt: values["debt"],
                Cash: values["cash"],
                ShareCount: values["share_count"]);

            var normalized = symbol.ToUpperInvariant();
            var instrumentId = await db.Instruments
                .Where(i => i.Symbol == normalized)
                .Select(i => (long?)i.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (instrumentId is null)
            {
                return NotFound(normalized);
            }

            var price = await db.DailyPrices
                .Where(p => p.InstrumentId == instrumentId)
                .OrderByDescending(p => p.PriceDate)
                .Select(p => new { p.CloseRaw, p.PriceDate })
                .FirstOrDefaultAsync(cancellationToken);
            if (price is null)
            {
                // A covered calculator always has a canonical price (the same row the
                // stock page derives from); without it the model is not usable.
                return NotFound(normalized);
            }

            var computation = DcfModel.Compute(parameters, price.CloseRaw);
            if (!computation.IsComputable)
            {
                return NotComputable(computation.Constraint);
            }

            var grid = DcfModel.ComputeSensitivity(parameters);
            var fairValues = grid.FairValues
                .Select(row => row.ToArray())
                .ToArray();

            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            var stale = price.PriceDate < LatestTradingDayOnOrBefore(today);

            return Results.Ok(new
            {
                fairValuePerShare = computation.Result!.FairValuePerShare,
                marginOfSafety = computation.Result.MarginOfSafety,
                price = new
                {
                    value = price.CloseRaw,
                    asOf = price.PriceDate,
                    stale,
                },
                sensitivity = new
                {
                    discountRates = grid.DiscountRates,
                    terminalGrowths = grid.TerminalGrowths,
                    fairValues,
                },
            });
        }
    }

    /// <summary>
    /// Reads one field with strict typing (`01` §10.5): a missing/null field is
    /// <c>required</c>, a wrong JSON kind is <c>not_a_number</c>, and a number that
    /// cannot be represented as a finite decimal is <c>out_of_range</c>. There is no
    /// "correctness" validation of the user's assumptions themselves (RISK-VAL-001).
    /// </summary>
    private static void ReadNumber(
        JsonElement root,
        string name,
        List<ValidationField> fields,
        Dictionary<string, decimal> values)
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            fields.Add(new ValidationField(name, "required"));
            return;
        }

        if (element.ValueKind != JsonValueKind.Number)
        {
            fields.Add(new ValidationField(name, "not_a_number"));
            return;
        }

        if (!decimal.TryParse(element.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            fields.Add(new ValidationField(name, "out_of_range"));
            return;
        }

        values[name] = value;
    }

    private static IResult Validation(IReadOnlyList<ValidationField> fields)
    {
        var payload = fields
            .Select(field => new { field = field.Field, code = field.Code })
            .ToArray();

        return Results.Problem(ApiProblem.Create(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationFailed,
            new Dictionary<string, object?> { ["fields"] = payload }));
    }

    private static IResult NotFound(string symbol)
        => Results.Problem(ApiProblem.Create(
            StatusCodes.Status404NotFound,
            ApiErrorCodes.NotFound,
            new Dictionary<string, object?> { ["symbol"] = symbol }));

    /// <summary>
    /// Maps a violated math constraint to `422 DCF_NOT_COMPUTABLE` naming both the
    /// constraint and the field the SPA must mark (UXR-VAL-008; `03` §5/§7).
    /// </summary>
    private static IResult NotComputable(DcfConstraint? constraint)
    {
        var (code, field) = constraint switch
        {
            DcfConstraint.DiscountRateNotGreaterThanTerminalGrowth =>
                ("DISCOUNT_RATE_NOT_GREATER_THAN_TERMINAL_GROWTH", "discount_rate"),
            DcfConstraint.HorizonOutOfRange =>
                ("HORIZON_OUT_OF_RANGE", "horizon_years"),
            _ => ("NOT_COMPUTABLE", "params"),
        };

        return Results.Problem(ApiProblem.Create(
            StatusCodes.Status422UnprocessableEntity,
            ApiErrorCodes.DcfNotComputable,
            new Dictionary<string, object?>
            {
                ["constraint"] = code,
                ["field"] = field,
            }));
    }

    /// <summary>The latest Mon–Fri trading day at or before <paramref name="date"/>.</summary>
    private static DateOnly LatestTradingDayOnOrBefore(DateOnly date)
    {
        while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            date = date.AddDays(-1);
        }

        return date;
    }

    private readonly record struct ValidationField(string Field, string Code);
}
