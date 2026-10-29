using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;
using Degerli.Ingestion.Quarantine;
using Degerli.Ingestion.Validation;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-foundation-035 acceptance — the paced, admin-triggered bulk-resolve/acknowledge lane over
/// <c>CONFLICTING_VALUE</c> quarantine rows (user-ratified policy OPTION 1 of 2026-10-29:
/// keep-first, no fact-semantics change). TC-MDF-129's four cases pin the contract:
/// <list type="bullet">
/// <item>(a) a bounded batch acknowledges the oldest conflicting rows as accepted drift, records
/// the run in the ledger, and leaves the rest of the backlog quarantined and open;</item>
/// <item>(b) repeated runs drain the backlog deterministically and report an honest zero, with
/// the reviewed-queue count following them down;</item>
/// <item>(c) the note is required and the batch bound is explicit — a rejected request has no
/// side effect and writes no ledger row;</item>
/// <item>(d) the honesty posture itself: only <c>CONFLICTING_VALUE</c> rows are ever acknowledged,
/// the acknowledged payloads are retained verbatim (both conflicting values stay inspectable), and
/// no stored fact is modified or promoted.</item>
/// </list>
/// Every test runs through the real in-process API host against a migrated, fixture-seeded
/// Testcontainers PostgreSQL. No source is called: the lane reads and writes quarantine rows only.
/// </summary>
public sealed class AdminQuarantineBulkResolveTests : IClassFixture<PostgresFixture>
{
    private const string BuilderEmail = "builder@degerli.test";
    private const string FixturePassword = "FixturePass1!";
    private const string QueuePath = "/api/v1/admin/quarantine";
    private const string ResolvePath = "/api/v1/admin/quarantine/conflicting/resolve";
    private const string SummaryPath = "/api/v1/admin/summary";
    private const string RunsPath = "/api/v1/admin/ingest-runs";

    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;

    private static readonly DateTimeOffset QuarantinedAt =
        new(T.ToDateTime(new TimeOnly(20, 30, 0)), TimeSpan.Zero);

    private readonly PostgresFixture _postgres;

    public AdminQuarantineBulkResolveTests(PostgresFixture postgres) => _postgres = postgres;

    // ---------------------------------------------------------------------------------
    // TC-MDF-129 (a) — A bounded batch acknowledges the oldest drift and records the run
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task TC_MDF_129_a_Bounded_batch_acknowledges_the_oldest_conflicting_rows()
    {
        var seeded = await ResetAsync();

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var builder = await SignedInClientAsync(factory, BuilderEmail);

        var csrf = await CsrfTokenAsync(builder);
        using var response = await PostAsync(
            builder,
            ResolvePath,
            new { note = "İş Yatırım re-fetch drift — revision churn acknowledged", batchSize = 3 },
            csrf);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.True(root.GetProperty("runId").GetInt64() > 0);
        Assert.Equal(QuarantineReasonValues.Conflicting, root.GetProperty("reasonCode").GetString());
        Assert.Equal(3, root.GetProperty("batchSize").GetInt32());
        Assert.Equal(3, root.GetProperty("resolved").GetInt32());
        // Five conflicting rows were seeded: the batch bound leaves two retained, quarantined.
        Assert.Equal(2, root.GetProperty("retained").GetInt32());

        // Oldest-first: exactly the three earliest rows, in (quarantined_at, id) order.
        var resolvedIds = root.GetProperty("resolvedIds").EnumerateArray().Select(id => id.GetInt64()).ToList();
        Assert.Equal(seeded.ConflictingIds.Take(3), resolvedIds);

        await using (var db = _postgres.CreateContext())
        {
            var rows = await db.QuarantinedFacts.OrderBy(fact => fact.Id).ToListAsync();

            // Nothing is ever deleted (BR-MDF-007).
            Assert.Equal(seeded.TotalRows, rows.Count);

            foreach (var acknowledged in rows.Where(row => resolvedIds.Contains(row.Id)))
            {
                Assert.Equal("dismissed", acknowledged.Status);
                Assert.Equal("İş Yatırım re-fetch drift — revision churn acknowledged", acknowledged.ResolutionNote);
                // The rejected payload is retained beside the note — never cleared.
                Assert.False(string.IsNullOrWhiteSpace(acknowledged.PayloadJson));
                Assert.Contains("storedClose", acknowledged.PayloadJson);
            }

            // The rows beyond the bound stay open, un-noted and untouched.
            foreach (var retained in rows.Where(row =>
                         seeded.ConflictingIds.Skip(3).Contains(row.Id)))
            {
                Assert.Equal("open", retained.Status);
                Assert.Null(retained.ResolutionNote);
            }

            // Other reason classes are never acknowledged by this lane.
            foreach (var other in rows.Where(row => seeded.OtherReasonIds.Contains(row.Id)))
            {
                Assert.Equal("open", other.Status);
                Assert.Null(other.ResolutionNote);
            }

            // A pre-existing dismissal keeps its own recorded gap.
            var preDismissed = rows.Single(row => row.Id == seeded.PreDismissedId);
            Assert.Equal("dismissed", preDismissed.Status);
            Assert.Equal("dismissed before the bulk lane existed", preDismissed.ResolutionNote);

            // The run-ledger audit trail: exactly one row, naming the class, the bound, the
            // resolved/retained counts, the operator's note and the acknowledged ids.
            var runs = await db.IngestRuns
                .Where(run => run.JobCode == QuarantineBacklogResolver.RunJobCode)
                .ToListAsync();
            var run = Assert.Single(runs);
            Assert.Equal(run.Id, root.GetProperty("runId").GetInt64());
            Assert.Equal("succeeded", run.Status);
            Assert.NotNull(run.StartedAt);
            Assert.NotNull(run.FinishedAt);
            Assert.Null(run.Error);

            using var stats = JsonDocument.Parse(run.StatsJson!);
            var statsRoot = stats.RootElement;
            Assert.Equal(QuarantineReasonValues.Conflicting, statsRoot.GetProperty("reasonCode").GetString());
            Assert.Equal(3, statsRoot.GetProperty("batchSize").GetInt32());
            Assert.Equal(3, statsRoot.GetProperty("resolved").GetInt32());
            Assert.Equal(2, statsRoot.GetProperty("retained").GetInt32());
            Assert.Equal(
                "İş Yatırım re-fetch drift — revision churn acknowledged",
                statsRoot.GetProperty("note").GetString());
            Assert.Equal("admin", statsRoot.GetProperty("trigger").GetString());
            Assert.Equal(
                resolvedIds,
                statsRoot.GetProperty("resolvedIds").EnumerateArray().Select(id => id.GetInt64()).ToList());
        }

        // The stored facts are exactly as they were: the lane never reads, writes or promotes a
        // fact (the quaranine rows are the only objects it touches).
        await AssertFactsUnchangedAsync(seeded.FactSnapshot);
    }

