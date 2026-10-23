using System.Globalization;
using System.Text;
using System.Text.Json;
using Degerli.Ingestion.Sources;
using Microsoft.Extensions.Logging;

namespace Degerli.Ingestion.Kap;

/// <summary>
/// The gateway's "no disclosure here" answer: HTTP 400 with a <c>{"code":"ER005", …}</c> body
/// (01 v1.5 §10.9.15, live-verified 2026-10-20). It is <b>not</b> a source failure — it is how the
/// VYK list route says "this company has no disclosure at or after the requested index".
/// </summary>
public static class VykDisclosureList
{
    /// <summary>The gateway code for "Bildirim bulunamadı" (disclosure not found).</summary>
    public const string EndOfListCode = "ER005";

    /// <summary>
    /// True when a failed list request is the gateway's end-of-list answer: HTTP <b>400</b> whose
    /// body carries the <c>ER005</c> code. Every other status/body (404, 401, 429, a <c>fault</c>
    /// envelope, another 400 code, transport) stays a source failure.
    /// </summary>
    public static bool IsEndOfList(SourceFetchException exception) =>
        exception.StatusCode == 400 && CarriesCode(exception.Body, EndOfListCode);

    /// <summary>
    /// Parses a <c>200</c> list body. A list body must be a JSON <b>array</b> of objects that each
    /// carry a numeric <c>disclosureIndex</c>; anything else (a bare object, a wrapper, a truncated
    /// body, a fault envelope) is an <see cref="SourcePayloadException"/> — never a fabricated empty
    /// success (TKT-foundation-034 R2). An empty array is a genuine empty list and yields no rows.
    /// </summary>
    public static IReadOnlyList<DisclosureInfo> Parse(string raw, Func<string, string>? redact = null)
    {
        using var document = ParseDocument(raw);
        var root = document.RootElement;

        var fault = VykFault.TryRead(root);
        if (fault is not null)
        {
            var message = $"VYK /disclosures returned a fault ({fault.Code}): {redact?.Invoke(fault.Message ?? string.Empty) ?? fault.Message}";
            throw new SourceFetchException(message);
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new SourcePayloadException(
                raw,
                new JsonException("VYK /disclosures response is not a JSON array of disclosure rows."));
        }

        var rows = new List<DisclosureInfo>();
        foreach (var element in root.EnumerateArray())
        {
            if (!HasNumericIndex(element))
            {
                throw new SourcePayloadException(
                    raw,
                    new JsonException("VYK /disclosures row carries no numeric disclosureIndex."));
            }

            rows.Add(DisclosureInfo.From(element));
        }

        return rows;
    }

    /// <summary>
    /// The gateway's own error code from a non-success body (the <c>code</c> of a
    /// <c>{"code":"ER005",…}</c> answer, or the <c>faultCode</c> of a fault envelope), or
    /// <c>null</c> when the body carries none. Used to record a stale-cursor reset verbatim
    /// (TKT-foundation-063).
    /// </summary>
    public static string? ReadCode(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return VykFault.TryRead(document.RootElement)?.Code;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool CarriesCode(string? body, string code) =>
        string.Equals(ReadCode(body), code, StringComparison.OrdinalIgnoreCase);

    private static bool HasNumericIndex(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("disclosureIndex", out var index))
        {
            return false;
        }

        return index.ValueKind switch
        {
            JsonValueKind.Number => index.TryGetInt64(out _),
            JsonValueKind.String => long.TryParse(index.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out _),
            _ => false,
        };
    }

    internal static JsonDocument ParseDocument(string raw)
    {
        try
        {
            return JsonDocument.Parse(raw);
        }
        catch (JsonException exception)
        {
            throw new SourcePayloadException(raw, exception);
        }
    }
}

