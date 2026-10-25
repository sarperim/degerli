using Microsoft.Extensions.Logging;

namespace Degerli.Ingestion.KapPublic;

/// <summary>How one identity-resolution pass ended (TC-MDF-105's <c>resolved</c> / <c>source-down</c>).</summary>
public enum KapPublicIdentityResolutionOutcome
{
    /// <summary>The pass read every bound symbol's identity answer; the resolved mappings are stored.</summary>
    Ok,

    /// <summary>The identity source failed — the pass reports it and stores nothing (nothing is fabricated).</summary>
    SourceDown,
}

/// <summary>
/// One bound symbol the pass could <b>not</b> map, with its named reason — the counted, named skip
/// (FR-MDF-012, the 044 <c>COMPANY_IDENTITY_UNRESOLVED</c> doctrine applied to this source). A
/// symbol in this list is never fetched and never guessed at; the loop continues past it.
/// </summary>
/// <param name="Symbol">The bound symbol, by name.</param>
/// <param name="ReasonCode">The named reason (<see cref="KapPublicReasonCodes"/>).</param>
/// <param name="Detail">The human-readable observation behind the reason.</param>
public sealed record KapPublicUnresolvedCompany(string Symbol, string ReasonCode, string? Detail);

/// <summary>
/// The outcome of one resolution pass over the bound universe: what was bound, what resolved, what
/// the store changed, and — by name — which symbols stayed unresolved. A source-down result carries
/// no counts and no writes: the failure is reported, not papered over with partial mappings.
/// </summary>
/// <param name="Outcome">Resolved-and-stored, or source-down.</param>
/// <param name="Bound">How many bindings the pass was given.</param>
/// <param name="Resolved">How many bindings produced a public oid.</param>
/// <param name="Inserted">Mappings newly stored.</param>
/// <param name="Updated">Mappings repaired in place.</param>
/// <param name="Unchanged">Mappings re-resolved to exactly the recorded values.</param>
/// <param name="Unresolved">The bound symbols the pass could not map, named with their reasons.</param>
/// <param name="ReasonCode">The source-down reason (null when <see cref="Outcome"/> is <c>Ok</c>).</param>
/// <param name="Detail">The source-down detail (null when <see cref="Outcome"/> is <c>Ok</c>).</param>
public sealed record KapPublicIdentityResolutionResult(
    KapPublicIdentityResolutionOutcome Outcome,
    int Bound,
    int Resolved,
    int Inserted,
    int Updated,
    int Unchanged,
    IReadOnlyList<KapPublicUnresolvedCompany> Unresolved,
    string? ReasonCode = null,
    string? Detail = null)
{
    /// <summary>True when the identity source failed and nothing was written.</summary>
    public bool IsSourceDown => Outcome == KapPublicIdentityResolutionOutcome.SourceDown;

    /// <summary>The number of bound symbols that stayed unresolved (the counted skip).</summary>
    public int UnresolvedCount => Unresolved.Count;

    /// <summary>The names of the unresolved symbols, in binding order.</summary>
    public IReadOnlyList<string> UnresolvedSymbols =>
        Unresolved.Select(unresolved => unresolved.Symbol).ToList();

    /// <summary>A source-down pass: no counts, no writes, its named reason recorded.</summary>
    public static KapPublicIdentityResolutionResult SourceDown(int bound, string reasonCode, string detail) =>
        new(KapPublicIdentityResolutionOutcome.SourceDown, bound, 0, 0, 0, 0, [], reasonCode, detail);
}

/// <summary>
/// The public-site <b>identity resolution</b> path (01 v1.5.54 §10.9.19, TKT-foundation-080): for
/// every bound instrument it reads the lane's paced identity answer (079's committed capture shape)
/// and stores the ticker → public-oid mapping with its provenance.
/// <para>
/// The rules are the honesty rules the 046 identity doctrine established, applied to this source:
/// </para>
/// <list type="bullet">
/// <item>a hit binds only when it is a <b>company</b> hit (<c>searchType</c> <c>C</c>) whose
/// <c>cmpOrFundCode</c> is the bound symbol — a hit for another company is not a match, and an
/// ambiguous answer is never resolved by picking one;</item>
/// <item>a bound symbol with no such hit is skipped, <b>named</b> and <b>counted</b>
/// (<c>COMPANY_IDENTITY_UNRESOLVED</c> / <c>COMPANY_IDENTITY_AMBIGUOUS</c>) and never fetched —
/// there is no fallback guess;</item>
/// <item>a transport failure or a body that is not the identity shape ends the pass as
/// <b>source-down</b> with its reason: the run reports the failure and <b>stores nothing</b>, so no
/// fabricated mapping can exist;</item>
/// <item>the stored mapping is the only bridge between our instruments and the public site's oid
/// space — a VYK numeric companyId, when the run holds one, is recorded beside it in its own column
/// and never derived from it.</item>
/// </list>
/// </summary>
public sealed class KapPublicCompanyIdentityResolver
{
    private readonly KapPublicIdentityReader _reader;
    private readonly KapPublicCompanyMapStore _store;
    private readonly TimeProvider _clock;
    private readonly ILogger<KapPublicCompanyIdentityResolver> _logger;

