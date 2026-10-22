using System.Globalization;
using System.Text.Json;
using Degerli.Ingestion.Prices;
using Degerli.Ingestion.Sources;
using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.Intraday;

/// <summary>
/// Source wiring for the intraday lane (config section <c>Ingestion:Intraday</c>,
/// TKT-foundation-047, architecture §10.9.18). The section mirrors the
/// <c>Ingestion:Prices</c> pattern on purpose: the lane is <b>probe-gated</b>, so choosing
/// between İş Yatırım (primary) and the documented Yahoo Finance <c>.IS</c> contingency is a
/// configuration repoint of <see cref="BaseUrl"/>/<see cref="Path"/> — never a code fork
/// (01 v1.5 §10.9.1). <see cref="BaseUrl"/> points at İş Yatırım in production and at the
/// WireMock source double in L2 tests.
/// </summary>
public sealed class IntradayQuoteSourceOptions
{
    public const string SectionName = "Ingestion:Intraday";

    /// <summary>Key of the intraday lane's own source client (it reads its own base address).</summary>
    public const string ClientKey = "intraday";

    /// <summary>
    /// Base address of the intraday source. Empty falls back to <c>Ingestion:Prices:BaseUrl</c>
    /// (the same host serves both endpoints today), then to <c>http://localhost</c>.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Path of the per-symbol intraday resource, relative to <see cref="BaseUrl"/>. Supports
    /// the same tokens as the prices lane: <c>{symbol}</c> and <c>{date}</c> (source-locale
    /// <c>dd-MM-yyyy</c>). The shipped value is the EOD-shaped İş Yatırım <c>HisseTekil</c>
    /// resource; the probe outcome decides whether it serves a same-day row mid-session, and
    /// a repoint at Yahoo (e.g. <c>/v8/finance/chart/{symbol}.IS?range=1d&amp;interval=5m</c>)
    /// needs no code change.
    /// </summary>
    public string Path { get; set; } = "/intraday/{symbol}";

    /// <summary>
    /// The index whose current constituents (<c>index_constituents</c>,
    /// <c>effective_to IS NULL</c>) the sweep loops over. Empty follows
    /// <c>Ingestion:Prices:UniverseIndex</c> — the intraday lane refreshes exactly the
    /// universe the EOD lane stores (no second bound to keep in sync).
    /// </summary>
    public string UniverseIndex { get; set; } = string.Empty;

    /// <summary>
    /// Inter-request delay (milliseconds) pacing the strictly sequential sweep — the same
    /// politeness posture as the EOD lane (01 §10.9.2): a ~100-constituent sweep costs
    /// ~1–2 minutes, four sweeps a day ≈ 400 calls/day. <c>0</c> disables the wait (L2
    /// harnesses bind it low for deterministic runs).
    /// </summary>
    public int RequestDelayMs { get; set; } = 500;
}

/// <summary>
/// The intraday source adapter (TKT-foundation-047, FR-MDF-001's intraday mode): fetches one
/// symbol's payload for a session and narrows it to that session's current reading, behind the
/// <b>same envelope detection</b> the EOD prices adapter uses (01 v1.5 §10.9.1) — the real İş
/// Yatırım <c>HGDG_*</c> body (daily-bar shaped: its session row's close is the last price the
/// source publishes) and Yahoo's <c>chart</c> contingency (whose last same-day observation is
/// the delayed quote). The envelope, not configuration, decides which parser runs; the
/// concrete source is stamped on every reading (<c>isyatirim</c> / <c>yahoo</c>) so storage
/// always names the source that produced the value (FR-MDF-008).
/// </summary>
public sealed class IntradayQuoteSourceAdapter
{
    /// <summary>Concrete source name of the primary source (03 §1.2).</summary>
    public const string IsYatirimSource = PricesSourceAdapter.IsYatirimSource;

    /// <summary>Concrete source name of the documented Yahoo <c>.IS</c> contingency.</summary>
    public const string YahooSource = PricesSourceAdapter.YahooSource;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISourceClient _client;
    private readonly IntradayQuoteSourceOptions _options;

