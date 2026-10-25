using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Core.Metrics;
using Degerli.Fixtures;
using Degerli.Ingestion.Statements;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-foundation-081 acceptance — the KAP <b>public-site</b> excel-HTML financial report
/// decoded onto the canonical statement model, <b>including the cash-flow statement</b>
/// (01 v1.5.55 §10.9.19, FR-MDF-002/003/008/012, <c>CanonicalMetrics</c> FCF v1.3).
/// <para>
/// <b>Doubles only, and the double is the whole input.</b> Every case here decodes the
/// committed bounded-trim capture <c>kap-public-report-1643141</c>
/// (<see cref="RealSourceCatalog.KapPublicReport"/>, ASELS 2026-06 FR, filed 04.08.2026) —
/// <b>no test calls kap.org.tr</b>, and the negative cases are derived by mutating that same
/// capture in memory (a renamed label, a rewritten <c>title</c> attribute) rather than by
/// inventing a body.
/// </para>
/// <para>
/// <b>What the capture proves, and how it is pinned.</b> The export carries the raw integer of
/// every value in the cell's <c>title=""</c> attribute beside the displayed Turkish-formatted
/// text (<c>title="39468926"</c> beside <c>39.468.926</c>), so the number-formatting risk is
/// removed — the attribute is the read path and the displayed text is the cross-check. The
/// column→period mapping is read from the column <b>headers</b> (the capture marks value cells
/// with <c>col-order-class-N</c>, whose <c>N - 4</c> is the header position), and that
/// alignment is independently corroborated by the capture's own cash ladder: the cash-flow
/// statement's current-period opening cash equals the balance sheet's prior-period cash
/// (<c>34.251.653</c>) and its current-period closing cash equals the balance sheet's current
/// cash (<c>39.468.926</c>). Under the opposite alignment neither holds.
/// </para>
/// <para>
/// <b>CF basis (user decision, 2026-10-25): annual FCF + labeled interims.</b> This report is
/// <c>Periyot 2</c> — a 6-month interim — so its statements land as <c>Q</c>, never <c>FY</c>,
/// and the decoder surfaces the period type (<c>6 Aylık</c>) for downstream selection. Nothing
/// here annualizes a half-year: the DCF baseline's FY filter simply never sees these rows.
/// </para>
/// </summary>
public sealed class KapPublicReportDecoderTests : IClassFixture<PostgresFixture>
{
    /// <summary>The committed bounded-trim capture — the entire decode input.</summary>
    private static readonly string Capture = RealSourceCatalog.Get(RealSourceCatalog.KapPublicReport).Body;

    private static readonly DateOnly CurrentYearToDate = new(2026, 6, 30);
    private static readonly DateOnly PriorYearToDate = new(2025, 6, 30);
    private static readonly DateOnly CurrentBalanceSheet = new(2026, 6, 30);
    private static readonly DateOnly PriorBalanceSheet = new(2025, 12, 31);

    private readonly PostgresFixture _postgres;

    public KapPublicReportDecoderTests(PostgresFixture postgres) => _postgres = postgres;

    // =================================================================================
    // TC-MDF-106 — header/metadata decode, statement typing, period coordinates
    // =================================================================================

    /// <summary>
    /// TC-MDF-106 (i) — the report identity block decodes from the capture's own metadata:
    /// <c>Gönderim Tarihi</c>, <c>Bildirim Tipi</c>, <c>Yıl</c>, <c>Periyot</c>,
    /// <c>Sunum Para Birimi</c> (<c>1.000 TL</c> → the store's <c>thousands</c> scale and
    /// <c>TRY</c>) and <c>Finansal Tablo Niteliği</c> (Konsolide) — plus the period type the
    /// lane's annual-FCF decision needs surfaced.
    /// </summary>
    [Fact]
    public void TC_MDF_106_header_metadata_decodes_from_the_capture()
    {
        var report = KapPublicReportDecoder.Decode(Capture, "ASELS");

        Assert.Equal("ASELS", report.Symbol);
        Assert.Equal("kap-public", report.SourceRef);
        Assert.True(KapPublicReportDecoder.LooksLikeReport(Capture));
        Assert.False(KapPublicReportDecoder.LooksLikeReport("{\"facts\":[]}"));

        Assert.Equal("ASELSAN ELEKTRONİK SANAYİ VE TİCARET A.Ş.", report.Header.CompanyName);
        Assert.Equal("Finansal Rapor", report.Header.ReportTitle);
        Assert.Equal(new DateOnly(2026, 8, 4), report.Header.SubmittedAt);
        Assert.Equal("FR", report.Header.SubmissionType);
        Assert.Equal(2026, report.Header.Year);
        Assert.Equal(2, report.Header.Period);
        Assert.Equal("Konsolide", report.Header.ConsolidationType);

        // `1.000 TL` is the presentation unit verbatim; its scale and currency are the
        // store's own vocabulary (02 §3.1) — the store's input, not a re-derivation.
        Assert.Equal("1.000 TL", report.Header.PresentationUnit);
        Assert.Equal("thousands", report.Header.UnitScale);
        Assert.Equal("TRY", report.Header.Currency);

        // The pinned CF basis needs the period type surfaced: this is a 6-month interim.
        Assert.Equal("6 Aylık", report.Header.PeriodType);
        Assert.Equal(6, report.Header.PeriodMonths);
        Assert.False(report.IsAnnual);

        // Nothing in the header block went unattributed.
        Assert.Equal(0, report.Count(KapPublicReportReasons.HeaderFieldMissing));
        Assert.Equal(0, report.Count(KapPublicReportReasons.PresentationUnitUnrecognised));
        Assert.Equal(0, report.Count(KapPublicReportReasons.PeriodTypeUnknown));
    }