/// <summary>
/// One disclosure detail the walk could <b>not</b> fetch (01 v1.5.40 §10.9.15,
/// TKT-foundation-061): the item was isolated, the walk continued, and this record is the
/// evidence — the disclosure's index, the named reason, the failure class and the gateway's own
/// error verbatim. It is never an empty row and never a silent omission: an unfetchable
/// attachment is quarantined with what the source actually said (§10.9.15's per-item honesty
/// pin), exactly as 046's per-constituent isolation names an unmappable symbol.
/// </summary>
public sealed record VykDetailSkip(
    string Symbol,
    string DisclosureIndex,
    string ReasonCode,
    string FailureKind,
    int? StatusCode,
    string Error,
    string? Body)
{
    /// <summary>
    /// The quarantine evidence document: the disclosure id, the named reason, the failure kind
    /// (an HTTP status, a fault envelope, a transport error or a timeout) and the gateway's error
    /// text/body verbatim. Written as the quarantine row's payload so the skip is inspectable
    /// and re-ingestable.
    /// </summary>
    public string ToEvidenceJson() => JsonSerializer.Serialize(new
    {
        symbol = Symbol,
        disclosureIndex = DisclosureIndex,
        reasonCode = ReasonCode,
        failureKind = FailureKind,
        statusCode = StatusCode,
        error = Error,
        body = Body,
    }, EvidenceJsonOptions);

    private static readonly JsonSerializerOptions EvidenceJsonOptions = new(JsonSerializerDefaults.Web);
}

/// <summary>
/// The per-disclosure half of the family-isolation doctrine (01 v1.5.40 §10.9.15): the details a
/// family adapter's last walk could not fetch, isolated item by item. The job folds them into the
/// run's per-symbol stats and the quarantine table, and the run terminates <c>partial</c> — the
/// per-item counterpart of <see cref="IVykIdentityIsolationReporter"/> (046).
/// </summary>
public interface IDisclosureIsolationReporter
{
    /// <summary>The disclosure details the last run isolated (empty when every detail answered).</summary>
    IReadOnlyList<VykDetailSkip> DetailSkips { get; }
}

/// <summary>
/// A disclosure detail that did not answer inside the configured per-item timeout (01 v1.5.40
/// §10.9.15). It is the <b>hang</b> class of 2026-10-22 (runs 185/190 — one attachment, 90
/// minutes): bounded here, at the item, so it becomes the same recorded skip as an HTTP error
/// instead of a run-level stall the 041 watchdog has to kill.
/// </summary>
public sealed class VykDetailTimeoutException : Exception
{
    public VykDetailTimeoutException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// The evidence of the <b>one</b> stale-cursor reset a company walk may perform (01 v1.5.42
/// §10.9.15, TKT-foundation-063): the persisted cursor answered HTTP 400 <c>ER005</c> — the
/// gateway saying "no disclosure at or after this index" — so the walk re-walked once from
/// <c>EarlyDisclosureIndex</c>. The record carries the symbol, the from/to indices and the
/// gateway's own code and body <b>verbatim</b> so the run can record the recovery as evidence
/// instead of hiding it (or failing the company forever). The reset's outcome is persisted by
/// the caller: the corrected cursor (last emitted index) or a cleared row when the re-walk was
/// itself honest-empty.
/// </summary>
public sealed record VykCursorReset(
    string Symbol,
    string FromCursor,
    string ToIndex,
    string Code,
    string? Body,
    string Error)
{
    /// <summary>The reset as the run's evidence document (structured log payload).</summary>
    public string ToEvidenceJson() => JsonSerializer.Serialize(new
    {
        symbol = Symbol,
        from = FromCursor,
        to = ToIndex,
        code = Code,
        body = Body,
        error = Error,
    }, ResetJsonOptions);

    private static readonly JsonSerializerOptions ResetJsonOptions = new(JsonSerializerDefaults.Web);
}

/// <summary>The outcome of one company's disclosure walk.</summary>
public sealed record VykCompanyWalk<TFact>(IReadOnlyList<TFact> Facts, string? NextCursor, string Raw)
{
    /// <summary>
    /// The disclosure details this walk isolated (01 v1.5.40 §10.9.15). Additive: a walk whose
    /// every detail answered carries none, so the pre-061 goldens are unchanged.
    /// </summary>
    public IReadOnlyList<VykDetailSkip> Skips { get; init; } = [];

