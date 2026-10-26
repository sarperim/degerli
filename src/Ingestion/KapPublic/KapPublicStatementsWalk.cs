using Degerli.Ingestion.Sources;
using Degerli.Ingestion.Statements;
using Microsoft.Extensions.Logging;

namespace Degerli.Ingestion.KapPublic;

/// <summary>
/// One report the walk could not turn into stored facts, with the evidence that says why
/// (01 v1.5.56 §10.9.19; the 063 per-detail isolation applied to this source).
/// </summary>
/// <param name="Symbol">The bound symbol the report was being walked for.</param>
/// <param name="DisclosureIndex">The <b>public-site</b> index of the report (its own space).</param>
/// <param name="ReasonCode">The named reason — a <see cref="KapPublicReasonCodes"/> value.</param>
/// <param name="Detail">The human-readable detail (a bounded excerpt of what the source said).</param>
/// <param name="EvidenceJson">What a quarantine row keeps: the source's own answer verbatim when
/// there was one, otherwise the failure's own record — never a fabricated body.</param>
public sealed record KapPublicStatementSkip(
    string Symbol,
    long DisclosureIndex,
    string ReasonCode,
    string? Detail,
    string? EvidenceJson);

/// <summary>
/// What one decoded report produced at the store (the job owns the write, so it also owns what
/// the write meant).
/// </summary>
/// <param name="Inserted">Statement rows written.</param>
/// <param name="Unchanged">Statement rows already held by this lane (idempotent re-send).</param>
/// <param name="OverlapRetained">Statement rows <b>not</b> written because the other source already
/// covers the period (TC-MDF-110's named, counted retention).</param>
public readonly record struct KapPublicStatementWrite(int Inserted, int Unchanged, int OverlapRetained);

/// <summary>
/// The persistence hook the walk calls once per decoded report. The walker owns the network,
/// the bounds, the cursor and the decode; the job owns the store write, the coexistence rule and
/// the quarantine (the same split the VYK lane keeps — §10.9.11).
/// </summary>
public delegate Task<KapPublicStatementWrite> KapPublicStatementSink(
    KapPublicReport report,
    long disclosureIndex,
    CancellationToken cancellationToken);