    /// <summary>
    /// TC-MDF-106 (ii) — each report table selects its statement type from its <b>captured
    /// heading</b> (IS/BS/CF/EQ), and the column→period mapping comes from the column
    /// <b>headers</b>, never from the cell's position: the balance sheet carries two headers
    /// (instant dates), the income statement four (two 6-month ranges + two 3-month ranges),
    /// the cash-flow statement two ranges.
    /// </summary>
    [Fact]
    public void TC_MDF_106_statements_type_by_heading_and_columns_by_header()
    {
        var report = KapPublicReportDecoder.Decode(Capture, "ASELS");

        Assert.Equal(4, report.Tables.Count);
        Assert.Equal(
            new[] { "BS", "IS", "CF", "EQ" },
            report.Tables.Select(t => t.StatementType).ToArray());

        var balanceSheet = report.Tables[0];
        Assert.Equal("Finansal Durum Tablosu (Bilanço)", balanceSheet.Heading);
        Assert.Equal(2, balanceSheet.Columns.Count);
        Assert.Equal(
            new[] { "Cari Dönem", "Önceki Dönem" },
            balanceSheet.Columns.Select(c => c.Role).ToArray());
        Assert.Equal(
            new[] { true, false },
            balanceSheet.Columns.Select(c => c.IsCurrent).ToArray());
        Assert.Equal(CurrentBalanceSheet, balanceSheet.Columns[0].PeriodEnd);
        Assert.Equal(PriorBalanceSheet, balanceSheet.Columns[1].PeriodEnd);
        Assert.Null(balanceSheet.Columns[0].DurationMonths); // an instant, not a range
        Assert.All(balanceSheet.Columns, c => Assert.True(c.Decoded));

        var income = report.Tables[1];
        Assert.Equal("Kar veya Zarar ve Diğer Kapsamlı Gelir Tablosu", income.Heading);
        Assert.Equal(4, income.Columns.Count);
        Assert.Equal(
            new[] { "Cari Dönem", "Önceki Dönem", "Cari Dönem 3 Aylık", "Önceki Dönem 3 Aylık" },
            income.Columns.Select(c => c.Role).ToArray());
        Assert.Equal(new[] { 6, 6, 3, 3 }, income.Columns.Select(c => c.DurationMonths!.Value).ToArray());
        Assert.Equal(
            new[] { true, false, true, false },
            income.Columns.Select(c => c.IsCurrent).ToArray());
        Assert.Equal(
            new[] { CurrentYearToDate, PriorYearToDate, CurrentYearToDate, PriorYearToDate },
            income.Columns.Select(c => c.PeriodEnd!.Value).ToArray());

        // The report's own period is 6 months, so the two 3-month columns are sub-period views
        // of the same statement end — named and skipped, never silently merged into the
        // year-to-date row (they share its (periodType, periodEndDate, statementType) key).
        Assert.Equal(new[] { true, true, false, false }, income.Columns.Select(c => c.Decoded).ToArray());
        Assert.Equal(KapPublicReportReasons.SubPeriodColumn, income.Columns[2].SkipReason);
        Assert.Equal(KapPublicReportReasons.SubPeriodColumn, income.Columns[3].SkipReason);
        Assert.Equal(6, report.Count(KapPublicReportReasons.SubPeriodColumn)); // 3 rows × 2 columns

        var cashFlow = report.Tables[2];
        Assert.Equal("Nakit Akış Tablosu (Dolaylı Yöntem)", cashFlow.Heading);
        Assert.Equal(2, cashFlow.Columns.Count);
        Assert.Equal(CurrentYearToDate, cashFlow.Columns[0].PeriodEnd);
        Assert.Equal(PriorYearToDate, cashFlow.Columns[1].PeriodEnd);

        // The equity statement's heading is recognised, but the capture's equity matrix has no
        // `context-header` cells — so its values cannot be attributed to a period and are an
        // honest-null drop with a counted reason (never defaulted to a period).
        var equity = report.Tables[3];
        Assert.Equal("Özkaynaklar Değişim Tablosu", equity.Heading);
        Assert.Equal("EQ", equity.StatementType);
        Assert.Empty(equity.Columns);
        Assert.Equal(98, report.Count(KapPublicReportReasons.NoColumnHeaders));
        Assert.Empty(report.LinesFor("EQ", CurrentYearToDate));
        Assert.Equal(0, report.Count(KapPublicReportReasons.UnknownTableHeading));
    }

    /// <summary>
    /// TC-MDF-106 (iii) — the period coordinates reach the store payload: one statement row per
    /// (statement type, period end), the comparative period landing as its <b>own</b> row with
    /// its own period end, the report's period type carried honestly (<c>Q</c> for this 6-month
    /// interim — never <c>FY</c>), and the published-at stamped.
    /// </summary>
    [Fact]
    public void TC_MDF_106_period_coordinates_land_on_the_store_payload()
    {
        var report = KapPublicReportDecoder.Decode(Capture, "ASELS");
        var payload = report.Payload;

        Assert.Equal("kap-public", payload.SourceRef);
        Assert.Equal("ASELS", payload.Symbol);
        Assert.Equal("thousands", payload.UnitScale);
        Assert.Equal("TRY", payload.Currency);

        // BS/IS/CF × (current, comparative) — and nothing for the un-attributable equity table.
        Assert.Equal(6, payload.Statements.Count);
        Assert.Equal(
            new[]
            {
                ("BS", CurrentBalanceSheet),
                ("BS", PriorBalanceSheet),
                ("IS", CurrentYearToDate),
                ("IS", PriorYearToDate),
                ("CF", CurrentYearToDate),
                ("CF", PriorYearToDate),
            },
            payload.Statements
                .Select(s => (s.StatementType, s.PeriodEndDate))
                .ToArray());

        foreach (var fact in payload.Statements)
        {
            // A half-year report is never an FY row (the DCF baseline's FY filter is the
            // pinned annual boundary) …
            Assert.Equal("Q", fact.PeriodType);
            Assert.Equal("as_reported", fact.Version);
            Assert.Equal(new DateOnly(2026, 8, 4), fact.PublishedAt);
            Assert.Null(fact.RestatementDate);
            Assert.Null(fact.ConflictingItemCodes);

            // … and its fiscal year is the period's own, not the report's blanket year.
            Assert.Equal(fact.PeriodEndDate.Year, fact.FiscalYear);
        }
    }

    /// <summary>
    /// TC-MDF-106 (iv) — the <c>Periyot</c> → period-type map is not invented: every triple it
    /// asserts is present in the committed <c>byCriteria</c> captures' own
    /// <c>year</c>/<c>ruleType</c>/<c>period</c> rows (2026 periods 1/2 → "3 Aylık"/"6 Aylık",
    /// 2025 period 4 → "Yıllık", and the market probe's period 3 → "9 Aylık").
    /// </summary>
    [Fact]
    public void TC_MDF_106_period_type_map_is_pinned_by_the_committed_bycriteria_capture()
    {
        Assert.Equal("3 Aylık", KapPublicReportDecoder.PeriodTypeFor(1));
        Assert.Equal("6 Aylık", KapPublicReportDecoder.PeriodTypeFor(2));
        Assert.Equal("9 Aylık", KapPublicReportDecoder.PeriodTypeFor(3));
        Assert.Equal(KapPublicReportDecoder.AnnualPeriodType, KapPublicReportDecoder.PeriodTypeFor(4));
        Assert.Equal("Yıllık", KapPublicReportDecoder.PeriodTypeFor(4));
        Assert.Equal(12, KapPublicReportDecoder.PeriodMonthsFor(4));
        Assert.Equal(6, KapPublicReportDecoder.PeriodMonthsFor(2));

        // Un-evidenced periods are null, never a guess.
        Assert.Null(KapPublicReportDecoder.PeriodTypeFor(null));
        Assert.Null(KapPublicReportDecoder.PeriodTypeFor(5));
        Assert.Null(KapPublicReportDecoder.PeriodMonthsFor(5));

        // … and the map agrees with every attested row of the two committed list captures.
        var attested = 0;
        foreach (var payload in new[] { RealSourceCatalog.KapPublicByCriteria, RealSourceCatalog.KapPublicMarketProbe })
        {
            using var document = JsonDocument.Parse(RealSourceCatalog.Get(payload).Body);
            foreach (var row in document.RootElement.EnumerateArray())
            {
                if (!row.TryGetProperty("period", out var periodElement)
                    || periodElement.ValueKind != JsonValueKind.Number)
                {
                    continue;
                }

                // Only financial-report disclosures pin the financial period map: the capture
                // also carries a non-FR filing (the 2025 TSRS sustainability report, whose
                // `period: 1` is not a financial period at all), and reading its `Yıllık` as a
                // financial-period spelling would be a false attestation.
                if (!row.TryGetProperty("disclosureType", out var typeElement)
                    || !string.Equals(typeElement.GetString(), "FR", StringComparison.Ordinal))
                {
                    continue;
                }

                var period = periodElement.GetInt32();
                if (KapPublicReportDecoder.PeriodTypeFor(period) is not { } periodType)
                {
                    continue;
                }

                Assert.Equal(row.GetProperty("ruleType").GetString(), periodType);
                attested++;
            }
        }

        Assert.True(attested >= 6, $"only {attested} attested period/ruleType rows in the committed captures");
    }

