using System.Text.Json;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Kap;
using Degerli.Ingestion.KapPublic;
using Degerli.Ingestion.Quarantine;
using Degerli.Ingestion.Sources;
using Degerli.Ingestion.Universe;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.Disclosures;

/// <summary>
/// The <c>disclosures</c> job (FR-MDF-005, UC-MDF-001 step 2): fetch KAP disclosure
/// metadata, enforce provenance and store metadata rows whose documents live on disk.
/// <para>
/// The company-scoped VYK walk lives in the adapter (01 v1.5 §10.9.15), so the job's part of the
/// §10.9.10/§10.9.11 per-company isolation doctrine (FR-MDF-012) is to carry the adapter's
/// isolated bound symbols into the run ledger: an unmapped symbol is never a whole-run failure —
/// the run ends <c>partial</c> with that symbol recorded (and named) in the per-symbol stats.
/// </para>
/// </summary>
public sealed class DisclosuresJob : IIngestJob
{
    /// <summary>Stable job code used by the ledger, scheduler and admin trigger.</summary>
    public const string Code = "disclosures";

    /// <summary>
    /// Reason code for an <c>Ingestion:Disclosures:Source</c> value the lane does not recognise. A
    /// typo must <b>fail</b> the run by name: silently running the other source would stamp the wrong
    /// provenance on every fact it wrote and look healthy in the ledger (the 082 precedent).
    /// </summary>
    public const string InvalidSourceSelection = "INVALID_SOURCE_SELECTION";

    /// <summary>
    /// The named reason of a public-site pass that has no bound to write: an empty constituent set is
    /// a failure (a feed over an empty bound proves nothing), never an empty success.
    /// </summary>
    public const string NoBoundConstituents = "NO_BOUND_CONSTITUENTS";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISourceAdapter<DisclosuresPayload> _adapter;
    private readonly DisclosureStore _store;
    private readonly IQuarantineWriter _quarantine;
    private readonly TimeProvider _clock;
    private readonly ILogger<DisclosuresJob> _logger;

    // The public-site feed's own seams (01 v1.5.85 §10.9.19, TKT-foundation-084). The VYK path above
    // is untouched by them: they are read only when `Ingestion:Disclosures:Source = kap-public`.
    private readonly DisclosuresSourceOptions _options;
    private readonly KapPublicSourceReader? _kapPublicReader;
    private readonly KapPublicCompanyMapStore? _kapPublicMaps;
    private readonly KapPublicSourceOptions? _kapPublicOptions;
    private readonly KapPublicDisclosuresCursorStore? _kapPublicCursors;
    private readonly KapPublicBoundUniverse? _kapPublicBound;
    private readonly UniverseSyncStore? _universe;

    /// <summary>
    /// The composed shape (01 v1.5.85 §10.9.19, TKT-foundation-084). The public lane's own stores —
    /// its date-watermark cursor and its bound selection — are built from the run's
    /// <see cref="DegerliDbContext"/> here, exactly as the 082 statements lane builds its cursor and
    /// depth stores: they are this lane's private write shape, not shared DI seams.
    /// </summary>
    public DisclosuresJob(
        ISourceAdapter<DisclosuresPayload> adapter,
        DisclosureStore store,
        IQuarantineWriter quarantine,
        TimeProvider clock,
        ILogger<DisclosuresJob> logger,
        IOptions<DisclosuresSourceOptions>? options,
        KapPublicSourceReader? kapPublicReader,
        KapPublicCompanyMapStore? kapPublicMaps,
        IOptions<KapPublicSourceOptions>? kapPublicOptions,
        UniverseSyncStore? universe,
        Degerli.Persistence.DegerliDbContext? db)
    {
        _adapter = adapter;
        _store = store;
        _quarantine = quarantine;
        _clock = clock;
        _logger = logger;
        _options = options?.Value ?? new DisclosuresSourceOptions();
        _kapPublicReader = kapPublicReader;
        _kapPublicMaps = kapPublicMaps;
        _kapPublicOptions = kapPublicOptions?.Value;
        _universe = universe;
        _kapPublicCursors = db is null ? null : new KapPublicDisclosuresCursorStore(db);
        _kapPublicBound = db is null || kapPublicMaps is null ? null : new KapPublicBoundUniverse(db, kapPublicMaps);
    }

