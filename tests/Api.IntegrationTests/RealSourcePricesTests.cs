using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;
using Degerli.Ingestion.Prices;
using Degerli.Ingestion.Sources;
using Microsoft.Extensions.Options;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-foundation-015 acceptance — the <b>real</b> İş Yatırım price envelope decoded by the
/// production adapter (01 v1.5 §10.9.1, FR-MDF-001/010). The real-source catalog's raw
/// bytes are served through the WireMock harness and parsed by <see cref="PricesSourceAdapter"/>
/// in <c>src/Ingestion</c> — never a fixture-owned decoder (the DR-01 rule, §10.9.7). The
/// FU golden values are untouched: these cases assert parse shape/fidelity against the
/// captured bodies, never derived numbers.
/// </summary>
public sealed class RealSourcePricesTests : IClassFixture<WireMockFixture>
{
    private static readonly FixtureSet L2 = FixtureUniverse.Build(FixtureAnchor.L2);
    private static DateOnly T => L2.Anchor.T;
    private static readonly DateOnly CaptureEnd = new(2026, 10, 8);

    private readonly WireMockFixture _wireMock;

    public RealSourcePricesTests(WireMockFixture wireMock) => _wireMock = wireMock;

    /// <summary>
    /// TC-MDF-063 — the real-source catalog loads through the same
    /// <c>Names</c>/<c>Build</c>/<c>Get</c>/<c>TryGet</c> seam as the FU §10 catalog, every
    /// entry carries raw bytes + a declared charset + an honest provenance label, the
    /// labeling rule holds (no constructed body claims a verbatim capture), and each
    /// entry's raw bytes decode through the production client to its decoded body.
    /// </summary>
    [Fact]
    public async Task TC_MDF_063_Real_source_catalog_contract_is_honest_and_charset_faithful()
    {
        var all = RealSourceCatalog.Build();

        Assert.Equal(RealSourceCatalog.Names.Count, all.Count);
        Assert.Equal(RealSourceCatalog.Names, all.Select(p => p.Name).ToList());
        Assert.False(string.IsNullOrWhiteSpace(RealSourceCatalog.RedistributionTerms));

        using var http = new HttpClient { BaseAddress = new Uri(_wireMock.BaseUrl) };
        var client = new HttpSourceClient(http);

        foreach (var payload in all)
        {
            Assert.True(RealSourceCatalog.TryGet(payload.Name, out var lookedUp), payload.Name);
            Assert.Equal(payload.Name, lookedUp.Name);

            // Every entry: raw bytes, a declared charset and a provenance statement.
            Assert.NotEmpty(payload.RawBytes);
            Assert.False(string.IsNullOrWhiteSpace(payload.EncodingName));
            Assert.False(string.IsNullOrWhiteSpace(payload.ProvenanceLabel));
            Assert.StartsWith("/real-sources/", payload.DefaultPath, StringComparison.Ordinal);
            // The real-source catalog is multi-source — İş Yatırım EOD prices (TKT-foundation-015),
            // KAP XBRL statements (TKT-foundation-017) and the EVDS macro envelope (TKT-foundation-019);
            // every entry records its concrete source (01 v1.5 §10.9.7): price bodies are
            // `isyatirim`, statement bodies are `kap`, macro bodies are `evds`. A mislabeled
            // entry fails here, so the catalog is never a source-blind bag. The assertions of
            // TC-MDF-063 (raw bytes, declared charset, honest provenance label) are below.
            var expectedSource = payload.Name.StartsWith("kap-", StringComparison.Ordinal)
                ? RealSourceCatalog.KapSource
                : payload.Name.StartsWith("evds-", StringComparison.Ordinal)
                    ? RealSourceCatalog.EvdsSource
                    : RealSourceCatalog.IsYatirimSource;
            Assert.Equal(expectedSource, payload.Source);

            switch (payload.Provenance)
            {
                case RealSourceProvenance.Captured:
                    // A captured body decodes verbatim under its declared charset, and its
                    // label says so.
                    Assert.Contains("captured", payload.ProvenanceLabel, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain('\uFFFD', payload.DecodeRawText());
                    break;
                case RealSourceProvenance.Constructed:
                    // The DR-03 rule: a hand-constructed body is never labeled a verbatim capture.
                    Assert.Contains("constructed", payload.ProvenanceLabel, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("captured verbatim", payload.ProvenanceLabel, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("verbatim capture", payload.ProvenanceLabel, StringComparison.OrdinalIgnoreCase);
                    break;
                default:
                    Assert.Fail($"Unexpected provenance '{payload.Provenance}' for '{payload.Name}'.");
                    break;
            }

            // Serving the raw bytes with the declared Content-Type; charset= header and
            // fetching through the production client yields the entry's decoded body. The
            // fetch honours the entry's declared transport (01 §10.9.7/§10.9.8): the EVDS v3
            // values entries are served at the `/fe` POST route, so a GET-only fetch would
            // 404 against an honest catalog. The request-body shape is pinned by the adapter
            // tests (TC-MOV-028); this contract exercises the response side only.
            _wireMock.StubRealSource(payload.Name);
            var fetched = string.Equals(payload.Method, "POST", StringComparison.OrdinalIgnoreCase)
                ? await client.PostJsonAsync(payload.DefaultPath, "{}")
                : await client.GetStringAsync(payload.DefaultPath);
            Assert.Equal(payload.Body, fetched);
        }

        // Unknown names: TryGet false, Get throws.
        Assert.False(RealSourceCatalog.TryGet("does-not-exist", out _));
        Assert.Throws<KeyNotFoundException>(() => RealSourceCatalog.Get("does-not-exist"));
    }

    /// <summary>
    /// TC-MDF-064 — each captured İş Yatırım HGDG body decodes through the production
    /// adapter to its pinned close/high/low/₺-turnover volume, with <c>open</c> NULL (the
    /// honest gap — never invented from <c>HGDG_AOF</c>); a constructed variant exercises
    /// the alternate <c>HG_*</c> fallback spellings.
    /// </summary>
    [Fact]
    public async Task TC_MDF_064_Real_hgdig_envelope_decodes_through_the_production_adapter()
    {
        // Fidelity pins — capture record 2026-10-16, window 2026-10-06..2026-10-08 (TC-MDF-064).
        var expected = new (string Payload, string Ticker, decimal Close, decimal High, decimal Low, long Volume)[]
        {
            (RealSourceCatalog.PricesAsels, "ASELS", 348.75m, 354.0m, 340.25m, 12_941_100_644L),
            (RealSourceCatalog.PricesThyao, "THYAO", 284.25m, 287.5m, 282.0m, 5_829_586_658L),
            (RealSourceCatalog.PricesGaran, "GARAN", 128.5m, 129.8m, 127.0m, 2_001_849_137L),
            (RealSourceCatalog.PricesFroto, "FROTO", 74.40m, 74.70m, 73.70m, 837_500_049L),
            (RealSourceCatalog.PricesEbebk, "EBEBK", 80.0176m, 80.56323m, 74.95755m, 39_335_497L),
        };

        foreach (var (payloadName, ticker, close, high, low, volume) in expected)
        {
            var payload = _wireMock.StubRealSource(payloadName);
            var adapter = BuildAdapter(payload.DefaultPath);

            var fetched = await adapter.FetchAsync(CaptureEnd);

            Assert.Equal(RealSourceCatalog.IsYatirimSource, fetched.SourceRef);
            var row = Assert.Single(fetched.Payload.Items);
            Assert.Equal(ticker, row.Symbol);
            Assert.Equal(close, row.Close!.Value);
            Assert.Equal(high, row.High!.Value);
            Assert.Equal(low, row.Low!.Value);
            Assert.Equal(volume, row.Volume!.Value);
            Assert.Null(row.Open);
        }

        // The constructed HG_* variant: only the fallback spellings are present.
        var fallback = _wireMock.StubRealSource(RealSourceCatalog.PricesHgFallback);
        var fallbackAdapter = BuildAdapter(fallback.DefaultPath);
        var fallbackFetched = await fallbackAdapter.FetchAsync(CaptureEnd);

        Assert.Equal(RealSourceCatalog.IsYatirimSource, fallbackFetched.SourceRef);
        var fallbackRow = Assert.Single(fallbackFetched.Payload.Items);
        Assert.Equal("ASELS", fallbackRow.Symbol);
        Assert.Equal(348.75m, fallbackRow.Close!.Value);
        Assert.Equal(354.0m, fallbackRow.High!.Value);
        Assert.Equal(340.25m, fallbackRow.Low!.Value);
        Assert.Equal(12_941_100_644L, fallbackRow.Volume!.Value);
        Assert.Null(fallbackRow.Open);
    }

    /// <summary>
    /// TC-MDF-068 — one captured HGDG payload flows end-to-end through the adapter
    /// <b>backfill</b> path: exactly the three captured rows, the ASELS 2026-10-08 row
    /// pinned per TC-MDF-064, one HTTP request, raw bytes served (never a re-serialized
    /// canonical envelope), and no fabricated rows outside the source-returned window.
    /// </summary>
    [Fact]
    public async Task TC_MDF_068_Captured_hgdig_payload_flows_end_to_end_through_backfill()
    {
        _wireMock.StubRealSource(RealSourceCatalog.PricesAsels, path: "/prices/backfill");
        var adapter = BuildAdapter(backfillPath: "/prices/backfill");

        var fetched = await adapter.FetchBackfillAsync(new DateOnly(2026, 10, 6));

        Assert.Equal(RealSourceCatalog.IsYatirimSource, fetched.SourceRef);
        Assert.Equal(3, fetched.Payload.Rows.Count);
        Assert.Equal(
            new[] { new DateOnly(2026, 10, 6), new DateOnly(2026, 10, 7), CaptureEnd },
            fetched.Payload.Rows.Select(r => r.Date).OrderBy(d => d).ToArray());

        var asels = fetched.Payload.Rows.Single(r => r.Date == CaptureEnd);
        Assert.Equal("ASELS", asels.Symbol);
        Assert.Equal(348.75m, asels.Close!.Value);
        Assert.Equal(354.0m, asels.High!.Value);
        Assert.Equal(340.25m, asels.Low!.Value);
        Assert.Equal(12_941_100_644L, asels.Volume!.Value);
        Assert.Null(asels.Open);

        // Exactly one HTTP request to the per-ticker backfill route, and only the rows the
        // source returned — the achieved depth is never fabricated (BR-MDF-006).
        Assert.Equal(1, _wireMock.RequestsFor("/prices/backfill"));
        Assert.DoesNotContain(fetched.Payload.Rows, r => r.Date < new DateOnly(2026, 10, 6));
        Assert.DoesNotContain(fetched.Payload.Rows, r => r.Date > CaptureEnd);
    }

    /// <summary>
    /// TC-MDF-069 — the same production adapter detects all three first-class envelopes:
    /// the real HGDG body, the Yahoo <c>chart</c> contingency (volume NULL per FLG-09(b))
    /// and the canonical <c>sourceRef</c>/<c>items</c> double (the L2 golden shape) — with
    /// no envelope rejection and no cross-attribution of rows to the wrong source.
    /// </summary>
    [Fact]
    public async Task TC_MDF_069_Envelope_detection_real_yahoo_canonical()
    {
        // (1) Real HGDG envelope → isyatirim, HGDG mapping, open NULL.
        var real = _wireMock.StubRealSource(RealSourceCatalog.PricesAsels, path: "/prices/real");
        var realFetched = await BuildAdapter("/prices/real").FetchAsync(CaptureEnd);
        Assert.Equal(RealSourceCatalog.IsYatirimSource, realFetched.SourceRef);
        var realRow = Assert.Single(realFetched.Payload.Items);
        Assert.Equal("ASELS", realRow.Symbol);
        Assert.Null(realRow.Open);
        Assert.Equal(348.75m, realRow.Close!.Value);

        // (2) Yahoo chart contingency → yahoo, rows may carry open, volume NULL (FLG-09(b)).
        var yahoo = _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesYahooContingency, path: "/prices/yahoo");
        var yahooFetched = await BuildAdapter("/prices/yahoo").FetchAsync(T);
        Assert.Equal(PricesSourceAdapter.YahooSource, yahooFetched.SourceRef);
        var yahooExpected = L2.Prices.Where(p => p.Date == T).ToDictionary(p => p.Symbol, p => p);
        Assert.Equal(yahooExpected.Count, yahooFetched.Payload.Items.Count);
        Assert.All(yahooFetched.Payload.Items, row =>
        {
            var fact = yahooExpected[row.Symbol];
            Assert.Equal(fact.CloseRaw, row.Close!.Value);
            Assert.Equal(fact.Open, row.Open);
            // FLG-09(b): Yahoo's share-count volume is a different unit — stored NULL.
            Assert.Null(row.Volume);
        });

        // (3) Canonical double envelope → the L2 golden shape with ₺ volume stored.
        var canonical = _wireMock.StubCannedSource(L2, CannedSourceCatalog.PricesOk, path: "/prices/canonical");
        var canonicalFetched = await BuildAdapter("/prices/canonical").FetchAsync(T);
        Assert.Equal(PricesSourceAdapter.IsYatirimSource, canonicalFetched.SourceRef);
        var canonicalExpected = L2.Prices.Where(p => p.Date == T).ToDictionary(p => p.Symbol, p => p);
        Assert.Equal(canonicalExpected.Count, canonicalFetched.Payload.Items.Count);
        Assert.All(canonicalFetched.Payload.Items, row =>
        {
            var fact = canonicalExpected[row.Symbol];
            Assert.Equal(fact.CloseRaw, row.Close!.Value);
            Assert.Equal(fact.Volume, row.Volume!.Value);
        });

        // No cross-attribution: the three sources' rows are attributed to their own source.
        Assert.NotEqual(real.Source, yahoo.Source);
        Assert.NotEqual(yahoo.Source, canonical.Source);
    }

    /// <summary>
    /// TC-MDF-070 — a real-shaped envelope reporting a transport failure (<c>ok:false</c>
    /// or a non-null <c>errorCode</c>) surfaces a <see cref="SourceFetchException"/> and
    /// emits <b>zero</b> rows — a source failure, never a parse event.
    /// </summary>
    [Theory]
    [InlineData(RealSourceCatalog.PricesOkFalse)]
    [InlineData(RealSourceCatalog.PricesErrorCode)]
    public async Task TC_MDF_070_Transport_failure_is_a_source_failure(string payloadName)
    {
        var payload = _wireMock.StubRealSource(payloadName);
        var adapter = BuildAdapter(payload.DefaultPath);

        await Assert.ThrowsAsync<SourceFetchException>(() => adapter.FetchAsync(CaptureEnd));

        // Not an unparseable-payload event: the adapter never produces a canonical payload.
        await Assert.ThrowsAsync<SourceFetchException>(() => adapter.FetchBackfillAsync(new DateOnly(2026, 10, 6)));
    }

    private PricesSourceAdapter BuildAdapter(string? path = null, string? backfillPath = null)
    {
        var http = new HttpClient { BaseAddress = new Uri(_wireMock.BaseUrl) };
        var options = new PricesSourceOptions
        {
            Path = path ?? "/prices/eod",
            BackfillPath = backfillPath ?? path ?? "/prices/history",
        };
        return new PricesSourceAdapter(new HttpSourceClient(http), Options.Create(options));
    }
}
