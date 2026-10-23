using System.Globalization;
using System.Text.Json;
using Degerli.Ingestion.Sources;
using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.Prices;

/// <summary>
/// Source wiring for the <c>prices</c> job (config section <c>Ingestion:Prices</c>).
/// <see cref="BaseUrl"/> points at İş Yatırım in production and at the WireMock source
/// double in L2 tests; the paths let a test select a named canned payload (FU §10).
/// The documented contingency is Yahoo Finance (<c>.IS</c> tickers) — repointing
/// <see cref="BaseUrl"/> at it is enough, because the same client and adapter parse
/// either source's envelope.
/// </summary>
public sealed class PricesSourceOptions
{
    public const string SectionName = "Ingestion:Prices";

    /// <summary>Base address of the price source (empty → adapter must be configured).</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Path of the EOD price resource relative to <see cref="BaseUrl"/>.</summary>
    public string Path { get; set; } = "/prices/eod";

    /// <summary>Path of the historical price resource used in backfill mode (TKT-mdf-007).</summary>
    public string BackfillPath { get; set; } = "/prices/history";

    /// <summary>
    /// The index whose current constituents (<c>index_constituents</c>, <c>effective_to IS NULL</c>)
    /// the production per-ticker fetch loops over (01 v1.5 §10.9.2, FR-MDF-006/007). Launch
    /// default <c>XU030</c>; flipping to <c>XU100</c> later is a config-only change — no code.
    /// </summary>
    public string UniverseIndex { get; set; } = "XU030";

    /// <summary>
    /// Inter-request delay (milliseconds) pacing the strictly sequential per-ticker loop
    /// against the undocumented public endpoint (01 v1.5 §10.9.2 — the politeness posture:
    /// sequential, no parallelism). <c>0</c> disables the wait; L2 harnesses bind it to
    /// <c>0</c> so the loop is deterministic.
    /// </summary>
    public int RequestDelayMs { get; set; } = 500;

    /// <summary>
    /// How many <b>passes</b> one incremental run may make over the bound to complete the
    /// day's coverage (01 v1.5 §10.9.2 amendment — the completeness gate). İş Yatırım
    /// publishes a session progressively: a symbol answers EMPTY shortly after the close
    /// and its full OHLCV row appears later the same evening, so a single pass can leave the
    /// day partly covered while the source still has the data coming. <c>1</c> disables the
    /// re-sweep (single pass); the default allows a first pass plus two bounded re-sweeps of
    /// only the symbols that answered empty. Never an unbounded loop: this bound and
    /// <see cref="CoverageBudget"/> both apply.
    /// </summary>
    public int CoverageMaxPasses { get; set; } = 3;

    /// <summary>
    /// Settle wait between coverage passes (bound as a <see cref="TimeSpan"/>). <b>Default
    /// <c>00:00:00</c> — no extra wait</b>: a pass is already spaced from the next by its own
    /// pacing (the pinned sequential posture makes a 100-symbol pass ≈ 50 s at
    /// <see cref="RequestDelayMs"/> 500 ms and ≈ 5 minutes at İş's ≥3 s politeness floor), so a
    /// re-sweep that starts when the previous paced pass ends is already minutes later, and
    /// every L2 run stays free of real timers. Raise it (e.g. <c>00:05:00</c>) to space passes
    /// further apart; <c>&lt;= 0</c> skips the wait entirely.
    /// </summary>
    public TimeSpan CoverageSettleDelay { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Wall-clock budget for the run's completeness effort (default 25 minutes: three
    /// 100-symbol passes at the politeness floor plus slack). Once the budget is spent the run
    /// stops re-sweeping and reports the coverage it actually achieved — it never spins.
    /// Deliberately well under the scheduler's per-attempt <c>MaxRunDuration</c> (90 min), which
    /// also bounds the whole EOD chain. Bound as a <see cref="TimeSpan"/>; <c>&lt;= 0</c> means
    /// "no time budgeted for re-sweeps" (single pass).
    /// </summary>
    public TimeSpan CoverageBudget { get; set; } = TimeSpan.FromMinutes(25);
}

/// <summary>
/// The price adapter (FR-MDF-001) for the İş Yatırım public endpoints (primary) with the
/// documented Yahoo Finance <c>.IS</c> contingency (03 §1.2). It fetches the raw payload
/// through the shared <see cref="ISourceClient"/>, detects which source's envelope it is —
/// all three first-class per 01 v1.5 §10.9.1: the <b>real</b> İş Yatırım <c>value</c> +
/// <c>HGDG_*</c> body (production primary, decoded by <see cref="IsYatirimPriceDecoder"/>),
/// Yahoo's <c>chart</c> shape (contingency), and the canonical <c>sourceRef</c>/<c>items</c>
/// double envelope (the L2 golden contract, §10.9.7) — and parses it into the canonical
/// payload; persistence and validation are the job's. Every parsed payload carries the
/// concrete source name (<c>isyatirim</c> / <c>yahoo</c>) so each stored row records its
/// provenance (FR-MDF-008). It is also the backfill adapter (FR-MDF-010): the same client
/// reads the historical resource, which returns as far back as the source allows.
/// </summary>
public sealed class PricesSourceAdapter : ISourceAdapter<PricesPayload>, IBackfillSourceAdapter<PricesHistoryPayload>
{
    /// <summary>Concrete source name of the primary EOD/index-level source (03 §1.2).</summary>
    public const string IsYatirimSource = "isyatirim";

