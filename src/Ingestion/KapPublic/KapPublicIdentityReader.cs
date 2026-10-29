using System.Text;
using System.Text.Json;
using Degerli.Ingestion.Sources;
using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.KapPublic;

/// <summary>
/// One <c>companyOrFunds</c> hit of the public site's combined search (live-verified shape,
/// captured 2026-10-25 as <c>kap-public-identity-ASELS</c>): the search value (the company's
/// title), the hit type, the opaque <c>memberOrFundOid</c> and the lowercased company code.
/// <para>
/// The members are the endpoint's own fields, kept as served: <c>memberOrFundOid</c> stays a raw
/// string here — the space-checking parse into a <see cref="PublicCompanyOid"/> happens in
/// <see cref="KapPublicCompanyIdentityResolver"/>, so a non-oid value is visible as unresolved
/// rather than coerced.
/// </para>
/// </summary>
/// <param name="SearchValue">The hit's display value (the company title).</param>
/// <param name="SearchType">The hit type (<c>C</c> = company); a non-company hit never binds a stock.</param>
/// <param name="MemberOrFundOid">The public-site company/fund oid, as served.</param>
/// <param name="CmpOrFundCode">The lowercased company/fund code (<c>asels</c>).</param>
public sealed record KapPublicCompanyHit(
    string? SearchValue,
    string? SearchType,
    string? MemberOrFundOid,
    string? CmpOrFundCode);

/// <summary>The well-formed answer of one identity search: its <c>companyOrFunds</c> hits.</summary>
/// <param name="Hits">The hits, in the order the site served them.</param>
public sealed record KapPublicCompanyHitList(IReadOnlyList<KapPublicCompanyHit> Hits);

/// <summary>
/// Reads the public site's <b>identity</b> answer — <c>POST /tr/api/search/combined</c> with
/// <c>{"keyword":"&lt;ticker&gt;"}</c> — and classifies it into the lane's honest statuses
/// (01 v1.5.54 §10.9.19, TKT-foundation-080, consuming 079's client and pacing).
/// <para>
/// The answer is an array of category blocks (<c>combined</c>, <c>subjects</c>,
/// <c>companyOrFunds</c>); only <c>companyOrFunds</c> is the identity space this lane consumes.
/// A body that is not that shape is quarantined with its raw excerpt (<c>UNPARSEABLE_PAYLOAD</c>),
/// a transport failure is source-down (<c>SOURCE_FETCH_FAILED</c> /
/// <c>SOURCE_REQUEST_TIMEOUT</c>), and a well-formed answer with no company hits is simply empty —
/// the caller names the symbol as unresolved. Nothing is guessed from a partial read.
/// </para>
/// </summary>
public sealed class KapPublicIdentityReader
{
    /// <summary>The category block that carries company identity hits.</summary>
    public const string CompanyCategory = "companyOrFunds";

    /// <summary>The hit type that denotes a company (a fund hit never binds a stock instrument).</summary>
    public const string CompanySearchType = "C";

    private readonly IKapPublicSourceClient _client;
    private readonly KapPublicSourceOptions _options;

