namespace Degerli.Ingestion.Statements;

/// <summary>
/// The decode result of one KAP <b>public-site</b> excel-HTML financial report
/// (TKT-foundation-081; 01 v1.5.55 §10.9.19, FR-MDF-002/008/012). It carries the canonical
/// <see cref="StatementsPayload"/> the store persists <b>plus</b> the provenance and the
/// honesty ledger the decode must be able to account for:
/// <list type="bullet">
/// <item><see cref="Header"/> — the report identity/coordinates/unit decoded from the
/// export's own metadata block (never defaulted: an unattributable header is a counted
/// finding, not a guessed value);</item>
/// <item><see cref="Tables"/>/<see cref="Columns"/> — the statement typing and the
/// column→period mapping, both taken from the capture's own headings and column headers
/// (position is never trusted);</item>
/// <item><see cref="Lines"/> — one record per decoded value answering <em>which table, which
/// label, which column, which cell</em> (TC-MDF-107 (vi));</item>
/// <item><see cref="Findings"/> — the counted honesty ledger: every dropped value, renamed
/// duplicate, unattributable column and display-text disagreement is named with a bounded
/// verbatim excerpt. Nothing is zero-filled and nothing is silently overwritten.</item>
/// </list>
/// <para>
/// <b>Why the payload is not the whole result.</b> <c>fin_line_items</c> stores
/// <c>(item_code, value)</c> only — it has no label/table provenance column — so the
/// "which table, which label" answer lives here, on the decode result that produced the row
/// set. Persisting per-line label provenance would be a schema change, out of this ticket's
/// scope (flagged for the lane).
/// </para>
/// </summary>
public sealed record KapPublicReport(
    string Symbol,
    string SourceRef,
    KapPublicReportHeader Header,
    IReadOnlyList<KapPublicTable> Tables,
    IReadOnlyList<KapPublicLine> Lines,
    IReadOnlyList<KapPublicFinding> Findings,
    StatementsPayload Payload)
{
    /// <summary>The counted total of one honesty reason (<c>0</c> when it never occurred).</summary>
    public int Count(string reasonCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);

        var total = 0;
        foreach (var finding in Findings)
        {
            if (string.Equals(finding.ReasonCode, reasonCode, StringComparison.Ordinal))
            {
                total += finding.Count;
            }
        }

        return total;
    }

    /// <summary>The decoded lines of one statement row (statement type + period end).</summary>
    public IReadOnlyList<KapPublicLine> LinesFor(string statementType, DateOnly periodEnd)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statementType);

        return Lines
            .Where(l => string.Equals(l.StatementType, statementType, StringComparison.Ordinal)
                && l.PeriodEndDate == periodEnd)
            .ToList();
    }

    /// <summary>True when the report's period is a full fiscal year (<c>Yıllık</c>).</summary>
    public bool IsAnnual => Header.IsAnnual;

    /// <summary>
    /// The filing's consolidation flag, derived from the header-table field
    /// <c>Finansal Tablo Niteliği</c> (<see cref="KapPublicReportHeader.ConsolidationType"/>):
    /// <c>Konsolide</c> → <c>true</c>, <c>Solo</c> → <c>false</c>, absent or any other value →
    /// <c>null</c> (honest-null, never guessed). TKT-foundation-093 decodes it; persisting and
    /// serving it is the statements lane's next slice.
    /// </summary>
    public bool? Consolidated => KapPublicReportDecoder.ConsolidatedFor(Header.ConsolidationType);
}

