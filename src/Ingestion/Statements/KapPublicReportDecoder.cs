using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Degerli.Ingestion.Sources;

namespace Degerli.Ingestion.Statements;

/// <summary>
/// Decodes the KAP <b>public-site</b> excel-HTML financial report
/// (<c>GET /tr/api/notification/export/excel/{disclosureIndex}</c>) onto the canonical
/// statement model (TKT-foundation-081; 01 v1.5.55 §10.9.19, FR-MDF-002/003/008/012).
/// <para>
/// <b>A source-shaped decoder beside the VYK one, not a replacement.</b> The VYK/XBRL decode
/// paths (<see cref="KapXbrlStatementDecoder"/>, <see cref="VykStatementDecoder"/>) are
/// untouched: this source is HTML tables, so it carries its own label→canonical map — but the
/// map targets the <b>same</b> canonical chart of accounts (<see cref="StatementMapper"/> is
/// untouched, <c>02</c> §6.1's one-definition doctrine), and the guard is that only spellings
/// a committed capture actually carries may enter it, each entry naming its capture
/// (<see cref="KapPublicLabelRule.Provenance"/>).
/// </para>
/// <para>
/// <b>What the capture pins (ASELS 2026-06, public disclosure 1643141, bounded trim).</b>
/// </para>
/// <list type="number">
/// <item><b>The raw integer is an attribute.</b> Every value cell carries the raw integer in
/// <c>title=""</c> beside the displayed Turkish-formatted text (<c>title="39468926"</c> beside
/// <c>39.468.926</c>). The attribute is the <b>only</b> read path; the displayed text is a
/// cross-check whose disagreement is a recorded finding, never a silent override. The display
/// grammar is still understood (through the shared
/// <see cref="KapXbrlStatementDecoder.ParseTurkishNumber"/>) for that cross-check only: a cell
/// with no raw attribute is a counted <c>VALUE_UNATTRIBUTED</c> drop, never decoded from its
/// displayed text.</item>
/// <item><b>Columns are periods by header, never by position.</b> Value cells are marked with
/// a <c>col-order-class-N</c> CSS class; <c>N</c> minus the table's first value column is the
/// column's position in the header row (verified on all four captured tables: balance sheet 2
/// headers / 2 value columns, income statement 4/4, cash-flow 2/2, equity statement 0/52 —
/// the last is why an equity table's values are an honest drop, not a default). The alignment
/// is independently corroborated by the capture's own cash ladder: the cash-flow statement's
/// current-period closing cash equals the balance sheet's current cash (<c>39.468.926</c>) and
/// its current-period opening cash equals the balance sheet's prior cash
/// (<c>34.251.653</c>).</item>
/// <item><b>Section before label.</b> A label only means something inside its statement and,
/// for the cash-flow statement, inside its operating/investing/financing block: the capture
/// repeats <c>Dönem Karı (Zararı)</c> from the income statement and repeats labels across the
/// cash-flow blocks. Sections are therefore resolved first
/// (<see cref="KapPublicSections"/>), and an unmapped label's passthrough code is
/// section-scoped — so a legitimate value is never discarded as a "duplicate".</item>
/// <item><b>The period type is surfaced.</b> Per the user's pinned CF basis (annual FCF +
/// labeled interims) the report's <c>Periyot</c> decodes to its period type and to the store's
/// <c>FY</c>/<c>Q</c> code, so a half-year report can never be consumed as an annual.</item>
/// </list>
/// <para>
/// <b>Cumulative vs discrete semantics (TKT-foundation-090, verified from the committed
/// interim body).</b> A Turkish interim FR's main column is <b>cumulative — year-to-date</b>:
/// the MİGROS 2026-06 capture declares its income-statement and cash-flow <c>Cari Dönem</c>
/// columns as <c>01.01.2026 - 30.06.2026</c>. The income-statement (and other-comprehensive-
/// income) table additionally carries the discrete quarter as a separate
/// <c>3 Aylık 01.04.2026 - 30.06.2026</c> column; the cash-flow table in that filing carries
/// <b>only</b> the two declared-range columns (current and prior) — no quarterly sibling. The
/// decoder therefore decodes the column whose <b>declared duration</b> is the report's own
/// (6 months here), skips the discrete quarter wherever a table has one as
/// <c>SUB_PERIOD_COLUMN</c>, and types the fact <c>Q</c> at the column's own period end — the
/// figure is the year-to-date value <b>labelled as the interim it is</b> at decode time, never a
/// discrete quarter's cell and never an annual. Each decoded line carries the declared
/// <c>PeriodStartDate</c>/<c>DurationMonths</c> it was decoded under — the filing's own span,
/// preserved <b>on the decode result</b> and available to the decode's own consumers and tests;
/// <c>null</c> on an instant (balance-sheet) line means the filing declared no range. That span
/// is <b>not</b> stored or served: <c>StatementFact</c> and the <c>FinancialStatement</c> entity
/// carry no span field, so a served interim is still identified by the store's
/// <c>FY</c>/<c>Q</c> vocabulary (the served-label gap is routed to a display/consumer ticket).
/// Because the store's vocabulary is only <c>FY</c>/<c>Q</c>, a period an annual declaration
/// claims but no column attests is <b>undetermined</b>: it is counted
/// <c>PERIOD_SEMANTICS_UNDETERMINED</c> and fails safe to <c>Q</c> (a twelve-month duration is
/// the rule's only route to <c>FY</c>).
/// </para>
/// <para>
/// <b>Honesty rules, mechanised.</b> Every decoded line carries table + label + column + row
/// provenance; a value that cannot be attributed is dropped (honest-null) and counted — never
/// zero-filled and never guessed; a duplicate label collapses when its values agree and
/// quarantines the statement row when they disagree (<c>CONFLICTING_VALUE</c>, both values
/// retained); a header the decoder cannot read stays null with a counted reason instead of a
/// default.
/// </para>
/// </summary>
public static class KapPublicReportDecoder
{
    /// <summary>The lane's evidence class (FR-MDF-008) — never the VYK lane's <c>kap</c>.</summary>
    public const string SourceRef = "kap-public";

    /// <summary>
    /// The committed capture every spelling in <see cref="LabelMap"/> is proven by
    /// (<c>RealSourceCatalog.KapPublicReport</c>): ASELS 2026-06 H1 FR, filed 04.08.2026,
    /// public disclosure index 1643141, bounded-trimmed to 630 of 3,484 value cells.
    /// </summary>
    public const string CaptureName = "kap-public-report-1643141";

    /// <summary>
    /// The TÜPRAŞ 2025 annual capture (<c>RealSourceCatalog.KapPublicReportTuprs</c>, public
    /// disclosure index <b>1554106</b>, filed 06.02.2026 18:34:29, bounded trim) — the
    /// balance sheet the debt/cash core is evidenced from (TKT-foundation-088).
    /// </summary>
    public const string TuprsAnnualCaptureName = "kap-public-report-1554106";

    /// <summary>
    /// The ASELSAN 2025 annual capture (<c>RealSourceCatalog.KapPublicReportAselsAnnual</c>,
    /// public disclosure index <b>1561039</b>, filed 24.02.2026 18:27:36, bounded trim) — the
    /// annual income statement the EBIT/NI/pretax/tax core is evidenced from
    /// (TKT-foundation-088; the body TKT-foundation-081 recorded as still owed).
    /// </summary>
    public const string AselsAnnualCaptureName = "kap-public-report-1561039";

    /// <summary>
    /// The MİGROS 2026-06 interim capture (<c>RealSourceCatalog.KapPublicReportMgros</c>,
    /// public disclosure index <b>1648240</b>, filed 11.08.2026 21:43:53, bounded trim) — the
    /// second filer whose IS/BS the live walk stored nothing for (the diagnosis input).
    /// </summary>
    public const string MgrosInterimCaptureName = "kap-public-report-1648240";

    /// <summary>
    /// The THYAO 2025 annual capture (<c>RealSourceCatalog.KapPublicReportThyao</c>, public
    /// disclosure index <b>1565996</b>, filed 04.03.2026 18:55:17, bounded trim) — the
    /// industrial FR whose report carries the <b>full</b> IS/BS/CF taxonomy row set (the
    /// statements view's rows: trade receivables/payables, inventories, PP&amp;E, right-of-use
    /// assets, goodwill, lease liabilities, deferred revenue, the equity block and the CF
    /// detail lines) and the <c>1.000.000 TL</c> presentation unit (TKT-foundation-093).
    /// </summary>
    public const string ThyaoAnnualCaptureName = "kap-public-report-1565996";

    /// <summary>
    /// Every committed capture this decoder's label map may cite as provenance
    /// (<see cref="KapPublicLabelRule.Provenance"/>). A spelling outside this set is
    /// un-evidenced by construction, and the acceptance suite checks each entry against the
    /// body it names.
    /// </summary>
    public static IReadOnlyList<string> CaptureNames { get; } =
    [
        CaptureName,
        TuprsAnnualCaptureName,
        AselsAnnualCaptureName,
        MgrosInterimCaptureName,
        ThyaoAnnualCaptureName,
    ];

    /// <summary>The report period type of a full fiscal year.</summary>
    public const string AnnualPeriodType = "Yıllık";

    /// <summary>The default version a decode declares (the store assigns the integer sequence).</summary>
    public const string AsReportedVersion = "as_reported";

    /// <summary>
    /// The fallback position of a table's first value column: the capture's rows start with a
    /// context cell, the field-title cell and a footnote cell, so the first value cell is the
    /// fourth <c>&lt;td&gt;</c> and is marked <c>col-order-class-4</c>. The decoder derives the
    /// offset per table from the observed classes — this constant is only the fallback.
    /// </summary>
    public const int FirstValueColumnClass = 4;