    /// <summary>
    /// The one stale-cursor reset this walk performed (01 v1.5.42 §10.9.15), or <c>null</c>
    /// when no persisted cursor answered <c>ER005</c>. Additive: the pre-063 goldens carry none.
    /// </summary>
    public VykCursorReset? Reset { get; init; }
}

/// <summary>
/// The shared company-scoped disclosure walk of the three events adapters
/// (01 v1.5 §10.9.15): <c>GET {listPath}?disclosureIndex={from}&amp;companyId={id}</c> pages
/// ascending, each family-filtered new row's detail is fetched and decoded, and the company's
/// per-instrument cursor advances to the highest <b>own</b> listed index. Extracted so dividends,
/// corporate actions and disclosures cannot drift apart in their honest-empty / failure semantics
/// (TKT-foundation-034 R8).
/// </summary>
public sealed class VykDisclosureWalker
{
    /// <summary>The VYK disclosure-list page size (01 v1.5 §10.9.11).</summary>
    public const int PageSize = 50;

    /// <summary>
    /// The named reason of a per-disclosure skip (01 v1.5.40 §10.9.15): a disclosure detail the
    /// gateway could not serve (HTTP error, fault envelope, transport error) or that did not
    /// answer inside the per-item timeout. One reason code for the whole class, with the failure
    /// kind and the gateway's own error carried in the evidence — the skip is counted, never
    /// hidden, and never fails the run.
    /// </summary>
    public const string DisclosureDetailUnfetchable = "DISCLOSURE_DETAIL_UNFETCHABLE";

    private readonly IKapSourceClient _client;
    private readonly KapRequestPacer? _pacer;
    private readonly ILogger? _logger;
    private readonly string _family;
    private readonly Func<string, string> _redact;
    private readonly TimeSpan _detailFetchTimeout;

    public VykDisclosureWalker(
        IKapSourceClient client,
        KapRequestPacer? pacer,
        ILogger? logger,
        string family,
        Func<string, string>? redact = null,
        TimeSpan? detailFetchTimeout = null)
    {
        _client = client;
        _pacer = pacer;
        _logger = logger;
        _family = family;
        _redact = redact ?? (message => message);

        // The per-item bound (TKT-foundation-061). The shipped default is the shared
        // KapSourceOptions constant (three minutes vs a ~10–12 s healthy pace), so a caller that
        // wires no timeout still bounds the hang class.
        _detailFetchTimeout = detailFetchTimeout is { } configured && configured > TimeSpan.Zero
            ? configured
            : TimeSpan.FromMilliseconds(KapSourceOptions.DefaultDetailFetchTimeoutMs);
    }