    public KapPublicCompanyIdentityResolver(
        KapPublicIdentityReader reader,
        KapPublicCompanyMapStore store,
        TimeProvider clock,
        ILogger<KapPublicCompanyIdentityResolver> logger)
    {
        _reader = reader;
        _store = store;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Resolves <paramref name="bindings"/> and persists the mappings. The walk is strictly sequential
    /// (the client is single-flight and paces every request — a public website, never parallel), stops
    /// at the first source-down, and counts every outcome.
    /// </summary>
    public async Task<KapPublicIdentityResolutionResult> ResolveAsync(
        IReadOnlyList<KapPublicInstrumentBinding> bindings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        var evidenceDate = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        var mappings = new List<KapPublicCompanyMapping>(bindings.Count);
        var unresolved = new List<KapPublicUnresolvedCompany>();

        foreach (var binding in bindings)
        {
            var read = await _reader.ReadCompanyHitsAsync(binding.Symbol, cancellationToken).ConfigureAwait(false);
            if (read.IsSourceDown || read.IsQuarantined)
            {
                // The identity source failed (or answered a body that is not its shape): the pass
                // ends honestly. Nothing is stored — a partial or guessed mapping would be worse
                // than none, and the caller reports the failure.
                var detail = $"{binding.Symbol}: {read.Detail ?? "no detail"}";
                _logger.LogWarning(
                    "kap-public identity resolution stopped at {Symbol} ({ReasonCode}): {Detail}",
                    binding.Symbol,
                    read.ReasonCode,
                    detail);
                return KapPublicIdentityResolutionResult.SourceDown(
                    bindings.Count,
                    read.ReasonCode ?? KapPublicReasonCodes.SourceFetchFailed,
                    detail);
            }

            var hits = read.Payload?.Hits ?? [];
            var classification = Classify(binding.Symbol, hits);
            if (classification.Oid is null)
            {
                unresolved.Add(new KapPublicUnresolvedCompany(
                    binding.Symbol,
                    classification.ReasonCode ?? KapPublicReasonCodes.CompanyIdentityUnresolved,
                    classification.Detail));
                _logger.LogWarning(
                    "kap-public: bound symbol {Symbol} is unresolved and is skipped ({ReasonCode}): {Detail}",
                    binding.Symbol,
                    classification.ReasonCode,
                    classification.Detail);
                continue;
            }

            mappings.Add(new KapPublicCompanyMapping(
                binding.InstrumentId,
                classification.Oid.Value,
                new IdentityEvidence(ProvenanceOf(binding.Symbol), evidenceDate),
                binding.Vyk?.CompanyId,
                binding.Vyk?.Evidence));
        }

        var sync = await _store.SyncAsync(mappings, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "kap-public identity resolution over {Bound} bound symbols: {Resolved} resolved, " +
            "{Unresolved} unresolved, {Inserted} inserted, {Updated} updated, {Unchanged} unchanged",
            bindings.Count,
            mappings.Count,
            unresolved.Count,
            sync.Inserted,
            sync.Updated,
            sync.Unchanged);

        return new KapPublicIdentityResolutionResult(
            KapPublicIdentityResolutionOutcome.Ok,
            bindings.Count,
            mappings.Count,
            sync.Inserted,
            sync.Updated,
            sync.Unchanged,
            unresolved);
    }

    /// <summary>
    /// The provenance recorded with a public oid: the resolution body that produced it (the paced
    /// identity route plus its keyword). The date half of the evidence is the run's own resolution
    /// date, so a stored mapping answers "which body, which date" (FR-MDF-008).
    /// </summary>
    private string ProvenanceOf(string symbol) =>
        $"{_reader.SourceNameOfLane}:{_reader.IdentityPath} keyword={symbol}";

    private sealed record Classification(PublicCompanyOid? Oid, string? ReasonCode, string? Detail)
    {
        public static Classification Unresolved(string detail) =>
            new(null, KapPublicReasonCodes.CompanyIdentityUnresolved, detail);

        public static Classification Ambiguous(string detail) =>
            new(null, KapPublicReasonCodes.CompanyIdentityAmbiguous, detail);
    }

    /// <summary>
    /// Matches one symbol's hits to exactly one public oid, or names why it cannot. Only company hits
    /// (<c>searchType</c> <c>C</c>) whose code is the bound symbol are candidates; anything else is
    /// ignored rather than guessed from.
    /// </summary>
    private static Classification Classify(string symbol, IReadOnlyList<KapPublicCompanyHit> hits)
    {
        var candidates = hits
            .Where(hit => string.Equals(hit.SearchType, KapPublicIdentityReader.CompanySearchType, StringComparison.OrdinalIgnoreCase))
            .Where(hit => string.Equals(hit.CmpOrFundCode, symbol, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count == 0)
        {
            return Classification.Unresolved(
                hits.Count == 0
                    ? "the identity answer carried no company hit for the symbol"
                    : $"the identity answer carried {hits.Count} company-or-fund hit(s) but none for this symbol");
        }

        var oids = new List<PublicCompanyOid>();
        foreach (var candidate in candidates)
        {
            if (!PublicCompanyOid.TryParse(candidate.MemberOrFundOid, out var oid))
            {
                return Classification.Unresolved(
                    $"the hit's memberOrFundOid '{candidate.MemberOrFundOid}' is not a public-site company oid shape");
            }

            oids.Add(oid);
        }

        var distinct = oids.Distinct().ToList();
        if (distinct.Count > 1)
        {
            return Classification.Ambiguous(
                $"the identity answer carried {distinct.Count} distinct oids for one symbol — refusing to pick one");
        }

        return new Classification(distinct[0], null, null);
    }
}