    public IntradayQuoteSourceAdapter(ISourceClient client, IOptions<IntradayQuoteSourceOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    /// <summary>
    /// Fetches one symbol's intraday reading for <paramref name="sessionDate"/>. A payload with
    /// no row for that session is an <b>empty item list</b>, never an error: that is the
    /// probe's honest "the source has nothing current" outcome and the sweep counts it
    /// (architecture §10.9.18, the 043 no-data doctrine).
    /// </summary>
    public async Task<SourcePayload<IntradayQuotePayload>> FetchSymbolAsync(
        string symbol,
        DateOnly sessionDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        var raw = await _client
            .GetStringAsync(ResolveFetchPath(_options.Path, symbol, sessionDate), cancellationToken)
            .ConfigureAwait(false);
        using var document = ParseDocument(raw);

        if (IsYahooEnvelope(document))
        {
            // Yahoo's chart is range-shaped and carries no provenance field: the adapter
            // stamps the concrete source and keeps only the requested day's last observation
            // (the delayed quote). Volume is deliberately dropped — Yahoo publishes a share
            // count, a different unit from the stored ₺ turnover semantic (FLG-09(b)).
            var yahooRows = ParseYahooSessionRows(raw, symbol, sessionDate);
            var last = yahooRows.LastOrDefault();
            var items = last is null
                ? Array.Empty<IntradayQuoteFact>()
                : new[] { new IntradayQuoteFact(symbol, sessionDate, last, DayVolume: null) };
            return new SourcePayload<IntradayQuotePayload>(
                YahooSource,
                new IntradayQuotePayload(YahooSource, sessionDate, items),
                raw);
        }

        if (IsYatirimPriceDecoder.IsEnvelope(document))
        {
            // The real İş envelope (01 §10.9.1): transport failures are source failures
            // (TC-MDF-070's class), then the request's session row is narrowed out of the
            // range body. Its close is the last price the daily-bar-shaped source publishes.
            IsYatirimPriceDecoder.EnsureSourceOk(document);
            var rows = IsYatirimPriceDecoder.DecodeRows(document)
                .Where(row => MatchesSymbol(row.Symbol, symbol) && row.Date == sessionDate)
                .ToList();
            var fact = rows
                .LastOrDefault(row => row.Close is not null) is { } sessionRow
                ? new IntradayQuoteFact(symbol, sessionDate, sessionRow.Close, sessionRow.Volume)
                : null;
            var items = fact is null ? Array.Empty<IntradayQuoteFact>() : new[] { fact };
            return new SourcePayload<IntradayQuotePayload>(
                IsYatirimSource,
                new IntradayQuotePayload(IsYatirimSource, sessionDate, items),
                raw);
        }

        var payload = Deserialize<IntradayQuotePayload>(raw);
        if (payload is null)
        {
            throw new SourceFetchException("Intraday payload deserialized to null.");
        }

        var filtered = payload.Items
            .Where(item => MatchesSymbol(item.Symbol, symbol))
            .ToList();
        return new SourcePayload<IntradayQuotePayload>(payload.SourceRef, payload with { Items = filtered }, raw);
    }

    /// <summary>Yahoo's same-day closes in timestamp order (the last one is the last price).</summary>
    private static IEnumerable<decimal?> ParseYahooSessionRows(string raw, string symbol, DateOnly sessionDate)
    {
        var payload = Deserialize<YahooPricesPayload>(raw)
            ?? throw new SourceFetchException("Yahoo intraday payload deserialized to null.");

        foreach (var result in payload.Chart?.Result ?? [])
        {
            if (!MatchesSymbol(NormalizeYahooSymbol(result.Meta?.Symbol), symbol))
            {
                continue;
            }

            var timestamps = result.Timestamp ?? [];
            var quote = result.Indicators?.Quote?.FirstOrDefault();
            for (var index = 0; index < timestamps.Count; index++)
            {
                var date = DateOnly.FromDateTime(
                    DateTimeOffset.FromUnixTimeSeconds(timestamps[index]).UtcDateTime);
                if (date != sessionDate)
                {
                    continue;
                }

                yield return At(quote?.Close, index);
            }
        }
    }

    private static bool MatchesSymbol(string? candidate, string symbol) =>
        string.Equals(candidate, symbol, StringComparison.OrdinalIgnoreCase);

    /// <summary>Strips Yahoo's Istanbul-exchange <c>.IS</c> suffix to the BIST ticker.</summary>
    private static string NormalizeYahooSymbol(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return string.Empty;
        }

        var trimmed = symbol.Trim();
        return trimmed.EndsWith(".IS", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^3]
            : trimmed;
    }

    private static decimal? At(IReadOnlyList<decimal?>? values, int index) =>
        values is not null && index >= 0 && index < values.Count ? values[index] : null;

    /// <summary>
    /// Resolves the per-symbol route: <c>{symbol}</c> and <c>{date}</c> tokens are substituted
    /// in place (the source-locale <c>dd-MM-yyyy</c> date, 01 §10.9.1); a template without the
    /// symbol token gets the symbol as a query parameter so the canonical double route keeps
    /// working.
    /// </summary>
    private static string ResolveFetchPath(string template, string symbol, DateOnly sessionDate)
    {
        var hasSymbol = template.Contains("{symbol}", StringComparison.Ordinal);
        var path = template.Replace("{symbol}", Uri.EscapeDataString(symbol), StringComparison.Ordinal);
        path = path.Replace(
            "{date}",
            sessionDate.ToString(IsYatirimPriceDecoder.SourceDateFormat, CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
        if (hasSymbol)
        {
            return path;
        }

        var separator = path.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{path}{separator}symbol={Uri.EscapeDataString(symbol)}";
    }

    private static bool IsYahooEnvelope(JsonDocument document) =>
        document.RootElement.ValueKind == JsonValueKind.Object
        && document.RootElement.TryGetProperty("chart", out _);

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

    private static T? Deserialize<T>(string raw)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(raw, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new SourcePayloadException(raw, exception);
        }
    }
}
