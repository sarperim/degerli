using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Statements;
using Degerli.Ingestion.Universe;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.KapPublic;

/// <summary>
/// The <c>kap-public-identity</c> job (01 v1.5.61 §10.9.19, TKT-foundation-087; FR-MDF-002/008/012/
/// FR-MDF-016, UC-MDF-002/004): the <b>production invocation surface</b> for the public-site company
/// identity resolver.
/// <para>
/// The recorded blocker this job closes: <see cref="KapPublicCompanyIdentityResolver.ResolveAsync"/>
/// had no caller outside tests, so <c>kap_public_company_map</c> never populated and a live
/// <c>Source = kap-public</c> statements walk isolated every company as
/// <c>COMPANY_IDENTITY_UNRESOLVED</c> and ingested nothing. Registering this
/// <see cref="IIngestJob"/> — one line beside its lane peers in the composition root — makes the
/// pass discoverable by the runner, the run ledger, the admin summary's every-registered-job list
/// (UXR-MDF-003) and the admin trigger <c>POST /api/v1/admin/ingest/{job}/run</c> (UC-MDF-002).
/// </para>
/// <para>
/// <b>Scheduling posture — the shares-lane precedent (01 §10.9.11):</b> admin-triggerable,
/// <b>not</b> a step of <see cref="Scheduling.EodIngestChain"/> or
/// <see cref="Scheduling.MorningIngestChain"/>. A company's public oid changes rarely, so a
/// scheduled cadence is a recorded later decision, never a silent EOD/morning cost.
/// </para>
/// <para>
/// <b>One paced pass, and no request path of its own.</b> The bind is the universe the
/// <c>kap-public</c> statements walk binds — the statements lane's configured
/// <see cref="StatementsSourceOptions.UniverseIndex"/>, resolved to instruments through
/// <see cref="KapPublicStatementTargets.GetInstrumentIdsAsync"/> (never a hardcoded symbol list,
/// never a new configuration key). Every request is the merged <b>079</b> client's: ≥3 s
/// configurable pacing, one flight gate, strictly sequential, honest User-Agent, the ratified access
/// posture gate. This job adds no <c>HttpClient</c>, no pacer and no parallelism.
/// </para>
/// <para>
/// <b>Honest statuses, mapped — never re-interpreted:</b>
/// <list type="bullet">
/// <item>the resolver's <c>SourceDown</c> is a <b>failed</b> run naming the resolver's own reason (and
/// the symbol it stopped at) with <b>nothing stored</b> — 080's contract, no partial mapping;</item>
/// <item>the resolver's <c>Ok</c> is a run whose counts are carried through: inserted as
/// <c>written</c>, repaired-in-place as <c>rewritten</c> (a repair is not a fresh write),
/// <c>unchanged</c> as itself, and every unresolved symbol <b>named</b> in the per-symbol ledger
/// stats under the 044 vocabulary (<c>COMPANY_IDENTITY_UNRESOLVED</c>/<c>COMPANY_IDENTITY_AMBIGUOUS</c>)
/// and <b>counted</b> in <c>skipped</c> — so a run that mapped only part of the universe reads
/// <c>partial</c>, never a green run that hides a company with no identity;</item>
/// <item>an <b>empty bound</b> is the honest named empty (<see cref="EmptyBoundUniverse"/> with the
/// index code): zero requests, zero writes, and a status that does not claim anything was
/// resolved. Nothing failed — the cold-start stream is the EOD/morning chain's, and the statements
/// lane names the same gap on its own walk — so this trigger does not manufacture a failure out of
/// it, and it never reports a fabricated resolution zero;</item>
/// <item>a re-run is <b>idempotent</b> (the resolver's store reports <c>unchanged</c> and
/// <c>recorded_at</c> does not move), so an operator can re-trigger freely.</item>
/// </list>
/// </para>
/// </summary>
public sealed class KapPublicIdentitySyncJob : IIngestJob
{
    /// <summary>Stable job code used by the ledger, the admin summary and the admin trigger.</summary>
    public const string Code = "kap-public-identity";

    /// <summary>
    /// The named honest empty of a bound index with no current constituent (a cold-start or
    /// misconfiguration gap). Recorded with the index code, so the run says <b>which</b> bound was
    /// empty instead of reporting a bare 0.
    /// </summary>
    public const string EmptyBoundUniverse = "EMPTY_BOUND_UNIVERSE";

    private readonly UniverseSyncStore _universe;
    private readonly KapPublicStatementTargets _targets;
    private readonly KapPublicCompanyIdentityResolver _resolver;
    private readonly StatementsSourceOptions _options;
    private readonly ILogger<KapPublicIdentitySyncJob> _logger;