    /// <summary>
    /// TC-MDF-106 (v) — an unrecognised presentation unit refuses rather than scales wrongly:
    /// <c>Sunum Para Birimi</c> <c>1.000 XAU</c> leaves scale and currency null with a counted,
    /// verbatim finding, and the real store path refuses the payload (<c>Inserted = 0</c>,
    /// <c>SCHEMA_MISMATCH</c>) instead of guessing a scale.
    /// </summary>
    [Fact]
    public async Task TC_MDF_106_unrecognised_presentation_unit_is_refused_by_the_store()
    {
        var mutated = Mutate(Capture, "1.000 TL", "1.000 XAU");
        var report = KapPublicReportDecoder.Decode(mutated, "ASELS");

        Assert.Null(report.Header.UnitScale);
        Assert.Null(report.Header.Currency);

        var finding = Assert.Single(
            report.Findings,
            f => f.ReasonCode == KapPublicReportReasons.PresentationUnitUnrecognised);
        Assert.Equal(1, finding.Count);
        Assert.Contains("1.000 XAU", finding.Detail, StringComparison.Ordinal); // verbatim

        var (store, _) = await StoreAsync();
        var result = await store.UpsertAsync(
            "kap-public",
            new DateTimeOffset(2026, 8, 4, 18, 39, 41, TimeSpan.Zero),
            report.Payload);

        Assert.Equal(0, result.Inserted);
        Assert.Contains("SCHEMA_MISMATCH", result.RejectedReasonCodes);
    }

    /// <summary>
    /// TC-MDF-106 (vi) — headers are paired to value columns by ordinal position, and that
    /// pairing is guarded: a spurious <c>context-header</c> in the captured cash-flow table
    /// (three headers against two value-column classes) is a counted
    /// <c>COLUMN_HEADER_MISMATCH</c> and the table is skipped whole — no shifted column is
    /// emitted.
    /// </summary>
    [Fact]
    public void TC_MDF_106_header_column_pairing_guard_skips_a_shifted_table()
    {
        // Occurrence 1 of this cell is the cash-flow table's (occurrence 0 is the balance
        // sheet's); the spurious header is inserted before it inside the same header row.
        const string headerCell = "<td align=\"center\" class=\"context-header\" column-id=\"5\">";
        var mutated = Mutate(
            Capture,
            headerCell,
            "<td align=\"center\" class=\"context-header\"><div>Fazladan Başlık</div></td>" + headerCell,
            occurrence: 1);

        var report = KapPublicReportDecoder.Decode(mutated, "ASELS");

        var finding = Assert.Single(
            report.Findings,
            f => f.ReasonCode == KapPublicReportReasons.ColumnHeaderMismatch);
        Assert.Equal("CF", finding.StatementType);
        Assert.Contains("Nakit Akış Tablosu (Dolaylı Yöntem)", finding.Detail, StringComparison.Ordinal);

        // The table is skipped whole: no column paired, no shifted period, no CF statement row.
        var cashFlow = report.Tables.Single(t => t.StatementType == "CF");
        Assert.Empty(cashFlow.Columns);
        Assert.Empty(report.LinesFor("CF", CurrentYearToDate));
        Assert.DoesNotContain(report.Payload.Statements, s => s.StatementType == "CF");
    }

    // =================================================================================
    // TC-MDF-107 — line-item honesty
    // =================================================================================

    /// <summary>
    /// TC-MDF-107 (i) — the label→canonical map is built <b>only</b> from labels the committed
    /// capture actually carries, each entry naming the capture that supplied the spelling, and
    /// every target is a canonical chart-of-accounts code (<c>StatementMapper</c> untouched).
    /// </summary>
    [Fact]
    public void TC_MDF_107_label_map_is_built_only_from_captured_labels_with_provenance()
    {
        var map = KapPublicReportDecoder.LabelMap;

        Assert.NotEmpty(map);
        foreach (var rule in map)
        {
            Assert.Equal(KapPublicReportDecoder.CaptureName, rule.Provenance);
            Assert.Contains(rule.Label, Capture, StringComparison.Ordinal);
            Assert.Contains(rule.ItemCode, StatementMapper.CanonicalCodes);
            Assert.Contains(rule.StatementType, new[] { "IS", "BS", "CF" });
        }

        // The three the cash-flow deliverable hinges on, plus their statement typing.
        Assert.Contains(map, r => r is { StatementType: "CF", ItemCode: "OCF" });
        Assert.Contains(map, r => r is { StatementType: "CF", ItemCode: "CAPEX" });
        Assert.Contains(map, r => r is { StatementType: "IS", ItemCode: "REV" });

        // No (statement, label) pair appears twice — the map is a function.
        Assert.Equal(map.Count, map.Select(r => (r.StatementType, r.Label)).Distinct().Count());

        // A label the capture does not carry can never be in the map (spot-check the trap: the
        // VYK taxonomy's bare element names are a different label space).
        Assert.DoesNotContain(map, r => r.Label == "Revenue" || r.Label == "CostOfSales");
    }

    /// <summary>
    /// TC-MDF-107 (ii) — Turkish number goldens. Two independent paths are pinned: the display
    /// text grammar (<c>.</c> thousands, <c>,</c> decimal — the shared culture-free parser) and
    /// the capture's raw-attribute integers, whose exact decimals these cases pin from real
    /// fixture values (thousand TL, per <c>Sunum Para Birimi</c>).
    /// </summary>
    [Fact]
    public void TC_MDF_107_turkish_number_and_presentation_unit_goldens()
    {
        // The display grammar, culture-free (InvariantGlobalization is on).
        Assert.Equal(1_234.56m, KapXbrlStatementDecoder.ParseTurkishNumber("1.234,56"));
        Assert.Equal(-60_397_561m, KapXbrlStatementDecoder.ParseTurkishNumber("-60.397.561"));
        Assert.Equal(88_494_252m, KapXbrlStatementDecoder.ParseTurkishNumber("88.494.252"));

        var report = KapPublicReportDecoder.Decode(Capture, "ASELS");

        // Every value came from the raw `title` attribute (a display-derived line would carry
        // no attribute at all), and that attribute is verbatim present in the capture.
        Assert.All(report.Lines, line =>
        {
            Assert.False(string.IsNullOrWhiteSpace(line.RawAttribute), line.Label);
            Assert.Contains(line.RawAttribute!, Capture, StringComparison.Ordinal);
        });
        Assert.Equal(0, report.Count(KapPublicReportReasons.DisplayTextMismatch));
        Assert.Equal(0, report.Count(KapPublicReportReasons.ValueUnattributed));

        // Raw-attribute goldens from the capture (thousand TL).
        var currentIncome = report.Payload.Statements.Single(
            s => s.StatementType == "IS" && s.PeriodEndDate == CurrentYearToDate).Lines;
        Assert.Equal(88_494_252m, currentIncome["REV"]);
        Assert.Equal(-60_397_561m, currentIncome["COGS"]);
        Assert.Equal(28_096_691m, currentIncome["GROSS_PROFIT"]);

        var currentBalanceSheet = report.Payload.Statements.Single(
            s => s.StatementType == "BS" && s.PeriodEndDate == CurrentBalanceSheet).Lines;
        Assert.Equal(39_468_926m, currentBalanceSheet["CASH"]);

        // The attribute is the value, the displayed text is the cross-check: the line records
        // both verbatim.
        var revenue = report.LinesFor("IS", CurrentYearToDate).Single(l => l.ItemCode == "REV");
        Assert.Equal("88494252", revenue.RawAttribute);
        Assert.Equal("88.494.252", revenue.DisplayedText);
    }

