using System.Text.Json.Serialization;

namespace Degerli.Ingestion.Prices;

/// <summary>
/// The İş Yatırım EOD price payload (FU §10 <c>prices-*.json</c>). The top-level
/// <see cref="SourceRef"/> is the provenance carried onto every stored fact (FR-MDF-008);
/// its absence is the missing-provenance class (TC-MDF-008).
/// </summary>
public sealed record PricesPayload(
    [property: JsonPropertyName("sourceRef")] string? SourceRef,
    [property: JsonPropertyName("date")] DateOnly Date,
    [property: JsonPropertyName("items")] IReadOnlyList<PriceFact> Items);

/// <summary>
/// One instrument's EOD row within a payload.
/// <para>
/// <see cref="MarketCap"/> is the source's <b>market capitalization</b> (<c>PD</c>, TL) when
/// the envelope carries one — additive and <b>nullable</b> (TKT-foundation-086, USER DECISION
/// D5). The İş Yatırım daily body carries it in the same row as the close; the canonical double
/// and the Yahoo contingency carry none, which is an honest absence, never a zero. It is the
/// numerator of the price-lane share-count derivation (<c>count = PD ÷ close</c>) and is stored
/// as an operand, never served as a fact in its own right.
/// </para>
/// </summary>
public sealed record PriceFact(
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("previousClose")] decimal? PreviousClose,
    [property: JsonPropertyName("open")] decimal? Open,
    [property: JsonPropertyName("high")] decimal? High,
    [property: JsonPropertyName("low")] decimal? Low,
    [property: JsonPropertyName("close")] decimal? Close,
    [property: JsonPropertyName("volume")] long? Volume,
    [property: JsonPropertyName("marketCap")] decimal? MarketCap = null);

/// <summary>
/// The historical price payload served in backfill mode (TKT-mdf-007, FR-MDF-010):
/// a flat, dated row set covering as far back as the source allows. The earliest row
/// is the source's real history limit — the job records it as the achieved depth
/// rather than fabricating rows ahead of it (BR-MDF-006).
/// </summary>
public sealed record PricesHistoryPayload(
    [property: JsonPropertyName("sourceRef")] string? SourceRef,
    [property: JsonPropertyName("rows")] IReadOnlyList<DatedPriceFact> Rows);

/// <summary>
/// The Yahoo Finance <c>.IS</c> contingency envelope (FR-MDF-001, 03 §1.2). Yahoo's
/// public chart payload is range-shaped and carries no provenance field, so the adapter
/// detects it by its <see cref="Chart"/> root and stamps the concrete source
/// (<c>yahoo</c>) itself. The two sources' envelopes are distinct on purpose: a stored
/// row always names the source that actually produced it.
/// </summary>
public sealed record YahooPricesPayload(
    [property: JsonPropertyName("chart")] YahooChart? Chart);

/// <summary>Yahoo chart container (<c>chart.result[]</c>).</summary>
public sealed record YahooChart(
    [property: JsonPropertyName("result")] IReadOnlyList<YahooChartResult>? Result);

/// <summary>One instrument's chart result: meta + parallel timestamp/quote arrays.</summary>
public sealed record YahooChartResult(
    [property: JsonPropertyName("meta")] YahooMeta? Meta,
    [property: JsonPropertyName("timestamp")] IReadOnlyList<long>? Timestamp,
    [property: JsonPropertyName("indicators")] YahooIndicators? Indicators);

/// <summary>Yahoo result metadata carrying the (suffixed) ticker.</summary>
public sealed record YahooMeta(
    [property: JsonPropertyName("symbol")] string? Symbol,
    [property: JsonPropertyName("currency")] string? Currency);

/// <summary>Yahoo indicators container.</summary>
public sealed record YahooIndicators(
    [property: JsonPropertyName("quote")] IReadOnlyList<YahooQuote>? Quote);

/// <summary>
/// Yahoo's quote arrays — index-aligned with the result's <c>timestamp</c> array (a
/// null element marks a missing observation for that timestamp).
/// </summary>
public sealed record YahooQuote(
    [property: JsonPropertyName("open")] IReadOnlyList<decimal?>? Open,
    [property: JsonPropertyName("high")] IReadOnlyList<decimal?>? High,
    [property: JsonPropertyName("low")] IReadOnlyList<decimal?>? Low,
    [property: JsonPropertyName("close")] IReadOnlyList<decimal?>? Close,
    [property: JsonPropertyName("volume")] IReadOnlyList<long?>? Volume);

/// <summary>
/// One dated instrument row within a backfill history payload. <see cref="MarketCap"/> is the
/// source's <c>PD</c> (TL) when the row carries one — additive, nullable, and honest absence
/// otherwise (TKT-foundation-086, USER DECISION D5; see <see cref="PriceFact.MarketCap"/>).
/// </summary>
public sealed record DatedPriceFact(
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("date")] DateOnly Date,
    [property: JsonPropertyName("open")] decimal? Open,
    [property: JsonPropertyName("high")] decimal? High,
    [property: JsonPropertyName("low")] decimal? Low,
    [property: JsonPropertyName("close")] decimal? Close,
    [property: JsonPropertyName("volume")] long? Volume,
    [property: JsonPropertyName("marketCap")] decimal? MarketCap = null);

/// <summary>A fact refused by the fact-storage invariants, with its reason code.</summary>
public sealed record RejectedPriceFact(PriceFact Fact, string ReasonCode);

/// <summary>Result of one batch upsert: what was written, what was an idempotent
/// no-op, and what was refused (conflicts and structural rejects).</summary>
public sealed record PriceWriteResult(
    int Inserted,
    int Unchanged,
    IReadOnlyList<PriceFact> Conflicts,
    IReadOnlyList<RejectedPriceFact> Rejected)
{
    public static readonly PriceWriteResult Empty = new(0, 0, [], []);
}