    /// <summary>
    /// Walks one company from <paramref name="cursor"/> (inclusive) to the end of its list.
    /// <para>
    /// <b>Honest empty.</b> HTTP 400 <c>ER005</c> is the gateway's "no disclosure at/after this
    /// index" answer, so it ends the walk for that company — no exception, no rows, and the cursor
    /// stays at the last index actually emitted. A zero-disclosure company therefore ends cleanly.
    /// </para>
    /// <para>
    /// <b>Stale-cursor reset.</b> When the <b>first</b> request of a company — the one made from a
    /// persisted cursor — answers 400 <c>ER005</c>, the cursor points past a tail the gateway no
    /// longer serves. The walk records the gateway's answer verbatim on
    /// <see cref="VykCompanyWalk{TFact}.Reset"/> and re-walks once from
    /// <paramref name="earlyDisclosureIndex"/> (at most one reset per company per run), so one
    /// stale row cannot poison every future run.
    /// </para>
    /// <para>
    /// <b>Per-disclosure isolation (01 v1.5.40 §10.9.15).</b> A disclosure <b>detail</b> the
    /// gateway cannot serve — an HTTP error, a fault envelope, a transport failure — or that does
    /// not answer inside the configured per-item timeout is isolated: the walk records a
    /// <see cref="VykDetailSkip"/> with the gateway's error verbatim, continues with the next
    /// row, and the cursor still advances past the recorded index. The run therefore completes as
    /// an honest <c>partial</c> (the job counts and quarantines the skips) instead of failing the
    /// whole family — the per-item counterpart of 046's per-constituent isolation. A <b>list</b>
    /// failure is unchanged: it stays a source failure.
    /// </para>
    /// <para>
    /// <b>Loud failures.</b> A non-empty cursor that does not parse fails the run instead of
    /// silently re-walking the whole archive (TKT-foundation-034 R7). Every other <b>list</b>
    /// non-success — including 400s that are not <c>ER005</c>, 404, 401, 429 and fault envelopes —
    /// stays a <see cref="SourceFetchException"/> (a company whose <i>list</i> cannot be read is a
    /// source failure, unchanged by v1.5.40); a malformed list or detail body is a
    /// <see cref="SourcePayloadException"/>.
    /// </para>
    /// <para>
    /// <b>Replay at the persisted index.</b> <paramref name="replayPersistedIndex"/> is the
    /// statements lane's inclusive re-decode (TKT-foundation-063): the statements cursor names the
    /// last index whose facts were <b>persisted</b>, so re-reading that one row keeps a re-run
    /// idempotent (it reports <c>unchanged</c>) while its own cursor semantics stay unchanged. It
    /// applies only to the very first request made from the persisted cursor — never to later
    /// pages and never to the post-reset re-walk.
    /// </para>
    /// </summary>
    public async Task<VykCompanyWalk<TFact>> WalkAsync<TFact>(
        string symbol,
        string companyId,
        string? cursor,
        string earlyDisclosureIndex,
        string listPath,
        string detailPath,
        Func<DisclosureInfo, bool> isFamily,
        Func<string, string, DisclosureInfo, IEnumerable<TFact>> decode,
        CancellationToken cancellationToken,
        bool replayPersistedIndex = false)
    {
        var early = ParseIndex(earlyDisclosureIndex)
            ?? throw new SourceFetchException(
                $"Ingestion:{_family}:EarlyDisclosureIndex '{earlyDisclosureIndex}' is not a disclosure index.");

        long? persisted = null;
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            persisted = ParseIndex(cursor)
                ?? throw new SourceFetchException(
                    $"{_family}: the persisted cursor '{cursor}' for '{symbol}' is not a disclosure index; refusing to silently re-walk the archive.");
        }

        var facts = new List<TFact>();
        var raw = new StringBuilder();
        var skips = new List<VykDetailSkip>();
        var processed = persisted ?? early;
        var from = processed;
        string? nextCursor = null;
        var resetAttempted = false;
        VykCursorReset? reset = null;

        // The statements lane's inclusive re-decode applies to the first request made from the
        // persisted cursor only (see the method remarks). It is dropped as soon as that request
        // has been answered — including the reset path, whose re-walk starts at the early index.
        var replayPending = replayPersistedIndex && persisted is not null;

