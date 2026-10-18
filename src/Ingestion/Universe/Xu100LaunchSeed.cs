using Degerli.Ingestion.Coverage;

namespace Degerli.Ingestion.Universe;

/// <summary>
/// The recorded launch state of the covered universe (BR-MDF-005, FR-MDF-007) — the BIST 100
/// (<c>XU100</c>): a checked-in, date-stamped membership launch-seed that writes the current
/// XU100 members to <c>index_constituents</c> append-only through the existing TC-MDF-062
/// launch-seed machinery (<see cref="UniverseSyncStore.SeedLaunchConstituentsAsync"/>) and
/// records the accepted pre-launch constituent-history coverage gap (FR-MDF-011, BR-MDF-007).
/// <para>
/// <b>Why this exists (01 v1.5 §10.9.3 amendment, 2026-10-18):</b> the read surface
/// (<c>src/Api/Stocks/StockListEndpoints.cs</c>, FR-RES-001) scopes the covered universe to
/// current XU100 membership, but the real İş Yatırım universe snapshot publishes instrument
/// rows only — no index-membership rows (01 v1.5 §10.9.8) — so the job's snapshot-derived
/// cold-start XU100 seed degrades to "no seed" and nothing in the real chain ever writes a real
/// XU100 membership row: hundreds of real instruments stored, only the fixture universe served.
/// This seed closes that gap. It is the <see cref="Xu030LaunchSeed"/> precedent applied to the
/// covered universe — a sibling, not a generalized refactor.
/// </para>
/// <para>
/// <b>No new FR:</b> FR-MDF-007 already covers effective-dated membership change sets — this is
/// the <em>recorded launch state</em>, not a new capability. The index code is pinned as the
/// real BIST code <c>XU100</c> (BR-MDF-005).
/// </para>
/// <para>
/// <b>Provenance (FR-MDF-008):</b> <see cref="SourceRef"/> is <c>bist://xu100-launch</c> — the
/// rows trace to BIST's published XU100 composition, not to an İş Yatırım feed. This
/// deliberately diverges from the XU030 seed's <c>isyatirim://xu030-launch</c> label: a
/// BIST-sourced list labeled <c>isyatirim</c> would be a provenance misnomer. <b>Never seeded
/// from the snapshot's instrument set:</b> the real snapshot publishes ~646 instruments, broader
/// than the BIST 100; seeding XU100 membership from it would mislabel the non-XU100 instruments
/// as covered-universe members.
/// </para>
/// <para>
/// <b>Live-pull criterion (not CI-verifiable):</b> the real-world correctness of
/// <see cref="Symbols"/> — that it matches BIST's actual XU100 for the launch index period — is
/// recorded in <see cref="VerificationNote"/> and remains a live-pull verification observation.
/// CI asserts the seed's structure and idempotency, never the list's real-world accuracy.
/// </para>
/// </summary>
public static class Xu100LaunchSeed
{
    /// <summary>The real BIST index code the covered universe is launched at (BR-MDF-005).</summary>
    public const string IndexCode = "XU100";

    /// <summary>
    /// Provenance recorded on every seeded membership row (FR-MDF-008): the rows trace to BIST's
    /// published XU100 composition, not to an İş Yatırım feed.
    /// </summary>
    public const string SourceRef = "bist://xu100-launch";

    /// <summary>
    /// The launch date the seeded membership intervals start at (<c>effective_from</c>). Mirrors
    /// <see cref="Xu030LaunchSeed.AsOfDate"/> so both seeds describe the same launch and the
    /// XU030 ⊆ XU100 consistency check is clean at one <c>effective_from</c>.
    /// </summary>
    public static readonly DateOnly AsOfDate = Xu030LaunchSeed.AsOfDate;

