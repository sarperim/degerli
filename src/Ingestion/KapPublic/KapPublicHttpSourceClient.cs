using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;
using Degerli.Ingestion.Sources;
using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.KapPublic;

/// <summary>
/// The recorded access posture does not permit automated reads (01 v1.5.53 §10.9.19, the
/// OQ-2 gate): the client throws this <b>before</b> issuing any request, so the gate is
/// checkable behaviour rather than a note someone may ignore. It is deliberately <b>not</b>
/// a <see cref="SourceFetchException"/>: this is a legal/configuration stop, never a source
/// failure to be retried or counted as a down source.
/// </summary>
public sealed class KapPublicAccessRefusedException : Exception
{
    public KapPublicAccessRefusedException(
        string message,
        string reasonCode = KapPublicReasonCodes.AutomatedAccessForbidden) : base(message)
    {
        ReasonCode = reasonCode;
    }

    /// <summary>The named reason code recorded with the refusal.</summary>
    public string ReasonCode { get; }
}

/// <summary>
/// One request exceeded its per-item timeout (the detail/report bound or the general
/// request bound). It is a <b>source-down</b> class with its own reason code — the local
/// deadline expired, the source did not answer.
/// </summary>
public sealed class KapPublicSourceTimeoutException : Exception
{
    public KapPublicSourceTimeoutException(string message) : base(message) { }
}

/// <summary>
/// The source's <b>request quota</b> answered <c>429</c> (TKT-foundation-100, 01 v1.5.77
/// §10.9.19). Two outcomes share this type because they are one story told honestly:
/// <list type="bullet">
/// <item><b>rate-limited</b> — the lane waited its one bounded backoff, re-issued exactly once and
/// was still refused (<see cref="KapPublicReasonCodes.QuotaRateLimited"/>): a named, counted
/// outcome, never a retry ladder;</item>
/// <item><b>deferred</b> — the wait the source demanded is <b>not shorter</b> than this request's
/// own per-item bound, so the lane does not sleep past its caller: the walk truncates
/// (<see cref="KapPublicReasonCodes.WalkQuotaBackoff"/>) at its last completed unit and the next
/// pass resumes there.</item>
/// </list>
/// <para>
/// The quota is <b>authoritative</b>: this type is how the lane obeys it. Nothing here retries in
/// parallel, rotates an identity or otherwise tries to get around the limit.
/// </para>
/// </summary>
public sealed class KapPublicQuotaException : Exception
{
    public KapPublicQuotaException(
        string message,
        string reasonCode,
        TimeSpan requiredWait,
        bool deferred) : base(message)
    {
        ReasonCode = reasonCode;
        RequiredWait = requiredWait;
        Deferred = deferred;
    }

    /// <summary>The named reason (<see cref="KapPublicReasonCodes"/>).</summary>
    public string ReasonCode { get; }

    /// <summary>The wait the source demanded (the <c>Retry-After</c> value, or the fixed backoff).</summary>
    public TimeSpan RequiredWait { get; }

    /// <summary>True when the demanded wait did not fit inside the caller's per-item bound.</summary>
    public bool Deferred { get; }
}