    /// <summary>Concrete source name of the documented contingency (Yahoo `.IS`).</summary>
    public const string YahooSource = "yahoo";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISourceClient _client;
    private readonly PricesSourceOptions _options;

    public PricesSourceAdapter(ISourceClient client, IOptions<PricesSourceOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    /// <inheritdoc />
    public string SourceName => IsYatirimSource;

    /// <inheritdoc />
    public async Task<SourcePayload<PricesPayload>> FetchAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var raw = await _client.GetStringAsync(_options.Path, cancellationToken).ConfigureAwait(false);
        using var document = ParseDocument(raw);

        if (IsYahooEnvelope(document))
        {
            // Yahoo is range-shaped and carries no provenance field; the adapter stamps
            // the concrete source and keeps only the requested day's rows.
            var items = ParseYahooRows(raw)
                .Where(row => row.Date == date)
                .Select(ToPriceFact)
                .ToList();
            return new SourcePayload<PricesPayload>(
                YahooSource,
                new PricesPayload(YahooSource, date, items),
                raw);
        }

        if (IsYatirimPriceDecoder.IsEnvelope(document))
        {
            // The real İş Yatırım envelope (01 v1.5 §10.9.1): a per-ticker range body.
            // Transport fields are checked first (ok:false / errorCode → source failure),
            // then the requested day's rows are mapped onto the canonical payload.
            IsYatirimPriceDecoder.EnsureSourceOk(document);
            var items = IsYatirimPriceDecoder.DecodeRows(document)
                .Where(row => row.Date == date)
                .Select(ToPriceFact)
                .ToList();
            return new SourcePayload<PricesPayload>(
                IsYatirimSource,
                new PricesPayload(IsYatirimSource, date, items),
                raw);
        }

        var payload = Deserialize<PricesPayload>(raw);
        if (payload is null)
        {
            throw new SourceFetchException("Price payload deserialized to null.");
        }

        return new SourcePayload<PricesPayload>(payload.SourceRef, payload, raw);
    }

    /// <inheritdoc />
    public async Task<SourcePayload<PricesHistoryPayload>> FetchBackfillAsync(
        DateOnly from,
        CancellationToken cancellationToken = default)
    {
        // The requested range is passed to the source as a query hint, but the source
        // returns whatever history it holds — the job records the achieved depth.
        var path = $"{_options.BackfillPath}?from={from:yyyy-MM-dd}";
        var raw = await _client.GetStringAsync(path, cancellationToken).ConfigureAwait(false);
        using var document = ParseDocument(raw);

        if (IsYahooEnvelope(document))
        {
            var rows = ParseYahooRows(raw).ToList();
            return new SourcePayload<PricesHistoryPayload>(
                YahooSource,
                new PricesHistoryPayload(YahooSource, rows),
                raw);
        }

        if (IsYatirimPriceDecoder.IsEnvelope(document))
        {
            // The same real-envelope field map as the fetch path (01 v1.5 §10.9.1): the
            // source returns whatever history it holds, so the achieved depth is what it
            // returned — never fabricated beyond the source limit (BR-MDF-006).
            IsYatirimPriceDecoder.EnsureSourceOk(document);
            var rows = IsYatirimPriceDecoder.DecodeRows(document);
            return new SourcePayload<PricesHistoryPayload>(
                IsYatirimSource,
                new PricesHistoryPayload(IsYatirimSource, rows),
                raw);
        }

        var payload = Deserialize<PricesHistoryPayload>(raw);
        if (payload is null)
        {
            throw new SourceFetchException("Price history payload deserialized to null.");
        }

        return new SourcePayload<PricesHistoryPayload>(payload.SourceRef, payload, raw);
    }

