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
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
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

        // The newest report came first: a bounded pass spends its budget on what a consumer needs
        // now, and truncation loses the oldest, never the newest.
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
    }

    /// <summary>
    /// TC-MDF-109 (iii) — the <b>wall-clock</b> bound is real and is the lane's own 90-minute bound,
    /// which is strictly below the scheduler's per-step bound: the budget truncates with
    /// <c>WALK_DEADLINE</c> when the injected clock passes it (no sleeps, no wall-clock dependence).
    /// </summary>
    [Fact]
    public async Task TC_MDF_109_iii_the_wall_clock_bound_truncates_the_pass()
    {
        var clock = new FakeTimeProvider(RecordedAt);
        var options = new KapPublicSourceOptions();

        // The shipped bound, and the relation to the scheduler that makes it usable.
        Assert.Equal(TimeSpan.FromMinutes(90), options.MaxWalkDuration);
        Assert.True(options.MaxWalkDuration < IngestionSchedulerOptions.DefaultMaxStepDuration);

        var budget = new KapPublicWalkBudget(
            options.MaxListPages,
            options.MaxDetailFetches,
            options.MaxWalkDuration,
            clock);

        Assert.True(budget.TryBeginPage());
        clock.Advance(TimeSpan.FromMinutes(90));
        Assert.False(budget.TryBeginPage());
        Assert.True(budget.Truncated);
        Assert.Equal(KapPublicReasonCodes.WalkDeadline, budget.TruncationReason);

        await Task.CompletedTask;
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

    // =============================================================================================
    // Harness
    // =============================================================================================

    /// <summary>
    /// The production ingestion composition over the Testcontainers database and the public-site
    /// double (the 079/080 harness convention: pacing bound to <c>0</c>, posture selected explicitly
    /// because the code default is a refusal).
    /// </summary>
    private IngestionHost BuildIngestion(IReadOnlyDictionary<string, string?>? extraSettings = null)
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
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(RecordedAt));
        services.AddMarketDataIngestion(configuration);

        return new IngestionHost(services.BuildServiceProvider());
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

    private void StubList(int statusCode = 200) =>
        _wireMock.StubJson(
            ByCriteriaPath,
            RealSourceCatalog.Get(RealSourceCatalog.KapPublicByCriteria).Body,
            statusCode,
            method: "POST");

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
}