    /// <summary>
    /// TC-MDF-107 (iii) — a captured label the map lacks lands as an <c>OTHER_*</c> line
    /// (recorded, ignored by metrics — the honest-gap doctrine), scoped by the statement section
    /// so that two identically-labelled lines in different statements or cash-flow blocks can
    /// never collide.
    /// </summary>
    [Fact]
    public void TC_MDF_107_unmapped_labels_pass_through_as_other_lines()
    {
        var report = KapPublicReportDecoder.Decode(Capture, "ASELS");

        var currentBalanceSheet = report.Payload.Statements.Single(
            s => s.StatementType == "BS" && s.PeriodEndDate == CurrentBalanceSheet).Lines;
        Assert.Equal(47_167_326m, currentBalanceSheet["OTHER_BS_Ticari_Alacaklar"]);
        Assert.Equal(21_831_106m, currentBalanceSheet["OTHER_BS_İlişkili_Taraflardan_Ticari_Alacaklar"]);

        // The unmapped passthrough survives the shared mapper unchanged (it is not re-prefixed).
        Assert.Equal(
            currentBalanceSheet["OTHER_BS_Ticari_Alacaklar"],
            StatementMapper.Map(currentBalanceSheet)["OTHER_BS_Ticari_Alacaklar"]);

        // The cash-flow statement's non-canonical companions are present and section-scoped.
        var currentCashFlow = report.Payload.Statements.Single(
            s => s.StatementType == "CF" && s.PeriodEndDate == CurrentYearToDate).Lines;
        Assert.Contains(
            "OTHER_CF_OPERATING_Dönem_Net_Karı_Zararı_Mutabakatı_İle_İlgili_Düzeltmeler",
            currentCashFlow.Keys);
        Assert.Contains(
            "OTHER_CF_INVESTING_Maddi_Duran_Varlık_Alımından_Kaynaklanan_Nakit_Çıkışları",
            currentCashFlow.Keys);
        Assert.Contains(
            "OTHER_CF_FINANCING_Borçlanmadan_Kaynaklanan_Nakit_Girişleri",
            currentCashFlow.Keys);
        Assert.Contains(
            "OTHER_CF_SUMMARY_DÖNEM_BAŞI_NAKİT_VE_NAKİT_BENZERLERİ",
            currentCashFlow.Keys);

        // The before-tax operating subtotal is not OCF (DR-2, user decision 2026-10-26) but it
        // must stay a decoded, labelled line — the passthrough keeps both measures transparent.
        Assert.Equal(
            20_382_722m,
            currentCashFlow["OTHER_CF_OPERATING_Faaliyetlerden_Elde_Edilen_Nakit_Akışları"]);
        var priorCashFlow = report.Payload.Statements.Single(
            s => s.StatementType == "CF" && s.PeriodEndDate == PriorYearToDate).Lines;
        Assert.Equal(
            21_061_426m,
            priorCashFlow["OTHER_CF_OPERATING_Faaliyetlerden_Elde_Edilen_Nakit_Akışları"]);

        // The whole line set is accounted for: no value is silently absent from the result.
        Assert.Equal(116, report.Lines.Count);
        Assert.Equal(4, report.LinesFor("BS", CurrentBalanceSheet).Count);
        Assert.Equal(4, report.LinesFor("BS", PriorBalanceSheet).Count);
        Assert.Equal(3, report.LinesFor("IS", CurrentYearToDate).Count);
        Assert.Equal(3, report.LinesFor("IS", PriorYearToDate).Count);
        Assert.Equal(51, report.LinesFor("CF", CurrentYearToDate).Count);
        Assert.Equal(51, report.LinesFor("CF", PriorYearToDate).Count);
    }

    /// <summary>
    /// TC-MDF-107 (iv) — a value that cannot be attributed to a table+label+period is an
    /// honest-null drop with a counted reason: never a guess, never zero. The capture's equity
    /// matrix is exactly this shape (98 valued cells, no period headers), so the drop is
    /// exercised on real data — and the un-attributable cells leave no line and no zero.
    /// </summary>
    [Fact]
    public void TC_MDF_107_unattributable_values_are_honest_nulls_with_a_counted_reason()
    {
        var report = KapPublicReportDecoder.Decode(Capture, "ASELS");

        // 98 valued cells in the equity matrix were dropped, and the drop is named verbatim.
        Assert.Equal(98, report.Count(KapPublicReportReasons.NoColumnHeaders));
        var finding = report.Findings.Single(
            f => f.ReasonCode == KapPublicReportReasons.NoColumnHeaders);
        Assert.Contains("Özkaynaklar Değişim Tablosu", finding.Detail, StringComparison.Ordinal);

        // No equity line exists at any period, and nothing was zero-filled to stand in for one.
        Assert.DoesNotContain(report.Lines, l => l.StatementType == "EQ");
        Assert.DoesNotContain(report.Payload.Statements, s => s.StatementType == "EQ");
        Assert.DoesNotContain(
            report.Payload.Statements.SelectMany(s => s.Lines),
            line => line.Key.StartsWith("OTHER_EQ", StringComparison.Ordinal));

        // A cell whose raw attribute is unreadable is dropped the same way (never coerced to
        // zero). Derived from the capture: the OCF cell's raw integer is corrupted in place.
        var corrupted = Mutate(Capture, "title=\"15206440\"", "title=\"n/a\"");
        var corruptedReport = KapPublicReportDecoder.Decode(corrupted, "ASELS");
        Assert.Equal(1, corruptedReport.Count(KapPublicReportReasons.ValueUnattributed));
        Assert.DoesNotContain(corruptedReport.LinesFor("CF", CurrentYearToDate), l => l.ItemCode == "OCF");
        Assert.DoesNotContain(
            corruptedReport.Payload.Statements
                .Single(s => s.StatementType == "CF" && s.PeriodEndDate == CurrentYearToDate)
                .Lines,
            line => line.Key == "OCF");
    }

