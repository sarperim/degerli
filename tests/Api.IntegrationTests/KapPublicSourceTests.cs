using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;
using Degerli.Ingestion;
using Degerli.Ingestion.KapPublic;
using Degerli.Ingestion.Scheduling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-foundation-079 acceptance — the KAP <b>public-site</b> source skeleton
/// (01 v1.5.53 §10.9.19, FR-MDF-002/008/012, NFR-MDF-001): the options contract, the
/// single-flight paced client, the honest-status classification and the walk budget.
/// <para>
/// <b>Doubles only.</b> Every body these tests serve comes from the committed paced-capture
/// set in <see cref="RealSourceCatalog"/> (<c>kap-public-*</c>, captured 2026-10-25) or from
/// the WireMock source double; <b>no test ever calls kap.org.tr</b>. Pacing is bound to 0
/// where it is not the subject, and observed on a fake clock where it is.
/// </para>
/// <para>
/// Two-source discipline is asserted, not assumed: the lane's evidence class is
/// <c>kap-public</c> (never the VYK lane's <c>kap</c>), and the public index space
/// (1643141) is never written where a VYK index (head 1231017) belongs.
/// </para>
/// </summary>
public sealed class KapPublicSourceTests : IClassFixture<WireMockFixture>
{
    private const string ByCriteriaPath = "/tr/api/disclosure/members/byCriteria";
    private const string ReportPath = "/tr/api/notification/export/excel/1643141";
    private const string IdentityPath = "/tr/api/search/combined";
    private const string ProbePath = "/tr/api/probe";

    /// <summary>The ASELS public-site company oid (its own identifier space — not VYK's 866).</summary>
    private const string AselsPublicOid = "4028e4a1413b7ef401413bc2251e0047";

    private readonly WireMockFixture _wireMock;

    public KapPublicSourceTests(WireMockFixture wireMock) => _wireMock = wireMock;

    // =================================================================================
    // TC-MDF-103 (i) — the configuration contract
    // =================================================================================

    /// <summary>
    /// TC-MDF-103 (i) — the public-site options section binds, the shipped constants are
    /// pinned as config-equivalence assertions, and the walk deadline sits strictly below the
    /// scheduler's per-step bound. The lane's full key set is pre-wired so later lane tickets
    /// (080–084) never re-touch <c>appsettings.json</c> for a shared concern: the routes, the
    /// pacing/caps/timeouts, the recorded access posture and the two <b>inert</b> source
    /// selectors (which still ship the incumbent <c>vyk</c>, so nothing flips silently).
    /// </summary>
    [Fact]
    public void TC_MDF_103_a_the_shipped_config_binds_the_full_public_site_key_set()
    {
        var appsettingsPath = Path.Combine(FindRepositoryRoot(), "src", "Api", "appsettings.json");
        Assert.True(File.Exists(appsettingsPath), $"appsettings.json missing at {appsettingsPath}");
        using var document = JsonDocument.Parse(File.ReadAllText(appsettingsPath));
        var ingestion = document.RootElement.GetProperty("Ingestion");
        var kapPublic = ingestion.GetProperty("KapPublic");

        // (i-a) the base, the honest UA, the 3 s politeness pace.
        Assert.Equal("https://www.kap.org.tr", kapPublic.GetProperty("BaseUrl").GetString());
        Assert.Equal(KapPublicSourceOptions.DefaultBaseUrl, new KapPublicSourceOptions().BaseUrl);
        var userAgent = kapPublic.GetProperty("UserAgent").GetString() ?? string.Empty;
        Assert.Equal(KapPublicSourceOptions.DefaultUserAgent, userAgent);
        Assert.Contains("Mozilla/5.0", userAgent, StringComparison.Ordinal);
        // TC-MDF-103 (i) — a realistic User-Agent pinned as a config-equivalence assertion
        // (this is the TC_MDF_103_a case's UA half). The WAF-blocked spelling (087 live
        // finding): the honest token WITHOUT a trailing parenthetical comment is the
        // live-verified passing string, so this pin keeps the blocked spelling (HTTP 666 on
        // every route) from silently returning. (The UA's browser tokens legitimately
        // contain "(" — the blocked marker is the trailing "DegerliBot/0.1 (…)" form, so it
        // is pinned token-and-comment, not punctuation.)
        Assert.Contains("Degerli/0.1", userAgent, StringComparison.Ordinal);
        Assert.DoesNotContain("DegerliBot", userAgent, StringComparison.Ordinal);
        Assert.DoesNotContain("public-site reader", userAgent, StringComparison.Ordinal);
        Assert.Equal(3000, AsInt(kapPublic, "RequestDelayMs"));
        Assert.Equal(KapPublicSourceOptions.DefaultRequestDelayMs, new KapPublicSourceOptions().RequestDelayMs);

        // (i-b) the live-verified routes (never the VYK gateway's).
        Assert.Equal(ByCriteriaPath, kapPublic.GetProperty("ByCriteriaPath").GetString());
        Assert.Equal(
            "/tr/api/notification/export/excel/{disclosureIndex}",
            kapPublic.GetProperty("ReportExportPath").GetString());
        Assert.Equal(IdentityPath, kapPublic.GetProperty("IdentitySearchPath").GetString());
        Assert.Equal("/tr/sirket/{ticker}", kapPublic.GetProperty("CompanyPagePath").GetString());
        Assert.Equal("/robots.txt", kapPublic.GetProperty("RobotsPath").GetString());

        // (i-c) the bounds: caps, per-item detail timeout, walk deadline.
        var shipped = new KapPublicSourceOptions
        {
            MaxListPages = AsInt(kapPublic, "MaxListPages"),
            MaxDetailFetches = AsInt(kapPublic, "MaxDetailFetches"),
            DetailFetchTimeoutMs = AsInt(kapPublic, "DetailFetchTimeoutMs"),
            RequestTimeoutMs = AsInt(kapPublic, "RequestTimeoutMs"),
            MaxWalkDuration = TimeSpan.Parse(kapPublic.GetProperty("MaxWalkDuration").GetString()!),
            AutomatedAccess = Enum.Parse<KapPublicAccessPosture>(
                kapPublic.GetProperty("AutomatedAccess").GetString()!, ignoreCase: true),
        };

        Assert.Equal(KapPublicSourceOptions.DefaultMaxListPages, shipped.MaxListPages);
        Assert.Equal(KapPublicSourceOptions.DefaultMaxDetailFetches, shipped.MaxDetailFetches);
        Assert.Equal(KapPublicSourceOptions.DefaultDetailFetchTimeoutMs, shipped.DetailFetchTimeoutMs);
        Assert.Equal(KapPublicSourceOptions.DefaultRequestTimeoutMs, shipped.RequestTimeoutMs);
        Assert.Equal(KapPublicSourceOptions.DefaultMaxWalkDuration, shipped.MaxWalkDuration);

        // The deadline must sit strictly inside the scheduler's per-step bound, or a walk can
        // be watchdog-cancelled with every completed unit discarded (the 078/§10.9.17 class).
        Assert.True(
            shipped.MaxWalkDuration < IngestionSchedulerOptions.DefaultMaxStepDuration,
            $"MaxWalkDuration ({shipped.MaxWalkDuration}) must be < MaxStepDuration " +
            $"({IngestionSchedulerOptions.DefaultMaxStepDuration}).");

        // (i-d) the recorded access posture (the OQ-2 gate) is an explicit shipped value, not a
        // comment — and it is *configured*, not inherited from the code default.
        var shippedPosture = Enum.Parse<KapPublicAccessPosture>(
            kapPublic.GetProperty("AutomatedAccess").GetString()!, ignoreCase: true);
        Assert.Equal(KapPublicAccessPosture.Allowed, shippedPosture);
        Assert.Equal(KapPublicAccessPosture.Allowed, shipped.AutomatedAccess);
        Assert.True(shipped.AllowsAutomatedAccess);

        // A posture nobody chose is not a permission: the code default is NotVerified and it
        // does *not* allow reads, so the shipped Allowed above can only come from config.
        var unconfigured = new KapPublicSourceOptions();
        Assert.Equal(KapPublicAccessPosture.NotVerified, unconfigured.AutomatedAccess);
        Assert.False(unconfigured.AllowsAutomatedAccess);

        // (i-e) the two source selectors are pre-wired inertly: both ship the incumbent VYK
        // source, so selecting the public site stays an explicit, recorded config flip
        // (TKT-foundation-082/084) and nothing changes behaviour by this ticket.
        Assert.Equal("vyk", ingestion.GetProperty("Statements").GetProperty("Source").GetString());
        Assert.Equal("vyk", ingestion.GetProperty("Disclosures").GetProperty("Source").GetString());

        // (i-f) the lane's evidence class is its own — never the VYK lane's `kap`.
        Assert.Equal("kap-public", KapPublicSourceOptions.SourceName);
        Assert.Equal("kap-public", KapPublicSourceReader.SourceName);
        Assert.NotEqual("kap", KapPublicSourceOptions.SourceName);
    }

