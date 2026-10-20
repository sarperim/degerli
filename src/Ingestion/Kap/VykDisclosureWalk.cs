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

    private static bool CarriesCode(string? body, string code)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return string.Equals(VykFault.TryRead(document.RootElement)?.Code, code, StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

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

/// <summary>The outcome of one company's disclosure walk.</summary>
public sealed record VykCompanyWalk<TFact>(IReadOnlyList<TFact> Facts, string? NextCursor, string Raw);

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

    private readonly IKapSourceClient _client;
    private readonly KapRequestPacer? _pacer;
    private readonly ILogger? _logger;
    private readonly string _family;
    private readonly Func<string, string> _redact;

    public VykDisclosureWalker(
        IKapSourceClient client,
        KapRequestPacer? pacer,
        ILogger? logger,
        string family,
        Func<string, string>? redact = null)
    {
        _client = client;
        _pacer = pacer;
        _logger = logger;
        _family = family;
        _redact = redact ?? (message => message);
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
    /// longer serves. The walk logs it and re-walks once from
    /// <paramref name="earlyDisclosureIndex"/> (at most one reset per company per run), so one
    /// stale row cannot poison every future run.
    /// </para>
    /// <para>
    /// <b>Loud failures.</b> A non-empty cursor that does not parse fails the run instead of
    /// silently re-walking the whole archive (TKT-foundation-034 R7). Every other non-success —
    /// including 400s that are not <c>ER005</c>, 404, 401, 429 and fault envelopes — stays a
    /// <see cref="SourceFetchException"/>; a malformed list or detail body is a
    /// <see cref="SourcePayloadException"/>.
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
        CancellationToken cancellationToken)
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
        var processed = persisted ?? early;
        var from = processed;
        string? nextCursor = null;
        var resetAttempted = false;

        while (true)
        {
            string listRaw;
            try
            {
                listRaw = await GetVykAsync(BuildListPath(listPath, from, companyId), cancellationToken).ConfigureAwait(false);
            }
            catch (SourceFetchException exception) when (VykDisclosureList.IsEndOfList(exception))
            {
                if (!resetAttempted && persisted is not null && from == processed && nextCursor is null)
                {
                    // The cursor persisted by an earlier run is past a tail the gateway no longer
                    // serves (or a post-fetch store failure left it ahead of the facts). Re-walk
                    // once from the configured early index instead of failing forever.
                    resetAttempted = true;
                    _logger?.LogWarning(
                        "{Family}: persisted cursor {Cursor} for {Symbol} answered HTTP 400 ER005; resetting to EarlyDisclosureIndex {Early}",
                        _family,
                        cursor,
                        symbol,
                        early);
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
                if (index <= processed || !isFamily(disclosure))
                {
                    continue;
                }

                var detailRaw = await GetVykAsync(BuildDetailPath(detailPath, disclosure.DisclosureIndex!), cancellationToken)
                    .ConfigureAwait(false);
                raw.AppendLine(detailRaw);
                facts.AddRange(decode(detailRaw, symbol, disclosure));
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

        return new VykCompanyWalk<TFact>(facts, nextCursor, raw.ToString());
    }

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
