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
/// The two pacing paths of the lane (TKT-foundation-100, 01 v1.5.77 §10.9.19). The public site
/// is one website with two very different cost profiles: the <b>general</b> path (the robots
/// probe, a byCriteria list, an identity search — a few KiB of JSON) keeps the shipped 3 s
/// politeness pace, while the <b>export</b> path (the ~1.8 MB excel report, whose route carries
/// a rolling per-IP request quota) takes the longer quota floor. The distinction is a property of
/// the request, never of the caller's discipline.
/// </summary>
public enum KapPublicRequestKind
{
    /// <summary>A list/identity/probe request — the shipped general pace applies.</summary>
    General = 0,

    /// <summary>An excel export/detail request — the export quota floor applies (raises the general pace).</summary>
    Export = 1,
}

/// <summary>
/// Paces the strictly sequential public-site calls of one run (01 v1.5.53 §10.9.19). The
/// subject is a <b>public website</b>, so <b>every</b> request — the robots probe, a
/// byCriteria list, an identity search, each report export — waits at least the configured
/// delay for its <see cref="KapPublicRequestKind"/> after the previous one, on the
/// injected <see cref="TimeProvider"/>. The delay is a real wall-clock timer (a politeness
/// measure, not a scheduling decision), so an L2 harness binding <c>0</c> — or substituting
/// a recording <see cref="KapPublicDelay"/> — keeps runs deterministic.
/// <para>
/// <b>Two paces, one gate</b> (TKT-foundation-100): the gap owed by a request is
/// <see cref="KapPublicSourceOptions.DelayMsFor"/> of its own kind — the shipped 3 s general
/// pace, or the 12 s export quota floor on the export path (the 429 quota finding; the floor
/// only ever <b>raises</b> the export path's, never lowers it). Both are measured
/// start-to-start on the injected clock, and both ride this one gate, so a floor and a general
/// pace can never be satisfied by two overlapping waits.
/// </para>
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

    /// <summary>The configured general inter-request delay in milliseconds (0 disables the wait).</summary>
    public int DelayMs => _options.RequestDelayMs;

    /// <summary>The gap the export path owes in milliseconds (0 when pacing is disabled).</summary>
    public int ExportDelayMs => _options.DelayMsFor(KapPublicRequestKind.Export);

    /// <summary>
    /// Waits until at least the gap <paramref name="kind"/> owes has elapsed since the previous
    /// paced request, then stamps the request's start (the instant the caller then issues it).
    /// The gap is measured start-to-start: a slow response does not let the next request come
    /// any sooner.
    /// </summary>
    public async Task PaceAsync(KapPublicRequestKind kind, CancellationToken cancellationToken = default)
    {
        var requiredMs = _options.DelayMsFor(kind);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_lastCall is not null && requiredMs > 0)
            {
                var elapsed = _clock.GetUtcNow() - _lastCall.Value;
                var remaining = TimeSpan.FromMilliseconds(requiredMs) - elapsed;
                if (remaining > TimeSpan.Zero)
                {
                    await _delay(remaining, cancellationToken).ConfigureAwait(false);
                }
            }

            _lastCall = _clock.GetUtcNow();
        }
        finally
        {
            _gate.Release();
        }
    }
}
