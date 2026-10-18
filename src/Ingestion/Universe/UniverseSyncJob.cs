using System.Text.Json;
using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.Coverage;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Quarantine;
using Degerli.Ingestion.Sources;
using Microsoft.Extensions.Logging;

namespace Degerli.Ingestion.Universe;

/// <summary>
/// The <c>universe-sync</c> job (FR-MDF-006, FR-MDF-007, UC-MDF-003): syncs the
/// instrument set and sector classifications, applies effective-dated membership
/// changes (append-only) and ingests BIST index levels. An instrument with no (or
/// unknown) sector classification is stored as unclassified and flagged — never
/// dropped (UC-MDF-003 alternate a, TC-MDF-025).
/// </summary>
public sealed class UniverseSyncJob : IIngestJob
{
    /// <summary>Stable job code used by the ledger, scheduler and admin trigger.</summary>
    public const string Code = "universe-sync";

    /// <summary>
    /// Reason code for an index-levels transport failure (a source-down, not a parse event).
    /// Matches <c>prices</c>' per-symbol source-failure class: the real index-levels resource
    /// is a secondary endpoint (01 v1.5 §10.9.8), so its unavailability degrades the run to
    /// <see cref="IngestResult.Partial"/> without aborting the instrument/membership sync.
    /// </summary>
    public const string SourceFetchFailed = "SOURCE_FETCH_FAILED";

    /// <summary>The covered universe index (BR-MDF-005): the BIST 100.</summary>
    private const string UniverseIndexCode = "XU100";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISourceAdapter<UniversePayload> _universeAdapter;
    private readonly ISourceAdapter<IndexLevelsPayload> _indexLevelsAdapter;
    private readonly UniverseSyncStore _store;
    private readonly CoverageRecorder _coverage;
    private readonly IQuarantineWriter _quarantine;
    private readonly IIngestionAlerter _alerter;
    private readonly TimeProvider _clock;
    private readonly ILogger<UniverseSyncJob> _logger;