    public KapPublicIdentitySyncJob(
        UniverseSyncStore universe,
        KapPublicStatementTargets targets,
        KapPublicCompanyIdentityResolver resolver,
        IOptions<StatementsSourceOptions> options,
        ILogger<KapPublicIdentitySyncJob> logger)
    {
        _universe = universe;
        _targets = targets;
        _resolver = resolver;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public string JobCode => Code;

    /// <inheritdoc />
    public async Task<IngestResult> RunAsync(
        IngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        // The bind: the current constituents of the index the public-site statements lane walks
        // (01 §10.9.19). A bound naming an unpublished index degrades to an empty set (the
        // universe store's own contract), which is named below rather than fetched.
        var constituents = await _universe
            .GetCurrentConstituentSymbolsAsync(_options.UniverseIndex, cancellationToken)
            .ConfigureAwait(false);

        if (constituents.Count == 0)
        {
            // An empty bound has nothing to resolve. Nothing failed, so this is not an error — but
            // it is never silent and never a fabricated resolution zero: the reason and the index
            // are named in the log, and the run reports zero of everything it actually did.
            _logger.LogWarning(
                "kap-public-identity: index {IndexCode} has no current constituents; no identity was resolved for {Date} ({ReasonCode}) — the cold start belongs to the EOD/morning chain, and the statements lane names the same gap",
                _options.UniverseIndex,
                request.Date,
                EmptyBoundUniverse);
            return new IngestResult(Code, 0, 0, 0, IngestResult.Succeeded);
        }

        var instrumentIds = await _targets
            .GetInstrumentIdsAsync(constituents, cancellationToken)
            .ConfigureAwait(false);

        var bindings = new List<KapPublicInstrumentBinding>(constituents.Count);
        var unbound = new List<string>();
        foreach (var symbol in constituents)
        {
            // The universe store joins constituents to `instruments`, so every bound symbol is
            // expected to have an id; a symbol that does not is never guessed at — it is skipped
            // and named with the identity-vocabulary reason below.
            if (instrumentIds.TryGetValue(symbol, out var instrumentId))
            {
                bindings.Add(new KapPublicInstrumentBinding(instrumentId, symbol));
            }
            else
            {
                unbound.Add(symbol);
            }
        }

        var resolution = await _resolver.ResolveAsync(bindings, cancellationToken).ConfigureAwait(false);

        if (resolution.IsSourceDown)
        {
            // The identity source failed: the resolver stored nothing, and the run says so by name
            // (the runner records this reason in the ledger's `error`, FR-MDF-009).
            _logger.LogError(
                "kap-public-identity: the identity source failed at {ReasonCode} ({Detail}); nothing was stored",
                resolution.ReasonCode,
                resolution.Detail);
            return new IngestResult(Code, 0, 0, 0, IngestResult.Failed)
            {
                FailureReason = IngestFailureReason.Compose(
                    resolution.ReasonCode ?? IngestFailureReason.SourceFetchFailed,
                    resolution.Detail),
            };
        }

        var symbolStats = new List<SymbolRunStat>(resolution.UnresolvedCount + unbound.Count);
        foreach (var unresolved in resolution.Unresolved)
        {
            symbolStats.Add(new SymbolRunStat(
                unresolved.Symbol,
                0,
                0,
                0,
                false,
                unresolved.ReasonCode));
        }

        foreach (var symbol in unbound)
        {
            _logger.LogWarning(
                "kap-public-identity: bound symbol {Symbol} has no instrument row and is skipped ({ReasonCode}); the loop continues",
                symbol,
                KapPublicReasonCodes.CompanyIdentityUnresolved);
            symbolStats.Add(new SymbolRunStat(
                symbol,
                0,
                0,
                0,
                false,
                KapPublicReasonCodes.CompanyIdentityUnresolved));
        }

        var skipped = symbolStats.Count;

        // A pass that mapped only part of its bound is `partial`, never `succeeded`: the source
        // answered (so it is not a failure) but the universe is not fully mapped, and the identities
        // it could not answer are named in this run's own stats (FR-MDF-012, NFR-MDF-001).
        var status = skipped > 0 ? IngestResult.Partial : IngestResult.Succeeded;

        _logger.LogInformation(
            "kap-public-identity: pass over {Bound} bound symbol(s) complete — resolved {Resolved}, inserted {Inserted}, updated {Updated}, unchanged {Unchanged}, unresolved {Unresolved}{UnresolvedNames} ({Status})",
            resolution.Bound,
            resolution.Resolved,
            resolution.Inserted,
            resolution.Updated,
            resolution.Unchanged,
            skipped,
            skipped > 0 ? $": {string.Join(", ", symbolStats.Select(stat => stat.Symbol))}" : string.Empty,
            status);

        return new IngestResult(
            Code,
            resolution.Inserted,
            resolution.Unchanged,
            0,
            status,
            Skipped: skipped)
        {
            // A mapping repaired in place is a repair, not a fresh write (the ledger's own rule).
            Rewritten = resolution.Updated,
            SymbolStats = symbolStats,
        };
    }
}
