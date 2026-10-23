using Degerli.Ingestion.Intraday;
using Degerli.Ingestion.Sources;
using Microsoft.Extensions.Options;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// Chore(intraday-chart-feed) — the İş Yatırım <b>delayed intraday chart feed</b> decoder
/// (<c>ChartData.aspx/IntradayDelay</c>, payload <c>{"data":[[epochMs, price], …]}</c>), which
/// the 2026-10-23 probe found serving the live session while the daily <c>HisseTekil</c>
/// resource still had no same-day row. No live call here: the bodies below are the captured
/// probe payload, trimmed to a handful of bars.
/// <para>
/// These are pure decoder cases against the production <see cref="IntradayQuoteSourceAdapter"/>
/// through a static <see cref="ISourceClient"/> double — no database, host or network. The
/// existing İş <c>HGDG_*</c> / Yahoo <c>chart</c> envelopes are pinned by
/// <see cref="IntradayQuotesTests"/> and are untouched by this branch.
/// </para>
/// </summary>
public sealed class IntradayChartFeedDecoderTests
{
    private const string IntradayPath =
        "/_layouts/15/IsYatirim.Website/Common/ChartData.aspx/IntradayDelay?period=5&code={symbol}&last=108";

    private static readonly DateOnly Session = new(2026, 10, 23);
    private static readonly DateOnly PriorSession = new(2026, 10, 22);

    /// <summary>
    /// The captured ASELS body (probe 2026-10-23, <c>last=108</c>) trimmed to five bars that
    /// span <b>two</b> sessions: 2026-10-22 (last bar 338.25) and 2026-10-23 (last bar, epoch
    /// <c>1792767600000</c> = 2026-10-23T15:00:00Z, price 338.00). The root <c>timestamp</c>
    /// field is the source's wall clock — transport metadata, never the quote time.
    /// </summary>
    private const string CapturedTwoSessionPayload = """
        {
          "data": [
            [1792677900000, 337.75],
            [1792681200000, 338.25],
            [1792738800000, 330.25],
            [1792767300000, 337.25],
            [1792767600000, 338.00]
          ],
          "timestamp": "2026-10-23T23:19:50.2032269+03:00"
        }
        """;

    [Fact]
    public async Task Chore_chart_feed_derives_the_session_date_from_the_bar_stamp_and_reads_its_last_bar()
    {
        var adapter = Build(CapturedTwoSessionPayload, out var client);

        var fetched = await adapter.FetchSymbolAsync("ASELS", Session);

        // The configured route carries the ticker in place — no query-string fallback appended.
        Assert.Equal(
            "/_layouts/15/IsYatirim.Website/Common/ChartData.aspx/IntradayDelay?period=5&code=ASELS&last=108",
            client.RequestedPath);
        // The same source and host as the daily envelope: one truthful provenance stamp.
        Assert.Equal("isyatirim", fetched.SourceRef);
        Assert.Equal("isyatirim", fetched.Payload.SourceRef);

        var reading = Assert.Single(fetched.Payload.Items);
        // (a) The session date comes from the bar's own epoch-millisecond stamp
        // (1792767600000 → 2026-10-23T15:00:00Z), never from the request or a wall clock.
        Assert.Equal(Session, reading.SessionDate);
        // (b) The last bar of the *latest* session in the payload — 338.00, not yesterday's
        // session-closing 338.25 and not the earlier 337.25 bar of today.
        Assert.Equal(338.00m, reading.LastPrice);
        // The feed carries no volume: the honest gap is NULL, never a price-derived count.
        Assert.Null(reading.DayVolume);
    }

    [Fact]
    public async Task Chore_chart_feed_with_only_a_prior_session_is_an_honest_empty_for_today()
    {
        // The daily-then-chart rollout can leave the rolling window on yesterday's session at
        // the open: yesterday's final bar must not be stamped with today's date.
        const string priorSessionOnly = """
            { "data": [[1792677900000, 337.75], [1792681200000, 338.25]] }
            """;
        var adapter = Build(priorSessionOnly, out _);

        var fetched = await adapter.FetchSymbolAsync("ASELS", Session);

        Assert.Equal("isyatirim", fetched.SourceRef);
        Assert.Empty(fetched.Payload.Items);
        Assert.Equal(Session, fetched.Payload.SessionDate);
    }

    [Fact]
    public async Task Chore_chart_feed_whose_latest_session_is_not_the_swept_session_is_an_honest_empty()
    {
        // A Saturday sweep (no session) over a payload whose newest bar is Friday's close is
        // no reading for Saturday — honest empty rather than a fabricated session.
        var adapter = Build(CapturedTwoSessionPayload, out _);

        var fetched = await adapter.FetchSymbolAsync("ASELS", Session.AddDays(1));

        Assert.Empty(fetched.Payload.Items);
    }

    [Theory]
    [InlineData("""{ "data": [] }""")]
    [InlineData("""{ "data": "not-an-array" }""")]
    [InlineData("""{ "data": [["oops", 1], [1792767600000], [1792767600000, "nope"], [1792767600000, null]] }""")]
    public async Task Chore_malformed_or_empty_chart_feed_data_is_an_honest_empty_not_a_crash(string body)
    {
        var adapter = Build(body, out _);

        var fetched = await adapter.FetchSymbolAsync("ASELS", Session);

        Assert.Equal("isyatirim", fetched.SourceRef);
        Assert.Empty(fetched.Payload.Items);
    }

    private static IntradayQuoteSourceAdapter Build(string body, out StaticSourceClient client)
    {
        client = new StaticSourceClient(body);
        return new IntradayQuoteSourceAdapter(
            client,
            Options.Create(new IntradayQuoteSourceOptions { Path = IntradayPath }));
    }

    /// <summary>A source-client double that always serves one captured body.</summary>
    private sealed class StaticSourceClient : ISourceClient
    {
        private readonly string _body;

        public StaticSourceClient(string body) => _body = body;

        public string? RequestedPath { get; private set; }

        public Task<string> GetStringAsync(string path, CancellationToken cancellationToken = default)
        {
            RequestedPath = path;
            return Task.FromResult(_body);
        }
    }
}
