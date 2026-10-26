using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Kap;
using Degerli.Ingestion.KapPublic;
using Degerli.Ingestion.Quarantine;
using Degerli.Ingestion.Sources;
using Degerli.Ingestion.Universe;
using Degerli.Ingestion.Validation;
using Degerli.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.Statements;

/// <summary>
/// The <c>statements</c> job (FR-MDF-002, UC-MDF-001 step 2). Per 01 v1.5 §10.9.2 the real
/// KAP financial-statement resource is <b>per-company</b> (the §10.9.5 envelope carries one
/// <c>companyCode</c> per payload): the job resolves the current constituents of the index
/// named by <see cref="StatementsSourceOptions.UniverseIndex"/> (<c>index_constituents</c>,
/// <c>effective_to IS NULL</c>) and iterates them <b>strictly sequentially</b>, paced by
/// <see cref="StatementsSourceOptions.RequestDelayMs"/>. The adapter is company-scoped;
/// persistence, validation, quarantine and pacing stay job-side, which is what makes
/// per-company failure isolation expressible (a failed company alerts/quarantines without
/// aborting the run; the run-ledger row carries per-company stats). Everything refused is
/// quarantined, never written.
/// <para>
/// Backfill mode (FR-MDF-010, UC-MDF-002) reuses the same per-company loop; the KAP resource
/// returns the periods the source holds, so the achieved depth is what was stored — never
/// fabricated beyond the source. The per-instrument statements coverage depth is derived from
/// the stored statements by <see cref="Degerli.Ingestion.Coverage.CoverageRecorder"/>'s
/// no-silent-gaps reconcile (FR-MDF-011), not re-invented here.
/// </para>
/// </summary>
public sealed class StatementsJob : IIngestJob
{
    /// <summary>Stable job code used by the ledger, scheduler and admin trigger.</summary>
    public const string Code = "statements";

    /// <summary>Reason code for a per-company transport failure (a source-down, not a parse event).</summary>
    public const string SourceFetchFailed = "SOURCE_FETCH_FAILED";

    /// <summary>
    /// Reason code for a bound symbol the VYK directory could not map to a
    /// <c>companyId</c> (FR-MDF-008/FR-MDF-012, 01 v1.5 §10.9.11): the company is marked
    /// failed and reported, never dropped and never written under a fabricated identity.
    /// </summary>
    public const string CompanyIdentityUnresolved = "COMPANY_IDENTITY_UNRESOLVED";

    /// <summary>
    /// Reason code for a public-site fact withheld because the <b>other</b> source already covers
    /// the same period (01 v1.5.56 §10.9.19, TKT-foundation-082; TC-MDF-110 as amended).
    /// <para>
    /// The incumbent row is retained exactly as it is — not overwritten, not deleted, not
    /// relabelled as a restatement — and the public-site fact is <b>not written</b>. The overlap is
    /// not swallowed as an idempotent <c>unchanged</c> (the store's natural key excludes the source,
    /// so a cross-source fact would otherwise collapse silently): it is a <b>named, counted
    /// finding</b>, quarantined with the evidence that names both sources. Once the source joins the
    /// natural key (TC-MDF-111, TKT-foundation-083) the withheld fact can coexist as its own row —
    /// but the lane has already marked the report completed (its index is in the cursor's seen set
    /// and its cursor was advanced), so re-ingesting it needs an explicit <b>cursor reset</b> or
    /// 083's recorded re-pin, not a later pass of this lane.
    /// </para>
    /// </summary>
    public const string SourceOverlapRetained = "SOURCE_OVERLAP_RETAINED";

    /// <summary>
    /// Reason code for a public-site fact withheld because the other source already covers the
    /// same <b>calendar</b> period under a <b>different period type</b> (01 v1.5.56 §10.9.19,
    /// TKT-foundation-082; review round 1, D5).
    /// <para>
    /// The store's natural key includes <c>periodType</c>, so VYK's <c>FY 2025-12-31</c> and the
    /// public decoder's fail-safe <c>Q 2025-12-31</c> are different keys for <b>one real period</b>.
    /// An exact-key-only overlap check would write a second row for that period with no finding.
    /// The fact is therefore withheld like an overlap, but named under its own reason: the
    /// disagreement is a period-type decode fact an operator must see, not a quiet retention.
    /// </para>
    /// </summary>
    public const string SourcePeriodTypeDisagreement = "SOURCE_PERIOD_TYPE_DISAGREEMENT";

    /// <summary>
    /// Reason code for an <c>Ingestion:Statements:Source</c> value the lane does not recognise.
    /// A typo must <b>fail</b> the run by name: silently running the other source would stamp the
    /// wrong provenance on every fact it wrote and look healthy in the ledger.
    /// </summary>
    public const string InvalidSourceSelection = "INVALID_SOURCE_SELECTION";

