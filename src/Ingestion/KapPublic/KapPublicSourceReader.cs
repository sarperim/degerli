using System.Text.Json;
using Degerli.Ingestion.Sources;
using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.KapPublic;

/// <summary>
/// Reads the public site's payloads and classifies each answer into the lane's four honest
/// statuses (TC-MDF-103 (iv), 01 v1.5.53 §10.9.19):
/// <list type="bullet">
/// <item><b>ok</b> — a well-formed payload;</item>
/// <item><b>honest empty</b> — a well-formed but empty window (a named skip; no cursor moves
/// backwards);</item>
/// <item><b>quarantine</b> — a 200 body that fails the shape check, carried <b>verbatim</b>
/// (<c>UNPARSEABLE_PAYLOAD</c>);</item>
/// <item><b>source down</b> — transport/non-success/timeout, with nothing fabricated.</item>
/// </list>
/// <para>
/// Shape detection is by <b>structure</b>, never by the declared content type: the report
/// route declares <c>application/vnd.ms-excel</c> while serving HTML, so type-trusting would
/// misclassify every real report. The classifier never rewrites a body — the raw text is what
/// a quarantine stores.
/// </para>
/// </summary>
public sealed class KapPublicSourceReader
{
    /// <summary>The lane's provenance name (never the VYK lane's <c>kap</c>).</summary>
    public const string SourceName = KapPublicSourceOptions.SourceName;

    /// <summary>The maximum number of body characters carried on a quarantine/failure record.</summary>
    public const int MaxExcerptChars = 2048;

    private readonly IKapPublicSourceClient _client;
    private readonly KapPublicSourceOptions _options;