        while (true)
        {
            string listRaw;
            try
            {
                listRaw = await GetVykAsync(BuildListPath(listPath, from, companyId), cancellationToken).ConfigureAwait(false);
            }
            catch (SourceFetchException exception) when (VykDisclosureList.IsEndOfList(exception))
            {
                if (!resetAttempted && persisted is not null && nextCursor is null)
                {
                    // The cursor persisted by an earlier run is past a tail the gateway no longer
                    // serves (or a post-fetch store failure left it ahead of the facts). Re-walk
                    // once from the configured early index instead of failing forever, and
                    // record the gateway's own answer verbatim (01 v1.5.42 §10.9.15).
                    resetAttempted = true;
                    replayPending = false;
                    var code = VykDisclosureList.ReadCode(exception.Body) ?? VykDisclosureList.EndOfListCode;
                    var error = _redact(exception.Message);
                    reset = new VykCursorReset(
                        symbol,
                        cursor!,
                        early.ToString(CultureInfo.InvariantCulture),
                        code,
                        exception.Body,
                        error);
                    _logger?.LogWarning(
                        "{Family}: persisted cursor {From} for {Symbol} answered HTTP 400 {Code}; resetting once to EarlyDisclosureIndex {To} (gateway body: {Body})",
                        _family,
                        reset.FromCursor,
                        reset.Symbol,
                        reset.Code,
                        reset.ToIndex,
                        reset.Body);
                    processed = early;
                    from = early;
                    continue;
                }

                // End of this company's list: an honest empty, never a source failure.
                break;
            }

            raw.AppendLine(listRaw);
            var listed = VykDisclosureList.Parse(listRaw, _redact);
            if (listed.Count == 0)
            {
                break;
            }

            var replayThisPage = replayPending;
            replayPending = false;

            long maxIndex = -1;
            foreach (var disclosure in listed)
            {
                var index = ParseIndex(disclosure.DisclosureIndex);
                if (index is null)
                {
                    continue;
                }

                // The identity guard runs before the detail fetch: a disclosure naming another
                // company is skipped, never decoded under this company's identity (FR-MDF-008).
                if (!string.Equals(disclosure.CompanyId, companyId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                maxIndex = Math.Max(maxIndex, index.Value);
                if (index.Value < processed
                    || (index.Value == processed && !replayThisPage)
                    || !isFamily(disclosure))
                {
                    continue;
                }

                string detailRaw;
                try
                {
                    detailRaw = await GetDetailVykAsync(
                            BuildDetailPath(detailPath, disclosure.DisclosureIndex!),
                            cancellationToken)
                        .ConfigureAwait(false);

                    // Decode inside the same per-item try: every adapter's DecodeDetail throws the
                    // fault-envelope SourceFetchException lazily, and it must become the same
                    // recorded skip as an HTTP error. Materialize before appending so a detail that
                    // fails mid-decode contributes no facts and no raw evidence.
                    var decoded = decode(detailRaw, symbol, disclosure).ToList();
                    raw.AppendLine(detailRaw);
                    facts.AddRange(decoded);
                }
                catch (Exception exception) when (exception is SourceFetchException or HttpRequestException or VykDetailTimeoutException)
                {
                    // Per-disclosure quarantine-and-continue (01 v1.5.40 §10.9.15, the 046
                    // per-constituent doctrine one level down): one un-fetchable attachment (the
                    // 2026-10-22 HTTP 500 on /disclosureDetail/1185709, a 200 fault envelope on an
                    // auth/throttle failure, a transport error, or the 90-minute hang) is
                    // isolated with its evidence, counted and skipped — the walk continues, the
                    // cursor still advances past the recorded index, and the run ends an honest
                    // partial. Never a whole-run failure, never a silent omission. A malformed
                    // detail body stays a SourcePayloadException and still fails the run.
                    var skip = DescribeSkip(symbol, disclosure.DisclosureIndex!, exception);
                    skips.Add(skip);
                    _logger?.LogWarning(
                        "{Family}: disclosure detail {Index} for {Symbol} could not be fetched; isolated and the walk continues ({ReasonCode}, {FailureKind}, status {Status}): {Error}",
                        _family,
                        skip.DisclosureIndex,
                        skip.Symbol,
                        skip.ReasonCode,
                        skip.FailureKind,
                        skip.StatusCode,
                        skip.Error);
                    continue;
                }
            }

            var advanced = maxIndex > from;
            if (maxIndex > processed)
            {
                processed = maxIndex;
                nextCursor = maxIndex.ToString(CultureInfo.InvariantCulture);
            }

            // A short page ends the walk; so does a full page that does not advance the cursor (a
            // gateway that ignored the offset and re-served the same page must not loop us).
            if (listed.Count < PageSize || maxIndex < 0 || !advanced)
            {
                break;
            }

            from = maxIndex;
        }

        return new VykCompanyWalk<TFact>(facts, nextCursor, raw.ToString()) { Skips = skips, Reset = reset };
    }

    /// <summary>
    /// Fetches one disclosure detail under the configured per-item timeout (01 v1.5.40
    /// §10.9.15). Pacing happens <b>before</b> the clock starts, so the politeness delay the
    /// gateway's rate limit imposes is never counted as a hang; the timeout then bounds the
    /// gateway's own answer time. The fetch is raced against the bound as well as cancelled, so a
    /// client that ignores cancellation (a socket that accepted and stalled) still cannot stall
    /// the walk. A caller cancellation is never turned into a skip — it propagates.
    /// </summary>
    private async Task<string> GetDetailVykAsync(string path, CancellationToken cancellationToken)
    {
        if (_pacer is not null)
        {
            await _pacer.PaceAsync(cancellationToken).ConfigureAwait(false);
        }

        using var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bound.CancelAfter(_detailFetchTimeout);

        var fetch = _client.GetStringAsync(path, bound.Token);
        try
        {
            var completed = await Task.WhenAny(fetch, Task.Delay(_detailFetchTimeout, bound.Token)).ConfigureAwait(false);
            if (!ReferenceEquals(completed, fetch))
            {
                // The bound won the race: abandon the fetch (cancelled above) and report the hang.
                // A caller cancellation is not a hang — it propagates and is never quarantined.
                Observe(fetch);
                cancellationToken.ThrowIfCancellationRequested();
                throw new VykDetailTimeoutException(
                    $"{_family}: disclosure detail '{path}' did not answer within {_detailFetchTimeout.TotalSeconds:0.###}s; bounded per item and skipped.");
            }

            return await fetch.ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            Observe(fetch);
            throw new VykDetailTimeoutException(
                $"{_family}: disclosure detail '{path}' did not answer within {_detailFetchTimeout.TotalSeconds:0.###}s; bounded per item and skipped.",
                exception);
        }
    }

    /// <summary>Observes an abandoned fetch so its eventual cancellation/fault is not unobserved.</summary>
    private static void Observe(Task fetch) =>
        _ = fetch.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    /// <summary>
    /// Builds the evidence of one isolated detail (01 v1.5.40 §10.9.15): the named reason, the
    /// failure kind and the gateway's error verbatim — an HTTP status and body excerpt for a
    /// served response, <c>fault</c> for a 200 fault envelope, <c>transport</c> for a connection
    /// failure, the bound message for a timeout.
    /// </summary>
    private VykDetailSkip DescribeSkip(string symbol, string disclosureIndex, Exception exception) =>
        exception switch
        {
            SourceFetchException fetch => new VykDetailSkip(
                symbol,
                disclosureIndex,
                DisclosureDetailUnfetchable,
                // A status means the gateway served an HTTP error; no status means the 200 body
                // was a fault envelope (each adapter's DecodeDetail throws it without a status).
                fetch.StatusCode is not null ? "http" : "fault",
                fetch.StatusCode,
                _redact(fetch.Message),
                fetch.Body),
            VykDetailTimeoutException timeout => new VykDetailSkip(
                symbol,
                disclosureIndex,
                DisclosureDetailUnfetchable,
                "timeout",
                null,
                _redact(timeout.Message),
                null),
            HttpRequestException transport => new VykDetailSkip(
                symbol,
                disclosureIndex,
                DisclosureDetailUnfetchable,
                "transport",
                null,
                _redact(transport.Message),
                null),
            _ => new VykDetailSkip(
                symbol,
                disclosureIndex,
                DisclosureDetailUnfetchable,
                exception.GetType().Name,
                null,
                _redact(exception.Message),
                null),
        };

    private async Task<string> GetVykAsync(string path, CancellationToken cancellationToken)
    {
        if (_pacer is not null)
        {
            await _pacer.PaceAsync(cancellationToken).ConfigureAwait(false);
        }

        return await _client.GetStringAsync(path, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildListPath(string listPath, long from, string companyId)
    {
        var path = AppendQuery(listPath, "disclosureIndex", from.ToString(CultureInfo.InvariantCulture));
        return AppendQuery(path, "companyId", companyId);
    }

    private static string BuildDetailPath(string detailPath, string disclosureIndex)
    {
        var path = detailPath.Replace("{disclosureIndex}", Uri.EscapeDataString(disclosureIndex), StringComparison.Ordinal);
        return AppendQuery(path, "fileType", "data");
    }

    private static long? ParseIndex(string? value) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ? index : null;

    private static string AppendQuery(string path, string key, string value)
    {
        var separator = path.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{path}{separator}{key}={Uri.EscapeDataString(value)}";
    }
}
