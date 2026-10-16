using System.Text;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Fixtures;
using Degerli.Ingestion.Prices;
using Degerli.Ingestion.Sources;
using Microsoft.Extensions.Options;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-foundation-015 acceptance — the real-source payload catalog (a second, real-shape
/// catalog separate from the synthetic FU §10 one) and parsing fidelity against real-world
/// input shapes. The FU golden values are untouched: these cases exercise parsing
/// <em>shape</em> — Turkish locale numeric formatting, ISO-8859-9/windows-1254 charsets and
/// inconsistent KAP XBRL tags — plus one payload consumed end-to-end through the WireMock
/// harness and the production price adapter.
/// </summary>
/// <remarks>
/// Case IDs TC-MDF-063..068 are the parsing cases this ticket introduces; they are proposed
/// for recording in <c>.pipeline/testing/market-data-foundation.md</c> (the test plan is
/// test-planner-owned and outside this ticket's <c>scope:</c> list — see the PR report).
/// </remarks>
public sealed class RealSourcePayloadTests : IClassFixture<WireMockFixture>
{
    private static readonly DateOnly CaptureEnd = new(2026, 10, 8);

    private readonly WireMockFixture _wireMock;

    public RealSourcePayloadTests(WireMockFixture wireMock) => _wireMock = wireMock;

    /// <summary>TC-MDF-063 — the real-source catalog loads through the same helper seam as the FU catalog.</summary>
    [Fact]
    public void TC_MDF_063_Real_source_catalog_loads_through_the_same_helper_seam()
    {
        var all = RealSourceCatalog.Build();

        Assert.Equal(RealSourceCatalog.Names.Count, all.Count);
        Assert.Equal(RealSourceCatalog.Names, all.Select(p => p.Name).ToList());

        foreach (var name in RealSourceCatalog.Names)
        {
            Assert.True(RealSourceCatalog.TryGet(name, out var payload), name);
            Assert.Equal(name, payload.Name);
            Assert.StartsWith("/real-sources/", payload.DefaultPath, StringComparison.Ordinal);
            Assert.Contains(payload.Source, new[] { RealSourceCatalog.IsYatirimSource, RealSourceCatalog.KapSource });
            Assert.NotEmpty(payload.RawBytes);
            Assert.False(string.IsNullOrWhiteSpace(payload.Body), $"{name} has an empty committed body");
        }

        Assert.False(RealSourceCatalog.TryGet("does-not-exist", out _));
        Assert.Throws<KeyNotFoundException>(() => RealSourceCatalog.Get("does-not-exist"));
    }

    /// <summary>TC-MDF-064 — each of the five tickers' prices decode into the canonical models.</summary>
    [Fact]
    public void TC_MDF_064_Each_ticker_prices_decode_into_the_canonical_models()
    {
        // (ticker, close, high, low, volume) captured for 2026-10-08 from the live
        // İş Yatırım HisseTekil envelope (HGDG_KAPANIS / HGDG_MAX / HGDG_MIN / HGDG_HACIM).
        var expected = new (string Price, string Ticker, decimal Close, decimal High, decimal Low, long Volume)[]
        {
            (RealSourceCatalog.PricesAsels, "ASELS", 348.75m, 354.0m, 340.25m, 12_941_100_644L),
            (RealSourceCatalog.PricesThyao, "THYAO", 284.25m, 287.5m, 282.0m, 5_829_586_658L),
            (RealSourceCatalog.PricesGaran, "GARAN", 128.5m, 129.8m, 127.0m, 2_001_849_137L),
            (RealSourceCatalog.PricesFroto, "FROTO", 74.40m, 74.70m, 73.70m, 837_500_049L),
            (RealSourceCatalog.PricesEbebk, "EBEBK", 80.0176m, 80.56323m, 74.95755m, 39_335_497L),
        };

        foreach (var (priceName, ticker, close, high, low, volume) in expected)
        {
            var payload = RealSourceCatalog.Get(priceName);
            var canonical = RealSourceCatalog.BuildPricesHistoryPayload(payload);

            Assert.Equal(RealSourceCatalog.IsYatirimSource, canonical.SourceRef);
            Assert.Equal(3, canonical.Rows.Count);

            var row = canonical.Rows.Single(r => r.Date == CaptureEnd);
            Assert.Equal(ticker, row.Symbol);
            Assert.Equal(close, row.Close!.Value);
            Assert.Equal(high, row.High!.Value);
            Assert.Equal(low, row.Low!.Value);
            Assert.Equal(volume, row.Volume!.Value);

            // The committed body is the canonical envelope the adapter parses.
            var reparsed = System.Text.Json.JsonSerializer.Deserialize<PricesHistoryPayload>(
                payload.Body, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            Assert.NotNull(reparsed);
            Assert.Equal(3, reparsed!.Rows.Count);
        }
    }

    /// <summary>TC-MDF-065 — statements decode with Turkish locale-formatted numbers decoded correctly.</summary>
    [Fact]
    public void TC_MDF_065_Statements_decode_locale_formatted_numbers_correctly()
    {
        // The parser: '.' is thousands, ',' is the decimal separator.
        Assert.Equal(1_234_567.89m, RealSourceCatalog.ParseTurkishNumber("1.234.567,89"));
        Assert.Equal(2_345_678_901.23m, RealSourceCatalog.ParseTurkishNumber("2.345.678.901,23"));
        Assert.Equal(-1_234.50m, RealSourceCatalog.ParseTurkishNumber("-1.234,50"));
        Assert.Equal(456_000_000m, RealSourceCatalog.ParseTurkishNumber("456.000.000"));

        var expectedRevenue = new (string Statements, decimal Revenue)[]
        {
            (RealSourceCatalog.StatementsAsels, 1_234_567.89m),
            (RealSourceCatalog.StatementsThyao, 2_345_678_901.23m),
            (RealSourceCatalog.StatementsGaran, 3_456_789_012.34m),
            (RealSourceCatalog.StatementsFroto, 4_567_890_123.45m),
            (RealSourceCatalog.StatementsEbebk, 5_678_901_234.56m),
        };

        foreach (var (statementsName, revenue) in expectedRevenue)
        {
            var canonical = RealSourceCatalog.BuildStatementsPayload(RealSourceCatalog.Get(statementsName));
            var fy2025 = canonical.Statements.Single(s => s.FiscalYear == 2025 && s.StatementType == "IS");

            Assert.Equal(revenue, fy2025.Lines["REV"]);
        }
    }

    /// <summary>TC-MDF-066 — the ISO-8859-9/windows-1254 bodies decode without mojibake.</summary>
    [Fact]
    public void TC_MDF_066_Legacy_encoded_bodies_decode_without_mojibake()
    {
        var payload = RealSourceCatalog.Get(RealSourceCatalog.LegacyWindows1254);
        Assert.True(payload.IsLegacyEncoded);
        Assert.Equal("windows-1254", payload.EncodingName);

        var decoded = payload.DecodeRawText();
        Assert.Contains("Şirket kârı", decoded, StringComparison.Ordinal);
        Assert.Contains("Kapanış", decoded, StringComparison.Ordinal);
        Assert.Contains("ığüşöçİĞÜŞÖÇ", decoded, StringComparison.Ordinal);
        Assert.DoesNotContain('\uFFFD', decoded);

        // ISO-8859-9 agrees with windows-1254 on the Turkish letters, so it decodes the
        // same body to the same text.
        var iso88599 = RealSourceCatalog.Decode(payload.RawBytes, "iso-8859-9");
        Assert.Equal(decoded, iso88599);

        // Negative control: interpreting the raw bytes as UTF-8 produces mojibake.
        var asUtf8 = Encoding.UTF8.GetString(payload.RawBytes);
        Assert.Contains('\uFFFD', asUtf8);
        Assert.DoesNotContain("Şirket", asUtf8, StringComparison.Ordinal);
    }

    /// <summary>TC-MDF-067 — inconsistent KAP XBRL tags collapse onto the canonical code.</summary>
    [Fact]
    public void TC_MDF_067_Kap_xbrl_tag_variance_maps_to_the_canonical_code()
    {
        var facts = RealSourceCatalog.DecodeStatementFacts(RealSourceCatalog.Get(RealSourceCatalog.StatementsAsels));

        var fy2025 = facts.Single(f => f.FiscalYear == 2025 && f.StatementType == "IS");
        var fy2024 = facts.Single(f => f.FiscalYear == 2024 && f.StatementType == "IS");

        // Same canonical line, two different source tags across periods.
        Assert.Contains("ifrs-full:Revenue", RealSourceCatalog.XbrlConceptMap.Keys);
        Assert.Contains("tr:Hasilat", RealSourceCatalog.XbrlConceptMap.Keys);
        Assert.Equal(RealSourceCatalog.XbrlConceptMap["ifrs-full:Revenue"], RealSourceCatalog.XbrlConceptMap["tr:Hasilat"]);
        Assert.Equal(1_234_567.89m, fy2025.Lines["REV"]);
        Assert.Equal(1_100_000.00m, fy2024.Lines["REV"]);

        // GROSS_PROFIT is derived from REV − COGS; an unknown tag is preserved, never dropped.
        Assert.Equal(246_913.57m, fy2025.Lines["GROSS_PROFIT"]);
        var fy2025BalanceSheet = facts.Single(f => f.FiscalYear == 2025 && f.StatementType == "BS");
        Assert.True(fy2025BalanceSheet.Lines.TryGetValue("OTHER_kap:OzkaynakDegisimTablosu", out var passthrough));
        Assert.Equal(42_000.00m, passthrough);
    }

    /// <summary>TC-MDF-068 — one real payload is consumed end-to-end through the harness.</summary>
    [Fact]
    public async Task TC_MDF_068_One_real_payload_consumed_end_to_end_through_the_harness()
    {
        var payload = _wireMock.StubRealSource(RealSourceCatalog.PricesAsels);

        using var http = new HttpClient { BaseAddress = new Uri(_wireMock.BaseUrl) };
        var adapter = new PricesSourceAdapter(
            new HttpSourceClient(http),
            Options.Create(new PricesSourceOptions { BackfillPath = payload.DefaultPath }));

        var fetched = await adapter.FetchBackfillAsync(new DateOnly(2026, 10, 6));

        Assert.Equal(RealSourceCatalog.IsYatirimSource, fetched.SourceRef);
        Assert.Equal(3, fetched.Payload.Rows.Count);

        var asels = fetched.Payload.Rows.Single(r => r.Date == CaptureEnd);
        Assert.Equal("ASELS", asels.Symbol);
        Assert.Equal(348.75m, asels.Close!.Value);
        Assert.Equal(12_941_100_644L, asels.Volume!.Value);
        Assert.Equal(1, _wireMock.RequestsFor(payload.DefaultPath));
    }
}

/// <summary>
/// The real-source counterpart of the FU catalog's <c>StubCannedSource</c> helper: stubs a
/// named <see cref="RealSourcePayload"/> at its committed body and default route, so both
/// catalogs load through the same WireMock harness seam (TKT-foundation-015 acceptance #1).
/// </summary>
internal static class RealSourceWireMockExtensions
{
    public static RealSourcePayload StubRealSource(
        this WireMockFixture wireMock,
        string name,
        string? path = null,
        string? method = null)
    {
        ArgumentNullException.ThrowIfNull(wireMock);

        var payload = RealSourceCatalog.Get(name);
        wireMock.StubJson(
            path ?? payload.DefaultPath,
            payload.Body,
            payload.StatusCode,
            method ?? payload.Method);
        return payload;
    }
}
