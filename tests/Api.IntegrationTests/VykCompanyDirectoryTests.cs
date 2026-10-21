using Degerli.Ingestion.Kap;
using Degerli.Ingestion.Sources;
using Degerli.Ingestion.Statements;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TC-MDF-082 — the VYK <c>/members</c> directory's symbol→<c>companyId</c> map
/// (01 v1.5 §10.9.15, §10.9.11's company mapping, FR-MDF-008). TKT-foundation-046 found the map
/// registering a member's whole <c>stockCode</c> string as <b>one</b> key, so a comma-joined code
/// (<c>"GARAN, TGB"</c>, 77 of the 1037 dev-gateway members) made every such ticker unresolvable
/// and fail-stopped all three family jobs on the first one. These tests pin the split.
/// <para>
/// The double below is <b>committed to the repo</b> (never a <c>/tmp</c> snapshot) and
/// **derived from the live capture** of `https://apigwdev.mkk.com.tr/api/vyk/members`
/// (2026-10-20, 1037 members): the real row shape, the real comma-joined codes (including the exact
/// `"GARAN, TGB"` and `"ALBRK, ALK"` / `"A1CAP, ACP"` pairs observed), the literal duplicate member
/// entries the payload really carries, and the codeless auditor-type members
/// (`YK`/`FK`/`IGS`/`IGMS`). Only the member set is trimmed to the shapes under assertion — no
/// invented field, no invented wrapping.
/// </para>
/// </summary>
public sealed class VykCompanyDirectoryTests
{
    /// <summary>The gateway routes the directory calls (01 v1.5 §10.9.11).</summary>
    private const string MembersPath = "/members";
    private const string AnchorPath = "/lastDisclosureIndex";

    /// <summary>TC-MDF-082 — a comma-joined <c>stockCode</c> resolves <b>every</b> of its tickers;
    /// the joined string itself is never a key.</summary>
    [Fact]
    public async Task TC_MDF_082_comma_joined_stock_codes_resolve_every_ticker()
    {
        var snapshot = await FetchAsync();

        Assert.False(snapshot.Failed);

        // The live shape "GARAN, TGB" → both tickers map to the same companyId.
        Assert.Equal("1002", snapshot.BySymbol["GARAN"]);
        Assert.Equal("1002", snapshot.BySymbol["TGB"]);

        // …and the same for the other observed pairs (a splitter that only handled two parts or
        // left the surrounding whitespace would fail here).
        Assert.Equal("1003", snapshot.BySymbol["ALBRK"]);
        Assert.Equal("1003", snapshot.BySymbol["ALK"]);
        Assert.Equal("1004", snapshot.BySymbol["A1CAP"]);
        Assert.Equal("1004", snapshot.BySymbol["ACP"]);

        // The joined string is not a key: the defect this ticket closes.
        Assert.False(snapshot.BySymbol.ContainsKey("GARAN, TGB"));
        Assert.False(snapshot.BySymbol.ContainsKey("ALBRK, ALK"));

        // A plain single-ticker member still resolves, and the map stays OrdinalIgnoreCase.
        Assert.Equal("1001", snapshot.BySymbol["ALFA"]);
        Assert.Equal("1002", snapshot.BySymbol["garan"]);
    }

    /// <summary>TC-MDF-082 — duplicate member entries are deterministic: an identical duplicate is
    /// idempotent (no warning), a same-code/different-id conflict is <b>last-wins with a logged
    /// warning</b> naming the code and both ids — never silent.</summary>
    [Fact]
    public async Task TC_MDF_082_duplicates_are_last_wins_and_id_conflicts_are_logged()
    {
        var logger = new RecordingLogger<VykCompanyDirectory>();

        var snapshot = await FetchAsync(logger);

        // Two member entries claim AKBNK (the live payload's duplicate-entry class, with different
        // ids): the later entry wins, deterministically.
        Assert.Equal("1006", snapshot.BySymbol["AKBNK"]);

        var conflict = Assert.Single(
            logger.Messages,
            message => message.Contains("AKBNK", StringComparison.Ordinal));
        Assert.Contains("1005", conflict, StringComparison.Ordinal);
        Assert.Contains("1006", conflict, StringComparison.Ordinal);
        Assert.Contains("last-wins", conflict, StringComparison.Ordinal);

        // The literal duplicate of "GARAN, TGB" with the same id is idempotent — nothing logged.
        Assert.DoesNotContain(logger.Messages, message => message.Contains("GARAN", StringComparison.Ordinal));
    }

