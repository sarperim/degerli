using Degerli.Ingestion.Coverage;

namespace Degerli.Ingestion.Universe;

/// <summary>
/// The recorded launch state of the XU030 fetch bound (01 v1.5 §10.9.3, FR-MDF-007): a
/// checked-in, date-stamped membership launch-seed that writes the current XU030 members to
/// <c>index_constituents</c> append-only through the existing TC-MDF-062 launch-seed
/// machinery (<see cref="UniverseSyncStore.SeedLaunchConstituentsAsync"/>) and records the
/// accepted pre-launch constituent-history coverage gap (FR-MDF-011, BR-MDF-007).
/// <para>
/// <b>No new FR:</b> FR-MDF-007 already covers effective-dated membership change sets — this
/// is the <em>recorded launch state</em>, not a new capability. Instruments outside the fetch
/// bound keep their existing recorded-gap handling (BR-MDF-007). The index code is pinned as
/// the real BIST code <c>XU030</c> (FLG-08 resolved).
/// </para>
/// <para>
/// <b>Live-pull criterion (not CI-verifiable):</b> the real-world correctness of
/// <see cref="Symbols"/> — that it matches BIST's actual XU030 at launch — is an explicit
/// TKT-foundation-018 acceptance criterion, observed at the first live pull. CI asserts the
/// seed's structure and idempotency (TC-MDF-076), never the list's real-world accuracy. The
/// membership list below is the recorded launch state; it must be verified against BIST's
/// published XU030 composition at cut-over (see <see cref="VerificationNote"/>).
/// </para>
/// </summary>
public static class Xu030LaunchSeed
{
    /// <summary>The real BIST index code the prices fetch bound is launched at (01 v1.5 §10.9.3).</summary>
    public const string IndexCode = "XU030";

    /// <summary>Provenance recorded on every seeded membership row (01 §1.2 — İş Yatırım primary).</summary>
    public const string SourceRef = "isyatirim://xu030-launch";

    /// <summary>The launch date the seeded membership intervals start at (effective_from).</summary>
    public static readonly DateOnly AsOfDate = new(2026, 10, 17);

    /// <summary>
    /// The XU030 membership observed at launch — the recorded launch state (01 v1.5 §10.9.3).
    /// <b>Live-pull criterion:</b> verify against BIST's published XU030 at cut-over.
    /// </summary>
    public static IReadOnlyList<string> Symbols { get; } =
    [
        "AKBNK", "ARCLK", "ASELS", "ASTOR", "BIMAS",
        "DOHOL", "EBEBK", "EKGYO", "ENKAI", "EREGL",
        "FROTO", "GARAN", "HALKB", "ISCTR", "KCHOL",
        "KRDMD", "MGROS", "ODAS", "PETKM", "PGSUS",
        "SAHOL", "SASA", "SISE", "TAVHL", "TCELL",
        "THYAO", "TOASO", "TUPRS", "VAKBN", "YKBNK",
    ];

    /// <summary>
    /// Human-readable provenance/verification note recorded with the seed. A hand-recorded
    /// launch state is never presented as machine-verified — the DR-03 labeling discipline.
    /// </summary>
    public const string VerificationNote =
        "Recorded XU030 launch state (2026-10-17). Real-world correctness against BIST's " +
        "published XU030 at cut-over is a live-pull verification criterion (TKT-foundation-018), " +
        "not CI-verifiable; CI asserts the seed's structure and idempotency.";

    /// <summary>
    /// Applies the launch seed (01 v1.5 §10.9.3): seeds the current XU030 membership
    /// append-only and records the accepted pre-launch constituent-history coverage gap.
    /// Idempotent — a re-run adds no duplicate rows and mutates nothing (the TC-MDF-062
    /// posture). Symbols that resolve to no stored instrument are reported, never dropped.
    /// </summary>
    public static async Task<UniverseLaunchSeedResult> ApplyAsync(
        UniverseSyncStore store,
        CoverageRecorder coverage,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(coverage);

        // The seed records the XU030 index reference row its memberships attach to. On cold
        // start no caller creates this exact code — the universe snapshot publishes XU30 and
        // the FU seed creates XU100 (FLG-08) — so gating the seed on an externally-created
        // row would make it inert and let the prices loop silently no-op on its production
        // default. The seed materialises its own reference row, idempotently.
        await store
            .EnsureIndexAsync(IndexCode, "BIST 30", "BIST 30", cancellationToken)
            .ConfigureAwait(false);

        var result = await store
            .SeedLaunchConstituentsAsync(
                SourceRef,
                AsOfDate,
                IndexCode,
                Symbols,
                recordedAt,
                cancellationToken)
            .ConfigureAwait(false);

        // The accepted pre-launch constituent-history gap (FR-MDF-007/011, BR-MDF-007):
        // recorded explicitly, never silently omitted. The gap row is universe-scoped and
        // shared with the universe launch seed, so it is recorded once — guarded on existence
        // exactly as the universe job does, so the XU030 seed never rewrites an already
        // recorded launch date. The recorder upsert itself is idempotent.
        if (!await coverage.HasUniverseSeedGapAsync(cancellationToken).ConfigureAwait(false))
        {
            await coverage.RecordUniverseSeedGapAsync(AsOfDate, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }
}
