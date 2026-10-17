using System.Text;
using Degerli.Api.IntegrationTests.Harness;
using Degerli.Ingestion;
using Degerli.Ingestion.Sources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TKT-foundation-016 acceptance — the production <see cref="ISourceClient"/> honors the
/// response's declared <c>Content-Type; charset=</c> (01 v1.5 §10.9.4, AD-14). The real
/// İş Yatırım/KAP artifacts arrive in the Turkish legacy code pages; the client decodes a
/// declared <c>windows-1254</c> / <c>ISO-8859-9</c> body faithfully and falls back to
/// UTF-8 when no charset is declared (or it is unknown) — the pre-v1.5 behavior. Charset
/// sniffing is deliberately not added; the <b>declared</b> header is the fix. A successful
/// code-page decode also proves the .NET code-pages provider is registered at product
/// startup (without it the encoding is unsupported and the decode would mojibake).
/// </summary>
public sealed class SourceClientCharsetTests : IClassFixture<WireMockFixture>, IDisposable
{
    private const string Turkish = "ığüşöçİĞÜŞÖÇ";
    private const string ExpectedBody = "{\"text\":\"" + Turkish + "\"}";

    // The windows-1254 / ISO-8859-9 byte values of Turkish's 12 letters (the two code
    // pages agree on these code points). Hard-coded so the test does not itself need the
    // code-pages provider to build the fixture bytes — its product-startup registration is
    // exactly what the decode below exercises.
    private static readonly byte[] TurkishCodePageBytes =
        [0xFD, 0xF0, 0xFC, 0xFE, 0xF6, 0xE7, 0xDD, 0xD0, 0xDC, 0xDE, 0xD6, 0xC7];

    // Wrap the legacy-encoded Turkish letters in a JSON envelope (`{"text":"…"}`).
    private static readonly byte[] LegacyArtifactBytes =
    [
        .. Encoding.ASCII.GetBytes("{\"text\":\""),
        .. TurkishCodePageBytes,
        .. Encoding.ASCII.GetBytes("\"}"),
    ];

    private static readonly byte[] Utf8ArtifactBytes = Encoding.UTF8.GetBytes(ExpectedBody);

    // R2-01 (reviews/TKT-foundation-016-round-2.md): the charset-honoring rewrite of
    // DecodeBody dropped the BOM handling HttpContent.ReadAsStringAsync provided — a UTF-8
    // body carrying its EF BB BF preamble decoded with a leading U+FEFF, and a UTF-16 body
    // with no declared charset was decoded as UTF-8 garbage. A real İş Yatırım body may
    // carry a BOM (the first live pull is the lane that would surface it), so these two
    // cases pin the restored behavior: strip the UTF-8 preamble on decode, and sniff a
    // UTF-16 BOM when no charset is declared.
    private static readonly byte[] Utf8BomArtifactBytes =
    [
        .. Encoding.UTF8.GetPreamble(),
        .. Encoding.UTF8.GetBytes(ExpectedBody),
    ];

    private static readonly byte[] Utf16BomArtifactBytes =
    [
        .. Encoding.Unicode.GetPreamble(),
        .. Encoding.Unicode.GetBytes(ExpectedBody),
    ];

    private readonly WireMockFixture _wireMock;
    private readonly ServiceProvider _provider;
    private readonly ISourceClient _client;

    public SourceClientCharsetTests(WireMockFixture wireMock)
    {
        _wireMock = wireMock;

        // Compose the source client through the same AddMarketDataIngestion registration
        // the API host uses, so the code-pages provider registration the decode depends on
        // is the product-startup one (AD-14), not a test-local registration.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ingestion:Prices:BaseUrl"] = wireMock.BaseUrl,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddMarketDataIngestion(configuration);
        _provider = services.BuildServiceProvider();
        _client = _provider.GetRequiredService<ISourceClient>();
    }

    public void Dispose() => _provider.Dispose();

    /// <summary>
    /// TC-MDF-066 — a declared Turkish legacy code page (<c>windows-1254</c> or its
    /// <c>ISO-8859-9</c> twin) decodes the Turkish letters without mojibake and no
    /// U+FFFD, proving both the declared-charset honoring and the product startup
    /// code-pages provider registration.
    /// </summary>
    [Theory]
    [InlineData("windows-1254")]
    [InlineData("ISO-8859-9")]
    public async Task TC_MDF_066_Declared_turkish_codepage_charset_decodes_faithfully(string charset)
    {
        var path = $"/charset/declared-{charset}";
        _wireMock.StubBytes(path, LegacyArtifactBytes, $"application/json; charset={charset}");

        var decoded = await _client.GetStringAsync(path);

        Assert.Equal(ExpectedBody, decoded);
        Assert.Contains(Turkish, decoded, StringComparison.Ordinal);
        Assert.DoesNotContain('\uFFFD', decoded);
    }

