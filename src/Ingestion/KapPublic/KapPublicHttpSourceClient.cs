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

    private readonly HttpClient _http;
    private readonly KapPublicSourceOptions _options;
    private readonly KapPublicRequestPacer _pacer;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _flight = new(1, 1);

    public KapPublicHttpSourceClient(
        HttpClient http,
        IOptions<KapPublicSourceOptions> options,
        KapPublicRequestPacer pacer,
        TimeProvider clock)
    {
        _http = http;
        _options = options.Value;
        _pacer = pacer;
        _clock = clock;

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
        SendAsync(HttpMethod.Get, path, null, _options.RequestTimeout, cancellationToken);

    /// <inheritdoc />
    public Task<string> PostJsonAsync(string path, string jsonBody, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jsonBody);
        return SendAsync(HttpMethod.Post, path, jsonBody, _options.RequestTimeout, cancellationToken);
    }

    /// <inheritdoc />
    public Task<string> GetDetailStringAsync(string path, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, path, null, _options.DetailFetchTimeout, cancellationToken);

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
            await _pacer.PaceAsync(cancellationToken).ConfigureAwait(false);

            using var request = new HttpRequestMessage(method, KapPublicUri.ToRelativePath(path));
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

            using var timeoutSource = new CancellationTokenSource(timeout, _clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutSource.Token);

            HttpResponseMessage response;
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
