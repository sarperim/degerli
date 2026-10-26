using Degerli.Persistence.Entities;

namespace Degerli.Ingestion.Statements;

/// <summary>
/// The pinned cross-source read preference for stored statements (<c>01</c> v1.5.57 §10.9.19,
/// TC-MDF-111; FR-MDF-008, FR-MDF-015).
/// <para>
/// Since <see cref="FinancialStatementStore"/>'s natural key includes the source, two sources may
/// each hold a fact for the same period — the VYK lane's <see cref="IncumbentSource"/> and the
/// public-site lane's <see cref="PublicSiteSource"/>. The rule is:
/// <b>the public-site current fact wins for the periods it covers; the incumbent's rows are
/// retained and stay as-of queryable.</b> The preference is a property of the source, never of
/// the write order: it is <b>not</b> a silent latest-write and <b>not</b> an order-nondeterministic
/// tie-break.
/// </para>
/// <para>
/// <b>Whole statement, never per line.</b> The winner is picked per
/// <c>(period, statement type)</c> and all of its lines are served from that one row. Mixing lines
/// from two sources would serve a statement no source ever filed — and it is exactly what makes
/// market P/E and the per-share figures honestly <c>NULL</c> when the current fact's source
/// carries no share count (the 065 Option-A posture: a paid-in-capital figure is never divided
/// into a count, and an older source's share count is never borrowed for a newer filing).
/// </para>
/// </summary>
public static class StatementSourcePreference
{
    /// <summary>The incumbent (VYK disclosure) lane's evidence class.</summary>
    public const string IncumbentSource = "kap";

    /// <summary>The public-site lane's evidence class.</summary>
    public const string PublicSiteSource = "kap-public";

    /// <summary>
    /// The preference rank of one evidence class — lower wins. The public-site lane is preferred;
    /// the incumbent is the retained fallback; any other recorded class (a fixture/backfill
    /// provenance) is served only when neither source above holds the key.
    /// </summary>
    public static int Rank(string? sourceRef) => sourceRef switch
    {
        PublicSiteSource => 0,
        IncumbentSource => 1,
        _ => 2,
    };

    /// <summary>
    /// The row to serve for one <c>(period, statement type)</c> group: the preferred source, then
    /// the highest restatement <c>version</c> within it (BR-MDF-011 — the restated row serves and
    /// the as-reported one is retained). The final tie-break is the row's own identity, so the
    /// answer is total and stable: it can never depend on the rows' arrival order.
    /// </summary>
    public static FinancialStatement? Pick(IEnumerable<FinancialStatement> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        return Order(rows, row => row.SourceRef, row => row.Version, row => row.Id).FirstOrDefault();
    }

    /// <summary>
    /// The row to serve for one statement type within one period group, when the caller holds a
    /// mixed group of statement types (the served financials payload groups by period).
    /// </summary>
    public static FinancialStatement? Pick(IEnumerable<FinancialStatement> rows, string statementType) =>
        Pick(rows.Where(row => string.Equals(row.StatementType, statementType, StringComparison.Ordinal)));

    /// <summary>
    /// The same preference over a caller's own projection of the three ordering columns — the
    /// shape the engines that select only the columns they need hold
    /// (<see cref="Metrics.MarketSnapshotEngine"/>). One definition of the rule, so no consumer
    /// can fall back to a version-only (order-nondeterministic) choice.
    /// </summary>
    public static T? Pick<T>(
        IEnumerable<T> rows,
        Func<T, string?> sourceRef,
        Func<T, int> version,
        Func<T, long> id)
    {
        ArgumentNullException.ThrowIfNull(rows);

        return Order(rows, sourceRef, version, id).FirstOrDefault();
    }

    /// <summary>The preference ordering itself: source rank, then restatement version, then identity.</summary>
    public static IOrderedEnumerable<T> Order<T>(
        IEnumerable<T> rows,
        Func<T, string?> sourceRef,
        Func<T, int> version,
        Func<T, long> id) =>
        rows.OrderBy(row => Rank(sourceRef(row)))
            .ThenByDescending(version)
            .ThenBy(id);
}
