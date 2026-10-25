using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.KapPublic;

/// <summary>
/// The wait primitive the pacer uses. The default is <c>Task.Delay(wait, token)</c>;
/// an L2 harness substitutes a recorder so the <i>requested</i> wait is asserted on a fake
/// clock without real sleeping (TC-MDF-103 (ii)).
/// </summary>
/// <param name="delay">How long the pacer wants to wait.</param>
/// <param name="cancellationToken">The caller's cancellation.</param>
public delegate Task KapPublicDelay(TimeSpan delay, CancellationToken cancellationToken);

/// <summary>
/// Paces the strictly sequential public-site calls of one run (01 v1.5.53 §10.9.19). The
/// subject is a <b>public website</b>, so <b>every</b> request — the robots probe, a
/// byCriteria list, an identity search, each report export — waits at least
/// <see cref="KapPublicSourceOptions.RequestDelayMs"/> after the previous one, on the
/// injected <see cref="TimeProvider"/>. The delay is a real wall-clock timer (a politeness
/// measure, not a scheduling decision), so an L2 harness binding <c>0</c> — or substituting
/// a recording <see cref="KapPublicDelay"/> — keeps runs deterministic.
/// <para>
/// The pacer is scoped to one run (one DI scope), so its <c>_lastCall</c> is per-run and
/// never leaks one lane's pacing onto the VYK client or another job. Serialized by a gate,
/// so concurrent callers (there are none by design — the client is single-flight) cannot
/// both pass.
/// </para>
/// </summary>
public sealed class KapPublicRequestPacer
{
    private readonly KapPublicSourceOptions _options;
    private readonly TimeProvider _clock;
    private readonly KapPublicDelay _delay;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset? _lastCall;

    public KapPublicRequestPacer(
        IOptions<KapPublicSourceOptions> options,
        TimeProvider clock,
        KapPublicDelay? delay = null)
    {
        _options = options.Value;
        _clock = clock;
        _delay = delay ?? ((wait, token) => Task.Delay(wait, token));
    }

    /// <summary>The configured inter-request delay in milliseconds (0 disables the wait).</summary>
    public int DelayMs => _options.RequestDelayMs;

    /// <summary>How many requests have been paced so far (one per issued request).</summary>
    public int PacedRequests { get; private set; }

    /// <summary>
    /// Waits until at least <see cref="DelayMs"/> has elapsed since the previous paced
    /// request, then stamps the request's start (the instant the caller then issues it).
    /// </summary>
    public async Task PaceAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_lastCall is not null && _options.RequestDelayMs > 0)
            {
                var elapsed = _clock.GetUtcNow() - _lastCall.Value;
                var remaining = TimeSpan.FromMilliseconds(_options.RequestDelayMs) - elapsed;
                if (remaining > TimeSpan.Zero)
                {
                    await _delay(remaining, cancellationToken).ConfigureAwait(false);
                }
            }

            _lastCall = _clock.GetUtcNow();
            PacedRequests++;
        }
        finally
        {
            _gate.Release();
        }
    }
}
