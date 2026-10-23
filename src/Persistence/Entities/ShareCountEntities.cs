namespace Degerli.Persistence.Entities;

/// <summary>
/// The per-instrument share-count store (TKT-foundation-058, 01 v1.5.36 §10.9.11): one row per
/// instrument, the current share count the VYK <c>/memberSecurities</c> security lines carry
/// (<c>currentCapital</c>, aggregated across the instrument's tertip lines). It is an input-aware
/// current-value store — a re-run with an unchanged count writes nothing, a changed count is
/// repaired in place (the TKT-foundation-042/050 doctrine) — because the endpoint serves a
/// <b>current</b> <c>sharesOutstanding</c> + <c>asOf</c>, not a history.
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
    /// The instrument's current share count. Sourced from the VYK security lines'
    /// <c>currentCapital</c>, aggregated across the instrument's matching tertip lines
    /// (A + B, 01 v1.5.36's evidenced hypothesis). Whether <c>currentCapital</c> is a share count
    /// or paid-in TL is <b>unresolved</b>: the ticket's post-merge live cross-validation decides
    /// it, and values are treated as counts only after that table passes.
    /// </summary>
    public decimal SharesOutstanding { get; set; }

    /// <summary>When the value was last written (the served <c>asOf</c>); an unchanged re-run does not move it.</summary>
    public DateTimeOffset RecordedAt { get; set; }

    /// <summary>Provenance of the fact (FR-MDF-008) — <c>kap</c> for the VYK gateway.</summary>
    public string SourceRef { get; set; } = null!;
}