/// <summary>
/// The report's own identity block, decoded verbatim from the export
/// (<c>Gönderim Tarihi</c>, <c>Bildirim Tipi</c>, <c>Yıl</c>, <c>Periyot</c>,
/// <c>Sunum Para Birimi</c>, <c>Finansal Tablo Niteliği</c>) plus the company heading.
/// <para>
/// <b>The period type is part of the contract (user decision, 2026-10-25).</b> The lane runs
/// on <b>annual FCF + labeled interims</b>: a DCF baseline takes the most recent
/// <b>annual</b> report's full-year FCF, while an interim populates the display under an
/// honest label ("6 months ended 30.06.2026") and is <b>never</b> annualized. The decoder
/// therefore surfaces <see cref="PeriodType"/> (<c>Yıllık</c> / <c>9 Aylık</c> /
/// <c>6 Aylık</c> / <c>3 Aylık</c>) and <see cref="PeriodMonths"/> so downstream can select
/// annuals — the row shape already carries <c>year</c>/<c>ruleType</c>/<c>period</c> from
/// <c>byCriteria</c>.
/// </para>
/// </summary>
/// <param name="CompanyName">The export's first <c>&lt;h1&gt;</c> heading, verbatim.</param>
/// <param name="ReportTitle">The export's second <c>&lt;h1&gt;</c> heading (e.g. "Finansal Rapor").</param>
/// <param name="SubmittedAt">The date part of <c>Gönderim Tarihi</c>.</param>
/// <param name="SubmissionType"><c>Bildirim Tipi</c> (e.g. <c>FR</c>).</param>
/// <param name="Year"><c>Yıl</c> as declared by the report.</param>
/// <param name="Period"><c>Periyot</c> as declared by the report (1..4).</param>
/// <param name="PeriodType">The period type derived from <paramref name="Period"/> — see
/// <see cref="KapPublicReportDecoder.PeriodTypeFor"/>; null when the period is absent or
/// outside the evidenced set (never guessed).</param>
/// <param name="PeriodMonths">The period's month count (3/6/9/12), null with the same rule.</param>
/// <param name="PresentationUnit"><c>Sunum Para Birimi</c> verbatim (e.g. <c>1.000 TL</c>).</param>
/// <param name="UnitScale">The store's scale vocabulary for the presentation unit
/// (<c>units</c>/<c>thousands</c>/<c>millions</c>), null when the unit is unrecognised.</param>
/// <param name="Currency">The ISO currency (<c>TRY</c>) or null when unrecognised.</param>
/// <param name="ConsolidationType"><c>Finansal Tablo Niteliği</c> verbatim (e.g. <c>Konsolide</c>).</param>
public sealed record KapPublicReportHeader(
    string? CompanyName,
    string? ReportTitle,
    DateOnly? SubmittedAt,
    string? SubmissionType,
    int? Year,
    int? Period,
    string? PeriodType,
    int? PeriodMonths,
    string? PresentationUnit,
    string? UnitScale,
    string? Currency,
    string? ConsolidationType)
{
    /// <summary>True when the report is a full fiscal year (<c>Yıllık</c>).</summary>
    public bool IsAnnual =>
        string.Equals(PeriodType, KapPublicReportDecoder.AnnualPeriodType, StringComparison.Ordinal);
}

/// <summary>One statement table found in the report, typed by its own captured heading.</summary>
/// <param name="Heading">The table's heading label, verbatim (e.g. <c>Nakit Akış Tablosu (Dolaylı Yöntem)</c>).</param>
/// <param name="StatementType">The canonical statement type the heading selects
/// (<c>IS</c>/<c>BS</c>/<c>CF</c>/<c>EQ</c>), null when the heading is not recognised —
/// then no row is decoded from the table and the miss is a counted finding.</param>
/// <param name="TableClass">The table's captured CSS class (e.g. <c>tbl_general_role_520003</c>) —
/// recorded for traceability; it is <b>not</b> the typing mechanism (the heading is).</param>
/// <param name="Columns">The value columns in header order (<see cref="KapPublicColumn.Index"/>
/// is the position, never the column's physical order in the DOM).</param>
public sealed record KapPublicTable(
    string Heading,
    string? StatementType,
    string TableClass,
    IReadOnlyList<KapPublicColumn> Columns);