/// <summary>
/// One company's walk plan: the date range to cover and how wide each list window is
/// (01 v1.5.56 §10.9.19). The range comes from the persisted watermark — or, on a bootstrap,
/// from the configured lookback — never from an assumption about what the archive holds.
/// </summary>
/// <param name="From">The first date the walk must cover (the watermark, or the lookback start).</param>
/// <param name="To">The last date the walk must cover (the run's day).</param>
/// <param name="WindowDays">How many days one byCriteria list request covers.</param>
public sealed record KapPublicStatementWalkPlan(DateOnly From, DateOnly To, int WindowDays)
{
    /// <summary>The list windows the plan asks for, oldest first (so the watermark advances monotonically).</summary>
    public IReadOnlyList<(DateOnly From, DateOnly To)> Windows()
    {
        var windows = new List<(DateOnly From, DateOnly To)>();
        if (From > To)
        {
            return windows;
        }

        var width = WindowDays > 0 ? WindowDays : 365;
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
/// One company's walk result: the cursor to persist (the walker already persisted it), what was
/// covered, what was skipped and — the honesty half — the depth actually achieved versus the
/// depth requested.
/// </summary>
public sealed record KapPublicStatementWalkResult(
    string Symbol,
    KapPublicStatementCursor Cursor,
    DateOnly RequestedFrom,
    DateOnly RequestedTo,
    DateOnly? AchievedFrom,
    DateOnly? AchievedTo,
    bool Bootstrapped,
    int WindowsListed,
    int WindowsWalked,
    int HonestEmptyWindows,
    int ReportsListed,
    int ReportsFetched,
    int Inserted,
    int Unchanged,
    int OverlapRetained,
    IReadOnlyList<KapPublicStatementSkip> Skips,
    bool Truncated,
    string? TruncationReason);

/// <summary>
/// The public-site statements <b>walk</b> (01 v1.5.56 §10.9.19, TKT-foundation-082;
/// FR-MDF-002/010/011/012): a <b>date-windowed</b> byCriteria pass over one company's
/// financial-report disclosures, each report fetched through the lane's own paced, single-flight
/// client (TKT-foundation-079, inherited unchanged) and decoded by TKT-foundation-081's decoder.
/// <para>
/// <b>Bounded by construction.</b> Every request is admitted by a shared
/// <see cref="KapPublicWalkBudget"/> (page ceiling, detail ceiling, wall-clock deadline) before it
/// is issued; on truncation the walk stops at the last <b>completed</b> report, persists the
/// cursor it reached and returns — an honest bounded pass with a saved position, which the next
/// pass resumes from. The budget is <b>per pass</b> (shared across a run's companies), so a run's
/// total request count and wall clock are bounded, not merely each company's.
/// </para>
/// <para>
/// <b>Honest depth.</b> The configured lookback is a <b>request</b>: a pass records the range it
/// asked for and the range it actually walked
/// (<see cref="KapPublicStatementDepthStore"/>), so nobody can read "I did not walk it" as "there
/// is nothing there" (FR-MDF-010).
/// </para>
/// <para>
/// <b>Isolation.</b> A detail the source cannot deliver (transport, non-success, timeout) or a
/// body that is not a report is a <b>named, counted skip with its evidence</b> — the company's
/// other reports are still written and the walk continues. A <b>list</b> that is source-down is
/// not isolated: the walk cannot proceed without its index, so it surfaces as a
/// <see cref="SourceFetchException"/> for the job to fail the company with.
/// </para>
/// </summary>
public sealed class KapPublicStatementsWalker
{
    /// <summary>
    /// The site's own discriminator for a <b>financial report</b> inside the <c>FR</c> class
    /// (evidenced by the committed byCriteria capture, 2026-10-25): the ten-row capture carries
    /// three <c>disclosureCategory=FR</c> rows — exactly the three with the
    /// <c>Finansal Rapor</c> subject (<c>1643141</c>, <c>1598316</c>, <c>1561039</c>) — while the
    /// other seven rows in the same class are <c>disclosureCategory=ODA</c> (statements of
    /// responsibility and activity reports, which carry no financial tables). Filtering on the
    /// class alone would fetch those seven and quarantine them as unparseable; the category is
    /// the source's own word for "this row is the financial report".
    /// </summary>
    public const string FinancialReportCategory = "FR";

    private readonly KapPublicSourceReader _reader;
    private readonly KapPublicStatementCursorStore _cursors;
    private readonly KapPublicStatementDepthStore _depths;
    private readonly KapPublicWalkBudget _budget;
    private readonly ILogger? _logger;

    public KapPublicStatementsWalker(
        KapPublicSourceReader reader,
        KapPublicStatementCursorStore cursors,
        KapPublicStatementDepthStore depths,
        KapPublicWalkBudget budget,
        ILogger? logger = null)
    {
        _reader = reader;
        _cursors = cursors;
        _depths = depths;
        _budget = budget;
        _logger = logger;
    }

    /// <summary>
    /// True when a listed row is one of the company's financial reports (see
    /// <see cref="FinancialReportCategory"/>).
    /// </summary>
    public static bool IsFinancialReport(KapPublicDisclosureRow row) =>
        string.Equals(row.DisclosureCategory?.Trim(), FinancialReportCategory, StringComparison.OrdinalIgnoreCase)
        && string.Equals(row.DisclosureType?.Trim(), FinancialReportCategory, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Walks one company. The persisted cursor (when any) decides where the walk starts; a
    /// bootstrap starts at the plan's <see cref="KapPublicStatementWalkPlan.From"/>.
    /// </summary>
    /// <param name="symbol">The bound BIST symbol (the facts' identity).</param>
    /// <param name="publicOid">The <b>stored</b> public-site oid (TKT-foundation-080) the list is queried with.</param>
    /// <param name="plan">The date range and window width.</param>
    /// <param name="sink">The job's write hook, called once per decoded report.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <exception cref="SourceFetchException">The disclosure list is source-down: the walk has no
    /// index to walk, so the company is failed rather than reported as having nothing.</exception>
    public async Task<KapPublicStatementWalkResult> WalkAsync(
        string symbol,
        PublicCompanyOid publicOid,
        KapPublicStatementWalkPlan plan,
        KapPublicStatementSink sink,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(sink);

        var stored = await _cursors.GetAsync(symbol, cancellationToken).ConfigureAwait(false);
        var bootstrapped = stored is null;
        var state = stored ?? KapPublicStatementCursor.Start(plan.From);

        var requestedFrom = bootstrapped ? plan.From : stored!.Watermark;
        var skips = new List<KapPublicStatementSkip>();
        var windowsListed = 0;
        var windowsWalked = 0;
        var honestEmpty = 0;
        var reportsListed = 0;
        var reportsFetched = 0;
        var inserted = 0;
        var unchanged = 0;
        var overlapRetained = 0;
        DateOnly? achievedFrom = null;
        DateOnly? achievedTo = null;
        var truncated = false;
        string? truncationReason = null;

        try
        {
            // The windows start at the watermark the store holds, not at the plan's bootstrap
            // start: a resumed pass must cover exactly what was not walked, and nothing else.
            var windows = (plan with { From = state.Watermark }).Windows();
            foreach (var window in windows)
            {
                if (truncated || state.Watermark > window.To)
                {
                    // Behind the watermark already covered, or the pass is bounded: stop where the
                    // last completed unit left off (nothing is skipped over).
                    break;
                }

                if (!_budget.TryBeginPage())
                {
                    truncated = true;
                    truncationReason = _budget.TruncationReason;
                    break;
                }

                var listed = await _reader
                    .ReadDisclosureListAsync(
                        new KapPublicListQuery(window.From, window.To, FinancialReportCategory, [publicOid.Value]),
                        cancellationToken)
                    .ConfigureAwait(false);
                windowsListed++;

                if (listed.IsSourceDown)
                {
                    // No index, no walk: the company is failed by the job (a genuine source-down),
                    // never recorded as a company with nothing to say.
                    throw new SourceFetchException(
                        $"KAP public-site disclosure list for '{symbol}' failed over {window.From:yyyy-MM-dd}..{window.To:yyyy-MM-dd} ({listed.ReasonCode}): {listed.Detail}");
                }

                if (listed.IsQuarantined)
                {
                    // The window's index is untrustworthy: record it and stop here rather than
                    // advance the watermark over rows nobody read.
                    skips.Add(new KapPublicStatementSkip(
                        symbol,
                        0,
                        listed.ReasonCode ?? KapPublicReasonCodes.UnparseablePayload,
                        listed.Detail,
                        listed.RawBody));
                    truncated = true;
                    truncationReason = listed.ReasonCode ?? KapPublicReasonCodes.UnparseablePayload;
                    break;
                }

                if (listed.IsHonestEmpty)
                {
                    honestEmpty++;
                    state = KapPublicStatementCursor.AfterWindow(window.To, state.Seen);
                    achievedFrom ??= window.From;
                    achievedTo = window.To;
                    windowsWalked++;
                    await _cursors.SetAsync(symbol, state, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var candidates = listed.Payload!
                    .Where(IsFinancialReport)
                    .GroupBy(row => row.DisclosureIndex)
                    .Select(group => group.First())
                    // Most current first: a bounded pass spends its detail budget on the reports a
                    // consumer needs now, and truncation loses the oldest, never the newest.
                    .OrderByDescending(PublishedOrMin)
                    .ThenByDescending(row => row.DisclosureIndex)
                    .Where(row => !state.Contains(row.DisclosureIndex))
                    .ToList();
                reportsListed += candidates.Count;

                var windowTruncated = false;

                foreach (var candidate in candidates)
                {
                    if (!_budget.TryBeginDetail())
                    {
                        windowTruncated = true;
                        truncated = true;
                        truncationReason = _budget.TruncationReason;
                        break;
                    }

                    var read = await _reader.ReadReportAsync(candidate.DisclosureIndex, cancellationToken)
                        .ConfigureAwait(false);
                    if (!read.IsOk)
                    {
                        // Per-detail isolation (the 063 doctrine applied to this source): the rest of
                        // the company's reports are still walked, and the failure travels with its
                        // evidence.
                        skips.Add(new KapPublicStatementSkip(
                            symbol,
                            candidate.DisclosureIndex,
                            read.ReasonCode ?? KapPublicReasonCodes.SourceFetchFailed,
                            read.Detail,
                            Evidence(symbol, candidate.DisclosureIndex, read)));
                        state = state.With(candidate.DisclosureIndex);
                        _budget.Complete(candidate.DisclosureIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        continue;
                    }

                    reportsFetched++;

                    KapPublicReport report;
                    try
                    {
                        report = KapPublicReportDecoder.Decode(read.Payload!.Html, symbol);
                    }
                    catch (SourcePayloadException exception)
                    {
                        skips.Add(new KapPublicStatementSkip(
                            symbol,
                            candidate.DisclosureIndex,
                            KapPublicReasonCodes.UnparseablePayload,
                            exception.Message,
                            exception.RawJson));
                        state = state.With(candidate.DisclosureIndex);
                        _budget.Complete(candidate.DisclosureIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        continue;
                    }

                    var write = await sink(report, candidate.DisclosureIndex, cancellationToken).ConfigureAwait(false);
                    inserted += write.Inserted;
                    unchanged += write.Unchanged;
                    overlapRetained += write.OverlapRetained;

                    state = state.With(candidate.DisclosureIndex);
                    _budget.Complete(candidate.DisclosureIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    await _cursors.SetAsync(symbol, state, cancellationToken).ConfigureAwait(false);
                }

                if (windowTruncated)
                {
                    // The cursor keeps this window's start (or the watermark it already had) plus
                    // every index completed inside it: the next pass resumes exactly at the first
                    // unfinished report.
                    await _cursors.SetAsync(symbol, state, cancellationToken).ConfigureAwait(false);
                    break;
                }

                state = KapPublicStatementCursor.AfterWindow(window.To, state.Seen);
                achievedFrom ??= window.From;
                achievedTo = window.To;
                windowsWalked++;
                await _cursors.SetAsync(symbol, state, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            // Progress is persisted even when a source failure unwinds the walk, so a failed pass
            // never costs the units it already completed.
            await _cursors.SetAsync(symbol, state, CancellationToken.None).ConfigureAwait(false);

            await _depths
                .RecordAsync(
                    symbol,
                    requestedFrom,
                    plan.To,
                    achievedFrom,
                    achievedTo,
                    bootstrapped,
                    truncated,
                    truncationReason,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        _logger?.LogInformation(
            "kap-public statements: {Symbol} walked {WindowsWalked}/{WindowsListed} window(s) {RequestedFrom}..{RequestedTo} — reports listed {ReportsListed}, fetched {ReportsFetched}, written {Inserted}, unchanged {Unchanged}, overlap-retained {OverlapRetained}, skipped {Skips}, truncated {Truncated} ({TruncationReason})",
            symbol,
            windowsWalked,
            windowsListed,
            requestedFrom,
            plan.To,
            reportsListed,
            reportsFetched,
            inserted,
            unchanged,
            overlapRetained,
            skips.Count,
            truncated,
            truncationReason);

        return new KapPublicStatementWalkResult(
            symbol,
            state,
            requestedFrom,
            plan.To,
            achievedFrom,
            achievedTo,
            bootstrapped,
            windowsListed,
            windowsWalked,
            honestEmpty,
            reportsListed,
            reportsFetched,
            inserted,
            unchanged,
            overlapRetained,
            skips,
            truncated,
            truncationReason);
    }

    /// <summary>
    /// The evidence a quarantine row keeps for a report that did not become facts: the source's
    /// own answer verbatim when the source answered at all, otherwise the failure's own record.
    /// Nothing is invented — a timeout has no body, so it says so.
    /// </summary>
    private static string Evidence(string symbol, long disclosureIndex, KapPublicFetchResult<KapPublicReportBody> read)
    {
        if (!string.IsNullOrEmpty(read.RawBody))
        {
            return read.RawBody;
        }

        using var buffer = new MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("symbol", symbol);
            writer.WriteNumber("disclosureIndex", disclosureIndex);
            writer.WriteString("reasonCode", read.ReasonCode);
            writer.WriteString("detail", read.Detail);
            writer.WriteBoolean("bodyReceived", false);
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// The row's publication stamp as a sortable date; an unparseable one sorts last (a row whose
    /// date the site did not serve is not allowed to jump the queue ahead of dated reports).
    /// </summary>
    private static DateOnly PublishedOrMin(KapPublicDisclosureRow row) =>
        DateOnly.TryParseExact(
            row.PublishDate,
            "dd.MM.yyyy HH:mm:ss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out var published)
            ? published
            : DateOnly.MinValue;
}