/// <summary>
/// Builds the lane's <b>browser-shaped</b> transport (TKT-foundation-100, 01 v1.5.77 §10.9.19).
/// The <c>2026-10-28</c> finding: after a burst the edge refused the .NET <c>HttpClient</c> at the
/// <b>transport</b> level (connection reset / timeout) while curl and python urllib succeeded from
/// the same host and even the same container — an escalation of the 087/091 User-Agent finding to
/// the <b>TLS ClientHello fingerprint</b>. The handler therefore presents a shape the edge accepts:
/// a TLS 1.2+ floor, a browser-ordered cipher-suite policy (where the platform allows one) and the
/// browser's ALPN offer.
/// <para>
/// <b>An honest fingerprint, not a disguise.</b> The User-Agent (<see
/// cref="KapPublicSourceOptions.DefaultUserAgent"/>) is unchanged, no header lies, and the lane
/// stays single-flight, paced and quota-obedient. .NET cannot reproduce a browser's ClientHello
/// byte for byte — the shape is pinned by configuration and confirmed by the orchestrator's live
/// probe, never by a claim in CI.
/// </para>
/// </summary>
public static class KapPublicHttpHandlerFactory
{
    /// <summary>
    /// The lane's <see cref="SocketsHttpHandler"/>: a browser-shaped TLS <b>ClientHello</b> when
    /// <see cref="KapPublicSourceOptions.TlsShapeEnabled"/> is on, and the framework's own stack
    /// (the pre-ticket behaviour) when it is off.
    /// </summary>
    public static SocketsHttpHandler Create(KapPublicSourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var handler = new SocketsHttpHandler
        {
            // Bounded reuse, like a browser's few long-lived connections: a rotated edge
            // certificate or a closed idle socket must not pin the lane to a dead connection, and
            // an unbounded pool would grow across a long pass.
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            UseCookies = false,
        };

        if (options.TlsShapeEnabled)
        {
            handler.SslOptions = CreateSslOptions(options);
        }

        return handler;
    }

    /// <summary>
    /// The TLS shape the handler presents (see <see cref="Create"/>): the configured floor (as an
    /// enabled set — the named protocol and everything above it), the configured ALPN offer, and
    /// the configured browser-ordered cipher-suite policy.
    /// <para>
    /// <see cref="System.Net.Security.SslClientAuthenticationOptions.CipherSuitesPolicy"/> is a
    /// <b>platform capability</b>: .NET supports it on Linux, throws
    /// <see cref="PlatformNotSupportedException"/> where the platform has no notion of a
    /// client-side suite order, and throws <see cref="ArgumentException"/> when the platform knows
    /// the suites but refuses to configure this set. Both are a platform refusal, not a
    /// configuration error to crash the lane on. Where the platform refuses it, the property stays
    /// unset and the platform's own order stands — recorded honestly in the config equivalence the
    /// tests pin, never faked by an order nobody presented.
    /// </para>
    /// </summary>
    public static SslClientAuthenticationOptions CreateSslOptions(KapPublicSourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var ssl = new SslClientAuthenticationOptions
        {
            EnabledSslProtocols = ParseMinimumTlsVersion(options.TlsMinimumVersion),
            ApplicationProtocols = ParseAlpn(options.TlsAlpnProtocols),
        };

        var suites = ParseCipherSuites(options.TlsCipherSuites);
        if (suites.Count > 0 && !OperatingSystem.IsWindows())
        {
            try
            {
                ssl.CipherSuitesPolicy = new CipherSuitesPolicy(suites);
            }
            catch (Exception exception) when (exception is PlatformNotSupportedException or ArgumentException)
            {
                // The platform does not let a client choose its cipher-suite order (or refuses this
                // recognised set): the property stays unset and the platform default stands
                // (documented on this method). A configuration typo therefore never widens the
                // offer and never takes the lane down at construction.
            }
        }

        return ssl;
    }

    /// <summary>
    /// The TLS floor as an enabled <b>set</b>: the named protocol plus every protocol above it
    /// (<c>Tls12</c> → TLS 1.2 + 1.3). TLS 1.0/1.1 are not offered by any value, and anything
    /// unparseable falls back to the shipped floor — a configuration typo never widens the offer.
    /// </summary>
    public static SslProtocols ParseMinimumTlsVersion(string? value)
    {
        foreach (var token in Tokenize(value))
        {
            if (Enum.TryParse<SslProtocols>(token, ignoreCase: true, out var parsed) &&
                parsed == SslProtocols.Tls13)
            {
                return SslProtocols.Tls13;
            }
        }

        // The shipped floor is TLS 1.2 as a minimum: 1.2 *or better*, never "1.2 only" and never
        // anything below it.
        return SslProtocols.Tls12 | SslProtocols.Tls13;
    }