/// <summary>
/// One value column of a statement table, resolved from its <b>column header</b> (the capture
/// marks value cells with a <c>col-order-class-N</c> CSS class whose <c>N - 4</c> is the
/// header position — verified on all four captured tables: the balance sheet carries 2
/// headers/2 value columns, the income statement 4/4, the cash-flow 2/2). The period is
/// therefore read from the header, never assumed from the cell's position.
/// </summary>
/// <param name="Index">The 0-based header position this column maps to.</param>
/// <param name="OrderClass">The captured <c>col-order-class-N</c> number of its value cells.</param>
/// <param name="Verbatim">The header text, whitespace-normalized and verbatim otherwise.</param>
/// <param name="Role">The header's role text with the dates removed (e.g. <c>Cari Dönem</c>,
/// <c>Önceki Dönem 3 Aylık</c>), null when no date could be read.</param>
/// <param name="IsCurrent">True when the role says <c>Cari</c> (the reporting period).</param>
/// <param name="PeriodStart">The range start (null on an instant header such as a balance sheet's).</param>
/// <param name="PeriodEnd">The period end — the last date in the header.</param>
/// <param name="DurationMonths">The range length in months (null for an instant header).</param>
/// <param name="Decoded">True when this column's values were decoded into the payload.</param>
/// <param name="SkipReason">Why a column was not decoded (<see cref="KapPublicReportReasons.SubPeriodColumn"/>),
/// null when decoded.</param>
public sealed record KapPublicColumn(
    int Index,
    int OrderClass,
    string Verbatim,
    string? Role,
    bool IsCurrent,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    int? DurationMonths,
    bool Decoded,
    string? SkipReason);

/// <summary>
/// One decoded value with its full provenance — the answer to "which table, which label,
/// which column, which cell" (TC-MDF-107 (vi)). <see cref="Value"/> is in the report's own
/// presentation unit (thousands for the captured ASELS report); the store applies the scale
/// (02 §3.1).
/// </summary>
/// <param name="StatementType">The selected statement type (<c>IS</c>/<c>BS</c>/<c>CF</c>).</param>
/// <param name="Section">The statement's section, resolved <b>before</b> the label map
/// (<see cref="KapPublicSections"/>) — the reason two identically-labelled cash-flow lines in
/// different sections never collide.</param>
/// <param name="TableHeading">The source table's heading, verbatim.</param>
/// <param name="Label">The source label, verbatim.</param>
/// <param name="ItemCode">The canonical code the label mapped to, or its
/// <c>OTHER_{section}_{label}</c> passthrough code.</param>
/// <param name="Value">The decoded value in the report's presentation unit.</param>
/// <param name="FromAttribute">True when the value was read from the cell's raw
/// <c>title=""</c> integer — the only read path. A cell with no raw attribute is a counted
/// drop, never decoded from the displayed text, so every decoded line carries <c>true</c>
/// here; the field is kept so a provenance record still states its source explicitly.</param>
/// <param name="RawAttribute">The cell's <c>title</c> attribute verbatim (null when absent).</param>
/// <param name="DisplayedText">The cell's displayed text, verbatim (the cross-check only).</param>
/// <param name="ColumnHeader">The period column header this value came from, verbatim.</param>
/// <param name="PeriodEndDate">The period end the column header declared.</param>
/// <param name="PeriodStartDate">The range start the column header declared (null on an instant
/// column such as a balance sheet's) — the <b>true declared duration</b> of the figure
/// (TKT-foundation-090). A cumulative interim figure therefore states the span it covers
/// (<c>01.01.2026 – 30.06.2026</c>) on the decode result. This is decode-result provenance: it
/// is available to the decode's own consumers and tests, and is <b>not</b> carried into
/// <c>StatementFact</c>/<c>FinancialStatement</c> or the served financials payload.</param>
/// <param name="DurationMonths">The declared range length in months (null for an instant
/// column, and always null together with <paramref name="PeriodStartDate"/>) — the same duration
/// the column was selected on.</param>
/// <param name="RowOrder">The row's ordinal within its table (0-based) — the last leg of the trace.</param>
public sealed record KapPublicLine(
    string StatementType,
    string Section,
    string TableHeading,
    string Label,
    string ItemCode,
    decimal Value,
    bool FromAttribute,
    string? RawAttribute,
    string DisplayedText,
    string ColumnHeader,
    DateOnly PeriodEndDate,
    DateOnly? PeriodStartDate,
    int? DurationMonths,
    int RowOrder);