    /// <summary>
    /// TC-MDF-107 (v) — the duplicate-label rule is the TC-MDF-065 doctrine applied to this
    /// source: within one statement row the same code with <b>equal</b> values collapses
    /// idempotently, with <b>unequal</b> values it is a counted conflict (both values retained
    /// in the finding) — never a silent latest-wins. Both cases are derived from the capture by
    /// renaming one real row's label, so the values stay real.
    /// </summary>
    [Fact]
    public void TC_MDF_107_duplicate_labels_equal_collapse_unequal_quarantine()
    {
        // (1) Equal duplicates collapse: two cash-flow rows carry 646.599 in the current
        // column; renaming one onto the other's label leaves one line and no conflict.
        var equal = Mutate(
            Capture,
            "Devam Eden İnşaat Ve Taahhüt İşlerinden Doğan Sözleşme Yükümlülüklerindeki Artış (Azalış)",
            "Müşteri Sözleşmelerinden Doğan Yükümlülüklerdeki Artış (Azalış) İle İlgili Düzeltmeler");
        var equalReport = KapPublicReportDecoder.Decode(equal, "ASELS");

        Assert.Equal(0, equalReport.Count(KapPublicReportReasons.DuplicateLabelConflict));
        var collapsed = equalReport.LinesFor("CF", CurrentYearToDate)
            .Where(l => l.ItemCode == "OTHER_CF_OPERATING_Müşteri_Sözleşmelerinden_Doğan_Yükümlülüklerdeki_Artış_Azalış_İle_İlgili_Düzeltmeler")
            .ToList();
        Assert.Single(collapsed);
        Assert.Equal(646_599m, collapsed[0].Value);
        Assert.Null(equalReport.Payload.Statements
            .Single(s => s.StatementType == "CF" && s.PeriodEndDate == CurrentYearToDate)
            .ConflictingItemCodes);

        // (2) Unequal duplicates quarantine DEPR_AMORT: the adjustment row is renamed onto the
        // depreciation label, so the code appears twice with different values.
        var unequal = Mutate(
            Capture,
            "Değer Düşüklüğü (İptali) İle İlgili Düzeltmeler",
            "Amortisman ve İtfa Gideri İle İlgili Düzeltmeler");
        var unequalReport = KapPublicReportDecoder.Decode(unequal, "ASELS");

        // One conflict finding per column (current and comparative), each retaining both values.
        Assert.Equal(2, unequalReport.Count(KapPublicReportReasons.DuplicateLabelConflict));
        var conflicts = unequalReport.Findings
            .Where(f => f.ReasonCode == KapPublicReportReasons.DuplicateLabelConflict
                && f.StatementType == "CF")
            .ToList();
        Assert.Equal(2, conflicts.Count);
        var conflict = conflicts[0];
        Assert.Contains("DEPR_AMORT", conflict.Detail, StringComparison.Ordinal);
        Assert.Contains("4.338.537", conflict.Detail, StringComparison.Ordinal);   // retained
        Assert.Contains("-218.085", conflict.Detail, StringComparison.Ordinal);    // retained, not dropped

        var flagged = unequalReport.Payload.Statements.Single(
            s => s.StatementType == "CF" && s.PeriodEndDate == CurrentYearToDate);
        Assert.Equal(new[] { "DEPR_AMORT" }, flagged.ConflictingItemCodes!);

        // A conflict is a validation event, not a rewrite: the first value is retained, never
        // the later one (02 §1, DR-05 overturned).
        Assert.Equal(4_338_537m, flagged.Lines["DEPR_AMORT"]);
    }

    /// <summary>
    /// TC-MDF-107 (vi) — the displayed-text cross-check is a <b>finding</b>, not a silent
    /// override: the raw <c>title</c> attribute is the pinned source, so a cell whose displayed
    /// text disagrees still decodes from the attribute while the disagreement is recorded
    /// verbatim. Derived from the capture by rewriting one cell's attribute (its displayed text
    /// stays as captured).
    /// </summary>
    [Fact]
    public void TC_MDF_107_display_text_mismatch_is_a_finding_not_an_override()
    {
        var mutated = Mutate(Capture, "title=\"15206440\"", "title=\"15206499\"");
        var report = KapPublicReportDecoder.Decode(mutated, "ASELS");

        var mismatch = Assert.Single(
            report.Findings,
            f => f.ReasonCode == KapPublicReportReasons.DisplayTextMismatch);
        Assert.Equal(1, mismatch.Count);
        Assert.Equal("CF", mismatch.StatementType);
        Assert.Contains("15.206.440", mismatch.Detail, StringComparison.Ordinal); // displayed, verbatim
        Assert.Contains("15206499", mismatch.Detail, StringComparison.Ordinal);    // attribute, verbatim
        Assert.Contains("Cari Dönem", mismatch.Detail, StringComparison.Ordinal);  // the column, not the table

        // The attribute wins — the decoder does not "repair" the value from the display text.
        var line = report.LinesFor("CF", CurrentYearToDate).Single(l => l.ItemCode == "OCF");
        Assert.Equal(15_206_499m, line.Value);
        Assert.Equal("15.206.440", line.DisplayedText);
        Assert.Equal("15206499", line.RawAttribute);
    }

    /// <summary>
    /// TC-MDF-107 (vii) — a value cell with <b>no</b> raw <c>title</c> attribute is a counted
    /// honest-null drop: the pinned contract reads the attribute and cross-checks the display
    /// text, never the reverse. Derived from the capture by stripping the current OCF cell's
    /// attribute while leaving its displayed text intact.
    /// </summary>
    [Fact]
    public void TC_MDF_107_cell_without_raw_attribute_is_dropped_never_read_from_display()
    {
        var stripped = Mutate(Capture, "title=\"15206440\"", string.Empty);
        var report = KapPublicReportDecoder.Decode(stripped, "ASELS");

        var finding = Assert.Single(
            report.Findings,
            f => f.ReasonCode == KapPublicReportReasons.ValueUnattributed);
        Assert.Equal(1, finding.Count);
        Assert.Equal("CF", finding.StatementType);
        Assert.Equal("İŞLETME FAALİYETLERİNDEN NAKİT AKIŞLARI", finding.Label);
        Assert.Contains("Cari Dönem", finding.Detail, StringComparison.Ordinal);
        Assert.Contains("15.206.440", finding.Detail, StringComparison.Ordinal); // displayed, verbatim

        // Absent from the payload for that (statement, period) — never decoded from the display
        // text and never zero-filled in its place.
        var currentCashFlow = report.Payload.Statements.Single(
            s => s.StatementType == "CF" && s.PeriodEndDate == CurrentYearToDate);
        Assert.False(currentCashFlow.Lines.ContainsKey("OCF"));
        Assert.DoesNotContain(report.LinesFor("CF", CurrentYearToDate), l => l.ItemCode == "OCF");
        Assert.DoesNotContain(
            report.LinesFor("CF", CurrentYearToDate),
            l => l.Label == "İŞLETME FAALİYETLERİNDEN NAKİT AKIŞLARI");
    }