    /// <summary>
    /// TC-MDF-066 — the two Turkish code pages agree on these letters, so the identical
    /// raw bytes decode to the identical text under either declared charset.
    /// </summary>
    [Fact]
    public async Task TC_MDF_066_Windows1254_and_iso88599_decode_identically()
    {
        _wireMock.StubBytes("/charset/agree-1254", LegacyArtifactBytes, "application/json; charset=windows-1254");
        _wireMock.StubBytes("/charset/agree-8859-9", LegacyArtifactBytes, "application/json; charset=ISO-8859-9");

        var viaWindows1254 = await _client.GetStringAsync("/charset/agree-1254");
        var viaIso88599 = await _client.GetStringAsync("/charset/agree-8859-9");

        Assert.Equal(viaWindows1254, viaIso88599);
    }

    /// <summary>
    /// TC-MDF-066 — no charset declaration, or a charset the client does not know, falls
    /// back to UTF-8 (the pre-v1.5 behavior). For the legacy bytes served this way the
    /// decode contains replacement characters — the honored header is the fix, and
    /// charset sniffing is deliberately not added (01 v1.5 §10.9.4).
    /// </summary>
    [Theory]
    [InlineData("absent", "application/json")]
    [InlineData("unknown", "application/json; charset=x-unknown")]
    public async Task TC_MDF_066_Absent_or_unknown_charset_falls_back_to_utf8(string label, string contentType)
    {
        var path = $"/charset/fallback-{label}";
        _wireMock.StubBytes(path, LegacyArtifactBytes, contentType);

        var decoded = await _client.GetStringAsync(path);

        // UTF-8 default: the legacy bytes are not valid UTF-8, so replacement characters appear.
        Assert.Contains('\uFFFD', decoded);
        Assert.DoesNotContain(Turkish, decoded, StringComparison.Ordinal);
    }

    /// <summary>
    /// TC-MDF-066 — a UTF-8 body decodes correctly both when the charset is declared and
    /// when it is absent (the UTF-8 default), leaving the current behavior intact.
    /// </summary>
    [Theory]
    [InlineData("utf8-lower", "application/json; charset=utf-8")]
    [InlineData("utf8-upper", "application/json; charset=UTF-8")]
    [InlineData("utf8-none", "application/json")]
    public async Task TC_MDF_066_Utf8_body_decodes_with_or_without_declaration(string label, string contentType)
    {
        var path = $"/charset/{label}";
        _wireMock.StubBytes(path, Utf8ArtifactBytes, contentType);

        var decoded = await _client.GetStringAsync(path);

        Assert.Equal(ExpectedBody, decoded);
        Assert.DoesNotContain('\uFFFD', decoded);
    }

    /// <summary>
    /// TC-MDF-066 (R2-01) — a UTF-8 body served with its EF BB BF preamble decodes to the
    /// same text as the BOM-less body: the leading U+FEFF is stripped (the behavior
    /// <c>ReadAsStringAsync</c> had). Without the strip the JSON envelope would open with a
    /// stray U+FEFF and every downstream parse would fail.
    /// </summary>
    [Fact]
    public async Task TC_MDF_066_Utf8_bom_body_decodes_without_a_leading_bom()
    {
        var path = "/charset/utf8-bom";
        _wireMock.StubBytes(path, Utf8BomArtifactBytes, "application/json; charset=utf-8");

        var decoded = await _client.GetStringAsync(path);

        Assert.Equal(ExpectedBody, decoded);
        Assert.DoesNotContain('\uFEFF', decoded);
        Assert.DoesNotContain('\uFFFD', decoded);
    }

    /// <summary>
    /// TC-MDF-066 (R2-01) — a UTF-16 LE BOM'd body served with <b>no</b> declared charset is
    /// sniffed from its BOM and decoded as UTF-16 (the behavior <c>ReadAsStringAsync</c> had).
    /// The UTF-8 default would render it as garbage; the sniff recovers the Turkish text.
    /// </summary>
    [Fact]
    public async Task TC_MDF_066_Utf16_bom_no_charset_body_is_sniffed_and_decoded()
    {
        var path = "/charset/utf16-bom-nocs";
        _wireMock.StubBytes(path, Utf16BomArtifactBytes, "application/json");

        var decoded = await _client.GetStringAsync(path);

        Assert.Equal(ExpectedBody, decoded);
        Assert.Contains(Turkish, decoded, StringComparison.Ordinal);
        Assert.DoesNotContain('\uFFFD', decoded);
    }
}
