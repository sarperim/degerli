namespace Degerli.SourceProbes.Policy;

/// <summary>
/// The politeness posture of the probe harness (TKT-foundation-068). Two rules, both
/// enforced in code rather than trusted to the operator:
/// <list type="number">
/// <item><b>Pacing floors.</b> A probe may never ask a source faster than the posture the
/// product's own ingestion already respects — İş Yatırım ≥ 3 s between calls, the KAP VYK
/// gateway ≥ 12 s (<c>01</c> §10.9.2/§10.9.10 pacing, the shipped
/// <c>Ingestion:Statements:RequestDelayMs</c> = 10 s with the observed walk ≈ 12 s). A
/// declared interval below the floor is clamped <i>up</i>, so a future edit cannot quietly
/// make the tool impolite.</item>
/// <item><b>No probing during a scheduled chain window.</b> The nightly EOD chain (17:30 UTC)
/// and the morning repair chain (05:30 UTC) own the sources; a live probe inside their
/// windows would compete with the run that actually matters. The windows are refused unless
/// the operator passes <c>--force</c>, which is documented as an emergency escape, never a
/// routine.</item>
/// </list>
/// Nothing here touches the network: this type is pure and deterministic (the clock is a
/// parameter), so the unit tests pin it exactly.
/// </summary>
public static class ProbePolicy
{
    /// <summary>The İş Yatırım floor between calls (<c>01</c> §10.9.2 posture; ticket pin ≥ 3 s).</summary>
    public const int IsYatirimIntervalFloorMs = 3_000;

    /// <summary>The KAP VYK gateway floor between calls (ticket pin ≥ 12 s).</summary>
    public const int KapIntervalFloorMs = 12_000;

    /// <summary>The floor for the remaining public sources (EVDS, TEFAS).</summary>
    public const int DefaultIntervalFloorMs = 1_000;

    /// <summary>
    /// The scheduled-chain quiet windows, UTC. The EOD chain fires 17:30 UTC and the morning
    /// chain 05:30 UTC; each window covers the run plus its retry ladder's first attempts.
    /// </summary>
    public static IReadOnlyList<(TimeOnly StartUtc, TimeOnly EndUtc)> ChainQuietWindowsUtc { get; } =
    [
        (new TimeOnly(17, 0), new TimeOnly(19, 30)),
        (new TimeOnly(5, 0), new TimeOnly(7, 30)),
    ];

    /// <summary>
    /// The minimum interval the harness will honour for <paramref name="sourceRef"/>
    /// (<c>isyatirim</c>, <c>yahoo</c>, <c>kap</c>, <c>evds</c>, <c>tefas</c>).
    /// </summary>
    public static int FloorFor(string sourceRef) => sourceRef.ToLowerInvariant() switch
    {
        "isyatirim" => IsYatirimIntervalFloorMs,
        "kap" => KapIntervalFloorMs,
        // The Yahoo contingency rides the same host family the product paces at 3 s (§10.9.2).
        "yahoo" => IsYatirimIntervalFloorMs,
        _ => DefaultIntervalFloorMs,
    };

    /// <summary>The interval actually used: the declared value, clamped up to the floor.</summary>
    public static int EffectiveInterval(string sourceRef, int declaredMs) =>
        Math.Max(declaredMs, FloorFor(sourceRef));

    /// <summary>True when <paramref name="instant"/> falls inside a scheduled chain window.</summary>
    public static bool IsQuietWindow(DateTimeOffset instant)
    {
        var time = TimeOnly.FromDateTime(instant.ToUniversalTime().DateTime);
        foreach (var (start, end) in ChainQuietWindowsUtc)
        {
            if (time >= start && time < end)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Refuses a live run the operator did not explicitly ask for. The default mode of the
    /// harness is offline: no flag, no network (ticket pin).
    /// </summary>
    /// <exception cref="LiveProbeRefusedException">The run was not explicitly requested.</exception>
    public static void EnsureLiveRequested(bool liveRequested)
    {
        if (!liveRequested)
        {
            throw new LiveProbeRefusedException(
                "Refusing to contact a live source: live mode must be requested explicitly (--live).");
        }
    }

    /// <summary>
    /// Refuses a live run inside a scheduled chain window unless the operator forces it.
    /// </summary>
    /// <exception cref="LiveProbeRefusedException">The run would overlap a chain window.</exception>
    public static void EnsureNotInChainWindow(DateTimeOffset instant, bool force)
    {
        if (force || !IsQuietWindow(instant))
        {
            return;
        }

        throw new LiveProbeRefusedException(
            $"Refusing a live probe at {instant.ToUniversalTime():O}: it falls inside a scheduled chain window "
            + "(EOD 17:00–19:30 UTC, morning 05:00–07:30 UTC). Use --force only for an emergency.");
    }
}

/// <summary>A live probe was refused by policy (no opt-in, or inside a chain window).</summary>
public sealed class LiveProbeRefusedException : Exception
{
    /// <summary>Creates the exception with a human-readable <paramref name="message"/>.</summary>
    public LiveProbeRefusedException(string message) : base(message)
    {
    }
}
