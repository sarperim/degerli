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
/// <b>The capture set since TKT-foundation-088.</b> Three further bodies were captured and
/// committed as bounded-trim doubles — TÜPRAŞ 2025 Yıllık (<c>kap-public-report-1554106</c>),
/// ASELSAN 2025 Yıllık (<c>kap-public-report-1561039</c>) and MİGROS 2026-06
/// (<c>kap-public-report-1648240</c>) — because the interim above carries no annual columns and
/// no debt split, and because the first live walk's IS/BS gap had to be reproduced from the very
/// bodies it stored nothing for. TC-MDF-117 (§28) is the acceptance contract for that set; every
/// body is still the <b>whole</b> input, and no case calls <c>kap.org.tr</c>.
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

    /// <summary>
    /// Every committed excel-HTML report body, keyed by the catalog name the label map cites as
    /// provenance. The bodies are the only evidence a map entry may stand on (TC-MDF-107 (i) /
    /// TC-MDF-117 (f)): a spelling outside this set is un-evidenced by construction.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> CaptureBodies =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RealSourceCatalog.KapPublicReport] = RealSourceCatalog.Get(RealSourceCatalog.KapPublicReport).Body,
            [RealSourceCatalog.KapPublicReportTuprs] = RealSourceCatalog.Get(RealSourceCatalog.KapPublicReportTuprs).Body,
            [RealSourceCatalog.KapPublicReportAselsAnnual] = RealSourceCatalog.Get(RealSourceCatalog.KapPublicReportAselsAnnual).Body,
            [RealSourceCatalog.KapPublicReportMgros] = RealSourceCatalog.Get(RealSourceCatalog.KapPublicReportMgros).Body,
        };

    /// <summary>
    /// The live reports TKT-foundation-088 committed (bounded trims): the filer, the filing
    /// coordinates its own header declares, and the period ends its columns carry.
    /// </summary>
    private static readonly LiveCapture[] LiveCaptures =
    [
        new(RealSourceCatalog.KapPublicReportTuprs, "TUPRS", 2025, 4, "Yıllık",
            new DateOnly(2025, 12, 31), new DateOnly(2024, 12, 31), new DateOnly(2025, 12, 31), new DateOnly(2024, 12, 31)),
        new(RealSourceCatalog.KapPublicReportAselsAnnual, "ASELS", 2025, 4, "Yıllık",
            new DateOnly(2025, 12, 31), new DateOnly(2024, 12, 31), new DateOnly(2025, 12, 31), new DateOnly(2024, 12, 31)),
        new(RealSourceCatalog.KapPublicReportMgros, "MGROS", 2026, 2, "6 Aylık",
            new DateOnly(2026, 6, 30), new DateOnly(2025, 12, 31), new DateOnly(2026, 6, 30), new DateOnly(2025, 6, 30)),
    ];

    private sealed record LiveCapture(
        string Name,
        string Symbol,
        int Year,
        int Period,
        string PeriodType,
        DateOnly CurrentBalanceSheet,
        DateOnly PriorBalanceSheet,
        DateOnly CurrentFlow,
        DateOnly PriorFlow);

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
    /// TC-MDF-107 (i) — the label→canonical map is built <b>only</b> from labels a committed
    /// capture actually carries, each entry naming the capture that supplied the spelling, and
    /// every target is a canonical chart-of-accounts code (<c>StatementMapper</c> untouched).
    /// <para>
    /// <b>Amended by TKT-foundation-088 (user-approved 2026-10-27), never weakened.</b> The
    /// assertion was a single-capture equality (<c>rule.Provenance == CaptureName</c>, the label
    /// in <c>kap-public-report-1643141</c>) while that was the only committed body. The ticket's
    /// clause 3 requires every entry to name <b>its</b> capture, and three further bodies landed,
    /// so the same invariant is now asserted against the capture set: the provenance is one of
    /// <see cref="KapPublicReportDecoder.CaptureNames"/> <b>and</b> the label is present in the
    /// body that provenance names. Nothing that was pinned before is unpinned — an entry citing
    /// an uncommitted body, or a label absent from the body it cites, still fails.
    /// </para>
    /// </summary>
    [Fact]
    public void TC_MDF_107_label_map_is_built_only_from_captured_labels_with_provenance()
    {
        var map = KapPublicReportDecoder.LabelMap;

        Assert.NotEmpty(map);
        foreach (var rule in map)
        {
            Assert.Contains(rule.Provenance, KapPublicReportDecoder.CaptureNames);
            Assert.True(
                CaptureBodies.TryGetValue(rule.Provenance, out var provenanceBody),
                $"label map cites '{rule.Provenance}', which is not a committed capture body");
            Assert.Contains(rule.Label, provenanceBody!, StringComparison.Ordinal);
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
    // TC-MDF-117 — the live captures' IS/BS rows store, and the map is proven by them
    // =================================================================================

    /// <summary>
    /// TC-MDF-117 (a) — <b>the red case.</b> The first live <c>kap-public</c> walk stored 62
    /// statements across 7 companies: 60 CF / 2 IS / 0 BS — the income statements and balance
    /// sheets the reports plainly contain produced no statement rows. Each committed live body
    /// now decodes to its full payload (BS/IS/CF × current + comparative, six rows) with no
    /// conflicting group and no <c>DUPLICATE_LABEL_CONFLICT</c>, and the production store writes
    /// <b>all six</b> — the row that used to be quarantined whole is stored, for all three
    /// filers, on the same bodies.
    /// </summary>
    [Fact]
    public async Task TC_MDF_117_every_captured_live_report_decodes_and_stores_its_is_and_bs_rows()
    {
        foreach (var capture in LiveCaptures)
        {
            var report = KapPublicReportDecoder.Decode(CaptureBodies[capture.Name], capture.Symbol);

            // The report identity is the capture's own, not the caller's argument.
            Assert.Equal(capture.Symbol, report.Symbol);
            Assert.Equal(capture.Year, report.Header.Year);
            Assert.Equal(capture.Period, report.Header.Period);
            Assert.Equal(capture.PeriodType, report.Header.PeriodType);
            Assert.Equal("1.000 TL", report.Header.PresentationUnit);

            // Six rows: BS/IS/CF × (current, comparative) — the equity matrix still drops whole
            // (`NO_COLUMN_HEADERS`, pinned by TC-MDF-106 (ii)) and contributes no row.
            Assert.Equal(6, report.Payload.Statements.Count);
            Assert.Equal(
                new[] { "BS", "BS", "CF", "CF", "IS", "IS" },
                report.Payload.Statements.Select(s => s.StatementType).OrderBy(t => t, StringComparer.Ordinal).ToArray());
            Assert.Contains(report.Payload.Statements, s => s.StatementType == "BS" && s.PeriodEndDate == capture.CurrentBalanceSheet);
            Assert.Contains(report.Payload.Statements, s => s.StatementType == "BS" && s.PeriodEndDate == capture.PriorBalanceSheet);
            Assert.Contains(report.Payload.Statements, s => s.StatementType == "IS" && s.PeriodEndDate == capture.CurrentFlow);
            Assert.Contains(report.Payload.Statements, s => s.StatementType == "IS" && s.PeriodEndDate == capture.PriorFlow);

            // Nothing is quarantined — the live symptom, asserted away.
            Assert.All(report.Payload.Statements, fact => Assert.Null(fact.ConflictingItemCodes));
            Assert.Equal(0, report.Count(KapPublicReportReasons.DuplicateLabelConflict));

            // …and every row carries canonical coverage, which is what the metrics consume.
            foreach (var fact in report.Payload.Statements)
            {
                Assert.Contains(fact.Lines.Keys, code => StatementMapper.CanonicalCodes.Contains(code));
            }

            var (store, instrumentId) = await StoreAsync(capture.Symbol);
            var result = await store.UpsertAsync(
                "kap-public",
                new DateTimeOffset(capture.Year, 8, 4, 18, 0, 0, TimeSpan.Zero),
                report.Payload);

            Assert.Equal(6, result.Inserted);
            Assert.Equal(0, result.Unchanged);
            Assert.Empty(result.RejectedReasonCodes);

            await using var db = _postgres.CreateContext();
            var stored = await db.FinancialStatements
                .Where(s => s.InstrumentId == instrumentId && s.SourceRef == "kap-public")
                .Select(s => s.StatementType)
                .ToListAsync();
            Assert.Equal(2, stored.Count(t => t == "BS"));
            Assert.Equal(2, stored.Count(t => t == "IS"));
            Assert.Equal(2, stored.Count(t => t == "CF"));
        }
    }

    /// <summary>
    /// TC-MDF-117 (b) — the ticket's headline consumer: the DCF's <c>debt</c> and <c>cash</c>
    /// inputs come from the balance sheet, with the same honest provenance as FCF. TÜPRAŞ's
    /// FY2025 balance sheet stores the financial borrowings split (<c>ST_DEBT</c>/<c>LT_DEBT</c>)
    /// and cash, so <c>debt = ST_DEBT + LT_DEBT</c> and <c>cash = CASH</c> exist as stored facts.
    /// Trade payables and every operating liability stay <c>OTHER_BS_*</c>; the current portion
    /// of long-term borrowings is a separate presentation line and stays a labelled companion —
    /// a combined/aggregated debt figure is never manufactured here.
    /// </summary>
    [Fact]
    public async Task TC_MDF_117_tuprs_fy2025_balance_sheet_stores_the_debt_and_cash_core()
    {
        var capture = LiveCaptures[0];
        var report = KapPublicReportDecoder.Decode(CaptureBodies[capture.Name], capture.Symbol);
        var (store, instrumentId) = await StoreAsync(capture.Symbol);

        var result = await store.UpsertAsync(
            "kap-public",
            new DateTimeOffset(2026, 2, 6, 18, 34, 29, TimeSpan.Zero),
            report.Payload);
        Assert.Equal(6, result.Inserted);
        Assert.Empty(result.RejectedReasonCodes);

        await using var db = _postgres.CreateContext();
        var balanceSheet = await db.FinancialStatements.SingleAsync(
            s => s.InstrumentId == instrumentId
                 && s.StatementType == "BS"
                 && s.PeriodEndDate == capture.CurrentBalanceSheet);
        Assert.Equal("FY", balanceSheet.PeriodType);
        Assert.Equal("thousands", balanceSheet.SourceUnitScale);
        Assert.Equal("TRY", balanceSheet.SourceCurrency);

        var lines = await db.FinLineItems
            .Where(l => l.StatementId == balanceSheet.Id)
            .ToDictionaryAsync(l => l.ItemCode, l => l.Value);

        // Normalized to single TL by the store's own scale (02 §3.1): thousand TL × 1.000.
        Assert.Equal(8_957_100_000m, lines["ST_DEBT"]);      // Kısa Vadeli Borçlanmalar
        Assert.Equal(28_428_775_000m, lines["LT_DEBT"]);     // Uzun Vadeli Borçlanmalar
        Assert.Equal(107_237_211_000m, lines["CASH"]);       // Nakit ve Nakit Benzerleri
        Assert.Equal(369_827_994_000m, lines["TOTAL_EQUITY"]);
        Assert.Equal(236_931_136_000m, lines["CUR_ASSETS"]);
        Assert.Equal(169_748_201_000m, lines["CUR_LIAB"]);

        // The consumer's own arithmetic (`DcfBaselineGenerationJob`: debt = ST_DEBT + LT_DEBT).
        Assert.Equal(37_385_875_000m, lines["ST_DEBT"] + lines["LT_DEBT"]);

        // Financial borrowings only: the canonical surfaces of this balance sheet are exactly
        // the six mapped codes — trade payables and operating liabilities never became debt.
        Assert.Equal(
            new[] { "CASH", "CUR_ASSETS", "CUR_LIAB", "LT_DEBT", "ST_DEBT", "TOTAL_EQUITY" },
            lines.Keys
                .Where(code => StatementMapper.CanonicalCodes.Contains(code))
                .OrderBy(code => code, StringComparer.Ordinal)
                .ToArray());
        Assert.Equal(105_513_256_000m, lines["OTHER_BS_Ticari_Borçlar"]);

        // The current portion of long-term borrowings is a presentation line of its own: kept
        // and labelled, never merged into ST_DEBT (the recorded, routed gap — a split or an
        // aggregate would be a modelling decision, not a decode).
        Assert.Equal(
            12_863_610_000m,
            lines["OTHER_BS_Uzun_Vadeli_Borçlanmaların_Kısa_Vadeli_Kısımları"]);
        Assert.DoesNotContain(
            "OTHER_BS_Uzun_Vadeli_Borçlanmaların_Kısa_Vadeli_Kısımları",
            StatementMapper.CanonicalCodes);
    }

    /// <summary>
    /// TC-MDF-117 (c) — the income statement's canonical ladder, evidenced by the ASELSAN 2025
    /// Yıllık capture and stored end-to-end: revenue/COGS/gross profit beside the operating
    /// subtotal, SGA, finance expense, pre-tax profit, tax and the bottom line. The statement's
    /// own passthrough companions stay beside them (the same profit under the taxonomy's
    /// differently-spelled subtotal label is a separate line, never a second <c>NI</c>).
    /// </summary>
    [Fact]
    public async Task TC_MDF_117_annual_income_statement_stores_the_canonical_ladder()
    {
        var capture = LiveCaptures[1];
        var report = KapPublicReportDecoder.Decode(CaptureBodies[capture.Name], capture.Symbol);

        var income = report.Payload.Statements.Single(
            s => s.StatementType == "IS" && s.PeriodEndDate == capture.CurrentFlow);
        Assert.Equal("FY", income.PeriodType);
        Assert.Null(income.ConflictingItemCodes);

        // Thousand TL, raw-attribute integers (TC-MDF-107 (ii) pins the read path).
        Assert.Equal(180_444_938m, income.Lines["REV"]);              // Satış Gelirleri
        Assert.Equal(-123_017_908m, income.Lines["COGS"]);            // Satışların Maliyeti
        Assert.Equal(57_427_030m, income.Lines["GROSS_PROFIT"]);
        Assert.Equal(-6_236_808m, income.Lines["SGA"]);               // Genel Yönetim Giderleri
        Assert.Equal(49_145_855m, income.Lines["EBIT"]);              // ESAS FAALİYET KARI (ZARARI)
        Assert.Equal(-13_369_696m, income.Lines["FIN_EXP"]);          // Finansman Giderleri
        Assert.Equal(26_390_612m, income.Lines["PRETAX_INC"]);
        Assert.Equal(3_527_115m, income.Lines["TAX_EXP"]);
        Assert.Equal(29_917_727m, income.Lines["NI"]);                // DÖNEM KARI (ZARARI)

        // The same profit under the statement's other sub-total label is its own line, so the
        // ladder neither double-counts NI nor loses the filer's own presentation.
        Assert.Equal(29_917_727m, income.Lines["OTHER_IS_SÜRDÜRÜLEN_FAALİYETLER_DÖNEM_KARI_ZARARI"]);
        Assert.Equal(57_427_030m, income.Lines["OTHER_IS_BRÜT_KAR_ZARAR"]);

        var (store, instrumentId) = await StoreAsync(capture.Symbol);
        var result = await store.UpsertAsync(
            "kap-public",
            new DateTimeOffset(2026, 2, 24, 18, 27, 36, TimeSpan.Zero),
            report.Payload);
        Assert.Equal(6, result.Inserted);

        await using var db = _postgres.CreateContext();
        var statement = await db.FinancialStatements.SingleAsync(
            s => s.InstrumentId == instrumentId
                 && s.StatementType == "IS"
                 && s.PeriodEndDate == capture.CurrentFlow);
        var stored = await db.FinLineItems
            .Where(l => l.StatementId == statement.Id)
            .ToDictionaryAsync(l => l.ItemCode, l => l.Value);

        Assert.Equal(49_145_855_000m, stored["EBIT"]);
        Assert.Equal(29_917_727_000m, stored["NI"]);
        Assert.Equal(180_444_938_000m, stored["REV"]);
    }

    /// <summary>
    /// TC-MDF-117 (d) — a repeated <c>OTHER_*</c> passthrough label inside one statement is what
    /// quarantined every live balance sheet. Two real presentation lines keep <b>both</b> values
    /// under a counted, deterministic <c>{code}_n</c>: no conflict, no overwrite, the row stores,
    /// and the finding names the label and the kept code verbatim. Metrics ignore passthrough
    /// codes either way, so no canonical figure is invented by the suffix.
    /// </summary>
    [Fact]
    public void TC_MDF_117_repeated_passthrough_label_keeps_both_values_and_is_not_a_conflict()
    {
        var capture = LiveCaptures[0];
        var report = KapPublicReportDecoder.Decode(CaptureBodies[capture.Name], capture.Symbol);
        var balanceSheet = report.Payload.Statements.Single(
            s => s.StatementType == "BS" && s.PeriodEndDate == capture.CurrentBalanceSheet);

        Assert.Null(balanceSheet.ConflictingItemCodes);
        Assert.Equal(0, report.Count(KapPublicReportReasons.DuplicateLabelConflict));

        // 7 repeated balance-sheet labels + 4 repeated income-statement labels, each in the
        // current and the comparative column (the fourth `Türev Araçlar` worth two).
        Assert.Equal(22, report.Count(KapPublicReportReasons.DuplicateLabelDisambiguated));

        // `ARA TOPLAM` is the current-block subtotal and the liabilities-block subtotal: two
        // real lines with different values, both kept.
        Assert.Equal(236_931_136m, balanceSheet.Lines["OTHER_BS_ARA_TOPLAM"]);
        Assert.Equal(169_748_201m, balanceSheet.Lines["OTHER_BS_ARA_TOPLAM_2"]);

        // Four `Türev Araçlar` rows across the two blocks → `_2`, `_3`, `_4`, in capture order.
        Assert.Equal(1_398_352m, balanceSheet.Lines["OTHER_BS_Türev_Araçlar"]);
        Assert.Equal(13_735m, balanceSheet.Lines["OTHER_BS_Türev_Araçlar_2"]);
        Assert.Equal(712_032m, balanceSheet.Lines["OTHER_BS_Türev_Araçlar_3"]);
        Assert.Equal(431_098m, balanceSheet.Lines["OTHER_BS_Türev_Araçlar_4"]);

        // The counts are honest and the finding is verbatim: nothing is silently dropped.
        var findings = report.Findings
            .Where(f => f.ReasonCode == KapPublicReportReasons.DuplicateLabelDisambiguated)
            .ToList();
        Assert.Contains(findings, f => f.Label == "ARA TOPLAM"
            && f.Detail.Contains("OTHER_BS_ARA_TOPLAM_2", StringComparison.Ordinal));
        Assert.All(findings, f => Assert.Equal(1, f.Count));
        Assert.Equal(22, findings.Count);

        // A disambiguated code is never a canonical code, and the same body always produces
        // the same codes.
        var suffixed = balanceSheet.Lines.Keys
            .Where(code => code.Length > 2 && code[^2] == '_' && char.IsAsciiDigit(code[^1]))
            .ToList();
        Assert.Contains("OTHER_BS_ARA_TOPLAM_2", suffixed);
        Assert.All(suffixed, code => Assert.DoesNotContain(code, StatementMapper.CanonicalCodes));
        var again = KapPublicReportDecoder.Decode(CaptureBodies[capture.Name], capture.Symbol);
        Assert.Equal(
            balanceSheet.Lines.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"),
            again.Payload.Statements
                .Single(s => s.StatementType == "BS" && s.PeriodEndDate == capture.CurrentBalanceSheet)
                .Lines.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"));
    }

    /// <summary>
    /// TC-MDF-117 (e) — the duplicate rule is not weakened for canonical codes. The same
    /// unequal-duplicate condition on a mapped code is still a counted conflict (both values
    /// retained in the finding, the first value kept in the line) and the store still
    /// quarantines that statement row as <c>CONFLICTING_VALUE</c> — derived from the annual
    /// capture by renaming one real income-statement row onto the bottom-line label.
    /// </summary>
    [Fact]
    public async Task TC_MDF_117_canonical_code_duplicate_still_conflicts_and_quarantines()
    {
        var capture = LiveCaptures[1];
        var mutated = Mutate(
            CaptureBodies[capture.Name],
            "Kontrol Gücü Olmayan Paylar",
            "DÖNEM KARI (ZARARI)",
            occurrence: 1); // the income statement's own row (occurrence 0 is the balance sheet's)
        var report = KapPublicReportDecoder.Decode(mutated, capture.Symbol);

        // One conflict per column (current and comparative), each retaining both values.
        Assert.Equal(2, report.Count(KapPublicReportReasons.DuplicateLabelConflict));
        Assert.Equal(42, report.Count(KapPublicReportReasons.DuplicateLabelDisambiguated));

        var income = report.Payload.Statements.Single(
            s => s.StatementType == "IS" && s.PeriodEndDate == capture.CurrentFlow);
        Assert.Equal(new[] { "NI" }, income.ConflictingItemCodes!);
        Assert.Equal(29_917_727m, income.Lines["NI"]); // the first value is retained, never the later one

        var conflict = Assert.Single(
            report.Findings,
            f => f.ReasonCode == KapPublicReportReasons.DuplicateLabelConflict
                && f.StatementType == "IS"
                && f.Detail.Contains("2025-12-31", StringComparison.Ordinal));
        Assert.Contains("NI", conflict.Detail, StringComparison.Ordinal);
        Assert.Contains("29.917.727", conflict.Detail, StringComparison.Ordinal); // retained
        Assert.Contains("-31.790", conflict.Detail, StringComparison.Ordinal);    // retained, not dropped

        var (store, instrumentId) = await StoreAsync(capture.Symbol);
        var result = await store.UpsertAsync(
            "kap-public",
            new DateTimeOffset(2026, 2, 24, 18, 27, 36, TimeSpan.Zero),
            report.Payload);

        // Both income-statement rows conflict (the renamed row carries a value in both
        // columns); the other four rows still land — a partial batch, never a lost report.
        Assert.Equal(4, result.Inserted);
        Assert.Contains("CONFLICTING_VALUE", result.RejectedReasonCodes);

        await using var db = _postgres.CreateContext();
        Assert.False(await db.FinancialStatements.AnyAsync(
            s => s.InstrumentId == instrumentId && s.StatementType == "IS"));
    }

    /// <summary>
    /// TC-MDF-117 (f) — the map extension's own evidence rule: every entry names a capture that
    /// is actually committed, and the spelling it claims is present in that capture's body. The
    /// debt/cash core and the income-tax ladder are cited from the filer that carried them, and
    /// the map is still a function of (statement, label).
    /// </summary>
    [Fact]
    public void TC_MDF_117_every_label_map_entry_names_the_capture_that_carries_it()
    {
        Assert.Equal(
            new[]
            {
                RealSourceCatalog.KapPublicReport,
                RealSourceCatalog.KapPublicReportTuprs,
                RealSourceCatalog.KapPublicReportAselsAnnual,
                RealSourceCatalog.KapPublicReportMgros,
            }.OrderBy(name => name, StringComparer.Ordinal),
            KapPublicReportDecoder.CaptureNames.OrderBy(name => name, StringComparer.Ordinal));

        var map = KapPublicReportDecoder.LabelMap;
        foreach (var rule in map)
        {
            Assert.Contains(rule.Provenance, KapPublicReportDecoder.CaptureNames);
            Assert.True(
                CaptureBodies.TryGetValue(rule.Provenance, out var body),
                $"label map cites '{rule.Provenance}', which is not a committed capture body");
            Assert.Contains(rule.Label, body!, StringComparison.Ordinal);
        }

        // The debt/cash core is cited from the TÜPRAŞ annual body that carries the split …
        Assert.Contains(map, r => r is
        {
            StatementType: "BS",
            Label: "Kısa Vadeli Borçlanmalar",
            ItemCode: "ST_DEBT",
            Provenance: RealSourceCatalog.KapPublicReportTuprs,
        });
        Assert.Contains(map, r => r is
        {
            StatementType: "BS",
            Label: "Uzun Vadeli Borçlanmalar",
            ItemCode: "LT_DEBT",
            Provenance: RealSourceCatalog.KapPublicReportTuprs,
        });

        // … and the annual income statement's ladder from the ASELSAN annual body.
        foreach (var code in new[] { "EBIT", "SGA", "FIN_EXP", "PRETAX_INC", "TAX_EXP", "NI" })
        {
            Assert.Contains(map, r => r.StatementType == "IS"
                && r.ItemCode == code
                && r.Provenance == RealSourceCatalog.KapPublicReportAselsAnnual);
        }

        // Never a spelling a committed body does not carry (the trap: a trade payable is not
        // debt, an operating liability is not debt).
        Assert.DoesNotContain(map, r => r.ItemCode is "ST_DEBT" or "LT_DEBT"
            && (r.Label.Contains("Ticari", StringComparison.Ordinal)
                || r.Label.Contains("Kiralama", StringComparison.Ordinal)
                || r.Label.Contains("Kısa Vadeli Kısımları", StringComparison.Ordinal)));

        // Still a function: one entry per (statement, label).
        Assert.Equal(map.Count, map.Select(r => (r.StatementType, r.Label)).Distinct().Count());
    }

    // =================================================================================
    // TC-MDF-119 — cumulative (year-to-date) vs discrete-period semantics
    // =================================================================================

    /// <summary>
    /// TC-MDF-119 (i) — <b>the determination, taken from the committed interim body's own
    /// column headers and cells.</b> The MİGROS 2026-06 filing (public index <c>1648240</c>,
    /// <c>Periyot 2</c> → <c>6 Aylık</c>) declares its main income-statement and cash-flow
    /// columns as <c>Cari Dönem 01.01.2026 - 30.06.2026</c> — a <b>six-month cumulative
    /// (year-to-date) range</b>. The income-statement tables carry the discrete quarter as a
    /// <b>separate</b> <c>Cari Dönem 3 Aylık 01.04.2026 - 30.06.2026</c> column; the
    /// cash-flow table carries <b>only</b> the two declared-range columns (current and prior),
    /// so the CF figure's span is attested by its own declared range alone. The decoder's
    /// column selection is therefore <b>not</b> storing a year-to-date figure under a
    /// discrete-quarter label: it decodes the column whose declared duration is the report's
    /// own, and names + skips the quarter columns where they exist. The arithmetic is the
    /// body's own and is decisive — the decoded <c>NI</c> is the six-month cell
    /// (<c>1.078.256</c>), while the three-month column's <c>DÖNEM KARI (ZARARI)</c> cell is
    /// <c>-644.936</c>; the decoded <c>REV</c> is the six-month <c>Hasılat</c>
    /// (<c>241.298.039</c>), not the three-month <c>124.399.203</c>.
    /// </summary>
    [Fact]
    public void TC_MDF_119_the_interim_column_is_the_declared_cumulative_range_never_a_quarter()
    {
        var body = CaptureBodies[RealSourceCatalog.KapPublicReportMgros];
        var report = KapPublicReportDecoder.Decode(body, "MGROS");

        Assert.Equal(2026, report.Header.Year);
        Assert.Equal(2, report.Header.Period);
        Assert.Equal("6 Aylık", report.Header.PeriodType);
        Assert.Equal(6, report.Header.PeriodMonths);

        // The cash-flow table's main column declares the six-month range its figures cover.
        var cashFlow = report.Tables.Single(t => t.StatementType == "CF");
        var currentFlow = cashFlow.Columns[0];
        Assert.Equal("Cari Dönem 01.01.2026 - 30.06.2026", currentFlow.Verbatim);
        Assert.Equal(new DateOnly(2026, 1, 1), currentFlow.PeriodStart);
        Assert.Equal(new DateOnly(2026, 6, 30), currentFlow.PeriodEnd);
        Assert.Equal(6, currentFlow.DurationMonths);
        Assert.True(currentFlow.Decoded);
        Assert.Null(currentFlow.SkipReason);

        // The cash-flow table has exactly two columns — current and prior, both declared
        // ranges. Unlike the income-statement tables it has no discrete `3 Aylık` sibling
        // (TKT-foundation-090), so the CF span rests on its own declaration.
        Assert.Equal(2, cashFlow.Columns.Count);
        Assert.All(cashFlow.Columns, column =>
        {
            Assert.Equal(6, column.DurationMonths);
            Assert.True(column.Decoded);
        });

        // The discrete quarter is its own column, named and skipped — never merged, never
        // stored under the same period end.
        var quarters = report.Tables
            .Where(t => t.StatementType == "IS")
            .SelectMany(t => t.Columns)
            .Where(c => c.DurationMonths == 3)
            .ToList();
        Assert.Equal(4, quarters.Count); // current + comparative, in both IS tables
        Assert.All(quarters, column =>
        {
            Assert.False(column.Decoded);
            Assert.Equal(KapPublicReportReasons.SubPeriodColumn, column.SkipReason);
            Assert.Contains("3 Aylık", column.Verbatim, StringComparison.Ordinal);
        });
        Assert.Contains(quarters, c => c.PeriodStart == new DateOnly(2026, 4, 1)
            && c.PeriodEnd == new DateOnly(2026, 6, 30)
            && c.IsCurrent);
        Assert.True(report.Count(KapPublicReportReasons.SubPeriodColumn) > 0);

        // Both columns are in the body; the decoded figure is the cumulative one, and the
        // duration travels with every decoded line.
        Assert.Contains("241298039", body, StringComparison.Ordinal);   // 6M Hasılat (raw)
        Assert.Contains("124399203", body, StringComparison.Ordinal);   // 3M Hasılat (raw)
        Assert.Contains("-644936", body, StringComparison.Ordinal);     // 3M DÖNEM KARI (raw)
        Assert.Contains("1078256", body, StringComparison.Ordinal);     // 6M DÖNEM KARI (raw)

        var income = report.Payload.Statements.Single(
            s => s.StatementType == "IS" && s.PeriodEndDate == new DateOnly(2026, 6, 30));
        Assert.Equal(241_298_039m, income.Lines["REV"]);
        Assert.Equal(1_078_256m, income.Lines["NI"]);

        var cash = report.Payload.Statements.Single(
            s => s.StatementType == "CF" && s.PeriodEndDate == new DateOnly(2026, 6, 30));
        Assert.Equal(17_837_860m, cash.Lines["OCF"]);
        Assert.Equal(-6_297_863m, cash.Lines["CAPEX"]);
        Assert.Equal(11_539_997m, CanonicalMetrics.FreeCashFlow(cash.Lines["OCF"], cash.Lines["CAPEX"]));

        // Every decoded flow value states the span it was decoded under: six months from
        // 1 January — the decode result's own provenance, which the decode's consumers and
        // tests can read (TC-MDF-119 (iii)). The store and the served payload carry no span.
        var flowLines = report.LinesFor("CF", new DateOnly(2026, 6, 30));
        Assert.NotEmpty(flowLines);
        Assert.All(flowLines, line =>
        {
            Assert.Equal(new DateOnly(2026, 1, 1), line.PeriodStartDate);
            Assert.Equal(6, line.DurationMonths);
        });
        Assert.All(
            report.LinesFor("IS", new DateOnly(2026, 6, 30)),
            line => Assert.Equal(6, line.DurationMonths));
    }

    /// <summary>
    /// TC-MDF-119 (ii) — <b>the annual bodies are twelve-month columns, and their FCF is the
    /// year</b> — so the FY basis the DCF consumes is unaffected by cumulative semantics. The
    /// TÜPRAŞ 2025 Yıllık body's cash-flow column declares <c>01.01.2025 - 31.12.2025</c> and
    /// its own cells give OCF <c>49.464.734</c> − |CAPEX <c>19.118.339</c>| = <b>30.346.395</b>
    /// thousand TL = ₺30,35bn (the recorded FY2025 figure); the ASELSAN 2025 Yıllık body gives
    /// <c>48.901.969</c> − |<c>38.168.467</c>| = <b>10.733.502</b>. Both statements exist once
    /// per column and both are typed <c>FY</c> because a twelve-month duration attests them.
    /// </summary>
    [Fact]
    public void TC_MDF_119_the_annual_columns_are_twelve_months_and_their_fcf_is_the_year()
    {
        foreach (var (name, symbol, ocf, capex, fcf) in new (string, string, decimal, decimal, decimal)[]
        {
            (RealSourceCatalog.KapPublicReportTuprs, "TUPRS", 49_464_734m, -19_118_339m, 30_346_395m),
            (RealSourceCatalog.KapPublicReportAselsAnnual, "ASELS", 48_901_969m, -38_168_467m, 10_733_502m),
        })
        {
            var report = KapPublicReportDecoder.Decode(CaptureBodies[name], symbol);
            Assert.Equal("Yıllık", report.Header.PeriodType);
            Assert.Equal(4, report.Header.Period);

            var cashFlow = report.Tables.Single(t => t.StatementType == "CF");
            Assert.All(cashFlow.Columns, column =>
            {
                Assert.Equal(12, column.DurationMonths);
                Assert.True(column.Decoded);
            });
            Assert.Equal(new DateOnly(2025, 1, 1), cashFlow.Columns[0].PeriodStart);
            Assert.Equal(new DateOnly(2025, 12, 31), cashFlow.Columns[0].PeriodEnd);

            var annual = report.Payload.Statements.Single(
                s => s.StatementType == "CF" && s.PeriodEndDate == new DateOnly(2025, 12, 31));
            Assert.Equal("FY", annual.PeriodType);
            Assert.Equal(ocf, annual.Lines["OCF"]);
            Assert.Equal(capex, annual.Lines["CAPEX"]);
            Assert.Equal(fcf, CanonicalMetrics.FreeCashFlow(annual.Lines["OCF"], annual.Lines["CAPEX"]));

            Assert.All(
                report.LinesFor("CF", new DateOnly(2025, 12, 31)),
                line =>
                {
                    Assert.Equal(new DateOnly(2025, 1, 1), line.PeriodStartDate);
                    Assert.Equal(12, line.DurationMonths);
                });
        }
    }

    /// <summary>
    /// TC-MDF-119 (iii) — <b>a cumulative interim can never be consumed as an annual, proved
    /// through the store and the consumer's own predicate.</b> Decoded and written, the MİGROS
    /// 2026-06 report stores its six-month cumulative cash flow as <c>Q</c> at
    /// <c>2026-06-30</c> with the body's cumulative <c>OCF</c>; an FY-equality filter — the
    /// predicate every FY-only consumer applies (<c>DcfBaselineGenerationJob</c>:
    /// <c>s.PeriodType == "FY"</c>) — therefore matches <b>nothing</b> in what this report
    /// stored. The test re-applies that predicate over the stored rows; it does not run the job.
    /// A half-year can therefore never enter the annual basis — what the display *labels* that
    /// `Q` row is 057's surface, not this case's claim.
    /// </summary>
    [Fact]
    public async Task TC_MDF_119_a_cumulative_interim_is_stored_as_q_and_the_fy_filter_cannot_reach_it()
    {
        var report = KapPublicReportDecoder.Decode(
            CaptureBodies[RealSourceCatalog.KapPublicReportMgros], "MGROS");
        var (store, instrumentId) = await StoreAsync("MGROS");

        var result = await store.UpsertAsync(
            "kap-public",
            new DateTimeOffset(2026, 8, 11, 21, 43, 53, TimeSpan.Zero),
            report.Payload);
        Assert.Equal(6, result.Inserted);

        await using var db = _postgres.CreateContext();
        var stored = await db.FinancialStatements
            .Where(s => s.InstrumentId == instrumentId)
            .ToListAsync();
        Assert.Equal(6, stored.Count);
        Assert.All(stored, statement => Assert.Equal("Q", statement.PeriodType));

        // The consumer's FY filter — the only path into a DCF baseline.
        var annualBasis = stored.Where(s => s.PeriodType == "FY").ToList();
        Assert.Empty(annualBasis);

        var cashFlow = stored.Single(
            s => s.StatementType == "CF" && s.PeriodEndDate == new DateOnly(2026, 6, 30));
        var lines = await db.FinLineItems
            .Where(l => l.StatementId == cashFlow.Id)
            .ToDictionaryAsync(l => l.ItemCode, l => l.Value);
        // The six-month cumulative cell, scaled by the store (thousands → TL) — never the
        // three-month cell and never annualized. The store has no span field; the declared
        // span lives on the decode result (asserted in (i)).
        Assert.Equal(17_837_860_000m, lines["OCF"]);
        Assert.Equal(-6_297_863_000m, lines["CAPEX"]);
    }

    /// <summary>
    /// TC-MDF-119 (iv) — <b>the honest behaviour when the semantics cannot be determined.</b>
    /// A report that self-declares an annual period while <b>none</b> of its columns declares a
    /// readable date range attests nothing: the pre-ticket decoder accepted the declaration
    /// silently and typed its instant (balance-sheet) rows <c>FY</c>, which would let a
    /// half-year's <c>debt</c>/<c>cash</c> into the DCF's annual basis. Now the ambiguity is a
    /// named, counted finding (<c>PERIOD_SEMANTICS_UNDETERMINED</c>) and every row fails safe to
    /// <c>Q</c> — never FY on an unattested declaration, and never a guessed period for the
    /// flow tables whose columns cannot be attributed at all (those cells stay
    /// <c>COLUMN_UNATTRIBUTED</c> drops).
    /// </summary>
    [Fact]
    public void TC_MDF_119_a_period_no_column_attests_is_named_and_fails_safe_to_q()
    {
        var body = CaptureBodies[RealSourceCatalog.KapPublicReportMgros];
        var stripped = WithoutDeclaredRanges(body);
        Assert.DoesNotContain("01.01.2026 - 30.06.2026", stripped, StringComparison.Ordinal);

        // The dangerous shape: the declaration says annual, nothing attests a duration.
        var annual = KapPublicReportDecoder.Decode(
            Mutate(stripped, "   Periyot:2", "   Periyot:4"),
            "MGROS");

        Assert.Equal("Yıllık", annual.Header.PeriodType); // surfaced verbatim, never rewritten

        // The danger this closes: on the pre-ticket behaviour the declaration alone typed the
        // half-year balance sheet FY — the shape that puts a 30.06 debt/cash into a DCF baseline.
        Assert.DoesNotContain(annual.Payload.Statements, s => s.PeriodType == "FY");
        Assert.All(annual.Payload.Statements, s => Assert.Equal("Q", s.PeriodType));

        var finding = Assert.Single(
            annual.Findings,
            f => f.ReasonCode == KapPublicReportReasons.PeriodSemanticsUndetermined);
        Assert.Equal(1, finding.Count);
        Assert.Contains("no column", finding.Detail, StringComparison.Ordinal);

        // The half-year balance sheet is still stored — honestly as an interim, its own date
        // being the only period evidence it carries.
        Assert.Contains(
            annual.Payload.Statements,
            s => s.StatementType == "BS"
                && s.PeriodEndDate == new DateOnly(2026, 6, 30)
                && s.PeriodType == "Q");
        Assert.Contains(
            annual.Payload.Statements,
            s => s.StatementType == "BS"
                && s.PeriodEndDate == new DateOnly(2025, 12, 31)
                && s.PeriodType == "Q");

        // No flow fact is invented from columns that cannot be attributed to a period.
        Assert.DoesNotContain(annual.Payload.Statements, s => s.StatementType is "IS" or "CF");
        Assert.True(annual.Count(KapPublicReportReasons.ColumnUnattributed) > 0);

        // The same body under its own `6 Aylık` declaration is named too: an interim's Q typing
        // must rest on evidence as well, not on the absence of a danger.
        var interim = KapPublicReportDecoder.Decode(stripped, "MGROS");
        Assert.Equal(1, interim.Count(KapPublicReportReasons.PeriodSemanticsUndetermined));
        Assert.All(interim.Payload.Statements, s => Assert.Equal("Q", s.PeriodType));
    }

    // =================================================================================
    // Harness
    // =================================================================================

    /// <summary>
    /// A store bound to the migrated test container with the given instrument present — the
    /// decode→store→metric path is the production one, nothing is re-implemented for the test.
    /// </summary>
    private async Task<(FinancialStatementStore Store, long InstrumentId)> StoreAsync(string symbol = "ASELS")
    {
        var db = _postgres.CreateContext();
        if (!await db.Instruments.AnyAsync(i => i.Symbol == symbol))
        {
            db.Instruments.Add(new Instrument
            {
                Symbol = symbol,
                Name = symbol,
                Status = "active",
                SourceRef = "test:kap-public-report",
                RecordedAt = new DateTimeOffset(2026, 8, 4, 0, 0, 0, TimeSpan.Zero),
            });
            await db.SaveChangesAsync();
        }

        await db.Database.ExecuteSqlRawAsync("DELETE FROM fin_line_items");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM financial_statements");

        var instrumentId = await db.Instruments.Where(i => i.Symbol == symbol).Select(i => i.Id).SingleAsync();
        return (new FinancialStatementStore(db), instrumentId);
    }

    /// <summary>
    /// Strips every declared date <b>range</b> from the body's column headers, leaving the
    /// single-date instants (the balance sheet's) intact — so the report declares a period that
    /// none of its duration columns any longer attests. Derived from the committed capture,
    /// never an invented body (TC-MDF-119 (iv)).
    /// </summary>
    private static string WithoutDeclaredRanges(string body) =>
        System.Text.RegularExpressions.Regex.Replace(
            body,
            @"\d{2}\.\d{2}\.\d{4} - \d{2}\.\d{2}\.\d{4}",
            "—");

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