    // ---------------------------------------------------------------------------------
    // TC-MDF-129 (b) — Repeated runs drain the backlog and report an honest zero
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task TC_MDF_129_b_Repeated_runs_drain_the_backlog_and_report_an_honest_zero()
    {
        var seeded = await ResetAsync();

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var builder = await SignedInClientAsync(factory, BuilderEmail);

        // Five conflicting rows seeded; the open-queue count is five conflicting + two others.
        Assert.Equal(7, await OpenQuarantineCountAsync(builder));

        var first = await ResolveAsync(builder, note: "drift batch 1", batchSize: 2);
        Assert.Equal(2, first.Resolved);
        Assert.Equal(3, first.Retained);
        Assert.Equal(5, await OpenQuarantineCountAsync(builder));

        var second = await ResolveAsync(builder, note: "drift batch 2", batchSize: 2);
        Assert.Equal(2, second.Resolved);
        Assert.Equal(1, second.Retained);
        Assert.Equal(3, await OpenQuarantineCountAsync(builder));

        var third = await ResolveAsync(builder, note: "drift batch 3", batchSize: 2);
        Assert.Equal(1, third.Resolved);
        Assert.Equal(0, third.Retained);
        // Only the two non-conflicting rows remain in the review queue.
        Assert.Equal(2, await OpenQuarantineCountAsync(builder));

        // A drained backlog is an honest zero — not an error, not an omission.
        var fourth = await ResolveAsync(builder, note: "drained check");
        Assert.Equal(0, fourth.Resolved);
        Assert.Equal(0, fourth.Retained);
        Assert.Empty(fourth.ResolvedIds);
        Assert.Equal(2, await OpenQuarantineCountAsync(builder));

        // Every run is its own ledger row, in order, each naming its own batch.
        using (var ledger = await GetAsync(builder, $"{RunsPath}?job={QuarantineBacklogResolver.RunJobCode}"))
        {
            Assert.Equal(HttpStatusCode.OK, ledger.StatusCode);
            using var document = JsonDocument.Parse(await ledger.Content.ReadAsStringAsync());
            var runs = document.RootElement.GetProperty("runs").EnumerateArray().ToList();
            Assert.Equal(4, runs.Count);
            Assert.All(runs, run => Assert.Equal("succeeded", run.GetProperty("status").GetString()));
        }

        await using (var db = _postgres.CreateContext())
        {
            // All five conflicting rows acknowledged, nothing deleted, the other classes untouched.
            Assert.Equal(seeded.TotalRows, await db.QuarantinedFacts.CountAsync());
            Assert.Equal(
                0,
                await db.QuarantinedFacts.CountAsync(fact =>
                    fact.Status == "open" && fact.ReasonCode == QuarantineReasonValues.Conflicting));
            Assert.Equal(
                seeded.OtherReasonIds.Count,
                await db.QuarantinedFacts.CountAsync(fact => fact.Status == "open"));

            var runs = await db.IngestRuns
                .Where(run => run.JobCode == QuarantineBacklogResolver.RunJobCode)
                .OrderBy(run => run.Id)
                .ToListAsync();
            Assert.Equal(4, runs.Count);
            Assert.Equal(2, RunResolved(runs[0]));
            Assert.Equal(2, RunResolved(runs[1]));
            Assert.Equal(1, RunResolved(runs[2]));
            Assert.Equal(0, RunResolved(runs[3]));
        }

        await AssertFactsUnchangedAsync(seeded.FactSnapshot);
    }