    /// <summary>The ALPN offer, in the configured order (<c>h2</c>, <c>http/1.1</c>); empty when unset.</summary>
    public static List<SslApplicationProtocol> ParseAlpn(string? value)
    {
        var protocols = new List<SslApplicationProtocol>();
        foreach (var token in Tokenize(value))
        {
            if (string.Equals(token, "h2", StringComparison.OrdinalIgnoreCase))
            {
                protocols.Add(SslApplicationProtocol.Http2);
            }
            else if (string.Equals(token, "http/1.1", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(token, "http1.1", StringComparison.OrdinalIgnoreCase))
            {
                protocols.Add(SslApplicationProtocol.Http11);
            }
        }

        return protocols;
    }

    /// <summary>
    /// The cipher-suite order, in the configured order. An unrecognised name is skipped (the
    /// remaining, recognised order still ships) rather than silently reordered.
    /// </summary>
    public static List<TlsCipherSuite> ParseCipherSuites(string? value)
    {
        var suites = new List<TlsCipherSuite>();
        foreach (var token in Tokenize(value))
        {
            if (Enum.TryParse<TlsCipherSuite>(token, ignoreCase: true, out var suite))
            {
                suites.Add(suite);
            }
        }

        return suites;
    }

    private static IEnumerable<string> Tokenize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>
/// The HTTP seam of the KAP <b>public site</b> (01 v1.5.53 §10.9.19, TKT-foundation-079) —
/// a separate source from the VYK gateway (<see cref="Kap.IKapSourceClient"/>), with its own
/// base address, its own pacing and its own DI registration. Three shapes are needed by the
/// lane: a plain <c>GET</c> (robots probe), a <c>POST</c> with a JSON body (the byCriteria
/// list and the identity search — neither exists on the VYK client) and a detail
/// <c>GET</c> carrying the per-item timeout (the ~1.8 MB report export).
/// </summary>
public interface IKapPublicSourceClient
{
    /// <summary>The lane's provenance name — always <c>kap-public</c>, never <c>kap</c>.</summary>
    string SourceName { get; }

    /// <summary>A paced <c>GET</c>; throws <see cref="SourceFetchException"/> on a non-success response.</summary>
    Task<string> GetStringAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>A paced <c>POST</c> with a JSON body; throws <see cref="SourceFetchException"/> on a non-success response.</summary>
    Task<string> PostJsonAsync(string path, string jsonBody, CancellationToken cancellationToken = default);

    /// <summary>
    /// A paced detail <c>GET</c> bounded by <see cref="KapPublicSourceOptions.DetailFetchTimeout"/>
    /// (the pacing is <b>not</b> counted against that bound: the client paces, then starts the
    /// clock). A timeout surfaces as <see cref="KapPublicSourceTimeoutException"/>.
    /// </summary>
    Task<string> GetDetailStringAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>
/// The default <see cref="IKapPublicSourceClient"/>. It mirrors
/// <see cref="Kap.KapHttpSourceClient"/>'s discipline — a non-success status carries a
/// bounded, verbatim body excerpt on <see cref="SourceFetchException"/> so the caller can
/// classify it — and adds the three properties this source needs beyond that client:
/// <list type="bullet">
/// <item><b>POST</b> with a JSON body (the byCriteria list and the identity search are
/// POST-only: a <c>GET</c> answers <c>405</c> — probe record in
/// <c>.pipeline/plan/run-report.md</c>, ~07:00 UTC 2026-10-25: <c>GET</c> → 405, <c>POST</c>
/// with a body → 200 <c>[]</c>);</item>
/// <item>the <b>User-Agent</b> on every request (the public site expects a browser-shaped
/// client, and the lane identifies itself honestly);</item>
/// <item><b>strict single-flight</b>: one gate is held across the whole request/read, so no
/// two requests are ever in flight together — the "never parallel" clause is enforced here,
/// not left to the caller's discipline.</item>
/// </list>
/// <para>
/// The client applies <b>no</b> content-type trust: the body is returned verbatim and the
/// caller detects the shape (the report route declares <c>application/vnd.ms-excel</c> and
/// serves HTML).
/// </para>
/// </summary>
public sealed class KapPublicHttpSourceClient : IKapPublicSourceClient
{
    /// <summary>The maximum number of response-body bytes carried on a failure/timeout.</summary>
    public const int MaxErrorBodyBytes = 2048;

    /// <summary>
    /// How many times one logical request is sent when the source answers <c>429</c>: the first
    /// attempt plus <b>exactly one</b> bounded re-issue (TKT-foundation-100). A third attempt would
    /// be the retry ladder the quota finding forbids.
    /// </summary>
    public const int MaxQuotaAttempts = 2;

    private readonly HttpClient _http;
    private readonly KapPublicSourceOptions _options;
    private readonly KapPublicRequestPacer _pacer;
    private readonly TimeProvider _clock;
    private readonly KapPublicDelay _quotaDelay;
    private readonly SemaphoreSlim _flight = new(1, 1);

    public KapPublicHttpSourceClient(
        HttpClient http,
        IOptions<KapPublicSourceOptions> options,
        KapPublicRequestPacer pacer,
        TimeProvider clock,
        KapPublicDelay? quotaDelay = null)
    {
        _http = http;
        _options = options.Value;
        _pacer = pacer;
        _clock = clock;
        _quotaDelay = quotaDelay ?? ((wait, token) => Task.Delay(wait, token));

        // The base is the site root; the request paths are root-relative routes on it. The
        // lane-local join keeps a configured path prefix (a reverse proxy in front of a
        // replayed capture) intact without borrowing a VYK type.
        if (_http.BaseAddress is not null)
        {
            _http.BaseAddress = KapPublicUri.WithDirectorySeparator(_http.BaseAddress);
        }
    }

    /// <inheritdoc />
    public string SourceName => KapPublicSourceOptions.SourceName;

    /// <inheritdoc />
    public Task<string> GetStringAsync(string path, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, path, null, _options.RequestTimeout, KapPublicRequestKind.General, cancellationToken);

    /// <inheritdoc />
    public Task<string> PostJsonAsync(string path, string jsonBody, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jsonBody);
        return SendAsync(
            HttpMethod.Post,
            path,
            jsonBody,
            _options.RequestTimeout,
            KapPublicRequestKind.General,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<string> GetDetailStringAsync(string path, CancellationToken cancellationToken = default) =>
        SendAsync(
            HttpMethod.Get,
            path,
            null,
            _options.DetailFetchTimeout,
            KapPublicRequestKind.Export,
            cancellationToken);

    /// <summary>
    /// The refusal gate (TC-MDF-104 (iii)): called before <b>every</b> request, and before the
    /// flight gate is taken, so a non-permitting posture issues nothing at all. Two postures
    /// refuse, each with its own reason: <see cref="KapPublicAccessPosture.Forbidden"/>
    /// (<c>AUTOMATED_ACCESS_FORBIDDEN</c>) and the code-default
    /// <see cref="KapPublicAccessPosture.NotVerified"/> (<c>ACCESS_POSTURE_NOT_VERIFIED</c>) —
    /// a posture nobody chose is not a permission.
    /// </summary>
    private void EnsureAccessPermitted()
    {
        if (_options.AutomatedAccess == KapPublicAccessPosture.Allowed)
        {
            return;
        }

        var reasonCode = _options.AutomatedAccess == KapPublicAccessPosture.Forbidden
            ? KapPublicReasonCodes.AutomatedAccessForbidden
            : KapPublicReasonCodes.AccessPostureNotVerified;
        throw new KapPublicAccessRefusedException(
            $"The recorded access posture for '{_http.BaseAddress}' is '{_options.AutomatedAccess}': " +
            $"{reasonCode} — no automated request is issued (01 v1.5.53 §10.9.19).",
            reasonCode);
    }

    private async Task<string> SendAsync(
        HttpMethod method,
        string path,
        string? jsonBody,
        TimeSpan timeout,
        KapPublicRequestKind kind,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        EnsureAccessPermitted();

        // Strict single-flight: the whole request/read is serialized, so two requests can
        // never overlap (the "never parallel against kap.org.tr" clause).
        await _flight.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Pace first, then start the per-item timeout: pacing is politeness, not latency.
            // The gap is the one this request's own kind owes — the general pace, or the export
            // route's quota floor (TKT-foundation-100).
            await _pacer.PaceAsync(kind, cancellationToken).ConfigureAwait(false);

            using var timeoutSource = new CancellationTokenSource(timeout, _clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutSource.Token);

            HttpResponseMessage response;
            for (var attempt = 1; ; attempt++)
            {
                if (attempt > 1)
                {
                    // Re-pace before a re-issue (TKT-foundation-100 review round 2, DR-3): the
                    // quota's single re-issue is a *second* request to the same website, so it
                    // owes the same start-to-start gap as any other. Sleeping the quota wait alone
                    // is not enough — a `Retry-After: 0` (or a date already past) maps to a zero
                    // wait, and the re-issue would then go out back-to-back.
                    try
                    {
                        await _pacer.PaceAsync(kind, linked.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new KapPublicSourceTimeoutException(
                            $"Public-site request '{method} {path}' timed out after {timeout.TotalSeconds:0.###} s " +
                            $"while re-pacing the quota re-issue ({KapPublicReasonCodes.RequestTimeout}).");
                    }
                }

                // The instant this attempt starts — after its pacing, because politeness is not
                // latency. The per-item bound is spent from here, so the quota check below compares
                // the demanded wait with what is actually *left* of the caller's bound (review
                // round 2, DR-9), not with the whole bound a slow attempt has already eaten into.
                var attemptStart = _clock.GetUtcNow();

                using var request = BuildRequest(method, path, jsonBody);

                try
                {
                    // Buffered completion on purpose: a headers-only read would put the body
                    // outside the per-item bound and let a site that sends headers then stalls
                    // hang the walk.
                    response = await _http
                        .SendAsync(request, HttpCompletionOption.ResponseContentRead, linked.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new KapPublicSourceTimeoutException(
                        $"Public-site request '{method} {path}' timed out after {timeout.TotalSeconds:0.###} s " +
                        $"({KapPublicReasonCodes.RequestTimeout}).");
                }
                catch (HttpRequestException exception)
                {
                    throw new SourceFetchException(
                        $"Public-site request '{method} {path}' failed in transport: {exception.Message}",
                        statusCode: null,
                        body: null,
                        exception);
                }

                if (response.StatusCode != HttpStatusCode.TooManyRequests)
                {
                    break;
                }

                // A 429 is the source's *quota* answering (TKT-foundation-100, 01 v1.5.77
                // §10.9.19): a named outcome, never a silent retry ladder. The wait is the
                // source's own Retry-After when it sends one, and the conservative fixed backoff
                // when it does not (the observed answer: empty body, no header).
                var wait = RequiredQuotaWait(response);
                var quota = $"Public-site request '{method} {path}' answered HTTP 429 (Request Limit Exceeded)";

                // What is left of the caller's per-item bound at this instant. On a fast attempt
                // this is the whole bound; a slow attempt has already spent part of it.
                var remaining = timeout - (_clock.GetUtcNow() - attemptStart);
                if (remaining < TimeSpan.Zero)
                {
                    remaining = TimeSpan.Zero;
                }

                if (wait >= remaining)
                {
                    // The demanded wait would outlive this request's own caller: sleeping is
                    // useless (the caller's bound expires first) and stacking waits is worse. Say
                    // so and stop — the walk truncates honestly and the next pass resumes.
                    response.Dispose();
                    throw new KapPublicQuotaException(
                        $"{quota}; the source asked for a {wait.TotalSeconds:0.###} s wait, which is not shorter " +
                        $"than the {remaining.TotalSeconds:0.###} s left of this request's " +
                        $"{timeout.TotalSeconds:0.###} s per-item bound, so the lane does not sleep past its " +
                        $"caller ({KapPublicReasonCodes.WalkQuotaBackoff}).",
                        KapPublicReasonCodes.WalkQuotaBackoff,
                        wait,
                        deferred: true);
                }

                if (attempt >= MaxQuotaAttempts)
                {
                    response.Dispose();
                    throw new KapPublicQuotaException(
                        $"{quota} again after one bounded {wait.TotalSeconds:0.###} s wait — no retry ladder " +
                        $"({KapPublicReasonCodes.QuotaRateLimited}).",
                        KapPublicReasonCodes.QuotaRateLimited,
                        wait,
                        deferred: false);
                }

                response.Dispose();
                try
                {
                    await _quotaDelay(wait, linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new KapPublicSourceTimeoutException(
                        $"Public-site request '{method} {path}' timed out after {timeout.TotalSeconds:0.###} s " +
                        $"while honoring the source's {wait.TotalSeconds:0.###} s quota backoff " +
                        $"({KapPublicReasonCodes.RequestTimeout}).");
                }
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new SourceFetchException(
                        $"Public-site request '{method} {path}' returned HTTP {(int)response.StatusCode}.",
                        (int)response.StatusCode,
                        await ReadBodyExcerptAsync(response, cancellationToken).ConfigureAwait(false));
                }

                try
                {
                    return await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new KapPublicSourceTimeoutException(
                        $"Public-site response body for '{method} {path}' timed out after " +
                        $"{timeout.TotalSeconds:0.###} s ({KapPublicReasonCodes.RequestTimeout}).");
                }
            }
        }
        finally
        {
            _flight.Release();
        }
    }

    /// <summary>
    /// The wait a <c>429</c> demands: the <c>Retry-After</c> the source sent, <b>verbatim</b>
    /// (delta-seconds or an HTTP date), or — the observed case, where the header is absent — the
    /// configured conservative fixed backoff. A header the framework could not read is treated as
    /// absent: the lane guesses nothing.
    /// </summary>
    private TimeSpan RequiredQuotaWait(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
        {
            return delta > TimeSpan.Zero ? delta : TimeSpan.Zero;
        }

        if (retryAfter?.Date is { } date)
        {
            var untilDate = date - _clock.GetUtcNow();
            return untilDate > TimeSpan.Zero ? untilDate : TimeSpan.Zero;
        }

        return _options.QuotaBackoff;
    }

    /// <summary>
    /// Builds one attempt's request. A fresh <see cref="HttpRequestMessage"/> per attempt: a
    /// message cannot be sent twice (the framework throws), and the quota's single re-issue is a
    /// genuine second request.
    /// </summary>
    private HttpRequestMessage BuildRequest(HttpMethod method, string path, string? jsonBody)
    {
        var request = new HttpRequestMessage(method, KapPublicUri.ToRelativePath(path));
        if (jsonBody is not null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        request.Headers.Accept.ParseAdd("application/json, text/plain, */*");
        if (!string.IsNullOrWhiteSpace(_options.UserAgent))
        {
            request.Headers.UserAgent.Clear();
            request.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent);
        }

        return request;
    }


    /// <summary>
    /// Reads at most <see cref="MaxErrorBodyBytes"/> of a non-success body (UTF-8) — the
    /// verbatim excerpt a quarantine or a failure record carries. A body that cannot be read
    /// yields <c>null</c>; the status alone still fails the read.
    /// </summary>
    private static async Task<string?> ReadBodyExcerptAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var buffer = new byte[MaxErrorBodyBytes];
            var read = 0;
            while (read < buffer.Length)
            {
                var chunk = await stream.ReadAsync(
                    buffer.AsMemory(read, buffer.Length - read), cancellationToken).ConfigureAwait(false);
                if (chunk == 0)
                {
                    break;
                }

                read += chunk;
            }

            return Encoding.UTF8.GetString(buffer, 0, read);
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException or ObjectDisposedException)
        {
            return null;
        }
    }
}
