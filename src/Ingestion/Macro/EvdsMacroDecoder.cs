using System.Globalization;
using System.Text.Json;

namespace Degerli.Ingestion.Macro;

/// <summary>
/// Decodes the <b>real</b> TCMB EVDS macro envelope (01 v1.5 §10.9.6, FR-MOV-014): a
/// <em>column-oriented</em> body of the shape
/// <c>{"totalCount":N,"items":[{"Tarih":"dd-MM-yyyy","&lt;seriesCode&gt;":value,…}]}</c>.
/// Each <c>items</c> row is one observation date; every requested EVDS series identifier is
/// a column on that row (a series with no observation that date is absent or <c>null</c>).
/// The adapter maps each EVDS column back to its canonical macro code through
/// <see cref="EvdsSeries.CanonicalByEvdsCode"/>, so the jobs, the store and the strip are
/// unchanged by the source consolidation.
///
/// <para>
/// This is the real-shape half of the macro ETL. The canonical EVDS double envelope — the
/// <c>sourceRef</c>/<c>series</c> row list that the L2 golden contract serves (FU §10,
/// §10.9.7) — remains the shape TC-MOV-026 asserts and is parsed by
/// <see cref="MacroSourceAdapter"/> directly; this decoder is only reached for the real
/// column shape.
/// </para>
///
/// <para>
/// The real EVDS body carries no per-row <c>unit</c> and no <c>recorded_at</c> (EVDS
/// publishes the observation, not a re-publication timestamp). Both are left <c>null</c>
/// — honest gaps: the unit is a property of the series definition (<c>02</c> §3.2, joined
/// for storage) and the observation timestamp is stamped by the job at ingestion time.
/// </para>
/// </summary>
public static class EvdsMacroDecoder
{
    /// <summary>The source-locale date format the real EVDS daily envelope uses (dd-MM-yyyy).</summary>
    public const string SourceDateFormat = "dd-MM-yyyy";

    /// <summary>The month format the real EVDS monthly envelope uses (yyyy-MM); resolves to
    /// the month's first day.</summary>
    public const string MonthFormat = "yyyy-MM";

    /// <summary>The date column of the real EVDS envelope.</summary>
    public const string DateColumn = "Tarih";

    /// <summary>
    /// True when the document is the real EVDS column envelope: an object carrying an
    /// <c>items</c> array and <b>no</b> <c>series</c> array (the canonical double uses
    /// <c>sourceRef</c>/<c>series</c>).
    /// </summary>
    public static bool IsEnvelope(JsonDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.RootElement;
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("items", out var items)
            && items.ValueKind == JsonValueKind.Array
            && !root.TryGetProperty("series", out _);
    }

    /// <summary>
    /// Decodes every observation in the real EVDS envelope onto a canonical
    /// <see cref="MacroItem"/>. Only the five official series resolve (an unknown EVDS
    /// column is ignored — it is not a V1 series the adapter may store), and only rows whose
    /// observation date falls inside the bounded <paramref name="from"/>..<paramref name="to"/>
    /// value-date window are kept (the 90-day lookback bound; a source that ignores the
    /// request window cannot smuggle out-of-bound values past the adapter).
    /// </summary>
    public static IReadOnlyList<MacroItem> DecodeRows(JsonDocument document, DateOnly from, DateOnly to) =>
        DecodeRows(document, from, to, allowedCanonicalCodes: null);

    /// <summary>
    /// As <see cref="DecodeRows(JsonDocument, DateOnly, DateOnly)"/>, additionally narrowing to
    /// <paramref name="allowedCanonicalCodes"/> when supplied: the v3 adapter issues one request
    /// per series group and decodes only the series it asked for (so a merged response cannot
    /// leak a series from the other request). A <c>null</c> set means every mapped series.
    /// </summary>
    public static IReadOnlyList<MacroItem> DecodeRows(
        JsonDocument document,
        DateOnly from,
        DateOnly to,
        IReadOnlySet<string>? allowedCanonicalCodes)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.RootElement;
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var rows = new List<MacroItem>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var dateText = ReadString(item, DateColumn);
            if (string.IsNullOrWhiteSpace(dateText) || !TryParseDate(dateText, out var date))
            {
                continue;
            }

            if (date < from || date > to)
            {
                continue;
            }

            // Enumerate the row's columns and resolve each EVDS column key to its canonical
            // code. The platform serialises a column key by replacing '.' with '_'
            // (TP.DK.USD.S.YTL → TP_DK_USD_S_YTL); normalising '_' → '.' recovers the
            // identifier, so both the dotted and underscored spellings resolve (the union map).
            foreach (var column in item.EnumerateObject())
            {
                if (!TryResolveCanonical(column.Name, out var canonical))
                {
                    continue;
                }

                if (allowedCanonicalCodes is not null && !allowedCanonicalCodes.Contains(canonical))
                {
                    continue;
                }

                var value = ReadDecimal(column.Value);
                if (value is null)
                {
                    continue;
                }

                // Unit and recorded_at are absent on the real row (honest gaps): the unit
                // is joined from the series definition and the job stamps the observation.
                rows.Add(new MacroItem(canonical, date, value.Value, Unit: null, RecordedAt: null));
            }
        }

        return rows;
    }

    /// <summary>
    /// Resolves one envelope column key to its canonical macro code. EVDS 3.0 underscores the
    /// dotted identifier, so the key is normalised (<c>_</c> → <c>.</c>) before the union
    /// lookup; a key that names no known series is ignored.
    /// </summary>
    public static bool TryResolveCanonical(string columnName, out string canonicalCode)
    {
        canonicalCode = string.Empty;
        if (string.IsNullOrEmpty(columnName))
        {
            return false;
        }

        var normalized = columnName.Replace('_', '.');
        return EvdsSeries.CanonicalByEvdsCode.TryGetValue(normalized, out canonicalCode!)
            || EvdsSeries.CanonicalByEvdsCode.TryGetValue(columnName, out canonicalCode!);
    }

    private static string? ReadString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// Reads one EVDS value cell. The real export serializes values as locale-free strings
    /// that carry <b>thousands-grouping commas</b> regardless of the requested
    /// <c>decimalSeperator</c> (01 v1.5.6 §10.9.12 — e.g. <c>"6,622,000.00"</c>): the grouping
    /// separators are stripped before the invariant numeric parse. A JSON number is accepted
    /// as-is; anything else (null, a non-numeric marker) is treated as a missing observation.
    /// </summary>
    private static decimal? ReadDecimal(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDecimal(),
            JsonValueKind.String => TryParseDecimal(value.GetString()),
            _ => null,
        };

    private static decimal? TryParseDecimal(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // EVDS 3.0 returns values with thousands-grouping commas and a '.' decimal point
        // regardless of the requested decimalSeperator; strip the grouping before parsing.
        var normalized = text.Replace(",", string.Empty, StringComparison.Ordinal);
        return decimal.TryParse(
            normalized,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    /// <summary>
    /// Parses a real EVDS observation date: daily rows use <c>dd-MM-yyyy</c>; monthly rows
    /// (the CPI index, the policy rate) use <c>yyyy-MM</c> and resolve to the month's first
    /// day (the observation is the month — 01 v1.5.6 §10.9.12).
    /// </summary>
    public static bool TryParseDate(string text, out DateOnly date)
    {
        if (DateOnly.TryParseExact(text, SourceDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return true;
        }

        if (DateOnly.TryParseExact(text, MonthFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return true;
        }

        return DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}