    // ---------------------------------------------------------------------------------
    // TC-MDF-129 (c) — The note is required and the bound is explicit, with no side effects
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task TC_MDF_129_c_The_note_is_required_and_the_batch_bound_is_explicit()
    {
        await ResetAsync();

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var builder = await SignedInClientAsync(factory, BuilderEmail);

        // --- a missing / empty note is an inline validation failure (the dismissal rule).
        foreach (var body in new object[] { new { batchSize = 5 }, new { note = "   ", batchSize = 5 } })
        {
            var csrf = await CsrfTokenAsync(builder);
            using var rejected = await PostAsync(builder, ResolvePath, body, csrf);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            using var document = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
            Assert.Equal("VALIDATION_FAILED", document.RootElement.GetProperty("code").GetString());
            var fields = document.RootElement.GetProperty("params").GetProperty("fields").EnumerateArray().ToList();
            Assert.Contains(fields, field => field.GetProperty("field").GetString() == "note");
        }

        // --- a bound outside the cap is rejected, never silently clamped.
        foreach (var batchSize in new[] { 0, -1, QuarantineBacklogResolver.MaxBatchSize + 1 })
        {
            var csrf = await CsrfTokenAsync(builder);
            using var rejected = await PostAsync(
                builder,
                ResolvePath,
                new { note = "out of range", batchSize },
                csrf);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            using var document = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
            Assert.Equal("VALIDATION_FAILED", document.RootElement.GetProperty("code").GetString());
            var fields = document.RootElement.GetProperty("params").GetProperty("fields").EnumerateArray().ToList();
            Assert.Contains(fields, field => field.GetProperty("field").GetString() == "batchSize");
        }

        await using (var db = _postgres.CreateContext())
        {
            // Nothing changed: every open row is still open and un-noted (only the pre-seeded
            // dismissal is dismissed), and no ledger row was written for a rejected request.
            Assert.Equal(0, await db.QuarantinedFacts.CountAsync(fact => fact.Status != "open"
                && fact.ResolutionNote != "dismissed before the bulk lane existed"));
            Assert.All(
                await db.QuarantinedFacts.Where(fact => fact.Status == "open").ToListAsync(),
                fact => Assert.Null(fact.ResolutionNote));
            Assert.Equal(0, await db.IngestRuns.CountAsync());
        }
    }

