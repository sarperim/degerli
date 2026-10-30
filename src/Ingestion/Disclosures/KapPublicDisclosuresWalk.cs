using System.Globalization;
using Degerli.Ingestion.KapPublic;
using Microsoft.Extensions.Logging;

namespace Degerli.Ingestion.Disclosures;

/// <summary>
/// One feed pass's date range and how wide each list request is (01 v1.5.85 §10.9.19,
/// TKT-foundation-084). The range comes from the persisted watermark — or, on a bootstrap, from the
/// configured lookback — never from an assumption about what the market published.
/// </summary>
/// <param name="From">The first day the pass must cover (the watermark, or the lookback start).</param>
/// <param name="To">The last day the pass must cover (the run's day).</param>
/// <param name="WindowDays">How many days one byCriteria list request covers.</param>
public sealed record KapPublicDisclosuresPlan(DateOnly From, DateOnly To, int WindowDays)
{
    /// <summary>The list windows the plan asks for, oldest first (so the watermark advances monotonically).</summary>
    public IReadOnlyList<(DateOnly From, DateOnly To)> Windows()
    {
        var windows = new List<(DateOnly From, DateOnly To)>();
        if (From > To)
        {
            return windows;
        }

        var width = WindowDays > 0 ? WindowDays : 1;
        var cursor = From;
        while (cursor <= To)
        {
            var end = cursor.AddDays(width - 1);
            if (end > To || end < cursor)
            {
                end = To;
            }

            windows.Add((cursor, end));
            if (end == To)
            {
                break;
            }

            cursor = end.AddDays(1);
        }

        return windows;
    }
}

/// <summary>
/// One row (or one list answer) the pass could not turn into a stored fact, with the evidence that
/// says why — the 063 per-item isolation applied to this feed.
/// </summary>
/// <param name="ReasonCode">The named reason (a <see cref="KapPublicReasonCodes"/> value or a
/// feed-local one such as <see cref="KapPublicDisclosuresWalker.UnparseablePublishDate"/>).</param>
/// <param name="DisclosureIndex">The <b>public-site</b> index of the row (its own space), when the
/// failure is about one row; <c>null</c> for a whole-list verdict.</param>
/// <param name="Detail">The human-readable detail (a bounded excerpt of what the source said).</param>
/// <param name="EvidenceJson">What a quarantine row keeps: the source's own answer, verbatim, when
/// there was one — never a fabricated body.</param>
public sealed record KapPublicDisclosureSkip(
    string ReasonCode,
    long? DisclosureIndex,
    string? Detail,
    string? EvidenceJson);

/// <summary>
/// A <b>list</b> the source could not deliver in the middle of a pass. It travels <b>with</b> the
/// pass's counts instead of unwinding it: the windows already completed are real, and a result that
/// reported zero beside them would be a lie (the 082/TC-MDF-109 D1 rule).
/// </summary>
/// <param name="ReasonCode">The source's own reason (<see cref="KapPublicReasonCodes"/>).</param>
/// <param name="Detail">The source's own detail text.</param>
/// <param name="WindowFrom">The window whose list failed (the first day it asked for).</param>
/// <param name="WindowTo">The window's last day.</param>
public sealed record KapPublicDisclosuresFailure(
    string ReasonCode,
    string? Detail,
    DateOnly WindowFrom,
    DateOnly WindowTo);

/// <summary>
/// One market-wide pass's result (TKT-foundation-084).
/// <para>
/// <see cref="Cursor"/> is <b>collected, not persisted</b> (TKT-foundation-069's shape): the job
/// stores <see cref="Facts"/> and the skip evidence first and only then writes the cursor, so a
/// store failure cannot advance the watermark past facts that never landed.
/// </para>
/// </summary>
/// <param name="Cursor">The cursor to persist after the fact write.</param>
/// <param name="Facts">The bound constituents' disclosure facts (only these are written).</param>
/// <param name="LastCoveredDay">The last day a <b>completed</b> window covered; <c>null</c> when none.</param>
/// <param name="WindowsListed">List requests issued.</param>
/// <param name="WindowsWalked">Windows walked to completion.</param>
/// <param name="HonestEmptyWindows">Windows the source answered with a well-formed empty list.</param>
/// <param name="RowsListed">Market rows the source returned (bound and out-of-bound alike).</param>
/// <param name="OutOfBoundRows">Rows whose owning company is outside the bound universe — counted
/// with the named reason and never fetched further.</param>
/// <param name="AlreadySeenRows">Rows already handled in this window (the seen set's dedupe).</param>
/// <param name="Skips">Per-row and per-window shape failures, with their evidence.</param>
/// <param name="Truncated">True when a budget bound stopped the pass.</param>
/// <param name="TruncationReason">The named bound (<see cref="KapPublicReasonCodes"/>).</param>
/// <param name="Bootstrapped">True when the pass started from the configured lookback because the
/// lane had no persisted cursor (provenance, never a claim that the market was quiet).</param>
public sealed record KapPublicDisclosuresWalkResult(
    KapPublicDisclosuresCursor Cursor,
    IReadOnlyList<DisclosureFact> Facts,
    DateOnly? LastCoveredDay,
    int WindowsListed,
    int WindowsWalked,
    int HonestEmptyWindows,
    int RowsListed,
    int OutOfBoundRows,
    int AlreadySeenRows,
    IReadOnlyList<KapPublicDisclosureSkip> Skips,
    bool Truncated,
    string? TruncationReason,
    bool Bootstrapped,
    KapPublicDisclosuresFailure? Failure = null);

