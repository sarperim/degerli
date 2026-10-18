using Degerli.Ingestion.Statements;
using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.Kap;

/// <summary>
/// Paces the strictly sequential VYK calls of one <c>statements</c> run (01 v1.5 §10.9.2/
/// §10.9.11). The free VYK plan allows a handful of calls per minute, so <b>every</b> gateway
/// call the run makes — the per-run <c>/members</c> and <c>/lastDisclosureIndex</c> anchors,
/// each company's <c>/disclosures</c> list and each <c>/disclosureDetail</c> — waits at least
/// <see cref="StatementsSourceOptions.RequestDelayMs"/> after the previous one. The delay is a
/// real wall-clock timer (a politeness measure, not a scheduling decision), so an L2 harness
/// binding <c>0</c> keeps runs deterministic. It composes with the job's per-company pace: the
/// pacer tracks the run's last gateway call, so a job-level wait that already covered the
/// delay is not duplicated.
/// <para>
/// The pacer is scoped to one run (one DI scope), so its <see cref="_lastCall"/> is per-run and
/// never leaks the read-only <c>statements</c> pacing onto the other KAP jobs sharing the
/// client.
/// </para>
/// </summary>
public sealed class KapRequestPacer
{
    private readonly StatementsSourceOptions _options;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset? _lastCall;

    public KapRequestPacer(IOptions<StatementsSourceOptions> options, TimeProvider clock)
    {
        _options = options.Value;
        _clock = clock;
    }

    /// <summary>The configured inter-request delay in milliseconds (0 disables the wait).</summary>
    public int DelayMs => _options.RequestDelayMs;

    /// <summary>
    /// Waits until at least <see cref="StatementsSourceOptions.RequestDelayMs"/> has elapsed
    /// since the previous gateway call, then stamps the call time. Serialized so concurrent
    /// callers (there are none by design) cannot both pass the gate.
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
                    await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
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