    public UniverseSyncJob(
        ISourceAdapter<UniversePayload> universeAdapter,
        ISourceAdapter<IndexLevelsPayload> indexLevelsAdapter,
        UniverseSyncStore store,
        CoverageRecorder coverage,
        IQuarantineWriter quarantine,
        IIngestionAlerter alerter,
        TimeProvider clock,
        ILogger<UniverseSyncJob> logger)
    {
        _universeAdapter = universeAdapter;
        _indexLevelsAdapter = indexLevelsAdapter;
        _store = store;
        _coverage = coverage;
        _quarantine = quarantine;
        _alerter = alerter;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public string JobCode => Code;

    /// <inheritdoc />
    public async Task<IngestResult> RunAsync(
        IngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        SourcePayload<UniversePayload> fetched;
        try
        {
            fetched = await _universeAdapter.FetchAsync(request.Date, cancellationToken).ConfigureAwait(false);
        }
        catch (SourcePayloadException exception)
        {
            await QuarantineAsync(null, exception.RawJson, "UNPARSEABLE_PAYLOAD", cancellationToken)
                .ConfigureAwait(false);
            return new IngestResult(Code, 0, 0, 1, IngestResult.Failed);
        }

        if (string.IsNullOrWhiteSpace(fetched.SourceRef))
        {
            _logger.LogWarning("universe-sync: payload carries no provenance (MISSING_PROVENANCE)");
            await QuarantineAsync(null, fetched.RawJson, "MISSING_PROVENANCE", cancellationToken)
                .ConfigureAwait(false);
            return new IngestResult(Code, 0, 0, 1, IngestResult.Failed);
        }

        var now = _clock.GetUtcNow();
        var universe = await _store
            .SyncUniverseAsync(fetched.SourceRef, now, fetched.Payload, cancellationToken)
            .ConfigureAwait(false);

        var quarantined = 0;

        // Launch seed (FR-MDF-007, BR-MDF-005/007): at launch there is no membership
        // history, so the current BIST 100 is seeded once from the full universe
        // snapshot the source publishes (the covered universe). The seed fires only on
        // cold start — when the snapshot carries no effective-dated change set and no
        // open XU100 membership row exists yet. This is the seed + coverage record only:
        // the daily change-detection path below is untouched, and a normal day (an open
        // row exists) skips this step entirely.
        var seededMemberships = 0;
        if (fetched.Payload.EffectiveDate is null
            && SnapshotSymbols(fetched.Payload) is { Count: > 0 } seedSymbols
            && !await _store.HasOpenConstituentsAsync(UniverseIndexCode, cancellationToken).ConfigureAwait(false))
        {
            if (!await _store.IndexExistsAsync(UniverseIndexCode, cancellationToken).ConfigureAwait(false))
            {
                // A snapshot that publishes instruments but no XU100 index reference has
                // no index to attach the seed to: degrade to "no seed" rather than let
                // the throwing seed abort the rest of the job (index-levels, …).
                _logger.LogWarning(
                    "universe-sync: launch seed skipped — index {IndexCode} is not stored (UNKNOWN_REFERENCE)",
                    UniverseIndexCode);
            }
            else
            {
                var seed = await _store
                    .SeedLaunchConstituentsAsync(
                        fetched.SourceRef,
                        request.Date,
                        UniverseIndexCode,
                        seedSymbols,
                        now,
                        cancellationToken)
                    .ConfigureAwait(false);
                seededMemberships = seed.Seeded;

                // Unresolved symbols are reported and quarantined, never silently dropped
                // (NFR-MDF-003).
                foreach (var symbol in seed.UnknownSymbols)
                {
                    _logger.LogWarning(
                        "universe-sync: launch seed symbol {Symbol} resolved to no stored instrument (UNKNOWN_REFERENCE)",
                        symbol);
                    await QuarantineAsync(fetched.SourceRef, symbol, "UNKNOWN_REFERENCE", cancellationToken)
                        .ConfigureAwait(false);
                    quarantined++;
                }
            }
        }

        // XU100 covered-universe launch-seed (01 v1.5 §10.9.3 amendment 2026-10-18,
        // FR-MDF-007, BR-MDF-005): the real İş Yatırım universe snapshot publishes instrument
        // rows only — no index-membership rows (§10.9.8) — so the snapshot-derived cold-start
        // block above degrades to "no seed" and nothing in the real chain ever writes a real
        // XU100 membership row; the covered-universe read surface (FR-RES-001) stays empty while
        // the real instruments sit stored. This checked-in seed closes that gap. It fires only
        // (1) for the real source envelope — the canonical L2 double carries its own ref, so the
        // checked-in real seed never fires in the stub world and canonical goldens stay untouched
        // (§10.9.7); (2) on cold-start posture — a snapshot that publishes an effective-dated
        // change set hands membership to the FR-MDF-007 change-detection path; and (3) at most
        // once — keyed on the seed's own provenance marker. Deliberately NOT gated on
        // !HasOpenConstituentsAsync("XU100"): the FU/dev world legitimately pre-populates XU100
        // with its fixture members (FLG-08) and the real seed must still fire alongside them —
        // the TC-MDF-062 machinery lands those overlapped members as AlreadyPresent, never
        // duplicated.
        if (fetched.Payload.EffectiveDate is null
            && string.Equals(fetched.SourceRef, UniverseSourceAdapter.IsYatirimSource, StringComparison.Ordinal)
            && !await _store
                .HasSeedAsync(Xu100LaunchSeed.IndexCode, Xu100LaunchSeed.SourceRef, cancellationToken)
                .ConfigureAwait(false))
        {
            var seed = await Xu100LaunchSeed
                .ApplyAsync(_store, _coverage, now, cancellationToken)
                .ConfigureAwait(false);
            seededMemberships += seed.Seeded;

            // Unresolved symbols are reported and quarantined, never silently dropped
            // (NFR-MDF-003).
            foreach (var symbol in seed.UnknownSymbols)
            {
                _logger.LogWarning(
                    "universe-sync: XU100 launch seed symbol {Symbol} resolved to no stored instrument (UNKNOWN_REFERENCE)",
                    symbol);
                await QuarantineAsync(Xu100LaunchSeed.SourceRef, symbol, "UNKNOWN_REFERENCE", cancellationToken)
                    .ConfigureAwait(false);
                quarantined++;
            }
        }

        // The accepted pre-launch constituent-history gap (FR-MDF-007/011, BR-MDF-007) is
        // recorded exactly once, independently of whether the seed above ran: a cold start
        // that already carries open XU100 memberships (e.g. a bootstrap that seeds the
        // reference data plus memberships) must still record it rather than silently omit
        // it. The recorder upsert is idempotent, and guarding on existence keeps the
        // recorded launch date fixed across later runs.
        if (!await _coverage.HasUniverseSeedGapAsync(cancellationToken).ConfigureAwait(false))
        {
            await _coverage.RecordUniverseSeedGapAsync(request.Date, cancellationToken).ConfigureAwait(false);
        }

        // A membership change referencing an unknown index/instrument is an integrity
        // gap: quarantine and count it rather than dropping it (NFR-MDF-003, FR-MDF-012).
        foreach (var rejected in universe.RejectedMemberships)
        {
            _logger.LogWarning(
                "universe-sync: membership change references unknown index/instrument (UNKNOWN_REFERENCE): {IndexCode}/{Symbol}",
                rejected.IndexCode,
                rejected.Symbol);
            await QuarantineAsync(
                    fetched.SourceRef,
                    Serialize(rejected),
                    "UNKNOWN_REFERENCE",
                    cancellationToken)
                .ConfigureAwait(false);
            quarantined++;
        }

        foreach (var unclassified in universe.Unclassified)
        {
            _logger.LogWarning(
                "universe-sync: instrument {Symbol} has no sector classification (MISSING_CLASSIFICATION)",
                unclassified.Symbol);
            await QuarantineAsync(
                    fetched.SourceRef,
                    Serialize(unclassified),
                    "MISSING_CLASSIFICATION",
                    cancellationToken)
                .ConfigureAwait(false);
            await _alerter.RaiseAsync(
                    new IngestionAlert(
                        Code,
                        "MISSING_CLASSIFICATION",
                        Subject: $"Degerli alert: universe-sync MISSING_CLASSIFICATION for {unclassified.Symbol}",
                        BodyTr: $"universe-sync işi {unclassified.Symbol} enstrümanında sektör sınıflandırması bulamadı; enstrüman sınıflandırılmamış olarak kaydedildi ve işaretlendi.",
                        BodyEn: $"The universe-sync job found no sector classification for {unclassified.Symbol}; the instrument was stored as unclassified and flagged.",
                        SourceRef: fetched.SourceRef),
                    cancellationToken)
                .ConfigureAwait(false);
            quarantined++;
        }

        var indexWritten = 0;
        var indexUnchanged = 0;
        try
        {
            var levels = await _indexLevelsAdapter.FetchAsync(request.Date, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(levels.SourceRef))
            {
                _logger.LogWarning("universe-sync: index-levels payload carries no provenance (MISSING_PROVENANCE)");
                await QuarantineAsync(null, levels.RawJson, "MISSING_PROVENANCE", cancellationToken)
                    .ConfigureAwait(false);
                quarantined++;
            }
            else
            {
                var result = await _store
                    .SyncIndexLevelsAsync(levels.SourceRef, now, levels.Payload, cancellationToken)
                    .ConfigureAwait(false);
                indexWritten = result.Written;
                indexUnchanged = result.Unchanged;

                // Unknown index references are an integrity gap: quarantine, never drop.
                foreach (var rejected in result.Rejected)
                {
                    _logger.LogWarning(
                        "universe-sync: index level references unknown index (UNKNOWN_REFERENCE): {IndexCode}",
                        rejected.IndexCode);
                    await QuarantineAsync(
                            levels.SourceRef,
                            Serialize(rejected),
                            "UNKNOWN_REFERENCE",
                            cancellationToken)
                        .ConfigureAwait(false);
                    quarantined++;
                }
            }
        }
        catch (SourcePayloadException exception)
        {
            _logger.LogWarning("universe-sync: index-levels payload unparseable (UNPARSEABLE_PAYLOAD)");
            await QuarantineAsync(null, exception.RawJson, "UNPARSEABLE_PAYLOAD", cancellationToken)
                .ConfigureAwait(false);
            quarantined++;
        }
        catch (SourceFetchException exception)
        {
            // Index-levels is a secondary resource (01 v1.5 §10.9.8): a live fetch failure (a
            // source-down — the chart endpoint unreachable, the pre-fix defect) must degrade to
            // the same non-fatal quarantine + Partial + alert class as an unparseable payload,
            // never abort the instrument/membership sync that already succeeded above. The
            // launch-blocking chain (instruments → XU030 membership → prices) is never hostage
            // to this second endpoint's availability.
            _logger.LogWarning(
                exception,
                "universe-sync: index-levels source fetch failed (SOURCE_FETCH_FAILED)");
            await QuarantineAsync(null, null, SourceFetchFailed, cancellationToken)
                .ConfigureAwait(false);
            await _alerter.RaiseAsync(
                    new IngestionAlert(
                        Code,
                        SourceFetchFailed,
                        Subject: $"Degerli alert: universe-sync could not fetch index-levels ({SourceFetchFailed})",
                        BodyTr: "universe-sync işi endeks seviyeleri kaynağından veri çekemedi; enstrüman ve üyelik senkronizasyonu tamamlandı, endeks seviyeleri bu çalıştırmada atlandı ve işaretlendi (kaynak hatası, ayrıştırma olayı değildir).",
                        BodyEn: "The universe-sync job could not fetch the index-levels source; the instrument and membership sync completed, and index levels were skipped and flagged for this run (a source failure, not a parse event).",
                        SourceRef: null),
                    cancellationToken)
                .ConfigureAwait(false);
            quarantined++;
        }

        // A closed membership interval is a recorded fact change too (F5) — count it
        // alongside the added memberships and newly written levels.
        var written = universe.InstrumentsWritten
            + universe.MembershipsAdded
            + universe.MembershipsClosed
            + seededMemberships
            + indexWritten;
        var unchanged = universe.InstrumentsUnchanged + indexUnchanged;
        var status = quarantined > 0 ? IngestResult.Partial : IngestResult.Succeeded;

        return new IngestResult(Code, written, unchanged, quarantined, status);
    }

    /// <summary>
    /// The candidate launch-seed symbols: every instrument the full universe snapshot
    /// publishes (the source's covered universe is the BIST 100, BR-MDF-005). The caller
    /// guards on <see cref="UniversePayload.EffectiveDate"/> so a payload carrying an
    /// effective-dated change set never seeds.
    /// </summary>
    private static List<string> SnapshotSymbols(UniversePayload payload) =>
        (payload.Instruments ?? [])
            .Select(instrument => instrument.Symbol)
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private Task QuarantineAsync(
        string? sourceRef,
        string? payloadJson,
        string reasonCode,
        CancellationToken cancellationToken) =>
        _quarantine.WriteAsync(
            new QuarantineEntry(Code, sourceRef, payloadJson, reasonCode, _clock.GetUtcNow()),
            cancellationToken);

    private static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, JsonOptions);
}
