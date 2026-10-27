using System.Globalization;
using System.Text.Json;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Ingestion.KapPublic;

/// <summary>
/// The public-site statements lane's <b>own</b> incremental cursor (01 v1.5.56 §10.9.19,
/// TKT-foundation-082; FR-MDF-010/011).
/// <para>
/// The public list is <b>date-windowed</b>, not index-paged: there is no index to walk forward.
/// The cursor is therefore a <b>date watermark</b> (the first day the next pass must list from)
/// plus the <b>set of public <c>disclosureIndex</c> values already completed inside the current
/// window</b> — so a pass truncated mid-window resumes exactly at the next unfinished report
/// instead of re-reading the whole window.
/// </para>
/// <para>
/// <b>Its own space, never mixed.</b> Everything here is the public site's own
/// <c>disclosureIndex</c> space (ASELS 2026-06 FR = <c>1643141</c>), recorded in its own
/// <c>coverage_metadata.data_type</c> rows (<see cref="KapPublicStatementCursorStore.DataType"/>)
/// — never in a VYK <c>kap-disclosure-cursor</c> row, and never carrying a VYK index (head
/// <c>1231017</c>). The store refuses to write a value that is not its own shape, and the two
/// lanes' data types are distinct strings, so the two spaces cannot meet.
/// </para>
/// </summary>
/// <param name="Watermark">The first date the next pass lists from (inclusive).</param>
/// <param name="Seen">Public indices already completed inside the window starting at <paramref name="Watermark"/>.</param>
/// <remarks>
/// <b>The quarantine replay's persisted state (01 v1.5.67 §10.9.19, TKT-foundation-092).</b> A
/// report whose <i>detail</i> the source failed to deliver in transport is quarantined
/// (<c>DISCLOSURE_DETAIL_UNFETCHABLE</c>) <b>and</b> marked completed — it enters
/// <see cref="Seen"/>, so the watermark advances past it and an ordinary pass can never list it
/// again. That is why the once-only replay needs its own persisted state, and why it lives here
/// rather than in a new table (<see cref="Quarantined"/> and <see cref="Retried"/>): the cursor row
/// is already the lane's own, already instrument-scoped, and a migration is not needed to make a
/// bounded repair possible. A shape verdict (<c>UNPARSEABLE_PAYLOAD</c>) is <b>never</b> recorded
/// here — it is a verdict about the body, not a transport outage, and replaying it would re-fetch
/// a report only to re-learn the same answer.
/// </remarks>
public sealed record KapPublicStatementCursor(DateOnly Watermark, IReadOnlyList<long> Seen)
{
    /// <summary>
    /// The <b>transport-failure backlog</b>: public indices whose detail the source failed to
    /// deliver, each owing at most <b>one</b> replay (the 063 stale-cursor-reset doctrine, one level
    /// down — recovery is bounded, recorded, never infinite).
    /// </summary>
    public IReadOnlyList<long> Quarantined { get; init; } = [];

    /// <summary>
    /// The <b>once-only tombstone</b>: indices whose single replay has been <b>spent</b>, whether it
    /// recovered the report or failed again. An index in this set is never replayed a third time —
    /// the "never retried forever" half of the 063 doctrine, persisted so a later process (or a
    /// later pass, days later) cannot forget it. Only an explicit cursor reset clears it.
    /// </summary>
    public IReadOnlyList<long> Retried { get; init; } = [];

    /// <summary>An empty cursor starting at <paramref name="from"/> (a bootstrap pass).</summary>
    public static KapPublicStatementCursor Start(DateOnly from) => new(from, []);

    /// <summary>True when <paramref name="disclosureIndex"/> was already completed in this window.</summary>
    public bool Contains(long disclosureIndex) => Seen.Contains(disclosureIndex);

    /// <summary>The same cursor with one more completed report (idempotent: no duplicate).</summary>
    public KapPublicStatementCursor With(long disclosureIndex) =>
        Contains(disclosureIndex) ? this : this with { Seen = [.. Seen, disclosureIndex] };

    /// <summary>The indices this pass may still replay, oldest first.</summary>
    public IReadOnlyList<long> PendingReplay() =>
        Quarantined.Where(index => !Retried.Contains(index)).Order().ToList();

