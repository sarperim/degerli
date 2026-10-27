using System.Net;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.Coverage;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Kap;
using Degerli.Ingestion.KapPublic;
using Degerli.Ingestion.Scheduling;
using Degerli.Ingestion.Statements;
using Degerli.Ingestion.Validation;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TC-MDF-109 / TC-MDF-110 (as amended by the user's decision B, 2026-10-26) — the statements lane's
/// <b>public-site mode</b> (test plan §23; <c>01</c> v1.5.56 §10.9.19, TKT-foundation-082;
/// FR-MDF-002/008/010/011/012).
/// <para>
/// <b>Doubles only, zero live requests.</b> Every payload this class feeds the lane is a committed
/// capture served by WireMock (<see cref="RealSourceCatalog.KapPublicByCriteria"/>,
/// <see cref="RealSourceCatalog.KapPublicReport"/>). Runs go through the production
/// <c>statements</c> job runner, the production composition (<c>AddMarketDataIngestion</c>) and the
/// lane's production paced, single-flight client — retargeted at WireMock with pacing bound to
/// <c>0</c> (the 079 harness convention: the pace is not the subject here, and CI never sleeps). No
/// test in this file reaches kap.org.tr; this ticket issued <b>zero</b> live requests.
/// </para>
/// <para>
/// <b>The one fixture constraint, stated honestly.</b> The only committed report <b>body</b> is the
/// 6-month interim <c>1643141</c> (<c>Periyot: 2</c>). The annual <c>1561039</c> is listed by the
/// committed byCriteria capture but has <b>no</b> committed body, so nothing here asserts an annual
/// column set and no test manufactures one. What the tests assert about <b>selection</b> is the
/// request journal: which public index the lane asked for, and that it never asked for the
/// <c>ODA</c>-categorised rows that share the capture's <c>FR</c> class.
/// </para>
/// </summary>
public sealed class KapPublicStatementsIngestTests : IClassFixture<PostgresFixture>, IClassFixture<WireMockFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;
    private static DateTimeOffset RecordedAt => new(T.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private const string ByCriteriaPath = "/tr/api/disclosure/members/byCriteria";
    private const string ReportRoute = "/tr/api/notification/export/excel/{0}";

    /// <summary>The ASELS public-site company oid (the public site's own space — never VYK's 866).</summary>
    private const string AselsPublicOid = "4028e4a1413b7ef401413bc2251e0047";

    /// <summary>
    /// The second company's public-site oid, deliberately <b>different</b> from ASELS's: the list
    /// request's own body then says which company a pass walked, which is what makes the pass-order
    /// case (review round 2, D14) an observable assertion instead of an inference.
    /// </summary>
    private const string ThyaoPublicOid = "4028e4a1413b7ef401413bc2251e0048";

    /// <summary>The interim report with a committed body (6 months ended 30.06.2026).</summary>
    private const long InterimIndex = 1643141L;

    /// <summary>The two further <c>FR</c>-categorised reports the capture lists (no committed bodies).</summary>
    private const long Q1Index = 1598316L;
    private const long AnnualIndex = 1561039L;

    /// <summary>An <c>ODA</c>-categorised row sharing the capture's <c>FR</c> class (never a report).</summary>
    private const long OdaRowIndex = 1643142L;

    /// <summary>The interim report's own two periods (current + comparative), both decoded as <c>Q</c>.</summary>
    private static readonly DateOnly InterimPeriodEnd = new(2026, 6, 30);
    private static readonly DateOnly InterimComparativeEnd = new(2025, 6, 30);

    private readonly PostgresFixture _postgres;
    private readonly WireMockFixture _wireMock;

    public KapPublicStatementsIngestTests(PostgresFixture postgres, WireMockFixture wireMock)
    {
        _postgres = postgres;
        _wireMock = wireMock;
    }

    // =============================================================================================
    // TC-MDF-109 (i) — the source is chosen by configuration, never by a rewrite
    // =============================================================================================

    /// <summary>
    /// TC-MDF-109 (i) — the public-site lane is <b>selected by configuration</b>, and the VYK lane is
    /// untouched by its existence: with <c>Source=kap-public</c> a run lists with the company's
    /// <b>stored</b> oid and fetches reports from the public export route while issuing <b>zero</b>
    /// VYK requests; with <c>Source=vyk</c> the run issues no public-site request at all; and the
    /// shipped configuration file still selects <c>vyk</c> (switching back is a config change).
    /// </summary>
    [Fact]
    public async Task TC_MDF_109_i_source_selection_routes_the_lane_and_leaves_the_other_source_alone()
    {
        await ResetAsync();
        await BindAsync("ASELS");
        StubDoubles();

        // --- (a) the public site, selected by configuration ---------------------------------
        await using (var host = BuildIngestion())
        {
            var result = await RunAsync(host);

            Assert.NotEqual(IngestResult.Failed, result.Status);
            Assert.True(result.Written > 0, $"the public-site lane must write statement rows ({result.Status})");

            var listRequest = Assert.Single(
                _wireMock.Server.LogEntries,
                entry => entry.RequestMessage?.Path == ByCriteriaPath);
            // The stored oid (TKT-foundation-080) — not a symbol's spelling, not the VYK companyId.
            Assert.Contains(
                $"\"mkkMemberOidList\":[\"{AselsPublicOid}\"]",
                listRequest.RequestMessage!.Body,
                StringComparison.Ordinal);
            Assert.Contains("\"disclosureClass\":\"FR\"", listRequest.RequestMessage!.Body, StringComparison.Ordinal);

            // Selection proven from the journal: the financial report was fetched, the ODA row that
            // shares its class never was.
            Assert.Equal(1, _wireMock.RequestsFor(string.Format(ReportRoute, InterimIndex)));
            Assert.Equal(0, _wireMock.RequestsFor(string.Format(ReportRoute, OdaRowIndex)));

            // No VYK request at all: the two sources are separate clients with separate routes.
            Assert.Equal(0, _wireMock.RequestsFor("/members"));
            Assert.Equal(0, _wireMock.RequestsFor("/disclosures"));
            Assert.Equal(0, _wireMock.Server.LogEntries.Count(
                entry => entry.RequestMessage?.Path?.StartsWith("/disclosureDetail", StringComparison.Ordinal) == true));
        }

        // The facts carry the lane's own provenance — never the VYK lane's `kap`.
        await using (var db = _postgres.CreateContext())
        {
            var asels = await db.Instruments.SingleAsync(instrument => instrument.Symbol == "ASELS");
            var stored = await db.FinancialStatements.Where(statement => statement.InstrumentId == asels.Id).ToListAsync();
            Assert.NotEmpty(stored);
            Assert.All(stored, statement => Assert.Equal(KapPublicSourceOptions.SourceName, statement.SourceRef));
        }

        // --- (b) the VYK lane still runs, and touches no public-site route -------------------
        await using (var db = _postgres.CreateContext())
        {
            await db.FinancialStatements.ExecuteDeleteAsync();
            await db.CoverageMetadata.ExecuteDeleteAsync();
        }

        ResetDoubles();
        StubVykCanonical();

        await using (var vykHost = BuildIngestion(new Dictionary<string, string?>
        {
            ["Ingestion:Statements:Source"] = StatementsSourceOptions.VykSource,
            ["Ingestion:Statements:Path"] = "/statements",
        }))
        {
            await RunAsync(vykHost);
        }

        Assert.Equal(0, _wireMock.Server.LogEntries.Count(entry => entry.RequestMessage?.Path == ByCriteriaPath));
        Assert.Equal(0, _wireMock.Server.LogEntries.Count(
            entry => entry.RequestMessage?.Path?.StartsWith("/tr/api/notification/export", StringComparison.Ordinal) == true));

        // The shipped configuration file still selects the VYK gateway; so does the code default.
        var shipped = FindRepositoryFile(Path.Combine("src", "Api", "appsettings.json"));
        using (var document = JsonDocument.Parse(File.ReadAllText(shipped)))
        {
            var statements = document.RootElement.GetProperty("Ingestion").GetProperty("Statements");
            Assert.Equal(StatementsSourceOptions.VykSource, statements.GetProperty("Source").GetString());
            Assert.True(statements.TryGetProperty("KapPublicLookbackMonths", out _));
            Assert.True(statements.TryGetProperty("KapPublicWindowDays", out _));
        }

        Assert.Equal(StatementsSourceOptions.VykSource, new StatementsSourceOptions().Source);
        Assert.False(new StatementsSourceOptions().KapPublicSelected);
        Assert.True(new StatementsSourceOptions { Source = "KAP-PUBLIC" }.KapPublicSelected);
    }

    /// <summary>
    /// TC-MDF-109 (i) — an <b>unrecognised</b> source value fails the run <b>by name</b> and fetches
    /// nothing: a typo must never quietly run the other source and stamp the wrong provenance.
    /// </summary>
    [Fact]
    public async Task TC_MDF_109_i_an_unrecognised_source_value_fails_by_name_and_fetches_nothing()
    {
        await ResetAsync();
        await BindAsync("ASELS");
        StubDoubles();

        await using var host = BuildIngestion(new Dictionary<string, string?>
        {
            ["Ingestion:Statements:Source"] = "kap-public-typo",
        });

        var result = await RunAsync(host);

        Assert.Equal(IngestResult.Failed, result.Status);
        Assert.Equal(0, result.Written);
        Assert.NotNull(result.FailureReason);
        Assert.Contains(StatementsJob.InvalidSourceSelection, result.FailureReason!, StringComparison.Ordinal);
        Assert.Contains("kap-public-typo", result.FailureReason!, StringComparison.Ordinal);

        // Nothing was fetched from either source.
        Assert.Empty(_wireMock.Server.LogEntries);

        await using var db = _postgres.CreateContext();
        Assert.Equal(0, await db.FinancialStatements.CountAsync());
    }

    // =============================================================================================
    // TC-MDF-109 (ii) — per-company isolation and the honest statuses
    // =============================================================================================

    /// <summary>
    /// TC-MDF-109 (ii) — a bound symbol with <b>no stored mapping</b> is named and counted and never
    /// fetched, while the mapped company's facts land: one company's inability to be walked never
    /// costs another its run, and an oid is never guessed.
    /// </summary>
    [Fact]
    public async Task TC_MDF_109_ii_an_unmapped_company_is_named_counted_and_never_fetched()
    {
        await ResetAsync();
        await BindAsync("ASELS", "THYAO");
        StubDoubles();

        await using var host = BuildIngestion();
        var result = await RunAsync(host);

        Assert.Equal(IngestResult.Partial, result.Status);
        Assert.True(result.Written > 0);
        Assert.Equal(
            KapPublicReasonCodes.CompanyIdentityUnresolved,
            Assert.Single(result.SymbolStats, stat => stat.Symbol == "THYAO").Reason);

        await using var db = _postgres.CreateContext();
        var row = Assert.Single(
            await db.QuarantinedFacts
                .Where(quarantine => quarantine.ReasonCode == KapPublicReasonCodes.CompanyIdentityUnresolved)
                .ToListAsync());
        using (var evidence = JsonDocument.Parse(row.PayloadJson!))
        {
            Assert.Equal("THYAO", evidence.RootElement.GetProperty("symbol").GetString());
            Assert.Equal(
                KapPublicReasonCodes.CompanyIdentityUnresolved,
                evidence.RootElement.GetProperty("reasonCode").GetString());
        }

        // Only the mapped company's oid was ever sent to the source.
        var listBody = Assert.Single(
                _wireMock.Server.LogEntries,
                entry => entry.RequestMessage?.Path == ByCriteriaPath)
            .RequestMessage!.Body!;
        Assert.Contains(AselsPublicOid, listBody, StringComparison.Ordinal);
        Assert.DoesNotContain("THYAO", listBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// TC-MDF-109 (ii) — one <b>report</b> the source cannot deliver is a named, counted skip carrying
    /// its evidence, and the company's other reports are still walked. A per-report failure isolates;
    /// it does not fail the company.
    /// </summary>
    [Fact]
    public async Task TC_MDF_109_ii_one_unfetchable_report_is_isolated_with_evidence_and_the_rest_are_written()
    {
        await ResetAsync();
        await BindAsync("ASELS");
        StubDoubles(failingReports: [Q1Index, AnnualIndex]);

        await using var host = BuildIngestion();
        var result = await RunAsync(host);

        Assert.Equal(IngestResult.Partial, result.Status);
        Assert.True(result.Written > 0, "the readable report's facts must still be written");
        Assert.Equal(2, result.Quarantined);

        await using var db = _postgres.CreateContext();
        var unfetchable = await db.QuarantinedFacts
            .Where(quarantine => quarantine.ReasonCode == VykDisclosureWalker.DisclosureDetailUnfetchable)
            .ToListAsync();
        Assert.Equal(2, unfetchable.Count);
        // The evidence is the source's own answer, verbatim — never a fabricated body.
        Assert.All(unfetchable, row =>
        {
            Assert.Equal(KapPublicSourceOptions.SourceName, row.SourceRef);
            Assert.Contains("report unavailable", row.PayloadJson!, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// TC-MDF-109 (ii) — an <b>all-isolated</b> zero-write run is <c>partial</c>, never <c>failed</c>
    /// (nothing was fabricated, and nothing failed at the source level), while a genuine
    /// <b>source-down</b> list <i>is</i> <c>failed</c>: with no index there is no walk, and an honest
    /// zero would be a lie.
    /// </summary>
    [Fact]
    public async Task TC_MDF_109_ii_all_isolated_is_partial_while_a_genuine_source_down_is_failed()
    {
        await ResetAsync();
        await BindAsync("ASELS");

        // (a) every report the walk reaches degrades: zero writes, quarantined with evidence, partial.
        StubDoubles(failingReports: [InterimIndex, Q1Index, AnnualIndex], serveInterimBody: false);
        await using (var host = BuildIngestion())
        {
            var result = await RunAsync(host);
            Assert.Equal(IngestResult.Partial, result.Status);
            Assert.Equal(0, result.Written);
            Assert.Equal(3, result.Quarantined);
        }

        // (b) the list itself is down: the company is failed, nothing is fabricated.
        await using (var db = _postgres.CreateContext())
        {
            await db.QuarantinedFacts.ExecuteDeleteAsync();
            await db.CoverageMetadata.ExecuteDeleteAsync();
        }

        ResetDoubles();
        StubList(statusCode: 500);

        await using (var host = BuildIngestion())
        {
            var result = await RunAsync(host);
            Assert.Equal(IngestResult.Failed, result.Status);
            Assert.Equal(0, result.Written);
            Assert.Equal(0, result.Quarantined);
            Assert.True(Assert.Single(result.SymbolStats).Failed);
        }

        await using (var db = _postgres.CreateContext())
        {
            // A source-down is not a parse event: nothing is quarantined and nothing is written.
            Assert.Equal(0, await db.QuarantinedFacts.CountAsync());
            Assert.Equal(0, await db.FinancialStatements.CountAsync());
        }
    }

    // =============================================================================================
    // TC-MDF-109 (iii) — bounded work, truncation and resume at the last completed unit
    // =============================================================================================

    /// <summary>
    /// TC-MDF-109 (iii) — the walk is <b>bounded</b> by the pass's detail ceiling from configuration: it
    /// stops at the last <b>completed</b> report, records the named truncation, persists where it got to,
    /// and the next pass resumes exactly there — it re-fetches nothing it already completed, writes no
    /// duplicate, and a completed pass re-runs as a no-op.
    /// </summary>
    [Fact]
    public async Task TC_MDF_109_iii_the_walk_truncates_at_its_bound_and_resumes_exactly_where_it_stopped()
    {
        await ResetAsync();
        await BindAsync("ASELS");
        StubDoubles();

        IngestResult first;
        await using (var host = BuildIngestion(new Dictionary<string, string?>
        {
            ["Ingestion:KapPublic:MaxDetailFetches"] = "1",
        }))
        {
            first = await RunAsync(host);
        }

        // Within the (single) window the newest report came first: a bounded pass spends its budget
        // on what a consumer needs now. Across windows the walk is oldest-first (the watermark has to
        // advance monotonically), so this is a window-scoped statement, not a global one.
        Assert.True(first.Written > 0);
        Assert.Equal(KapPublicReasonCodes.DetailCap, Assert.Single(first.SymbolStats).Reason);
        Assert.Equal(1, _wireMock.RequestsFor(string.Format(ReportRoute, InterimIndex)));

        await using (var db = _postgres.CreateContext())
        {
            var cursorRow = Assert.Single(
                await db.CoverageMetadata
                    .Where(row => row.DataType == KapPublicStatementCursorStore.DataType)
                    .ToListAsync());
            var cursor = KapPublicStatementCursor.TryParse(cursorRow.Notes)!;
            Assert.Contains(InterimIndex, cursor.Seen);
            Assert.DoesNotContain(Q1Index, cursor.Seen);
        }

        // The resumed pass asks for exactly the reports it has not completed.
        _wireMock.Server.ResetLogEntries();
        IngestResult second;
        await using (var host = BuildIngestion())
        {
            second = await RunAsync(host);
        }

        Assert.Equal(0, _wireMock.RequestsFor(string.Format(ReportRoute, InterimIndex)));
        Assert.Equal(1, _wireMock.RequestsFor(string.Format(ReportRoute, Q1Index)));

        // The two remaining indices have no committed body, so they are honest skips — the fixture
        // constraint, not a fabricated success. What the resume proves is where it resumed.
        Assert.Equal(2, second.Quarantined);

        // A completed pass re-runs as a no-op: every index in the window is already completed, so no
        // report is fetched a second time.
        _wireMock.Server.ResetLogEntries();
        await using (var host = BuildIngestion())
        {
            var third = await RunAsync(host);
            Assert.Equal(0, third.Written);
        }

        Assert.Equal(0, _wireMock.RequestsFor(string.Format(ReportRoute, InterimIndex)));
        Assert.Equal(0, _wireMock.RequestsFor(string.Format(ReportRoute, Q1Index)));
        Assert.Equal(0, _wireMock.RequestsFor(string.Format(ReportRoute, AnnualIndex)));

        await using (var db = _postgres.CreateContext())
        {
            // No duplicate: each natural key still holds exactly one as-reported row from this lane.
            var rows = await db.FinancialStatements
                .Where(statement => statement.SourceRef == KapPublicSourceOptions.SourceName && statement.Version == 0)
                .ToListAsync();
            var keys = rows.Select(row => (row.PeriodType, row.PeriodEndDate, row.StatementType)).ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count());
        }
    }

    /// <summary>
    /// TC-MDF-109 (iii) — the <b>page</b> ceiling is wired from configuration and bounds the pass:
    /// with one page allowed and three windows to cover, the first window is walked and the rest are
    /// honestly truncated (named, with the position persisted), and the next pass resumes at the
    /// watermark the truncated pass left.
    /// </summary>
    [Fact]
    public async Task TC_MDF_109_iii_the_page_ceiling_bounds_the_pass_and_the_next_pass_resumes_after_it()
    {
        await ResetAsync();
        await BindAsync("ASELS");
        StubDoubles();

        var truncatedSettings = new Dictionary<string, string?>
        {
            ["Ingestion:Statements:KapPublicLookbackMonths"] = "24",
            ["Ingestion:Statements:KapPublicWindowDays"] = "365",
            ["Ingestion:KapPublic:MaxListPages"] = "1",
        };

        await using (var host = BuildIngestion(truncatedSettings))
        {
            var result = await RunAsync(host);
            Assert.Equal(KapPublicReasonCodes.PageCap, Assert.Single(result.SymbolStats).Reason);
            Assert.Equal(IngestResult.Partial, result.Status);
        }

        var firstWindow = T.AddMonths(-24);
        var lastListedInTruncatedPass = Assert.Single(_wireMock.Server.LogEntries, entry =>
            entry.RequestMessage?.Path == ByCriteriaPath);
        Assert.Contains(
            $"\"fromDate\":\"{firstWindow:yyyy-MM-dd}\"",
            lastListedInTruncatedPass.RequestMessage!.Body!,
            StringComparison.Ordinal);

        DateOnly watermark;
        await using (var db = _postgres.CreateContext())
        {
            var cursorRow = Assert.Single(
                await db.CoverageMetadata
                    .Where(row => row.DataType == KapPublicStatementCursorStore.DataType)
                    .ToListAsync());
            watermark = KapPublicStatementCursor.TryParse(cursorRow.Notes)!.Watermark;
            Assert.True(watermark > firstWindow, "the completed window advanced the watermark");
        }

        // The next pass lists from the watermark, not from the bootstrap start.
        _wireMock.Server.ResetLogEntries();
        await using (var host = BuildIngestion())
        {
            await RunAsync(host);
        }

        var resumed = Assert.Single(
            _wireMock.Server.LogEntries,
            entry => entry.RequestMessage?.Path == ByCriteriaPath);
        Assert.Contains(
            $"\"fromDate\":\"{watermark:yyyy-MM-dd}\"",
            resumed.RequestMessage!.Body!,
            StringComparison.Ordinal);

        // The resumed pass updates the achieved depth but must NOT rewrite the configured request:
        // the lookback is still the range nobody has achieved yet (review round 1, D12).
        await using (var db = _postgres.CreateContext())
        {
            var depth = Assert.Single(
                await db.CoverageMetadata
                    .Where(row => row.DataType == KapPublicStatementDepthStore.DataType)
                    .ToListAsync());
            using var notes = JsonDocument.Parse(depth.Notes!);
            Assert.Equal(
                firstWindow.ToString("yyyy-MM-dd"),
                notes.RootElement.GetProperty("requestedFrom").GetString());
            Assert.False(notes.RootElement.GetProperty("bootstrapped").GetBoolean());
        }
    }

    /// <summary>
    /// TC-MDF-109 (iii) — a list outage in a <b>later</b> window does not erase what the earlier
    /// windows already wrote (review round 1, D1). The company is still failed (a genuine
    /// source-down), but the run reports the rows it actually ingested and is <c>partial</c>, never
    /// <c>failed</c>-with-zero and never falsely complete; the persisted position is the last
    /// <b>completed</b> window, and the depth record does not claim the requested range was achieved.
    /// </summary>
    [Fact]
    public async Task TC_MDF_109_iii_a_midwalk_list_outage_keeps_the_counts_of_the_windows_already_written()
    {
        await ResetAsync();
        await BindAsync("ASELS");

        var bootstrapStart = T.AddMonths(-24);
        var firstWindowEnd = bootstrapStart.AddDays(364);
        var secondWindowStart = firstWindowEnd.AddDays(1);
        StubWindowedList(bootstrapStart, secondWindowStart);

        IngestResult result;
        await using (var host = BuildIngestion(new Dictionary<string, string?>
        {
            ["Ingestion:Statements:KapPublicWindowDays"] = "365",
        }))
        {
            result = await RunAsync(host);
        }

        // Rows exist in the store, so the ledger must say so: pre-fix this run reported `written 0`
        // beside stored rows and a `failed` status.
        await using (var db = _postgres.CreateContext())
        {
            var stored = await db.FinancialStatements.CountAsync(
                row => row.SourceRef == KapPublicSourceOptions.SourceName);
            Assert.True(stored > 0, "the first window wrote rows before the second window's list failed");
            Assert.Equal(stored, result.Written);
        }

        Assert.Equal(IngestResult.Partial, result.Status);

        var stat = Assert.Single(result.SymbolStats);
        Assert.True(stat.Failed, "the company is still failed by name");
        Assert.Equal(result.Written, stat.Written);
        // The first window's two bodyless reports are the honest per-report skips; they survive too.
        Assert.Equal(2, result.Quarantined);

        await using (var db = _postgres.CreateContext())
        {
            var cursorRow = Assert.Single(
                await db.CoverageMetadata
                    .Where(row => row.DataType == KapPublicStatementCursorStore.DataType)
                    .ToListAsync());
            var cursor = KapPublicStatementCursor.TryParse(cursorRow.Notes)!;
            // The completed window advanced the watermark; the failed one did not.
            Assert.Equal(firstWindowEnd, cursor.Watermark);

            var depth = Assert.Single(
                await db.CoverageMetadata
                    .Where(row => row.DataType == KapPublicStatementDepthStore.DataType)
                    .ToListAsync());
            using var notes = JsonDocument.Parse(depth.Notes!);
            Assert.Equal(bootstrapStart.ToString("yyyy-MM-dd"), notes.RootElement.GetProperty("requestedFrom").GetString());
            Assert.Equal(T.ToString("yyyy-MM-dd"), notes.RootElement.GetProperty("requestedTo").GetString());
            Assert.Equal(firstWindowEnd.ToString("yyyy-MM-dd"), notes.RootElement.GetProperty("achievedTo").GetString());
        }
    }

    /// <summary>
    /// TC-MDF-109 (iii) — the detail ceiling is <b>per pass</b>, not per company (review round 1, D3;
    /// test plan v1.29 item 3). Two mapped companies with a ceiling of <b>one</b> detail: the pass
    /// issues exactly one report request in total, so a per-company allowance (which would issue two)
    /// is excluded; the company that was not started is named under <c>WALK_NOT_STARTED</c> and its
    /// list is never requested.
    /// </summary>
    [Fact]
    public async Task TC_MDF_109_iii_the_detail_ceiling_is_shared_by_the_pass_not_multiplied_per_company()
    {
        await ResetAsync();
        await BindAsync("ASELS", "THYAO");
        await MapPublicOidAsync("THYAO");
        StubDoubles();

        IngestResult result;
        await using (var host = BuildIngestion(new Dictionary<string, string?>
        {
            ["Ingestion:KapPublic:MaxDetailFetches"] = "1",
        }))
        {
            result = await RunAsync(host);
        }

        var reportRequests = _wireMock.RequestsFor(string.Format(ReportRoute, InterimIndex));
        Assert.Equal(1, reportRequests);
        Assert.True(reportRequests <= 1, "the pass's detail requests never exceed its ceiling");

        // Only one company was listed at all: the second was never started.
        Assert.Equal(
            1,
            _wireMock.Server.LogEntries.Count(entry => entry.RequestMessage?.Path == ByCriteriaPath));

        var walked = Assert.Single(result.SymbolStats, stat => stat.Reason == KapPublicReasonCodes.DetailCap);
        Assert.Equal("ASELS", walked.Symbol);
        Assert.False(walked.Failed);

        var notStarted = Assert.Single(result.SymbolStats, stat => stat.Reason == KapPublicReasonCodes.WalkNotStarted);
        Assert.Equal("THYAO", notStarted.Symbol);
        Assert.False(notStarted.Failed);
        Assert.Equal(0, notStarted.Written);

        Assert.True(result.Written > 0);
    }

    /// <summary>
    /// TC-MDF-109 (iii) — the pass order is <b>fair across passes</b> (review round 2, D14): the
    /// company a bounded pass could not start is walked <b>first</b> by the next pass, so the
    /// omission is bounded in passes and never permanent starvation.
    /// <para>
    /// Pass 1's ceiling of one detail is spent by the head of the universe; the tail is named
    /// <c>WALK_NOT_STARTED</c> and has <b>no cursor row at all</b>. Pass 2 (same ceiling) must walk
    /// the never-walked company first — asserted from observable state only: the pass's list request
    /// carries that company's <b>stored oid</b>, its cursor row appears, the head company's cursor is
    /// untouched (watermark and seen set unchanged) and it is the one now named not-started. With the
    /// order reverted to the bare constituent list, pass 2 re-walks the head company and the tail
    /// company's list is never requested — which is exactly the permanent starvation D2 removed.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TC_MDF_109_iii_a_bounded_pass_resumes_the_not_started_company_first()
    {
        await ResetAsync();
        await BindAsync("ASELS", "THYAO");
        await MapPublicOidAsync("THYAO", ThyaoPublicOid);
        StubDoubles();

        var passSettings = new Dictionary<string, string?>
        {
            ["Ingestion:KapPublic:MaxDetailFetches"] = "1",
        };

        IngestResult first;
        await using (var host = BuildIngestion(passSettings))
        {
            first = await RunAsync(host);
        }

        // Pass 1: the ceiling is spent on the head of the universe; the tail never starts and leaves
        // no position behind (absence is the "never walked" signal the pass order reads).
        Assert.Equal(
            "ASELS",
            Assert.Single(first.SymbolStats, stat => stat.Reason == KapPublicReasonCodes.DetailCap).Symbol);
        Assert.Equal(
            "THYAO",
            Assert.Single(first.SymbolStats, stat => stat.Reason == KapPublicReasonCodes.WalkNotStarted).Symbol);
        Assert.Equal(1, ListRequestsFor(AselsPublicOid));
        Assert.Equal(0, ListRequestsFor(ThyaoPublicOid));

        var aselsAfterPassOne = Assert.IsType<KapPublicStatementCursor>(await ReadCursorAsync("ASELS"));
        Assert.Contains(InterimIndex, aselsAfterPassOne.Seen);
        Assert.Null(await ReadCursorAsync("THYAO"));

        // Pass 2: the same ceiling, the same universe — and the pass order has changed on its own.
        IngestResult second;
        await using (var host = BuildIngestion(passSettings))
        {
            second = await RunAsync(host);
        }

        // The pass's list request names the never-walked company (the oid on the wire), and the head
        // company's list was not requested again: one request per company across both passes.
        Assert.Equal(1, ListRequestsFor(ThyaoPublicOid));
        Assert.Equal(1, ListRequestsFor(AselsPublicOid));
        Assert.Equal(
            "THYAO",
            Assert.Single(second.SymbolStats, stat => stat.Reason == KapPublicReasonCodes.DetailCap).Symbol);
        Assert.Equal(
            "ASELS",
            Assert.Single(second.SymbolStats, stat => stat.Reason == KapPublicReasonCodes.WalkNotStarted).Symbol);

        // The previously-not-started company now has a real position, and the head company's is
        // exactly where pass 1 left it — the fair order is a reorder, not a reset.
        var thyao = Assert.IsType<KapPublicStatementCursor>(await ReadCursorAsync("THYAO"));
        Assert.Contains(InterimIndex, thyao.Seen);
        var aselsAfterPassTwo = Assert.IsType<KapPublicStatementCursor>(await ReadCursorAsync("ASELS"));
        Assert.Equal(aselsAfterPassOne.Watermark, aselsAfterPassTwo.Watermark);
        Assert.Equal(aselsAfterPassOne.Seen, aselsAfterPassTwo.Seen);

        // The resumed company's facts are in the store under its own symbol.
        Assert.True(second.Written > 0, $"the resumed company's report was written ({second.Status})");
        await using (var db = _postgres.CreateContext())
        {
            var thyaoInstrumentId = await db.Instruments
                .Where(instrument => instrument.Symbol == "THYAO")
                .Select(instrument => instrument.Id)
                .SingleAsync();
            var thyaoRows = await db.FinancialStatements.CountAsync(
                statement => statement.InstrumentId == thyaoInstrumentId
                    && statement.SourceRef == KapPublicSourceOptions.SourceName);
            Assert.True(thyaoRows > 0, "the resumed company's statement rows were stored under its own symbol");
        }
    }

    /// <summary>
    /// TC-MDF-109 (iii) — the <b>wall-clock</b> bound is the job's own: the shipped 90-minute bound is
    /// strictly below the scheduler's per-step bound, and the <b>walk</b> truncates with
    /// <c>WALK_DEADLINE</c> when the injected clock passes it — no sleeps, no wall-clock dependence.
    /// <para>
    /// This is a walk-level case, not a direct unit drive of <see cref="KapPublicWalkBudget"/>
    /// (review round 1, D4): the production job builds the budget from
    /// <c>Ingestion:KapPublic:MaxWalkDuration</c>, the pacer advances the shared fake clock between two
    /// admitted requests, and the assertions are the walk's own — the named deadline on the company's
    /// stat, the <b>persisted</b> completed position in the cursor (the report it finished, watermark
    /// unmoved) and the <b>truncated depth</b> row that says so.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TC_MDF_109_iii_the_wall_clock_bound_truncates_the_walk_and_persists_its_position()
    {
        var options = new KapPublicSourceOptions();

        // The shipped bound, and the relation to the scheduler that makes it usable.
        Assert.Equal(TimeSpan.FromMinutes(90), options.MaxWalkDuration);
        Assert.True(options.MaxWalkDuration < IngestionSchedulerOptions.DefaultMaxStepDuration);

        await ResetAsync();
        await BindAsync("ASELS");
        StubDoubles();

        // A one-minute walk bound; the pacer's wait advances the fake clock by two minutes, so the
        // deadline expires between the first completed report and the next detail request.
        var clock = new FakeTimeProvider(RecordedAt);
        var advanceOnPace = new KapPublicDelay((_, _) =>
        {
            clock.Advance(TimeSpan.FromMinutes(2));
            return Task.CompletedTask;
        });

        IngestResult result;
        await using (var host = BuildIngestion(
            new Dictionary<string, string?>
            {
                ["Ingestion:KapPublic:RequestDelayMs"] = "1",
                ["Ingestion:KapPublic:MaxWalkDuration"] = "00:01:00",
            },
            clock,
            advanceOnPace))
        {
            result = await RunAsync(host);
        }

        // The walk stopped on the deadline, after exactly one completed report.
        Assert.Equal(KapPublicReasonCodes.WalkDeadline, Assert.Single(result.SymbolStats).Reason);
        Assert.Equal(1, _wireMock.RequestsFor(string.Format(ReportRoute, InterimIndex)));
        Assert.Equal(0, _wireMock.RequestsFor(string.Format(ReportRoute, Q1Index)));
        Assert.Equal(0, _wireMock.RequestsFor(string.Format(ReportRoute, AnnualIndex)));
        Assert.True(result.Written > 0, $"the completed report's facts were written ({result.Status})");

        // The persisted position is the last *completed* report, and the window is not claimed as
        // walked: its watermark still sits at the bootstrap start.
        var bootstrapStart = T.AddMonths(-24);
        await using (var db = _postgres.CreateContext())
        {
            var cursorRow = Assert.Single(
                await db.CoverageMetadata
                    .Where(row => row.DataType == KapPublicStatementCursorStore.DataType)
                    .ToListAsync());
            var cursor = KapPublicStatementCursor.TryParse(cursorRow.Notes)!;
            Assert.Contains(InterimIndex, cursor.Seen);
            Assert.Equal(bootstrapStart, cursor.Watermark);

            // The depth row records the truncation by name: asked for the lookback, achieved no
            // complete window.
            var depth = Assert.Single(
                await db.CoverageMetadata
                    .Where(row => row.DataType == KapPublicStatementDepthStore.DataType)
                    .ToListAsync());
            using var notes = JsonDocument.Parse(depth.Notes!);
            Assert.True(notes.RootElement.GetProperty("truncated").GetBoolean());
            Assert.Equal(
                KapPublicReasonCodes.WalkDeadline,
                notes.RootElement.GetProperty("truncationReason").GetString());
            Assert.Equal(bootstrapStart.ToString("yyyy-MM-dd"), notes.RootElement.GetProperty("requestedFrom").GetString());
            Assert.Equal(T.ToString("yyyy-MM-dd"), notes.RootElement.GetProperty("requestedTo").GetString());
            Assert.False(notes.RootElement.TryGetProperty("achievedFrom", out _));
            Assert.Null(depth.AvailableFrom);
        }
    }

    // =============================================================================================
    // TC-MDF-109 (iv) — its own cursors; the two index spaces never mix
    // =============================================================================================

    /// <summary>
    /// TC-MDF-109 (iv) — the public lane keeps its <b>own</b> cursor: a public index (<c>1643141</c>)
    /// never enters the VYK cursor, a VYK index (head <c>1231017</c>) never enters the public one, and
    /// the data types are distinct strings outside the coverage recorder's owned set, so the two
    /// spaces cannot meet.
    /// </summary>
    [Fact]
    public async Task TC_MDF_109_iv_the_lane_keeps_its_own_cursor_and_the_two_index_spaces_never_mix()
    {
        await ResetAsync();
        await BindAsync("ASELS");
        StubDoubles();

        // The VYK lane's cursor for the same instrument, in its own space.
        const string vykCursor = "1231017";
        await using (var db = _postgres.CreateContext())
        {
            var asels = await db.Instruments.SingleAsync(instrument => instrument.Symbol == "ASELS");
            db.CoverageMetadata.Add(new CoverageMetadata
            {
                Scope = "instrument",
                InstrumentId = asels.Id,
                DataType = KapDisclosureCursorStore.DataType,
                Notes = vykCursor,
            });
            await db.SaveChangesAsync();
        }

        await using (var host = BuildIngestion())
        {
            var result = await RunAsync(host);
            Assert.True(result.Written > 0, result.Status);
        }

        await using (var db = _postgres.CreateContext())
        {
            var rows = await db.CoverageMetadata.ToListAsync();

            var vyk = Assert.Single(rows, row => row.DataType == KapDisclosureCursorStore.DataType);
            Assert.Equal(vykCursor, vyk.Notes);
            Assert.DoesNotContain(InterimIndex.ToString(), vyk.Notes!, StringComparison.Ordinal);

            var publicCursor = Assert.Single(rows, row => row.DataType == KapPublicStatementCursorStore.DataType);
            Assert.DoesNotContain(vykCursor, publicCursor.Notes!, StringComparison.Ordinal);
            Assert.Contains(InterimIndex.ToString(), publicCursor.Notes!, StringComparison.Ordinal);

            // Distinct data types, none of them a coverage-recorder-owned type: the no-silent-gaps
            // reconcile can neither refresh one lane's position with the other's nor overwrite it.
            Assert.NotEqual(KapDisclosureCursorStore.DataType, KapPublicStatementCursorStore.DataType);
            Assert.NotEqual(KapDisclosureCursorStore.DataType, KapPublicStatementDepthStore.DataType);
            Assert.NotEqual(KapPublicStatementCursorStore.DataType, KapPublicStatementDepthStore.DataType);
            Assert.DoesNotContain(KapPublicStatementCursorStore.DataType, CoverageDataTypes.InstrumentScoped);
            Assert.DoesNotContain(KapPublicStatementDepthStore.DataType, CoverageDataTypes.InstrumentScoped);
        }
    }

    // =============================================================================================
    // TC-MDF-109 (v) — the lookback is a request; the depth record is what was achieved
    // =============================================================================================

    /// <summary>
    /// TC-MDF-109 (v) — a bootstrap walk asks for the configured <b>lookback</b> (read off the wire,
    /// never assumed) and records the depth it <b>achieved</b> beside what it requested, so an unwalked
    /// range never reads as "no statements exist" (FR-MDF-010).
    /// </summary>
    [Fact]
    public async Task TC_MDF_109_v_the_lookback_is_requested_and_the_achieved_depth_is_recorded()
    {
        await ResetAsync();
        await BindAsync("ASELS");
        StubDoubles();

        await using (var host = BuildIngestion(new Dictionary<string, string?>
        {
            ["Ingestion:Statements:KapPublicLookbackMonths"] = "12",
            ["Ingestion:Statements:KapPublicWindowDays"] = "3650",
        }))
        {
            var result = await RunAsync(host);
            Assert.True(result.Written > 0, result.Status);
        }

        var expectedFrom = T.AddMonths(-12);
        var body = Assert.Single(
                _wireMock.Server.LogEntries,
                entry => entry.RequestMessage?.Path == ByCriteriaPath)
            .RequestMessage!.Body!;
        Assert.Contains($"\"fromDate\":\"{expectedFrom:yyyy-MM-dd}\"", body, StringComparison.Ordinal);
        Assert.Contains($"\"toDate\":\"{T:yyyy-MM-dd}\"", body, StringComparison.Ordinal);

        await using (var db = _postgres.CreateContext())
        {
            var depth = Assert.Single(
                await db.CoverageMetadata
                    .Where(row => row.DataType == KapPublicStatementDepthStore.DataType)
                    .ToListAsync());
            // The achieved depth is a real range…
            Assert.Equal(expectedFrom, depth.AvailableFrom);
            Assert.Equal(T, depth.AvailableTo);
            // …and the record says what was asked for, so "not walked yet" can never be read as
            // "nothing there".
            using var notes = JsonDocument.Parse(depth.Notes!);
            Assert.Equal(expectedFrom.ToString("yyyy-MM-dd"), notes.RootElement.GetProperty("requestedFrom").GetString());
            Assert.Equal(T.ToString("yyyy-MM-dd"), notes.RootElement.GetProperty("requestedTo").GetString());
            Assert.Equal(expectedFrom.ToString("yyyy-MM-dd"), notes.RootElement.GetProperty("achievedFrom").GetString());
            Assert.True(notes.RootElement.GetProperty("bootstrapped").GetBoolean());
            Assert.False(notes.RootElement.GetProperty("truncated").GetBoolean());
        }
    }

    // =============================================================================================
    // TC-MDF-110 — two-source coexistence, provenance, and the named overlap retention
    // =============================================================================================

    /// <summary>
    /// TC-MDF-110 — the two sources <b>coexist</b>: a VYK period already stored stays exactly as it is
    /// while the public lane's periods are added, every row answers its own evidence class, and the
    /// serving read path returns both periods. Nothing is overwritten to make the two agree.
    /// </summary>
    [Fact]
    public async Task TC_MDF_110_a_both_sources_coexist_and_each_row_carries_its_own_provenance()
    {
        await ResetAsync();
        await BindAsync("ASELS");
        StubDoubles();

        long instrumentId;
        await using (var db = _postgres.CreateContext())
        {
            instrumentId = (await db.Instruments.SingleAsync(instrument => instrument.Symbol == "ASELS")).Id;
            await SeedVykStatementAsync(db, instrumentId, new DateOnly(2023, 12, 31), "FY");
        }

        await using (var host = BuildIngestion())
        {
            var result = await RunAsync(host);
            Assert.True(result.Written > 0, result.Status);
        }

        await using (var db = _postgres.CreateContext())
        {
            var rows = await db.FinancialStatements
                .Where(statement => statement.InstrumentId == instrumentId)
                .ToListAsync();

            // The VYK period is untouched — same provenance, same version, same as-reported stamp.
            var vyk = Assert.Single(rows, row => row.PeriodEndDate == new DateOnly(2023, 12, 31));
            Assert.Equal("kap", vyk.SourceRef);
            Assert.Equal(0, vyk.Version);
            Assert.Single(await db.FinLineItems.Where(line => line.StatementId == vyk.Id).ToListAsync());

            // The public lane's periods sit beside it, under their own evidence class.
            var @public = rows.Where(row => row.SourceRef == KapPublicSourceOptions.SourceName).ToList();
            Assert.NotEmpty(@public);
            Assert.All(@public, row => Assert.NotEqual(new DateOnly(2023, 12, 31), row.PeriodEndDate));
            Assert.Contains(@public, row => row.PeriodEndDate == InterimPeriodEnd);
        }

        // The serving read path answers both periods over the mixed world: the coexistence is in the
        // store, not in a consumer's imagination.
        await using (var factory = new DegerliAppFactory(_postgres.ConnectionString))
        {
            var client = factory.CreateClient();
            var response = await client.GetAsync(new Uri("/api/v1/stocks/ASELS/financials", UriKind.Relative));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var periods = document.RootElement.GetProperty("periods").EnumerateArray()
                .Select(period => period.GetProperty("periodEndDate").GetString())
                .ToList();
            Assert.Contains("2023-12-31", periods);
            Assert.Contains("2026-06-30", periods);
        }
    }

    /// <summary>
    /// TC-MDF-110 (as amended) — for a period <b>both</b> sources cover, the public-site fact is
    /// <b>not written</b> and the overlap is a <b>named, counted</b> <c>SOURCE_OVERLAP_RETAINED</c>
    /// finding: the incumbent rows are retained as-is and stay as-of queryable, the same report's
    /// <i>other</i> period is still written (the rule is per key, not per report), nothing is swallowed
    /// as an idempotent <c>unchanged</c>, and no false <c>restated</c> flag is raised.
    /// </summary>
    [Fact]
    public async Task TC_MDF_110_b_a_covered_period_is_withheld_and_the_overlap_is_named_and_counted()
    {
        await ResetAsync();
        await BindAsync("ASELS");
        StubDoubles();

        // What the committed report actually contains — read through 081's decoder (the accepted
        // seam, TC-MDF-106..108), never assumed here. The interim carries its own period plus the
        // statements' comparatives, so the covered period is a subset of the report's facts, and the
        // expected split is derived from the source rather than from this lane's output.
        var decoded = KapPublicReportDecoder.Decode(
            RealSourceCatalog.Get(RealSourceCatalog.KapPublicReport).Body,
            "ASELS");
        var coveredFacts = decoded.Payload.Statements
            .Where(fact => fact.PeriodType == "Q" && fact.PeriodEndDate == InterimPeriodEnd)
            .ToList();
        var uncoveredFacts = decoded.Payload.Statements.Except(coveredFacts).ToList();
        Assert.NotEmpty(coveredFacts);
        Assert.NotEmpty(uncoveredFacts);

        // The incumbent already covers the interim's current period, statement type by statement type.
        long instrumentId;
        await using (var db = _postgres.CreateContext())
        {
            instrumentId = (await db.Instruments.SingleAsync(instrument => instrument.Symbol == "ASELS")).Id;
            foreach (var fact in coveredFacts)
            {
                await SeedVykStatementAsync(
                    db,
                    instrumentId,
                    InterimPeriodEnd,
                    "Q",
                    fact.StatementType,
                    publishedAt: new DateOnly(2026, 8, 4));
            }
        }

        await using (var host = BuildIngestion())
        {
            var result = await RunAsync(host);

            // Counted: one quarantine row per withheld fact, all under the one name — and the
            // report's uncovered facts are written, because the rule is per key, not per report.
            // Counted, part 1: the report's uncovered facts are written — the rule is per key, not
            // per report, so withholding the covered period costs the report nothing else.
            Assert.Equal(uncoveredFacts.Count, result.Written);
            // The run is honestly `partial`: it withheld facts by decision and skipped the two report
            // indices this fixture carries no body for (TC-MDF-109 (ii) pins that skip path).
            Assert.Equal(IngestResult.Partial, result.Status);
        }

        await using (var db = _postgres.CreateContext())
        {
            // Counted, part 2 + named: exactly one retained finding per withheld fact, each naming
            // both provenances and the period.
            var retained = await db.QuarantinedFacts
                .Where(quarantine => quarantine.ReasonCode == StatementsJob.SourceOverlapRetained)
                .ToListAsync();
            Assert.Equal(coveredFacts.Count, retained.Count);
            Assert.Equal(
                coveredFacts.Select(fact => fact.StatementType).Order().ToList(),
                retained
                    .Select(row => JsonDocument.Parse(row.PayloadJson!).RootElement.GetProperty("statementType").GetString()!)
                    .Order()
                    .ToList());
            Assert.All(retained, row =>
            {
                Assert.Equal(KapPublicSourceOptions.SourceName, row.SourceRef);
                using var evidence = JsonDocument.Parse(row.PayloadJson!);
                Assert.Equal("ASELS", evidence.RootElement.GetProperty("symbol").GetString());
                Assert.Equal(InterimIndex, evidence.RootElement.GetProperty("kapPublicDisclosureIndex").GetInt64());
                Assert.Equal("2026-06-30", evidence.RootElement.GetProperty("periodEndDate").GetString());
                Assert.Equal("kap", evidence.RootElement.GetProperty("incumbentSource").GetString());
                Assert.Equal(KapPublicSourceOptions.SourceName, evidence.RootElement.GetProperty("withheldSource").GetString());
            });

            // Not written: the covered key still holds exactly the incumbent rows, untouched.
            var covered = await db.FinancialStatements
                .Where(statement => statement.InstrumentId == instrumentId
                    && statement.PeriodEndDate == InterimPeriodEnd)
                .ToListAsync();
            Assert.Equal(coveredFacts.Count, covered.Count);
            Assert.All(covered, row =>
            {
                Assert.Equal("kap", row.SourceRef);
                Assert.Equal(0, row.Version);
                Assert.Null(row.RestatementDate);
            });

            // Every uncovered fact of the same report is written under this lane's provenance — its
            // comparative periods included, so withholding the covered period costs nothing else.
            var publicRows = await db.FinancialStatements
                .Where(statement => statement.InstrumentId == instrumentId
                    && statement.SourceRef == KapPublicSourceOptions.SourceName)
                .ToListAsync();
            Assert.All(publicRows, row => Assert.NotEqual(InterimPeriodEnd, row.PeriodEndDate));
            foreach (var fact in uncoveredFacts)
            {
                Assert.Contains(publicRows, row =>
                    row.PeriodType == fact.PeriodType
                    && row.PeriodEndDate == fact.PeriodEndDate
                    && row.StatementType == fact.StatementType
                    && row.Version == 0);
            }

            // As-of queryable: the incumbent rows and their lines are still there.
            var incumbentIds = covered.Select(row => row.Id).ToList();
            Assert.Equal(coveredFacts.Count, await db.FinLineItems.CountAsync(line => incumbentIds.Contains(line.StatementId)));
        }
    }

    /// <summary>
    /// TC-MDF-110 (as amended) — a <b>calendar-period duplicate under a different period type</b> is
    /// withheld, not written twice (review round 1, D5). VYK stores the annual 2025-12-31 period as
    /// <c>FY</c> while the public decoder fails safe to <c>Q</c> for an annual report whose
    /// <c>Periyot</c> is absent/unknown; the store's natural key includes the period type, so an
    /// exact-key-only overlap check would write a second row for one real period with no finding.
    /// The fix names the disagreement under its own reason and leaves the incumbent untouched.
    /// </summary>
    [Fact]
    public async Task TC_MDF_110_b_a_cross_source_period_type_disagreement_is_named_and_withheld()
    {
        await ResetAsync();
        await BindAsync("ASELS");
        StubDoubles();

        var decoded = KapPublicReportDecoder.Decode(
            RealSourceCatalog.Get(RealSourceCatalog.KapPublicReport).Body,
            "ASELS");
        var foreignPeriodEnd = new DateOnly(2025, 12, 31);
        var disagreementFacts = decoded.Payload.Statements
            .Where(fact => fact.PeriodEndDate == foreignPeriodEnd)
            .ToList();
        var writeableFacts = decoded.Payload.Statements.Except(disagreementFacts).ToList();
        Assert.NotEmpty(disagreementFacts);
        Assert.NotEmpty(writeableFacts);
        Assert.All(disagreementFacts, fact => Assert.Equal("Q", fact.PeriodType));

        // The incumbent holds the same calendar period, statement type by statement type, as FY.
        long instrumentId;
        await using (var db = _postgres.CreateContext())
        {
            instrumentId = (await db.Instruments.SingleAsync(instrument => instrument.Symbol == "ASELS")).Id;
            foreach (var fact in disagreementFacts)
            {
                await SeedVykStatementAsync(db, instrumentId, foreignPeriodEnd, "FY", fact.StatementType);
            }
        }

        await using (var host = BuildIngestion())
        {
            var result = await RunAsync(host);
            Assert.Equal(writeableFacts.Count, result.Written);
            Assert.Equal(IngestResult.Partial, result.Status);
        }

        await using (var db = _postgres.CreateContext())
        {
            // Named: one finding per withheld fact, under the disagreement's own reason.
            var findings = await db.QuarantinedFacts
                .Where(quarantine => quarantine.ReasonCode == StatementsJob.SourcePeriodTypeDisagreement)
                .ToListAsync();
            Assert.Equal(disagreementFacts.Count, findings.Count);
            Assert.All(findings, row =>
            {
                Assert.Equal(KapPublicSourceOptions.SourceName, row.SourceRef);
                using var evidence = JsonDocument.Parse(row.PayloadJson!);
                Assert.Equal("ASELS", evidence.RootElement.GetProperty("symbol").GetString());
                Assert.Equal("2025-12-31", evidence.RootElement.GetProperty("periodEndDate").GetString());
                Assert.Equal("Q", evidence.RootElement.GetProperty("withheldPeriodType").GetString());
                Assert.Equal("FY", evidence.RootElement.GetProperty("incumbentPeriodType").GetString());
                Assert.Equal("kap", evidence.RootElement.GetProperty("incumbentSource").GetString());
            });
            // It is not reported as a plain overlap: the two cases stay distinguishable.
            Assert.Empty(await db.QuarantinedFacts
                .Where(quarantine => quarantine.ReasonCode == StatementsJob.SourceOverlapRetained)
                .ToListAsync());

            // Not written twice: the calendar period still holds exactly the FY incumbents.
            var rowsAtPeriod = await db.FinancialStatements
                .Where(statement => statement.InstrumentId == instrumentId
                    && statement.PeriodEndDate == foreignPeriodEnd)
                .ToListAsync();
            Assert.Equal(disagreementFacts.Count, rowsAtPeriod.Count);
            Assert.All(rowsAtPeriod, row =>
            {
                Assert.Equal("FY", row.PeriodType);
                Assert.Equal("kap", row.SourceRef);
            });

            // The report's other periods are still written under this lane's provenance.
            var publicRows = await db.FinancialStatements
                .Where(statement => statement.InstrumentId == instrumentId
                    && statement.SourceRef == KapPublicSourceOptions.SourceName)
                .ToListAsync();
            Assert.DoesNotContain(publicRows, row => row.PeriodEndDate == foreignPeriodEnd);
            Assert.Equal(writeableFacts.Count, publicRows.Count);
        }
    }

    // =============================================================================================
    // TC-MDF-121 — the quarantined-cursor recovery: a transport-failure quarantine owes exactly one
    // replay, and a verdict the source already delivered is terminal
    // =============================================================================================

    /// <summary>
    /// TC-MDF-121 (i) — a <b>transport-failure</b> detail quarantine (the live runs' own signature:
    /// <c>failed in transport</c>, <c>bodyReceived: false</c>) is replayed <b>once</b> by a later
    /// pass and the recovered report's facts are written; a replay that fails again is re-quarantined
    /// <b>terminally</b>; and no third attempt ever fires.
    /// <para>
    /// The red this case carries is the live runs' own observed state (2026-10-26): the failed
    /// detail was quarantined <b>and</b> marked completed, so its window closed, the watermark moved
    /// past it and the report could never be listed again — a transient outage silently bounded the
    /// company's history. With the replay disabled, the second pass issues <b>zero</b> report
    /// requests for a now-healthy source (<c>Expected: 1, Actual: 0</c>), which is exactly the
    /// cursor-locked world the live walk produced.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TC_MDF_121_a_transport_failure_quarantine_is_replayed_once_and_the_recovered_report_is_written()
    {
        await ResetAsync();
        await BindAsync("ASELS");

        // --- pass 1: every report fetch dies in transport (the burst shape) -------------------
        StubDoubles(serveInterimBody: false);
        StubTransportFailure(InterimIndex, Q1Index, AnnualIndex);
        await using (var host = BuildTransportIngestion())
        {
            var first = await RunAsync(host);
            Assert.Equal(IngestResult.Partial, first.Status);
            Assert.Equal(0, first.Written);
            Assert.Equal(3, first.Quarantined);
        }

        // Every failed index owes its one replay; none has spent it yet.
        var afterFirst = await ReadCursorAsync("ASELS");
        Assert.NotNull(afterFirst);
        Assert.Equal(
            new[] { AnnualIndex, Q1Index, InterimIndex }.Order(),
            afterFirst!.Quarantined.Order());
        Assert.Empty(afterFirst.Retried);
        // The failures are completed reports for the walk's purposes: the window closed and the
        // watermark moved past them (that is exactly why the replay has to be remembered).
        Assert.All([AnnualIndex, Q1Index, InterimIndex], index => Assert.Contains(index, afterFirst.Seen));

        // --- pass 2: the source is healthy again for the interim; the other two still die ------
        ResetDoubles();
        StubDoubles();
        StubTransportFailure(Q1Index, AnnualIndex);
        var log = new LogSink();
        await using (var host = BuildTransportIngestion(logSink: log))
        {
            var second = await RunAsync(host);

            // The recovered index was fetched exactly <b>once</b> on this pass — and this one is
            // countable, because its double answers immediately: an aborting stub (no body ever
            // arrives) is never entered into the request journal at all, so a per-index count is
            // only evidence where the answer actually came back. The two still-failing replays are
            // witnessed below instead, by the pass's own ledger and by the tombstones they spent.
            Assert.Equal(1, _wireMock.RequestsFor(string.Format(ReportRoute, InterimIndex)));

            // The recovery is a write: the interim's facts land under this lane's provenance.
            Assert.True(second.Written > 0, $"the recovered report's facts must be written ({second.Status})");
            // The two that failed again are still counted (a hole is never silent) and are still
            // quarantined — the run is honest `partial`, never green, never `failed`.
            Assert.Equal(IngestResult.Partial, second.Status);
            Assert.Equal(2, second.Quarantined);

            // The run's own stats name all three outcomes verbatim (NFR-MDF-001): three pending
            // replays, three attempts — exactly one each — one recovered, two now terminal.
            Assert.Contains(
                log.Messages,
                message => message.Contains(
                    "quarantine-replays 3 (recovered 1, still-quarantined 2)",
                    StringComparison.Ordinal));
        }

        await using (var db = _postgres.CreateContext())
        {
            var asels = await db.Instruments.SingleAsync(instrument => instrument.Symbol == "ASELS");
            var stored = await db.FinancialStatements
                .Where(statement => statement.InstrumentId == asels.Id
                    && statement.SourceRef == KapPublicSourceOptions.SourceName)
                .ToListAsync();
            Assert.NotEmpty(stored);
            Assert.Contains(stored, row => row.PeriodEndDate == InterimPeriodEnd);

            // Every attempt keeps its evidence, and the evidence says what actually happened: no
            // body was ever received. Three rows from pass 1, two more from pass 2's re-quarantine
            // (the interim's recovery adds none).
            var unfetchable = await db.QuarantinedFacts
                .Where(quarantine => quarantine.ReasonCode == VykDisclosureWalker.DisclosureDetailUnfetchable)
                .ToListAsync();
            Assert.Equal(5, unfetchable.Count);
            Assert.All(unfetchable, row => Assert.Contains(
                "\"bodyReceived\": false",
                row.PayloadJson!,
                StringComparison.Ordinal));
        }

        // The recovery is persisted and the two terminal failures spent their one replay.
        var afterSecond = await ReadCursorAsync("ASELS");
        Assert.NotNull(afterSecond);
        Assert.Empty(afterSecond!.PendingReplay());
        Assert.Equal(
            new[] { AnnualIndex, Q1Index, InterimIndex }.Order(),
            afterSecond.Retried.Order());

        // --- pass 3: no third attempt, for any index -----------------------------------------
        // Healthy doubles on this pass, deliberately: a route that answers at once is entered into
        // the journal the moment it is asked, so `0 requests` here means the lane never asked —
        // not that the journal could not see the ask. (The spent replay is a tombstone, so even a
        // now-healthy source is never re-fetched.)
        ResetDoubles();
        StubDoubles();
        await using (var host = BuildTransportIngestion())
        {
            await RunAsync(host);
        }

        Assert.Equal(0, _wireMock.RequestsFor(string.Format(ReportRoute, InterimIndex)));
        Assert.Equal(0, _wireMock.RequestsFor(string.Format(ReportRoute, Q1Index)));
        Assert.Equal(0, _wireMock.RequestsFor(string.Format(ReportRoute, AnnualIndex)));
    }

    /// <summary>
    /// TC-MDF-121 (ii) — a <b>shape</b> verdict is terminal and never replayed: a body the source
    /// delivered in a non-report shape (<c>UNPARSEABLE_PAYLOAD</c>) is not a transport outage, and
    /// re-fetching it would spend ~900 KB to re-learn the same answer. Only the transport failure
    /// beside it is registered for replay, and it is replayed once.
    /// </summary>
    [Fact]
    public async Task TC_MDF_121_a_shape_verdict_is_terminal_and_only_the_transport_failure_is_replayed()
    {
        await ResetAsync();
        await BindAsync("ASELS");

        // Pass 1: the interim decodes, one report answers a 200 that is not a report (a shape
        // verdict), one dies in transport (a replayable outage).
        StubDoubles();
        _wireMock.StubJson(
            string.Format(ReportRoute, Q1Index),
            """{"error":"this is not a report body"}""",
            200);
        StubTransportFailure(AnnualIndex);
        await using (var host = BuildTransportIngestion())
        {
            var first = await RunAsync(host);
            Assert.Equal(IngestResult.Partial, first.Status);
            Assert.Equal(2, first.Quarantined);
        }

        await using (var db = _postgres.CreateContext())
        {
            Assert.Equal(
                1,
                await db.QuarantinedFacts.CountAsync(row => row.ReasonCode == QuarantineReason.UnparseablePayload));
            Assert.Equal(
                1,
                await db.QuarantinedFacts.CountAsync(row => row.ReasonCode == VykDisclosureWalker.DisclosureDetailUnfetchable));
        }

        // Only the transport failure owes a replay; the shape verdict is terminal (and is recorded
        // as neither pending nor spent — the cursor never re-tries it).
        var afterFirst = await ReadCursorAsync("ASELS");
        Assert.NotNull(afterFirst);
        Assert.Equal([AnnualIndex], afterFirst!.Quarantined);
        Assert.DoesNotContain(Q1Index, afterFirst.Retried);
        Assert.Equal(1, _wireMock.RequestsFor(string.Format(ReportRoute, Q1Index)));

        // Pass 2: replay the one transport failure; the shape-quarantined report is never fetched
        // again, on this pass or any other.
        ResetDoubles();
        StubDoubles();
        StubTransportFailure(AnnualIndex);

        var log = new LogSink();
        await using (var host = BuildTransportIngestion(logSink: log))
        {
            await RunAsync(host);
        }

        Assert.Equal(0, _wireMock.RequestsFor(string.Format(ReportRoute, Q1Index)));
        // The transport failure's own attempt is not journalable (no body ever came back); the
        // pass's ledger and the tombstone below are what witness it — one pending replay, one
        // attempt, one spent tombstone.
        Assert.Contains(
            log.Messages,
            message => message.Contains("quarantine-replays 1 (recovered 0, still-quarantined 1)", StringComparison.Ordinal));

        var afterSecond = await ReadCursorAsync("ASELS");
        Assert.Equal([AnnualIndex], afterSecond!.Retried);
        Assert.Empty(afterSecond.PendingReplay());

        // Pass 3 proves the tombstone: the spent replay is never re-attempted, now that the source
        // would answer at once (so an attempt would show up in the journal immediately).
        ResetDoubles();
        StubDoubles();
        await using (var host = BuildTransportIngestion())
        {
            await RunAsync(host);
        }

        Assert.Equal(0, _wireMock.RequestsFor(string.Format(ReportRoute, AnnualIndex)));
        Assert.Equal(0, _wireMock.RequestsFor(string.Format(ReportRoute, Q1Index)));
    }

    /// <summary>
    /// TC-MDF-121 (iii) — a source that <b>answered</b> is not a transport outage, even though its
    /// answer lands under the same quarantine word: a non-success status carrying the gateway's own
    /// body is terminal (replaying it would be a retry ladder against a source that already spoke),
    /// and the transport failure beside it is the only replayable one. This is also the case the
    /// 082 suite's own unfetchable-report worlds pin, which is why the class is drawn from what the
    /// read carried, never from the status code alone.
    /// </summary>
    [Fact]
    public async Task TC_MDF_121_a_non_success_status_with_a_body_is_terminal_while_the_transport_failure_replays()
    {
        await ResetAsync();
        await BindAsync("ASELS");

        // The interim decodes normally (so the company has a real report beside the two failed
        // ones); Q1 answers a 500 carrying the gateway's own body; the annual dies in transport.
        StubDoubles(failingReports: [Q1Index]);
        StubTransportFailure(AnnualIndex);
        await using (var host = BuildTransportIngestion())
        {
            var first = await RunAsync(host);
            Assert.Equal(IngestResult.Partial, first.Status);
            Assert.Equal(2, first.Quarantined);
        }

        var afterFirst = await ReadCursorAsync("ASELS");
        Assert.Equal([AnnualIndex], afterFirst!.Quarantined);
        Assert.DoesNotContain(Q1Index, afterFirst.Quarantined);

        // The status answer is never re-fetched (its double answers at once, so its absence from
        // the journal is real evidence); the transport failure is replayed once — witnessed by the
        // pass's ledger and the tombstone, since a body that never arrived is never journaled.
        ResetDoubles();
        StubDoubles(failingReports: [Q1Index]);
        StubTransportFailure(AnnualIndex);
        var log = new LogSink();
        await using (var host = BuildTransportIngestion(logSink: log))
        {
            await RunAsync(host);
        }

        Assert.Equal(0, _wireMock.RequestsFor(string.Format(ReportRoute, Q1Index)));
        Assert.Contains(
            log.Messages,
            message => message.Contains("quarantine-replays 1 (recovered 0, still-quarantined 1)", StringComparison.Ordinal));

        var afterSecond = await ReadCursorAsync("ASELS");
        Assert.Equal([AnnualIndex], afterSecond!.Retried);
        Assert.Empty(afterSecond.PendingReplay());
    }

    /// <summary>
    /// TC-MDF-121 (iv) — the replay rides the ordinary pass with the pass's own bounds: a replayed
    /// index costs a <b>detail fetch</b>, so the detail ceiling bounds it exactly like any other
    /// fetch. A bounded pass spends what it may, keeps the unspent backlog in the cursor, records
    /// the named truncation and resumes the backlog on the next pass.
    /// </summary>
    [Fact]
    public async Task TC_MDF_121_the_replay_counts_against_the_pass_detail_ceiling_and_the_backlog_resumes()
    {
        await ResetAsync();
        await BindAsync("ASELS");

        // Seed the backlog: three transport-failure quarantines, no window content written.
        StubDoubles(serveInterimBody: false);
        StubTransportFailure(InterimIndex, Q1Index, AnnualIndex);
        await using (var host = BuildTransportIngestion())
        {
            await RunAsync(host);
        }

        Assert.Equal(3, (await ReadCursorAsync("ASELS"))!.PendingReplay().Count);

        // Pass 2 with a one-detail ceiling: exactly one replay, the rest of the backlog stays owed.
        ResetDoubles();
        StubDoubles();
        StubTransportFailure(Q1Index, AnnualIndex);
        var log = new LogSink();
        await using (var host = BuildTransportIngestion(
            new Dictionary<string, string?>
            {
                ["Ingestion:KapPublic:MaxDetailFetches"] = "1",
            },
            logSink: log))
        {
            var bounded = await RunAsync(host);
            Assert.Equal(KapPublicReasonCodes.DetailCap, Assert.Single(bounded.SymbolStats).Reason);
            Assert.Equal(IngestResult.Partial, bounded.Status);
        }

        // The one admitted replay is the backlog's oldest index (the annual, which fails again —
        // not journalable, no body ever arrived), and the ceiling stops before the recoverable
        // interim: its double answers at once, so `0` here is real evidence of the bound.
        Assert.Equal(0, _wireMock.RequestsFor(string.Format(ReportRoute, InterimIndex)));
        Assert.Contains(
            log.Messages,
            message => message.Contains("quarantine-replays 1 (recovered 0, still-quarantined 1)", StringComparison.Ordinal));

        var afterBounded = await ReadCursorAsync("ASELS");
        Assert.Equal([AnnualIndex], afterBounded!.Retried);
        Assert.Equal(2, afterBounded.PendingReplay().Count);

        // Pass 3 (default ceiling) drains the backlog: the recoverable report is written and the
        // other one is spent terminally.
        ResetDoubles();
        StubDoubles();
        StubTransportFailure(Q1Index, AnnualIndex);
        await using (var host = BuildTransportIngestion())
        {
            var resumed = await RunAsync(host);
            Assert.True(resumed.Written > 0, "the backlog's recoverable report must be written on the resumed pass");
        }

        // The interim was replayed on this pass — its answered report shows in the journal.
        Assert.Equal(1, _wireMock.RequestsFor(string.Format(ReportRoute, InterimIndex)));

        var afterResumed = await ReadCursorAsync("ASELS");
        Assert.Empty(afterResumed!.PendingReplay());
        Assert.Equal(3, afterResumed.Retried.Count);
    }

    // =============================================================================================
    // Harness
    // =============================================================================================

    /// <summary>
    /// The production ingestion composition over the Testcontainers database and the public-site
    /// double (the 079/080 harness convention: pacing bound to <c>0</c>, posture selected explicitly
    /// because the code default is a refusal).
    /// </summary>
    private IngestionHost BuildIngestion(
        IReadOnlyDictionary<string, string?>? extraSettings = null,
        TimeProvider? clock = null,
        KapPublicDelay? publicDelay = null,
        LogSink? logSink = null)
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ConnectionStrings:Default"] = _postgres.ConnectionString,
            ["Ingestion:Statements:UniverseIndex"] = StatementBound.IndexCode,
            ["Ingestion:Statements:RequestDelayMs"] = "0",
            ["Ingestion:Statements:Source"] = StatementsSourceOptions.KapPublicSource,
            ["Ingestion:Statements:KapPublicLookbackMonths"] = "24",
            ["Ingestion:Statements:KapPublicWindowDays"] = "3650",
            ["Ingestion:KapPublic:BaseUrl"] = _wireMock.BaseUrl,
            ["Ingestion:KapPublic:RequestDelayMs"] = "0",
            ["Ingestion:KapPublic:ByCriteriaPath"] = ByCriteriaPath,
            ["Ingestion:KapPublic:AutomatedAccess"] = nameof(KapPublicAccessPosture.Allowed),
            // The VYK client points at the double too, so *any* VYK request would show up in the
            // journal — which is what makes the "no VYK request" assertions worth something.
            ["Ingestion:Kap:BaseUrl"] = _wireMock.BaseUrl,
        };

        if (extraSettings is not null)
        {
            foreach (var (key, value) in extraSettings)
            {
                settings[key] = value;
            }
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        if (logSink is not null)
        {
            // The lane's own log lines are part of its honesty contract (the recovered /
            // still-quarantined counts), so a case that asserts them reads them from here.
            services.AddSingleton<ILoggerProvider>(new SinkLoggerProvider(logSink));
        }
        services.AddSingleton<TimeProvider>(clock ?? new FakeTimeProvider(RecordedAt));
        services.AddMarketDataIngestion(configuration);

        // A walk-level wall-clock case needs the pacer to move the injected clock (the pacer is the
        // only production seam that runs between two admitted requests); the default stays the real
        // timer, so every other case is untouched.
        if (publicDelay is not null)
        {
            services.AddSingleton(publicDelay);
        }

        return new IngestionHost(services.BuildServiceProvider());
    }

    /// <summary>
    /// The host a <b>transport-failure</b> case needs: a real clock (the per-item timeout is a real
    /// timer — the harness's fake clock never advances on its own, so a delayed answer would hang
    /// rather than fail) and a 150 ms detail bound, so a report route that answers after seconds is
    /// a genuine transport failure and not a shape verdict. Only the failure class uses this; every
    /// other case keeps the deterministic fake clock.
    /// </summary>
    private IngestionHost BuildTransportIngestion(
        IReadOnlyDictionary<string, string?>? extraSettings = null,
        LogSink? logSink = null)
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Ingestion:KapPublic:DetailFetchTimeoutMs"] = "150",
        };

        if (extraSettings is not null)
        {
            foreach (var (key, value) in extraSettings)
            {
                settings[key] = value;
            }
        }

        return BuildIngestion(settings, clock: TimeProvider.System, logSink: logSink);
    }

    private static async Task<IngestResult> RunAsync(IngestionHost host)
    {
        using var scope = host.Provider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IIngestionJobRunner>();
        return await runner.RunAsync(StatementsJob.Code, new IngestionRequest(T));
    }

    /// <summary>
    /// The public-site doubles: the committed byCriteria list and the committed interim body. Report
    /// indices named in <paramref name="failingReports"/> answer a 500 carrying the source's own error
    /// shape (the failure the isolation rule must keep, verbatim).
    /// </summary>
    private void StubDoubles(IReadOnlyList<long>? failingReports = null, bool serveInterimBody = true)
    {
        StubList();

        if (serveInterimBody)
        {
            var report = RealSourceCatalog.Get(RealSourceCatalog.KapPublicReport);
            _wireMock.StubBytes(
                string.Format(ReportRoute, InterimIndex),
                report.RawBytes,
                "text/html; charset=utf-8");
        }

        foreach (var failing in failingReports ?? [])
        {
            _wireMock.Server
                .Given(Request.Create().WithPath(string.Format(ReportRoute, failing)).UsingGet())
                .RespondWith(Response.Create()
                    .WithStatusCode(500)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody("""{"error":"report unavailable"}"""));
        }
    }

    /// <summary>
    /// Stubs report indices to fail <b>in transport</b>: the answer arrives far too late to be read
    /// inside the lane's per-item bound, so <b>no body ever reaches the lane</b> — the live runs'
    /// recorded signature (<c>failed in transport</c>, <c>bodyReceived: false</c>), and the class
    /// TKT-foundation-092 replays once. This is a real connection that never delivers, which is what
    /// a shape verdict and a non-success status are not.
    /// <para>
    /// The delay is deliberately far longer than any one case runs (and than the 150 ms bound): an
    /// aborted answer is entered into the request journal only when it eventually completes, so a
    /// short delay would let a stray entry land in a <i>later</i> pass's journal and make a request
    /// count flaky. With a delay no test can outlive, an aborted request is never journaled at all —
    /// deterministic, at the price that these indices' own attempts are witnessed by the pass's
    /// ledger and the cursor's tombstones rather than by a request count.
    /// </para>
    /// </summary>
    private void StubTransportFailure(params long[] indexes)
    {
        foreach (var index in indexes)
        {
            _wireMock.Server
                .Given(Request.Create().WithPath(string.Format(ReportRoute, index)).UsingGet())
                .RespondWith(Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "text/html; charset=utf-8")
                    .WithBody("<html><table></table></html>")
                    .WithDelay(TimeSpan.FromSeconds(30)));
        }
    }

    private void StubList(int statusCode = 200) =>
        _wireMock.StubJson(
            ByCriteriaPath,
            RealSourceCatalog.Get(RealSourceCatalog.KapPublicByCriteria).Body,
            statusCode,
            method: "POST");

    /// <summary>
    /// A two-window list double: the <b>first</b> window answers the committed capture, the
    /// <b>second</b> answers a 500 (a genuine list source-down). Each window is matched on the
    /// request's own <c>fromDate</c>, so the failure lands exactly where the test says it does.
    /// </summary>
    private void StubWindowedList(DateOnly firstWindowFrom, DateOnly secondWindowFrom)
    {
        _wireMock.Server
            .Given(Request.Create()
                .WithPath(ByCriteriaPath)
                .UsingPost()
                .WithBody((string? body) =>
                    body!.Contains($"\"fromDate\":\"{firstWindowFrom:yyyy-MM-dd}\"", StringComparison.Ordinal)))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(RealSourceCatalog.Get(RealSourceCatalog.KapPublicByCriteria).Body));

        _wireMock.Server
            .Given(Request.Create()
                .WithPath(ByCriteriaPath)
                .UsingPost()
                .WithBody((string? body) =>
                    body!.Contains($"\"fromDate\":\"{secondWindowFrom:yyyy-MM-dd}\"", StringComparison.Ordinal)))
            .RespondWith(Response.Create()
                .WithStatusCode(500)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"error":"list unavailable"}"""));

        var report = RealSourceCatalog.Get(RealSourceCatalog.KapPublicReport);
        _wireMock.StubBytes(
            string.Format(ReportRoute, InterimIndex),
            report.RawBytes,
            "text/html; charset=utf-8");
    }

    /// <summary>The canonical statements double the VYK lane reads when it is selected.</summary>
    private void StubVykCanonical() =>
        _wireMock.StubJson(
            "/statements",
            """
            {"sourceRef":"kap","symbol":"ASELS","unitScale":"thousands","currency":"TRY","statements":[]}
            """);

    private void ResetDoubles()
    {
        _wireMock.Server.ResetMappings();
        _wireMock.Server.ResetLogEntries();
    }

    private async Task ResetAsync()
    {
        await using (var db = _postgres.CreateContext())
        {
            await FixtureSeeder.ApplyAsync(db, L2);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM kap_public_company_map");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM financial_statements");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM quarantined_facts");
            await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM coverage_metadata WHERE data_type IN ({0}, {1}, {2})",
                KapPublicStatementCursorStore.DataType,
                KapPublicStatementDepthStore.DataType,
                KapDisclosureCursorStore.DataType);
        }

        ResetDoubles();
    }

    /// <summary>
    /// Binds <paramref name="symbols"/>: the instrument rows, the test-local <c>STMT</c> fetch bound,
    /// membership of the production universe index (so the serving read path can resolve the symbol),
    /// and — for <c>ASELS</c> only — the <b>stored</b> public oid mapping the walk reads. A symbol
    /// without a mapping stays unmapped on purpose: that is a case of its own.
    /// </summary>
    private async Task BindAsync(params string[] symbols)
    {
        await using var db = _postgres.CreateContext();
        foreach (var symbol in symbols)
        {
            if (await db.Instruments.AnyAsync(instrument => instrument.Symbol == symbol))
            {
                continue;
            }

            db.Instruments.Add(new Instrument
            {
                Symbol = symbol,
                Isin = $"TRA{symbol}00001",
                Name = symbol,
                Status = "active",
                SourceRef = "test:kap-public-statements",
                RecordedAt = RecordedAt,
            });
        }

        await db.SaveChangesAsync();
        await StatementBound.ReplaceAsync(db, symbols);
        await BindProductionUniverseAsync(db, symbols);

        if (symbols.Contains("ASELS", StringComparer.Ordinal))
        {
            var asels = await db.Instruments.SingleAsync(instrument => instrument.Symbol == "ASELS");
            db.KapPublicCompanyMaps.Add(new KapPublicCompanyMap
            {
                InstrumentId = asels.Id,
                KapPublicOid = AselsPublicOid,
                KapPublicOidSource = "test: committed kap-public identity capture 2026-10-25",
                KapPublicOidAsOf = T,
                RecordedAt = RecordedAt,
            });
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Maps one more symbol to a stored public oid, so a two-company case can walk both (the map is
    /// per instrument; the oid it points at is the same public company — the point of the case is the
    /// <b>budget</b>, not the identity).
    /// </summary>
    private async Task MapPublicOidAsync(string symbol, string oid = AselsPublicOid)
    {
        await using var db = _postgres.CreateContext();
        var instrument = await db.Instruments.SingleAsync(candidate => candidate.Symbol == symbol);
        db.KapPublicCompanyMaps.Add(new KapPublicCompanyMap
        {
            InstrumentId = instrument.Id,
            KapPublicOid = oid,
            KapPublicOidSource = "test: budget-fairness mapping",
            KapPublicOidAsOf = T,
            RecordedAt = RecordedAt,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// How many byCriteria list requests carried <paramref name="oid"/> — the observable answer to
    /// "which company did a pass walk?" (the pass-order case, review round 2, D14).
    /// </summary>
    private int ListRequestsFor(string oid) =>
        _wireMock.Server.LogEntries.Count(entry =>
            entry.RequestMessage is { Path: ByCriteriaPath, Body: { } body }
            && body.Contains($"\"{oid}\"", StringComparison.Ordinal));

    /// <summary>The persisted public-site cursor for <paramref name="symbol"/>, or <c>null</c> when none exists.</summary>
    private async Task<KapPublicStatementCursor?> ReadCursorAsync(string symbol)
    {
        await using var db = _postgres.CreateContext();
        var instrumentId = await db.Instruments
            .Where(instrument => instrument.Symbol == symbol)
            .Select(instrument => (long?)instrument.Id)
            .SingleAsync();
        var notes = await db.CoverageMetadata
            .Where(row => row.DataType == KapPublicStatementCursorStore.DataType
                && row.InstrumentId == instrumentId)
            .Select(row => row.Notes)
            .FirstOrDefaultAsync();
        return KapPublicStatementCursor.TryParse(notes);
    }

    /// <summary>Adds the bound symbols to <c>XU100</c>'s current membership (the serving path's universe).</summary>
    private static async Task BindProductionUniverseAsync(DegerliDbContext db, params string[] symbols)
    {
        var xu100 = await db.Indices.SingleAsync(index => index.Code == "XU100");
        var instruments = await db.Instruments
            .Where(instrument => symbols.Contains(instrument.Symbol))
            .ToDictionaryAsync(instrument => instrument.Symbol, instrument => instrument.Id);

        foreach (var symbol in symbols)
        {
            var instrumentId = instruments[symbol];
            var bound = await db.IndexConstituents.AnyAsync(constituent =>
                constituent.IndexId == xu100.Id
                && constituent.InstrumentId == instrumentId
                && constituent.EffectiveTo == null);
            if (bound)
            {
                continue;
            }

            db.IndexConstituents.Add(new IndexConstituent
            {
                IndexId = xu100.Id,
                InstrumentId = instrumentId,
                EffectiveFrom = new DateOnly(2025, 1, 2),
                SourceRef = "test:kap-public-statements",
                RecordedAt = RecordedAt,
            });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds one VYK-sourced period (the incumbent half of the mixed world) with a line the serving
    /// path reads, so "the incumbent row is retained as-is" is testable rather than asserted into
    /// existence.
    /// </summary>
    private static async Task SeedVykStatementAsync(
        DegerliDbContext db,
        long instrumentId,
        DateOnly periodEnd,
        string periodType,
        string statementType = "IS",
        DateOnly? publishedAt = null)
    {
        var statement = new FinancialStatement
        {
            InstrumentId = instrumentId,
            PeriodType = periodType,
            PeriodEndDate = periodEnd,
            FiscalYear = periodEnd.Year,
            StatementType = statementType,
            Version = 0,
            PublishedAt = publishedAt ?? periodEnd,
            SourceUnitScale = "thousands",
            SourceCurrency = "TRY",
            SourceRef = "kap",
            RecordedAt = RecordedAt,
        };
        db.FinancialStatements.Add(statement);
        await db.SaveChangesAsync();

        db.FinLineItems.Add(new FinLineItem
        {
            StatementId = statement.Id,
            ItemCode = "REV",
            Value = 1_000m,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Locates a repository file from the test binary's directory by walking up to the checkout root
    /// (the configuration-equivalence convention the shipped-default assertions use).
    /// </summary>
    private static string FindRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"{relativePath} was not found above {AppContext.BaseDirectory}.");
    }

    private sealed class IngestionHost : IAsyncDisposable
    {
        public IngestionHost(ServiceProvider provider) => Provider = provider;

        public ServiceProvider Provider { get; }

        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    /// <summary>
    /// The run's log lines, collected so a case can assert what the run <b>said</b> as well as what
    /// it stored — the recovered / still-quarantined counts are the honesty half of TC-MDF-121 and
    /// have no other observable surface (the per-symbol ledger stat type is shared with other
    /// lanes and is not this ticket's to widen).
    /// </summary>
    private sealed class LogSink
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _messages = new();

        public IReadOnlyList<string> Messages => _messages.ToArray();

        public void Add(string message) => _messages.Enqueue(message);
    }

    private sealed class SinkLoggerProvider : ILoggerProvider
    {
        private readonly LogSink _sink;

        public SinkLoggerProvider(LogSink sink) => _sink = sink;

        public ILogger CreateLogger(string categoryName) => new SinkLogger(_sink);

        public void Dispose()
        {
        }
    }

    private sealed class SinkLogger : ILogger
    {
        private readonly LogSink _sink;

        public SinkLogger(LogSink sink) => _sink = sink;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            _sink.Add(formatter(state, exception));
    }
}
