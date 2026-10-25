using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Degerli.Ingestion.KapPublic;

/// <summary>
/// The named reason codes of the public-site lane (the 044/063 vocabulary applied to this
/// source). Every status the lane can return has exactly one of these reasons — an
/// unexplained status is a contract violation (NFR-MDF-001: no silent ledger hole).
/// </summary>
public static class KapPublicReasonCodes
{
    /// <summary>The window answered a well-formed, <b>empty</b> list — an honest empty, never an error.</summary>
    public const string HonestEmpty = "HONEST_EMPTY_WINDOW";

    /// <summary>A 200 body that is not the expected shape (quarantine; the body travels verbatim).</summary>
    public const string UnparseablePayload = "UNPARSEABLE_PAYLOAD";

    /// <summary>A transport/non-success response — the source itself failed; nothing is fabricated.</summary>
    public const string SourceFetchFailed = "SOURCE_FETCH_FAILED";

    /// <summary>A per-item request exceeded its timeout (a source-down class, its own reason).</summary>
    public const string RequestTimeout = "SOURCE_REQUEST_TIMEOUT";

    /// <summary>The recorded access posture forbids automated reads — the client refuses.</summary>
    public const string AutomatedAccessForbidden = "AUTOMATED_ACCESS_FORBIDDEN";

    /// <summary>No access posture has been recorded (the code default) — the client refuses.</summary>
    public const string AccessPostureNotVerified = "ACCESS_POSTURE_NOT_VERIFIED";

    /// <summary>A walk stopped at its page ceiling (bounded pass; resumes from the last completed unit).</summary>
    public const string PageCap = "WALK_PAGE_CAP";

    /// <summary>A walk stopped at its detail ceiling (bounded pass; resumes from the last completed unit).</summary>
    public const string DetailCap = "WALK_DETAIL_CAP";

    /// <summary>A walk stopped at its wall-clock deadline (bounded pass; resumes from the last completed unit).</summary>
    public const string WalkDeadline = "WALK_DEADLINE";
}

/// <summary>
/// The four honest statuses of one public-site read (TC-MDF-103 (iv)). They are a closed
/// set: <see cref="Ok"/>, <see cref="HonestEmpty"/>, <see cref="Quarantined"/> and
/// <see cref="SourceDown"/> — and each carries a reason code.
/// </summary>
public enum KapPublicFetchOutcome
{
    /// <summary>A well-formed payload was read.</summary>
    Ok,

    /// <summary>A well-formed, empty answer — an honest empty (skip with its named reason).</summary>
    HonestEmpty,

    /// <summary>The body did not have the expected shape — quarantine it verbatim.</summary>
    Quarantined,

    /// <summary>The source failed (transport/non-success/timeout) — nothing is fabricated.</summary>
    SourceDown,
}

/// <summary>
/// One public-site read's outcome: the status, the decoded payload when <see cref="Ok"/>,
/// the raw body verbatim (quarantine material; never re-serialized) and the named reason
/// (<see cref="KapPublicReasonCodes"/>). A non-<see cref="KapPublicFetchOutcome.Ok"/> result
/// always carries a reason; <see cref="Ok"/> always carries a payload.
/// </summary>
/// <typeparam name="T">The decoded payload type (a class, so absence is expressible as null).</typeparam>
public sealed record KapPublicFetchResult<T>(
    KapPublicFetchOutcome Outcome,
    T? Payload,
    string? RawBody,
    string? ReasonCode,
    string? Detail = null)
    where T : class
{
    /// <summary>True for a well-formed payload.</summary>
    public bool IsOk => Outcome == KapPublicFetchOutcome.Ok;

    /// <summary>True when the read should be recorded as a quarantine with the raw body.</summary>
    public bool IsQuarantined => Outcome == KapPublicFetchOutcome.Quarantined;

    /// <summary>True when the read is an honest empty (a named skip, never a failure).</summary>
    public bool IsHonestEmpty => Outcome == KapPublicFetchOutcome.HonestEmpty;

    /// <summary>True when the source failed (the run is <c>failed</c>; no rows, nothing fabricated).</summary>
    public bool IsSourceDown => Outcome == KapPublicFetchOutcome.SourceDown;

    /// <summary>A well-formed payload.</summary>
    public static KapPublicFetchResult<T> Ok(T payload, string rawBody) =>
        new(KapPublicFetchOutcome.Ok, payload, rawBody, null);

    /// <summary>A well-formed, empty answer.</summary>
    public static KapPublicFetchResult<T> HonestEmpty(string rawBody) =>
        new(KapPublicFetchOutcome.HonestEmpty, null, rawBody, KapPublicReasonCodes.HonestEmpty,
            "the window answered a well-formed, empty list");

    /// <summary>A shape failure — the raw body is the quarantine payload.</summary>
    public static KapPublicFetchResult<T> Quarantined(string rawBody, string detail) =>
        new(KapPublicFetchOutcome.Quarantined, null, rawBody, KapPublicReasonCodes.UnparseablePayload, detail);

    /// <summary>A source failure (transport, non-success, timeout).</summary>
    public static KapPublicFetchResult<T> SourceDown(string reasonCode, string detail, string? rawBody = null) =>
        new(KapPublicFetchOutcome.SourceDown, null, rawBody, reasonCode, detail);
}