    /// <summary>
    /// Records a <b>transport-failure</b> quarantine: the index becomes replay-eligible exactly once
    /// (and stays in the seen set — it was walked, and its window must not be re-listed for it).
    /// An index whose replay is already spent is added to the seen set only: an index gets one
    /// automatic replay in its lifetime, never one per quarantine.
    /// </summary>
    public KapPublicStatementCursor WithQuarantine(long disclosureIndex)
    {
        var seen = With(disclosureIndex);
        return Retried.Contains(disclosureIndex)
            ? seen
            : seen with { Quarantined = Quarantined.Contains(disclosureIndex) ? Quarantined : [.. Quarantined, disclosureIndex] };
    }

    /// <summary>
    /// Spends the one replay of <paramref name="disclosureIndex"/>: it leaves the backlog and enters
    /// the tombstone, whatever the outcome was. Called for a recovery <b>and</b> for a replay that
    /// failed again — that is what makes the second failure terminal rather than a retry ladder.
    /// </summary>
    public KapPublicStatementCursor WithReplaySpent(long disclosureIndex) => this with
    {
        Quarantined = Quarantined.Where(candidate => candidate != disclosureIndex).ToList(),
        Retried = Retried.Contains(disclosureIndex) ? Retried : [.. Retried, disclosureIndex],
    };

    /// <summary>
    /// The cursor for a <b>fully walked</b> window: the watermark is the window's last covered day
    /// (inclusive, so a filing published later that same day is still seen by the next pass) and the
    /// seen set is every report completed since the watermark the pass started from.
    /// <para>
    /// Accumulating rather than replacing is the honest rule: a window that took <b>two</b> passes to
    /// walk (the first bounded, the second resuming it) has one seen set, and replacing it at the end
    /// of the second pass would forget the first pass's completions and re-fetch them. Keeping an
    /// index is never wrong — a completed report keeps its own index forever.
    /// </para>
    /// <para>
    /// <b>The set is not bounded by one window.</b> Because the seen set accumulates across the whole
    /// walk and is re-persisted on every window close and on every completed report, the
    /// <c>coverage_metadata.notes</c> row grows for the lane's lifetime (each index ever walked stays
    /// in it). That is a size cost, not a correctness one, and it is stated here rather than claimed
    /// away (review round 1, D11): pruning it safely needs index→window attribution the cursor does not
    /// record, so pruning is a schema change in its own right.
    /// </para>
    /// <para>
    /// The retry markers travel with the cursor through every window close: advancing the watermark
    /// is a position change, never a reason to forget which quarantined reports still owe their one
    /// replay (TKT-foundation-092).
    /// </para>
    /// </summary>
    public KapPublicStatementCursor CloseWindow(DateOnly windowLastDay) =>
        this with { Watermark = windowLastDay, Seen = Seen.Distinct().Order().ToList() };

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
            foreach (var index in Seen.Order())
            {
                writer.WriteNumberValue(index);
            }

            writer.WriteEndArray();

            // The quarantine replay's bounded state (TKT-foundation-092): what still owes its one
            // replay, and what has already spent it. Written always (even empty) so a reader never
            // has to infer "absent" from "not written by this version".
            writer.WriteStartArray("quarantined");
            foreach (var index in Quarantined.Distinct().Order())
            {
                writer.WriteNumberValue(index);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("retried");
            foreach (var index in Retried.Distinct().Order())
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
    public static KapPublicStatementCursor? TryParse(string? notes)
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

            // A cursor written before TKT-foundation-092 carries neither marker array: an absent
            // array is an empty one, so an existing row keeps its position and simply has no replay
            // backlog to work through. It is never reinterpreted as "everything was retried".
            return new KapPublicStatementCursor(parsed, seen.Distinct().ToList())
            {
                Quarantined = ReadIndexes(root, "quarantined"),
                Retried = ReadIndexes(root, "retried"),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads one of the cursor's index arrays, ignoring anything that is not a positive integer.</summary>
    private static IReadOnlyList<long> ReadIndexes(JsonElement root, string property)
    {
        var indexes = new List<long>();
        if (!root.TryGetProperty(property, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return indexes;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var index) && index > 0)
            {
                indexes.Add(index);
            }
        }

        return indexes.Distinct().ToList();
    }
}

/// <summary>
/// Persists the public-site statements lane's date-watermark cursor (TKT-foundation-082,
/// FR-MDF-010/011) in the existing <c>coverage_metadata</c> table — Scope <c>instrument</c>, the
/// lane's <b>own</b> <see cref="DataType"/>.
/// <para>
/// The data type is deliberately a different string from the VYK lane's
/// <see cref="Statements.KapDisclosureCursorStore.DataType"/> (<c>kap-disclosure-cursor</c>), and
/// outside <c>CoverageDataTypes.InstrumentScoped</c>, so the no-silent-gaps reconcile never
/// refreshes or clobbers it and the two lanes' cursor rows can never be confused for one another.
/// </para>
/// </summary>
public sealed class KapPublicStatementCursorStore
{
    /// <summary>The <c>coverage_metadata.data_type</c> of the public-site statements cursor.</summary>
    public const string DataType = "kap-public-statements-cursor";

