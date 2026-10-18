using System.Globalization;
using System.Text.Json;
using Degerli.Ingestion.Sources;
using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.Universe;

/// <summary>
/// Source wiring for the <c>universe-sync</c> job (config section
/// <c>Ingestion:Universe</c>). <see cref="Path"/> is the instrument/classification resource;
/// <see cref="IndexLevelsPath"/> is the BIST index-levels resource (FR-MDF-006).
/// <para>
/// <b>Real wiring (F4, NFR-MDF-006 — fulfilled here):</b> both resources are the real
/// İş Yatırım endpoints verified live 2026-10-18 (01 v1.5 §10.9.8), on the same host as the
/// prices source but reached through their own keyed <see cref="ISourceClient"/>
/// (<see cref="ClientKey"/>, base address <see cref="BaseUrl"/>). The universe resource is a
/// legacy ASP.NET AJAX <b>POST</b> handler (<see cref="Method"/>), so it does not ride the GET
/// seam; the index-levels resource is a plain GET with a <c>{from}</c>/<c>{to}</c>/<c>{index}</c>
/// templated query.
/// </para>
/// </summary>
public sealed class UniverseSourceOptions
{
    public const string SectionName = "Ingestion:Universe";

    /// <summary>Keyed source-client name for the universe source (no shared prices client).</summary>
    public const string ClientKey = "universe";

    /// <summary>Base address of the universe/index-levels source (İş Yatırım in production).</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// HTTP method of the universe resource. Defaults to <c>GET</c> so the canonical L2
    /// double route (the shape every existing universe test serves over GET, 01 v1.5 §10.9.7)
    /// keeps composing through this adapter unchanged; production opts into the real
    /// <c>POST</c> handler explicitly (<see cref="SectionName"/> sets <c>Method</c> — the
    /// resource is POST-only, §10.9.8).
    /// </summary>
    public string Method { get; set; } = "GET";

    /// <summary>Path of the universe resource relative to the source base address.</summary>
    public string Path { get; set; } =
        "/_layouts/15/IsYatirim.Website/StockInfo/CompanyInfoAjax.aspx/GetHissePerformans";

    /// <summary>
    /// JSON request body of the universe resource (used only when <see cref="Method"/> is
    /// POST). The default selects every listed national instrument (<c>endeksKodu 09</c> =
    /// Ulusal-Tüm, 01 v1.5 §10.9.8).
    /// </summary>
    public string RequestBody { get; set; } = """{"endeksKodu":"09","sektorKodu":"","exchange":""}""";

    /// <summary>
    /// Path of the index-levels resource relative to the source base address. Supports the
    /// <c>{from}</c>/<c>{to}</c> (<c>yyyyMMddHHmmss</c>) and <c>{index}</c> tokens
    /// (01 v1.5 §10.9.8).
    /// </summary>
    public string IndexLevelsPath { get; set; } =
        "/_Layouts/15/IsYatirim.Website/Common/ChartData.aspx/IndexHistoricalAll" +
        "?period=1440&from={from}&to={to}&endeks={index}";

    /// <summary>
    /// The indices whose levels the index-levels resource is queried for (FR-MDF-006 pins
    /// "including BIST 100 and BIST 30", FR-MOV-001 displays both; 01 v1.5 §10.9.8). The
    /// universe job's index-levels step fetches each configured index through the templated
    /// route and degrades per index. Production default <c>XU100, XU030</c>; the L2 harness
    /// binds its own subset either way. An empty list (unconfigured) falls back to
    /// <see cref="DefaultIndexLevelsIndices"/>.
    /// <para>
    /// The instance default is <b>empty</b>, not the production set: the options binder
    /// <i>appends</i> configured entries to any non-empty initial value (a
    /// <c>List&lt;string&gt;</c>/array with a seeded default would double the set), so a
    /// clean bind requires an empty seed and the fallback below carries the documented default.
    /// </para>
    /// </summary>
    public List<string> IndexLevelsIndices { get; set; } = [];

    /// <summary>The production default index set (FR-MDF-006), applied when the set is
    /// unconfigured.</summary>
    public static IReadOnlyList<string> DefaultIndexLevelsIndices { get; } = ["XU100", "XU030"];

    /// <summary>Bounded lookback (days) of the index-levels request window.</summary>
    public int IndexLevelsLookbackDays { get; set; } = 30;
}

