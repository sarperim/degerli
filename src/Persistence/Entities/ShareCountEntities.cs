namespace Degerli.Persistence.Entities;

/// <summary>
/// The per-instrument <b>paid-in capital</b> store (TKT-foundation-058 as renamed by
/// TKT-foundation-086, 01 v1.5.60 §10.9.11): one row per instrument, the figure the VYK
/// <c>/memberSecurities</c> security lines carry (<c>currentCapital</c>, aggregated across the
/// instrument's tertip lines).
/// <para>
/// <b>The unit is resolved, and it is money (USER DECISION D4, 2026-10-26):</b> the value is
/// <b>paid-in capital in Turkish lira</b> — 64 of 556 live rows carry sub-unit precision, which
/// no share count can. It is therefore named for what it is, in the column (<c>paid_in_capital</c>)
/// as well as in the served contract: a lying column name one layer down would be the same error
/// the rename exists to prevent. This store is <b>never</b> a source of shares outstanding — the
/// count comes from the price-lane derivation (<see cref="PriceDerivedShareCount"/>) or, when
/// evidenced, from a statement-reported count.
/// </para>
/// <para>
/// It is an input-aware current-value store — a re-run with an unchanged figure writes nothing, a
/// changed figure is repaired in place (the TKT-foundation-042/050 doctrine) — because the
/// endpoint serves a <b>current</b> <c>paidInCapital</c> + <c>asOf</c>, not a history.
/// </para>
/// <para>
/// <b>Honest absence:</b> an instrument with no security line (the archive-absent members) has
/// <b>no row</b> here — never a zero row (the 031/052 doctrine). The serving seam renders the
/// absence as a null/dash, never as <c>0</c>.
/// </para>
/// </summary>
public class ShareCount
{
    /// <summary>One row per instrument (primary key).</summary>
    public long InstrumentId { get; set; }

    /// <summary>
    /// The instrument's paid-in capital in TRY, sourced from the VYK security lines'
    /// <c>currentCapital</c>, aggregated across the instrument's matching tertip lines
    /// (01 v1.5.36's evidenced hypothesis; the unit was resolved to money by the 065/058
    /// cross-validation — 64/556 rows carry sub-unit precision).
    /// </summary>
    public decimal PaidInCapital { get; set; }

    /// <summary>When the value was last written (the served <c>asOf</c>); an unchanged re-run does not move it.</summary>
    public DateTimeOffset RecordedAt { get; set; }

    /// <summary>Provenance of the fact (FR-MDF-008) — <c>kap</c> for the VYK gateway.</summary>
    public string SourceRef { get; set; } = null!;

    /// <summary>
    /// The stored column's name is part of the contract (TKT-foundation-086's forward-only
    /// rename): <c>paid_in_capital</c>, never <c>shares_outstanding</c>.
    /// </summary>
    public const string TableName = "share_counts";
}

/// <summary>
/// The per-instrument <b>price-derived share count</b> (TKT-foundation-086, USER DECISION D5,
/// 01 v1.5.60 §10.9.11): the honest count the prices lane can produce from the payload it already
/// fetches — <c>count = PD ÷ close</c>, where both operands are the source's own figures for one
/// instrument-date.
/// <para>
/// <b>Every operand is stored, so the arithmetic is auditable from the row</b> (FR-MDF-008):
/// <see cref="MarketCapRaw"/> (TL), <see cref="CloseRaw"/> (TL), the <see cref="PriceDate"/> the
/// pair belongs to, the <see cref="SourceRef"/> that produced it and the instant it was
/// <see cref="RecordedAt"/>. A count without its operands would be an unverifiable claim.
/// </para>
/// <para>
/// <b>Integral or withheld (the honesty core):</b> the row is written only when the quotient is
/// integral within the store's pinned tolerance. A non-integral quotient means the two operands
/// disagree — withheld and counted under its own reason code, <b>never rounded</b>; a missing
/// <c>PD</c> or a non-positive close is the same kind of named, counted skip; the count is
/// <b>never zero</b>. No nominal divisor and no <c>/memberSecurities</c> division exists here —
/// the 065 Option-B path stays forbidden.
/// </para>
/// <para>
/// Input-aware like every derived lane (042/050): an unchanged fact writes nothing and does not
/// move <see cref="RecordedAt"/>; a changed fact repairs the row in place; and the as-of is
/// <b>monotonic</b> — an older fact never regresses a row a newer one already supplied. A
/// withheld fact leaves any stored row alone: the row's own as-of is what makes a stale count
/// honest, and silently deleting it would be the same class of error as inventing one.
/// </para>
/// <para>
/// <b>Precedence (pinned, recorded in 01/02 for a future lane):</b> a statement-reported share
/// count (<c>SHARES_DILUTED</c>) supersedes this derived count when a statements decoder actually
/// evidences one. This store is the fallback, not the authority.
/// </para>
/// </summary>
public class PriceDerivedShareCount
{
    /// <summary>One row per instrument (primary key).</summary>
    public long InstrumentId { get; set; }

    /// <summary>The derived share count — <b>integer-valued</b> by construction (never a rounded guess).</summary>
    public long SharesOutstanding { get; set; }

    /// <summary>The source's market capitalization (<c>PD</c>, TL) the count was derived from.</summary>
    public decimal MarketCapRaw { get; set; }

    /// <summary>The close (TL) the market cap was divided by.</summary>
    public decimal CloseRaw { get; set; }

    /// <summary>The session date of the operand pair — the served as-of of the count.</summary>
    public DateOnly PriceDate { get; set; }

    /// <summary>When the row was written; an unchanged re-run does not move it.</summary>
    public DateTimeOffset RecordedAt { get; set; }

    /// <summary>Provenance of the fact (FR-MDF-008) — <c>isyatirim</c> for the price lane.</summary>
    public string SourceRef { get; set; } = null!;

    /// <summary>The store's table name (01 v1.5.60 §10.9.11, 02 §3.1).</summary>
    public const string TableName = "price_derived_share_counts";
}
