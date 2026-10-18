using System.Text.Json;

namespace Degerli.Ingestion.Universe;

/// <summary>
/// Decodes the <b>real</b> İş Yatırım instrument-universe and index-levels envelopes
/// (01 v1.5 §10.9.8, FR-MDF-006/007). Discovery was live (2026-10-18, read-only); both
/// resources sit on the same <c>www.isyatirim.com.tr</c> host as the prices endpoint and
/// answer anonymously (no browser headers, no form digest).
/// <para>
/// <b>Universe.</b> <c>POST …/StockInfo/CompanyInfoAjax.aspx/GetHissePerformans</c> with the
/// JSON body <c>{"endeksKodu":"09","sektorKodu":"","exchange":""}</c> (Ulusal-Tüm = every
/// listed national instrument) returns a legacy ASP.NET AJAX envelope whose payload is a
/// <b>JSON string</b>: <c>{"d":"{\"Sector\":null,\"Oran\":[ {…row…}, … ]}"}</c>. Each
/// <c>Oran</c> row is one instrument:
/// <list type="bullet">
///   <item><c>HISSE_KODU</c> → symbol (BIST ticker, stored as-is);</item>
///   <item><c>HISSE_TANIM</c> → name (the source pads it with trailing spaces — trimmed);</item>
///   <item><c>AS_ALT_SEKTOR_KODU</c> → sector code, falling back to <c>AS_ALT_SEKTOR_TANIMI</c>
///         (the sub-sector's Turkish label) when the source publishes no code — it publishes
///         only labels today;</item>
///   <item><c>AS_SEKTOR_KODU</c> → parent sector code (2-level hierarchy; usually absent);</item>
///   <item>— (no source field) → <c>listingDate</c>: <b>honest gap, NULL</b>.</item>
/// </list>
/// The distinct sector labels seen across the rows are reshaped into canonical
/// <see cref="UniverseSector"/> reference rows (code = the instrument's resolved sector code,
/// so the store links every instrument to its classification) — a faithful denormalization of
/// the classification the source already states per row, never an invented value.
/// </para>
/// <para>
/// <b>Index levels.</b> <c>GET …/Common/ChartData.aspx/IndexHistoricalAll?period=1440&amp;from=&lt;yyyyMMddHHmmss&gt;&amp;to=&lt;yyyyMMddHHmmss&gt;&amp;endeks=&lt;code&gt;</c>
/// returns <c>{"data":[[&lt;epochMillis&gt;,&lt;close&gt;], …],"timestamp":"…"}</c>. Each pair is
/// one trading day's index close. The chart timestamps are <b>midnight Europe/Istanbul</b>,
/// so the calendar date is the epoch converted to UTC+3 (Türkiye has observed permanent UTC+3
/// since 2016 — not DST-dependent). The requested index code is stamped onto every fact (the
/// body carries no code).
/// </para>
/// <para>
/// The canonical <c>sourceRef</c>/<c>instruments</c>/<c>levels</c> source-double envelopes
/// (the L2 golden contract, §10.9.7) stay first-class: detection is by shape, never by config.
/// </para>
/// </summary>
public static class UniverseRealEnvelopeDecoder
{
    /// <summary>The source-locale timezone offset of the chart timestamps (permanent UTC+3).</summary>
    public static readonly TimeSpan SourceTimeZoneOffset = TimeSpan.FromHours(3);

    /// <summary>The payload property of the legacy ASP.NET AJAX wrapper (<c>{"d":"…"}</c>).</summary>
    public const string AjaxWrapperProperty = "d";

    /// <summary>The instrument-row array property of the real universe envelope.</summary>
    public const string RowsProperty = "Oran";

    /// <summary>The dated-close array property of the real index-levels envelope.</summary>
    public const string LevelsDataProperty = "data";

    /// <summary>
    /// True when the document is the real İş Yatırım instrument-universe envelope: an object
    /// carrying the ASP.NET AJAX <c>d</c> wrapper and/or the <c>Oran</c> row array. The
    /// canonical double uses <c>sourceRef</c>/<c>instruments</c>.
    /// </summary>
    public static bool IsEnvelope(JsonDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.RootElement;
        return root.ValueKind == JsonValueKind.Object
            && (root.TryGetProperty(AjaxWrapperProperty, out _)
                || (root.TryGetProperty(RowsProperty, out var rows) && rows.ValueKind == JsonValueKind.Array));
    }