    // ---------------------------------------------------------------------------------
    // TC-MDF-129 (d) — Only the class is acknowledged; payloads and facts are preserved
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task TC_MDF_129_d_Only_the_conflicting_class_is_acknowledged_and_nothing_is_promoted()
    {
        var seeded = await ResetAsync();

        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var builder = await SignedInClientAsync(factory, BuilderEmail);

        // A generous bound: the whole conflicting backlog goes in one run, and still only that class.
        var result = await ResolveAsync(builder, note: "whole backlog acknowledged", batchSize: QuarantineBacklogResolver.MaxBatchSize);
        Assert.Equal(seeded.ConflictingIds.Count, result.Resolved);
        Assert.Equal(0, result.Retained);

        await using (var db = _postgres.CreateContext())
        {
            // Every other reason class is still open and un-noted.
            var others = await db.QuarantinedFacts
                .Where(fact => seeded.OtherReasonIds.Contains(fact.Id))
                .ToListAsync();
            Assert.Equal(seeded.OtherReasonIds.Count, others.Count);
            Assert.All(others, fact => Assert.Equal("open", fact.Status));
            Assert.All(others, fact => Assert.Null(fact.ResolutionNote));
        }

        // The acknowledged payloads stay inspectable through the dismissed queue, both conflicting
        // values included — acknowledgement is a note, never a rewrite of the evidence.
        using (var dismissed = await GetAsync(builder, $"{QueuePath}?status=dismissed"))
        {
            Assert.Equal(HttpStatusCode.OK, dismissed.StatusCode);
            using var document = JsonDocument.Parse(await dismissed.Content.ReadAsStringAsync());
            var items = document.RootElement.GetProperty("items").EnumerateArray()
                .Where(item => item.GetProperty("reasonCode").GetString() == QuarantineReasonValues.Conflicting)
                .ToList();

            // The seeded pre-existing dismissal plus every row this run acknowledged.
            Assert.Equal(seeded.ConflictingIds.Count + 1, items.Count);
            Assert.All(items, item =>
                Assert.NotEqual(JsonValueKind.Null, item.GetProperty("payloadJson").ValueKind));
            var acknowledged = items
                .Where(item => item.GetProperty("resolutionNote").GetString() == "whole backlog acknowledged")
                .ToList();
            Assert.Equal(seeded.ConflictingIds.Count, acknowledged.Count);
            // Both conflicting values survive the acknowledgement, verbatim and inspectable.
            Assert.All(acknowledged, item =>
            {
                Assert.Equal(20.0m, item.GetProperty("payloadJson").GetProperty("storedClose").GetDecimal());
                Assert.Equal(20.5m, item.GetProperty("payloadJson").GetProperty("close").GetDecimal());
            });
        }

        await AssertFactsUnchangedAsync(seeded.FactSnapshot);
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    private sealed record SeedState(
        IReadOnlyList<long> ConflictingIds,
        IReadOnlyList<long> OtherReasonIds,
        long PreDismissedId,
        int TotalRows,
        FactSnapshot FactSnapshot);

    /// <summary>The stored facts this lane must never touch, captured before a run.</summary>
    private sealed record FactSnapshot(
        int DailyPrices,
        decimal AlfaClose,
        int Statements,
        int LineItems);

    private sealed record ResolveResult(int Resolved, int Retained, IReadOnlyList<long> ResolvedIds);

    /// <summary>Resets the shared container: re-applies the fixture universe and clears the
    /// mutable tables each test owns.</summary>
    private async Task<SeedState> ResetAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, L2);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM quarantined_facts");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ingest_runs");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM coverage_metadata");

        FactSnapshot snapshot;
        var alfaClose = await db.DailyPrices
            .Where(price => price.InstrumentId == db.Instruments
                .Where(instrument => instrument.Symbol == "ALFA")
                .Select(instrument => instrument.Id)
                .First())
            .OrderBy(price => price.PriceDate)
            .Select(price => (decimal?)price.CloseRaw)
            .FirstAsync();
        snapshot = new FactSnapshot(
            await db.DailyPrices.CountAsync(),
            alfaClose!.Value,
            await db.FinancialStatements.CountAsync(),
            await db.FinLineItems.CountAsync());

        // Five conflicting rows (oldest first — the bulk lane's selection order), two rows of
        // other reason classes, one pre-existing dismissal.
        var conflicting = new List<QuarantinedFact>();
        for (var index = 0; index < 5; index++)
        {
            conflicting.Add(new QuarantinedFact
            {
                JobCode = "prices",
                SourceRef = $"isbank://eod/ALFA?revision={index}",
                // Both values retained: the stored fact and the re-fetched drift.
                PayloadJson = $"{{\"symbol\":\"ALFA\",\"date\":\"{T:yyyy-MM-dd}\",\"close\":20.5,\"storedClose\":20.0}}",
                ReasonCode = QuarantineReasonValues.Conflicting,
                QuarantinedAt = QuarantinedAt.AddMinutes(index),
                Status = "open",
            });
        }

        var others = new List<QuarantinedFact>
        {
            new()
            {
                JobCode = "prices",
                SourceRef = "isbank://eod/BETA",
                PayloadJson = "{\"symbol\":\"BETA\",\"close\":-7}",
                ReasonCode = QuarantineReasonValues.NegativePrice,
                QuarantinedAt = QuarantinedAt.AddMinutes(-10),
                Status = "open",
            },
            new()
            {
                JobCode = "statements",
                SourceRef = "kap://statements/PART",
                PayloadJson = "{\"symbol\":\"PART\",\"netIncome\":\"N/A\"}",
                ReasonCode = QuarantineReasonValues.SchemaMismatch,
                QuarantinedAt = QuarantinedAt.AddMinutes(-5),
                Status = "open",
            },
        };

        var preDismissed = new QuarantinedFact
        {
            JobCode = "prices",
            SourceRef = "isbank://eod/GAMA",
            PayloadJson = "{\"symbol\":\"GAMA\",\"close\":1.5,\"storedClose\":1.4}",
            ReasonCode = QuarantineReasonValues.Conflicting,
            QuarantinedAt = QuarantinedAt.AddDays(-1),
            Status = "dismissed",
            ResolutionNote = "dismissed before the bulk lane existed",
        };

        db.QuarantinedFacts.AddRange(conflicting);
        db.QuarantinedFacts.AddRange(others);
        db.QuarantinedFacts.Add(preDismissed);
        await db.SaveChangesAsync();

        return new SeedState(
            conflicting.Select(fact => fact.Id).Order().ToList(),
            others.Select(fact => fact.Id).ToList(),
            preDismissed.Id,
            conflicting.Count + others.Count + 1,
            snapshot);
    }

    private async Task AssertFactsUnchangedAsync(FactSnapshot before)
    {
        await using var db = _postgres.CreateContext();

        var alfaClose = await db.DailyPrices
            .Where(price => price.InstrumentId == db.Instruments
                .Where(instrument => instrument.Symbol == "ALFA")
                .Select(instrument => instrument.Id)
                .First())
            .OrderBy(price => price.PriceDate)
            .Select(price => (decimal?)price.CloseRaw)
            .FirstAsync();

        Assert.Equal(before.DailyPrices, await db.DailyPrices.CountAsync());
        Assert.Equal(before.AlfaClose, alfaClose);
        Assert.Equal(before.Statements, await db.FinancialStatements.CountAsync());
        Assert.Equal(before.LineItems, await db.FinLineItems.CountAsync());
    }

    private async Task<ResolveResult> ResolveAsync(HttpClient builder, string note, int? batchSize = null)
    {
        var csrf = await CsrfTokenAsync(builder);
        using var response = await PostAsync(
            builder,
            ResolvePath,
            batchSize is null ? new { note } : new { note, batchSize },
            csrf);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        return new ResolveResult(
            root.GetProperty("resolved").GetInt32(),
            root.GetProperty("retained").GetInt32(),
            root.GetProperty("resolvedIds").EnumerateArray().Select(id => id.GetInt64()).ToList());
    }

    private static async Task<int> OpenQuarantineCountAsync(HttpClient builder)
    {
        using var response = await GetAsync(builder, SummaryPath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("openQuarantineCount").GetInt32();
    }

    private static int RunResolved(IngestRun run)
    {
        using var stats = JsonDocument.Parse(run.StatsJson!);
        return stats.RootElement.GetProperty("resolved").GetInt32();
    }

    /// <summary>The reason-code literals this lane pins (the catalog's own strings).</summary>
    private static class QuarantineReasonValues
    {
        public const string Conflicting = QuarantineReason.ConflictingValue;
        public const string NegativePrice = QuarantineReason.NegativePrice;
        public const string SchemaMismatch = QuarantineReason.SchemaMismatch;
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    private static string NextClientIp() => Guid.NewGuid().ToString("N");

    /// <summary>Establishes a real session through the Identity sign-in pipeline (the
    /// path <c>POST /auth/login</c> uses) and returns a client carrying its cookie.</summary>
    private static async Task<HttpClient> SignedInClientAsync(WebApplicationFactory<Program> factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var signInManager = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();

        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);

        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        accessor.HttpContext = httpContext;

        var result = await signInManager.PasswordSignInAsync(user!, FixturePassword, isPersistent: false, lockoutOnFailure: true);
        Assert.True(result.Succeeded, $"expected a sign-in for {email} to succeed");

        var setCookie = httpContext.Response.Headers.SetCookie.ToString();
        var cookiePair = setCookie.Split(';', 2)[0];
        Assert.StartsWith("degerli.auth=", cookiePair, StringComparison.Ordinal);

        var client = CreateClient(factory);
        client.DefaultRequestHeaders.Add("Cookie", cookiePair);
        return client;
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Forwarded-For", NextClientIp());
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, object? body, string? csrf)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("X-Forwarded-For", NextClientIp());
        if (csrf is not null)
        {
            request.Headers.Add("X-CSRF-Token", csrf);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request);
    }

    private static async Task<string> CsrfTokenAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/csrf-token");
        request.Headers.Add("X-Forwarded-For", NextClientIp());
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("token").GetString()!;
    }
}
