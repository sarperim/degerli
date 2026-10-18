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

    /// <summary>The index whose levels the index-levels resource is queried for (default XU030).</summary>
    public string IndexLevelsIndex { get; set; } = "XU030";

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
        var path = ResolveIndexLevelsPath(_options.IndexLevelsPath, _options.IndexLevelsIndex, date);
        var raw = await _client.GetStringAsync(path, cancellationToken).ConfigureAwait(false);

        using var document = ParseDocument(raw);
        if (UniverseRealEnvelopeDecoder.IsIndexLevelsEnvelope(document))
        {
            var payload = UniverseRealEnvelopeDecoder.DecodeIndexLevels(
                document,
                UniverseSourceAdapter.IsYatirimSource,
                _options.IndexLevelsIndex);
            return new SourcePayload<IndexLevelsPayload>(UniverseSourceAdapter.IsYatirimSource, payload, raw);
        }

        IndexLevelsPayload? canonical;
        try
        {
            canonical = JsonSerializer.Deserialize<IndexLevelsPayload>(raw, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new SourcePayloadException(raw, exception);
        }

        if (canonical is null)
        {
            throw new SourceFetchException("Index-levels payload deserialized to null.");
        }

        return new SourcePayload<IndexLevelsPayload>(canonical.SourceRef, canonical, raw);
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