    private const string InstrumentScope = "instrument";

    private readonly DegerliDbContext _db;

    public KapPublicStatementCursorStore(DegerliDbContext db) => _db = db;

    /// <summary>The persisted cursor for <paramref name="symbol"/>, or <c>null</c> when none exists.</summary>
    public async Task<KapPublicStatementCursor?> GetAsync(string symbol, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        var instrumentId = await InstrumentIdAsync(symbol, cancellationToken).ConfigureAwait(false);
        if (instrumentId is null)
        {
            return null;
        }

        var notes = await _db.CoverageMetadata
            .Where(row => row.Scope == InstrumentScope
                && row.InstrumentId == instrumentId
                && row.DataType == DataType)
            .Select(row => row.Notes)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return KapPublicStatementCursor.TryParse(notes);
    }

    /// <summary>
    /// The persisted watermark of every symbol that has one, in one read per run
    /// (TKT-foundation-082 review round 1, D2). The public run uses it to iterate the universe
    /// <b>least-recently-walked first</b>, so a company the pass budget skipped is at the front of
    /// the next pass instead of being starved forever behind the same head companies.
    /// A symbol with no cursor row is simply absent — absence means "never walked", and the caller
    /// orders it first.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, DateOnly>> GetWatermarksAsync(
        IReadOnlyCollection<string> symbols,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        if (symbols.Count == 0)
        {
            return new Dictionary<string, DateOnly>(StringComparer.Ordinal);
        }

        var wanted = symbols.Distinct(StringComparer.Ordinal).ToList();
        var rows = await _db.CoverageMetadata
            .Where(row => row.Scope == InstrumentScope
                && row.DataType == DataType
                && row.InstrumentId != null)
            .Join(
                _db.Instruments.Where(instrument => wanted.Contains(instrument.Symbol)),
                row => row.InstrumentId,
                instrument => (long?)instrument.Id,
                (row, instrument) => new { instrument.Symbol, row.Notes })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var watermarks = new Dictionary<string, DateOnly>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (row.Symbol is not null && KapPublicStatementCursor.TryParse(row.Notes) is { } cursor)
            {
                watermarks.TryAdd(row.Symbol, cursor.Watermark);
            }
        }