    /// <summary>
    /// The XU100 membership observed at launch — the recorded launch state (BR-MDF-005,
    /// FR-MDF-007). <b>Live-pull criterion:</b> verified against BIST's published XU100
    /// composition for the Ekim–Aralık 2026 index period on 2026-10-18; see
    /// <see cref="VerificationNote"/>. Every <see cref="Xu030LaunchSeed.Symbols"/> entry is
    /// contained here (XU030 ⊆ XU100 — a market-structure invariant asserted by test).
    /// </summary>
    public static IReadOnlyList<string> Symbols { get; } =
    [
        "AEFES", "AGHOL", "AHGAZ", "AKBNK", "AKCNS",
        "AKFYE", "AKSA", "AKSEN", "ALARK", "ALBRK",
        "ALTNY", "ANHYT", "ANSGR", "ARCLK", "ASELS",
        "ASTOR", "AYGAZ", "BERA", "BIMAS", "BINHO",
        "BRSAN", "BRYAT", "BTCIM", "CANTE", "CCOLA",
        "CIMSA", "CVKMD", "CWENE", "DOAS", "DOHOL",
        "ECILC", "ECZYT", "EGEEN", "EGGUB", "EKGYO",
        "ENERY", "ENJSA", "ENKAI", "ENTRA", "EREGL",
        "EUREN", "FENER", "FROTO", "GARAN", "GLRMK",
        "GLYHO", "GRSEL", "GUBRF", "GWIND", "HALKB",
        "HEKTS", "ISCTR", "ISDMR", "ISMEN", "KARSN",
        "KATMR", "KCAER", "KCHOL", "KORDS", "KRDMD",
        "LMKDC", "MAVI", "MGROS", "MPARK", "OBAMS",
        "ODAS", "OTKAR", "OYAKC", "PAHOL", "PETKM",
        "PGSUS", "RGYAS", "RYSAS", "SAHOL", "SASA",
        "SISE", "SNGYO", "SOKM", "TABGD", "TAVHL",
        "TCELL", "TCKRC", "THYAO", "TKFEN", "TOASO",
        "TRALT", "TRENJ", "TRGYO", "TRMET", "TSKB",
        "TTKOM", "TTRAK", "TUKAS", "TUPRS", "TURSG",
        "ULKER", "VAKBN", "VESTL", "YKBNK", "ZOREN",
    ];

    /// <summary>
    /// Human-readable provenance/verification note recorded with the seed. A hand-recorded
    /// launch state is never presented as machine-verified — the DR-03 labeling discipline.
    /// </summary>
    public const string VerificationNote =
        "Recorded XU100 launch state (2026-10-17, the covered universe — BR-MDF-005). Verified " +
        "2026-10-18 against BIST's published XU100 composition for the Ekim–Aralık 2026 index " +
        "period via two independent, dated third-party aggregator listings (BIST's own published " +
        "page was not reachable from this environment): borsapusula.com/hisseler (the dated " +
        "'Ekim–Aralık 2026 dönemi' BIST 100 set, 100 companies) and yatirimci.ai/endeksler/xu100 " +
        "(the dated '16 Eki 2026 18:19' component table, 100 companies) — the two listings agree " +
        "on the exact same 100 symbols; these are third-party aggregators, not BIST's own " +
        "published page, and are recorded honestly as such on the 020 precedent. The BIST 30 " +
        "containment invariant (XU030 ⊆ XU100) holds: every checked-in Xu030LaunchSeed symbol is " +
        "present here, cross-checked on the same dated sources. Real-world correctness remains a " +
        "live-pull verification observation, not CI-verifiable; CI asserts the seed's structure " +
        "and idempotency.";

    /// <summary>
    /// Applies the launch seed (01 v1.5 §10.9.3): seeds the current XU100 membership
    /// append-only and records the accepted pre-launch constituent-history coverage gap.
    /// Idempotent — a re-run adds no duplicate rows and mutates nothing (the TC-MDF-062
    /// posture). Instruments the FU/L2 or real world already carries as open XU100 members land
    /// as <c>AlreadyPresent</c>, never duplicated. Symbols that resolve to no stored instrument
    /// are reported, never dropped (NFR-MDF-003).
    /// </summary>
    public static async Task<UniverseLaunchSeedResult> ApplyAsync(
        UniverseSyncStore store,
        CoverageRecorder coverage,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(coverage);

        // The seed records the XU100 index reference row its memberships attach to. In the
        // FU/dev world the fixture universe already carries it (FLG-08) and the real job's
        // change-detection path may too; on a true cold start no caller does, so the seed
        // materialises its own reference row, idempotently (never rewritten).
        await store
            .EnsureIndexAsync(IndexCode, "BIST 100", "BIST 100", cancellationToken)
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
        // shared with the XU030 and universe launch seeds, so it is recorded once — guarded on
        // existence exactly as the job does, so the seed never rewrites an already recorded
        // launch date. The recorder upsert itself is idempotent.
        if (!await coverage.HasUniverseSeedGapAsync(cancellationToken).ConfigureAwait(false))
        {
            await coverage.RecordUniverseSeedGapAsync(AsOfDate, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }
}