    private readonly StatementsSourceAdapter _adapter;
    private readonly FinancialStatementStore _store;
    private readonly IQuarantineWriter _quarantine;
    private readonly IIngestionAlerter _alerter;
    private readonly UniverseSyncStore _universe;
    private readonly KapDisclosureCursorStore _cursorStore;
    private readonly KapPublicSourceReader _kapPublicReader;
    private readonly KapPublicCompanyMapStore _kapPublicMaps;
    private readonly KapPublicStatementTargets _kapPublicTargets;
    private readonly KapPublicStatementCursorStore _kapPublicCursors;
    private readonly KapPublicStatementDepthStore _kapPublicDepths;
    private readonly KapPublicSourceOptions _kapPublicOptions;
    private readonly StatementsSourceOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<StatementsJob> _logger;

    public StatementsJob(
        StatementsSourceAdapter adapter,
        FinancialStatementStore store,
        IQuarantineWriter quarantine,
        IIngestionAlerter alerter,
        UniverseSyncStore universe,
        KapDisclosureCursorStore cursorStore,
        KapPublicSourceReader kapPublicReader,
        KapPublicCompanyMapStore kapPublicMaps,
        DegerliDbContext db,
        IOptions<KapPublicSourceOptions> kapPublicOptions,
        IOptions<StatementsSourceOptions> options,
        TimeProvider clock,
        ILogger<StatementsJob> logger)
    {
        _adapter = adapter;
        _store = store;
        _quarantine = quarantine;
        _alerter = alerter;
        _universe = universe;
        _cursorStore = cursorStore;
        _kapPublicReader = kapPublicReader;
        _kapPublicMaps = kapPublicMaps;
        _kapPublicOptions = kapPublicOptions.Value;
        _options = options.Value;
        _clock = clock;
        _logger = logger;

        // The public lane's own stores (01 v1.5.56 §10.9.19): its cursor and its achieved-depth
        // provenance, both in `coverage_metadata` under data types that are neither the VYK
        // cursor's nor any CoverageRecorder-owned type. They are constructed from the run's
        // context because they are this lane's private write shape, not a shared seam.
        _kapPublicTargets = new KapPublicStatementTargets(db);
        _kapPublicCursors = new KapPublicStatementCursorStore(db);
        _kapPublicDepths = new KapPublicStatementDepthStore(db);
    }

    /// <inheritdoc />
    public string JobCode => Code;