/// <summary>
/// The universe adapter (FR-MDF-006/007): fetches the instrument/classification payload
/// through the universe-bound <see cref="ISourceClient"/> and parses it. Both first-class
/// shapes parse through this one adapter (01 v1.5 §10.9.8): the <b>real</b> İş Yatırım
/// <c>Oran</c> envelope (decoded by <see cref="UniverseRealEnvelopeDecoder"/>) and the
/// canonical <c>sourceRef</c>/<c>instruments</c> double (the L2 golden shape, §10.9.7) —
/// detection is by shape, never by config. Persistence and validation are the job's. Every
/// parsed real payload carries the concrete source name (<c>isyatirim</c>) so each stored row
/// records its provenance (FR-MDF-008).
/// </summary>
public sealed class UniverseSourceAdapter : ISourceAdapter<UniversePayload>
{
    /// <summary>Concrete source name recorded for the real İş Yatırım universe rows (FR-MDF-008).</summary>
    public const string IsYatirimSource = "isyatirim";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISourceClient _client;
    private readonly UniverseSourceOptions _options;

    public UniverseSourceAdapter(ISourceClient client, IOptions<UniverseSourceOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    /// <inheritdoc />
    public string SourceName => "kap";

    /// <inheritdoc />
    public async Task<SourcePayload<UniversePayload>> FetchAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var raw = await FetchRawAsync(_options.Path, cancellationToken).ConfigureAwait(false);

        using var document = ParseDocument(raw);
        if (UniverseRealEnvelopeDecoder.IsEnvelope(document))
        {
            // The real envelope carries no provenance field; the adapter stamps the concrete
            // İş Yatırım source (FR-MDF-008) and emits the canonical instruments + sectors.
            var payload = UniverseRealEnvelopeDecoder.Decode(document, IsYatirimSource);
            return new SourcePayload<UniversePayload>(IsYatirimSource, payload, raw);
        }

        UniversePayload? canonical;
        try
        {
            canonical = JsonSerializer.Deserialize<UniversePayload>(raw, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new SourcePayloadException(raw, exception);
        }

        if (canonical is null)
        {
            throw new SourceFetchException("Universe payload deserialized to null.");
        }

        return new SourcePayload<UniversePayload>(canonical.SourceRef, canonical, raw);
    }

    private Task<string> FetchRawAsync(string path, CancellationToken cancellationToken) =>
        string.Equals(_options.Method, "POST", StringComparison.OrdinalIgnoreCase)
            ? _client.PostJsonAsync(path, _options.RequestBody, cancellationToken)
            : _client.GetStringAsync(path, cancellationToken);

    private static JsonDocument ParseDocument(string raw)
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
/// The index-levels adapter (BIST index levels; FR-MDF-006): fetches the index-level payload
/// through the universe-bound <see cref="ISourceClient"/> and parses it. Both shapes parse
/// through this one adapter (01 v1.5 §10.9.8): the <b>real</b> İş Yatırım <c>data</c>
/// dated-close envelope (decoded by <see cref="UniverseRealEnvelopeDecoder"/>) and the
/// canonical <c>sourceRef</c>/<c>levels</c> double (the L2 golden shape, §10.9.7).
/// <para>
/// The resource is <b>per-index</b> — one request per <c>endeks</c> code. This adapter fetches
/// every index named by <see cref="UniverseSourceOptions.IndexLevelsIndices"/> (production
/// default <c>XU100, XU030</c>; FR-MDF-006 pins "including BIST 100 and BIST 30") through the
/// templated route and merges their levels into one payload, recording each failing index in
/// <see cref="IndexLevelsPayload.Failures"/> so the job can apply the §10.9.8 <b>per-index</b>
/// degradation posture (one index's failure quarantines + alerts + <c>Partial</c> without
/// dropping the other index's levels, and never aborts the instrument/membership sync). A
/// transport failure is recorded (the TC-MDF-070 class) rather than thrown; only the canonical
/// single-index path that carries no failures keeps the fetch-exception class job-side.
/// </para>
/// </summary>
public sealed class IndexLevelsSourceAdapter : ISourceAdapter<IndexLevelsPayload>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISourceClient _client;
    private readonly UniverseSourceOptions _options;

    public IndexLevelsSourceAdapter(ISourceClient client, IOptions<UniverseSourceOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    /// <inheritdoc />
    public string SourceName => "kap";

    /// <inheritdoc />
    public async Task<SourcePayload<IndexLevelsPayload>> FetchAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var configured = _options.IndexLevelsIndices is { Count: > 0 }
            ? _options.IndexLevelsIndices
            : UniverseSourceOptions.DefaultIndexLevelsIndices;

        // A path without an `{index}` token is a single fixed route (the canonical L2 double,
        // §10.9.7): it cannot be queried per-index, so fetch it once and let the payload's own
        // index codes stand. Only the real templated route loops the configured set.
        var indices = _options.IndexLevelsPath.Contains("{index}", StringComparison.Ordinal)
            ? configured
            : configured.Take(1).ToList();

        var levels = new List<IndexLevelFact>();
        var failures = new List<IndexLevelFetchFailure>();
        var raws = new List<string>();
        string? sourceRef = null;

        foreach (var indexCode in indices)
        {
            if (string.IsNullOrWhiteSpace(indexCode))
            {
                continue;
            }

            var path = ResolveIndexLevelsPath(_options.IndexLevelsPath, indexCode, date);

            string raw;
            try
            {
                raw = await _client.GetStringAsync(path, cancellationToken).ConfigureAwait(false);
            }
            catch (SourceFetchException)
            {
                // A transport failure is a source-down (the TC-MDF-070 class), not a parse
                // event: recorded per index so the job quarantines + alerts + degrades to
                // Partial without dropping the other indices' levels (§10.9.8).
                failures.Add(new IndexLevelFetchFailure(indexCode, UniverseSyncJob.SourceFetchFailed));
                continue;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(raw);
            }
            catch (JsonException)
            {
                failures.Add(new IndexLevelFetchFailure(indexCode, "UNPARSEABLE_PAYLOAD", raw));
                continue;
            }

            using (document)
            {
                if (UniverseRealEnvelopeDecoder.IsIndexLevelsEnvelope(document))
                {
                    var payload = UniverseRealEnvelopeDecoder.DecodeIndexLevels(
                        document,
                        UniverseSourceAdapter.IsYatirimSource,
                        indexCode);
                    sourceRef = UniverseSourceAdapter.IsYatirimSource;
                    levels.AddRange(payload.Levels ?? []);
                }
                else
                {
                    IndexLevelsPayload? canonical;
                    try
                    {
                        canonical = JsonSerializer.Deserialize<IndexLevelsPayload>(raw, JsonOptions);
                    }
                    catch (JsonException)
                    {
                        failures.Add(new IndexLevelFetchFailure(indexCode, "UNPARSEABLE_PAYLOAD", raw));
                        continue;
                    }

                    if (canonical is null)
                    {
                        failures.Add(new IndexLevelFetchFailure(indexCode, "UNPARSEABLE_PAYLOAD", raw));
                        continue;
                    }

                    sourceRef ??= canonical.SourceRef;
                    levels.AddRange(canonical.Levels ?? []);
                }
            }

            raws.Add(raw);
        }

        var merged = new IndexLevelsPayload(
            sourceRef,
            levels.Count > 0 ? levels : null,
            failures.Count > 0 ? failures : null);
        return new SourcePayload<IndexLevelsPayload>(
            sourceRef,
            merged,
            raws.Count > 0 ? string.Join('\n', raws) : string.Empty);
    }

    /// <summary>
    /// Substitutes the <c>{from}</c>/<c>{to}</c>/<c>{index}</c> tokens. The window ends at the
    /// requested date and spans <see cref="UniverseSourceOptions.IndexLevelsLookbackDays"/>
    /// back; dates are the source-locale <c>yyyyMMddHHmmss</c> form (01 v1.5 §10.9.8). A path
    /// without tokens is returned unchanged (the canonical L2 double route).
    /// </summary>
    private string ResolveIndexLevelsPath(string template, string indexCode, DateOnly date)
    {
        if (!template.Contains('{', StringComparison.Ordinal))
        {
            return template;
        }

        var from = date.AddDays(-_options.IndexLevelsLookbackDays).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var to = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return template
            .Replace("{from}", from + "000000", StringComparison.Ordinal)
            .Replace("{to}", to + "235959", StringComparison.Ordinal)
            .Replace("{index}", Uri.EscapeDataString(indexCode), StringComparison.Ordinal);
    }
}
