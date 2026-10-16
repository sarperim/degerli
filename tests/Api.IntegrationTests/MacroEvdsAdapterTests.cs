using System.Text.Json;
using Degerli.Ingestion.Macro;
using Degerli.Ingestion.Sources;
using Microsoft.Extensions.Options;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-mov-008 acceptance — the consolidated TCMB EVDS macro adapter (TC-MOV-026, FR-MOV-014,
/// OQ-MOV-005). These are adapter-contract tests: they drive the real
/// <see cref="MacroSourceAdapter"/> through the <see cref="ISourceClient"/> seam with a fake
/// client and assert the EVDS request it builds and how it maps the EVDS envelope back to
/// the canonical macro series. No database or WireMock is involved — the cross-process
/// DB outcome is asserted in <see cref="MacroIngestionTests.TC_MOV_026_Consolidated_evds_adapter_ingests_the_five_official_series"/>.
/// </summary>
public sealed class MacroEvdsAdapterTests
{
    private static readonly DateOnly Date = new(2026, 10, 6);

    /// <summary>TC-MOV-026 — the adapter issues a single EVDS request naming the five
    /// official series, the bounded value-date window, the JSON type and the configured API key.</summary>
    [Fact]
    public async Task TC_MOV_026_Builds_the_consolidated_evds_request()
    {
        var client = new RecordingSourceClient(OfficialEnvelope());
        var adapter = Build(client, apiKey: "test-evds-key");

        await adapter.FetchAsync(Date);

        Assert.Equal(MacroSourceAdapter.EvdsSource, adapter.SourceName);
        var path = Assert.Single(client.Requests);
        Assert.StartsWith("/service/evds?", path, StringComparison.Ordinal);
        Assert.Contains("type=json", path);
        Assert.Contains("key=test-evds-key", path);

        // A bounded lookback, not a single-day window (DR-2): EVDS filters by observation
        // date, so the monthly/per-release series are only reachable with a lookback.
        Assert.Contains("endDate=06-10-2026", path);
        Assert.Contains("startDate=08-07-2026", path);

        // One request carries all five official series joined in order (the consolidation).
        var expectedSeries = string.Join('-',
            "TP.TUKFIY2025.GENEL",
            "TP.APIFON1",
            "TP.DK.USD.A.YTL",
            "TP.DK.EUR.A.YTL",
            "TP.MK.ALTIN");
        Assert.Contains($"series={expectedSeries}", path);
    }

    /// <summary>TC-MOV-026 — the EVDS envelope is mapped onto the canonical macro items:
    /// each EVDS <c>seriesCode</c> resolves to its canonical code (the observations carry no
    /// canonical <c>code</c>, so the mapping is what supplies it), value/date/unit/recorded_at
    /// survive, and an unknown code with no mapping is dropped.</summary>
    [Fact]
    public async Task TC_MOV_026_Maps_the_evds_envelope_to_canonical_series()
    {
        var client = new RecordingSourceClient(OfficialEnvelope());
        var adapter = Build(client, apiKey: "k");

        var payload = (await adapter.FetchAsync(Date)).Payload;

        var codes = payload.Series!.Select(s => s.Code).ToList();
        Assert.Contains(MacroSeries.UsdTry, codes);
        Assert.Contains(MacroSeries.EurTry, codes);
        Assert.Contains(MacroSeries.Gold, codes);
        Assert.Contains(MacroSeries.TuikCpi, codes);
        Assert.Contains(MacroSeries.CbrtRepo, codes);

        var usd = Assert.Single(payload.Series!, s => s.Code == MacroSeries.UsdTry);
        Assert.Equal(47.10m, usd.Value);
        Assert.Equal(Date, usd.ValueDate);
        Assert.Equal("TRY", usd.Unit);

        // An observation naming an unknown EVDS code with no canonical code is dropped.
        Assert.DoesNotContain(payload.Series!, s => s.Code == "TP.UNKNOWN.SERIES");
    }

    /// <summary>TC-MOV-026 — a malformed EVDS body surfaces as an invalid (payload) failure
    /// so the job can quarantine it and alert; a body that deserializes to null is an
    /// unreachable-class failure.</summary>
    [Fact]
    public async Task TC_MOV_026_Unparseable_evds_body_is_a_payload_failure()
    {
        var malformed = new RecordingSourceClient("{ not valid json");
        await Assert.ThrowsAsync<SourcePayloadException>(() => Build(malformed, "k").FetchAsync(Date));

        var empty = new RecordingSourceClient("null");
        await Assert.ThrowsAsync<SourceFetchException>(() => Build(empty, "k").FetchAsync(Date));
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    private static MacroSourceAdapter Build(ISourceClient client, string apiKey) =>
        new(
            client,
            Options.Create(new MacroSourceOptions
            {
                BaseUrl = "https://evds2.tcmb.example",
                Path = "/service/evds",
                EvdsApiKey = apiKey,
            }));

    /// <summary>An EVDS envelope covering the five official series plus one unknown code.
    /// The official observations deliberately carry no canonical <c>code</c> (as the real EVDS
    /// response does not) so the test discriminates the <c>seriesCode</c> → canonical mapping.</summary>
    private static string OfficialEnvelope() =>
        JsonSerializer.Serialize(new
        {
            sourceRef = "evds://tcmb",
            totalCount = 6,
            series = new object[]
            {
                new { seriesCode = "TP.TUKFIY2025.GENEL", valueDate = "2026-09-01", value = 45.20m, unit = "% YoY", recordedAt = "2026-10-03T10:00:00+00:00" },
                new { seriesCode = "TP.APIFON1", valueDate = "2026-09-15", value = 42.50m, unit = "%", recordedAt = "2026-09-15T14:00:00+00:00" },
                new { seriesCode = "TP.DK.USD.A.YTL", valueDate = "2026-10-06", value = 47.10m, unit = "TRY", recordedAt = "2026-10-06T21:30:00+00:00" },
                new { seriesCode = "TP.DK.EUR.A.YTL", valueDate = "2026-10-06", value = 51.40m, unit = "TRY", recordedAt = "2026-10-06T21:30:00+00:00" },
                new { seriesCode = "TP.MK.ALTIN", valueDate = "2026-09-26", value = 5200.00m, unit = "USD/oz", recordedAt = "2026-09-26T21:30:00+00:00" },
                new { seriesCode = "TP.UNKNOWN.SERIES", code = (string?)null, valueDate = "2026-10-06", value = 1m, unit = "x", recordedAt = (string?)null },
            },
        });

    private sealed class RecordingSourceClient : ISourceClient
    {
        private readonly string _body;

        public RecordingSourceClient(string body) => _body = body;

        public List<string> Requests { get; } = [];

        public Task<string> GetStringAsync(string path, CancellationToken cancellationToken = default)
        {
            Requests.Add(path);
            return Task.FromResult(_body);
        }
    }
}