    /// <summary>
    /// Symbol-scoped incremental fetch (01 v1.5 §10.9.2, FR-MDF-001): the per-ticker loop
    /// resolves the configured index's current constituents and asks the adapter for one
    /// symbol's payload for the requested day. Envelope detection stays inside the adapter
    /// (real İş Yatırım HGDG / Yahoo chart / canonical double, §10.9.1); rows are filtered to
    /// the requested <paramref name="symbol"/> so a range-shaped body is narrowed to that
    /// instrument. Persistence, validation and quarantine stay in the job.
    /// </summary>
    public async Task<SourcePayload<PricesPayload>> FetchSymbolAsync(
        string symbol,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        var raw = await _client
            .GetStringAsync(ResolveFetchPath(_options.Path, symbol, date), cancellationToken)
            .ConfigureAwait(false);
        using var document = ParseDocument(raw);

        if (IsYahooEnvelope(document))
        {
            var items = ParseYahooRows(raw)
                .Where(row => MatchesSymbol(row.Symbol, symbol) && row.Date == date)
                .Select(ToPriceFact)
                .ToList();
            return new SourcePayload<PricesPayload>(
                YahooSource,
                new PricesPayload(YahooSource, date, items),
                raw);
        }

        if (IsYatirimPriceDecoder.IsEnvelope(document))
        {
            IsYatirimPriceDecoder.EnsureSourceOk(document);
            var items = IsYatirimPriceDecoder.DecodeRows(document)
                .Where(row => MatchesSymbol(row.Symbol, symbol) && row.Date == date)
                .Select(ToPriceFact)
                .ToList();
            return new SourcePayload<PricesPayload>(
                IsYatirimSource,
                new PricesPayload(IsYatirimSource, date, items),
                raw);
        }

        var payload = Deserialize<PricesPayload>(raw);
        if (payload is null)
        {
            throw new SourceFetchException("Price payload deserialized to null.");
        }

        var filtered = payload.Items
            .Where(item => MatchesSymbol(item.Symbol, symbol))
            .ToList();
        return new SourcePayload<PricesPayload>(payload.SourceRef, payload with { Items = filtered }, raw);
    }

    /// <summary>
    /// Symbol-scoped backfill fetch (01 v1.5 §10.9.2/§10.9.1, FR-MDF-010): the same
    /// per-ticker loop asks for one symbol's historical range; the source returns whatever
    /// history it holds, so the achieved depth is what it returned — never fabricated beyond
    /// the source limit (BR-MDF-006). Rows are filtered to <paramref name="symbol"/>.
    /// </summary>
    public async Task<SourcePayload<PricesHistoryPayload>> FetchSymbolBackfillAsync(
        string symbol,
        DateOnly from,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        var raw = await _client
            .GetStringAsync(ResolveBackfillPath(_options.BackfillPath, symbol, from), cancellationToken)
            .ConfigureAwait(false);
        using var document = ParseDocument(raw);

        if (IsYahooEnvelope(document))
        {
            var rows = ParseYahooRows(raw).Where(row => MatchesSymbol(row.Symbol, symbol)).ToList();
            return new SourcePayload<PricesHistoryPayload>(
                YahooSource,
                new PricesHistoryPayload(YahooSource, rows),
                raw);
        }

        if (IsYatirimPriceDecoder.IsEnvelope(document))
        {
            IsYatirimPriceDecoder.EnsureSourceOk(document);
            var rows = IsYatirimPriceDecoder.DecodeRows(document)
                .Where(row => MatchesSymbol(row.Symbol, symbol))
                .ToList();
            return new SourcePayload<PricesHistoryPayload>(
                IsYatirimSource,
                new PricesHistoryPayload(IsYatirimSource, rows),
                raw);
        }

        var payload = Deserialize<PricesHistoryPayload>(raw);
        if (payload is null)
        {
            throw new SourceFetchException("Price history payload deserialized to null.");
        }

        var filtered = payload.Rows
            .Where(row => MatchesSymbol(row.Symbol, symbol))
            .ToList();
        return new SourcePayload<PricesHistoryPayload>(payload.SourceRef, payload with { Rows = filtered }, raw);
    }