    /// <summary>
    /// TC-MDF-103 (i) — the composition root wires the public-site source as its <b>own</b>
    /// source: the keyed client, the per-run pacer and the reader resolve, the pacer carries
    /// the configured pace, the client reports the <c>kap-public</c> evidence class, and a
    /// read through the DI graph reaches the double at the public-site base — while the VYK
    /// client is untouched and still registered.
    /// </summary>
    [Fact]
    public async Task TC_MDF_103_a2_the_public_site_source_is_its_own_di_registration()
    {
        ResetDoubles();
        _wireMock.StubJson(ByCriteriaPath, RealSourceCatalog.Get(RealSourceCatalog.KapPublicByCriteria).Body, method: "POST");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Host=localhost;Database=composition-only",
                ["Ingestion:KapPublic:BaseUrl"] = _wireMock.BaseUrl,
                ["Ingestion:KapPublic:RequestDelayMs"] = "0",
                // The posture is configured, never inherited: the harness selects the shipped
                // value explicitly (the code default, NotVerified, refuses every request).
                ["Ingestion:KapPublic:AutomatedAccess"] = "Allowed",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMarketDataIngestion(configuration);
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService<IKapPublicSourceClient>();
        var reader = scope.ServiceProvider.GetRequiredService<KapPublicSourceReader>();
        var pacer = scope.ServiceProvider.GetRequiredService<KapPublicRequestPacer>();

        Assert.Equal("kap-public", client.SourceName);
        Assert.Equal("kap-public", reader.SourceNameOfLane);
        Assert.Equal(0, pacer.DelayMs);
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<Degerli.Ingestion.Kap.IKapSourceClient>());