/// <summary>
/// One entry of the honesty ledger: a named, counted reason plus a bounded verbatim excerpt.
/// A finding is a <b>record</b>, never a repair — a value the decoder could not attribute is
/// dropped (honest-null), a display text that disagrees with its raw attribute is reported
/// while the attribute wins, and a duplicate label that disagrees with itself is quarantined
/// rather than resolved by order.
/// </summary>
/// <param name="ReasonCode">One of <see cref="KapPublicReportReasons"/>.</param>
/// <param name="Count">How many occurrences this entry accounts for (cells, rows or columns).</param>
/// <param name="Detail">A bounded verbatim excerpt naming the table/label/cell concerned.</param>
/// <param name="StatementType">The statement type concerned, when the finding is scoped to one.</param>
/// <param name="Label">The source label concerned, when the finding is scoped to one.</param>
public sealed record KapPublicFinding(
    string ReasonCode,
    int Count,
    string Detail,
    string? StatementType = null,
    string? Label = null);

/// <summary>One entry of the HTML label → canonical chart-of-accounts map (<c>02</c> §6.1 extended to
/// this source's label space — the canonical <see cref="StatementMapper"/> is untouched).
/// <b>Provenance is part of the entry</b> (TC-MDF-107 (i)): a spelling is only in this map
/// because a committed capture carried it, and the entry names which capture.
/// <para>
/// <b>The scope dimension (TKT-foundation-093).</b> A caption alone is not always a line: the
/// balance sheet repeats <c>Ticari Alacaklar</c>, <c>Ticari Borçlar</c>,
/// <c>Kiralama İşlemlerinden Borçlar</c> and <c>Ertelenmiş Gelirler (…)</c> in its current and
/// non-current blocks with different values, the income statement repeats
/// <c>Ana Ortaklık Payları</c> in its profit-or-loss and other-comprehensive-income parts, and
/// the cash-flow statement repeats captions across its operating/investing/financing blocks.
/// <see cref="Scope"/> names the row scope such a caption belongs to, resolved <b>before</b> the
/// map exactly as the cash-flow section is (<see cref="KapPublicScopes"/>); <c>null</c> is an
/// <b>unqualified</b> entry, which matches in every scope — so every pre-093 entry is
/// unchanged and the 088/090/091/092 goldens hold.
/// </para>
/// </summary>
/// <param name="StatementType">The statement type the spelling belongs to — the same label in
/// another statement is a different line and is never mapped by this entry.</param>
/// <param name="Label">The source label, verbatim.</param>
/// <param name="ItemCode">The canonical item code it collapses onto.</param>
/// <param name="Provenance">The committed capture that supplied the spelling.</param>
/// <param name="Scope">The row scope this entry is qualified to
/// (<see cref="KapPublicScopes"/>), or null when the caption means the same line in every
/// scope.</param>
public sealed record KapPublicLabelRule(
    string StatementType,
    string Label,
    string ItemCode,
    string Provenance,
    string? Scope = null);

/// <summary>
/// The section vocabulary the decoder assigns <b>before</b> label mapping. Sections are the
/// reason a duplicate label is not a duplicate line: the captured cash-flow statement repeats
/// labels across its operating/investing/financing blocks (<c>Alınan Temettüler</c>,
/// <c>Ödenen Faiz</c>, <c>Vergi İadeleri (Ödemeleri)</c>, …), and the income statement repeats
/// <c>Dönem Karı (Zararı)</c> from the cash-flow statement — assigning the section first keeps
/// every legitimate value attributable (TC-MDF-065 doctrine applied to this source).
/// </summary>
public static class KapPublicSections
{
    /// <summary>Balance sheet.</summary>
    public const string BalanceSheet = "BS";

    /// <summary>Income statement.</summary>
    public const string IncomeStatement = "IS";

    /// <summary>Cash-flow — operating activities block.</summary>
    public const string CashFlowOperating = "CF_OPERATING";

    /// <summary>Cash-flow — investing activities block.</summary>
    public const string CashFlowInvesting = "CF_INVESTING";

    /// <summary>Cash-flow — financing activities block.</summary>
    public const string CashFlowFinancing = "CF_FINANCING";

    /// <summary>Cash-flow — the closing block (fx effect, net change, opening and closing cash).</summary>
    public const string CashFlowSummary = "CF_SUMMARY";

    /// <summary>Statement of changes in equity.</summary>
    public const string Equity = "EQ";
}