/// <summary>
/// The market-wide <b>recent-disclosures</b> feed of the KAP public site (01 v1.5.85 §10.9.19,
/// TKT-foundation-084; FR-MDF-005/008/011/012): a <b>date-windowed</b> byCriteria pass over the
/// <b>market's</b> disclosures — the query shape 079's OQ-1 probe recorded as working (an empty
/// <c>mkkMemberOidList</c> answers 12 market rows over 2026-10-24..2026-10-25).
/// <para>
/// <b>A separate source, next to the VYK feed — never a repointing of it.</b> This walk reads the
/// public site through 079's paced, single-flight client and 080's stored identity map, keeps its
/// own date-watermark cursor in its own <c>coverage_metadata</c> data type, stamps every fact
/// <c>kap-public</c>, and carries the public <c>disclosureIndex</c>. The VYK disclosures lane
/// (078's <c>kap</c> market feed) stays wired and untouched; the two feeds coexist as separate
/// sources and their index spaces never meet.
/// </para>
/// <para>
/// <b>Bound-only writes.</b> A market row names the companies it concerns through
/// <c>stockCodes</c> (the site's own ticker spelling; comma-joined when a filing concerns more than
/// one). Only codes that belong to the configured bound's constituents <b>and</b> have a stored
/// public-site identity (080's map) produce facts; every other row is counted under
/// <see cref="OutsideBoundUniverse"/> and never inspected further. That is what keeps this feed
/// from writing rows for the ~1,000 listed companies nobody asked for.
/// </para>
/// <para>
/// <b>No detail fetch at all.</b> The byCriteria row is rich — class, publish stamp, subject,
/// summary, related stocks, company title, code — so this feed needs no per-disclosure request: its
/// only requests are list pages, bounded by the shared <see cref="KapPublicWalkBudget"/> (page
/// ceiling + wall-clock deadline). The detail ceiling is therefore never spent here, which is stated
/// rather than silently assumed.
/// </para>
/// <para>
/// <b>Honest statuses.</b> A well-formed empty window is an <b>honest empty</b> — the watermark
/// still advances <i>forward</i> past it (nothing moves backwards); a transport/non-success answer
/// is a <see cref="KapPublicDisclosuresFailure"/> (the run is <c>failed</c>, nothing fabricated); a
/// body that fails the shape check is a named, evidence-carrying skip (<c>UNPARSEABLE_PAYLOAD</c>)
/// with the source's own body. A row whose publish stamp cannot be read is a named, counted skip —
/// never a guessed date.
/// </para>
/// </summary>
public sealed class KapPublicDisclosuresWalker
{
    /// <summary>
    /// The named reason recorded for a market row whose owning company is outside the bound universe
    /// (the VYK lane's <c>OUTSIDE_BOUND_UNIVERSE</c>, re-applied to this source): counted, logged,
    /// never written under a fabricated identity.
    /// </summary>
    public const string OutsideBoundUniverse = DisclosuresSourceAdapter.OutsideBoundUniverse;

    /// <summary>
    /// The named reason for a listed row whose <c>publishDate</c> is not a readable stamp. The row is
    /// skipped and counted with the source's own value quoted — a date is never guessed.
    /// </summary>
    public const string UnparseablePublishDate = "UNPARSEABLE_PUBLISH_DATE";

    /// <summary>
    /// The public site's own disclosure-page route, in the spelling a <b>committed live capture</b>
    /// carries (the VYK detail body's <c>link</c>: <c>https://www.kap.org.tr/Bildirim/{index}</c>,
    /// <c>RealSourceCatalog</c>) — used to stamp each row with the public index it was read from, so
    /// a stored row answers both <i>which source</i> and <i>which disclosure</i> without a schema
    /// change. The index stamped is always this lane's own (public) space; it is never converted
    /// between spaces.
    /// </summary>
    public const string DisclosurePageOrigin = "https://www.kap.org.tr";