    /// <summary>
    /// TC-MDF-107 (viii) — every decoded line answers "which table, which label, which column,
    /// which cell", and the section is assigned <b>before</b> the label map: the cash-flow
    /// statement's <c>Dönem Karı (Zararı)</c> (a label the income statement also uses) is
    /// attributed to the operating block, and a label repeated across cash-flow blocks keeps
    /// both values under distinct, section-scoped codes.
    /// </summary>
    [Fact]
    public void TC_MDF_107_every_line_answers_which_table_and_label()
    {
        var report = KapPublicReportDecoder.Decode(Capture, "ASELS");

        foreach (var line in report.Lines)
        {
            // The four legs of the trace are non-empty and verbatim present in the capture.
            Assert.False(string.IsNullOrWhiteSpace(line.TableHeading));
            Assert.False(string.IsNullOrWhiteSpace(line.Label));
            Assert.False(string.IsNullOrWhiteSpace(line.ColumnHeader));
            Assert.Contains(line.TableHeading, Capture, StringComparison.Ordinal);
            Assert.Contains(line.Label, Capture, StringComparison.Ordinal);
            Assert.True(line.RowOrder >= 0);

            // A line is either a canonical code or this source's section-scoped passthrough —
            // nothing else, and a cash-flow line never leaves the CF sections.
            if (StatementMapper.CanonicalCodes.Contains(line.ItemCode))
            {
                continue;
            }

            var expectedPrefix = line.StatementType == "CF" ? "OTHER_CF_" : $"OTHER_{line.StatementType}_";
            Assert.StartsWith(expectedPrefix, line.ItemCode, StringComparison.Ordinal);
        }

        // Section BEFORE label: `Dönem Karı (Zararı)` is the cash-flow statement's starting
        // profit line (the full income statement carries the same label), so it is attributed
        // to the operating block and mapped to NI there.
        var profit = report.LinesFor("CF", CurrentYearToDate).Single(l => l.Label == "Dönem Karı (Zararı)");
        Assert.Equal(KapPublicSections.CashFlowOperating, profit.Section);
        Assert.Equal("NI", profit.ItemCode);
        Assert.Equal(14_449_834m, profit.Value);

        // The same label in different cash-flow blocks keeps both values: derived from the
        // capture by renaming the investing block's valued `Alınan Temettüler` (122.899, the
        // operating block's identically-labelled template row carries no value) onto a
        // financing-block label that also carries a value (-20.875.900).
        var mutated = Mutate(
            Capture,
            "Alınan Temettüler",
            "Borç Ödemelerine İlişkin Nakit Çıkışları",
            occurrence: 1);
        var mutatedReport = KapPublicReportDecoder.Decode(mutated, "ASELS");
        var duplicates = mutatedReport.LinesFor("CF", CurrentYearToDate)
            .Where(l => l.Label == "Borç Ödemelerine İlişkin Nakit Çıkışları")
            .ToList();

        Assert.Equal(2, duplicates.Count);
        Assert.Equal(
            new[] { KapPublicSections.CashFlowFinancing, KapPublicSections.CashFlowInvesting },
            duplicates.Select(l => l.Section).OrderBy(s => s, StringComparer.Ordinal).ToArray());
        Assert.Equal(122_899m, duplicates.Single(l => l.Section == KapPublicSections.CashFlowInvesting).Value);
        Assert.Equal(-20_875_900m, duplicates.Single(l => l.Section == KapPublicSections.CashFlowFinancing).Value);
        Assert.NotEqual(duplicates[0].ItemCode, duplicates[1].ItemCode);
        Assert.Equal(0, mutatedReport.Count(KapPublicReportReasons.DuplicateLabelConflict));
    }

    // =================================================================================
    // TC-MDF-108 — the cash-flow statement decodes and FCF computes end-to-end
    // =================================================================================

    /// <summary>
    /// TC-MDF-108 (i) — the 051 goal, re-pointed at this source: the cash-flow statement
    /// decodes with correct typing, units and period coordinates, the stored
    /// <c>fin_line_items</c> rows carry <c>OCF</c>/<c>CAPEX</c> (red before this ticket: zero
    /// cash-flow rows anywhere in the store), and FCF computes through the locked formula
    /// (OCF − |CAPEX|, NULL-if-missing) with the store's unit normalization applied.
    /// </summary>
    [Fact]
    public async Task TC_MDF_108_cash_flow_decodes_and_fcf_computes_end_to_end()
    {
        var report = KapPublicReportDecoder.Decode(Capture, "ASELS");
        var (store, instrumentId) = await StoreAsync();

        var result = await store.UpsertAsync(
            "kap-public",
            new DateTimeOffset(2026, 8, 4, 18, 39, 41, TimeSpan.Zero),
            report.Payload);

        Assert.Equal(6, result.Inserted);
        Assert.Equal(0, result.Unchanged);
        Assert.Empty(result.RejectedReasonCodes);

        await using var db = _postgres.CreateContext();
        var cfStatements = await db.FinancialStatements
            .Where(s => s.InstrumentId == instrumentId && s.StatementType == "CF")
            .ToListAsync();

        // The deliverable: cash-flow statements exist at all (there were none before).
        Assert.Equal(2, cfStatements.Count);
        Assert.All(cfStatements, s =>
        {
            Assert.Equal("Q", s.PeriodType);
            Assert.Equal("thousands", s.SourceUnitScale);
            Assert.Equal("TRY", s.SourceCurrency);
            Assert.Equal("kap-public", s.SourceRef);
        });

        var currentStatement = cfStatements.Single(s => s.PeriodEndDate == CurrentYearToDate);
        var currentLines = await db.FinLineItems
            .Where(l => l.StatementId == currentStatement.Id)
            .ToDictionaryAsync(l => l.ItemCode, l => l.Value);

        // Normalized to single TL by the store's own scale (02 §3.1): the report is in
        // thousands, so the stored value is the captured integer × 1.000.
        Assert.Equal(15_206_440_000m, currentLines["OCF"]);
        Assert.Equal(-33_746_150_000m, currentLines["CAPEX"]);
        Assert.Equal(-5_991_513_000m, currentLines["ΔWC"]);
        Assert.Equal(4_338_537_000m, currentLines["DEPR_AMORT"]);
        Assert.Equal(39_468_926_000m, currentLines["CASH"]);

        // FCF through the locked formula (CanonicalMetrics FCF v1.3), on the stored units.
        var currentFcf = CanonicalMetrics.FreeCashFlow(currentLines["OCF"], currentLines["CAPEX"]);
        Assert.Equal(15_206_440_000m - 33_746_150_000m, currentFcf); // −18.539.710 thousand TL

        var priorStatement = cfStatements.Single(s => s.PeriodEndDate == PriorYearToDate);
        var priorLines = await db.FinLineItems
            .Where(l => l.StatementId == priorStatement.Id)
            .ToDictionaryAsync(l => l.ItemCode, l => l.Value);
        Assert.Equal(18_013_334_000m, priorLines["OCF"]);
        Assert.Equal(-17_190_837_000m, priorLines["CAPEX"]);
        Assert.Equal(
            18_013_334_000m - 17_190_837_000m,
            CanonicalMetrics.FreeCashFlow(priorLines["OCF"], priorLines["CAPEX"]));

        // The balance sheet's cash for the same period end agrees with the cash-flow
        // statement's closing cash — the capture's own cross-statement invariant, asserted
        // rather than assumed (it is what independently pins the header→column alignment, on
        // top of the header order itself).
        var balanceSheet = await db.FinancialStatements.SingleAsync(
            s => s.InstrumentId == instrumentId && s.StatementType == "BS" && s.PeriodEndDate == CurrentBalanceSheet);
        var balanceLines = await db.FinLineItems
            .Where(l => l.StatementId == balanceSheet.Id)
            .ToDictionaryAsync(l => l.ItemCode, l => l.Value);
        Assert.Equal(balanceLines["CASH"], currentLines["CASH"]);

        // The comparative balance-sheet cash is the cash-flow statement's opening balance for
        // the current period: 34.251.653 opens 39.468.926 closes.
        var priorBalanceSheetId = await db.FinancialStatements
            .Where(s => s.InstrumentId == instrumentId && s.StatementType == "BS" && s.PeriodEndDate == PriorBalanceSheet)
            .Select(s => s.Id)
            .SingleAsync();
        var priorBalanceLines = await db.FinLineItems
            .Where(l => l.StatementId == priorBalanceSheetId)
            .ToDictionaryAsync(l => l.ItemCode, l => l.Value);
        var opening = report.LinesFor("CF", CurrentYearToDate)
            .Single(l => l.Label == "DÖNEM BAŞI NAKİT VE NAKİT BENZERLERİ");
        Assert.Equal(priorBalanceLines["CASH"], opening.Value * 1_000m);
    }

