using System.Text.Json.Serialization;

namespace Degerli.Ingestion.Funds;

/// <summary>
/// The TEFAS fund-universe payload (FR-FDF-001..003). One fetch carries every fund the
/// source publishes for the day — the fund identity plus its NAV history, performance
/// rows and (where available) a holdings snapshot. The universe bound (BR-FDF-006) is
/// applied to this list: only equity and equity-heavy mixed funds are ingested, the
/// out-of-scope types are skipped and counted (I-FDF-1), never quarantined.
/// </summary>
public sealed record TefasUniversePayload(
    [property: JsonPropertyName("sourceRef")] string? SourceRef,
    [property: JsonPropertyName("funds")] IReadOnlyList<TefasFund>? Funds);

/// <summary>
/// One fund's block inside the TEFAS universe payload. The shape mirrors the FU §10
/// <c>tefas-*</c> canned payload: a fund identity, its NAV rows, performance rows and
/// holdings lines.
/// </summary>
public sealed record TefasFund(
    [property: JsonPropertyName("sourceRef")] string? SourceRef,
    [property: JsonPropertyName("fund")] TefasFundInfo? Fund,
    [property: JsonPropertyName("navs")] IReadOnlyList<TefasNavItem>? Navs,
    [property: JsonPropertyName("performance")] IReadOnlyList<TefasPerformanceItem>? Performance,
    [property: JsonPropertyName("holdings")] IReadOnlyList<TefasHoldingItem>? Holdings);

/// <summary>Fund identity as published by TEFAS.</summary>
public sealed record TefasFundInfo(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("type")] string? Type);

/// <summary>One dated NAV publication.</summary>
public sealed record TefasNavItem(
    [property: JsonPropertyName("date")] DateOnly Date,
    [property: JsonPropertyName("value")] decimal Value);

/// <summary>One performance row as published (period + as-of date + decimal return).</summary>
public sealed record TefasPerformanceItem(
    [property: JsonPropertyName("period")] string Period,
    [property: JsonPropertyName("asOfDate")] DateOnly AsOfDate,
    [property: JsonPropertyName("returnValue")] decimal? ReturnValue);

/// <summary>
/// One holdings line. <see cref="Symbol"/> is present when the source maps the line to a
/// BIST symbol; otherwise <see cref="NameRaw"/> is retained for the look-through link and
/// <c>instrument_id</c> stays NULL (never dropped — FR-FDF-003).
/// </summary>
public sealed record TefasHoldingItem(
    [property: JsonPropertyName("lineNo")] int LineNo,
    [property: JsonPropertyName("symbol")] string? Symbol,
    [property: JsonPropertyName("nameRaw")] string? NameRaw,
    [property: JsonPropertyName("weight")] decimal? Weight,
    [property: JsonPropertyName("units")] decimal? Units);

/// <summary>
/// Which TEFAS fund types are in the V1 fund-universe bound (BR-FDF-006): equity funds
/// and equity-heavy mixed funds. Everything else is skipped-and-counted (I-FDF-1).
/// </summary>
public static class TefasFundTypes
{
    public const string Equity = "equity";
    public const string EquityHeavyMixed = "equity-heavy mixed";

    /// <summary>True when the published type is inside the V1 fund-universe bound.</summary>
    public static bool IsInScope(string? fundType)
    {
        if (string.IsNullOrWhiteSpace(fundType))
        {
            return false;
        }

        var normalized = fundType.Trim();
        return normalized.Equals(Equity, StringComparison.OrdinalIgnoreCase)
            || normalized.Equals(EquityHeavyMixed, StringComparison.OrdinalIgnoreCase);
    }
}