    /// <summary>TC-MDF-082 — codeless auditor-type members (119 of the live 1037) are skipped: no
    /// empty key, no fabricated mapping, and the resolved map holds exactly the ticker keys.</summary>
    [Fact]
    public async Task TC_MDF_082_codeless_members_are_skipped()
    {
        var snapshot = await FetchAsync();

        Assert.False(snapshot.BySymbol.ContainsKey(string.Empty));
        Assert.DoesNotContain(snapshot.BySymbol.Keys, key => key.Trim().Length == 0);

        // GARAN, TGB, ALBRK, ALK, A1CAP, ACP, ALFA, AKBNK — nothing else (the codeless members and
        // the member without a stockCode property contribute no key).
        Assert.Equal(
            new[] { "A1CAP", "ACP", "AKBNK", "ALBRK", "ALFA", "ALK", "GARAN", "TGB" },
            snapshot.BySymbol.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray());
    }

    // ---------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------

    private static async Task<VykDirectorySnapshot> FetchAsync(ILogger<VykCompanyDirectory>? logger = null)
    {
        var client = new StubKapClient(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [MembersPath] = VykMembersRealShape.Payload,
            [AnchorPath] = """{"lastDisclosureIndex":"1231017"}""",
        });

        var options = Options.Create(new KapSourceOptions
        {
            MembersPath = MembersPath,
            LastDisclosureIndexPath = AnchorPath,
        });
        var pacer = new KapRequestPacer(
            Options.Create(new StatementsSourceOptions { RequestDelayMs = 0 }),
            TimeProvider.System);

        var directory = new VykCompanyDirectory(
            client,
            options,
            pacer,
            logger ?? NullLogger<VykCompanyDirectory>.Instance);

        return await directory.GetAsync();
    }

    /// <summary>A KAP client double serving exactly the two directory routes from memory.</summary>
    private sealed class StubKapClient : IKapSourceClient
    {
        private readonly IReadOnlyDictionary<string, string> _bodies;

        public StubKapClient(IReadOnlyDictionary<string, string> bodies) => _bodies = bodies;

        public Task<string> GetStringAsync(string path, CancellationToken cancellationToken = default) =>
            _bodies.TryGetValue(path, out var body)
                ? Task.FromResult(body)
                : throw new SourceFetchException($"The double serves no body at '{path}'.");
    }

    /// <summary>An <see cref="ILogger{T}"/> that retains the rendered messages for assertion.</summary>
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}

/// <summary>
/// The committed real-shape <c>/members</c> (Şirket Listesi) double for TKT-foundation-046,
/// <b>derived from the live capture</b> of `https://apigwdev.mkk.com.tr/api/vyk/members`
/// (2026-10-20; 1037 members) — the capture itself is an ephemeral <c>/tmp</c> snapshot and is
/// deliberately not referenced: this body lives with the tests.
/// <para>
/// What it reproduces from the capture, verbatim in shape: the row fields
/// (<c>id</c>/<c>title</c>/<c>stockCode</c>/<c>memberType</c>); comma-joined multi-ticker codes —
/// including Türkiye Garanti Bankası's exact <c>"GARAN, TGB"</c>, Albaraka Türk's
/// <c>"ALBRK, ALK"</c> and A1 Capital's <c>"A1CAP, ACP"</c> (77 of the 1037 members carry one);
/// <b>literal duplicate member entries</b> (the payload really repeats rows); the same-code/
/// different-id collision class those duplicates imply; and <b>codeless auditor-type members</b>
/// (<c>YK</c>/<c>FK</c>/<c>IGS</c>, 119 of 1037 — one with an empty <c>stockCode</c>, one with a
/// whitespace-only one, one carrying no <c>stockCode</c> property at all). The member set is
/// trimmed to those shapes; no field or wrapping is invented.
/// </para>
/// </summary>
internal static class VykMembersRealShape
{
    public const string Payload =
        """
        [
          {"id":"1001","title":"ALFA","stockCode":"ALFA","memberType":"stock"},
          {"id":"1002","title":"TÜRKİYE GARANTİ BANKASI A.Ş.","stockCode":"GARAN, TGB","memberType":"stock"},
          {"id":"1003","title":"ALBARAKA TÜRK KATILIM BANKASI A.Ş.","stockCode":"ALBRK, ALK","memberType":"stock"},
          {"id":"1004","title":"A1 CAPITAL MENKUL DEĞERLER A.Ş.","stockCode":"A1CAP, ACP","memberType":"stock"},
          {"id":"1002","title":"TÜRKİYE GARANTİ BANKASI A.Ş.","stockCode":"GARAN, TGB","memberType":"stock"},
          {"id":"1005","title":"AKBANK T.A.Ş.","stockCode":"AKBNK","memberType":"stock"},
          {"id":"1006","title":"AKBANK T.A.Ş.","stockCode":"AKBNK","memberType":"stock"},
          {"id":"2001","title":"BAĞIMSIZ DENETİM KURULUŞU","stockCode":"","memberType":"YK"},
          {"id":"2002","title":"DENETÇİ","stockCode":"   ","memberType":"FK"},
          {"id":"2003","title":"GÖZETİM KURULU","memberType":"IGS"}
        ]
        """;
}