        var result = await reader.ReadDisclosureListAsync(AselsFrQuery());
        Assert.True(result.IsOk, $"expected an ok read, got {result.Outcome} ({result.ReasonCode})");
        Assert.Equal(10, result.Payload!.Count);
        Assert.Equal(1, _wireMock.RequestsFor(ByCriteriaPath));
    }

    // =================================================================================
    // TC-MDF-103 (ii) — pacing
    // =================================================================================

    /// <summary>
    /// TC-MDF-103 (ii) — on a fake clock, <b>every</b> consecutive request pair is separated
    /// by at least the configured delay: the first request waits nothing, the second waits the
    /// full 3 s, and a request issued 1 s into the gap waits only the 2 s remainder. The pace
    /// is configuration, not a literal.
    /// </summary>
    [Fact]
    public async Task TC_MDF_103_b_every_consecutive_request_waits_out_the_configured_delay()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero));
        var waits = new List<TimeSpan>();
        KapPublicDelay recorder = (delay, _) =>
        {
            waits.Add(delay);
            return Task.CompletedTask;
        };

        var handler = new RecordingHandler(_ => JsonResponse(RealSourceCatalog.Get(
            RealSourceCatalog.KapPublicByCriteria).Body));
        var client = BuildClient(handler, clock, delay: recorder, delayMs: 3000);

        await client.GetStringAsync(ProbePath);
        await client.GetStringAsync(ProbePath);
        clock.Advance(TimeSpan.FromSeconds(1));
        await client.GetStringAsync(ProbePath);

        Assert.Equal(2, waits.Count);
        Assert.Equal(TimeSpan.FromSeconds(3), waits[0]);
        Assert.Equal(TimeSpan.FromSeconds(2), waits[1]);
        Assert.Equal(3, handler.Requests.Count);
    }

    /// <summary>
    /// TC-MDF-103 (ii) — the reader's own list→detail pair is what §20 (ii) names: a
    /// <c>ReadDisclosureListAsync</c> followed by a <c>ReadReportAsync</c> on a fake clock
    /// records exactly one wait, the full configured 3 s; at <c>delayMs: 0</c> the same pair
    /// records <b>no</b> wait at all (the unpaced half is asserted, not assumed).
    /// </summary>
    [Fact]
    public async Task TC_MDF_103_b2_reader_level_list_then_detail_is_paced_and_a_zero_bound_waits_none()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero));
        var waits = new List<TimeSpan>();
        KapPublicDelay recorder = (delay, _) =>
        {
            waits.Add(delay);
            return Task.CompletedTask;
        };

        var listBody = RealSourceCatalog.Get(RealSourceCatalog.KapPublicByCriteria).Body;
        var reportBody = RealSourceCatalog.Get(RealSourceCatalog.KapPublicReport).Body;
        var handler = new RecordingHandler(request =>
            request.RequestUri!.AbsolutePath == ByCriteriaPath
                ? JsonResponse(listBody)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(reportBody, Encoding.UTF8, "text/html"),
                });

        var pacedOptions = OptionsOf(o => o.RequestDelayMs = 3000);
        var paced = new KapPublicSourceReader(
            new KapPublicHttpSourceClient(
                BuildHttp(handler), pacedOptions, new KapPublicRequestPacer(pacedOptions, clock, recorder), clock),
            pacedOptions);

        var list = await paced.ReadDisclosureListAsync(AselsFrQuery());
        var report = await paced.ReadReportAsync(1643141);

        Assert.True(list.IsOk, $"expected the list read to succeed ({list.ReasonCode})");
        Assert.True(report.IsOk, $"expected the report read to succeed ({report.ReasonCode})");
        Assert.Equal(2, handler.Requests.Count);
        Assert.Single(waits);
        Assert.Equal(TimeSpan.FromSeconds(3), waits[0]);

        // The "0 bound produces none" half is real: no wait is requested at all.
        var unpacedWaits = new List<TimeSpan>();
        KapPublicDelay unpacedRecorder = (delay, _) =>
        {
            unpacedWaits.Add(delay);
            return Task.CompletedTask;
        };
        var unpacedHandler = new RecordingHandler(request =>
            request.RequestUri!.AbsolutePath == ByCriteriaPath
                ? JsonResponse(listBody)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(reportBody, Encoding.UTF8, "text/html"),
                });
        var unpacedOptions = OptionsOf(o => o.RequestDelayMs = 0);
        var unpaced = new KapPublicSourceReader(
            new KapPublicHttpSourceClient(
                BuildHttp(unpacedHandler), unpacedOptions,
                new KapPublicRequestPacer(unpacedOptions, clock, unpacedRecorder), clock),
            unpacedOptions);

        Assert.True((await unpaced.ReadDisclosureListAsync(AselsFrQuery())).IsOk);
        Assert.True((await unpaced.ReadReportAsync(1643141)).IsOk);
        Assert.Equal(2, unpacedHandler.Requests.Count);
        Assert.Empty(unpacedWaits);
    }

    /// <summary>
    /// TC-MDF-103 (ii) — the delay is configuration: a <b>200 ms</b> bound produces ~200 ms
    /// gaps on the real wire, and a <c>0</c> bound produces none. (The fixture is measured by
    /// the double's own request stamps — a real-clock politeness measure, no live site.)
    /// </summary>
    [Fact]
    public async Task TC_MDF_103_c_the_pace_is_configuration_a_200ms_bound_produces_200ms_gaps()
    {
        ResetDoubles();
        _wireMock.StubJson(ProbePath, """[]""");

        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json"),
        });

        // Unpaced (0 ms): the three requests land back-to-back.
        var unpaced = BuildClient(handler, TimeProvider.System, delayMs: 0);
        await unpaced.GetStringAsync(ProbePath);
        await unpaced.GetStringAsync(ProbePath);
        await unpaced.GetStringAsync(ProbePath);

        // Paced at 200 ms: consecutive starts are at least ~200 ms apart.
        var stamped = new List<DateTimeOffset>();
        var stopwatch = new List<long>();
        var pacedHandler = new RecordingHandler(_ =>
        {
            stamped.Add(DateTimeOffset.UtcNow);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            };
        });
        var paced = BuildClient(pacedHandler, TimeProvider.System, delayMs: 200);
        var total = Stopwatch.StartNew();
        await paced.GetStringAsync(ProbePath);
        await paced.GetStringAsync(ProbePath);
        await paced.GetStringAsync(ProbePath);
        total.Stop();

        Assert.Equal(3, stamped.Count);
        foreach (var pair in stamped.Zip(stamped.Skip(1)))
        {
            var gap = pair.Second - pair.First;
            Assert.True(
                gap >= TimeSpan.FromMilliseconds(150),
                $"a 200 ms pace must separate the requests; observed {gap.TotalMilliseconds:0} ms.");
        }

        Assert.True(
            total.Elapsed >= TimeSpan.FromMilliseconds(300),
            $"two 200 ms gaps must cost ≥ 300 ms of wall clock; observed {total.ElapsedMilliseconds} ms.");
    }

    // =================================================================================
    // TC-MDF-103 (iii) — request shape
    // =================================================================================

    /// <summary>
    /// TC-MDF-103 (iii) — the client sends the realistic User-Agent on every request (the
    /// double only answers a request carrying it) and the fixture-faithful byCriteria POST
    /// body: <c>fromDate</c>/<c>toDate</c>/<c>disclosureClass</c>/<c>mkkMemberOidList</c> plus
    /// the endpoint's empty sibling arrays, in the live member order.
    /// </summary>
    [Fact]
    public async Task TC_MDF_103_d_the_client_sends_the_user_agent_and_the_bycriteria_body()
    {
        ResetDoubles();
        var catalog = RealSourceCatalog.Get(RealSourceCatalog.KapPublicByCriteria);

        // TC-MDF-103 (iii) — request shape: the client sends the realistic User-Agent header
        // (this is the TC_MDF_103_d case's UA half). The stub matches ONLY on the honest UA
        // token — a request without it would 404 and the read below would be a source-down
        // (i.e. this is a real on-the-wire assertion).
        _wireMock.Server
            .Given(Request.Create().WithPath(ByCriteriaPath).UsingPost()
                .WithHeader("User-Agent", "*Degerli/0.1*"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(catalog.Body));

        var query = AselsFrQuery();
        var wireReader = new KapPublicSourceReader(BuildClient(null, TimeProvider.System), OptionsOf());
        var throughDouble = await wireReader.ReadDisclosureListAsync(query);
        Assert.True(throughDouble.IsOk, $"expected the UA-carrying request to be served ({throughDouble.ReasonCode})");
        Assert.Equal(10, throughDouble.Payload!.Count);
        Assert.Equal(1, _wireMock.RequestsFor(ByCriteriaPath));

        // The exact body, in the endpoint's live member order (recorded request).
        var handler = new RecordingHandler(_ => JsonResponse(catalog.Body));
        var reader = new KapPublicSourceReader(BuildClient(handler, TimeProvider.System), OptionsOf());
        Assert.True((await reader.ReadDisclosureListAsync(query)).IsOk);
        var body = handler.Requests.Last().Body;
        Assert.Equal(query.ToJson(), body);
        using var document = JsonDocument.Parse(body!);
        Assert.Equal("2026-01-01", document.RootElement.GetProperty("fromDate").GetString());
        Assert.Equal("2026-10-25", document.RootElement.GetProperty("toDate").GetString());
        Assert.Equal("FR", document.RootElement.GetProperty("disclosureClass").GetString());
        Assert.Equal(
            AselsPublicOid,
            document.RootElement.GetProperty("mkkMemberOidList")[0].GetString());
        Assert.Equal(0, document.RootElement.GetProperty("subjectList").GetArrayLength());
        Assert.Equal(0, document.RootElement.GetProperty("disclosureIndexList").GetArrayLength());

        // The request shape and the honest UA travel together on the recorded handler too
        // (TC-MDF-103 (iii): the realistic User-Agent header is actually sent).
        var recorded = handler.Requests.Last();
        Assert.Equal(HttpMethod.Post, recorded.Method);
        Assert.Contains("Degerli/0.1", recorded.UserAgent, StringComparison.Ordinal);
    }

    /// <summary>
    /// TC-MDF-103 (iii)+(iv) — the report is fetched by its <b>public-site</b>
    /// <c>disclosureIndex</c> on the configured route, and the body is classified <b>by
    /// shape</b>: the route declares <c>application/vnd.ms-excel</c> while serving HTML, and a
    /// type-trusting reader would quarantine every real report.
    /// </summary>
    [Fact]
    public async Task TC_MDF_103_e_the_report_is_fetched_by_index_and_classified_by_shape_not_type()
    {
        ResetDoubles();
        var capture = RealSourceCatalog.Get(RealSourceCatalog.KapPublicReport);

        // Served exactly as the live route does: the lying content type, the captured bytes.
        _wireMock.Server
            .Given(Request.Create().WithPath(ReportPath).UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/vnd.ms-excel")
                .WithBody(capture.RawBytes));

        var client = BuildClient(null, TimeProvider.System);
        var reader = new KapPublicSourceReader(client, OptionsOf());
        var result = await reader.ReadReportAsync(1643141);

        Assert.True(result.IsOk, $"expected the HTML report to classify by shape ({result.ReasonCode})");
        Assert.Equal(1643141, result.Payload!.DisclosureIndex);
        Assert.Contains("<table", result.Payload.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Nakit Akış Tablosu (Dolaylı Yöntem)", result.Payload.Html, StringComparison.Ordinal);

        // The route carries the public index — the lane's own space (never a VYK index).
        var requested = _wireMock.Server.LogEntries
            .Where(entry => entry.RequestMessage is not null)
            .Select(entry => entry.RequestMessage!.Path)
            .ToList();
        Assert.Contains(ReportPath, requested);
        Assert.DoesNotContain("1231017", requested);
    }

    // =================================================================================
    // TC-MDF-103 (iv) — the four honest statuses
    // =================================================================================

    /// <summary>
    /// TC-MDF-103 (iv) — a transport/5xx list failure is a <b>source failure</b>: the read is
    /// <c>source-down</c> with its named reason, no rows, and nothing quarantined (nothing is
    /// fabricated from a failure).
    /// </summary>
    [Fact]
    public async Task TC_MDF_103_f_a_list_5xx_is_a_source_down_with_no_rows_and_no_quarantine()
    {
        ResetDoubles();
        var body = """
                   <html><body><p>Yasadiginiz bu erisim problemini "349891-PPE13" ...</p></body></html>
                   """;
        _wireMock.Server
            .Given(Request.Create().WithPath(ByCriteriaPath).UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(503)
                .WithHeader("Content-Type", "text/html")
                .WithBody(body));

        var client = BuildClient(null, TimeProvider.System);
        var reader = new KapPublicSourceReader(client, OptionsOf());
        var result = await reader.ReadDisclosureListAsync(AselsFrQuery());

        Assert.True(result.IsSourceDown);
        Assert.Equal(KapPublicReasonCodes.SourceFetchFailed, result.ReasonCode);
        Assert.Null(result.Payload);
        Assert.False(result.IsQuarantined);
        Assert.Contains("503", result.Detail ?? string.Empty, StringComparison.Ordinal);
        // The failure excerpt is the source's own body (bounded), never a fabrication.
        Assert.Contains("erisim problemini", result.RawBody ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// TC-MDF-103 (iv) — a 200 body that fails the shape parse is <b>quarantined</b> as
    /// <c>UNPARSEABLE_PAYLOAD</c> with the <b>verbatim</b> body, whatever the declared content
    /// type says: here the site's HTML error page is served as <c>application/json</c> — the
    /// mirror image of the lying excel route, and the reason the classifier only ever looks at
    /// the shape.
    /// </summary>
    [Fact]
    public async Task TC_MDF_103_g_a_200_body_that_is_not_the_list_shape_quarantines_verbatim()
    {
        ResetDoubles();
        var errorPage = RealSourceCatalog.Get(RealSourceCatalog.KapPublicRobots).Body;
        _wireMock.Server
            .Given(Request.Create().WithPath(ByCriteriaPath).UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(errorPage));

        var client = BuildClient(null, TimeProvider.System);
        var reader = new KapPublicSourceReader(client, OptionsOf());
        var result = await reader.ReadDisclosureListAsync(AselsFrQuery());

        Assert.True(result.IsQuarantined);
        Assert.Equal(KapPublicReasonCodes.UnparseablePayload, result.ReasonCode);
        Assert.Equal(errorPage, result.RawBody);

        // A 200 JSON body whose rows are not the byCriteria shape is the same class — a list of
        // scalars, or rows missing the members the lane depends on, is never silently partial.
        var scalarBody = "[1,2,3]";
        Assert.True(KapPublicSourceReader.ClassifyDisclosureList(scalarBody).IsQuarantined);
        Assert.True(KapPublicSourceReader.ClassifyDisclosureList(
            """[{"disclosureIndex":0,"publishDate":null,"disclosureClass":null}]""").IsQuarantined);
        Assert.True(KapPublicSourceReader.ClassifyDisclosureList("   ").IsQuarantined);
    }

    /// <summary>
    /// TC-MDF-103 (iv) — the lane's own observed non-success status, exercised end to end:
    /// the report route answers the captured <b>HTTP 666</b> access-error page, and the reader
    /// classifies it as a <b>source failure</b> — never a fabricated payload or a parsed
    /// status. The verbatim (bounded) error excerpt rides on <c>RawBody</c>, so the failure is
    /// diagnosable without trusting the status code alone.
    /// </summary>
    [Fact]
    public async Task TC_MDF_103_f2_a_666_access_error_is_a_source_down_with_the_verbatim_excerpt()
    {
        ResetDoubles();
        var errorPage = RealSourceCatalog.Get(RealSourceCatalog.KapPublicRobots).Body;
        _wireMock.Server
            .Given(Request.Create().WithPath(ReportPath).UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(666)
                .WithHeader("Content-Type", "text/html")
                .WithBody(errorPage));

        var client = BuildClient(null, TimeProvider.System);
        var reader = new KapPublicSourceReader(client, OptionsOf());
        var result = await reader.ReadReportAsync(1643141);

        Assert.True(result.IsSourceDown);
        Assert.Equal(KapPublicReasonCodes.SourceFetchFailed, result.ReasonCode);
        Assert.Null(result.Payload);
        Assert.False(result.IsQuarantined);
        Assert.Contains("666", result.Detail ?? string.Empty, StringComparison.Ordinal);
        // The captured body is smaller than the excerpt cap, so it is carried verbatim.
        Assert.Equal(errorPage, result.RawBody);
        Assert.Contains("Yasadiginiz bu erisim problemini", result.RawBody ?? string.Empty, StringComparison.Ordinal);
        Assert.True(result.RawBody!.Length <= KapPublicSourceReader.MaxExcerptChars);
    }

    /// <summary>
    /// TC-MDF-103 (iv) — the excerpt bound is applied to <b>quarantine</b> bodies too, not just
    /// to failure records: a realistic ~1.8 MB 200 body that fails the shape check is
    /// quarantined as a bounded excerpt (<c>MaxExcerptChars</c>) and its detail records the
    /// original length, so the truncation is visible.
    /// </summary>
    [Fact]
    public async Task TC_MDF_103_g2_a_large_quarantine_body_is_bounded_and_its_original_length_recorded()
    {
        ResetDoubles();
        var oversized = "<html><body><p>" + new string('x', KapPublicSourceReader.MaxExcerptChars * 2) + "</p></body></html>";
        _wireMock.Server
            .Given(Request.Create().WithPath(ReportPath).UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/vnd.ms-excel")
                .WithBody(oversized));

        var client = BuildClient(null, TimeProvider.System);
        var reader = new KapPublicSourceReader(client, OptionsOf());
        var result = await reader.ReadReportAsync(1643141);

        Assert.True(result.IsQuarantined);
        Assert.Equal(KapPublicReasonCodes.UnparseablePayload, result.ReasonCode);
        Assert.Null(result.Payload);
        Assert.NotNull(result.RawBody);
        Assert.Equal(KapPublicSourceReader.MaxExcerptChars, result.RawBody!.Length);
        Assert.Equal(oversized[..KapPublicSourceReader.MaxExcerptChars], result.RawBody);
        Assert.Contains($"{oversized.Length} chars", result.Detail ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains(
            $"{KapPublicSourceReader.MaxExcerptChars}",
            result.Detail ?? string.Empty,
            StringComparison.Ordinal);

        // The static list classifier applies the same bound.
        var classified = KapPublicSourceReader.ClassifyDisclosureList(oversized);
        Assert.True(classified.IsQuarantined);
        Assert.Equal(KapPublicSourceReader.MaxExcerptChars, classified.RawBody!.Length);
        Assert.Contains($"{oversized.Length} chars", classified.Detail ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// TC-MDF-103 (iv) — a well-formed empty window is an <b>honest empty</b> (a named skip,
    /// never a failure and never a fabricated row): the reason code says so, the body is kept,
    /// and no payload is invented.
    /// </summary>
    [Fact]
    public async Task TC_MDF_103_h_an_empty_window_is_an_honest_empty_with_a_named_reason()
    {
        ResetDoubles();
        _wireMock.StubJson(ByCriteriaPath, "[]", method: "POST");

        var client = BuildClient(null, TimeProvider.System);
        var reader = new KapPublicSourceReader(client, OptionsOf());
        var result = await reader.ReadDisclosureListAsync(AselsFrQuery());

        Assert.True(result.IsHonestEmpty);
        Assert.Equal(KapPublicReasonCodes.HonestEmpty, result.ReasonCode);
        Assert.Null(result.Payload);
        Assert.Equal("[]", result.RawBody);
        Assert.False(result.IsSourceDown);
        Assert.False(result.IsQuarantined);
    }

    /// <summary>
    /// TC-MDF-103 (iv) — a stalled detail is its own <b>source-down</b> reason: the per-item
    /// detail timeout fires (the pace is not counted against it) and the read reports
    /// <c>SOURCE_REQUEST_TIMEOUT</c> instead of hanging the walk.
    /// </summary>
    [Fact]
    public async Task TC_MDF_103_k_a_stalled_detail_times_out_as_its_own_source_down_reason()
    {
        ResetDoubles();
        _wireMock.Server
            .Given(Request.Create().WithPath(ReportPath).UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/vnd.ms-excel")
                .WithBody("<html><table><tr><td>x</td></tr></table></html>")
                .WithDelay(TimeSpan.FromMilliseconds(1500)));

        var client = BuildClient(null, TimeProvider.System, detailTimeoutMs: 200);
        var reader = new KapPublicSourceReader(client, OptionsOf(o => o.DetailFetchTimeoutMs = 200));

        var result = await reader.ReadReportAsync(1643141);

        Assert.True(result.IsSourceDown);
        Assert.Equal(KapPublicReasonCodes.RequestTimeout, result.ReasonCode);
        Assert.Null(result.Payload);
    }

    // =================================================================================
    // TC-MDF-103 (v) — the walk bounds
    // =================================================================================

    /// <summary>
    /// TC-MDF-103 (v) — the walk budget bounds work and wall clock exactly as the 078
    /// <c>WalkBudget</c> does: the detail ceiling stops the walk after two completed units and
    /// the position persists for the resume, the page ceiling stops a page walk, and the
    /// deadline (a fake clock crossing it) truncates with its own reason — every stop is an
    /// honest bounded pass with a saved position, never a lost unit.
    /// </summary>
    [Fact]
    public void TC_MDF_103_i_the_walk_obeys_its_caps_and_deadline_and_resumes_from_its_position()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero));
        var units = new[] { "1643141", "1598316", "1561039" };

        // (v-a) the detail ceiling: two details complete, the third is not begun.
        var budget = new KapPublicWalkBudget(maxPages: 10, maxDetailFetches: 2,
            TimeSpan.FromMinutes(10), clock);
        var completed = new List<string>();
        foreach (var unit in units)
        {
            if (!budget.TryBeginDetail())
            {
                break;
            }

            completed.Add(unit);
            budget.Complete(unit);
        }

        Assert.Equal(["1643141", "1598316"], completed);
        Assert.True(budget.Truncated);
        Assert.Equal(KapPublicReasonCodes.DetailCap, budget.TruncationReason);
        Assert.Equal(2, budget.DetailsUsed);
        Assert.Equal("1598316", budget.LastCompletedUnit);

        // The resume: the next walk starts after the persisted position, never from the start
        // and never skipping the unit it had not completed.
        var resumed = units.SkipWhile(unit => unit != budget.LastCompletedUnit).Skip(1).ToList();
        Assert.Equal(["1561039"], resumed);

        // (v-b) the page ceiling.
        var pageBudget = new KapPublicWalkBudget(maxPages: 1, maxDetailFetches: 10,
            TimeSpan.FromMinutes(10), clock);
        Assert.True(pageBudget.TryBeginPage());
        Assert.False(pageBudget.TryBeginPage());
        Assert.Equal(KapPublicReasonCodes.PageCap, pageBudget.TruncationReason);

        // (v-c) the wall clock: crossing the deadline truncates before the next request.
        var deadlineBudget = new KapPublicWalkBudget(maxPages: 10, maxDetailFetches: 10,
            TimeSpan.FromMinutes(10), clock);
        Assert.True(deadlineBudget.TryBeginDetail());
        deadlineBudget.Complete("1643141");
        clock.Advance(TimeSpan.FromMinutes(11));
        Assert.False(deadlineBudget.TryBeginDetail());
        Assert.Equal(KapPublicReasonCodes.WalkDeadline, deadlineBudget.TruncationReason);
        Assert.Equal("1643141", deadlineBudget.LastCompletedUnit);

        // (v-d) a non-positive deadline disables the bound (the pre-budget behaviour).
        var unbounded = new KapPublicWalkBudget(maxPages: 0, maxDetailFetches: 0, TimeSpan.Zero, clock);
        clock.Advance(TimeSpan.FromDays(1));
        Assert.True(unbounded.TryBeginDetail());
        Assert.False(unbounded.Truncated);
    }

    // =================================================================================
    // TC-MDF-103 (vi) — never parallel
    // =================================================================================

    /// <summary>
    /// TC-MDF-103 (vi) — the client is strictly single-flight: with concurrent callers
    /// offered (which the lane never does, but a wiring mistake could), no two requests are
    /// ever in flight together — the double's journal shows the requests serialized, never
    /// overlapping.
    /// </summary>
    [Fact]
    public async Task TC_MDF_103_j_requests_are_strictly_sequential_never_overlapping()
    {
        var probe = new ConcurrencyProbe();
        var handler = new RecordingHandler(async _ =>
        {
            probe.Enter();
            await Task.Delay(20);
            probe.Exit();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            };
        });
        var client = BuildClient(handler, TimeProvider.System, delayMs: 0);

        await Task.WhenAll(
            client.GetStringAsync(ProbePath),
            client.GetStringAsync(ProbePath),
            client.GetStringAsync(ProbePath));

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(1, probe.MaxInFlight);
    }

    // =================================================================================
    // TC-MDF-104 — the ToS/robots gate and the OQ-1 probe (recorded evidence + refusal)
    // =================================================================================

    /// <summary>
    /// TC-MDF-104 (i) — the ToS/robots evidence is in the repo: the <c>robots.txt</c> answer
    /// is committed <b>verbatim</b> (the site serves its HTML access-error page at that path,
    /// HTTP 666 — so there is no directive to read and the answer is kept as-is rather than
    /// summarised), its provenance is a capture with a readable label, and the pinned reading
    /// is recorded in the architecture's §10.9.19 evidence block.
    /// </summary>
    [Fact]
    public void TC_MDF_104_a_the_robots_capture_and_its_recorded_reading_are_in_the_repo()
    {
        var robots = RealSourceCatalog.Get(RealSourceCatalog.KapPublicRobots);

        Assert.Equal(RealSourceProvenance.Captured, robots.Provenance);
        Assert.Equal("kap-public", robots.Source);

        // The record's status is the status the probe actually saw: HTTP 666, not a
        // normalised 200. The committed evidence and its own label must agree — a catalog
        // entry that says "666" in prose and "200" in the field is not honest evidence.
        Assert.Equal(666, robots.StatusCode);
        Assert.Contains("666", robots.ProvenanceLabel, StringComparison.Ordinal);

        // `DefaultPath` is the source-double's serving route (the catalog-wide
        // `/real-sources/{name}` convention); the live upstream route the bytes came from is
        // recorded in the provenance label, not smuggled into the serving route.
        Assert.Equal($"/real-sources/{RealSourceCatalog.KapPublicRobots}", robots.DefaultPath);
        Assert.Contains("https://www.kap.org.tr/robots.txt", robots.ProvenanceLabel, StringComparison.Ordinal);

        // The verbatim body: the site's own HTML access-error page, not a prose summary.
        Assert.Contains("<html>", robots.Body, StringComparison.Ordinal);
        Assert.Contains("Yasadiginiz bu erisim problemini", robots.Body, StringComparison.Ordinal);
        Assert.Contains("kapdestek@mkk.com.tr", robots.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Disallow", robots.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("User-agent", robots.Body, StringComparison.OrdinalIgnoreCase);

        // The recorded reading (the go/no-go judgement) lives with the contract it governs.
        var architecturePath = Path.Combine(
            FindRepositoryRoot(), ".pipeline", "architecture", "01-system-architecture.md");
        Assert.True(File.Exists(architecturePath), $"architecture doc missing at {architecturePath}");
        var architecture = File.ReadAllText(architecturePath);
        Assert.Contains("#### 10.9.19", architecture, StringComparison.Ordinal);
        Assert.Contains("kap-public-robots", architecture, StringComparison.Ordinal);
        Assert.Contains("666", architecture, StringComparison.Ordinal);
    }

    /// <summary>
    /// TC-MDF-104 (ii) — the OQ-1 probe is committed and honest whatever it showed: an
    /// <b>empty</b> <c>mkkMemberOidList</c> answered the market-wide list (12 rows on the
    /// captured window), it classifies through the <b>production</b> reader as a well-formed
    /// list, and the captured production test entity (<c>KAP TEST A.Ş.</c>, <c>KTEST</c>) is
    /// present — so any later exclusion of it must be a named, recorded rule, never a silent
    /// drop. This is the evidence TKT-foundation-084's gate reads.
    /// </summary>
    [Fact]
    public void TC_MDF_104_b_the_oq1_market_wide_probe_is_committed_and_honest()
    {
        var probe = RealSourceCatalog.Get(RealSourceCatalog.KapPublicMarketProbe);

        Assert.Equal(RealSourceProvenance.Captured, probe.Provenance);
        Assert.Equal("kap-public", probe.Source);
        Assert.Equal("POST", probe.Method);
        Assert.Contains("OQ-1", probe.ProvenanceLabel, StringComparison.Ordinal);
        Assert.Contains("market-wide", probe.ProvenanceLabel, StringComparison.OrdinalIgnoreCase);

        var classified = KapPublicSourceReader.ClassifyDisclosureList(probe.Body);
        Assert.True(classified.IsOk, $"the market-wide probe must classify as a list ({classified.ReasonCode})");
        var rows = classified.Payload!;
        Assert.Equal(12, rows.Count);
        Assert.Equal(1687047L, rows[0].DisclosureIndex);
        Assert.Equal(new[] { "DG", "DKB", "FR", "ODA" },
            rows.Select(row => row.DisclosureClass!)
                .Distinct()
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray());
        Assert.Contains(rows, row => row.StockCodes == "KTEST" && row.KapTitle == "KAP TEST A.Ş.");

        // Market-wide rows carry no company scope of their own: some stockCodes are null, so
        // the later feed must count them honestly rather than assume a company.
        Assert.Contains(rows, row => row.StockCodes is null);
    }

    /// <summary>
    /// TC-MDF-104 (iii) — the gate is <b>checkable behaviour</b>, not prose: with the recorded
    /// posture configured <c>Forbidden</c> the client refuses to issue a single request
    /// (<see cref="KapPublicAccessRefusedException"/>, its own reason code) and the double
    /// records none — so a forbidden reading cannot be silently walked past.
    /// </summary>
    [Fact]
    public async Task TC_MDF_104_c_a_forbidden_posture_refuses_before_any_request_is_issued()
    {
        ResetDoubles();
        _wireMock.StubJson(ByCriteriaPath, RealSourceCatalog.Get(RealSourceCatalog.KapPublicByCriteria).Body, method: "POST");

        var handler = new RecordingHandler(_ => JsonResponse("[]"));
        var forbiddenOptions = OptionsOf(o => o.AutomatedAccess = KapPublicAccessPosture.Forbidden);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero));

        // The client is built against the public-site double <b>through the recording handler</b>,
        // so a leaked request would be recorded — none is.
        var http = BuildHttp(handler);
        var pacer = new KapPublicRequestPacer(forbiddenOptions, clock);
        var client = new KapPublicHttpSourceClient(http, forbiddenOptions, pacer, clock);
        var reader = new KapPublicSourceReader(client, forbiddenOptions);

        var exception = await Assert.ThrowsAsync<KapPublicAccessRefusedException>(
            () => reader.ReadDisclosureListAsync(AselsFrQuery()));
        Assert.Equal(KapPublicReasonCodes.AutomatedAccessForbidden, exception.ReasonCode);
        Assert.Equal(0, _wireMock.RequestsFor(ByCriteriaPath));

        // The refusal holds for every method shape (a report detail is refused the same way).
        await Assert.ThrowsAsync<KapPublicAccessRefusedException>(() => reader.ReadReportAsync(1643141));
        Assert.Empty(_wireMock.Server.LogEntries);

        // And the recording handler never saw the refused request either.
        Assert.Empty(handler.Requests);
        Assert.False(forbiddenOptions.Value.AllowsAutomatedAccess);
    }

    /// <summary>
    /// TC-MDF-104 (c) — the code default is <b>not</b> a permission: an options object nobody
    /// configured is <see cref="KapPublicAccessPosture.NotVerified"/>, and the client refuses
    /// with the distinct reason <c>ACCESS_POSTURE_NOT_VERIFIED</c> (kept apart from
    /// <c>AUTOMATED_ACCESS_FORBIDDEN</c>) before it takes the flight gate or paces.
    /// </summary>
    [Fact]
    public async Task TC_MDF_104_c2_an_unconfigured_posture_refuses_as_not_verified()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero));
        var handler = new RecordingHandler(_ => JsonResponse("[]"));
        var options = Microsoft.Extensions.Options.Options.Create(new KapPublicSourceOptions
        {
            BaseUrl = _wireMock.BaseUrl,
            RequestDelayMs = 0,
        });

        Assert.Equal(KapPublicAccessPosture.NotVerified, options.Value.AutomatedAccess);
        Assert.False(options.Value.AllowsAutomatedAccess);
        Assert.NotEqual(
            KapPublicReasonCodes.AutomatedAccessForbidden,
            KapPublicReasonCodes.AccessPostureNotVerified);

        var client = new KapPublicHttpSourceClient(
            BuildHttp(handler), options, new KapPublicRequestPacer(options, clock), clock);
        var reader = new KapPublicSourceReader(client, options);

        var exception = await Assert.ThrowsAsync<KapPublicAccessRefusedException>(
            () => reader.ReadDisclosureListAsync(AselsFrQuery()));
        Assert.Equal(KapPublicReasonCodes.AccessPostureNotVerified, exception.ReasonCode);
        await Assert.ThrowsAsync<KapPublicAccessRefusedException>(() => reader.ReadReportAsync(1643141));
        Assert.Empty(handler.Requests);
    }

    // =================================================================================
    // TC-MDF-127 — the durable access fix: quota awareness, the export floor, the TLS shape
    // (TKT-foundation-100, 01 v1.5.77 §10.9.19)
    // =================================================================================

    /// <summary>
    /// TC-MDF-127 (a) — a <c>429</c> is a <b>named</b> outcome with <b>one</b> bounded re-issue,
    /// never a retry ladder (the 2026-10-28 finding: the export route answers
    /// "Request Limit Exceeded", empty body, no <c>Retry-After</c>). The request journal proves
    /// exactly two requests — the refused one and the single re-issue after the wait — and a
    /// source that stays rate-limited is reported under its own reason, not as a transport
    /// failure and not as another attempt.
    /// </summary>
    [Fact]
    public async Task TC_MDF_127_a_a_429_is_named_with_one_bounded_reissue_and_never_a_ladder()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 29, 9, 0, 0, TimeSpan.Zero));
        var waits = new List<TimeSpan>();
        KapPublicDelay quotaRecorder = (wait, _) =>
        {
            waits.Add(wait);
            return Task.CompletedTask;
        };

        var reportBody = RealSourceCatalog.Get(RealSourceCatalog.KapPublicReport).Body;

        // The quota is honored, then the source answers: exactly one re-issue after the wait.
        var attempts = 0;
        var recovered = new RecordingHandler(_ =>
        {
            attempts++;
            return attempts == 1
                ? TooManyRequests()
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(reportBody, Encoding.UTF8, "text/html"),
                };
        });
        var client = BuildClient(recovered, clock, quotaDelay: quotaRecorder);
        var body = await client.GetDetailStringAsync(ReportPath);

        Assert.Equal(reportBody, body);
        Assert.Equal(2, recovered.Requests.Count);
        var wait = Assert.Single(waits);
        Assert.Equal(TimeSpan.FromMilliseconds(KapPublicSourceOptions.DefaultQuotaBackoffMs), wait);

        // A source that stays rate-limited is named, counted, and asked exactly twice.
        var refusing = new RecordingHandler(_ => TooManyRequests());
        var refusingWaits = new List<TimeSpan>();
        KapPublicDelay refusingRecorder = (delay, _) =>
        {
            refusingWaits.Add(delay);
            return Task.CompletedTask;
        };
        var refusingClient = BuildClient(refusing, clock, quotaDelay: refusingRecorder);

        var quota = await Assert.ThrowsAsync<KapPublicQuotaException>(
            () => refusingClient.GetDetailStringAsync(ReportPath));
        Assert.Equal(KapPublicReasonCodes.QuotaRateLimited, quota.ReasonCode);
        Assert.False(quota.Deferred);
        Assert.Equal(TimeSpan.FromMilliseconds(KapPublicSourceOptions.DefaultQuotaBackoffMs), quota.RequiredWait);
        Assert.Equal(KapPublicHttpSourceClient.MaxQuotaAttempts, refusing.Requests.Count);
        Assert.Equal(2, refusing.Requests.Count);
        Assert.Single(refusingWaits);

        // …and the reader names it, so the ledger never records it as a plain source-down.
        var reader = new KapPublicSourceReader(refusingClient, OptionsOf());
        var read = await reader.ReadReportAsync(1643141);
        Assert.True(read.IsSourceDown);
        Assert.False(read.IsQuotaDeferred);
        Assert.Equal(KapPublicReasonCodes.QuotaRateLimited, read.ReasonCode);
    }

    /// <summary>
    /// TC-MDF-127 (b) — a <c>Retry-After</c> the source <b>does</b> send is honored
    /// <b>verbatim</b>: the delta-seconds form and the HTTP-date form both produce exactly the
    /// wait the source asked for (never the configured fixed backoff, never a rounded or capped
    /// value).
    /// </summary>
    [Fact]
    public async Task TC_MDF_127_b_a_retry_after_header_is_honored_verbatim()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 29, 9, 0, 0, TimeSpan.Zero));
        var reportBody = RealSourceCatalog.Get(RealSourceCatalog.KapPublicReport).Body;

        // (b-i) the delta-seconds form.
        var waits = new List<TimeSpan>();
        KapPublicDelay recorder = (delay, _) =>
        {
            waits.Add(delay);
            return Task.CompletedTask;
        };
        var attempts = 0;
        var handler = new RecordingHandler(_ =>
        {
            attempts++;
            if (attempts > 1)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(reportBody, Encoding.UTF8, "text/html"),
                };
            }

            var response = TooManyRequests();
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
                TimeSpan.FromSeconds(7));
            return response;
        });

        var client = BuildClient(handler, clock, quotaDelay: recorder);
        Assert.Equal(reportBody, await client.GetDetailStringAsync(ReportPath));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(TimeSpan.FromSeconds(7), Assert.Single(waits));

        // (b-ii) the HTTP-date form: the wait is the distance to the date it names.
        var dateWaits = new List<TimeSpan>();
        KapPublicDelay dateRecorder = (delay, _) =>
        {
            dateWaits.Add(delay);
            return Task.CompletedTask;
        };
        var dateAttempts = 0;
        var dateHandler = new RecordingHandler(_ =>
        {
            dateAttempts++;
            if (dateAttempts > 1)
            {
                return JsonResponse("[]");
            }

            var response = TooManyRequests();
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
                clock.GetUtcNow().AddSeconds(5));
            return response;
        });

        var datedClient = BuildClient(dateHandler, clock, quotaDelay: dateRecorder);
        Assert.Equal("[]", await datedClient.GetStringAsync(ProbePath));
        Assert.Equal(2, dateHandler.Requests.Count);
        Assert.Equal(TimeSpan.FromSeconds(5), Assert.Single(dateWaits));
    }

    /// <summary>
    /// TC-MDF-127 (c) — a backoff that would <b>exceed the caller's per-item bound</b> is never
    /// slept: the lane refuses to outlive its own caller, reports the walk's truncation reason
    /// (<c>WALK_QUOTA_BACKOFF</c>, the bounded-pass family that resumes next pass) and issues
    /// <b>no</b> second request. Both sources of the demanded wait are covered — the source's own
    /// <c>Retry-After</c> and the configured fixed backoff — because both are bounded by the same
    /// rule.
    /// </summary>
    [Fact]
    public async Task TC_MDF_127_c_a_backoff_past_the_per_item_bound_truncates_instead_of_sleeping()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 29, 9, 0, 0, TimeSpan.Zero));

        // (c-i) the source asks for 120 s on a route whose own bound is 60 s.
        var waits = new List<TimeSpan>();
        KapPublicDelay recorder = (delay, _) =>
        {
            waits.Add(delay);
            return Task.CompletedTask;
        };
        var handler = new RecordingHandler(_ =>
        {
            var response = TooManyRequests();
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
                TimeSpan.FromSeconds(120));
            return response;
        });

        var client = BuildClient(handler, clock, quotaDelay: recorder);
        var deferred = await Assert.ThrowsAsync<KapPublicQuotaException>(
            () => client.GetDetailStringAsync(ReportPath));

        Assert.True(deferred.Deferred);
        Assert.Equal(KapPublicReasonCodes.WalkQuotaBackoff, deferred.ReasonCode);
        Assert.Equal(TimeSpan.FromSeconds(120), deferred.RequiredWait);
        Assert.Contains("per-item bound", deferred.Message, StringComparison.Ordinal);
        Assert.Single(handler.Requests);   // no re-issue: the wait would outlive the caller
        Assert.Empty(waits);               // and nothing was slept past the bound
        Assert.Contains(KapPublicReasonCodes.WalkQuotaBackoff, deferred.Message, StringComparison.Ordinal);

        // The walk's seam sees a truncation, not a skippable source-down.
        var reader = new KapPublicSourceReader(client, OptionsOf());
        var read = await reader.ReadReportAsync(1643141);
        Assert.True(read.IsQuotaDeferred);
        Assert.True(read.IsSourceDown);
        Assert.False(read.IsOk);
        Assert.Equal(KapPublicReasonCodes.WalkQuotaBackoff, read.ReasonCode);

        // (c-ii) the same rule bounds the *configured* fixed backoff: with a 3 s general bound,
        // the shipped 8 s backoff is over it, so the general path defers too.
        var generalWaits = new List<TimeSpan>();
        KapPublicDelay generalRecorder = (delay, _) =>
        {
            generalWaits.Add(delay);
            return Task.CompletedTask;
        };
        var listHandler = new RecordingHandler(_ => TooManyRequests());
        var boundedClient = BuildClient(listHandler, clock, quotaDelay: generalRecorder, requestTimeoutMs: 3_000);

        var generalQuota = await Assert.ThrowsAsync<KapPublicQuotaException>(
            () => boundedClient.GetStringAsync(ProbePath));
        Assert.True(generalQuota.Deferred);
        Assert.Equal(KapPublicReasonCodes.WalkQuotaBackoff, generalQuota.ReasonCode);
        Assert.Equal(TimeSpan.FromMilliseconds(KapPublicSourceOptions.DefaultQuotaBackoffMs), generalQuota.RequiredWait);
        Assert.Single(listHandler.Requests);
        Assert.Empty(generalWaits);

        // The contrast that makes it a *bound*, not a blanket refusal: a wait that fits still
        // re-issues (asserted by (a)/(b)); here the same 8 s backoff under a 30 s bound defers
        // nothing — it is slept and re-issued.
        var roomyHandler = new RecordingHandler(_ => JsonResponse("[]"));
        var roomy = BuildClient(roomyHandler, clock, quotaDelay: generalRecorder);
        Assert.Equal("[]", await roomy.GetStringAsync(ProbePath));
        Assert.Single(roomyHandler.Requests);
    }

    /// <summary>
    /// TC-MDF-127 (d) — the export pacing knob delays <b>detail</b> fetches and <b>only</b> them:
    /// on a fake clock the recorded waits are the export floor (12 s) for each export request and
    /// the unchanged general pace (3 s) for each list/probe request, in the order the journal
    /// shows the requests. The floor raises the export path, never the others, and a disabled
    /// general pace disables both (the L2 harness binding).
    /// </summary>
    [Fact]
    public async Task TC_MDF_127_d_the_export_pacing_knob_delays_detail_fetches_and_only_them()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 29, 9, 0, 0, TimeSpan.Zero));
        var waits = new List<TimeSpan>();
        KapPublicDelay recorder = (delay, _) =>
        {
            waits.Add(delay);
            return Task.CompletedTask;
        };

        var reportBody = RealSourceCatalog.Get(RealSourceCatalog.KapPublicReport).Body;
        var handler = new RecordingHandler(request =>
            request.RequestUri!.AbsolutePath == ReportPath
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(reportBody, Encoding.UTF8, "text/html"),
                }
                : JsonResponse("[]"));

        var options = OptionsOf(o =>
        {
            o.RequestDelayMs = 3000;
            o.ExportRequestDelayMs = 12000;
        });
        var client = new KapPublicHttpSourceClient(
            BuildHttp(handler), options, new KapPublicRequestPacer(options, clock, recorder), clock);

        await client.GetStringAsync(ProbePath);        // 1. list/probe — general pace
        await client.GetDetailStringAsync(ReportPath); // 2. export — the floor
        await client.GetStringAsync(ProbePath);        // 3. list/probe — general pace
        await client.GetDetailStringAsync(ReportPath); // 4. export — the floor

        // Journal-proven order and kind.
        Assert.Equal(
            [ProbePath, ReportPath, ProbePath, ReportPath],
            handler.Requests.Select(request => request.Path).ToArray());

        // Three gaps: export, general, export — the general pace is untouched on the list path.
        Assert.Equal(
            [TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(12)],
            waits);

        // The floor is configuration: with it at 0 the export path keeps the general 3 s pace.
        var flatWaits = new List<TimeSpan>();
        KapPublicDelay flatRecorder = (delay, _) =>
        {
            flatWaits.Add(delay);
            return Task.CompletedTask;
        };
        var flatOptions = OptionsOf(o =>
        {
            o.RequestDelayMs = 3000;
            o.ExportRequestDelayMs = 0;
        });
        var flatHandler = new RecordingHandler(_ => JsonResponse("[]"));
        var flat = new KapPublicHttpSourceClient(
            BuildHttp(flatHandler), flatOptions, new KapPublicRequestPacer(flatOptions, clock, flatRecorder), clock);

        await flat.GetStringAsync(ProbePath);
        await flat.GetDetailStringAsync(ReportPath);
        Assert.Equal([TimeSpan.FromSeconds(3)], flatWaits);

        // A disabled general pace (the harness binding) disables the floor too: nothing is slept.
        var offWaits = new List<TimeSpan>();
        KapPublicDelay offRecorder = (delay, _) =>
        {
            offWaits.Add(delay);
            return Task.CompletedTask;
        };
        var offOptions = OptionsOf(o =>
        {
            o.RequestDelayMs = 0;
            o.ExportRequestDelayMs = 12000;
        });
        var offHandler = new RecordingHandler(_ => JsonResponse("[]"));
        var off = new KapPublicHttpSourceClient(
            BuildHttp(offHandler), offOptions, new KapPublicRequestPacer(offOptions, clock, offRecorder), clock);

        await off.GetStringAsync(ProbePath);
        await off.GetDetailStringAsync(ReportPath);
        Assert.Empty(offWaits);
    }

    /// <summary>
    /// TC-MDF-127 (e) — the TLS shape is <b>config-equivalence-pinned</b> (the 091 UA-pin
    /// discipline applied to the new options): the shipped <c>appsettings.json</c> values equal
    /// the code defaults, the factory the DI registration uses builds exactly that shape
    /// (protocol floor, ALPN offer, cipher-suite order where the platform allows one), standing
    /// the shaping down reproduces the pre-ticket stack, and the honest User-Agent the shaping
    /// rides beside is <b>unchanged</b>.
    /// </summary>
    [Fact]
    public async Task TC_MDF_127_e_the_tls_shape_is_config_equivalence_pinned()
    {
        var appsettingsPath = Path.Combine(FindRepositoryRoot(), "src", "Api", "appsettings.json");
        Assert.True(File.Exists(appsettingsPath), $"appsettings.json missing at {appsettingsPath}");
        using var document = JsonDocument.Parse(File.ReadAllText(appsettingsPath));
        var kapPublic = document.RootElement.GetProperty("Ingestion").GetProperty("KapPublic");

        // (e-i) shipped config == code defaults, key by key.
        Assert.Equal(KapPublicSourceOptions.DefaultExportRequestDelayMs, AsInt(kapPublic, "ExportRequestDelayMs"));
        Assert.Equal(12000, AsInt(kapPublic, "ExportRequestDelayMs"));
        Assert.Equal(KapPublicSourceOptions.DefaultQuotaBackoffMs, AsInt(kapPublic, "QuotaBackoffMs"));
        Assert.Equal(8000, AsInt(kapPublic, "QuotaBackoffMs"));
        Assert.Equal("true", kapPublic.GetProperty("TlsShapeEnabled").GetString());
        Assert.Equal(KapPublicSourceOptions.DefaultTlsShapeEnabled, new KapPublicSourceOptions().TlsShapeEnabled);
        Assert.Equal(KapPublicSourceOptions.DefaultTlsMinimumVersion, kapPublic.GetProperty("TlsMinimumVersion").GetString());
        Assert.Equal("Tls12", kapPublic.GetProperty("TlsMinimumVersion").GetString());
        Assert.Equal(KapPublicSourceOptions.DefaultTlsAlpnProtocols, kapPublic.GetProperty("TlsAlpnProtocols").GetString());
        Assert.Equal("h2,http/1.1", kapPublic.GetProperty("TlsAlpnProtocols").GetString());
        Assert.Equal(KapPublicSourceOptions.DefaultTlsCipherSuites, kapPublic.GetProperty("TlsCipherSuites").GetString());

        // The shipped general pace is unchanged by this ticket (the export floor raises only the
        // export path) and so is the honest UA (the 091 pin, frozen input).
        Assert.Equal(KapPublicSourceOptions.DefaultRequestDelayMs, AsInt(kapPublic, "RequestDelayMs"));
        var shippedUserAgent = kapPublic.GetProperty("UserAgent").GetString() ?? string.Empty;
        Assert.Equal(KapPublicSourceOptions.DefaultUserAgent, shippedUserAgent);
        Assert.Contains("Degerli/0.1", shippedUserAgent, StringComparison.Ordinal);
        Assert.DoesNotContain("DegerliBot", shippedUserAgent, StringComparison.Ordinal);

        // (e-ii) the shipped values, bound onto the options, build the pinned shape.
        var shipped = new KapPublicSourceOptions
        {
            ExportRequestDelayMs = AsInt(kapPublic, "ExportRequestDelayMs"),
            QuotaBackoffMs = AsInt(kapPublic, "QuotaBackoffMs"),
            TlsShapeEnabled = bool.Parse(kapPublic.GetProperty("TlsShapeEnabled").GetString()!),
            TlsMinimumVersion = kapPublic.GetProperty("TlsMinimumVersion").GetString()!,
            TlsAlpnProtocols = kapPublic.GetProperty("TlsAlpnProtocols").GetString()!,
            TlsCipherSuites = kapPublic.GetProperty("TlsCipherSuites").GetString()!,
        };
        var codeDefaults = new KapPublicSourceOptions();
        Assert.Equal(codeDefaults.ExportRequestDelayMs, shipped.ExportRequestDelayMs);
        Assert.Equal(codeDefaults.QuotaBackoffMs, shipped.QuotaBackoffMs);
        Assert.Equal(codeDefaults.TlsShapeEnabled, shipped.TlsShapeEnabled);
        Assert.Equal(codeDefaults.TlsMinimumVersion, shipped.TlsMinimumVersion);
        Assert.Equal(codeDefaults.TlsAlpnProtocols, shipped.TlsAlpnProtocols);
        Assert.Equal(codeDefaults.TlsCipherSuites, shipped.TlsCipherSuites);

        var handler = KapPublicHttpHandlerFactory.Create(shipped);
        // An exact set, not a flag test: TLS 1.2 + 1.3 and nothing else (TLS 1.0/1.1/SSL3 are
        // never offered — the browser floor).
        Assert.Equal(SslProtocols.Tls12 | SslProtocols.Tls13, handler.SslOptions.EnabledSslProtocols);

        Assert.Equal(
            [SslApplicationProtocol.Http2, SslApplicationProtocol.Http11],
            handler.SslOptions.ApplicationProtocols?.ToArray() ?? []);

        var expectedSuites = KapPublicHttpHandlerFactory.ParseCipherSuites(shipped.TlsCipherSuites);
        Assert.Equal(11, expectedSuites.Count);
        if (OperatingSystem.IsWindows())
        {
            // Recorded platform fact, never faked: Windows does not let a client order its
            // cipher suites, so the property stays unset and the platform default stands.
            Assert.Null(handler.SslOptions.CipherSuitesPolicy);
        }
        else
        {
            Assert.NotNull(handler.SslOptions.CipherSuitesPolicy);
            Assert.Equal(expectedSuites, handler.SslOptions.CipherSuitesPolicy!.AllowedCipherSuites.ToArray());
        }

        // (e-iii) standing the shaping down reproduces the pre-ticket stack: no TLS opinions.
        var unshaped = KapPublicHttpHandlerFactory.Create(
            new KapPublicSourceOptions { TlsShapeEnabled = false });
        Assert.Equal(SslProtocols.None, unshaped.SslOptions.EnabledSslProtocols);
        Assert.Null(unshaped.SslOptions.CipherSuitesPolicy);
        Assert.Empty(unshaped.SslOptions.ApplicationProtocols ?? []);

        // (e-iv) the DI registration really builds its client over that handler and agrees with
        // the ALPN it offers (h2 with the HTTP/1.1 fallback) — composition, not a code fork.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Host=localhost;Database=composition-only",
                ["Ingestion:KapPublic:BaseUrl"] = "https://www.kap.org.tr",
                ["Ingestion:KapPublic:AutomatedAccess"] = "Allowed",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMarketDataIngestion(configuration);
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var http = scope.ServiceProvider.GetRequiredKeyedService<HttpClient>(KapPublicSourceOptions.ClientKey);
        Assert.Equal(HttpVersion.Version20, http.DefaultRequestVersion);
        Assert.Equal(HttpVersionPolicy.RequestVersionOrLower, http.DefaultVersionPolicy);
    }

    // =================================================================================
    // helpers
    // =================================================================================

    private static int AsInt(JsonElement section, string name) =>
        int.Parse(
            section.GetProperty(name).GetRawText().Trim('"'),
            System.Globalization.CultureInfo.InvariantCulture);

    private static KapPublicListQuery AselsFrQuery() => new(
        new DateOnly(2026, 1, 1),
        new DateOnly(2026, 10, 25),
        "FR",
        [AselsPublicOid]);

    private static KapPublicSourceOptions NewOptions(Action<KapPublicSourceOptions>? configure = null)
    {
        var options = new KapPublicSourceOptions
        {
            BaseUrl = "http://localhost",
            RequestDelayMs = 0,
            // The harness binds the export quota floor OFF by default so a case that is about
            // the general pace keeps measuring the general pace; the floor's own behaviour is
            // TC-MDF-127 (d)'s subject and is bound explicitly there.
            ExportRequestDelayMs = 0,
            // L2 harness config: the tests that are not about the posture select the shipped
            // value explicitly. The default (NotVerified) is exercised by its own case.
            AutomatedAccess = KapPublicAccessPosture.Allowed,
        };
        configure?.Invoke(options);
        return options;
    }

    private static IOptions<KapPublicSourceOptions> OptionsOf(Action<KapPublicSourceOptions>? configure = null) =>
        Microsoft.Extensions.Options.Options.Create(NewOptions(configure));

    private KapPublicHttpSourceClient BuildClient(
        HttpMessageHandler? handler,
        TimeProvider clock,
        KapPublicDelay? delay = null,
        int delayMs = 0,
        int detailTimeoutMs = KapPublicSourceOptions.DefaultDetailFetchTimeoutMs,
        KapPublicDelay? quotaDelay = null,
        int requestTimeoutMs = KapPublicSourceOptions.DefaultRequestTimeoutMs)
    {
        var options = OptionsOf(o =>
        {
            o.RequestDelayMs = delayMs;
            o.DetailFetchTimeoutMs = detailTimeoutMs;
            o.RequestTimeoutMs = requestTimeoutMs;
        });
        // A null handler means "go to the double over the wire" (the WireMock cases); a
        // recording handler is used where the request shape, the pacing or the flight gate is
        // the subject.
        var http = handler is null ? new HttpClient() : new HttpClient(handler);
        http.BaseAddress = new Uri(_wireMock.BaseUrl);
        http.Timeout = Timeout.InfiniteTimeSpan;
        var pacer = new KapPublicRequestPacer(options, clock, delay);
        return new KapPublicHttpSourceClient(http, options, pacer, clock, quotaDelay);
    }

    /// <summary>An <see cref="HttpClient"/> over a recording handler, addressed at the WireMock double.</summary>
    private HttpClient BuildHttp(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler);
        http.BaseAddress = new Uri(_wireMock.BaseUrl);
        http.Timeout = Timeout.InfiniteTimeSpan;
        return http;
    }

    private static HttpResponseMessage JsonResponse(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    /// <summary>
    /// The observed quota answer (2026-10-28): <c>429</c>, empty body, no <c>Retry-After</c>.
    /// </summary>
    private static HttpResponseMessage TooManyRequests() => new(HttpStatusCode.TooManyRequests)
    {
        Content = new StringContent(string.Empty, Encoding.UTF8, "text/plain"),
    };

    private void ResetDoubles()
    {
        _wireMock.Server.ResetLogEntries();
        _wireMock.Server.ResetMappings();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Degerli.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root (Degerli.sln) not found.");
    }

    /// <summary>
    /// A recording HTTP handler — the second double of this suite. It captures each request's
    /// method, path, User-Agent and body so the request <b>shape</b> is asserted directly
    /// (WireMock's journal is kept for the routes and the on-the-wire counts), and it can
    /// answer asynchronously to exercise the single-flight gate.
    /// </summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _respond;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            : this(request => Task.FromResult(respond(request)))
        {
        }

        public RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) =>
            _respond = respond;

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var userAgent = request.Headers.TryGetValues("User-Agent", out var values)
                ? string.Join(' ', values)
                : string.Empty;
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri?.AbsolutePath ?? string.Empty,
                userAgent,
                body));
            return await _respond(request);
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, string Path, string UserAgent, string? Body);

    /// <summary>Counts concurrent requests so the single-flight guarantee is asserted, not assumed.</summary>
    private sealed class ConcurrencyProbe
    {
        private int _inFlight;
        private int _maxInFlight;

        public int MaxInFlight => Volatile.Read(ref _maxInFlight);

        public void Enter()
        {
            var current = Interlocked.Increment(ref _inFlight);
            int observed;
            do
            {
                observed = Volatile.Read(ref _maxInFlight);
                if (observed >= current)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref _maxInFlight, current, observed) != observed);
        }

        public void Exit() => Interlocked.Decrement(ref _inFlight);
    }
}