    private static bool MatchesSymbol(string? candidate, string symbol) =>
        string.Equals(candidate, symbol, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves the incremental route for one symbol: a <c>{symbol}</c> template in the
    /// configured path is substituted in place; a path without the token gets the symbol as
    /// a query parameter instead (the FU §10 double route is fixed, so the loop still reaches
    /// it). An optional <c>{date}</c> token is substituted with the source-locale
    /// <c>dd-MM-yyyy</c> date (the real endpoint's date format, §10.9.1).
    /// </summary>
    private static string ResolveFetchPath(string template, string symbol, DateOnly date)
    {
        var hasSymbol = template.Contains("{symbol}", StringComparison.Ordinal);
        var path = template.Replace("{symbol}", Uri.EscapeDataString(symbol), StringComparison.Ordinal);
        path = path.Replace(
            "{date}",
            date.ToString(IsYatirimPriceDecoder.SourceDateFormat, CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
        return hasSymbol ? path : AppendQuery(path, "symbol", symbol);
    }

    /// <summary>
    /// Resolves the backfill route for one symbol, mirroring <see cref="ResolveFetchPath"/>:
    /// <c>{symbol}</c> and an optional <c>{from}</c> template are substituted; when the
    /// template lacks the token the symbol and the ISO <c>from</c> date are appended as query
    /// parameters (the pre-existing backfill hint), preserving the canonical double route.
    /// </summary>
    private static string ResolveBackfillPath(string template, string symbol, DateOnly from)
    {
        var hasSymbol = template.Contains("{symbol}", StringComparison.Ordinal);
        var hasFrom = template.Contains("{from}", StringComparison.Ordinal);
        var path = template.Replace("{symbol}", Uri.EscapeDataString(symbol), StringComparison.Ordinal);
        path = path.Replace(
            "{from}",
            from.ToString(IsYatirimPriceDecoder.SourceDateFormat, CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
        if (!hasSymbol)
        {
            path = AppendQuery(path, "symbol", symbol);
        }

        if (!hasFrom)
        {
            path = AppendQuery(path, "from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        return path;
    }

    private static string AppendQuery(string path, string key, string value)
    {
        var separator = path.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{path}{separator}{key}={Uri.EscapeDataString(value)}";
    }

    /// <summary>True when the body is a Yahoo chart envelope rather than the İş Yatırım one.</summary>
    private static bool IsYahooEnvelope(JsonDocument document) =>
        document.RootElement.ValueKind == JsonValueKind.Object
        && document.RootElement.TryGetProperty("chart", out _);

    private static IEnumerable<DatedPriceFact> ParseYahooRows(string raw)
    {
        var payload = Deserialize<YahooPricesPayload>(raw)
            ?? throw new SourceFetchException("Yahoo price payload deserialized to null.");

        foreach (var result in payload.Chart?.Result ?? [])
        {
            var symbol = NormalizeYahooSymbol(result.Meta?.Symbol);
            if (string.IsNullOrWhiteSpace(symbol))
            {
                continue;
            }

            var timestamps = result.Timestamp ?? [];
            var quote = result.Indicators?.Quote?.FirstOrDefault();
            for (var index = 0; index < timestamps.Count; index++)
            {
                yield return new DatedPriceFact(
                    symbol,
                    DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(timestamps[index]).UtcDateTime),
                    At(quote?.Open, index),
                    At(quote?.High, index),
                    At(quote?.Low, index),
                    At(quote?.Close, index),
                    // Volume is deliberately dropped: Yahoo's field is share count, a
                    // different unit from the stored ₺ turnover semantic (FLG-09(b),
                    // 01 v1.5 §10.9.1). Deriving ₺ from shares × close was rejected as
                    // computing — the same honest-gap posture as `open`. Stored NULL.
                    Volume: null);
            }
        }
    }

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

    private static PriceFact ToPriceFact(DatedPriceFact row) =>
        new(row.Symbol, null, row.Open, row.High, row.Low, row.Close, row.Volume);

    private static decimal? At(IReadOnlyList<decimal?>? values, int index) =>
        values is not null && index >= 0 && index < values.Count ? values[index] : null;

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