/// <summary>
/// One byCriteria disclosure-list query, in the live-verified body shape (2026-10-25):
/// a date window plus the class/member filters, <b>never</b> an index cursor — the public
/// list is windowed by date (the 082/084 cursor is a date watermark plus a seen-index set
/// in the source's own space). An <b>empty</b> <see cref="MkkMemberOidList"/> is the
/// market-wide query (the OQ-1 probe: it answers the market's disclosures).
/// </summary>
/// <param name="FromDate">Window start (inclusive, <c>yyyy-MM-dd</c>).</param>
/// <param name="ToDate">Window end (inclusive, <c>yyyy-MM-dd</c>).</param>
/// <param name="DisclosureClass">The disclosure class filter; empty = every class.</param>
/// <param name="MkkMemberOidList">The public-site company oids; empty = market-wide.</param>
/// <param name="SubjectList">The subject filter (e.g. the "Finansal Rapor" subject oid); empty = every subject.</param>
public sealed record KapPublicListQuery(
    DateOnly FromDate,
    DateOnly ToDate,
    string DisclosureClass = "",
    IReadOnlyList<string>? MkkMemberOidList = null,
    IReadOnlyList<string>? SubjectList = null)
{
    /// <summary>
    /// Serializes the request body in the endpoint's live-verified member order and
    /// spelling (camelCase; the empty arrays present, not omitted) — the request shape the
    /// capture was taken with, so a served double and the live site answer the same query.
    /// </summary>
    public string ToJson()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("fromDate", FromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            writer.WriteString("toDate", ToDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            writer.WriteString("disclosureClass", DisclosureClass ?? string.Empty);
            WriteArray(writer, "subjectList", SubjectList);
            WriteArray(writer, "mkkMemberOidList", MkkMemberOidList);
            WriteArray(writer, "inactiveMkkMemberOidList", null);
            WriteArray(writer, "bdkMemberOidList", null);
            writer.WriteBoolean("fromSrc", false);
            WriteArray(writer, "disclosureIndexList", null);
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteArray(Utf8JsonWriter writer, string name, IReadOnlyList<string>? values)
    {
        writer.WriteStartArray(name);
        if (values is not null)
        {
            foreach (var value in values)
            {
                writer.WriteStringValue(value);
            }
        }

        writer.WriteEndArray();
    }
}

/// <summary>
/// One rich byCriteria disclosure row, in the shape captured live on 2026-10-25. Every
/// member is the endpoint's own field — nothing is invented, and a member the capture shows
/// as nullable stays nullable (the market probe's rows are not all populated).
/// <para>
/// <see cref="DisclosureIndex"/> is the <b>public site's own index space</b> (ASELS 2026-06
/// FR = 1643141) — never a VYK gateway index (head 1231017), and never to be written into a
/// VYK cursor.
/// </para>
/// </summary>
public sealed record KapPublicDisclosureRow
{
    /// <summary>The public site's disclosure index (its own space).</summary>
    public long DisclosureIndex { get; init; }

    /// <summary>The disclosure class (<c>FR</c>, <c>ODA</c>, <c>DKB</c>, <c>DG</c> …).</summary>
    public string? DisclosureClass { get; init; }

    /// <summary>The disclosure type.</summary>
    public string? DisclosureType { get; init; }

    /// <summary>The publication stamp, as the site serves it (<c>dd.MM.yyyy HH:mm:ss</c>, Turkish local).</summary>
    public string? PublishDate { get; init; }

    /// <summary>The disclosure category (nullable in the live market feed).</summary>
    public string? DisclosureCategory { get; init; }

    /// <summary>The company/fund title.</summary>
    public string? KapTitle { get; init; }

    /// <summary>The fiscal year the disclosure belongs to (nullable).</summary>
    public int? Year { get; init; }

    /// <summary>The period/rule label (<c>Yıllık</c>, <c>6 Aylık</c> …).</summary>
    public string? RuleType { get; init; }

    /// <summary>The numeric period (nullable).</summary>
    public int? Period { get; init; }

    /// <summary>The subject line.</summary>
    public string? Subject { get; init; }

    /// <summary>The summary line (nullable).</summary>
    public string? Summary { get; init; }

    /// <summary>
    /// The related-stock member, kept as its raw JSON element: the capture shows it
    /// <c>null</c>, and whatever shape the site serves next must not be guessed into a
    /// scalar — an unreckoned shape is visible here rather than silently coerced.
    /// </summary>
    public JsonElement? RelatedStocks { get; init; }

    /// <summary>The stock codes the row relates to (nullable; <c>KTEST</c> is the test entity).</summary>
    public string? StockCodes { get; init; }

    /// <summary>Whether the filing was late.</summary>
    public bool IsLate { get; init; }

    /// <summary>Whether the row belongs to the old KAP system.</summary>
    public bool IsOldKap { get; init; }

    /// <summary>Whether the disclosure carries multi-language support.</summary>
    public bool HasMultiLanguageSupport { get; init; }

    /// <summary>The attachment count.</summary>
    public int AttachmentCount { get; init; }

    /// <summary>The modification status (nullable).</summary>
    public string? ModifyStatus { get; init; }

    /// <summary>The fund code for fund disclosures (nullable).</summary>
    public string? FundCode { get; init; }
}

/// <summary>
/// One fetched full-report body: the disclosure index it belongs to and the <b>HTML</b> the
/// export route answered (the route declares <c>application/vnd.ms-excel</c>; the body is
/// HTML — detected by shape, never by the declared type). The HTML is the decoder's input
/// (TKT-foundation-081) and is kept verbatim.
/// </summary>
/// <param name="DisclosureIndex">The public-site disclosure index requested.</param>
/// <param name="Html">The report body, verbatim.</param>
public sealed record KapPublicReportBody(long DisclosureIndex, string Html);

/// <summary>The JSON options the public site's bodies are read with (camelCase members).</summary>
internal static class KapPublicJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        // The live rows carry a few members the model does not need yet; an unknown member is
        // not a shape failure (the site may add one without warning) — the shape contract is
        // enforced on the members the lane depends on (see <c>KapPublicSourceReader</c>).
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };
}
