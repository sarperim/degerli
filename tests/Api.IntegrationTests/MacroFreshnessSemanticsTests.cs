using System.Net;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Macro;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-foundation-038 acceptance — <b>TC-MOV-033</b>: macro staleness is per-series and
/// publication-frequency aware. The user-reported defect (2026-10-20 morning check): TÜİK
/// TÜFE and TCMB Politika Faizi served <c>Bayat</c> while serving their latest published
/// values, because the freshness rule was <em>job-scoped</em> and only counted
/// <c>succeeded</c> runs — the live <c>macro-cpi</c> ledger has fourteen <c>failed</c> runs,
/// two honest <c>partial</c> runs that really wrote the series, and no <c>succeeded</c> run
/// ever. Every case below reproduces that ledger shape on doubles (rows written to the test
/// database), never a live source.
/// <para>
/// The doctrine (01 §10.9.12 freshness note, 02 §5.6): a series is <b>failure-stale</b> only
/// when the owning job's latest failure is newer than <em>that series' own</em> latest
/// <c>recorded_at</c> (stored data is the evidence its ingest succeeded) and newer than the
/// job's latest success; a <b>monthly</b> series is age-stale only when the
/// publication window has closed behind its published value date
/// (<c>today &gt; asOf + <see cref="MacroFreshnessService.MonthlyPublicationWindowDays"/></c>);
/// <c>per_release</c> keeps its no-window posture; a series with no value is
/// <c>unavailable</c> and never contributes a stale flag to the strip envelope.
/// </para>
/// </summary>
public sealed class MacroFreshnessSemanticsTests : IClassFixture<PostgresFixture>
{
    /// <summary>Mirrors <see cref="DegerliAppFactory.Clock"/> so service and endpoint reads agree.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 18, 0, 0, TimeSpan.Zero);

    private static DateOnly Today => DateOnly.FromDateTime(Now.UtcDateTime);

    private readonly PostgresFixture _postgres;

    public MacroFreshnessSemanticsTests(PostgresFixture postgres) => _postgres = postgres;

    // ---------------------------------------------------------------------------------
    // TC-MOV-033 (i) — a `partial` run that wrote the series is evidence of success
    // ---------------------------------------------------------------------------------

    /// <summary>TC-MOV-033 (i) — the live ledger shape: <c>failed@t1</c>, then
    /// <c>partial@t2</c> which really wrote the monthly series (<c>recorded_at = t2</c>),
    /// and no failure afterwards. The series must serve <b>fresh</b> — its own stored data
    /// proves the ingest happened (red under the old job-scoped rule, stale forever).</summary>
    [Fact]
    public async Task TC_MOV_033_partial_run_that_wrote_the_series_serves_fresh()
    {
        var t1 = Now.AddDays(-1);
        var t2 = Now.AddHours(-12);

        await using var db = await ResetAsync();
        AddValue(db, Macro.Cpi, Today.AddDays(-35), 29.73m, t2);
        AddRun(db, MacroCpiJob.Code, t1, "failed");
        AddRun(db, MacroCpiJob.Code, t2, "partial");
        await db.SaveChangesAsync();

        var state = await Service(db).GetStateAsync(Macro.Cpi);

        Assert.Equal("fresh", state.State);
        Assert.False(state.Stale);
        Assert.Equal(29.73m, state.Value);
        Assert.Equal(Today.AddDays(-35), state.AsOf);
    }

    // ---------------------------------------------------------------------------------
    // TC-MOV-033 (ii) — a real missed ingest stays honestly stale
    // ---------------------------------------------------------------------------------

    /// <summary>TC-MOV-033 (ii) — the honesty direction survives: a job failure NEWER than
    /// the series' latest <c>recorded_at</c> is a genuinely missed ingest, so the last-known
    /// value still serves marked <b>stale</b> (FR-MOV-016 — never blanked).</summary>
    [Fact]
    public async Task TC_MOV_033_job_failure_newer_than_the_series_rows_serves_stale()
    {
        var recorded = Now.AddHours(-30);
        var failure = Now.AddHours(-2);

        await using var db = await ResetAsync();
        AddValue(db, Macro.Cpi, Today.AddDays(-35), 29.73m, recorded);
        AddRun(db, MacroCpiJob.Code, recorded.AddHours(-1), "succeeded");
        AddRun(db, MacroCpiJob.Code, failure, "failed");
        await db.SaveChangesAsync();

        var state = await Service(db).GetStateAsync(Macro.Cpi);

        Assert.True(state.Stale);
        Assert.Equal("stale", state.State);
        // Last-known-good is still served with its as-of date (FR-MOV-016).
        Assert.Equal(29.73m, state.Value);
        Assert.Equal(Today.AddDays(-35), state.AsOf);
    }

    // ---------------------------------------------------------------------------------
    // TC-MOV-033 (iii) — the monthly window is publication-aware, measured from the value date
    // ---------------------------------------------------------------------------------