    /// <summary>
    /// Decodes the real instrument-universe envelope into the canonical
    /// <see cref="UniversePayload"/> (instruments + the sector reference rows their
    /// classification implies). No effective-dated change set is ever emitted: the resource
    /// is a full snapshot.
    /// </summary>
    public static UniversePayload Decode(JsonDocument document, string sourceRef)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRef);

        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(AjaxWrapperProperty, out var wrapper)
            && wrapper.ValueKind == JsonValueKind.String)
        {
            using var inner = JsonDocument.Parse(wrapper.GetString()!);
            return DecodeFromObject(inner.RootElement, sourceRef);
        }

        return DecodeFromObject(root, sourceRef);
    }

    private static UniversePayload DecodeFromObject(JsonElement root, string sourceRef)
    {
        var instruments = new List<UniverseInstrument>();
        var sectors = new Dictionary<string, UniverseSector>(StringComparer.Ordinal);

        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(RowsProperty, out var rows)
            && rows.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var symbol = ReadString(row, "HISSE_KODU");
                if (string.IsNullOrWhiteSpace(symbol))
                {
                    continue;
                }

                var name = ReadString(row, "HISSE_TANIM");
                var sectorLabel = ReadString(row, "AS_ALT_SEKTOR_TANIMI");
                var sectorLabelEn = ReadString(row, "AS_ALT_SEKTOR_TANIMI_YD");
                // The source publishes the sub-sector label but (today) no code; key the
                // classification on the code when present, else on the label itself so the
                // instrument's classification is preserved rather than dropped.
                var sectorCode = ReadString(row, "AS_ALT_SEKTOR_KODU") ?? sectorLabel;
                var parentCode = ReadString(row, "AS_SEKTOR_KODU");

                instruments.Add(new UniverseInstrument(symbol, name, sectorCode, ListingDate: null));

                if (sectorCode is not null && !sectors.ContainsKey(sectorCode))
                {
                    sectors[sectorCode] = new UniverseSector(sectorCode, sectorLabel, sectorLabelEn, parentCode);
                }
            }
        }

        return new UniversePayload(
            sourceRef,
            EffectiveDate: null,
            sectors.Values.ToList(),
            Indices: null,
            instruments,
            Add: null,
            Remove: null);
    }

    /// <summary>
    /// True when the document is the real index-levels envelope: an object carrying the
    /// <c>data</c> dated-close array and <b>no</b> canonical <c>levels</c> array.
    /// </summary>
    public static bool IsIndexLevelsEnvelope(JsonDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.RootElement;
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(LevelsDataProperty, out var data)
            && data.ValueKind == JsonValueKind.Array
            && !root.TryGetProperty("levels", out _);
    }

    /// <summary>
    /// Decodes the real index-levels envelope into the canonical
    /// <see cref="IndexLevelsPayload"/>, stamping <paramref name="indexCode"/> on every fact
    /// (the body carries no code). Each <c>[epochMillis, close]</c> pair becomes one dated
    /// close; the epoch is read as Istanbul local time so the calendar date is correct.
    /// </summary>
    public static IndexLevelsPayload DecodeIndexLevels(JsonDocument document, string sourceRef, string indexCode)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(indexCode);

        var root = document.RootElement;
        var levels = new List<IndexLevelFact>();
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(LevelsDataProperty, out var data)
            && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var pair in data.EnumerateArray())
            {
                if (pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength() < 2)
                {
                    continue;
                }

                var stamp = pair[0];
                var close = pair[1];
                if (stamp.ValueKind != JsonValueKind.Number
                    || close.ValueKind != JsonValueKind.Number
                    || !stamp.TryGetInt64(out var epochMillis))
                {
                    continue;
                }

                var date = DateOnly.FromDateTime(
                    DateTimeOffset.FromUnixTimeMilliseconds(epochMillis)
                        .ToOffset(SourceTimeZoneOffset)
                        .DateTime);

                levels.Add(new IndexLevelFact(indexCode, date, close.GetDecimal()));
            }
        }

        return new IndexLevelsPayload(sourceRef, levels);
    }

    /// <summary>Reads a string property, trimming the source's padding; empty → null.</summary>
    private static string? ReadString(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
