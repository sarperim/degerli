using System.Globalization;
using System.Text.Json;
using Degerli.Ingestion.Sources;

namespace Degerli.Ingestion.Prices;

/// <summary>
/// Decodes the <b>real</b> İş Yatırım EOD price envelope (01 v1.5 §10.9.1, FR-MDF-001,
/// FR-MDF-010): a per-ticker body of the shape
/// <c>{"ok":true,"errorCode":null,…, "value":[{"HGDG_*":…}]}</c>. Each <c>value</c> row is
/// one instrument-date. The field map is pinned by the architecture:
/// <list type="bullet">
///   <item><c>HGDG_HS_KODU</c> → symbol (as-is);</item>
///   <item><c>HGDG_TARIH</c> → date (<c>dd-MM-yyyy</c>, the source locale — <b>not</b> ISO);</item>
///   <item><c>HGDG_KAPANIS</c> (fallback <c>HG_KAPANIS</c>) → close; <c>HGDG_MAX</c>/<c>HGDG_MIN</c>
///         (fallbacks <c>HG_MAX</c>/<c>HG_MIN</c>) → high/low;</item>
///   <item><c>HGDG_HACIM</c> (fallback <c>HG_HACIM</c>) → volume, a scientific-notation-capable
///         JSON number stored as <c>bigint</c> rounded half-away-from-zero. The stored semantic
///         is <b>TRY turnover (₺)</b> (FLG-09);</item>
///   <item><c>PD</c> → <b>market capitalization</b> (TL), additive and nullable (TKT-foundation-086,
///         USER DECISION D5). It arrives in the same row as the close and is the numerator of the
///         price-lane share-count derivation (<c>count = PD ÷ close</c>); an envelope without it
///         (the <c>HG_*</c> fallback shape, the canonical double, Yahoo) carries none — an honest
///         absence, never a zero, and never defaulted from <c>SERMAYE</c> (a nominal-capital
///         figure is not a market cap);</item>
///   <item><c>open</c> → <b>NULL</b> — an honest gap: the source provides only AOF-weighted
///         fields and open is never invented from <c>HGDG_AOF</c>.</item>
/// </list>
/// Transport fields are not data: <c>ok:false</c> or a non-null <c>errorCode</c> is a
/// <see cref="SourceFetchException"/> (the TC-MDF-070 class), never a parse event.
/// </summary>
public static class IsYatirimPriceDecoder
{
    /// <summary>The source-locale date format the real envelope uses (dd-MM-yyyy).</summary>
    public const string SourceDateFormat = "dd-MM-yyyy";

    /// <summary>
    /// True when the document is the real İş Yatırım envelope: an object carrying the
    /// transport field <c>ok</c> and/or the <c>value</c> row array (the canonical double
    /// uses <c>sourceRef</c>/<c>items</c>; Yahoo uses <c>chart</c>).
    /// </summary>
    public static bool IsEnvelope(JsonDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.RootElement;
        return root.ValueKind == JsonValueKind.Object
            && (root.TryGetProperty("ok", out _) || root.TryGetProperty("value", out _));
    }

    /// <summary>
    /// Enforces the transport contract: a body with <c>ok:false</c> or a non-null
    /// <c>errorCode</c> is a source failure. Throws <see cref="SourceFetchException"/> so
    /// the job's retry ladder treats it as a source-down, not an <c>UNPARSEABLE_PAYLOAD</c>.
    /// </summary>
    public static void EnsureSourceOk(JsonDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False)
        {
            throw new SourceFetchException("İş Yatırım price envelope reported ok:false (source failure).");
        }

        if (root.TryGetProperty("errorCode", out var errorCode)
            && errorCode.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            throw new SourceFetchException(
                $"İş Yatırım price envelope reported errorCode '{errorCode}' (source failure).");
        }
    }

    /// <summary>Decodes the <c>value</c> rows into the canonical dated facts (open NULL).</summary>
    public static IReadOnlyList<DatedPriceFact> DecodeRows(JsonDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.RootElement;
        if (!root.TryGetProperty("value", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var rows = new List<DatedPriceFact>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var symbol = ReadString(item, "HGDG_HS_KODU");
            var dateText = ReadString(item, "HGDG_TARIH");
            if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(dateText))
            {
                continue;
            }

            var date = DateOnly.ParseExact(dateText, SourceDateFormat, CultureInfo.InvariantCulture);

            rows.Add(new DatedPriceFact(
                symbol,
                date,
                Open: null,
                High: ReadDecimal(item, "HGDG_MAX", "HG_MAX"),
                Low: ReadDecimal(item, "HGDG_MIN", "HG_MIN"),
                Close: ReadDecimal(item, "HGDG_KAPANIS", "HG_KAPANIS"),
                Volume: ReadVolume(item, "HGDG_HACIM", "HG_HACIM"),
                // The market cap (TL) — read verbatim, never derived, never defaulted.
                MarketCap: ReadDecimal(item, "PD")));
        }

        return rows;
    }

    private static string? ReadString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Reads the first present JSON number among <paramref name="names"/> (primary first).</summary>
    private static decimal? ReadDecimal(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (item.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number)
            {
                return property.GetDecimal();
            }
        }

        return null;
    }

    /// <summary>
    /// Reads the volume (scientific-notation-capable) and rounds half-away-from-zero to the
    /// stored <c>bigint</c> ₺ turnover.
    /// </summary>
    private static long? ReadVolume(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (item.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number)
            {
                return (long)decimal.Round(property.GetDecimal(), MidpointRounding.AwayFromZero);
            }
        }

        return null;
    }
}
