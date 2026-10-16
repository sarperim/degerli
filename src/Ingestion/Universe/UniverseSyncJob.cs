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
        // open XU100 membership row exists yet — and records the accepted pre-launch
        // history gap once. This is the seed + coverage record only: the daily
        // change-detection path below is untouched, and a normal day (an open row
        // exists) skips this step entirely.
        var seededMemberships = 0;
        if (fetched.Payload.EffectiveDate is null
            && SnapshotSymbols(fetched.Payload) is { Count: > 0 } seedSymbols
            && !await _store.HasOpenConstituentsAsync(UniverseIndexCode, cancellationToken).ConfigureAwait(false))
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