    /// <inheritdoc />
    public string JobCode => Code;

    /// <inheritdoc />
    public async Task<IngestResult> RunAsync(
        IngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        // Which source this lane reads is configuration (01 v1.5.85 §10.9.19). An unrecognised value
        // fails the run <b>by name</b> and fetches nothing: a typo silently falling back to the other
        // source would stamp the wrong provenance on every fact and look healthy.
        if (!DisclosuresSourceOptions.TryParseSource(_options.Source, out var selectedSource))
        {
            _logger.LogError(
                "disclosures: unrecognised source selection '{Source}' in {Section}:{Key}; expected '{Vyk}' or '{KapPublic}'. Nothing was fetched ({ReasonCode})",
                _options.Source,
                DisclosuresSourceOptions.SectionName,
                nameof(DisclosuresSourceOptions.Source),
                DisclosuresSourceOptions.VykSource,
                DisclosuresSourceOptions.KapPublicSource,
                InvalidSourceSelection);
            return new IngestResult(Code, 0, 0, 0, IngestResult.Failed)
            {
                FailureReason = $"{InvalidSourceSelection}: {DisclosuresSourceOptions.SectionName}:{nameof(DisclosuresSourceOptions.Source)} '{_options.Source}' is neither '{DisclosuresSourceOptions.VykSource}' nor '{DisclosuresSourceOptions.KapPublicSource}'",
            };
        }

        if (string.Equals(selectedSource, DisclosuresSourceOptions.KapPublicSource, StringComparison.Ordinal))
        {
            return await RunKapPublicAsync(cancellationToken).ConfigureAwait(false);
        }

        SourcePayload<DisclosuresPayload> fetched;
        try
        {
            fetched = await _adapter.FetchAsync(request.Date, cancellationToken).ConfigureAwait(false);
        }
        catch (SourcePayloadException exception)
        {
            await QuarantineAsync(null, exception.RawJson, "UNPARSEABLE_PAYLOAD", cancellationToken).ConfigureAwait(false);
            return new IngestResult(Code, 0, 0, 1, IngestResult.Failed);
        }

        if (string.IsNullOrWhiteSpace(fetched.SourceRef))
        {
            _logger.LogWarning("disclosures: payload carries no provenance (MISSING_PROVENANCE)");
            await QuarantineAsync(fetched, fetched.RawJson, "MISSING_PROVENANCE", cancellationToken).ConfigureAwait(false);
            return new IngestResult(Code, 0, 0, 1, IngestResult.Failed);
        }

        var result = await _store
            .UpsertAsync(fetched.SourceRef, _clock.GetUtcNow(), fetched.Payload, cancellationToken)
            .ConfigureAwait(false);

        var payloadJson = JsonSerializer.Serialize(fetched.Payload, JsonOptions);
        var quarantined = 0;
        foreach (var symbol in result.UnknownSymbols)
        {
            _logger.LogWarning("disclosures: unknown instrument {Symbol} (SCHEMA_MISMATCH)", symbol);
            await QuarantineAsync(fetched, payloadJson, "SCHEMA_MISMATCH", cancellationToken).ConfigureAwait(false);
            quarantined++;
        }

        // The adapter's per-constituent identity isolation (01 v1.5 §10.9.15, FR-MDF-012): the
        // symbols its loop skipped because the VYK directory could not map them. They ride the
        // run's per-symbol stats with the named reason logged, so the run reports what it could
        // not fetch instead of failing or silently looking complete.
        var unresolved = _adapter is IVykIdentityIsolationReporter reporter
            ? reporter.UnresolvedSymbols
            : [];

        if (unresolved.Count > 0)
        {
            _logger.LogWarning(
                "disclosures: {Count} bound symbol(s) isolated ({ReasonCode}): {Symbols}",
                unresolved.Count,
                VykCompanyDirectory.CompanyIdentityUnresolved,
                string.Join(", ", unresolved));
        }

        // The adapter's per-disclosure isolation (01 v1.5.40 §10.9.15, TKT-foundation-061): the
        // disclosure details its walk could not fetch — an HTTP error, a fault envelope, a
        // transport failure or a per-item timeout (the 2026-10-22 500 on /disclosureDetail/1185709
        // and the 90-minute hang it bounded) — ride the run as quarantine rows carrying the
        // gateway's own error, counted with the named reason. One bad attachment is an honest
        // partial: never a failed run that fail-stops the chain, never a silent omission.
        var detailSkips = _adapter is IDisclosureIsolationReporter detailReporter
            ? detailReporter.DetailSkips
            : [];

        foreach (var skip in detailSkips)
        {
            _logger.LogWarning(
                "{Job}: disclosure detail {Index} for {Symbol} could not be fetched and is quarantined with evidence ({ReasonCode}, {FailureKind}, status {Status}): {Error}",
                Code,
                skip.DisclosureIndex,
                skip.Symbol,
                skip.ReasonCode,
                skip.FailureKind,
                skip.StatusCode,
                skip.Error);
            await QuarantineAsync(
                    fetched,
                    skip.ToEvidenceJson(),
                    VykDisclosureWalker.DisclosureDetailUnfetchable,
                    cancellationToken)
                .ConfigureAwait(false);
            quarantined++;
        }

        // The adapter's bootstrap provenance (TKT-foundation-078, 01 §10.9.15): a run that seeded a
        // cursor from the platform head's window says so — a start-of-read decision, never a claim
        // that the company (or the market) had no disclosures before it. The count is logged and
        // the entries ride the run's per-symbol stats with the named reason, so the ledger carries
        // the provenance rather than a fabricated "no disclosures" state.
        var bootstrapped = _adapter is IDisclosureBootstrapReporter bootstrapReporter
            ? bootstrapReporter.Bootstrapped
            : [];

        if (bootstrapped.Count > 0)
        {
            _logger.LogInformation(
                "{Job}: bootstrapped {Count} cursor(s) from the platform head's window ({ReasonCode}): {Cursors}",
                Code,
                bootstrapped.Count,
                DisclosuresSourceAdapter.BootstrappedCursor,
                string.Join(", ", bootstrapped));
        }

        var outOfBound = _adapter is IDisclosureBootstrapReporter bp ? bp.OutOfBoundRows : 0;
        var listPages = _adapter is IDisclosureBootstrapReporter lp ? lp.ListPages : 0;
        var truncated = _adapter is IDisclosureBootstrapReporter t ? t.Truncated : false;

        if (outOfBound > 0)
        {
            _logger.LogInformation(
                "{Job}: {Count} market row(s) outside the bound universe were counted, not fetched ({ReasonCode})",
                Code,
                outOfBound,
                DisclosuresSourceAdapter.OutsideBoundUniverse);
        }

        // A pass that stops at its configured budget is bounded, not broken: the cursor names the
        // last index it completed and the next run resumes there (TKT-foundation-078's "the run
        // must finish" pin). It is logged so an operator can see the lane is behind, not stalled.
        _logger.LogInformation(
            "{Job}: walk finished — {Pages} list page(s), {Bootstrapped} bootstrap(s), {OutOfBound} out-of-bound row(s), truncated {Truncated}",
            Code,
            listPages,
            bootstrapped.Count,
            outOfBound,
            truncated);

        // The cursor checkpoint (TKT-foundation-069, 01 §10.9.15): both feeds hand back their
        // cursor work — the market row, the per-instrument advances and the clears — and persist
        // nothing themselves. It is written only now, after the facts above were stored and the
        // skip evidence was recorded, so a store failure leaves every cursor where the last
        // durable write left it and the next run re-walks those indexes (the idempotent store
        // reports the re-emitted facts `unchanged`). A cursor can therefore never advance past
        // facts that were never written.
        if (_adapter is DisclosuresSourceAdapter vyk && vyk.HasPendingCursors)
        {
            await vyk.PersistCursorsAsync(cancellationToken).ConfigureAwait(false);
        }

        // Per-symbol stats carry both isolation classes additively: the 046 identity isolation
        // (failed), this ticket's per-disclosure skips (quarantined) and — new here — the bootstrap
        // provenance (written 0, not failed: the symbol's walk is bounded, not empty).
        var symbolStats = unresolved.Select(symbol => new SymbolRunStat(symbol, 0, 0, 0, Failed: true)).ToList();
        foreach (var group in detailSkips.GroupBy(skip => skip.Symbol, StringComparer.Ordinal))
        {
            symbolStats.Add(new SymbolRunStat(group.Key, 0, 0, group.Count(), Failed: false));
        }

        foreach (var symbol in bootstrapped.Where(cursor =>
                     !string.Equals(cursor, DisclosuresSourceAdapter.MarketCursorName, StringComparison.Ordinal)))
        {
            // The market cursor names no instrument, so it rides the counted log line above and the
            // out-of-bound `skipped` count — a per-symbol stat must name a symbol (TKT-foundation-078).
            symbolStats.Add(new SymbolRunStat(symbol, 0, 0, 0, Failed: false, DisclosuresSourceAdapter.BootstrappedCursor));
        }

        var status = quarantined > 0 || unresolved.Count > 0 ? IngestResult.Partial : IngestResult.Succeeded;
        return new IngestResult(Code, result.Inserted, result.Unchanged, quarantined, status)
        {
            SymbolStats = symbolStats,
            // Market rows outside the bound universe are a counted skip (never an omission, never
            // a fabricated row) — they ride the ledger's own `skipped` count.
            Skipped = outOfBound,
        };
    }