    /// <summary>
    /// TC-MDF-108 (ii) — FCF is NULL where either input is missing, never zero-for-missing: a
    /// capture whose CAPEX label does not map leaves the cash-flow statement without
    /// <c>CAPEX</c>, and the metric returns null rather than treating absence as 0. The
    /// statement itself still stores (an honest gap, not a refusal).
    /// </summary>
    [Fact]
    public async Task TC_MDF_108_fcf_is_null_when_either_input_is_missing()
    {
        var mutated = Mutate(
            Capture,
            "Maddi ve Maddi Olmayan Duran Varlıkların Alımından Kaynaklanan Nakit Çıkışları",
            "Maddi ve Maddi Olmayan Duran Varlıkların Alımından Kaynaklanmayan Nakit Çıkışları");
        var report = KapPublicReportDecoder.Decode(mutated, "ASELS");

        var currentCashFlow = report.Payload.Statements.Single(
            s => s.StatementType == "CF" && s.PeriodEndDate == CurrentYearToDate);
        Assert.True(currentCashFlow.Lines.ContainsKey("OCF"));
        Assert.False(currentCashFlow.Lines.ContainsKey("CAPEX"));

        // The unmapped spelling is recorded honestly (never dropped, never coerced to zero).
        Assert.Contains(
            "OTHER_CF_INVESTING_Maddi_ve_Maddi_Olmayan_Duran_Varlıkların_Alımından_Kaynaklanmayan_Nakit_Çıkışları",
            currentCashFlow.Lines.Keys);

        var (store, instrumentId) = await StoreAsync();
        var result = await store.UpsertAsync(
            "kap-public",
            new DateTimeOffset(2026, 8, 4, 18, 39, 41, TimeSpan.Zero),
            report.Payload);
        Assert.Equal(6, result.Inserted);

        await using var db = _postgres.CreateContext();
        var statement = await db.FinancialStatements.SingleAsync(
            s => s.InstrumentId == instrumentId && s.StatementType == "CF" && s.PeriodEndDate == CurrentYearToDate);
        var lines = await db.FinLineItems
            .Where(l => l.StatementId == statement.Id)
            .ToDictionaryAsync(l => l.ItemCode, l => l.Value);

        Assert.False(lines.ContainsKey("CAPEX"));
        decimal? capex = lines.TryGetValue("CAPEX", out var capexValue) ? capexValue : null;
        Assert.Null(CanonicalMetrics.FreeCashFlow(lines["OCF"], capex));
        Assert.Null(CanonicalMetrics.FreeCashFlow(null, capex));
        Assert.Null(CanonicalMetrics.FreeCashFlow(null, null));
    }

    /// <summary>
    /// TC-MDF-108 (iii) — the annual-FCF basis is enforced by the coordinates, not by a
    /// consumer's care: a half-year report never produces an <c>FY</c> row (so the DCF
    /// baseline's FY filter cannot consume an interim), and an unknown <c>Periyot</c> fails
    /// <b>safe</b> — never labeled annual on a guess.
    /// </summary>
    [Fact]
    public void TC_MDF_108_interim_never_becomes_an_annual_row()
    {
        var report = KapPublicReportDecoder.Decode(Capture, "ASELS");
        Assert.DoesNotContain(report.Payload.Statements, s => s.PeriodType == "FY");
        Assert.All(report.Payload.Statements, s => Assert.Equal("Q", s.PeriodType));
        Assert.Contains(
            report.Payload.Statements,
            s => s.StatementType == "CF" && s.PeriodEndDate == CurrentYearToDate && s.PeriodType == "Q");

        // The store's period-type vocabulary is the decoder's mapping, not a consumer's guess.
        Assert.Equal("FY", KapPublicReportDecoder.StatementPeriodTypeFor(4));
        Assert.Equal("Q", KapPublicReportDecoder.StatementPeriodTypeFor(2));
        Assert.Equal("Q", KapPublicReportDecoder.StatementPeriodTypeFor(null)); // fail safe: never FY

        // A body whose `Periyot` is missing leaves the period type null (counted, never a
        // default) while the columns still decode through the table's own longest range — and
        // the rows still cannot be mistaken for annuals.
        var unknownPeriod = Mutate(Capture, "   Periyot:2", "   Periyot:");
        var unknown = KapPublicReportDecoder.Decode(unknownPeriod, "ASELS");
        Assert.Null(unknown.Header.Period);
        Assert.Null(unknown.Header.PeriodType);
        Assert.Equal(1, unknown.Count(KapPublicReportReasons.PeriodTypeUnknown));
        Assert.Equal(6, unknown.Payload.Statements.Count);
        Assert.All(unknown.Payload.Statements, s => Assert.Equal("Q", s.PeriodType));
        Assert.Equal(
            15_206_440m,
            unknown.Payload.Statements.Single(
                s => s.StatementType == "CF" && s.PeriodEndDate == CurrentYearToDate).Lines["OCF"]);
    }

    /// <summary>
    /// TC-MDF-108 (iv) — the declared <c>Periyot</c> is cross-checked against the durations the
    /// column headers actually declare. A 6-month capture self-declaring <c>Periyot:4</c> is
    /// counted <c>PERIOD_DURATION_MISMATCH</c> and fails safe to <c>Q</c> for every statement
    /// row — the instant balance-sheet columns included — so no half-year is written as FY.
    /// </summary>
    [Fact]
    public void TC_MDF_108_declared_period_is_cross_checked_against_column_durations()
    {
        var mutated = Mutate(Capture, "   Periyot:2", "   Periyot:4");
        var report = KapPublicReportDecoder.Decode(mutated, "ASELS");

        Assert.Equal("Yıllık", report.Header.PeriodType); // self-declared, surfaced verbatim

        var finding = Assert.Single(
            report.Findings,
            f => f.ReasonCode == KapPublicReportReasons.PeriodDurationMismatch);
        Assert.Equal(1, finding.Count);
        Assert.Contains("12", finding.Detail, StringComparison.Ordinal); // declared months
        Assert.Contains("6", finding.Detail, StringComparison.Ordinal);  // observed months

        Assert.DoesNotContain(report.Payload.Statements, s => s.PeriodType == "FY");
        Assert.All(report.Payload.Statements, s => Assert.Equal("Q", s.PeriodType));

        // The balance sheet still lands — but as a Q half-year, never an FY row.
        Assert.Contains(
            report.Payload.Statements,
            s => s.StatementType == "BS" && s.PeriodEndDate == CurrentBalanceSheet && s.PeriodType == "Q");
    }

