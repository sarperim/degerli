namespace Degerli.Ingestion.KapPublic;

/// <summary>
/// One walk's bounds (01 v1.5.53 §10.9.19) — the 078 <c>WalkBudget</c> pattern re-applied to
/// the public site, where <b>boundedness is politeness</b>: a walk must never be able to run
/// for an unbounded time against a website.
/// <para>
/// Three bounds, all checked <b>before</b> a request is issued:
/// <list type="bullet">
/// <item>a per-walk <b>page</b> ceiling (<see cref="KapPublicSourceOptions.MaxListPages"/>);</item>
/// <item>a per-walk <b>detail</b> ceiling (<see cref="KapPublicSourceOptions.MaxDetailFetches"/>);</item>
/// <item>a <b>wall-clock deadline</b> (<see cref="KapPublicSourceOptions.MaxWalkDuration"/>,
/// strictly below the scheduler's per-step bound), read from the injected
/// <see cref="TimeProvider"/> so an L2 fake clock is deterministic.</item>
/// </list>
/// </para>
/// <para>
/// The resume contract: the walker calls <see cref="Complete"/> only after a unit's work is
/// <b>durably done</b>, persists <see cref="LastCompletedUnit"/>, and the next walk starts
/// after that unit — a truncated pass is an honest bounded pass with a saved position, never
/// a lost page and never a silent gap (TC-MDF-103 (v)).
/// </para>
/// </summary>
public sealed class KapPublicWalkBudget
{
    private readonly TimeProvider _clock;
    private readonly DateTimeOffset? _deadline;

    public KapPublicWalkBudget(
        int maxPages,
        int maxDetailFetches,
        TimeSpan maxWalkDuration,
        TimeProvider clock)
    {
        _clock = clock;
        MaxPages = maxPages;
        MaxDetailFetches = maxDetailFetches;
        _deadline = maxWalkDuration > TimeSpan.Zero ? clock.GetUtcNow() + maxWalkDuration : null;
    }

    /// <summary>The page ceiling (≤ 0 = unlimited).</summary>
    public int MaxPages { get; }

    /// <summary>The detail ceiling (≤ 0 = unlimited).</summary>
    public int MaxDetailFetches { get; }

    /// <summary>Pages begun by this walk.</summary>
    public int PagesUsed { get; private set; }

    /// <summary>Detail fetches begun by this walk.</summary>
    public int DetailsUsed { get; private set; }

    /// <summary>True when a bound stopped the walk.</summary>
    public bool Truncated { get; private set; }

    /// <summary>The named reason the walk stopped (a <see cref="KapPublicReasonCodes"/> value); null when not truncated.</summary>
    public string? TruncationReason { get; private set; }

    /// <summary>
    /// The last unit whose work was durably completed — the position the next walk resumes
    /// from (the caller persists it; nothing is inferred).
    /// </summary>
    public string? LastCompletedUnit { get; private set; }

    /// <summary>True when the wall-clock deadline has passed.</summary>
    public bool IsDeadlineExpired => _deadline is { } deadline && _clock.GetUtcNow() >= deadline;

    /// <summary>
    /// Asks to begin one list page. Returns <c>false</c> (recording the truncation) when the
    /// deadline has passed or the page ceiling is reached — the caller must then stop and
    /// persist <see cref="LastCompletedUnit"/>.
    /// </summary>
    public bool TryBeginPage()
    {
        if (!Admit(pages: true))
        {
            return false;
        }

        PagesUsed++;
        return true;
    }

    /// <summary>
    /// Asks to begin one detail fetch. Returns <c>false</c> (recording the truncation) when the
    /// deadline has passed or the detail ceiling is reached.
    /// </summary>
    public bool TryBeginDetail()
    {
        if (!Admit(pages: false))
        {
            return false;
        }

        DetailsUsed++;
        return true;
    }

    /// <summary>Records that <paramref name="unit"/>'s work is durably complete (the resume position).</summary>
    public void Complete(string unit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unit);
        LastCompletedUnit = unit;
    }

    private bool Admit(bool pages)
    {
        if (IsDeadlineExpired)
        {
            Truncate(KapPublicReasonCodes.WalkDeadline);
            return false;
        }

        if (pages ? MaxPages > 0 && PagesUsed >= MaxPages : MaxDetailFetches > 0 && DetailsUsed >= MaxDetailFetches)
        {
            Truncate(pages ? KapPublicReasonCodes.PageCap : KapPublicReasonCodes.DetailCap);
            return false;
        }

        return true;
    }

    private void Truncate(string reason)
    {
        Truncated = true;
        TruncationReason ??= reason;
    }
}
