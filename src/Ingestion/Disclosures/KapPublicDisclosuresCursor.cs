using System.Globalization;
using System.Text.Json;
using Degerli.Ingestion.KapPublic;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Ingestion.Disclosures;

/// <summary>
/// The public-site <b>disclosures feed</b>'s own incremental cursor (01 v1.5.85 §10.9.19,
/// TKT-foundation-084; FR-MDF-005/010/011).
/// <para>
/// The public list is <b>date-windowed</b>, not index-paged (the investigation's byCriteria
/// contract, 079): there is no index to walk forward, so the cursor is a <b>date watermark</b> —
/// the last day a completed pass covered, kept <b>inclusive</b> so a filing published later that
/// same day is still listed by the next pass — plus the <b>set of public
/// <c>disclosureIndex</c> values already handled inside that window</b>, which is what stops the
/// re-listed same-day rows from being written twice.
/// </para>
/// <para>
/// <b>Its own space, never mixed.</b> Everything here is the public site's own
/// <c>disclosureIndex</c> space (the market probe's newest is <c>1687047</c>) — never a VYK
/// gateway index (head <c>1231017</c>), and never the VYK lane's <c>kap-disclosures-market-cursor</c>
/// row. The store writes it under its own <see cref="KapPublicDisclosuresCursorStore.DataType"/>,
/// at <c>market</c> scope, so the two feeds' cursors cannot be confused for one another even though
/// both are market-wide.
/// </para>
/// </summary>
/// <param name="Watermark">The last day a completed pass covered (inclusive).</param>
/// <param name="Seen">Public disclosure indices already handled inside the window at <paramref name="Watermark"/>.</param>
public sealed record KapPublicDisclosuresCursor(DateOnly Watermark, IReadOnlyList<long> Seen)
{
    /// <summary>An empty cursor starting at <paramref name="from"/> (a bootstrap pass).</summary>
    public static KapPublicDisclosuresCursor Start(DateOnly from) => new(from, []);

    /// <summary>True when <paramref name="disclosureIndex"/> was already handled in this window.</summary>
    public bool Contains(long disclosureIndex) => Seen.Contains(disclosureIndex);

    /// <summary>The same cursor with one more handled disclosure (idempotent: no duplicate).</summary>
    public KapPublicDisclosuresCursor With(long disclosureIndex) =>
        Contains(disclosureIndex) ? this : this with { Seen = [.. Seen, disclosureIndex] };

    /// <summary>
    /// The cursor for a window the pass <b>completed</b>: the watermark becomes the window's last
    /// day and the seen set keeps every index handled since the pass started.
    /// <para>
    /// Accumulating rather than replacing is the honest rule (the 082 precedent): a window that took
    /// two passes to walk has one seen set, and replacing it at the end of the second pass would
    /// forget the first pass's completions and let it write them again. Keeping an index is never
    /// wrong — a handled disclosure keeps its own index forever — and it is stated here rather than
    /// claimed away that the row therefore grows for the lane's lifetime: that is a size cost, not a
    /// correctness one, and pruning safely needs index→window attribution the cursor does not record.
    /// </para>
    /// </summary>
    public KapPublicDisclosuresCursor AdvanceTo(DateOnly lastCoveredDay) =>
        this with { Watermark = lastCoveredDay, Seen = Seen.Distinct().Order().ToList() };

    /// <summary>Serializes the cursor for <c>coverage_metadata.notes</c> (stable, non-lossy).</summary>
    public string ToJson()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("source", KapPublicSourceOptions.SourceName);
            writer.WriteString("watermark", Watermark.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            writer.WriteStartArray("seen");
            foreach (var index in Seen.Distinct().Order())
            {
                writer.WriteNumberValue(index);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Reads a stored cursor back, or <c>null</c> when the row is absent or is not this lane's
    /// cursor shape. A value this reader does not understand is <b>not</b> reinterpreted as a
    /// watermark: the lane re-bootstraps from its configured lookback instead of guessing a
    /// position it never recorded.
    /// </summary>
    public static KapPublicDisclosuresCursor? TryParse(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(notes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("watermark", out var watermark)
                || watermark.ValueKind != JsonValueKind.String
                || !DateOnly.TryParseExact(
                    watermark.GetString(),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsed))
            {
                return null;
            }

            var seen = new List<long>();
            if (root.TryGetProperty("seen", out var seenElement) && seenElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in seenElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var index) && index > 0)
                    {
                        seen.Add(index);
                    }
                }
            }

            return new KapPublicDisclosuresCursor(parsed, seen.Distinct().Order().ToList());
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// Persists the public-site disclosures feed's date-watermark cursor (TKT-foundation-084,
/// FR-MDF-010/011) in the existing <c>coverage_metadata</c> table — Scope <c>market</c>, no
/// instrument, the lane's <b>own</b> <see cref="DataType"/>.
/// <para>
/// The data type is deliberately a different string from the VYK lane's
/// <c>kap-disclosures-market-cursor</c> and from the public statements lane's
/// <c>kap-public-statements-cursor</c>, and it is outside <c>CoverageDataTypes.InstrumentScoped</c>,
/// so the no-silent-gaps reconcile never refreshes or clobbers it and no two lanes' cursor rows can
/// be mistaken for one another.
/// </para>
/// </summary>
public sealed class KapPublicDisclosuresCursorStore
{
    /// <summary>The <c>coverage_metadata.data_type</c> of the public-site disclosures cursor.</summary>
    public const string DataType = "kap-public-disclosures-cursor";

    /// <summary>The market scope of the one cursor row (no instrument, no fund).</summary>
    public const string Scope = "market";

    private readonly DegerliDbContext _db;

    public KapPublicDisclosuresCursorStore(DegerliDbContext db) => _db = db;

    /// <summary>The persisted cursor, or <c>null</c> when the lane has never completed a pass.</summary>
    public async Task<KapPublicDisclosuresCursor?> GetAsync(CancellationToken cancellationToken = default)
    {
        var notes = await _db.CoverageMetadata
            .Where(row => row.Scope == Scope
                && row.InstrumentId == null
                && row.FundId == null
                && row.DataType == DataType)
            .Select(row => row.Notes)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return KapPublicDisclosuresCursor.TryParse(notes);
    }

    /// <summary>
    /// Upserts the one cursor row (idempotent on the scope/data-type key). The caller persists it
    /// <b>after</b> its fact write (TKT-foundation-069's shape), never before.
    /// </summary>
    public async Task SetAsync(KapPublicDisclosuresCursor cursor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cursor);

        var row = await _db.CoverageMetadata
            .FirstOrDefaultAsync(
                existing => existing.Scope == Scope
                    && existing.InstrumentId == null
                    && existing.FundId == null
                    && existing.DataType == DataType,
                cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            _db.CoverageMetadata.Add(new CoverageMetadata
            {
                Scope = Scope,
                InstrumentId = null,
                FundId = null,
                DataType = DataType,
                Notes = cursor.ToJson(),
            });
        }
        else
        {
            row.Notes = cursor.ToJson();
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