    /// <summary>
    /// TC-MDF-108 (v) — a statement that decodes lines but maps <b>none</b> to a canonical code
    /// is named <c>NO_CANONICAL_LINES</c>: rewriting every CF label the map knows leaves 102
    /// <c>OTHER_*</c> lines and zero canonical ones, so the drift cannot masquerade as a clean
    /// ledger.
    /// </summary>
    [Fact]
    public void TC_MDF_108_canonical_empty_statement_is_named()
    {
        var mutated = Mutate(Capture, "Dönem Karı (Zararı)", "Dönem Sonucu (Yeniden Adlandırıldı)");
        mutated = Mutate(
            mutated,
            "İŞLETME FAALİYETLERİNDEN NAKİT AKIŞLARI",
            "İşletme Faaliyetleri (Yeniden Adlandırıldı)");
        mutated = Mutate(
            mutated,
            "Maddi ve Maddi Olmayan Duran Varlıkların Alımından Kaynaklanan Nakit Çıkışları",
            "Duran Varlık Alım Çıkışları (Yeniden Adlandırıldı)");
        mutated = Mutate(
            mutated,
            "İşletme Sermayesinde Gerçekleşen Değişimler",
            "İşletme Sermayesi Değişimi (Yeniden Adlandırıldı)");
        mutated = Mutate(
            mutated,
            "Amortisman ve İtfa Gideri İle İlgili Düzeltmeler",
            "Amortisman Düzeltmesi (Yeniden Adlandırıldı)");
        mutated = Mutate(
            mutated,
            "DÖNEM SONU NAKİT VE NAKİT BENZERLERİ",
            "DÖNEM SONU NAKİT (Yeniden Adlandırıldı)");

        var report = KapPublicReportDecoder.Decode(mutated, "ASELS");

        var currentCashFlow = report.LinesFor("CF", CurrentYearToDate);
        Assert.Equal(51, currentCashFlow.Count);
        Assert.DoesNotContain(
            currentCashFlow,
            l => StatementMapper.CanonicalCodes.Contains(l.ItemCode));

        var finding = Assert.Single(
            report.Findings,
            f => f.ReasonCode == KapPublicReportReasons.NoCanonicalLines);
        Assert.Equal("CF", finding.StatementType);
        Assert.Equal(102, finding.Count); // both CF columns, zero canonical lines
        Assert.Contains("Nakit Akış Tablosu (Dolaylı Yöntem)", finding.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// TC-MDF-108 (vi) — the FY guard's <b>success</b> direction: when the declared
    /// <c>Periyot</c> and the observed column durations agree at 12 months, the rows type
    /// <c>FY</c> with no <c>PERIOD_DURATION_MISMATCH</c>, and the aggregate FY filter (the DCF
    /// baseline's own <c>PeriodType == "FY"</c> selection) picks the cash-flow row up. Derived
    /// from the committed half-year capture by aligning its declaration and its year-to-date
    /// headers to a 12-month span ending 30.06.2026 — never a new committed fixture.
    /// </summary>
    [Fact]
    public async Task TC_MDF_108_agreed_year_declaration_types_fy_end_to_end()
    {
        var annual = Mutate(Capture, "   Periyot:2", "   Periyot:4");
        // The IS year-to-date pair (columns 0 and 1) and the CF pair become 12-month spans;
        // the IS 3-month sub-period pair stays as captured (it is skipped either way).
        annual = Mutate(annual, "01.01.2026 - 30.06.2026", "01.07.2025 - 30.06.2026");
        annual = Mutate(annual, "01.01.2026 - 30.06.2026", "01.07.2025 - 30.06.2026");
        annual = Mutate(annual, "01.01.2025 - 30.06.2025", "01.07.2024 - 30.06.2025");
        annual = Mutate(annual, "01.01.2025 - 30.06.2025", "01.07.2024 - 30.06.2025");

        var report = KapPublicReportDecoder.Decode(annual, "ASELS");
        Assert.Equal("Yıllık", report.Header.PeriodType); // self-declared, surfaced verbatim
        Assert.Equal(0, report.Count(KapPublicReportReasons.PeriodDurationMismatch));

        // The CF row is an annual row at the same period end, carrying the section-total OCF.
        var currentCashFlow = report.Payload.Statements.Single(
            s => s.StatementType == "CF" && s.PeriodEndDate == CurrentYearToDate);
        Assert.Equal("FY", currentCashFlow.PeriodType);
        Assert.Equal(15_206_440m, currentCashFlow.Lines["OCF"]);

        // The aggregate FY filter — the selection the DCF baseline and the metrics engines use —
        // picks the row up end-to-end from the store.
        var (store, instrumentId) = await StoreAsync();
        var result = await store.UpsertAsync(
            "kap-public",
            new DateTimeOffset(2026, 8, 4, 18, 39, 41, TimeSpan.Zero),
            report.Payload);
        Assert.Equal(6, result.Inserted);

        await using var db = _postgres.CreateContext();
        var annualOcf = await (
                from line in db.FinLineItems
                join statement in db.FinancialStatements on line.StatementId equals statement.Id
                where statement.InstrumentId == instrumentId
                      && statement.StatementType == "CF"
                      && statement.PeriodType == "FY"
                      && statement.PeriodEndDate == CurrentYearToDate
                      && line.ItemCode == "OCF"
                select line.Value)
            .SingleAsync();
        Assert.Equal(15_206_440_000m, annualOcf);
    }

    // =================================================================================
    // Harness
    // =================================================================================

    /// <summary>
    /// A store bound to the migrated test container with the ASELS instrument present — the
    /// decode→store→metric path is the production one, nothing is re-implemented for the test.
    /// </summary>
    private async Task<(FinancialStatementStore Store, long InstrumentId)> StoreAsync()
    {
        var db = _postgres.CreateContext();
        if (!await db.Instruments.AnyAsync(i => i.Symbol == "ASELS"))
        {
            db.Instruments.Add(new Instrument
            {
                Symbol = "ASELS",
                Name = "ASELSAN ELEKTRONİK SANAYİ VE TİCARET A.Ş.",
                Status = "active",
                SourceRef = "test:kap-public-report",
                RecordedAt = new DateTimeOffset(2026, 8, 4, 0, 0, 0, TimeSpan.Zero),
            });
            await db.SaveChangesAsync();
        }

        await db.Database.ExecuteSqlRawAsync("DELETE FROM fin_line_items");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM financial_statements");

        var instrumentId = await db.Instruments.Where(i => i.Symbol == "ASELS").Select(i => i.Id).SingleAsync();
        return (new FinancialStatementStore(db), instrumentId);
    }

    /// <summary>
    /// Replaces the <paramref name="occurrence"/>-th occurrence of <paramref name="oldValue"/>
    /// — the negative cases are derived from the committed capture, never invented.
    /// </summary>
    private static string Mutate(string body, string oldValue, string newValue, int occurrence = 0)
    {
        var index = -1;
        for (var i = 0; i <= occurrence; i++)
        {
            index = body.IndexOf(oldValue, index + 1, StringComparison.Ordinal);
            Assert.True(index >= 0, $"mutation needle not found (occurrence {occurrence}): {oldValue}");
        }

        return string.Concat(body.AsSpan(0, index), newValue, body.AsSpan(index + oldValue.Length));
    }
}