    public KapPublicIdentityReader(IKapPublicSourceClient client, IOptions<KapPublicSourceOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    /// <summary>The lane's provenance name (never the VYK lane's <c>kap</c>).</summary>
    public string SourceNameOfLane => _client.SourceName;

    /// <summary>The identity-search route the reader consumes.</summary>
    public string IdentityPath => _options.IdentitySearchPath;

    /// <summary>
    /// The exact request body the identity route is queried with — the live-verified
    /// <c>{"keyword":"&lt;ticker&gt;"}</c> shape.
    /// </summary>
    public static string RequestJson(string keyword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("keyword", keyword);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Reads one identity search for <paramref name="keyword"/> (the bound symbol). The keyword is
    /// the lane's own instrument symbol; the oid that comes back is the <b>public site's</b> space.
    /// </summary>
    public async Task<KapPublicFetchResult<KapPublicCompanyHitList>> ReadCompanyHitsAsync(
        string keyword,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);

        string body;
        try
        {
            body = await _client
                .PostJsonAsync(_options.IdentitySearchPath, RequestJson(keyword), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SourceFetchException exception)
        {
            return KapPublicFetchResult<KapPublicCompanyHitList>.SourceDown(
                KapPublicReasonCodes.SourceFetchFailed,
                exception.Message,
                Excerpt(exception.Body));
        }
        catch (KapPublicQuotaException exception)
        {
            // The source's request quota answered 429 (TKT-foundation-100): the pass ends honestly
            // under the quota's own named reason — nothing stored, nothing guessed.
            return KapPublicFetchResult<KapPublicCompanyHitList>.SourceDown(
                exception.ReasonCode,
                exception.Message);
        }
        catch (KapPublicSourceTimeoutException exception)
        {
            return KapPublicFetchResult<KapPublicCompanyHitList>.SourceDown(
                KapPublicReasonCodes.RequestTimeout,
                exception.Message);
        }

        return ClassifyCompanyHits(body);
    }

    /// <summary>
    /// Classifies an identity-search body: the array-of-category-blocks shape with a
    /// <c>companyOrFunds</c> block. A JSON body of another shape (an envelope, an object, a
    /// non-array) is a shape failure — quarantined, never read as "no hits".
    /// </summary>
    public static KapPublicFetchResult<KapPublicCompanyHitList> ClassifyCompanyHits(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (string.IsNullOrWhiteSpace(body))
        {
            return KapPublicFetchResult<KapPublicCompanyHitList>.Quarantined(
                Excerpt(body)!,
                "the body is empty — not the identity-search category array");
        }

        var hits = new List<KapPublicCompanyHit>();
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
            {
                return KapPublicFetchResult<KapPublicCompanyHitList>.Quarantined(
                    Excerpt(body)!,
                    $"the body is a JSON {root.ValueKind}, not the identity-search category array");
            }

            foreach (var category in root.EnumerateArray())
            {
                if (category.ValueKind != JsonValueKind.Object)
                {
                    return KapPublicFetchResult<KapPublicCompanyHitList>.Quarantined(
                        Excerpt(body)!,
                        $"a category block is a JSON {category.ValueKind}, not an object");
                }

                if (!category.TryGetProperty("category", out var name))
                {
                    continue;
                }

                // A category name is a JSON string in the served shape. A number/object/array name
                // is not the identity shape at all (GetString() would throw for it), so the whole
                // body is quarantined with its excerpt — the same honest treatment as any other
                // non-identity body, never a silent "no hits" and never an escaping exception.
                if (name.ValueKind != JsonValueKind.String)
                {
                    return KapPublicFetchResult<KapPublicCompanyHitList>.Quarantined(
                        Excerpt(body)!,
                        $"a category block's 'category' is a JSON {name.ValueKind}, not the identity-search category name");
                }

                if (!string.Equals(name.GetString(), CompanyCategory, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!category.TryGetProperty("results", out var results)
                    || results.ValueKind != JsonValueKind.Array)
                {
                    return KapPublicFetchResult<KapPublicCompanyHitList>.Quarantined(
                        Excerpt(body)!,
                        "the companyOrFunds block carries no results array");
                }

                foreach (var result in results.EnumerateArray())
                {
                    hits.Add(new KapPublicCompanyHit(
                        Text(result, "searchValue"),
                        Text(result, "searchType"),
                        Text(result, "memberOrFundOid"),
                        Text(result, "cmpOrFundCode")));
                }
            }
        }
        catch (JsonException exception)
        {
            return KapPublicFetchResult<KapPublicCompanyHitList>.Quarantined(
                Excerpt(body)!,
                $"the body is not the identity-search category array: {exception.Message}");
        }

        return KapPublicFetchResult<KapPublicCompanyHitList>.Ok(new KapPublicCompanyHitList(hits), body);
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? Excerpt(string? body) =>
        body is null || body.Length <= KapPublicSourceReader.MaxExcerptChars
            ? body
            : body[..KapPublicSourceReader.MaxExcerptChars];
}
