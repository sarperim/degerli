namespace Degerli.Ingestion.Intraday;

/// <summary>
/// One symbol's intraday reading for a session (TKT-foundation-047, architecture §10.9.18).
/// <see cref="LastPrice"/> is the source's current reading — never a close — and
/// <see cref="DayVolume"/> is the session's ₺ turnover when the source carries it (Yahoo's
/// share-count volume is a different unit and stays NULL — FLG-09(b)).
/// </summary>
public sealed record IntradayQuoteFact(
    string Symbol,
    DateOnly SessionDate,
    decimal? LastPrice,
    long? DayVolume);

/// <summary>
/// The canonical intraday payload the adapter emits. <see cref="SourceRef"/> carries the
/// concrete source that produced the reading (<c>isyatirim</c> primary, <c>yahoo</c> the
/// documented contingency, 01 v1.5 §10.9.1) so every stored row records its provenance
/// (FR-MDF-008).
/// </summary>
public sealed record IntradayQuotePayload(
    string? SourceRef,
    DateOnly SessionDate,
    IReadOnlyList<IntradayQuoteFact> Items);

/// <summary>An intraday reading refused by the store's invariants, with its reason code.</summary>
public sealed record RejectedIntradayQuote(IntradayQuoteFact Fact, string ReasonCode);

/// <summary>
/// Outcome of one sweep's write pass: rows inserted for the session
/// (<see cref="Inserted"/>), rows whose values the sweep re-observed identically
/// (<see cref="Unchanged"/>), and rows overwritten in place because the source's current
/// value changed (<see cref="Rewritten"/>) — the input-aware-overwrite precedent of
/// TKT-foundation-042. <see cref="Rejected"/> carries the refused readings.
/// </summary>
public sealed record IntradayQuoteWriteResult(
    int Inserted,
    int Unchanged,
    int Rewritten,
    IReadOnlyList<RejectedIntradayQuote> Rejected)
{
    public static readonly IntradayQuoteWriteResult Empty = new(0, 0, 0, []);
}