    /// <summary>The attribute that carries a value cell's raw, unformatted integer.</summary>
    public const string RawValueAttribute = "title";

    private const string StatementTableSelector = "table.financial-table";
    private const string HeaderTableSelector = "table.financial-header-table";
    private const string ContextHeaderSelector = "td.context-header";
    private const string ValueCellSelector = "td.taxonomy-context-value";
    private const string TitleCellSelector = "td.taxonomy-field-title";
    private const string OrderClassPrefix = "col-order-class-";

    private static readonly Regex DatePattern = new(@"(\d{1,2})\.(\d{1,2})\.(\d{4})", RegexOptions.CultureInvariant);
    private static readonly Regex PresentationUnitPattern = new(@"^(?<number>[\d.,]+)\s*(?<unit>\S+)$", RegexOptions.CultureInvariant);

    /// <summary>
    /// The HTML label → canonical chart-of-accounts map (<c>02</c> §6.1 extended to this
    /// source's label space). <b>Every entry is proven by a committed capture</b> — the
    /// spelling is in the capture the entry names in <see cref="KapPublicLabelRule.Provenance"/>
    /// (one of <see cref="CaptureNames"/>), and the entry records that fact — and every
    /// target is a canonical item code from <see cref="StatementMapper.CanonicalCodes"/>.
    /// <para>
    /// <b>The recorded gap this map deliberately leaves (TKT-foundation-088, routed).</b> The
    /// balance sheet's current portion of long-term borrowings
    /// (<c>Uzun Vadeli Borçlanmaların Kısa Vadeli Kısımları</c>, 6.916.519 thousand TL in the
    /// TÜPRAŞ FY2025 capture) is a <b>separate presentation line</b> from the taxonomy's own
    /// <c>Kısa Vadeli Borçlanmalar</c> (4.934.360). A 1:1 concept map cannot merge two lines
    /// into one canonical code — doing so would either overwrite one value with the other or
    /// manufacture an unreported sum — so the current portion stays an
    /// <c>OTHER_BS_…</c> companion and the DCF's <c>debt</c> (= <c>ST_DEBT</c> +
    /// <c>LT_DEBT</c>) is the two borrowings lines only. Aggregating presentation lines into
    /// a canonical code is a modelling decision, not a decode: it is recorded on the ticket
    /// and routed to the planner/user, never guessed here.
    /// </para>
    /// <para>
    /// The cash-flow <c>OCF</c> basis is the statement's own operating total
    /// <c>İŞLETME FAALİYETLERİNDEN NAKİT AKIŞLARI</c> — the figure <b>after</b> tax paid and
    /// other operating payments (user decision 2026-10-26, DR-2; <c>CanonicalMetrics</c>
    /// documents OCF as "taken as the KAP cash-flow statement reports it"). The before-tax
    /// subtotal <c>Faaliyetlerden Elde Edilen Nakit Akışları</c> is <b>not</b> mapped: it stays
    /// a decoded, labelled <c>OTHER_CF_OPERATING_*</c> companion, so the display keeps both
    /// measures instead of equating them.
    /// </para>
    /// <para>
    /// Other deliberate non-mappings, each an honest gap rather than a guess: the
    /// tangible/intangible acquisition children stay <c>OTHER_*</c> because the combined
    /// <c>Maddi ve Maddi Olmayan …</c> row is <c>CAPEX</c> and mapping the children too would
    /// double-count it, and <b>no share-count line is mapped at all</b> (share counts are
    /// TKT-foundation-065's decision, never consumed or relabelled here).
    /// </para>
    /// </summary>
    private static readonly IReadOnlyList<KapPublicLabelRule> Rules =
    [
        // ---- Income statement ------------------------------------------------------
        new("IS", "Hasılat", "REV", CaptureName),
        new("IS", "Satışların Maliyeti", "COGS", CaptureName),
        new("IS", "TİCARİ FAALİYETLERDEN BRÜT KAR (ZARAR)", "GROSS_PROFIT", CaptureName),

        // The annual income statement's own subtotal ladder (evidenced by the ASELSAN 2025
        // Yıllık capture, disclosure 1561039). Each target is the canonical code the VYK
        // concept map already assigns to the equivalent IFRS element
        // (`ProfitLossFromOperatingActivities` → EBIT, `AdministrativeExpense` → SGA,
        // `FinanceCosts` → FIN_EXP, `ProfitLossBeforeTax` → PRETAX_INC,
        // `IncomeTaxExpenseContinuingOperations` → TAX_EXP, `ProfitLoss` → NI), so the one
        // chart-of-accounts definition (`02` §6.1) is not forked by this source.
        new("IS", "ESAS FAALİYET KARI (ZARARI)", "EBIT", AselsAnnualCaptureName),
        new("IS", "Genel Yönetim Giderleri", "SGA", AselsAnnualCaptureName),
        new("IS", "Finansman Giderleri", "FIN_EXP", AselsAnnualCaptureName),
        new("IS", "SÜRDÜRÜLEN FAALİYETLER VERGİ ÖNCESİ KARI (ZARARI)", "PRETAX_INC", AselsAnnualCaptureName),
        new("IS", "Sürdürülen Faaliyetler Vergi (Gideri) Geliri", "TAX_EXP", AselsAnnualCaptureName),
        // The statement's own bottom line, verbatim and in its reported case (the cash-flow
        // statement carries the mixed-case `Dönem Karı (Zararı)` spelling — two entries, two
        // statements, never one spelling standing in for the other).
        new("IS", "DÖNEM KARI (ZARARI)", "NI", AselsAnnualCaptureName),

        // The income statement's remaining families (TKT-foundation-093): R&D and the
        // other-operating / investing / financing income-and-expense pairs, kept as distinct
        // lines (a single "net" code would have to manufacture a sum the report never states),
        // plus the TMS 29 net monetary position gain/loss. Evidenced by the THYAO 2025 Yıllık
        // body; `Araştırma ve Geliştirme Giderleri` by the ASELSAN annual body.
        new("IS", "Araştırma ve Geliştirme Giderleri", "RND", AselsAnnualCaptureName),
        new("IS", "Esas Faaliyetlerden Diğer Gelirler", "OP_INCOME_OTHER", ThyaoAnnualCaptureName),
        new("IS", "Esas Faaliyetlerden Diğer Giderler", "OP_EXPENSE_OTHER", ThyaoAnnualCaptureName),
        new("IS", "Yatırım Faaliyetlerinden Gelirler", "INVEST_INCOME", ThyaoAnnualCaptureName),
        new("IS", "Yatırım Faaliyetlerinden Giderler", "INVEST_EXPENSE", ThyaoAnnualCaptureName),
        new("IS", "Finansman Gelirleri", "FIN_INCOME", ThyaoAnnualCaptureName),
        new("IS", "Net Parasal Pozisyon Kazançları (Kayıpları)", "NET_MONETARY_GL", ThyaoAnnualCaptureName),

        // ---- Balance sheet ---------------------------------------------------------
        new("BS", "Nakit ve Nakit Benzerleri", "CASH", CaptureName),

        // The debt/cash core the DCF baseline's `debt` and `cash` inputs need
        // (FR-VAL-001, TKT-foundation-088), evidenced by the TÜPRAŞ 2025 Yıllık capture
        // (disclosure 1554106). Financial borrowings only: the taxonomy's own
        // short-term and long-term borrowings lines. Trade payables (`Ticari Borçlar`) and
        // every operating liability are deliberately NOT mapped to debt, and the current
        // portion line `Uzun Vadeli Borçlanmaların Kısa Vadeli Kısımları` stays an OTHER_*
        // companion — see the class doc comment's recorded gap.
        new("BS", "Kısa Vadeli Borçlanmalar", "ST_DEBT", TuprsAnnualCaptureName),
        new("BS", "Uzun Vadeli Borçlanmalar", "LT_DEBT", TuprsAnnualCaptureName),
        new("BS", "TOPLAM ÖZKAYNAKLAR", "TOTAL_EQUITY", TuprsAnnualCaptureName),
        new("BS", "TOPLAM DÖNEN VARLIKLAR", "CUR_ASSETS", TuprsAnnualCaptureName),
        new("BS", "TOPLAM KISA VADELİ YÜKÜMLÜLÜKLER", "CUR_LIAB", TuprsAnnualCaptureName),

        // The statements view's remaining balance-sheet families (TKT-foundation-093),
        // evidenced by the THYAO 2025 Yıllık body — the industrial FR carrying the full
        // taxonomy row set — except where another committed body is the one that carries the
        // caption (noted per entry). Each caption means the same line in every block; the
        // repeated current/non-current families live in `ScopedRules` instead.
        new("BS", "Stoklar", "INVENTORY", ThyaoAnnualCaptureName),
        new("BS", "Maddi Duran Varlıklar", "PPE_NET", ThyaoAnnualCaptureName),
        new("BS", "Kullanım Hakkı Varlıkları", "ROU_ASSET", ThyaoAnnualCaptureName),
        new("BS", "Maddi Olmayan Duran Varlıklar", "INTANGIBLES_NET", ThyaoAnnualCaptureName),
        new("BS", "Şerefiye", "GOODWILL", ThyaoAnnualCaptureName),
        new("BS", "Ödenmiş Sermaye", "PAID_IN_CAPITAL", ThyaoAnnualCaptureName),
        new("BS", "Geçmiş Yıllar Karları veya Zararları", "RETAINED_EARNINGS", ThyaoAnnualCaptureName),
        // The VYK concept map's existing `MINORITY_INT` code, now wired to its KAP spelling.
        new("BS", "Kontrol Gücü Olmayan Paylar", "MINORITY_INT", ThyaoAnnualCaptureName),
        new("BS", "Ana Ortaklığa Ait Özkaynaklar", "EQUITY_PARENT", ThyaoAnnualCaptureName),
        new("BS", "TOPLAM VARLIKLAR", "TOTAL_ASSETS", ThyaoAnnualCaptureName),
        new("BS", "TOPLAM YÜKÜMLÜLÜKLER", "TOTAL_LIAB", ThyaoAnnualCaptureName),
        new("BS", "Diğer Dönen Varlıklar", "CUR_ASSETS_OTHER", ThyaoAnnualCaptureName),
        new("BS", "Diğer Kısa Vadeli Yükümlülükler", "CUR_LIAB_OTHER", ThyaoAnnualCaptureName),
        // `Diğer Duran Varlıklar` is carried by the TÜPRAŞ and ASELSAN annual bodies, not THYAO.
        new("BS", "Diğer Duran Varlıklar", "NONCUR_ASSETS_OTHER", TuprsAnnualCaptureName),
        new("BS", "Diğer Uzun Vadeli Yükümlülükler", "NONCUR_LIAB_OTHER", ThyaoAnnualCaptureName),

        // ---- Cash-flow statement (the 051 deliverable) -----------------------------
        // The statement's own operating total, after tax paid and other operating payments
        // (captured 15.206.440 current / 18.013.334 comparative, thousand TL; user decision
        // 2026-10-26 — see the class doc comment above). The before-tax subtotal
        // `Faaliyetlerden Elde Edilen Nakit Akışları` stays an OTHER_CF_OPERATING_* companion.
        new("CF", "İŞLETME FAALİYETLERİNDEN NAKİT AKIŞLARI", "OCF", CaptureName),
        // The combined tangible + intangible acquisition outflow — its two children sum to
        // exactly this line (5.513.477 + 11.677.360 = 17.190.837, comparative column).
        new("CF", "Maddi ve Maddi Olmayan Duran Varlıkların Alımından Kaynaklanan Nakit Çıkışları", "CAPEX", CaptureName),
        new("CF", "İşletme Sermayesinde Gerçekleşen Değişimler", "ΔWC", CaptureName),
        new("CF", "Amortisman ve İtfa Gideri İle İlgili Düzeltmeler", "DEPR_AMORT", CaptureName),
        // The cash-flow statement's own starting profit line (the income statement carries the
        // same label in the full report — which is exactly why the section is resolved first).
        new("CF", "Dönem Karı (Zararı)", "NI", CaptureName),
        // The cash ladder's closing balance; it equals the balance sheet's `Nakit ve Nakit
        // Benzerleri` for the same period end (39.468.926 on 30.06.2026) — the capture's own
        // cross-statement invariant, asserted by the acceptance suite.
        new("CF", "DÖNEM SONU NAKİT VE NAKİT BENZERLERİ", "CASH", CaptureName),
    ];

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> RulesByStatement =
        Rules
            .GroupBy(r => r.StatementType, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<string, string>)g.ToDictionary(
                    r => r.Label,
                    r => r.ItemCode,
                    StringComparer.Ordinal),
                StringComparer.Ordinal);

    /// <summary>
    /// The <b>scope-qualified</b> entries (TKT-foundation-093): captions the capture really
    /// repeats across a statement's row scopes with different values, each entry naming the
    /// scope it is qualified to. A row whose resolved scope is null (an undetermined
    /// balance-sheet block) never matches one of these — it falls through to the section-scoped
    /// <c>OTHER_*</c> passthrough and is counted <c>BLOCK_UNDETERMINED</c>, never guessed.
    /// <para>
    /// Every spelling and every provenance is capture-verified exactly as an unqualified entry
    /// is (the TC-MDF-107 (i)/117 (f) invariant, generalized by TC-MDF-122): the THYAO 2025
    /// Yıllık body carries both blocks of the lease/deferred-revenue/deferred-tax families, the
    /// ASELSAN 2025 Yıllık body carries both blocks of trade receivables and payables, and the
    /// cash-flow captions are cited from the filer that carries them.
    /// </para>
    /// </summary>
    private static readonly IReadOnlyList<KapPublicLabelRule> ScopedRules =
    [
        // ---- Balance sheet: current / non-current blocks ----------------------------
        // Evidenced in both blocks by the ASELSAN 2025 Yıllık capture: 38.859.628 current /
        // 87.944.574 non-current trade receivables and 37.964.626 current / 24.432 non-current
        // trade payables (thousand TL, current column).
        new("BS", "Ticari Alacaklar", "ST_TRADE_RECV", AselsAnnualCaptureName, KapPublicScopes.Current),
        new("BS", "Ticari Alacaklar", "LT_TRADE_RECV", AselsAnnualCaptureName, KapPublicScopes.NonCurrent),
        new("BS", "Ticari Borçlar", "ST_TRADE_PAY", AselsAnnualCaptureName, KapPublicScopes.Current),
        new("BS", "Ticari Borçlar", "LT_TRADE_PAY", AselsAnnualCaptureName, KapPublicScopes.NonCurrent),
        // Evidenced in both blocks by THYAO 2025: 86.152 current / 552.824 non-current lease
        // liabilities and 151.257 current / 3.240 non-current deferred revenue (million TL).
        new("BS", "Kiralama İşlemlerinden Borçlar", "ST_LEASE_LIAB", ThyaoAnnualCaptureName, KapPublicScopes.Current),
        new("BS", "Kiralama İşlemlerinden Borçlar", "LT_LEASE_LIAB", ThyaoAnnualCaptureName, KapPublicScopes.NonCurrent),
        new("BS", "Ertelenmiş Gelirler (Müşteri Sözleşmelerinden Doğan Yükümlülüklerin Dışında Kalanlar)", "ST_DEFERRED_REV", ThyaoAnnualCaptureName, KapPublicScopes.Current),
        new("BS", "Ertelenmiş Gelirler (Müşteri Sözleşmelerinden Doğan Yükümlülüklerin Dışında Kalanlar)", "LT_DEFERRED_REV", ThyaoAnnualCaptureName, KapPublicScopes.NonCurrent),

        // ---- Income statement: profit-or-loss / other comprehensive income -----------
        // THYAO 2025 repeats `Ana Ortaklık Payları`: 118.208 in the profit-or-loss part and
        // 241.065 in the other-comprehensive-income part. Only the profit-or-loss occurrence is
        // the parent's net income; the OCI occurrence stays a labelled `OTHER_*` line.
        new("IS", "Ana Ortaklık Payları", "NI_PARENT", ThyaoAnnualCaptureName, KapPublicScopes.ProfitOrLoss),

        // ---- Cash-flow statement: block-qualified detail lines -----------------------
        new("CF", "Değer Düşüklüğü (İptali) İle İlgili Düzeltmeler", "IMPAIRMENT", ThyaoAnnualCaptureName, KapPublicSections.CashFlowOperating),
        new("CF", "Karşılıklar İle İlgili Düzeltmeler", "PROVISIONS", ThyaoAnnualCaptureName, KapPublicSections.CashFlowOperating),
        new("CF", "Parasal Pozisyon Kazanç ve Kayıplarıyla İlgili Düzeltmeler", "MONETARY_ADJ", TuprsAnnualCaptureName, KapPublicSections.CashFlowOperating),
        new("CF", "Maddi ve Maddi Olmayan Duran Varlıkların Satışından Kaynaklanan Nakit Girişleri", "ASSET_SALE_PROCEEDS", ThyaoAnnualCaptureName, KapPublicSections.CashFlowInvesting),
        new("CF", "Bağlı Ortaklıkların Kontrolünün Elde Edilmesine Yönelik Alışlara İlişkin Nakit Çıkışları", "ACQUISITION_CASH", TuprsAnnualCaptureName, KapPublicSections.CashFlowInvesting),
        new("CF", "Pay ve Diğer Özkaynağa Dayalı Araçların İhracından Kaynaklanan Nakit Girişleri", "EQUITY_ISSUE_CASH", ThyaoAnnualCaptureName, KapPublicSections.CashFlowFinancing),
        new("CF", "İşletmenin Kendi Paylarını ve Diğer Özkaynağa Dayalı Araçlarını Almasıyla İlgili Nakit Çıkışları", "BUYBACK", ThyaoAnnualCaptureName, KapPublicSections.CashFlowFinancing),
        new("CF", "Ödenen Temettüler", "DIVIDENDS_PAID", ThyaoAnnualCaptureName, KapPublicSections.CashFlowFinancing),
        new("CF", "Ödenen Faiz", "INTEREST_PAID", ThyaoAnnualCaptureName, KapPublicSections.CashFlowFinancing),
        new("CF", "Borçlanmadan Kaynaklanan Nakit Girişleri", "DEBT_ISSUED", ThyaoAnnualCaptureName, KapPublicSections.CashFlowFinancing),
        new("CF", "Borç Ödemelerine İlişkin Nakit Çıkışları", "DEBT_REPAID", ThyaoAnnualCaptureName, KapPublicSections.CashFlowFinancing),
    ];

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<(string Label, string Scope), string>> ScopedRulesByStatement =
        ScopedRules
            .GroupBy(r => r.StatementType, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<(string, string), string>)g.ToDictionary(
                    r => (r.Label, r.Scope!),
                    r => r.ItemCode),
                StringComparer.Ordinal);

    /// <summary>The label → canonical map, built only from committed-capture labels.</summary>
    public static IReadOnlyList<KapPublicLabelRule> LabelMap => Rules;

    /// <summary>
    /// The <b>scope-qualified</b> half of the map (TKT-foundation-093): every entry whose
    /// <see cref="KapPublicLabelRule.Scope"/> names the row scope it is qualified to (the balance
    /// sheet's current/non-current block, the income statement's profit-or-loss /
    /// other-comprehensive-income part, a cash-flow block). <see cref="LabelMap"/> is the
    /// unqualified half; together they are the whole map.
    /// <para>
    /// <b>Why two projections and not one list.</b> The map is a function of
    /// <c>(statement, label)</c> for an unqualified caption — the invariant TC-MDF-107 (i)/
    /// TC-MDF-117 (f) pin — while a caption that really is two lines must have one entry per
    /// scope. Keeping the qualified entries in their own projection keeps both statements true:
    /// the unqualified map stays a function, and every qualified entry is still evidence-bound
    /// (provenance names one of <see cref="CaptureNames"/> and the body carries the spelling).
    /// </para>
    /// </summary>
    public static IReadOnlyList<KapPublicLabelRule> ScopedLabelMap => ScopedRules;

    /// <summary>
    /// True when the body is this source's excel-HTML report shape. Detection is by <b>shape</b>,
    /// never by the declared content type: the export route serves
    /// <c>application/vnd.ms-excel</c> over plain HTML (01 §10.9.19), so a caller classifying an
    /// <c>UNPARSEABLE_PAYLOAD</c> must ask the body itself.
    /// </summary>
    public static bool LooksLikeReport(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        return body.Contains("financial-table", StringComparison.Ordinal)
            && body.Contains("taxonomy-field-title", StringComparison.Ordinal);
    }

    /// <summary>
    /// The evidenced <c>Periyot</c> → period-type map, or null when the period is not evidenced
    /// (never a guess). Every pair is attested by the committed list captures' own
    /// <c>period</c>/<c>ruleType</c> fields: <c>1 → 3 Aylık</c> and <c>2 → 6 Aylık</c> (ASELS
    /// 2026), <c>3 → 9 Aylık</c> (the market probe's RODRG row) and <c>4 → Yıllık</c> (ASELS
    /// FY2025).
    /// </summary>
    public static string? PeriodTypeFor(int? period) => period switch
    {
        1 => "3 Aylık",
        2 => "6 Aylık",
        3 => "9 Aylık",
        4 => AnnualPeriodType,
        _ => null,
    };

    /// <summary>
    /// The filing's consolidation flag from the header-table field <c>Finansal Tablo Niteliği</c>
    /// (TKT-foundation-093): <c>Konsolide</c> → <c>true</c>, <c>Solo</c> → <c>false</c>, and
    /// anything else — including an absent field — <c>null</c>. The declaration is a header field,
    /// never inferred from the company heading or from the presence of minority interests.
    /// </summary>
    public static bool? ConsolidatedFor(string? consolidationType)
    {
        if (string.IsNullOrWhiteSpace(consolidationType))
        {
            return null;
        }

        var declared = consolidationType.Trim();
        if (string.Equals(declared, "Konsolide", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(declared, "Solo", StringComparison.OrdinalIgnoreCase) ? false : null;
    }

    /// <summary>The period's month count, or null when the period type is unknown.</summary>
    public static int? PeriodMonthsFor(int? period) => period switch
    {
        1 => 3,
        2 => 6,
        3 => 9,
        4 => 12,
        _ => null,
    };

    /// <summary>
    /// The store's period-type code (<c>FY</c>/<c>Q</c>) for a report period. The mapping fails
    /// <b>safe</b>: only an attested annual period (4) yields <c>FY</c>; an unknown period
    /// yields <c>Q</c> as well, because mislabeling an interim as annual would let the DCF
    /// baseline consume a half-year, while mislabeling an annual as an interim only hides it
    /// from that baseline. An unknown period is never silent — it is counted on the report's
    /// findings.
    /// </summary>
    public static string StatementPeriodTypeFor(int? period) =>
        string.Equals(PeriodTypeFor(period), AnnualPeriodType, StringComparison.Ordinal) ? "FY" : "Q";

    /// <summary>
    /// Decodes one report body into the canonical statement payload plus its provenance and
    /// honesty ledger.
    /// </summary>
    /// <param name="html">The excel-HTML report body (the committed capture, or a double of the
    /// same shape).</param>
    /// <param name="symbol">The bound BIST symbol the statements are written for.</param>
    /// <exception cref="SourcePayloadException">The body is not this source's report shape
    /// (<c>UNPARSEABLE_PAYLOAD</c>), so the caller quarantines it rather than writing nothing
    /// silently.</exception>
    public static KapPublicReport Decode(string html, string symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(html);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        if (!LooksLikeReport(html))
        {
            throw new SourcePayloadException(
                html,
                new InvalidDataException("Body is not the KAP public-site excel-HTML financial report shape."));
        }

        IDocument document;
        try
        {
            document = new HtmlParser().ParseDocument(html);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new SourcePayloadException(html, exception);
        }

        var findings = new List<KapPublicFinding>();
        var header = ReadHeader(document, findings);
        var tables = new List<KapPublicTable>();
        var decodes = new List<TableDecode>();
        var lines = new List<KapPublicLine>();
        var groups = new List<Group>();
        var groupsByKey = new Dictionary<string, Group>(StringComparer.Ordinal);

        // Phase 1 — read every table's headings and columns. The whole report is inspected
        // before any value is decoded so the declared period can be cross-checked against the
        // durations the columns actually declare (DR-3), and so a contradicted declaration
        // fails safe to Q for every statement row, balance sheet included.
        foreach (var table in document.QuerySelectorAll(StatementTableSelector))
        {
            var tableClass = TableClassOf(table);
            var heading = ReadHeading(table);
            var statementType = StatementTypeForHeading(heading);
            var baseOrder = FirstValueColumnOf(table);
            var rows = DirectRows(table);
            var columns = ReadColumns(table, baseOrder, header.Period, tableClass, heading, statementType, findings);

            tables.Add(new KapPublicTable(heading, statementType, tableClass, columns ?? []));
            decodes.Add(new TableDecode(tableClass, heading, statementType, rows, columns));
        }

        var declaredMonths = PeriodMonthsFor(header.Period);
        var observedMonths = decodes
            .Where(decode => decode.Columns is not null)
            .SelectMany(decode => decode.Columns!)
            .Where(column => column.DurationMonths is not null)
            .Select(column => column.DurationMonths!.Value)
            .Distinct()
            .OrderBy(months => months)
            .ToList();

        // The report's self-declared Periyot must agree with the durations its own column
        // headers declare. A 6-month report mis-declaring Periyot:4 would otherwise type its
        // instant (balance-sheet) columns as FY and write half-year balance sheets into the
        // DCF baseline's FY filter — silently. Disagreement is counted and fails safe to Q.
        var durationMismatch = declaredMonths is not null
            && observedMonths.Count > 0
            && !observedMonths.Contains(declaredMonths.Value);
        if (durationMismatch)
        {
            findings.Add(Finding(
                KapPublicReportReasons.PeriodDurationMismatch,
                1,
                $"Periyot '{header.Period?.ToString(CultureInfo.InvariantCulture) ?? "absent"}' declares "
                + $"{declaredMonths} month(s), but the column headers declare "
                + $"{string.Join(", ", observedMonths)} month range(s) — the statement period type fails safe to Q "
                + "(never FY on a contradicted declaration)."));
        }

        // The other half of the same guard (TKT-foundation-090): a report that declares a period
        // its own columns do not attest has undetermined cumulative-vs-discrete semantics — no
        // range anywhere means nothing in the body says what the figures cover. An annual
        // declaration is then as unproven as any other, and FY is never emitted on it: a
        // half-year's balance sheet typed FY would let its debt/cash into the DCF's annual basis.
        var semanticsUndetermined = declaredMonths is not null && observedMonths.Count == 0;
        if (semanticsUndetermined)
        {
            findings.Add(Finding(
                KapPublicReportReasons.PeriodSemanticsUndetermined,
                1,
                $"Periyot '{header.Period?.ToString(CultureInfo.InvariantCulture) ?? "absent"}' declares "
                + $"{declaredMonths} month(s), but no column header declares a readable date range — what the "
                + "figures cover is undetermined, so the statement period type fails safe to Q (never FY on an "
                + "unattested declaration)."));
        }

        var periodType = durationMismatch || semanticsUndetermined
            ? "Q"
            : StatementPeriodTypeFor(header.Period);

        // Phase 2 — decode the value cells of every table whose heading and columns resolved.
        foreach (var decode in decodes)
        {
            var (tableClass, heading, statementType, rows, columns) = decode;

            if (statementType is null)
            {
                findings.Add(Finding(
                    KapPublicReportReasons.UnknownTableHeading,
                    CountValuedCells(rows),
                    $"{tableClass}: heading '{heading}' selects no statement type — the table was skipped whole."));
                continue;
            }

            // A header/column pairing that is not contiguous and count-equal was already
            // counted COLUMN_HEADER_MISMATCH: never pair a shifted column, skip the table whole.
            if (columns is null)
            {
                continue;
            }

            if (columns.Count == 0)
            {
                // No column headers means no period can be attributed to any value: every
                // valued cell is an honest-null drop, never defaulted to a period.
                findings.Add(Finding(
                    KapPublicReportReasons.NoColumnHeaders,
                    CountValuedCells(rows),
                    $"{tableClass} '{heading}': no column headers — {CountValuedCells(rows)} valued cell(s) "
                    + "could not be attributed to a period and were dropped (never zero-filled).",
                    statementType));
                continue;
            }

            var section = DefaultSection(statementType);
            var skipped = new Dictionary<int, int>();
            var rowOrder = -1;
            var statementLineCount = 0;
            var canonicalLineCount = 0;

            // The row-scope state (TKT-foundation-093): resolved from the capture's own block
            // anchors <b>before</b> the label map, exactly as the cash-flow section is. A
            // separate income-statement table headed `Diğer Kapsamlı Gelir Tablosu` starts in
            // the OCI part; a combined statement starts in profit-or-loss and flips at its own
            // `Kar veya Zararda Yeniden Sınıflandırılmayacaklar` anchor.
            string? block = null;
            string? incomeScope = statementType == "IS"
                && string.Equals(heading, "Diğer Kapsamlı Gelir Tablosu", StringComparison.Ordinal)
                    ? KapPublicScopes.OtherComprehensiveIncome
                    : statementType == "IS" ? KapPublicScopes.ProfitOrLoss : null;

            foreach (var row in rows)
            {
                // Anchors and section-heading rows carry no value cells but they are what the
                // scopes are read from — so the label is read before the value-cell guard.
                var labelCell = row.QuerySelector(TitleCellSelector);
                var rowLabel = labelCell is null ? null : LabelOf(labelCell);

                switch (statementType)
                {
                    case "BS":
                        block = BalanceSheetBlockFor(rowLabel, block);
                        break;
                    case "IS":
                        incomeScope = IncomeStatementScopeFor(rowLabel, incomeScope);
                        break;
                    case "CF":
                        section = CashFlowSectionFor(rowLabel, section);
                        break;
                }

                var valueCells = row.QuerySelectorAll(ValueCellSelector);
                if (valueCells.Length == 0)
                {
                    continue;
                }

                rowOrder++;
                var label = rowLabel;
                var scope = statementType switch
                {
                    "BS" => block,
                    "IS" => incomeScope,
                    "CF" => section,
                    _ => null,
                };
                var blockUndetermined = false;

                foreach (var cell in valueCells)
                {
                    var orderClass = OrderClassOf(cell);
                    var column = orderClass is null
                        ? null
                        : columns.FirstOrDefault(c => c.OrderClass == orderClass.Value);

                    if (column is null)
                    {
                        if (IsValued(cell))
                        {
                            findings.Add(Finding(
                                KapPublicReportReasons.ColumnUnattributed,
                                1,
                                $"{tableClass} '{heading}' row '{label}': value cell column class "
                                + $"{orderClass?.ToString(CultureInfo.InvariantCulture) ?? "absent"} matches no column "
                                + "header — dropped (never zero-filled).",
                                statementType,
                                label));
                        }

                        continue;
                    }

                    if (!column.Decoded)
                    {
                        if (IsValued(cell))
                        {
                            skipped[column.Index] = skipped.GetValueOrDefault(column.Index) + 1;
                        }

                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(label))
                    {
                        if (IsValued(cell))
                        {
                            findings.Add(Finding(
                                KapPublicReportReasons.LabelUnattributed,
                                1,
                                $"{tableClass} '{heading}' column '{column.Verbatim}': a valued cell carries no "
                                + "readable label — dropped.",
                                statementType));
                        }

                        continue;
                    }

                    var cellValue = ReadCell(
                        cell, tableClass, heading, column.Verbatim, label, statementType, findings);
                    if (cellValue is null)
                    {
                        continue;
                    }

                    var (value, fromAttribute, raw, displayed) = cellValue.Value;
                    var itemCode = MapCode(statementType, section, scope, label);

                    // The row is one a block-qualified entry could have placed, but its block is
                    // undetermined: no qualified entry matched and the value stays an honest
                    // passthrough. Counted once for the row below, never guessed into a block.
                    blockUndetermined |= scope is null && HasScopedRule(statementType, label);
                    // Decoded is only true when the column's header resolved a period end; the
                    // null-forgiving access records that invariant in one place (DR-8).
                    var periodEnd = column.PeriodEnd!.Value;
                    var key = string.Join(
                        '|',
                        periodType,
                        periodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        statementType);

                    if (!groupsByKey.TryGetValue(key, out var group))
                    {
                        group = new Group(statementType, periodEnd, periodType, header.SubmittedAt);
                        groupsByKey[key] = group;
                        groups.Add(group);
                    }

                    if (group.Add(itemCode, value, label, findings, out var storedCode) == GroupOutcome.Collapsed)
                    {
                        // An equal duplicate is one line, not two: the provenance record follows
                        // the payload, so a collapsed value is not counted twice here either.
                        continue;
                    }

                    lines.Add(new KapPublicLine(
                        statementType,
                        section,
                        heading,
                        label,
                        storedCode,
                        value,
                        fromAttribute,
                        raw,
                        displayed,
                        column.Verbatim,
                        periodEnd,
                        column.PeriodStart,
                        column.DurationMonths,
                        rowOrder));

                    statementLineCount++;
                    if (StatementMapper.CanonicalCodes.Contains(storedCode))
                    {
                        canonicalLineCount++;
                    }
                }

                if (blockUndetermined)
                {
                    findings.Add(Finding(
                        KapPublicReportReasons.BlockUndetermined,
                        1,
                        $"{tableClass} '{heading}' row {rowOrder.ToString(CultureInfo.InvariantCulture)} '{label}': "
                        + "the balance sheet carries a block-qualified entry for this caption but the row's "
                        + "current/non-current block could not be determined from the capture's own anchors — the "
                        + "value stays a section-scoped OTHER_* passthrough, never guessed into a block.",
                        statementType,
                        label));
                }
            }

            foreach (var (columnIndex, count) in skipped)
            {
                var column = columns[columnIndex];

                // SkipReason is non-null exactly when the column was not decoded (DR-8): an
                // unreadable period is COLUMN_UNATTRIBUTED, a shorter range is SUB_PERIOD_COLUMN
                // — and the detail is worded for the reason it actually carries.
                var reason = column.SkipReason!;
                var detail = reason == KapPublicReportReasons.ColumnUnattributed
                    ? $"{tableClass} '{heading}' column '{column.Verbatim}': the header carries no readable "
                        + $"period — {count} value(s) named and dropped (never defaulted to a period)."
                    : $"{tableClass} '{heading}' column '{column.Verbatim}': covers "
                        + $"{column.DurationMonths?.ToString(CultureInfo.InvariantCulture) ?? "?"} month(s) against the "
                        + $"report's own {header.PeriodMonths?.ToString(CultureInfo.InvariantCulture) ?? "?"} — {count} "
                        + "value(s) named and skipped, never merged into the report-period column sharing its end date.";

                findings.Add(Finding(reason, count, detail, statementType));
            }

            // A statement that decoded lines but no canonical one is a label-spelling drift:
            // every row is an OTHER_* passthrough, so metrics silently see nothing. Name it.
            if (statementLineCount > 0 && canonicalLineCount == 0)
            {
                findings.Add(Finding(
                    KapPublicReportReasons.NoCanonicalLines,
                    statementLineCount,
                    $"{tableClass} '{heading}': none of the {statementLineCount} decoded line(s) mapped to a "
                    + "canonical code — only OTHER_* passthroughs, so no metric can consume this statement.",
                    statementType));
            }
        }

        return new KapPublicReport(
            symbol,
            SourceRef,
            header,
            tables,
            lines,
            findings,
            new StatementsPayload(
                SourceRef,
                symbol,
                header.UnitScale,
                header.Currency,
                groups.Select(g => g.ToFact()).ToList()));
    }

    // ---------------------------------------------------------------------------------
    // Header / metadata
    // ---------------------------------------------------------------------------------

    /// <summary>Reads the report's identity block; every absent field is a counted finding.</summary>
    private static KapPublicReportHeader ReadHeader(IDocument document, List<KapPublicFinding> findings)
    {
        var headings = document.QuerySelectorAll("h1")
            .Select(h => Normalize(h.TextContent))
            .Where(text => text.Length > 0)
            .ToList();

        var submittedRaw = ReadField(document, "Gönderim Tarihi");
        var submittedAt = ParseFirstDate(submittedRaw);
        if (submittedAt is null)
        {
            findings.Add(FieldMissing("Gönderim Tarihi", submittedRaw));
        }

        var submissionType = ReadField(document, "Bildirim Tipi");
        if (string.IsNullOrWhiteSpace(submissionType))
        {
            findings.Add(FieldMissing("Bildirim Tipi", submissionType));
        }

        var yearRaw = ReadField(document, "Yıl");
        var year = ParseInt(yearRaw);
        if (year is null)
        {
            findings.Add(FieldMissing("Yıl", yearRaw));
        }

        var periodRaw = ReadField(document, "Periyot");
        var period = ParseInt(periodRaw);
        if (period is null)
        {
            findings.Add(FieldMissing("Periyot", periodRaw));
        }

        var periodType = PeriodTypeFor(period);
        if (periodType is null)
        {
            findings.Add(Finding(
                KapPublicReportReasons.PeriodTypeUnknown,
                1,
                $"Periyot '{periodRaw ?? "absent"}' is outside the evidenced set (1..4) — the period type stays null, "
                + "and the statements are typed Q (never FY on a guess)."));
        }

        var headerTable = document.QuerySelector(HeaderTableSelector);
        var presentationUnit = ReadHeaderTableField(headerTable, "Sunum Para Birimi");
        var consolidation = ReadHeaderTableField(headerTable, "Finansal Tablo Niteliği");
        var (scale, currency) = ParsePresentationUnit(presentationUnit, findings);

        return new KapPublicReportHeader(
            headings.Count > 0 ? headings[0] : null,
            headings.Count > 1 ? headings[1] : null,
            submittedAt,
            string.IsNullOrWhiteSpace(submissionType) ? null : submissionType!.Trim(),
            year,
            period,
            periodType,
            PeriodMonthsFor(period),
            presentationUnit,
            scale,
            currency,
            consolidation);
    }

    private static KapPublicFinding FieldMissing(string field, string? read) =>
        new(
            KapPublicReportReasons.HeaderFieldMissing,
            1,
            $"{field}: '{read ?? "absent"}' could not be read — left null, never defaulted.");

    /// <summary>Reads a <c>Key:value</c> field from the export's flat metadata block.</summary>
    private static string? ReadField(IDocument document, string field)
    {
        foreach (var element in document.QuerySelectorAll("div, span, td, th, p"))
        {
            if (element.Children.Length > 0)
            {
                continue;
            }

            var text = Normalize(element.TextContent);
            var prefix = field + ":";
            if (text.StartsWith(prefix, StringComparison.Ordinal))
            {
                return text[prefix.Length..].Trim();
            }
        }

        return null;
    }

    /// <summary>Reads one <c>title/value</c> row of the export's header table.</summary>
    private static string? ReadHeaderTableField(IElement? headerTable, string field)
    {
        if (headerTable is null)
        {
            return null;
        }

        foreach (var row in DirectRows(headerTable))
        {
            var cells = row.Children.Where(c => string.Equals(c.LocalName, "td", StringComparison.Ordinal)).ToList();
            if (cells.Count >= 2 && string.Equals(Normalize(cells[0].TextContent), field, StringComparison.Ordinal))
            {
                return Normalize(cells[1].TextContent);
            }
        }

        return null;
    }

    /// <summary>
    /// Parses <c>Sunum Para Birimi</c> (the capture's <c>1.000 TL</c>) into the store's scale
    /// vocabulary and an ISO currency. An unrecognised unit leaves both null — the store then
    /// refuses the payload rather than scaling it wrongly (02 §3.1, TC-MDF-055).
    /// </summary>
    private static (string? Scale, string? Currency) ParsePresentationUnit(
        string? presentationUnit,
        List<KapPublicFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(presentationUnit))
        {
            findings.Add(Finding(
                KapPublicReportReasons.PresentationUnitUnrecognised,
                1,
                "Sunum Para Birimi is absent — scale and currency stay null (the store refuses rather than guesses)."));
            return (null, null);
        }

        var match = PresentationUnitPattern.Match(presentationUnit.Trim());
        if (!match.Success)
        {
            findings.Add(Finding(
                KapPublicReportReasons.PresentationUnitUnrecognised,
                1,
                $"Sunum Para Birimi '{presentationUnit}' does not read as '<multiplier> <currency>'."));
            return (null, null);
        }

        decimal multiplier;
        try
        {
            multiplier = KapXbrlStatementDecoder.ParseTurkishNumber(match.Groups["number"].Value);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
        {
            multiplier = 0m;
        }

        var scale = multiplier switch
        {
            1m => "units",
            1_000m => "thousands",
            1_000_000m => "millions",
            _ => null,
        };

        var currency = match.Groups["unit"].Value.ToUpperInvariant() switch
        {
            "TL" or "TRY" or "₺" => "TRY",
            "USD" or "$" => "USD",
            "EUR" or "€" => "EUR",
            _ => null,
        };

        if (scale is null || currency is null)
        {
            var unrecognised = (scale, currency) switch
            {
                (null, null) => "multiplier and currency",
                (null, _) => "multiplier",
                _ => "currency",
            };

            findings.Add(Finding(
                KapPublicReportReasons.PresentationUnitUnrecognised,
                1,
                $"Sunum Para Birimi '{presentationUnit}' declares an unrecognised {unrecognised} — both stay "
                + "null (the store refuses rather than scales wrongly)."));

            // Neither half is usable: an unrecognised unit refuses the whole declaration,
            // exactly like the absent-field branch above — never a partial (scale, null)
            // pair that would let the store scale a value it cannot label.
            return (null, null);
        }

        return (scale, currency);
    }

    // ---------------------------------------------------------------------------------
    // Tables and columns
    // ---------------------------------------------------------------------------------

    private static string TableClassOf(IElement table) =>
        string.Join(' ', table.ClassList.Where(c => c.StartsWith("tbl_", StringComparison.Ordinal)));

    /// <summary>The table's own heading label (the capture's abstract title row).</summary>
    private static string ReadHeading(IElement table)
    {
        foreach (var row in DirectRows(table))
        {
            foreach (var panel in row.QuerySelectorAll("table.taxonomy-title-panel"))
            {
                if (!panel.ClassList.Contains("taxonomy-abstract-title"))
                {
                    continue;
                }

                return Normalize(panel.QuerySelector("div.gwt-Label")?.TextContent);
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Selects the statement type from the table's <b>heading</b> — never from the export's
    /// table class (which is a taxonomy-version artefact). An unrecognised heading selects
    /// nothing and the table is skipped whole with a counted reason.
    /// </summary>
    private static string? StatementTypeForHeading(string heading)
    {
        if (heading.Contains("Nakit Akış", StringComparison.Ordinal))
        {
            return "CF";
        }

        if (heading.Contains("Finansal Durum", StringComparison.Ordinal)
            || heading.Contains("Bilanço", StringComparison.Ordinal))
        {
            return "BS";
        }

        if (heading.Contains("Kar veya Zarar", StringComparison.Ordinal)
            || heading.Contains("Gelir Tablosu", StringComparison.Ordinal))
        {
            return "IS";
        }

        if (heading.Contains("Özkaynak", StringComparison.Ordinal))
        {
            return "EQ";
        }

        return null;
    }

    /// <summary>
    /// The table's first value column's <c>col-order-class</c> number, observed from its value
    /// cells (fallback <see cref="FirstValueColumnClass"/>). This is what turns a cell's CSS
    /// class into a header position without assuming the physical order.
    /// </summary>
    private static int FirstValueColumnOf(IElement table)
    {
        var orders = table.QuerySelectorAll(ValueCellSelector)
            .Select(OrderClassOf)
            .Where(order => order is not null)
            .Select(order => order!.Value)
            .ToList();

        return orders.Count == 0 ? FirstValueColumnClass : orders.Min();
    }

    /// <summary>
    /// Reads the table's column headers into columns, resolving each header's role, dates and
    /// duration, and deciding which columns the report's own period covers. Headers are paired to
    /// value columns by <b>ordinal position</b>, guarded by a count/contiguity check: a table
    /// whose <c>col-order-class</c> values are not contiguous from <paramref name="baseOrder"/>
    /// or whose distinct count differs from its <c>context-header</c> count is never paired at
    /// all — the mismatch is counted (<c>COLUMN_HEADER_MISMATCH</c>) and <c>null</c> is returned
    /// so the caller skips the table whole instead of emitting a shifted column.
    /// </summary>
    private static List<KapPublicColumn>? ReadColumns(
        IElement table,
        int baseOrder,
        int? reportPeriod,
        string tableClass,
        string heading,
        string? statementType,
        List<KapPublicFinding> findings)
    {
        var headers = table.QuerySelectorAll(ContextHeaderSelector);
        if (headers.Length == 0)
        {
            return [];
        }

        if (statementType is not null)
        {
            var observedOrders = table.QuerySelectorAll(ValueCellSelector)
                .Select(OrderClassOf)
                .Where(order => order is not null)
                .Select(order => order!.Value)
                .Distinct()
                .OrderBy(order => order)
                .ToList();

            // Distinct count must equal the header count, and the classes must be exactly the
            // contiguous run baseOrder..baseOrder+headerCount-1. Anything else means the two
            // independently ordered lists cannot be trusted to pair one-to-one.
            if (observedOrders.Count != headers.Length
                || !observedOrders.SequenceEqual(Enumerable.Range(baseOrder, headers.Length)))
            {
                findings.Add(Finding(
                    KapPublicReportReasons.ColumnHeaderMismatch,
                    CountValuedCells(DirectRows(table)),
                    $"{tableClass} '{heading}': {headers.Length} context-header cell(s) but value cells declare "
                    + $"{observedOrders.Count} distinct col-order-class value(s) "
                    + $"({string.Join(", ", observedOrders)}) — headers are paired to columns by ordinal position, so "
                    + "an inconsistent pairing is not trusted: the table was skipped whole (never a shifted column).",
                    statementType));
                return null;
            }
        }

        var drafts = headers
            .Select(header => ParseHeader(Normalize(header.TextContent)))
            .ToList();

        // The report's own duration: its declared period, or — when the period is not attested —
        // the longest declared range in the table (deterministic, never a guess of a value).
        var reportMonths = PeriodMonthsFor(reportPeriod)
            ?? drafts.Where(d => d.DurationMonths is not null)
                .Select(d => d.DurationMonths!.Value)
                .DefaultIfEmpty(0)
                .Max();

        var columns = new List<KapPublicColumn>(drafts.Count);
        for (var index = 0; index < drafts.Count; index++)
        {
            var draft = drafts[index];
            var decoded = draft.PeriodEnd is not null
                && (draft.DurationMonths is null || draft.DurationMonths == reportMonths);

            columns.Add(new KapPublicColumn(
                index,
                baseOrder + index,
                draft.Verbatim,
                draft.Role,
                draft.IsCurrent,
                draft.PeriodStart,
                draft.PeriodEnd,
                draft.DurationMonths,
                decoded,
                decoded
                    ? null
                    : draft.PeriodEnd is null
                        ? KapPublicReportReasons.ColumnUnattributed
                        : KapPublicReportReasons.SubPeriodColumn));
        }

        return columns;
    }

    /// <summary>
    /// Parses one column header: the role text (dates removed), whether it is the current
    /// period, and its date or date range. A header with no readable date cannot bound a period.
    /// </summary>
    private static (string Verbatim, string? Role, bool IsCurrent, DateOnly? PeriodStart, DateOnly? PeriodEnd, int? DurationMonths)
        ParseHeader(string verbatim)
    {
        var dates = DatePattern.Matches(verbatim)
            .Select(match => TryDate(match, out var date) ? date : (DateOnly?)null)
            .ToList();

        var role = Normalize(DatePattern.Replace(verbatim, " "))
            .Trim(' ', '-', '–', '—');
        role = Normalize(role);

        if (dates.Count == 0 || dates.Any(d => d is null))
        {
            return (verbatim, string.IsNullOrWhiteSpace(role) ? null : role, IsCurrentRole(role), null, null, null);
        }

        var start = dates.Count > 1 ? dates[0]!.Value : (DateOnly?)null;
        var end = dates[^1]!.Value;
        int? duration = start is null
            ? null
            : ((end.Year - start.Value.Year) * 12) + (end.Month - start.Value.Month) + 1;

        return (verbatim, string.IsNullOrWhiteSpace(role) ? null : role, IsCurrentRole(role), start, end, duration);
    }

    private static bool IsCurrentRole(string? role) =>
        role is not null && role.StartsWith("Cari", StringComparison.Ordinal);

    // ---------------------------------------------------------------------------------
    // Sections and codes
    // ---------------------------------------------------------------------------------

    private static string DefaultSection(string statementType) => statementType switch
    {
        "CF" => KapPublicSections.CashFlowOperating,
        "IS" => KapPublicSections.IncomeStatement,
        "BS" => KapPublicSections.BalanceSheet,
        _ => KapPublicSections.Equity,
    };

    /// <summary>
    /// Resolves the cash-flow block a row belongs to from the capture's own aggregate rows. The
    /// section is decided <b>before</b> the label map, which is what keeps repeated labels
    /// (<c>Alınan Temettüler</c>, <c>Ödenen Faiz</c>, <c>Vergi İadeleri (Ödemeleri)</c>, …)
    /// attributable instead of "duplicate".
    /// </summary>
    private static string CashFlowSectionFor(string? label, string current)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return current;
        }

        if (label.Contains("İŞLETME FAALİYETLERİNDEN NAKİT AKIŞLARI", StringComparison.Ordinal))
        {
            return KapPublicSections.CashFlowOperating;
        }

        if (label.Contains("YATIRIM FAALİYETLERİNDEN KAYNAKLANAN NAKİT AKIŞLARI", StringComparison.Ordinal))
        {
            return KapPublicSections.CashFlowInvesting;
        }

        if (label.Contains("FİNANSMAN FAALİYETLERİNDEN NAKİT AKIŞLARI", StringComparison.Ordinal))
        {
            return KapPublicSections.CashFlowFinancing;
        }

        if (label.Contains("NAKİT VE NAKİT BENZERLERİ", StringComparison.Ordinal)
            || label.Contains("ENFLASYON ETKİSİ", StringComparison.Ordinal)
            || label.Contains("YABANCI PARA ÇEVRİM FARKLARININ ETKİSİNDEN ÖNCE", StringComparison.Ordinal))
        {
            return KapPublicSections.CashFlowSummary;
        }

        return current;
    }

    /// <summary>
    /// Resolves the balance-sheet block a row belongs to from the capture's own anchors
    /// (<c>DÖNEN VARLIKLAR</c> / <c>DURAN VARLIKLAR</c> / <c>KISA VADELİ YÜKÜMLÜLÜKLER</c> /
    /// <c>UZUN VADELİ YÜKÜMLÜLÜKLER</c>), exactly as the cash-flow section is resolved. The
    /// equity block (<c>ÖZKAYNAKLAR</c>) has no current/non-current split, so it — and every row
    /// before the first anchor — resolves to <c>null</c>: a block-qualified entry never matches
    /// that, and the miss is counted, never guessed (TKT-foundation-093).
    /// </summary>
    private static string? BalanceSheetBlockFor(string? label, string? current)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return current;
        }

        if (string.Equals(label, "DÖNEN VARLIKLAR", StringComparison.Ordinal)
            || string.Equals(label, "KISA VADELİ YÜKÜMLÜLÜKLER", StringComparison.Ordinal))
        {
            return KapPublicScopes.Current;
        }

        if (string.Equals(label, "DURAN VARLIKLAR", StringComparison.Ordinal)
            || string.Equals(label, "UZUN VADELİ YÜKÜMLÜLÜKLER", StringComparison.Ordinal))
        {
            return KapPublicScopes.NonCurrent;
        }

        if (string.Equals(label, "ÖZKAYNAKLAR", StringComparison.Ordinal))
        {
            return null;
        }

        return current;
    }

    /// <summary>
    /// Resolves the income-statement part a row belongs to: profit-or-loss until the capture's
    /// own <c>Kar veya Zararda Yeniden Sınıflandırılmayacaklar</c> anchor, other-comprehensive-
    /// income from there on (a standalone <c>Diğer Kapsamlı Gelir Tablosu</c> starts there
    /// already). TKT-foundation-093.
    /// </summary>
    private static string? IncomeStatementScopeFor(string? label, string? current)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return current;
        }

        return string.Equals(label, "Kar veya Zararda Yeniden Sınıflandırılmayacaklar", StringComparison.Ordinal)
            ? KapPublicScopes.OtherComprehensiveIncome
            : current;
    }

    /// <summary>True when the map carries a scope-qualified entry for this statement+label.</summary>
    private static bool HasScopedRule(string statementType, string label) =>
        ScopedRulesByStatement.TryGetValue(statementType, out var rules)
        && rules.Keys.Any(key => string.Equals(key.Label, label, StringComparison.Ordinal));

    /// <summary>
    /// Maps a label to its canonical code inside its statement and row scope, or to its
    /// section-scoped <c>OTHER_*</c> passthrough code — recorded and ignored by metrics (the
    /// honest gap). A scope-qualified entry wins only when the row's scope was resolved and
    /// matches it; an unqualified entry matches every scope (TKT-foundation-093).
    /// </summary>
    private static string MapCode(string statementType, string section, string? scope, string label)
    {
        if (scope is not null
            && ScopedRulesByStatement.TryGetValue(statementType, out var scoped)
            && scoped.TryGetValue((label, scope), out var scopedCode))
        {
            return scopedCode;
        }

        return RulesByStatement.TryGetValue(statementType, out var rules) && rules.TryGetValue(label, out var code)
            ? code
            : $"OTHER_{section}_{Slug(label)}";
    }

    /// <summary>Folds a verbatim label into an item-code-safe token (letters/digits, single underscores).</summary>
    private static string Slug(string label)
    {
        var builder = new StringBuilder(label.Length);
        var separator = true;
        foreach (var character in label)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                separator = false;
            }
            else if (!separator)
            {
                builder.Append('_');
                separator = true;
            }
        }

        return builder.ToString().TrimEnd('_');
    }

    // ---------------------------------------------------------------------------------
    // Cells
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Reads one value cell. The raw <c>title</c> attribute is the <b>only</b> read path (the
    /// pinned de-risking discovery); the displayed text is cross-checked against it, and a
    /// disagreement is reported while the attribute wins. A cell with no raw attribute is a
    /// counted <c>VALUE_UNATTRIBUTED</c> drop — the display text is never decoded as a
    /// substitute — and a blank cell is simply not a value. Never zero, never a guess.
    /// </summary>
    private static (decimal Value, bool FromAttribute, string? Raw, string Displayed)? ReadCell(
        IElement cell,
        string tableClass,
        string heading,
        string column,
        string label,
        string statementType,
        List<KapPublicFinding> findings)
    {
        var raw = cell.GetAttribute(RawValueAttribute)
            ?? cell.QuerySelector($"[{RawValueAttribute}]")?.GetAttribute(RawValueAttribute);
        var displayed = Normalize(
            (cell.QuerySelector("div.gwt-Label") ?? cell).TextContent);

        if (!string.IsNullOrWhiteSpace(raw))
        {
            var rawText = raw.Trim();
            if (!decimal.TryParse(rawText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                findings.Add(Finding(
                    KapPublicReportReasons.ValueUnattributed,
                    1,
                    $"{tableClass} '{heading}' row '{label}': raw attribute '{rawText}' is not a number — dropped "
                    + "(never zero-filled, never read from the displayed text as a substitute).",
                    statementType,
                    label));
                return null;
            }

            if (!TryParseDisplayed(displayed, out var shown) || shown != value)
            {
                findings.Add(Finding(
                    KapPublicReportReasons.DisplayTextMismatch,
                    1,
                    $"{tableClass} '{heading}' row '{label}': raw attribute '{rawText}' vs displayed text "
                    + $"'{displayed}' on column '{column}' — the attribute is the value, the disagreement is recorded.",
                    statementType,
                    label));
            }

            return (value, true, rawText, displayed);
        }

        if (string.IsNullOrWhiteSpace(displayed))
        {
            return null; // a blank cell is not a value — nothing to attribute, nothing dropped
        }

        // Non-blank display text with no raw attribute is not a value source: the pinned
        // contract reads the attribute and cross-checks the display, never the reverse.
        findings.Add(Finding(
            KapPublicReportReasons.ValueUnattributed,
            1,
            $"{tableClass} '{heading}' row '{label}' column '{column}': displayed text '{displayed}' is not a "
            + "value source — the cell carries no raw 'title' attribute, so it is dropped (never read from the displayed text).",
            statementType,
            label));
        return null;
    }

    private static bool TryParseDisplayed(string text, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            value = KapXbrlStatementDecoder.ParseTurkishNumber(text);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
        {
            return false;
        }
    }

    private static bool IsValued(IElement cell)
    {
        var raw = cell.GetAttribute(RawValueAttribute)
            ?? cell.QuerySelector($"[{RawValueAttribute}]")?.GetAttribute(RawValueAttribute);

        return !string.IsNullOrWhiteSpace(raw) || !string.IsNullOrWhiteSpace(Normalize(cell.TextContent));
    }

    private static int CountValuedCells(IReadOnlyList<IElement> rows) =>
        rows.Sum(row => row.QuerySelectorAll(ValueCellSelector).Count(IsValued));

    private static int? OrderClassOf(IElement cell)
    {
        foreach (var name in cell.ClassList)
        {
            if (name.StartsWith(OrderClassPrefix, StringComparison.Ordinal)
                && int.TryParse(name[OrderClassPrefix.Length..], CultureInfo.InvariantCulture, out var order))
            {
                return order;
            }
        }

        return null;
    }

    /// <summary>The row label from the cell's title panel (the capture's single <c>gwt-Label</c>).</summary>
    private static string LabelOf(IElement titleCell)
    {
        var label = titleCell.QuerySelector("table.taxonomy-title-panel div.gwt-Label")
            ?? titleCell.QuerySelector("div.gwt-Label");

        return Normalize(label?.TextContent);
    }

    // ---------------------------------------------------------------------------------
    // Small helpers
    // ---------------------------------------------------------------------------------

    /// <summary>The bound on a finding's verbatim excerpt (the 079 / TC-MDF-104 precedent).</summary>
    private const int MaxExcerptChars = 512;

    /// <summary>One table's phase-1 read, held until the report-wide period cross-check.</summary>
    private sealed record TableDecode(
        string TableClass,
        string Heading,
        string? StatementType,
        IReadOnlyList<IElement> Rows,
        List<KapPublicColumn>? Columns);

    /// <summary>
    /// Adds one ledger entry. Every finding goes through here so its verbatim excerpt is
    /// <b>bounded</b> — a pathological cell text cannot grow the ledger without limit.
    /// </summary>
    private static KapPublicFinding Finding(
        string reasonCode,
        int count,
        string detail,
        string? statementType = null,
        string? label = null) =>
        new(reasonCode, count, Excerpt(detail), statementType, label);

    private static string Excerpt(string value) =>
        value.Length <= MaxExcerptChars ? value : string.Concat(value.AsSpan(0, MaxExcerptChars), " …");

    /// <summary>The table's <b>direct</b> rows — never the nested title-panel tables' rows.</summary>
    private static IReadOnlyList<IElement> DirectRows(IElement table)
    {
        var rows = new List<IElement>();
        foreach (var child in table.Children)
        {
            if (string.Equals(child.LocalName, "tr", StringComparison.Ordinal))
            {
                rows.Add(child);
                continue;
            }

            if (!string.Equals(child.LocalName, "tbody", StringComparison.Ordinal)
                && !string.Equals(child.LocalName, "thead", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var row in child.Children)
            {
                if (string.Equals(row.LocalName, "tr", StringComparison.Ordinal))
                {
                    rows.Add(row);
                }
            }
        }

        return rows;
    }

    private static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var space = false;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                space = true;
                continue;
            }

            if (space && builder.Length > 0)
            {
                builder.Append(' ');
            }

            space = false;
            builder.Append(character);
        }

        return builder.ToString();
    }

    /// <summary>The Turkish display formatting, used only in finding details (never as a value).</summary>
    private static string FormatTurkish(decimal value) =>
        value.ToString("#,0.####", CultureInfo.InvariantCulture).Replace(",", ".", StringComparison.Ordinal);

    private static DateOnly? ParseFirstDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = DatePattern.Match(text);
        return TryDate(match, out var date) ? date : null;
    }

    private static bool TryDate(Match match, out DateOnly date)
    {
        date = default;
        if (!match.Success)
        {
            return false;
        }

        return DateOnly.TryParseExact(
            $"{match.Groups[1].Value.PadLeft(2, '0')}.{match.Groups[2].Value.PadLeft(2, '0')}.{match.Groups[3].Value}",
            "dd.MM.yyyy",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }

    private static int? ParseInt(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    /// <summary>
    /// One statement row under (statement type, period end): accumulates the mapped lines with
    /// the duplicate-label rule — equal values collapse, unequal values on a <b>canonical</b>
    /// code conflict (both retained, the row quarantined by the store as
    /// <c>CONFLICTING_VALUE</c>; 02 §1, DR-05 overturned), while unequal values on an
    /// <c>OTHER_*</c> <b>passthrough</b> code are two real presentation lines and are kept
    /// side by side under a counted, disambiguated code (TKT-foundation-088).
    /// </summary>
    /// <summary>What one <see cref="Group.Add"/> did to its statement row.</summary>
    private enum GroupOutcome
    {
        /// <summary>A new line was added.</summary>
        Added,

        /// <summary>An equal value for an existing code — collapsed idempotently, no new line.</summary>
        Collapsed,

        /// <summary>An unequal value for an existing code — retained with the first value and counted.</summary>
        Conflicted,

        /// <summary>An unequal value for a repeatable <c>OTHER_*</c> passthrough code — kept as
        /// its own disambiguated line, counted, and <b>not</b> a conflict.</summary>
        Disambiguated,
    }

    private sealed class Group
    {
        private const string PassthroughPrefix = "OTHER_";

        private readonly DateOnly? _publishedAt;
        private readonly Dictionary<string, string> _labels = new(StringComparer.Ordinal);
        private readonly List<string> _conflicts = [];

        public Group(string statementType, DateOnly periodEndDate, string periodType, DateOnly? publishedAt)
        {
            StatementType = statementType;
            PeriodEndDate = periodEndDate;
            PeriodType = periodType;
            _publishedAt = publishedAt;
        }

        public string StatementType { get; }

        public DateOnly PeriodEndDate { get; }

        public string PeriodType { get; }

        public Dictionary<string, decimal> Lines { get; } = new(StringComparer.Ordinal);

        public GroupOutcome Add(
            string itemCode,
            decimal value,
            string label,
            List<KapPublicFinding> findings,
            out string storedCode)
        {
            storedCode = itemCode;

            if (Lines.TryGetValue(itemCode, out var existing))
            {
                if (existing == value)
                {
                    return GroupOutcome.Collapsed; // an equal duplicate collapses idempotently
                }

                // A passthrough code is a synthesised display code, not a canonical concept:
                // the balance sheet legitimately repeats `Ticari Alacaklar`, `Peşin Ödenmiş
                // Giderler`, `Finansal Yatırımlar` and `ARA TOPLAM` in its current and
                // non-current blocks, and the income statement repeats `Ana Ortaklık Payları`
                // in its profit-allocation and comprehensive-income sections. Those are two
                // real lines. Quarantining the whole statement row for them is what made the
                // first live walk store 0 balance sheets and ~0 income statements
                // (TKT-foundation-088's diagnosis), so the second line is kept under a
                // counted, documented suffix instead — both values retained, nothing guessed
                // and nothing overwritten. Metrics ignore OTHER_* lines either way.
                if (itemCode.StartsWith(PassthroughPrefix, StringComparison.Ordinal))
                {
                    storedCode = Disambiguate(itemCode);
                    Lines[storedCode] = value;
                    _labels[storedCode] = label;

                    findings.Add(Finding(
                        KapPublicReportReasons.DuplicateLabelDisambiguated,
                        1,
                        $"{StatementType} {PeriodEndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}: "
                        + $"'{label}' appears again as {FormatTurkish(value)} where '{itemCode}' already carried "
                        + $"{FormatTurkish(existing)} — a repeated presentation label inside one statement is two "
                        + $"real lines, so the second is kept as '{storedCode}' (counted, never overwritten, and "
                        + "never a conflict: the OTHER_* passthrough is ignored by metrics).",
                        StatementType,
                        label));

                    return GroupOutcome.Disambiguated;
                }

                if (!_conflicts.Contains(itemCode, StringComparer.Ordinal))
                {
                    _conflicts.Add(itemCode);
                }

                findings.Add(Finding(
                    KapPublicReportReasons.DuplicateLabelConflict,
                    1,
                    $"{StatementType} {PeriodEndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}: {itemCode} "
                    + $"from '{_labels[itemCode]}' = {FormatTurkish(existing)} and again from '{label}' = "
                    + $"{FormatTurkish(value)} — both values retained, the statement row is quarantined as "
                    + "CONFLICTING_VALUE (never a silent latest-wins).",
                    StatementType,
                    label));
                return GroupOutcome.Conflicted;
            }

            Lines[itemCode] = value;
            _labels[itemCode] = label;
            return GroupOutcome.Added;
        }

        /// <summary>
        /// The next free <c>{code}_{n}</c> for a repeated passthrough label — deterministic,
        /// so the same report always produces the same codes.
        /// </summary>
        private string Disambiguate(string itemCode)
        {
            for (var ordinal = 2; ; ordinal++)
            {
                var candidate = itemCode + "_" + ordinal.ToString(CultureInfo.InvariantCulture);
                if (!Lines.ContainsKey(candidate))
                {
                    return candidate;
                }
            }
        }

        public StatementFact ToFact() => new(
            PeriodType,
            PeriodEndDate,
            PeriodEndDate.Year,
            StatementType,
            AsReportedVersion,
            null,
            _publishedAt,
            StatementMapper.Map(Lines),
            _conflicts.Count == 0 ? null : _conflicts.ToList());
    }
}
