using System.Text.Json;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Kap;
using Degerli.Ingestion.Quarantine;
using Degerli.Ingestion.Sources;
using Microsoft.Extensions.Logging;

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

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISourceAdapter<DisclosuresPayload> _adapter;
    private readonly DisclosureStore _store;
    private readonly IQuarantineWriter _quarantine;
    private readonly TimeProvider _clock;
    private readonly ILogger<DisclosuresJob> _logger;

    public DisclosuresJob(
        ISourceAdapter<DisclosuresPayload> adapter,
        DisclosureStore store,
        IQuarantineWriter quarantine,
        TimeProvider clock,
        ILogger<DisclosuresJob> logger)
    {
        _adapter = adapter;
        _store = store;
        _quarantine = quarantine;
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

    private Task QuarantineAsync(
        SourcePayload<DisclosuresPayload>? fetched,
        string payloadJson,
        string reasonCode,
        CancellationToken cancellationToken) =>
        _quarantine.WriteAsync(
            new QuarantineEntry(Code, fetched?.SourceRef, payloadJson, reasonCode, _clock.GetUtcNow()),
            cancellationToken);
}