        return watermarks;
    }

    /// <summary>
    /// Upserts <paramref name="symbol"/>'s cursor (idempotent on the instrument/data-type key). An
    /// unresolvable symbol is a no-op — there is nothing to key the position to.
    /// </summary>
    public async Task SetAsync(
        string symbol,
        KapPublicStatementCursor cursor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentNullException.ThrowIfNull(cursor);

        var instrumentId = await InstrumentIdAsync(symbol, cancellationToken).ConfigureAwait(false);
        if (instrumentId is null)
        {
            return;
        }

        var id = instrumentId.Value;
        var row = await RowAsync(id, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            _db.CoverageMetadata.Add(new CoverageMetadata
            {
                Scope = InstrumentScope,
                InstrumentId = id,
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

    private async Task<CoverageMetadata?> RowAsync(long instrumentId, CancellationToken cancellationToken) =>
        await _db.CoverageMetadata
            .FirstOrDefaultAsync(
                row => row.Scope == InstrumentScope
                    && row.InstrumentId == instrumentId
                    && row.DataType == DataType,
                cancellationToken)
            .ConfigureAwait(false);

    private async Task<long?> InstrumentIdAsync(string symbol, CancellationToken cancellationToken) =>
        await _db.Instruments
            .Where(instrument => instrument.Symbol == symbol)
            .Select(instrument => (long?)instrument.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
}

/// <summary>
/// Records the public-site statements lane's <b>achieved depth</b> (TKT-foundation-082,
/// FR-MDF-010/011) — the backfill half of the walk's honesty rule.
/// <para>
/// A configured lookback is a <b>request</b>, not a finding: the pass records which date range it
/// asked for and which range it actually walked, in its own
/// <see cref="DataType"/> rows. A truncated pass therefore says "I asked from X and got as far as
/// Y", and never stands in for "this company has no statements before Y" — the honest absence of a
/// walk is not the absence of a fact.
/// </para>
/// </summary>
public sealed class KapPublicStatementDepthStore
{
    /// <summary>The <c>coverage_metadata.data_type</c> of the public-site statements achieved depth.</summary>
    public const string DataType = "kap-public-statements-depth";

    private const string InstrumentScope = "instrument";

    private readonly DegerliDbContext _db;

    public KapPublicStatementDepthStore(DegerliDbContext db) => _db = db;

    /// <summary>
    /// Records one pass's requested window and the depth it actually achieved:
    /// <paramref name="requestedFrom"/>/<paramref name="requestedTo"/> are what the lookback asked
    /// for, <paramref name="achievedFrom"/>/<paramref name="achievedTo"/> are the windows the pass
    /// completed (<c>null</c> when it completed none — the achieved depth is then honestly unknown,
    /// never zero). Idempotent on the instrument/data-type key: a later pass updates the one row.
    /// <para>
    /// <b>The configured request is not overwritten by a resumed pass.</b> A bootstrap records
    /// <paramref name="requestedFrom"/> (the configured lookback start). A resumed pass is given its
    /// own start (the stored watermark) as its effective walk start, but the <i>request</i> did not
    /// change: the configured lookback is still the range nobody has achieved yet. Overwriting it with
    /// the watermark would silently turn "asked for 24 months, walked 6" into "asked for 6 and got it"
    /// (FR-MDF-010; review round 1, D12). Only a real bootstrap writes
    /// <paramref name="requestedFrom"/>.
    /// </para>
    /// </summary>
    public async Task RecordAsync(
        string symbol,
        DateOnly requestedFrom,
        DateOnly requestedTo,
        DateOnly? achievedFrom,
        DateOnly? achievedTo,
        bool bootstrapped,
        bool truncated,
        string? truncationReason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        var instrumentId = await _db.Instruments
            .Where(instrument => instrument.Symbol == symbol)
            .Select(instrument => (long?)instrument.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (instrumentId is null)
        {
            return;
        }

        var row = await _db.CoverageMetadata
            .FirstOrDefaultAsync(
                existing => existing.Scope == InstrumentScope
                    && existing.InstrumentId == instrumentId
                    && existing.DataType == DataType,
                cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            row = new CoverageMetadata
            {
                Scope = InstrumentScope,
                InstrumentId = instrumentId,
                DataType = DataType,
            };
            _db.CoverageMetadata.Add(row);
        }
        else if (!bootstrapped && TryReadRequestedFrom(row.Notes) is { } storedRequest)
        {
            // A resumed pass keeps the configured request the bootstrap recorded; only its own
            // walk start (the watermark) moved. See the remark on this method.
            requestedFrom = storedRequest;
        }

        row.AvailableFrom = achievedFrom;
        row.AvailableTo = achievedTo;
        row.Notes = Notes(requestedFrom, requestedTo, achievedFrom, achievedTo, bootstrapped, truncated, truncationReason);

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The <c>requestedFrom</c> already recorded in a depth row's notes, if readable.</summary>
    private static DateOnly? TryReadRequestedFrom(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(notes);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("requestedFrom", out var requested)
                && requested.ValueKind == JsonValueKind.String
                && DateOnly.TryParseExact(
                    requested.GetString(),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsed))
            {
                return parsed;
            }
        }
        catch (JsonException)
        {
            // An unreadable row is not reinterpreted: this pass's own request is what it records.
        }

        return null;
    }

    /// <summary>
    /// The provenance note: what was requested, what was achieved, and — when the pass was bounded —
    /// the named reason. <c>bootstrapped</c> marks a pass that started from the configured lookback
    /// rather than a stored watermark (the 078 vocabulary, re-applied).
    /// </summary>
    private static string Notes(
        DateOnly requestedFrom,
        DateOnly requestedTo,
        DateOnly? achievedFrom,
        DateOnly? achievedTo,
        bool bootstrapped,
        bool truncated,
        string? truncationReason)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("source", KapPublicSourceOptions.SourceName);
            writer.WriteString("requestedFrom", requestedFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            writer.WriteString("requestedTo", requestedTo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            if (achievedFrom is { } from)
            {
                writer.WriteString("achievedFrom", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }

            if (achievedTo is { } to)
            {
                writer.WriteString("achievedTo", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }

            writer.WriteBoolean("bootstrapped", bootstrapped);
            writer.WriteBoolean("truncated", truncated);
            if (truncationReason is not null)
            {
                writer.WriteString("truncationReason", truncationReason);
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}