/// <summary>
/// The <b>row scope</b> vocabulary the decoder resolves <b>before</b> label mapping — the
/// second half of the "section before label" rule (TKT-foundation-093, <c>01</c> §10.9.19 /
/// <c>02</c> §6.1). A scope is what makes a repeated caption a different line:
/// <list type="bullet">
/// <item><b>balance sheet</b> — the current / non-current block, read from the capture's own
/// <c>DÖNEN VARLIKLAR</c> / <c>DURAN VARLIKLAR</c> / <c>KISA VADELİ YÜKÜMLÜLÜKLER</c> /
/// <c>UZUN VADELİ YÜKÜMLÜLÜKLER</c> anchor rows. A row whose block is undetermined (before any
/// anchor, or inside the equity block, which has no current/non-current split) carries
/// <c>null</c>: a scope-qualified entry <b>never</b> matches it and the miss is a counted
/// <c>BLOCK_UNDETERMINED</c> finding — never a guessed block;</item>
/// <item><b>income statement</b> — the profit-or-loss part and the other-comprehensive-income
/// part, split at the capture's own <c>Kar veya Zararda Yeniden Sınıflandırılmayacaklar</c>
/// anchor;</item>
/// <item><b>cash-flow statement</b> — the same operating/investing/financing/summary block the
/// section carries (<see cref="KapPublicSections"/>), so a caption repeated across cash-flow
/// blocks stays attributable.</item>
/// </list>
/// </summary>
public static class KapPublicScopes
{
    /// <summary>Balance sheet — the current block (<c>DÖNEN VARLIKLAR</c> / <c>KISA VADELİ YÜKÜMLÜLÜKLER</c>).</summary>
    public const string Current = "CURRENT";

    /// <summary>Balance sheet — the non-current block (<c>DURAN VARLIKLAR</c> / <c>UZUN VADELİ YÜKÜMLÜLÜKLER</c>).</summary>
    public const string NonCurrent = "NONCURRENT";

    /// <summary>Income statement — the profit-or-loss part.</summary>
    public const string ProfitOrLoss = "PL";

    /// <summary>Income statement — the other-comprehensive-income part.</summary>
    public const string OtherComprehensiveIncome = "OCI";
}

/// <summary>
/// The honesty ledger's closed reason set (each one is counted on every decode; none is a
/// repair). Names are stable so a caller can assert on them.
/// </summary>
public static class KapPublicReportReasons
{
    /// <summary>A statement table carried no column headers — its values cannot be attributed
    /// to a period, so every one of them is an honest-null drop (the captured
    /// <c>Özkaynaklar Değişim Tablosu</c> is this case: an equity matrix whose column headers
    /// are not <c>context-header</c> cells).</summary>
    public const string NoColumnHeaders = "NO_COLUMN_HEADERS";

    /// <summary>A value cell's column class pointed outside the header set — dropped.</summary>
    public const string ColumnUnattributed = "COLUMN_UNATTRIBUTED";

    /// <summary>The table's observed <c>col-order-class</c> values are not contiguous from its
    /// first value column, or their count differs from its <c>context-header</c> count. Headers
    /// are paired to value columns by ordinal position, so an inconsistent pairing is never
    /// trusted — the table is skipped whole rather than a shifted column being emitted.</summary>
    public const string ColumnHeaderMismatch = "COLUMN_HEADER_MISMATCH";

    /// <summary>A cell's raw attribute was not a number — dropped, never coerced to zero.</summary>
    public const string ValueUnattributed = "VALUE_UNATTRIBUTED";

    /// <summary>A value row carried no readable label — its cells are dropped.</summary>
    public const string LabelUnattributed = "LABEL_UNATTRIBUTED";

    /// <summary>The cell's displayed text disagrees with its raw <c>title</c> attribute. The
    /// attribute is the pinned source, so it wins — the disagreement is reported, never
    /// silently overridden.</summary>
    public const string DisplayTextMismatch = "DISPLAY_TEXT_MISMATCH";

    /// <summary>The same canonical code appeared twice in one statement row with
    /// <b>unequal</b> values — quarantined (both values retained in the finding), never a
    /// silent latest-wins (TC-MDF-065).</summary>
    public const string DuplicateLabelConflict = "DUPLICATE_LABEL_CONFLICT";

