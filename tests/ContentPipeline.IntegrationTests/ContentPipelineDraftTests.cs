using System.Text.Json;
using Degerli.ContentPipeline;
using Degerli.Fixtures;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Degerli.ContentPipeline.IntegrationTests;

/// <summary>
/// TC-RES-016 / TC-RES-017 (stock-research test plan §2 Group C): the C4 drafting CLI
/// produces bilingual draft rows with KAP provenance, and those drafts are never
/// publicly served. The real drafting logic, status transition and source-ref recording
/// run against real PostgreSQL; the evren AI API is doubled at the HTTP boundary.
/// </summary>
public sealed class ContentPipelineDraftTests : IClassFixture<PostgresFixture>, IClassFixture<EvrenWireMockFixture>
{
    private const string DraftPath = "/v1/drafts";

    private readonly PostgresFixture _postgres;
    private readonly EvrenWireMockFixture _evren;

    public ContentPipelineDraftTests(PostgresFixture postgres, EvrenWireMockFixture evren)
    {
        _postgres = postgres;
        _evren = evren;
    }

    private static FixtureSet BaseSet => FixtureUniverse.Build(FixtureAnchor.L2);

    // TC-RES-016 — C4 drafting produces bilingual drafts with provenance.
    [Fact]
    public async Task TC_RES_016_draft_command_writes_bilingual_draft_with_source_refs()
    {
        await SeedDatabaseAsync();
        StubRecordedEvren();

        var (exitCode, output, error) = await RunDraftAsync("THETA");
        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrEmpty(error.ToString()), error.ToString());

        await using var db = _postgres.CreateContext();
        var thetaId = await db.Instruments.Where(i => i.Symbol == "THETA").Select(i => i.Id).SingleAsync();

        var draft = await db.BusinessDescriptions.SingleAsync(d => d.InstrumentId == thetaId);

        Assert.Equal("draft", draft.Status);
        Assert.Equal(1, draft.Version);
        Assert.False(string.IsNullOrWhiteSpace(draft.TextTr));
        Assert.False(string.IsNullOrWhiteSpace(draft.TextEn));
        Assert.Null(draft.PublishedAt);
        Assert.Null(draft.LastReviewedAt);

        using var refs = JsonDocument.Parse(draft.SourceRefsJson!);
        var recorded = refs.RootElement.EnumerateArray().Select(e => e.GetInt64()).OrderBy(id => id).ToArray();
        var thetaDisclosures = await db.KapDisclosures
            .Where(k => k.InstrumentId == thetaId)
            .Select(k => k.Id)
            .OrderBy(id => id)
            .ToArrayAsync();

        Assert.NotEmpty(thetaDisclosures);
        Assert.Equal(thetaDisclosures, recorded);
        Assert.Equal(DraftFixtureSet.ThetaDisclosureIds.OrderBy(id => id).ToArray(), recorded);

        // The draft is not published anywhere: no published version exists for THETA.
        Assert.False(await db.BusinessDescriptions.AnyAsync(d => d.InstrumentId == thetaId && d.Status == "published"));
    }

    // TC-RES-017 — Drafts are never publicly served.
    [Fact]
    public async Task TC_RES_017_drafts_are_never_publicly_served()
    {
        await SeedDatabaseAsync();
        StubRecordedEvren();

        // Re-draft ALFA (which has a published description) and REST (which has a draft in
        // storage, FU §9b). Both runs must only append drafts.
        Assert.Equal(0, (await RunDraftAsync("ALFA")).ExitCode);
        Assert.Equal(0, (await RunDraftAsync("REST")).ExitCode);

        await using var db = _postgres.CreateContext();
        var alfaId = await db.Instruments.Where(i => i.Symbol == "ALFA").Select(i => i.Id).SingleAsync();
        var restId = await db.Instruments.Where(i => i.Symbol == "REST").Select(i => i.Id).SingleAsync();

        // ALFA's published version is untouched by the fresh draft (lineage continues at v3).
        var publicAlfa = await SelectPublicAsync(db, alfaId);
        Assert.NotNull(publicAlfa);
        Assert.Equal(2, publicAlfa!.Version);
        Assert.Equal("published", publicAlfa.Status);
        Assert.NotNull(publicAlfa.PublishedAt);
        Assert.True(await db.BusinessDescriptions.AnyAsync(d => d.InstrumentId == alfaId && d.Status == "draft" && d.Version == 3));

        // REST keeps only drafts in storage — no published row — so the public serving rule
        // yields nothing (the SPA renders the FR-RES-026 "preparing" state).
        var restRows = await db.BusinessDescriptions.Where(d => d.InstrumentId == restId).ToListAsync();
        Assert.NotEmpty(restRows);
        Assert.All(restRows, row => Assert.Equal("draft", row.Status));
        Assert.Null(await SelectPublicAsync(db, restId));
    }

    private async Task SeedDatabaseAsync()
    {
        await using var db = _postgres.CreateContext();
        await FixtureSeeder.ApplyAsync(db, DraftFixtureSet.ForDatabase(BaseSet));
    }

    private void StubRecordedEvren()
    {
        var payload = CannedSourceCatalog.Get(DraftFixtureSet.ForEvren(BaseSet), CannedSourceCatalog.EvrenDraftOk);
        _evren.StubDraft(DraftPath, payload.Body);
    }

    private async Task<(int ExitCode, string Output, string Error)> RunDraftAsync(string symbol)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await ContentPipelineCli.RunAsync(
            ["draft", $"--symbol={symbol}"],
            new ContentPipelineSettings
            {
                ConnectionString = _postgres.ConnectionString,
                EvrenBaseUrl = _evren.BaseUrl,
                EvrenDraftPath = DraftPath,
            },
            output,
            error);

        return (exitCode, output.ToString(), error.ToString());
    }

    /// <summary>
    /// The documented public serving rule (02 §3.4): the latest <c>published</c> version per
    /// instrument; none found → the FR-RES-026 <c>preparing</c> state. The HTTP overview
    /// endpoint that consumes this rule is TKT-res-003's surface; here the CLI's storage
    /// output is asserted against it to prove drafts never leak.
    /// </summary>
    private static Task<BusinessDescription?> SelectPublicAsync(DegerliDbContext db, long instrumentId) =>
        db.BusinessDescriptions
            .Where(d => d.InstrumentId == instrumentId && d.Status == "published")
            .OrderByDescending(d => d.Version)
            .FirstOrDefaultAsync();
}
