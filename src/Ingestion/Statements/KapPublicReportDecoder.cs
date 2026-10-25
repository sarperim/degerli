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
/// <c>39.468.926</c>). The attribute is the read path; the displayed text is a cross-check
/// whose disagreement is a recorded finding, never a silent override. The display grammar is
/// still understood (through the shared
/// <see cref="KapXbrlStatementDecoder.ParseTurkishNumber"/>) for that cross-check and for a
/// cell that carries only text.</item>
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
    /// source's label space). <b>Every entry is proven by the committed capture</b> — the
    /// spelling is in <see cref="CaptureName"/>, and the entry records that fact — and every
    /// target is a canonical item code from <see cref="StatementMapper.CanonicalCodes"/>.
    /// <para>
    /// Deliberate non-mappings, each an honest gap rather than a guess: the cash-flow
    /// <c>İŞLETME FAALİYETLERİNDEN NAKİT AKIŞLARI</c> section total is <b>not</b> <c>OCF</c>
    /// (the capture's own <c>Faaliyetlerden Elde Edilen Nakit Akışları</c> line is the
    /// operating subtotal the lane's OCF means, and mapping both would equate two different
    /// measures — the section total stays an <c>OTHER_*</c> companion), the
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

        // ---- Balance sheet ---------------------------------------------------------
        new("BS", "Nakit ve Nakit Benzerleri", "CASH", CaptureName),

        // ---- Cash-flow statement (the 051 deliverable) -----------------------------
        // The operating subtotal the lane's OCF means (captured 20.382.722 current /
        // 21.061.426 comparative, thousand TL).
        new("CF", "Faaliyetlerden Elde Edilen Nakit Akışları", "OCF", CaptureName),
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

    /// <summary>The label → canonical map, built only from committed-capture labels.</summary>
    public static IReadOnlyList<KapPublicLabelRule> LabelMap => Rules;

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
        var lines = new List<KapPublicLine>();
        var groups = new List<Group>();
        var groupsByKey = new Dictionary<string, Group>(StringComparer.Ordinal);
        var periodType = StatementPeriodTypeFor(header.Period);

        foreach (var table in document.QuerySelectorAll(StatementTableSelector))
        {
            var tableClass = TableClassOf(table);
            var heading = ReadHeading(table);
            var statementType = StatementTypeForHeading(heading);
            var baseOrder = FirstValueColumnOf(table);
            var rows = DirectRows(table);
            var columns = ReadColumns(table, baseOrder, header.Period);

            tables.Add(new KapPublicTable(heading, statementType, tableClass, columns));

            if (statementType is null)
            {
                findings.Add(new KapPublicFinding(
                    KapPublicReportReasons.UnknownTableHeading,
                    CountValuedCells(rows),
                    $"{tableClass}: heading '{heading}' selects no statement type — the table was skipped whole."));
                continue;
            }

            if (columns.Count == 0)
            {
                // No column headers means no period can be attributed to any value: every
                // valued cell is an honest-null drop, never defaulted to a period.
                findings.Add(new KapPublicFinding(
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

            foreach (var row in rows)
            {
                var valueCells = row.QuerySelectorAll(ValueCellSelector);
                if (valueCells.Length == 0)
                {
                    continue;
                }

                rowOrder++;
                var labelCell = row.QuerySelector(TitleCellSelector);
                var label = labelCell is null ? null : LabelOf(labelCell);

                if (statementType == "CF")
                {
                    section = CashFlowSectionFor(label, section);
                }

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
                            findings.Add(new KapPublicFinding(
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

                    if (string.IsNullOrWhiteSpace(label) || column.PeriodEnd is null)
                    {
                        if (IsValued(cell))
                        {
                            findings.Add(new KapPublicFinding(
                                KapPublicReportReasons.LabelUnattributed,
                                1,
                                $"{tableClass} '{heading}' column '{column.Verbatim}': a valued cell carries no "
                                + "readable label — dropped.",
                                statementType));
                        }

                        continue;
                    }

                    var cellValue = ReadCell(cell, tableClass, heading, label, statementType, findings);
                    if (cellValue is null)
                    {
                        continue;
                    }

                    var (value, fromAttribute, raw, displayed) = cellValue.Value;
                    var itemCode = MapCode(statementType, section, label);
                    var periodEnd = column.PeriodEnd.Value;
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

                    if (group.Add(itemCode, value, label, findings) == GroupOutcome.Collapsed)
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
                        itemCode,
                        value,
                        fromAttribute,
                        raw,
                        displayed,
                        column.Verbatim,
                        periodEnd,
                        rowOrder));
                }
            }

            foreach (var (columnIndex, count) in skipped)
            {
                var column = columns[columnIndex];
                findings.Add(new KapPublicFinding(
                    column.SkipReason ?? KapPublicReportReasons.SubPeriodColumn,
                    count,
                    $"{tableClass} '{heading}' column '{column.Verbatim}': covers "
                    + $"{column.DurationMonths?.ToString(CultureInfo.InvariantCulture) ?? "?"} month(s) against the "
                    + $"report's own {header.PeriodMonths?.ToString(CultureInfo.InvariantCulture) ?? "?"} — {count} "
                    + "value(s) named and skipped, never merged into the report-period column sharing its end date.",
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
            findings.Add(new KapPublicFinding(
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
            findings.Add(new KapPublicFinding(
                KapPublicReportReasons.PresentationUnitUnrecognised,
                1,
                "Sunum Para Birimi is absent — scale and currency stay null (the store refuses rather than guesses)."));
            return (null, null);
        }

        var match = PresentationUnitPattern.Match(presentationUnit.Trim());
        if (!match.Success)
        {
            findings.Add(new KapPublicFinding(
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
            findings.Add(new KapPublicFinding(
                KapPublicReportReasons.PresentationUnitUnrecognised,
                1,
                $"Sunum Para Birimi '{presentationUnit}' declares an unrecognised scale/currency — both stay null."));
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
    /// duration, and deciding which columns the report's own period covers.
    /// </summary>
    private static List<KapPublicColumn> ReadColumns(IElement table, int baseOrder, int? reportPeriod)
    {
        var headers = table.QuerySelectorAll(ContextHeaderSelector);
        if (headers.Length == 0)
        {
            return [];
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
    /// Maps a label to its canonical code inside its statement, or to its section-scoped
    /// <c>OTHER_*</c> passthrough code — recorded and ignored by metrics (the honest gap).
    /// </summary>
    private static string MapCode(string statementType, string section, string label) =>
        RulesByStatement.TryGetValue(statementType, out var rules) && rules.TryGetValue(label, out var code)
            ? code
            : $"OTHER_{section}_{Slug(label)}";

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
    /// Reads one value cell. The raw <c>title</c> attribute is the primary source (the pinned
    /// de-risking discovery); the displayed text is cross-checked against it, and a
    /// disagreement is reported while the attribute wins. A cell with no attribute but readable
    /// text still decodes (flagged <c>FromAttribute = false</c>); a cell that yields no usable
    /// value is dropped with a counted reason — never zero.
    /// </summary>
    private static (decimal Value, bool FromAttribute, string? Raw, string Displayed)? ReadCell(
        IElement cell,
        string tableClass,
        string heading,
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
                findings.Add(new KapPublicFinding(
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
                findings.Add(new KapPublicFinding(
                    KapPublicReportReasons.DisplayTextMismatch,
                    1,
                    $"{tableClass} '{heading}' row '{label}': raw attribute '{rawText}' vs displayed text "
                    + $"'{displayed}' on column '{heading}' — the attribute is the value, the disagreement is recorded.",
                    statementType,
                    label));
            }

            return (value, true, rawText, displayed);
        }

        if (string.IsNullOrWhiteSpace(displayed))
        {
            return null; // a blank cell is not a value — nothing to attribute, nothing dropped
        }

        if (!TryParseDisplayed(displayed, out var parsed))
        {
            findings.Add(new KapPublicFinding(
                KapPublicReportReasons.ValueUnattributed,
                1,
                $"{tableClass} '{heading}' row '{label}': displayed text '{displayed}' does not parse as a number "
                + "and the cell carries no raw attribute — dropped.",
                statementType,
                label));
            return null;
        }

        return (parsed, false, null, displayed);
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
    /// the duplicate-label rule — equal values collapse, unequal values conflict (both retained,
    /// the row quarantined by the store as <c>CONFLICTING_VALUE</c>; 02 §1, DR-05 overturned).
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
    }

    private sealed class Group
    {
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

        public GroupOutcome Add(string itemCode, decimal value, string label, List<KapPublicFinding> findings)
        {
            if (Lines.TryGetValue(itemCode, out var existing))
            {
                if (existing == value)
                {
                    return GroupOutcome.Collapsed; // an equal duplicate collapses idempotently
                }

                if (!_conflicts.Contains(itemCode, StringComparer.Ordinal))
                {
                    _conflicts.Add(itemCode);
                }

                findings.Add(new KapPublicFinding(
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