    /// <summary>A report metadata field was absent or unreadable — left null, never defaulted.</summary>
    public const string HeaderFieldMissing = "HEADER_FIELD_MISSING";

    /// <summary><c>Sunum Para Birimi</c> declared a unit the decoder does not recognise —
    /// scale/currency stay null so the store refuses rather than scaling wrongly.</summary>
    public const string PresentationUnitUnrecognised = "PRESENTATION_UNIT_UNRECOGNISED";

    /// <summary>A table heading selected no statement type — the table is skipped whole.</summary>
    public const string UnknownTableHeading = "UNKNOWN_TABLE_HEADING";

    /// <summary>A value column covers less than the report's own period (the captured income
    /// statement's 3-month Q2 columns inside a 6-month report). Its values are named and
    /// skipped: the store's natural key cannot distinguish two durations ending on the same
    /// date, and silently overwriting the year-to-date row would be a silent rewrite.</summary>
    public const string SubPeriodColumn = "SUB_PERIOD_COLUMN";

    /// <summary>The report's <c>Periyot</c> is absent or outside the evidenced set, so the
    /// period type is unknown — surfaced as null rather than guessed.</summary>
    public const string PeriodTypeUnknown = "PERIOD_TYPE_UNKNOWN";

    /// <summary>The report's declared <c>Periyot</c> duration disagrees with the durations its
    /// own column headers declare (e.g. a 6-month report self-declaring <c>Periyot:4</c>). The
    /// statement period type then fails safe to <c>Q</c> — an FY row is never emitted on a
    /// contradicted declaration.</summary>
    public const string PeriodDurationMismatch = "PERIOD_DURATION_MISMATCH";

    /// <summary>
    /// The report declares a period (<c>Periyot</c>) but <b>no</b> column declares a readable
    /// date range, so nothing in the body attests what the figures cover — the cumulative-vs-
    /// discrete semantics are undetermined (TKT-foundation-090). Counted once per report; the
    /// statement period type fails safe to <c>Q</c> (never <c>FY</c> on an unattested
    /// declaration), so a half-year's balance sheet can never enter a DCF's annual basis.
    /// </summary>
    public const string PeriodSemanticsUndetermined = "PERIOD_SEMANTICS_UNDETERMINED";

    /// <summary>A statement table decoded lines but <b>none</b> mapped to a canonical code —
    /// only <c>OTHER_*</c> passthroughs. A label-spelling drift must be named, not left as a
    /// clean ledger with no metric input.</summary>
    public const string NoCanonicalLines = "NO_CANONICAL_LINES";

    /// <summary>A presentation label repeated inside one statement with a <b>different</b>
    /// value on an <c>OTHER_*</c> passthrough code (the balance sheet's current and
    /// non-current <c>Ticari Alacaklar</c>, <c>ARA TOPLAM</c>, …; the income statement's
    /// profit-allocation and comprehensive-income <c>Ana Ortaklık Payları</c>). Both values are
    /// kept — the second under a documented <c>{code}_n</c> suffix — because these are two
    /// real lines, not one ambiguous concept. The counted finding records each one; the
    /// <c>DUPLICATE_LABEL_CONFLICT</c> quarantine stays reserved for <b>canonical</b> codes
    /// (TKT-foundation-088).</summary>
    public const string DuplicateLabelDisambiguated = "DUPLICATE_LABEL_DISAMBIGUATED";

    /// <summary>
    /// A valued balance-sheet row's <b>block could not be determined</b> (it sits before any
    /// <c>DÖNEN VARLIKLAR</c>/<c>DURAN VARLIKLAR</c>/<c>KISA VADELİ YÜKÜMLÜLÜKLER</c>/<c>UZUN
    /// VADELİ YÜKÜMLÜLÜKLER</c> anchor, or inside the equity block, which has no current/non-current
    /// split) while the map carries a scope-qualified entry for its caption. No qualified entry
    /// matched it, so the row is left as its section-scoped <c>OTHER_*</c> passthrough —
    /// <b>never</b> guessed into a block. Counted once per such row, naming the row order and
    /// the caption (TKT-foundation-093).
    /// </summary>
    public const string BlockUndetermined = "BLOCK_UNDETERMINED";
}