    /// <summary>
    /// The public-site <b>recent-disclosures</b> feed (01 v1.5.85 §10.9.19, TKT-foundation-084;
    /// FR-MDF-005/008/011/012) — the lane's <b>second source</b>, beside the VYK feed and never a
    /// repointing of it.
    /// <para>
    /// One market-wide byCriteria pass from one <b>date watermark</b> (the public list is
    /// date-windowed, not index-paged): the bound constituents' rows are written through the same
    /// metadata store under the lane's own evidence class <c>kap-public</c>, the rows of every other
    /// company are counted under <see cref="KapPublicDisclosuresWalker.OutsideBoundUniverse"/> and
    /// never fetched further, and the cursor is persisted only <b>after</b> the fact write
    /// (TKT-foundation-069's shape — a store failure cannot advance the watermark past unwritten
    /// facts).
    /// </para>
    /// <para>
    /// <b>Politeness is structural.</b> One <see cref="KapPublicWalkBudget"/> is created for the pass
    /// and admits every list request: the 079 client keeps its ≥3 s pacing, its single-flight gate,
    /// its honest User-Agent and its posture gate untouched — this job adds no request path of its
    /// own, and this feed makes no per-disclosure request at all (the byCriteria row is the metadata).
    /// </para>
    /// </summary>
    private async Task<IngestResult> RunKapPublicAsync(CancellationToken cancellationToken)
    {
        if (_kapPublicReader is null || _kapPublicMaps is null || _kapPublicOptions is null
            || _kapPublicCursors is null || _kapPublicBound is null || _universe is null)
        {
            // Composition without the public-site seams cannot run this feed; say so by name rather
            // than pretending it ran (the ledger never carries a silent hole).
            _logger.LogError(
                "disclosures: the public-site feed is selected but its seams are not composed; nothing was fetched ({ReasonCode})",
                InvalidSourceSelection);
            return new IngestResult(Code, 0, 0, 0, IngestResult.Failed)
            {
                FailureReason = $"{InvalidSourceSelection}: the public-site feed's seams are not composed for this run",
            };
        }

        var now = _clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var symbols = await _universe
            .GetCurrentConstituentSymbolsAsync(_options.UniverseIndex, cancellationToken)
            .ConfigureAwait(false);

        if (symbols.Count == 0)
        {
            _logger.LogWarning(
                "disclosures: index {IndexCode} has no current constituents; a public-site feed over an empty bound is a failure, not an empty success ({ReasonCode})",
                _options.UniverseIndex,
                NoBoundConstituents);
            return new IngestResult(Code, 0, 0, 0, IngestResult.Failed)
            {
                FailureReason = $"{NoBoundConstituents}: index '{_options.UniverseIndex}' has no current constituents",
            };
        }

        // The stored public-site identity (080) is what makes a constituent part of *this* source:
        // the feed writes only companies whose public oid the lane holds, and never guesses one.
        var selection = await _kapPublicBound.SelectAsync(symbols, cancellationToken).ConfigureAwait(false);
        var boundSymbols = selection.Bound;

        var symbolStats = new List<SymbolRunStat>(symbols.Count);
        var identityIsolated = 0;
        foreach (var symbol in selection.UnresolvedSymbols)
        {
            identityIsolated++;
            _logger.LogWarning(
                "disclosures: company {Symbol} has no stored public-site identity ({ReasonCode}); it is isolated and the market feed continues",
                symbol,
                KapPublicReasonCodes.CompanyIdentityUnresolved);
            symbolStats.Add(new SymbolRunStat(symbol, 0, 0, 0, false, KapPublicReasonCodes.CompanyIdentityUnresolved));
        }

        if (boundSymbols.Count == 0)
        {
            _logger.LogWarning(
                "disclosures: no constituent of {IndexCode} has a stored public-site identity; the market feed has nothing it may write ({ReasonCode})",
                _options.UniverseIndex,
                NoBoundConstituents);
            return new IngestResult(Code, 0, 0, 0, IngestResult.Failed)
            {
                SymbolStats = symbolStats,
                FailureReason = $"{NoBoundConstituents}: no constituent of '{_options.UniverseIndex}' has a stored public-site oid",
            };
        }

        var stored = await _kapPublicCursors.GetAsync(cancellationToken).ConfigureAwait(false);
        var lookbackDays = _options.KapPublicLookbackDays > 0 ? _options.KapPublicLookbackDays : 1;
        var from = stored?.Watermark ?? today.AddDays(-lookbackDays);
        if (from > today)
        {
            // A watermark ahead of the clock (a clock change, or a restored database) is never walked
            // backwards silently: the pass covers the run's own day and re-anchors the watermark at
            // it, saying so (TKT-foundation-084 review DR-8: this is a pull-back, not "nothing moves").
            _logger.LogWarning(
                "disclosures: the persisted watermark {Watermark} is ahead of the run's day {Today}; the pass covers {RunDay} and re-anchors the watermark there",
                from,
                today,
                today);
            from = today;
        }

        var plan = new KapPublicDisclosuresPlan(from, today, _options.KapPublicWindowDays);
        var budget = new KapPublicWalkBudget(
            _kapPublicOptions.MaxListPages,
            _kapPublicOptions.MaxDetailFetches,
            _kapPublicOptions.MaxWalkDuration,
            _clock);
        var walker = new KapPublicDisclosuresWalker(_kapPublicReader, budget, _logger);

        if (stored is null)
        {
            _logger.LogInformation(
                "disclosures: public-site market cursor bootstrapped at {From} (run day {Today} − lookback {Lookback} day(s)); a start-of-read decision, not a claim that the market published nothing before it",
                from,
                today,
                lookbackDays);
        }

        var walk = await walker
            .WalkAsync(stored ?? KapPublicDisclosuresCursor.Start(from), plan, boundSymbols, cancellationToken)
            .ConfigureAwait(false);
        walk = walk with { Bootstrapped = stored is null };

        // Per-window and per-row shape failures: named, counted and quarantined with the source's own
        // evidence — a verdict about a body is never a transport failure in disguise.
        var quarantined = 0;
        foreach (var skip in walk.Skips)
        {
            _logger.LogWarning(
                "disclosures: public-site market evidence skipped ({ReasonCode}, index {Index}): {Detail}",
                skip.ReasonCode,
                skip.DisclosureIndex?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "(window)",
                skip.Detail);
            await _quarantine.WriteAsync(
                    new QuarantineEntry(
                        Code,
                        KapPublicSourceOptions.SourceName,
                        skip.EvidenceJson,
                        skip.ReasonCode,
                        now),
                    cancellationToken)
                .ConfigureAwait(false);
            quarantined++;
        }

        // The fact write: only the bound constituents' rows, stamped with this lane's evidence class
        // (never the VYK lane's `kap`) and carrying the public index they were read from.
        var inserted = 0;
        var unchanged = 0;
        if (walk.Facts.Count > 0)
        {
            var outcome = await _store
                .UpsertAsync(
                    KapPublicSourceOptions.SourceName,
                    now,
                    new DisclosuresPayload(KapPublicSourceOptions.SourceName, walk.Facts),
                    cancellationToken)
                .ConfigureAwait(false);
            inserted = outcome.Inserted;
            unchanged = outcome.Unchanged;
            foreach (var symbol in outcome.UnknownSymbols)
            {
                // A fact whose symbol has no instrument is a composition surprise, not a data event:
                // named and counted, never written under a guessed identity.
                _logger.LogWarning(
                    "disclosures: public-site fact for unknown instrument {Symbol} ({ReasonCode})",
                    symbol,
                    KapPublicReasonCodes.CompanyIdentityUnresolved);
                identityIsolated++;
                symbolStats.Add(new SymbolRunStat(symbol, 0, 0, 0, true, KapPublicReasonCodes.CompanyIdentityUnresolved));
            }
        }

        // The cursor is a checkpoint, not a walk product (TKT-foundation-069): it is written only now,
        // after the facts above were stored and the skip evidence recorded, so a store failure leaves
        // it where the last durable write left it and the next pass re-lists the same window.
        if (walk.LastCoveredDay is not null)
        {
            await _kapPublicCursors.SetAsync(walk.Cursor, cancellationToken).ConfigureAwait(false);
        }

        foreach (var group in walk.Facts.GroupBy(fact => fact.Symbol, StringComparer.Ordinal))
        {
            symbolStats.Add(new SymbolRunStat(group.Key, group.Count(), 0, 0, false));
        }

        _logger.LogInformation(
            "disclosures: public-site market pass done — {Windows} window(s) listed, {Walked} walked ({Empty} honestly empty), {Rows} row(s) listed, {Facts} fact(s) for {Bound} bound symbol(s), {OutOfBound} out-of-bound row(s), {Seen} already seen, truncated {Truncated}, watermark {Watermark}",
            walk.WindowsListed,
            walk.WindowsWalked,
            walk.HonestEmptyWindows,
            walk.RowsListed,
            walk.Facts.Count,
            boundSymbols.Count,
            walk.OutOfBoundRows,
            walk.AlreadySeenRows,
            walk.Truncated,
            walk.LastCoveredDay?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "(unchanged)");

        if (walk.Truncated)
        {
            _logger.LogWarning(
                "disclosures: public-site market pass stopped at its configured bound ({ReasonCode}); the watermark keeps the last completed window and the next pass resumes there",
                walk.TruncationReason);
        }

        var result = new IngestResult(
            Code,
            inserted,
            unchanged,
            quarantined,
            IngestResult.Succeeded)
        {
            SymbolStats = symbolStats,
            Skipped = walk.OutOfBoundRows,
        };

        if (walk.Failure is { } failure)
        {
            // A list source-down is a failed run with the counts it actually earned: the completed
            // windows are stored and the watermark that covers them, so neither "written 0" nor
            // "complete" would be honest.
            _logger.LogWarning(
                "disclosures: public-site market list failed for {From}..{To} ({ReasonCode}): {Detail}; {Written} row(s) were written before it and the saved watermark stands",
                failure.WindowFrom,
                failure.WindowTo,
                failure.ReasonCode,
                failure.Detail,
                inserted);
            return result with
            {
                Status = IngestResult.Failed,
                FailureReason = $"{failure.ReasonCode}: the public-site market list failed for {failure.WindowFrom:yyyy-MM-dd}..{failure.WindowTo:yyyy-MM-dd} ({failure.Detail})",
            };
        }

        if (quarantined > 0 || identityIsolated > 0 || walk.Truncated)
        {
            return result with { Status = quarantined > 0 || identityIsolated > 0 ? IngestResult.Partial : IngestResult.Succeeded };
        }

        return result;
    }

    private Task QuarantineAsync(
        SourcePayload<DisclosuresPayload>? fetched,
        string payloadJson,
        string reasonCode,
        CancellationToken cancellationToken) =>
        _quarantine.WriteAsync(
            new QuarantineEntry(Code, fetched?.SourceRef, payloadJson, reasonCode, _clock.GetUtcNow()),
            cancellationToken);
}