    /// <inheritdoc />
    public async Task<IngestResult> RunAsync(
        IngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        // Which source this lane reads is configuration (01 v1.5.56 §10.9.19). An unrecognised
        // value fails the run <b>by name</b> and fetches nothing: a typo silently falling back to
        // the other source would stamp the wrong provenance on every fact and look healthy.
        if (!StatementsSourceOptions.TryParseSource(_options.Source, out var selectedSource))
        {
            _logger.LogError(
                "statements: unrecognised source selection '{Source}' in {Section}:{Key}; expected '{Vyk}' or '{KapPublic}'. Nothing was fetched ({ReasonCode})",
                _options.Source,
                StatementsSourceOptions.SectionName,
                nameof(StatementsSourceOptions.Source),
                StatementsSourceOptions.VykSource,
                StatementsSourceOptions.KapPublicSource,
                InvalidSourceSelection);
            return new IngestResult(Code, 0, 0, 0, IngestResult.Failed)
            {
                FailureReason = $"{InvalidSourceSelection}: Ingestion:Statements:Source '{_options.Source}' is neither '{StatementsSourceOptions.VykSource}' nor '{StatementsSourceOptions.KapPublicSource}'",
            };
        }

        // The fetch bound: the current constituents of the configured index (01 v1.5 §10.9.2).
        // A bound naming an unpublished index resolves to an empty set (degrade, never throw —
        // the instruments the bound needs may not exist yet).
        var constituents = await ResolveConstituentsAsync(cancellationToken).ConfigureAwait(false);
        if (constituents.Count == 0)
        {
            _logger.LogWarning(
                "statements: index {IndexCode} has no current constituents; nothing to fetch for {Date}",
                _options.UniverseIndex,
                request.Date);
            return EmptyBoundResult();
        }

        if (string.Equals(selectedSource, StatementsSourceOptions.KapPublicSource, StringComparison.Ordinal))
        {
            return await RunKapPublicAsync(request, constituents, cancellationToken).ConfigureAwait(false);
        }

        var written = 0;
        var unchanged = 0;
        var quarantined = 0;
        var failedSymbols = new List<string>();
        var parseFailed = 0;
        var identityIsolated = 0;
        var cursorResets = 0;
        var symbolStats = new List<SymbolRunStat>(constituents.Count);

        for (var index = 0; index < constituents.Count; index++)
        {
            await PaceAsync(index, cancellationToken).ConfigureAwait(false);
            var symbol = constituents[index];

            try
            {
                // The per-company incremental cursor (01 v1.5 §10.9.11): only the VYK source
                // is cursor-driven; the single-resource canonical/XBRL path ignores it (and
                // consults no cursor row), so its L2 goldens are byte-for-byte unchanged.
                var cursor = _adapter.VykEnabled
                    ? await _cursorStore.GetAsync(symbol, cancellationToken).ConfigureAwait(false)
                    : null;

                var fetched = await _adapter
                    .FetchCompanyAsync(symbol, cursor, cancellationToken)
                    .ConfigureAwait(false);
                var source = fetched.Source;

                if (string.IsNullOrWhiteSpace(source.SourceRef))
                {
                    // Provenance is required on every fact (FR-MDF-008): refuse this company's payload.
                    _logger.LogWarning(
                        "statements: company {Symbol} payload for {Date} carries no provenance (MISSING_PROVENANCE)",
                        symbol,
                        request.Date);
                    await QuarantineAsync(null, source.RawJson, QuarantineReason.MissingProvenance, cancellationToken)
                        .ConfigureAwait(false);
                    quarantined++;
                    symbolStats.Add(new SymbolRunStat(symbol, 0, 0, 1, false));
                    continue;
                }

                var outcome = await _store
                    .UpsertAsync(source.SourceRef, _clock.GetUtcNow(), source.Payload, cancellationToken)
                    .ConfigureAwait(false);

                var symbolQuarantined = 0;
                foreach (var reasonCode in outcome.RejectedReasonCodes)
                {
                    _logger.LogWarning(
                        "statements: refused payload for {Symbol} ({ReasonCode})", outcome.Symbol, reasonCode);
                    await QuarantineAsync(source.SourceRef, source.RawJson, reasonCode, cancellationToken)
                        .ConfigureAwait(false);
                    symbolQuarantined++;
                }

                // The per-disclosure isolation evidence (01 v1.5.40/v1.5.42 §10.9.15,
                // TKT-foundation-063): each detail the shared walker could not fetch — an HTTP
                // error, a fault envelope, a transport failure or the bounded timeout — is
                // quarantined with the gateway's own answer verbatim and counted, never a failed
                // company and never a silent omission. The other disclosures' facts were written
                // above; the company's cursor already advanced past the recorded index.
                var skips = fetched.DetailSkips;
                foreach (var skip in skips)
                {
                    _logger.LogWarning(
                        "statements: disclosure detail {Index} for {Symbol} could not be fetched and is quarantined with evidence ({ReasonCode}, {FailureKind}, status {Status}): {Error}",
                        skip.DisclosureIndex,
                        skip.Symbol,
                        skip.ReasonCode,
                        skip.FailureKind,
                        skip.StatusCode,
                        skip.Error);
                    await QuarantineAsync(
                            source.SourceRef,
                            skip.ToEvidenceJson(),
                            VykDisclosureWalker.DisclosureDetailUnfetchable,
                            cancellationToken)
                        .ConfigureAwait(false);
                    symbolQuarantined++;
                }

                written += outcome.Inserted;
                unchanged += outcome.Unchanged;
                quarantined += symbolQuarantined;

                // Advance the company's cursor to the last-processed disclosure index (the
                // disclosure was decoded and persisted — or idempotently collapsed — so the
                // next run resumes after it; FR-MDF-010/011). A stale-cursor reset (01 v1.5.42
                // §10.9.15, TKT-foundation-063) is recorded verbatim and its outcome persisted:
                // the corrected cursor when the re-walk emitted facts, or a cleared row when the
                // re-walk was itself honest-empty — so the next run never repeats the same ER005.
                if (fetched.CursorReset is { } cursorReset)
                {
                    cursorResets++;
                    _logger.LogWarning(
                        "statements: stale disclosure cursor reset for {Symbol}: {From} -> {To} ({Code}); gateway body: {Body}. {Error}",
                        cursorReset.Symbol,
                        cursorReset.FromCursor,
                        cursorReset.ToIndex,
                        cursorReset.Code,
                        cursorReset.Body ?? string.Empty,
                        cursorReset.Error);
                }

                if (fetched.Cursor is not null)
                {
                    await _cursorStore.SetAsync(symbol, fetched.Cursor, cancellationToken).ConfigureAwait(false);
                }
                else if (fetched.CursorReset is not null)
                {
                    // The re-walk was honest-empty; keep the stale value would repeat ER005 forever.
                    await _cursorStore.ClearAsync(symbol, cancellationToken).ConfigureAwait(false);
                }

                symbolStats.Add(new SymbolRunStat(symbol, outcome.Inserted, outcome.Unchanged, symbolQuarantined, false));
            }
            catch (CompanyIdentityUnresolvedException)
            {
                // The bound symbol has no VYK companyId (FR-MDF-008/FR-MDF-012): the company is
                // isolated with the named reason, writes nothing, is named in the per-company
                // stats and never fabricates a mapping — and, per the 046/v1.5.42 honesty pin,
                // never fail-stops the chain on its own.
                _logger.LogWarning(
                    "statements: company {Symbol} could not be mapped to a VYK companyId for {Date} ({ReasonCode}); isolated and the loop continues",
                    symbol,
                    request.Date,
                    CompanyIdentityUnresolved);
                identityIsolated++;
                symbolStats.Add(new SymbolRunStat(symbol, 0, 0, 0, true));
            }
            catch (SourcePayloadException exception)
            {
                // A per-company parse failure is isolated: it quarantines and the loop continues.
                _logger.LogWarning(
                    "statements: company {Symbol} payload for {Date} unparseable (UNPARSEABLE_PAYLOAD)",
                    symbol,
                    request.Date);
                await QuarantineAsync(null, exception.RawJson, QuarantineReason.UnparseablePayload, cancellationToken)
                    .ConfigureAwait(false);
                quarantined++;
                parseFailed++;
                symbolStats.Add(new SymbolRunStat(symbol, 0, 0, 1, false));
            }
            catch (SourceFetchException exception)
            {
                // A transport failure is NOT a parse/validation event: no quarantine. The
                // company is marked failed; the run continues and is alerted once at the end.
                _logger.LogWarning(
                    exception,
                    "statements: company {Symbol} fetch failed for {Date} (SOURCE_FETCH_FAILED)",
                    symbol,
                    request.Date);
                failedSymbols.Add(symbol);
                symbolStats.Add(new SymbolRunStat(symbol, 0, 0, 0, true));
            }
        }

        return await CompleteAsync(
            request,
            constituents.Count,
            written,
            unchanged,
            quarantined,
            failedSymbols,
            parseFailed,
            identityIsolated,
            cursorResets,
            symbolStats,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The public-site run (01 v1.5.56 §10.9.19, TKT-foundation-082; TC-MDF-109). Per bound
    /// company: resolve the <b>stored</b> public oid (TKT-foundation-080 — never a symbol's
    /// spelling), walk the company's financial reports over date windows through the lane's own
    /// paced, single-flight client (079) and decoder (081), and write each report's facts with the
    /// lane's own provenance — unless the other source already covers the period, in which case the
    /// fact is withheld as a named, counted <see cref="SourceOverlapRetained"/> finding.
    /// <para>
    /// <b>Politeness is structural, not advisory.</b> The walk budget is created <b>once per
    /// pass</b> and shared by every company, so the pass's list requests, detail requests and wall
    /// clock are bounded as a whole: the caps are not a per-company allowance that a wide universe
    /// multiplies. Every request is admitted by that budget before it is issued, and the 079 client
    /// keeps its ≥3 s pacing, its single-flight gate, its honest User-Agent and its posture gate
    /// untouched — this job adds no request path of its own.
    /// </para>
    /// </summary>
    private async Task<IngestResult> RunKapPublicAsync(
        IngestionRequest request,
        IReadOnlyList<string> constituents,
        CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var lookbackMonths = _options.KapPublicLookbackMonths > 0 ? _options.KapPublicLookbackMonths : 1;
        var plan = new KapPublicStatementWalkPlan(
            today.AddMonths(-lookbackMonths),
            today,
            _options.KapPublicWindowDays);

        // One budget for the whole pass: the politeness caps bound the run, not each company.
        var budget = new KapPublicWalkBudget(
            _kapPublicOptions.MaxListPages,
            _kapPublicOptions.MaxDetailFetches,
            _kapPublicOptions.MaxWalkDuration,
            _clock);
        var walker = new KapPublicStatementsWalker(
            _kapPublicReader,
            _kapPublicCursors,
            _kapPublicDepths,
            budget,
            _logger);

        var instrumentIds = await _kapPublicTargets
            .GetInstrumentIdsAsync(constituents, cancellationToken)
            .ConfigureAwait(false);
        var publicOids = await _kapPublicMaps
            .GetPublicOidsAsync(instrumentIds.Values.Distinct().ToList(), cancellationToken)
            .ConfigureAwait(false);

        // Fair iteration across passes (review round 1, D2). The budget is run-sticky, so a fixed
        // universe order would let the same head companies spend it every pass and starve the tail
        // forever. Least-recently-walked first — never-walked companies ahead of everyone — makes the
        // omission self-correcting: whoever this pass could not start is at the front of the next.
        var watermarks = await _kapPublicCursors
            .GetWatermarksAsync(constituents, cancellationToken)
            .ConfigureAwait(false);
        var walkOrder = constituents
            .Select((symbol, index) => (
                Symbol: symbol,
                Index: index,
                Watermark: watermarks.TryGetValue(symbol, out var watermark) ? watermark : (DateOnly?)null))
            .OrderBy(candidate => candidate.Watermark.HasValue ? 1 : 0)
            .ThenBy(candidate => candidate.Watermark ?? DateOnly.MinValue)
            .ThenBy(candidate => candidate.Index)
            .Select(candidate => candidate.Symbol)
            .ToList();

        var written = 0;
        var unchanged = 0;
        var quarantined = 0;
        var overlapRetained = 0;
        var identityIsolated = 0;
        var notWalked = 0;
        var truncatedCompanies = 0;
        var failedSymbols = new List<string>();
        var symbolStats = new List<SymbolRunStat>(constituents.Count);

        foreach (var symbol in walkOrder)
        {
            if (budget.Truncated)
            {
                // The pass is bounded: this company was <b>not started</b> at all. Its cursor did not
                // move, and the pass order above puts it near the front of the next pass — so the
                // omission is bounded in passes, never permanent (D2). It is named under its own
                // reason code, distinct from a company that was walked and then truncated.
                notWalked++;
                _logger.LogWarning(
                    "statements: public-site walk for {Symbol} not started — the pass is bounded ({ReasonCode}); the fair pass order resumes it on the next run ({NotStartedReason})",
                    symbol,
                    budget.TruncationReason,
                    KapPublicReasonCodes.WalkNotStarted);
                symbolStats.Add(new SymbolRunStat(symbol, 0, 0, 0, false, KapPublicReasonCodes.WalkNotStarted));
                continue;
            }

            if (!instrumentIds.TryGetValue(symbol, out var instrumentId)
                || !publicOids.TryGetValue(instrumentId, out var publicOid))
            {
                // The stored mapping is the only source of the oid (080): an unmapped company is
                // named and counted, never fetched under a guessed identity.
                identityIsolated++;
                _logger.LogWarning(
                    "statements: company {Symbol} has no stored public-site oid for {Date} ({ReasonCode}); isolated and the loop continues",
                    symbol,
                    request.Date,
                    KapPublicReasonCodes.CompanyIdentityUnresolved);
                await QuarantineAsync(
                        KapPublicSourceOptions.SourceName,
                        IdentityEvidence(symbol),
                        KapPublicReasonCodes.CompanyIdentityUnresolved,
                        cancellationToken)
                    .ConfigureAwait(false);
                symbolStats.Add(new SymbolRunStat(symbol, 0, 0, 1, false, KapPublicReasonCodes.CompanyIdentityUnresolved));
                quarantined++;
                continue;
            }

            // What the other source already holds for this instrument: the coexistence inputs are
            // read once per company, before the walk, and every fact is tested against them.
            var foreignPeriods = await _kapPublicTargets
                .GetForeignPeriodsAsync(instrumentId, KapPublicSourceOptions.SourceName, cancellationToken)
                .ConfigureAwait(false);

            var symbolQuarantined = 0;
            var symbolOverlap = 0;

            // Nothing here guards a SourceFetchException from the walk (review round 2, D15): the
            // reader (079) converts every transport failure, non-success and timeout into its own
            // SourceDown result — a list failure becomes KapPublicStatementFailure, a detail failure
            // a per-report skip — so the walk does not throw for one. The old catch was therefore
            // unreachable, and had it ever fired mid-walk it would have reported `written 0` beside
            // rows already stored (the D1 lie). The accounted paths above are the only ones.
            var walk = await walker
                .WalkAsync(
                    symbol,
                    publicOid,
                    plan,
                    async (report, disclosureIndex, token) =>
                    {
                        var keep = new List<StatementFact>(report.Payload.Statements.Count);
                        var retained = new List<(StatementFact Fact, ForeignStatementPeriod Incumbent)>();
                        var disagreed = new List<(StatementFact Fact, ForeignStatementPeriod Incumbent)>();
                        foreach (var fact in report.Payload.Statements)
                        {
                            // The coexist rule asks two questions (D5): the store's exact key, and
                            // the calendar period under any period type. Either answer means the
                            // period is already covered — the second is a period-type disagreement,
                            // named under its own code rather than written as a duplicate.
                            if (foreignPeriods.TryExact(fact.PeriodType, fact.PeriodEndDate, fact.StatementType, out var incumbent))
                            {
                                retained.Add((fact, incumbent));
                                continue;
                            }

                            var sameCalendarPeriod = foreignPeriods.AtCalendarPeriod(fact.PeriodEndDate, fact.StatementType);
                            if (sameCalendarPeriod.Count > 0)
                            {
                                disagreed.Add((fact, sameCalendarPeriod[0]));
                                continue;
                            }

                            keep.Add(fact);
                        }

                        var outcome = keep.Count == 0
                            ? new StatementWriteResult(symbol, 0, 0, [])
                            : await _store
                                .UpsertAsync(
                                    KapPublicSourceOptions.SourceName,
                                    now,
                                    report.Payload with { Statements = keep },
                                    token)
                                .ConfigureAwait(false);

                        foreach (var (fact, incumbent) in retained)
                        {
                            _logger.LogWarning(
                                "statements: public-site fact withheld for {Symbol} {PeriodType} {PeriodEnd} {StatementType} ({ReasonCode}) — the incumbent source {Incumbent} already covers the period; the incumbent row is retained as-is",
                                symbol,
                                fact.PeriodType,
                                fact.PeriodEndDate,
                                fact.StatementType,
                                SourceOverlapRetained,
                                incumbent.SourceRef);
                            await QuarantineAsync(
                                    KapPublicSourceOptions.SourceName,
                                    OverlapEvidence(symbol, disclosureIndex, fact, incumbent.SourceRef),
                                    SourceOverlapRetained,
                                    token)
                                .ConfigureAwait(false);
                            symbolQuarantined++;
                            symbolOverlap++;
                        }

                        foreach (var (fact, incumbent) in disagreed)
                        {
                            _logger.LogWarning(
                                "statements: public-site fact withheld for {Symbol} {PeriodType} {PeriodEnd} {StatementType} ({ReasonCode}) — the incumbent source {Incumbent} covers the same calendar period as {IncumbentPeriodType}; one real period, two period-type keys",
                                symbol,
                                fact.PeriodType,
                                fact.PeriodEndDate,
                                fact.StatementType,
                                SourcePeriodTypeDisagreement,
                                incumbent.SourceRef,
                                incumbent.PeriodType);
                            await QuarantineAsync(
                                    KapPublicSourceOptions.SourceName,
                                    PeriodTypeDisagreementEvidence(symbol, disclosureIndex, fact, incumbent),
                                    SourcePeriodTypeDisagreement,
                                    token)
                                .ConfigureAwait(false);
                            symbolQuarantined++;
                            symbolOverlap++;
                        }

                        foreach (var reasonCode in outcome.RejectedReasonCodes)
                        {
                            _logger.LogWarning(
                                "statements: refused public-site payload for {Symbol} ({ReasonCode})", symbol, reasonCode);
                            await QuarantineAsync(KapPublicSourceOptions.SourceName, null, reasonCode, token)
                                .ConfigureAwait(false);
                            symbolQuarantined++;
                        }

                        return new KapPublicStatementWrite(
                            outcome.Inserted,
                            outcome.Unchanged,
                            retained.Count + disagreed.Count);
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            // Per-report isolation (the 063 doctrine, applied to this source): a report the
            // source could not deliver — or delivered in a shape that is not a report — is
            // quarantined with its evidence and counted; the company's other reports were
            // written. A transport failure is never counted as a parse event, so it travels
            // under the unfetchable-detail word, not under SOURCE_FETCH_FAILED.
            symbolQuarantined += await QuarantineSkipsAsync(walk.Skips, cancellationToken).ConfigureAwait(false);

            written += walk.Inserted;
            unchanged += walk.Unchanged;
            quarantined += symbolQuarantined;
            overlapRetained += symbolOverlap;

            if (walk.Failure is { } failure)
            {
                // A mid-walk list source-down (D1): the company is failed, but the counts above are
                // the ones it actually earned — the windows it completed were written and are in
                // the store. Reporting `written 0` beside existing rows would be a false ledger;
                // reporting the company as complete would be the opposite lie. Both are avoided:
                // the company is failed with its real counts, and the run is `partial` because rows
                // exist.
                failedSymbols.Add(symbol);
                _logger.LogWarning(
                    "statements: company {Symbol} public-site list failed for {Date} ({ReasonCode}) over {From}..{To} after writing {Written} row(s) and keeping {Unchanged} unchanged; the completed position is persisted and the next pass resumes there",
                    symbol,
                    request.Date,
                    SourceFetchFailed,
                    failure.WindowFrom,
                    failure.WindowTo,
                    walk.Inserted,
                    walk.Unchanged);
                symbolStats.Add(new SymbolRunStat(
                    symbol,
                    walk.Inserted,
                    walk.Unchanged,
                    symbolQuarantined,
                    true,
                    SourceFetchFailed));
                continue;
            }

            if (walk.Truncated)
            {
                truncatedCompanies++;
                _logger.LogWarning(
                    "statements: public-site walk for {Symbol} was bounded ({ReasonCode}) after {ReportsFetched} report(s); the cursor keeps the completed position and the next pass resumes there",
                    symbol,
                    walk.TruncationReason,
                    walk.ReportsFetched);
            }

            symbolStats.Add(new SymbolRunStat(
                symbol,
                walk.Inserted,
                walk.Unchanged,
                symbolQuarantined,
                false,
                walk.Truncated ? walk.TruncationReason : null));
        }

        return await CompleteAsync(
            request,
            constituents.Count,
            written,
            unchanged,
            quarantined,
            failedSymbols,
            parseFailed: 0,
            identityIsolated,
            cursorResets: 0,
            symbolStats,
            cancellationToken,
            truncatedWalk: truncatedCompanies > 0 || notWalked > 0,
            notWalked,
            overlapRetained).ConfigureAwait(false);
    }

    /// <summary>
    /// Quarantines the walk's per-report skips with their evidence and their honest reason
    /// (unparseable payload vs unfetchable detail), returning how many were written. A transport
    /// failure is never counted as a parse event.
    /// </summary>
    private async Task<int> QuarantineSkipsAsync(
        IReadOnlyList<KapPublicStatementSkip> skips,
        CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var skip in skips)
        {
            var reasonCode = string.Equals(
                skip.ReasonCode,
                KapPublicReasonCodes.UnparseablePayload,
                StringComparison.Ordinal)
                ? QuarantineReason.UnparseablePayload
                : VykDisclosureWalker.DisclosureDetailUnfetchable;
            _logger.LogWarning(
                "statements: public-site report {Index} for {Symbol} could not be walked ({SourceReason}: {Detail}); quarantined with evidence as {ReasonCode}",
                skip.DisclosureIndex,
                skip.Symbol,
                skip.ReasonCode,
                skip.Detail,
                reasonCode);
            await QuarantineAsync(
                    KapPublicSourceOptions.SourceName,
                    skip.EvidenceJson,
                    reasonCode,
                    cancellationToken)
                .ConfigureAwait(false);
            count++;
        }

        return count;
    }

    /// <summary>
    /// The evidence a quarantine row keeps for a withheld cross-source fact: both provenances, the
    /// period, and the report it came from — enough for an operator to see the overlap is a
    /// <b>decision</b>, not a failure. Re-ingesting it after the source joins the natural key needs an
    /// explicit cursor reset (the lane has already marked the report completed), so the note says that
    /// rather than promising a later pass will pick it up (review round 1, D9).
    /// </summary>
    private static string OverlapEvidence(
        string symbol,
        long disclosureIndex,
        StatementFact fact,
        string incumbentSource)
    {
        using var buffer = new MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("symbol", symbol);
            writer.WriteNumber("kapPublicDisclosureIndex", disclosureIndex);
            writer.WriteString("periodType", fact.PeriodType);
            writer.WriteString("periodEndDate", fact.PeriodEndDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteString("statementType", fact.StatementType);
            writer.WriteString("withheldSource", KapPublicSourceOptions.SourceName);
            writer.WriteString("incumbentSource", incumbentSource);
            writer.WriteBoolean("incumbentRetainedAsIs", true);
            writer.WriteString(
                "note",
                "the period is already covered by the incumbent source; the public-site fact is not written, " +
                "the incumbent row is untouched. The report itself is marked completed by this lane, so " +
                "re-ingesting this fact after the source joins the natural key needs a cursor reset (or " +
                "TKT-foundation-083's recorded re-pin), not a later pass by itself");
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// The evidence for a fact withheld because the incumbent covers the same <b>calendar</b> period
    /// under a different period type (D5): the calendar key both sides agree on, each side's period
    /// type, and both provenances — so the disagreement is visible and re-ingestable by decision.
    /// </summary>
    private static string PeriodTypeDisagreementEvidence(
        string symbol,
        long disclosureIndex,
        StatementFact fact,
        ForeignStatementPeriod incumbent)
    {
        using var buffer = new MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("symbol", symbol);
            writer.WriteNumber("kapPublicDisclosureIndex", disclosureIndex);
            writer.WriteString("periodEndDate", fact.PeriodEndDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteString("statementType", fact.StatementType);
            writer.WriteString("withheldSource", KapPublicSourceOptions.SourceName);
            writer.WriteString("withheldPeriodType", fact.PeriodType);
            writer.WriteString("incumbentSource", incumbent.SourceRef);
            writer.WriteString("incumbentPeriodType", incumbent.PeriodType);
            writer.WriteBoolean("incumbentRetainedAsIs", true);
            writer.WriteString(
                "note",
                "the incumbent source already covers this calendar period under a different period type " +
                "(the store's natural key includes the period type, so writing this fact would duplicate one " +
                "real period); the public-site fact is not written and the incumbent row is untouched");
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>The evidence a quarantine row keeps for an unmapped company (no oid was invented).</summary>
    private static string IdentityEvidence(string symbol)
    {
        using var buffer = new MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("symbol", symbol);
            writer.WriteString("reasonCode", KapPublicReasonCodes.CompanyIdentityUnresolved);
            writer.WriteString(
                "note",
                "no stored kap_public_company_map row for this instrument; the oid is never guessed from the symbol's spelling");
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// The run's terminal outcome: aggregate the per-company counts, emit the run summary and
    /// raise a single aggregate alert naming the failed companies (the TC-MDF-073 posture).
    /// <para>
    /// <b>Honest status (01 v1.5.42 §10.9.15, TKT-foundation-063).</b> A run is
    /// <see cref="IngestResult.Failed"/> only when <b>every</b> constituent failed for a genuine
    /// source reason (transport/list non-success, or an unparseable payload) — so the retry
    /// ladder keeps its meaning. A run whose writes are zero because companies were honestly
    /// skipped — a stale-cursor reset, an unfetchable detail, an unresolved identity, a
    /// quarantine — is <see cref="IngestResult.Partial"/>, never <c>failed</c> and never a
    /// chain fail-stop (the 2026-10-23 run-212 shape). Some failed or some facts quarantined →
    /// <see cref="IngestResult.Partial"/>; otherwise <see cref="IngestResult.Succeeded"/>.
    /// </para>
    /// </summary>
    private async Task<IngestResult> CompleteAsync(
        IngestionRequest request,
        int constituentCount,
        int written,
        int unchanged,
        int quarantined,
        List<string> failedSymbols,
        int parseFailed,
        int identityIsolated,
        int cursorResets,
        IReadOnlyList<SymbolRunStat> symbolStats,
        CancellationToken cancellationToken,
        bool truncatedWalk = false,
        int notWalked = 0,
        int overlapRetained = 0)
    {
        if (failedSymbols.Count > 0)
        {
            await _alerter.RaiseAsync(
                new IngestionAlert(
                    JobCode: Code,
                    ReasonCode: SourceFetchFailed,
                    Subject: $"Degerli alert: {Code} source fetch failed for {string.Join(", ", failedSymbols)}",
                    BodyTr: $"Finansal tablo işi {request.Date:yyyy-MM-dd} için şu şirketlerin kaynağından veri çekemedi: {string.Join(", ", failedSymbols)}. Diğer şirketler işlendi; kaynak hatası karantinaya alınmaz (kaynak hatası, ayrıştırma olayı değildir).",
                    BodyEn: $"The statements job could not fetch data for {string.Join(", ", failedSymbols)} on {request.Date:yyyy-MM-dd}. The remaining companies were processed; a source failure is not quarantined (it is a source-down, not a parse event).",
                    SourceRef: null),
                cancellationToken).ConfigureAwait(false);
        }

        // Every constituent a genuine source failure → failed (the retry ladder's meaning). A
        // parse-only world keeps the pre-063 posture (all unparseable → failed). A run that wrote
        // rows is never `failed` even when every company also hit a failure: `failed` means "nothing
        // was ingested", so the partial writes win (review round 1, D1 — a mid-walk list source-down
        // after real writes).
        var allGenuinelyFailed = written == 0 && failedSymbols.Count + parseFailed == constituentCount;

        var status = allGenuinelyFailed
            ? IngestResult.Failed
            : failedSymbols.Count > 0
                || quarantined > 0
                || identityIsolated > 0
                || notWalked > 0
                || (truncatedWalk && written == 0)
                || (written == 0 && cursorResets > 0)
                    ? IngestResult.Partial
                    : IngestResult.Succeeded;

        _logger.LogInformation(
            "statements: run for {Date} complete — constituents {Constituents}, written {Written}, unchanged {Unchanged}, quarantined {Quarantined}, failed {Failed}, identity-isolated {IdentityIsolated}, cursor-resets {CursorResets}, overlap-retained {OverlapRetained}, not-walked {NotWalked}, truncated {Truncated} ({Status})",
            request.Date,
            constituentCount,
            written,
            unchanged,
            quarantined,
            failedSymbols.Count,
            identityIsolated,
            cursorResets,
            overlapRetained,
            notWalked,
            truncatedWalk,
            status);

        return new IngestResult(Code, written, unchanged, quarantined, status) { SymbolStats = symbolStats };
    }

    /// <summary>
    /// The result of a run whose configured bound resolved to no current constituents. A bound
    /// naming an unpublished index degrades to an empty set (never throws). Under the production
    /// launch default <see cref="Xu030LaunchSeed.IndexCode"/> however, an empty bound is a real
    /// cold-start/misconfiguration gap — the universe sync materialised no members — so it is
    /// reported as a failure rather than a silent success that would look healthy in the run
    /// ledger while fetching nothing (01 v1.5 §10.9.2, the prices-job posture).
    /// </summary>
    private IngestResult EmptyBoundResult() =>
        string.Equals(_options.UniverseIndex, Xu030LaunchSeed.IndexCode, StringComparison.Ordinal)
            ? new IngestResult(Code, 0, 0, 0, IngestResult.Failed)
            : new IngestResult(Code, 0, 0, 0, IngestResult.Succeeded);

    private Task<IReadOnlyList<string>> ResolveConstituentsAsync(CancellationToken cancellationToken) =>
        _universe.GetCurrentConstituentSymbolsAsync(_options.UniverseIndex, cancellationToken);

    /// <summary>
    /// Paces the strictly sequential loop (01 v1.5 §10.9.2): waits
    /// <see cref="StatementsSourceOptions.RequestDelayMs"/> between requests, never before the
    /// first. A non-positive delay disables the wait (deterministic L2 runs). The wait is a real
    /// wall-clock timer — a politeness measure, not a scheduling decision — so the TC-MDF-074
    /// lower-bound timing assertion reflects the live posture.
    /// </summary>
    private async Task PaceAsync(int index, CancellationToken cancellationToken)
    {
        if (index == 0 || _options.RequestDelayMs <= 0)
        {
            return;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(_options.RequestDelayMs), cancellationToken).ConfigureAwait(false);
    }

    private Task QuarantineAsync(
        string? sourceRef,
        string? payloadJson,
        string reasonCode,
        CancellationToken cancellationToken) =>
        _quarantine.WriteAsync(
            new QuarantineEntry(Code, sourceRef, payloadJson, reasonCode, _clock.GetUtcNow()),
            cancellationToken);
}