    private readonly KapPublicSourceReader _reader;
    private readonly KapPublicWalkBudget _budget;
    private readonly ILogger? _logger;

    public KapPublicDisclosuresWalker(
        KapPublicSourceReader reader,
        KapPublicWalkBudget budget,
        ILogger? logger = null)
    {
        _reader = reader;
        _budget = budget;
        _logger = logger;
    }

    /// <summary>The stored row's <c>sourceUrl</c> for one public disclosure index (see <see cref="DisclosurePageOrigin"/>).</summary>
    public static string DisclosureUrl(long disclosureIndex) =>
        $"{DisclosurePageOrigin}/Bildirim/{disclosureIndex.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Walks <paramref name="plan"/>'s windows from <paramref name="cursor"/> (oldest first),
    /// returning the bound constituents' facts and the cursor work the job persists after its write.
    /// </summary>
    /// <param name="cursor">The cursor the pass starts from (<see cref="KapPublicDisclosuresCursor.Start"/> on a bootstrap).</param>
    /// <param name="plan">The date range and window width.</param>
    /// <param name="boundSymbols">The symbols this feed may write (bound constituents with a stored public identity).</param>
    /// <param name="cancellationToken">Run cancellation.</param>
    public async Task<KapPublicDisclosuresWalkResult> WalkAsync(
        KapPublicDisclosuresCursor cursor,
        KapPublicDisclosuresPlan plan,
        IReadOnlySet<string> boundSymbols,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(boundSymbols);

        var facts = new List<DisclosureFact>();
        var skips = new List<KapPublicDisclosureSkip>();
        var current = cursor;
        DateOnly? lastCoveredDay = null;
        var windowsListed = 0;
        var windowsWalked = 0;
        var honestEmpty = 0;
        var rowsListed = 0;
        var outOfBound = 0;
        var alreadySeen = 0;
        KapPublicDisclosuresFailure? failure = null;

        foreach (var (from, to) in plan.Windows())
        {
            // Boundedness is politeness (079/082): the shared budget admits every page before it is
            // issued, and on truncation the pass stops at the last window it *completed*.
            if (!_budget.TryBeginPage())
            {
                break;
            }

            windowsListed++;
            var read = await _reader
                .ReadDisclosureListAsync(new KapPublicListQuery(from, to), cancellationToken)
                .ConfigureAwait(false);

            if (read.IsSourceDown)
            {
                failure = new KapPublicDisclosuresFailure(
                    read.ReasonCode ?? KapPublicReasonCodes.SourceFetchFailed,
                    read.Detail,
                    from,
                    to);
                _logger?.LogWarning(
                    "disclosures: public-site market list failed for {From}..{To} ({ReasonCode}): {Detail}; the completed windows' counts and the saved watermark stand",
                    from,
                    to,
                    failure.ReasonCode,
                    failure.Detail);
                break;
            }

            if (read.IsQuarantined)
            {
                // A 200 body that is not the byCriteria array: a verdict about the body, never a
                // transport failure. The pass cannot trust the window, so it stops there with the
                // evidence; nothing is written for it and the watermark does not move past it.
                skips.Add(new KapPublicDisclosureSkip(
                    KapPublicReasonCodes.UnparseablePayload,
                    DisclosureIndex: null,
                    read.Detail,
                    read.RawBody));
                _logger?.LogWarning(
                    "disclosures: public-site market list for {From}..{To} is not the byCriteria array and is quarantined with its body ({ReasonCode}): {Detail}",
                    from,
                    to,
                    KapPublicReasonCodes.UnparseablePayload,
                    read.Detail);
                break;
            }

            if (read.IsHonestEmpty)
            {
                // An honest empty window: nothing to write, and the watermark advances *forward*
                // past it — an empty answer is a statement about the window, never a reason to move
                // the position backwards.
                honestEmpty++;
                windowsWalked++;
                current = current.AdvanceTo(to);
                lastCoveredDay = to;
                _budget.Complete(to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                _logger?.LogInformation(
                    "disclosures: public-site window {From}..{To} is honestly empty ({ReasonCode}); the watermark advances to {Watermark}",
                    from,
                    to,
                    KapPublicReasonCodes.HonestEmpty,
                    to);
                continue;
            }

            var rows = read.Payload!;
            rowsListed += rows.Count;
            foreach (var row in rows)
            {
                if (current.Contains(row.DisclosureIndex))
                {
                    // The watermark is inclusive, so the newest day is re-listed by the next pass.
                    // The seen set is what keeps that from writing — or re-counting — anything.
                    alreadySeen++;
                    continue;
                }

                current = current.With(row.DisclosureIndex);

                var publishedOn = ParsePublishStamp(row.PublishDate);
                if (publishedOn is null)
                {
                    skips.Add(new KapPublicDisclosureSkip(
                        UnparseablePublishDate,
                        row.DisclosureIndex,
                        $"publishDate '{row.PublishDate}' is not a readable dd.MM.yyyy[ HH:mm:ss] stamp (index {row.DisclosureIndex})",
                        Evidence(row)));
                    _logger?.LogWarning(
                        "disclosures: public-site row {Index} carries an unreadable publishDate '{PublishDate}' ({ReasonCode}); skipped, never guessed",
                        row.DisclosureIndex,
                        row.PublishDate,
                        UnparseablePublishDate);
                    continue;
                }

                var matched = MatchBoundSymbols(row.StockCodes, boundSymbols);
                if (matched.Count == 0)
                {
                    // Outside the bound: counted with the named reason, never fetched further and
                    // never written under a fabricated identity (the 078 rule, this source's shape).
                    outOfBound++;
                    continue;
                }

                foreach (var symbol in matched)
                {
                    facts.Add(new DisclosureFact(
                        row.DisclosureIndex,
                        symbol,
                        DisclosureTypeOf(row),
                        publishedOn,
                        TitleOf(row),
                        DisclosureUrl(row.DisclosureIndex)));
                }
            }

            windowsWalked++;
            current = current.AdvanceTo(to);
            lastCoveredDay = to;
            _budget.Complete(to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (outOfBound > 0)
        {
            _logger?.LogInformation(
                "disclosures: {Count} public-site market row(s) belong to companies outside the bound and were counted, not written ({ReasonCode})",
                outOfBound,
                OutsideBoundUniverse);
        }

        return new KapPublicDisclosuresWalkResult(
            current,
            facts,
            lastCoveredDay,
            windowsListed,
            windowsWalked,
            honestEmpty,
            rowsListed,
            outOfBound,
            alreadySeen,
            skips,
            _budget.Truncated,
            _budget.TruncationReason,
            Bootstrapped: false,
            failure);
    }

    /// <summary>
    /// The bound symbols a market row concerns: its <c>stockCodes</c> split on commas (the site
    /// joins a multi-company filing's codes; single-code rows carry one) and intersected with
    /// <paramref name="boundSymbols"/>. A row with no code, or with codes nobody bound, matches
    /// nothing and is counted out-of-bound.
    /// </summary>
    private static IReadOnlyList<string> MatchBoundSymbols(string? stockCodes, IReadOnlySet<string> boundSymbols)
    {
        if (string.IsNullOrWhiteSpace(stockCodes))
        {
            return [];
        }

        var matched = new List<string>();
        foreach (var code in stockCodes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (boundSymbols.Contains(code) && !matched.Contains(code, StringComparer.Ordinal))
            {
                matched.Add(code);
            }
        }

        return matched;
    }

    /// <summary>
    /// The row's disclosure type: the site's own <c>disclosureType</c> when it carries one, else its
    /// <c>disclosureClass</c>. Both are the capture's members — nothing is derived.
    /// </summary>
    private static string? DisclosureTypeOf(KapPublicDisclosureRow row) =>
        !string.IsNullOrWhiteSpace(row.DisclosureType) ? row.DisclosureType!.Trim()
        : !string.IsNullOrWhiteSpace(row.DisclosureClass) ? row.DisclosureClass!.Trim()
        : null;

    /// <summary>
    /// The row's title: its <c>subject</c>, falling back to its <c>summary</c>. The company title
    /// (<c>kapTitle</c>) is deliberately <b>not</b> used — it names the issuer, not the disclosure,
    /// and presenting it as the title would misdescribe every row.
    /// </summary>
    private static string? TitleOf(KapPublicDisclosureRow row) =>
        !string.IsNullOrWhiteSpace(row.Subject) ? row.Subject!.Trim()
        : !string.IsNullOrWhiteSpace(row.Summary) ? row.Summary!.Trim()
        : null;

    /// <summary>The row's own members, kept as evidence for a quarantine row (never re-serialized into a fabricated body).</summary>
    private static string Evidence(KapPublicDisclosureRow row) =>
        System.Text.Json.JsonSerializer.Serialize(row, KapPublicJsonForEvidence);

    /// <summary>The publish stamp <c>dd.MM.yyyy HH:mm:ss</c> (the site's spelling, captured 2026-10-25).</summary>
    private static DateOnly? ParsePublishStamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string[] formats = ["dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy"];
        return DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? DateOnly.FromDateTime(parsed)
            : null;
    }

    /// <summary>The serializer the evidence uses (the lane's own camelCase contract).</summary>
    private static readonly System.Text.Json.JsonSerializerOptions KapPublicJsonForEvidence = new(System.Text.Json.JsonSerializerDefaults.Web);
}
