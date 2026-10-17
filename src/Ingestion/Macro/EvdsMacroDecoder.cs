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
    /// <summary>The source-locale date format the real EVDS envelope uses (dd-MM-yyyy).</summary>
    public const string SourceDateFormat = "dd-MM-yyyy";

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
    public static IReadOnlyList<MacroItem> DecodeRows(JsonDocument document, DateOnly from, DateOnly to)
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

            foreach (var evdsCode in EvdsSeries.RequestOrder)
            {
                if (!EvdsSeries.CanonicalByEvdsCode.TryGetValue(evdsCode, out var canonical))
                {
                    continue;
                }

                var value = ReadDecimal(item, evdsCode);
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

    private static string? ReadString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// Reads one EVDS value cell. The real export serializes values as JSON numbers, but a
    /// few series arrive as locale-free numeric strings; both are accepted, anything else
    /// (null, a non-numeric marker) is treated as a missing observation.
    /// </summary>
    private static decimal? ReadDecimal(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDecimal(),
            JsonValueKind.String => TryParseDecimal(value.GetString()),
            _ => null,
        };
    }

    private static decimal? TryParseDecimal(string? text) =>
        decimal.TryParse(
            text,
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;

    private static bool TryParseDate(string text, out DateOnly date) =>
        DateOnly.TryParseExact(text, SourceDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
        || DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
