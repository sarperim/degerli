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
    /// <c>dd-MM-yyyy</c>); a template that does not need them simply omits them. The shipped
    /// value is İş Yatırım's <b>delayed intraday chart feed</b>
    /// (<c>ChartData.aspx/IntradayDelay</c>) — unlike the EOD-shaped <c>HisseTekil</c> daily
    /// resource it publishes the live session's 5-minute bars (probed 2026-10-23: ASELS'
    /// 15:00 UTC bar = 338.00), which is what makes an honest mid-session reading possible.
    /// The EOD prices lane keeps <c>HisseTekil</c>; a repoint at Yahoo
    /// (e.g. <c>/v8/finance/chart/{symbol}.IS?range=1d&amp;interval=5m</c>) needs no code change.
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
/// source publishes), İş Yatırım's <c>{"data":[[epochMs, price], …]}</c> <b>delayed intraday
/// chart feed</b> (the shipped intraday resource — its last bar is the current session's latest
/// reading), and Yahoo's <c>chart</c> contingency (whose last same-day observation is the
/// delayed quote). The envelope, not configuration, decides which parser runs; the concrete
/// source is stamped on every reading (<c>isyatirim</c> / <c>yahoo</c>) so storage always
/// names the source that produced the value (FR-MDF-008).
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
            // stamps the concrete source and keeps only the requested day's last *available*
            // observation (the delayed quote). The grid carries a null close for every
            // interval without a trade, so a literal "last element" can be a hole with a real
            // price before it — narrow to the last non-null close, exactly like the İş branch
            // below. A session whose every interval is null has no reading at all: an empty
            // item list (the honest no-data class), never a hole passed off as a price.
            // Volume is deliberately dropped — Yahoo publishes a share count, a different unit
            // from the stored ₺ turnover semantic (FLG-09(b)).
            var yahooRows = ParseYahooSessionRows(raw, symbol, sessionDate);
            var last = yahooRows.LastOrDefault(close => close is not null);
            var items = last is null
                ? Array.Empty<IntradayQuoteFact>()
                : new[] { new IntradayQuoteFact(symbol, sessionDate, last, DayVolume: null) };
            return new SourcePayload<IntradayQuotePayload>(
                YahooSource,
                new IntradayQuotePayload(YahooSource, sessionDate, items),
                raw);
        }

        if (IsYatirimChartEnvelope(document))
        {
            // İş Yatırım's delayed intraday chart feed (the shipped intraday resource, probed
            // 2026-10-23): a per-code body of 5-minute bars, each [epoch-milliseconds, price].
            // It carries no volume (never synthesised: a price bar is not a ₺ turnover —
            // FLG-09(b)) and echoes no symbol (the configured route already names the code).
            //
            // The reading is the last bar of the **latest session present in the payload**,
            // and only when that session is the one being swept. The feed returns a rolling
            // window that can still contain yesterday's session at the open (the probe body
            // carried 2026-10-22 and 2026-10-23), so a payload whose newest bar belongs to an
            // earlier session is the honest no-data class for today — an empty item list,
            // never yesterday's price stamped with today's date. The session date is derived
            // from the bar's own epoch timestamp, never from the request or the clock.
            var bars = ParseChartBars(document);
            var latestSession = bars.Count == 0 ? (DateOnly?)null : bars.Max(bar => bar.SessionDate);
            var lastBar = latestSession == sessionDate
                ? bars.Where(bar => bar.SessionDate == sessionDate)
                    .OrderBy(bar => bar.EpochMilliseconds)
                    .Last()
                : (ChartBar?)null;
            var chartItems = lastBar is { } bar
                ? new[] { new IntradayQuoteFact(symbol, bar.SessionDate, bar.Price, DayVolume: null) }
                : Array.Empty<IntradayQuoteFact>();
            return new SourcePayload<IntradayQuotePayload>(
                IsYatirimSource,
                new IntradayQuotePayload(IsYatirimSource, sessionDate, chartItems),
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

    /// <summary>
    /// Yahoo's same-day closes in timestamp order; a null entry is an interval with no trade,
    /// so the last <b>non-null</b> one is the last available price.
    /// </summary>
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

    // NOTE (TKT-foundation-047): the envelope/symbol helpers below (MatchesSymbol,
    // NormalizeYahooSymbol, the Yahoo .IS suffix strip) deliberately duplicate
    // `PricesSourceAdapter`'s. `src/Ingestion/Prices/` is TKT-foundation-043's lane and was
    // read-only for this ticket, so sharing them would have meant editing out-of-scope code;
    // the duplication is to be retired (extracted into one shared helper) the next time
    // `src/Ingestion/Prices/` opens.
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

    /// <summary>
    /// True when the document is İş Yatırım's delayed intraday chart feed: an object carrying
    /// a <c>data</c> member (the canonical payload uses <c>sourceRef</c>/<c>items</c>, the EOD
    /// envelope <c>ok</c>/<c>value</c>, Yahoo <c>chart</c>). The member's <i>presence</i>
    /// decides the branch, not its type: a body whose <c>data</c> is malformed is still the
    /// chart feed and must decode to the honest no-data outcome, never fall through to the
    /// canonical parser. The feed also carries a root <c>timestamp</c> field (the server's wall
    /// clock); it is transport metadata and never becomes the reading's session date.
    /// </summary>
    private static bool IsYatirimChartEnvelope(JsonDocument document) =>
        document.RootElement.ValueKind == JsonValueKind.Object
        && document.RootElement.TryGetProperty("data", out _);

    /// <summary>One chart-feed bar: the source's epoch-millisecond stamp, its UTC session date and price.</summary>
    private readonly record struct ChartBar(DateOnly SessionDate, long EpochMilliseconds, decimal Price);

    /// <summary>
    /// Decodes the chart feed's <c>data</c> bars, skipping anything malformed (a non-array
    /// entry, a non-numeric stamp or price) rather than failing the whole payload: a body with
    /// no usable bar is the honest no-data outcome. Stamps are epoch <b>milliseconds</b>; BIST
    /// session bars sit entirely inside one UTC calendar date, so the UTC date is the trading session
    /// date (the Yahoo branch derives it the same way).
    /// </summary>
    private static IReadOnlyList<ChartBar> ParseChartBars(JsonDocument document)
    {
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var bars = new List<ChartBar>();
        foreach (var entry in data.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Array || entry.GetArrayLength() < 2)
            {
                continue;
            }

            var stamp = entry[0];
            var price = entry[1];
            if (stamp.ValueKind != JsonValueKind.Number
                || !stamp.TryGetInt64(out var epochMilliseconds)
                || price.ValueKind != JsonValueKind.Number
                || !price.TryGetDecimal(out var value))
            {
                continue;
            }

            var sessionDate = DateOnly.FromDateTime(
                DateTimeOffset.FromUnixTimeMilliseconds(epochMilliseconds).UtcDateTime);
            bars.Add(new ChartBar(sessionDate, epochMilliseconds, value));
        }

        return bars;
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