    public KapPublicSourceReader(IKapPublicSourceClient client, IOptions<KapPublicSourceOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    /// <summary>The lane's provenance name — recorded with every fact this source produces.</summary>
    public string SourceNameOfLane => _client.SourceName;

    /// <summary>
    /// Reads one byCriteria disclosure list for <paramref name="query"/> (POST). The
    /// <b>public</b> site's index space is what comes back — the caller must never feed one of
    /// those indices to the VYK gateway, nor a VYK index here.
    /// </summary>
    public async Task<KapPublicFetchResult<IReadOnlyList<KapPublicDisclosureRow>>> ReadDisclosureListAsync(
        KapPublicListQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        string body;
        try
        {
            body = await _client
                .PostJsonAsync(_options.ByCriteriaPath, query.ToJson(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SourceFetchException exception)
        {
            return KapPublicFetchResult<IReadOnlyList<KapPublicDisclosureRow>>.SourceDown(
                KapPublicReasonCodes.SourceFetchFailed,
                exception.Message,
                Excerpt(exception.Body));
        }
        catch (KapPublicQuotaException exception)
        {
            // The source's request quota answered 429 (TKT-foundation-100, 01 v1.5.77 §10.9.19):
            // a named outcome with its own reason, never a transport failure in disguise and never
            // a retry ladder.
            return KapPublicFetchResult<IReadOnlyList<KapPublicDisclosureRow>>.SourceDown(
                exception.ReasonCode,
                exception.Message);
        }
        catch (KapPublicSourceTimeoutException exception)
        {
            return KapPublicFetchResult<IReadOnlyList<KapPublicDisclosureRow>>.SourceDown(
                KapPublicReasonCodes.RequestTimeout,
                exception.Message);
        }

        return ClassifyDisclosureList(body);
    }

    /// <summary>
    /// Fetches one full FR report by its <b>public-site</b> disclosure index and validates it
    /// by shape (an HTML table document). The per-item detail timeout bounds the fetch.
    /// </summary>
    public async Task<KapPublicFetchResult<KapPublicReportBody>> ReadReportAsync(
        long disclosureIndex,
        CancellationToken cancellationToken = default)
    {
        if (disclosureIndex <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(disclosureIndex),
                "A public-site disclosure index is a positive number (its own index space, never a VYK index).");
        }

        var route = _options.ReportExportRoute(disclosureIndex);
        string body;
        try
        {
            body = await _client.GetDetailStringAsync(route, cancellationToken).ConfigureAwait(false);
        }
        catch (SourceFetchException exception)
        {
            return KapPublicFetchResult<KapPublicReportBody>.SourceDown(
                KapPublicReasonCodes.SourceFetchFailed,
                exception.Message,
                Excerpt(exception.Body));
        }
        catch (KapPublicQuotaException exception)
        {
            // The export route's quota (the 2026-10-28 live finding): a named outcome. A *deferred*
            // one (IsQuotaDeferred) is a truncation, not a skip — the walk stops at its last
            // completed unit instead of marking this report done.
            return KapPublicFetchResult<KapPublicReportBody>.SourceDown(
                exception.ReasonCode,
                exception.Message);
        }
        catch (KapPublicSourceTimeoutException exception)
        {
            return KapPublicFetchResult<KapPublicReportBody>.SourceDown(
                KapPublicReasonCodes.RequestTimeout,
                exception.Message);
        }

        // Shape, not content type: the route declares `application/vnd.ms-excel` and every real
        // answer is an HTML table document (captured 2026-10-25: 731 tables, 1,467 rows).
        if (!LooksLikeHtmlTableDocument(body))
        {
            return KapPublicFetchResult<KapPublicReportBody>.Quarantined(
                Excerpt(body)!,
                QuarantineDetail(body.Length,
                    "the body is not an HTML table document (the export route's shape) — " +
                    "classification is by shape, never by the declared content type"));
        }

        return KapPublicFetchResult<KapPublicReportBody>.Ok(
            new KapPublicReportBody(disclosureIndex, body),
            body);
    }

    /// <summary>
    /// Classifies a byCriteria body. A well-formed empty array is an honest empty; an array
    /// whose rows lack the members the lane depends on is a shape failure (quarantine), never
    /// a silently-partial list.
    /// </summary>
    public static KapPublicFetchResult<IReadOnlyList<KapPublicDisclosureRow>> ClassifyDisclosureList(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (string.IsNullOrWhiteSpace(body))
        {
            return KapPublicFetchResult<IReadOnlyList<KapPublicDisclosureRow>>.Quarantined(
                Excerpt(body)!,
                QuarantineDetail(body.Length, "the body is empty — not the byCriteria disclosure array"));
        }

        List<KapPublicDisclosureRow>? rows;
        try
        {
            rows = JsonSerializer.Deserialize<List<KapPublicDisclosureRow>>(body, KapPublicJson.Options);
        }
        catch (JsonException exception)
        {
            return KapPublicFetchResult<IReadOnlyList<KapPublicDisclosureRow>>.Quarantined(
                Excerpt(body)!,
                QuarantineDetail(body.Length,
                    $"the body is not the byCriteria disclosure array: {exception.Message}"));
        }

        if (rows is null)
        {
            return KapPublicFetchResult<IReadOnlyList<KapPublicDisclosureRow>>.Quarantined(
                Excerpt(body)!,
                QuarantineDetail(body.Length, "the byCriteria body deserialized to null"));
        }

        if (rows.Count == 0)
        {
            return KapPublicFetchResult<IReadOnlyList<KapPublicDisclosureRow>>.HonestEmpty(body);
        }

        var malformed = rows.Where(row =>
            row.DisclosureIndex <= 0 ||
            string.IsNullOrWhiteSpace(row.PublishDate) ||
            string.IsNullOrWhiteSpace(row.DisclosureClass)).ToList();
        if (malformed.Count > 0)
        {
            return KapPublicFetchResult<IReadOnlyList<KapPublicDisclosureRow>>.Quarantined(
                Excerpt(body)!,
                QuarantineDetail(body.Length,
                    $"{malformed.Count} of {rows.Count} rows lack the byCriteria members the lane depends on " +
                    "(disclosureIndex, publishDate, disclosureClass)"));
        }

        return KapPublicFetchResult<IReadOnlyList<KapPublicDisclosureRow>>.Ok(rows, body);
    }

    /// <summary>
    /// True when a body is plausibly an HTML document carrying at least one table — the report
    /// export's shape. Never a content-type test (the declared type is a lie on this route).
    /// </summary>
    public static bool LooksLikeHtmlTableDocument(string? body) =>
        !string.IsNullOrWhiteSpace(body)
        && body.Contains("<table", StringComparison.OrdinalIgnoreCase)
        && body.Contains("<html", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Bounds a body excerpt carried on a quarantine/failure record. The excerpt is what a
    /// quarantine stores (never the whole body — a 200 body can be ~1.8 MB), and
    /// <see cref="QuarantineDetail"/> records the original length so the truncation is visible.
    /// </summary>
    private static string? Excerpt(string? body)
    {
        if (body is null)
        {
            return null;
        }

        return body.Length <= MaxExcerptChars ? body : body[..MaxExcerptChars];
    }

    /// <summary>
    /// Appends the original body length (and the excerpt cap) to a quarantine detail, so a
    /// truncated record says so instead of looking like the whole answer.
    /// </summary>
    private static string QuarantineDetail(int originalLength, string detail) =>
        $"{detail} (raw body {originalLength} chars; excerpt cap {MaxExcerptChars})";
}