    /// <summary>TC-MOV-033 (iii) — a monthly series' latest published value is fresh until its
    /// next expected publication window closes: <c>asOf = today − 40</c> serves fresh and
    /// <c>asOf = today − 70</c> (no newer value) serves stale, both under the shipped window.
    /// The <c>recorded_at</c> is recent in both cases, so the second cell is red under the old
    /// recorded_at-based rule — the fix is precision, never suppression.</summary>
    [Fact]
    public async Task TC_MOV_033_monthly_freshness_follows_the_publication_window_from_the_value_date()
    {
        // The shipped constant is the one documented knob (02 §5.6) — pinned here.
        Assert.Equal(65, MacroFreshnessService.MonthlyPublicationWindowDays);

        var recorded = Now.AddHours(-1);

        // Inside the window: TÜİK's latest published month (M+1-early publication).
        await using (var db = await ResetAsync())
        {
            AddValue(db, Macro.Cpi, Today.AddDays(-40), 29.73m, recorded);
            await db.SaveChangesAsync();

            var state = await Service(db).GetStateAsync(Macro.Cpi);
            Assert.False(state.Stale);
            Assert.Equal("fresh", state.State);
            Assert.Equal(Today.AddDays(-40), state.AsOf);
        }

        // Past the window with no newer value: a genuinely missed monthly publication.
        await using (var db = await ResetAsync())
        {
            AddValue(db, Macro.Cpi, Today.AddDays(-70), 29.73m, recorded);
            await db.SaveChangesAsync();

            var state = await Service(db).GetStateAsync(Macro.Cpi);
            Assert.True(state.Stale);
            Assert.Equal("stale", state.State);
            Assert.Equal(Today.AddDays(-70), state.AsOf);
        }
    }

    // ---------------------------------------------------------------------------------
    // TC-MOV-033 (iv) — the admin-entered series is unavailable, never false-stale
    // ---------------------------------------------------------------------------------

    /// <summary>TC-MOV-033 (iv) — <c>INDEP_CPI</c> is admin-entered (<c>per_release</c>, no
    /// owning job): with no value it serves <c>state: "unavailable"</c>, <c>stale: false</c>,
    /// and must not poison the strip envelope — even with a <c>macro-cpi</c> failure on the
    /// ledger that also owns the monthly pair.</summary>
    [Fact]
    public async Task TC_MOV_033_absent_independent_cpi_is_unavailable_and_does_not_mark_the_strip_stale()
    {
        var recorded = Now.AddHours(-12);

        await using (var db = await ResetAsync())
        {
            // The live shape: a `failed` macro-cpi run older than the rows the honest
            // `partial` run wrote this morning (the monthly pair + the policy rate).
            AddRun(db, MacroCpiJob.Code, Now.AddDays(-1), "failed");
            AddValue(db, Macro.Cpi, Today.AddDays(-35), 29.73m, recorded);
            AddValue(db, Macro.Repo, Today.AddDays(-65), 37.0m, recorded);

            // The daily lane is untouched by this ticket (fresh, as it serves now).
            var freshDaily = Now.AddHours(-1);
            AddValue(db, Macro.Usd, Today, 47.10m, freshDaily);
            AddValue(db, Macro.Eur, Today, 51.40m, freshDaily);
            AddValue(db, Macro.Gold, Today, 5200.00m, freshDaily);
            await db.SaveChangesAsync();
        }

        await using (var db = _postgres.CreateContext())
        {
            var service = Service(db);
            Assert.Equal("unavailable", (await service.GetStateAsync(Macro.Indep)).State);
            Assert.False((await service.GetStateAsync(Macro.Indep)).Stale);
            Assert.False((await service.GetStateAsync(Macro.Cpi)).Stale);
            // per_release keeps its no-publication-window posture (stale only on the
            // per-series failure rule, which the fresh rows above have cleared).
            Assert.False((await service.GetStateAsync(Macro.Repo)).Stale);
        }

        // The strip envelope aggregates available series only (the endpoint's
        // `series.Any(entry => entry.Stale)` reads the honest per-series flags).
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/market/macro");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        var indep = SeriesByCode(root, Macro.Indep);
        Assert.Equal(JsonValueKind.Null, indep.GetProperty("value").ValueKind);
        Assert.Equal("unavailable", indep.GetProperty("state").GetString());
        Assert.False(indep.GetProperty("stale").GetBoolean());

        Assert.False(root.GetProperty("stale").GetBoolean());
        Assert.False(SeriesByCode(root, Macro.Cpi).GetProperty("stale").GetBoolean());
        Assert.False(SeriesByCode(root, Macro.Repo).GetProperty("stale").GetBoolean());
    }

    // ---------------------------------------------------------------------------------
    // helpers
    // ---------------------------------------------------------------------------------

    /// <summary>Seeds the series definitions, then clears the macro state each case owns.</summary>
    private async Task<DegerliDbContext> ResetAsync()
    {
        var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, FixtureUniverse.Build(FixtureAnchor.L2));
        await db.Database.ExecuteSqlRawAsync("DELETE FROM macro_values");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
        return db;
    }

    private static MacroFreshnessService Service(DegerliDbContext db) =>
        new(db, new FakeTimeProvider(Now));

    private static void AddValue(
        DegerliDbContext db, string seriesCode, DateOnly valueDate, decimal value, DateTimeOffset recordedAt) =>
        db.MacroValues.Add(new MacroValue
        {
            SeriesCode = seriesCode,
            ValueDate = valueDate,
            Value = value,
            SourceRef = "evds://tcmb",
            RecordedAt = recordedAt,
        });

    private static void AddRun(DegerliDbContext db, string jobCode, DateTimeOffset finishedAt, string status) =>
        db.IngestRuns.Add(new IngestRun
        {
            JobCode = jobCode,
            StartedAt = finishedAt.AddMinutes(-1),
            FinishedAt = finishedAt,
            Status = status,
        });

    private static JsonElement SeriesByCode(JsonElement root, string code) =>
        root.GetProperty("series").EnumerateArray()
            .Single(entry => entry.GetProperty("code").GetString() == code);

    /// <summary>The canonical macro series codes (02 §3.2), spelled once for readability.</summary>
    private static class Macro
    {
        public const string Cpi = "TUIK_CPI";
        public const string Indep = "INDEP_CPI";
        public const string Repo = "CBRT_REPO";
        public const string Usd = "USD_TRY";
        public const string Eur = "EUR_TRY";
        public const string Gold = "GOLD";
    }
}
