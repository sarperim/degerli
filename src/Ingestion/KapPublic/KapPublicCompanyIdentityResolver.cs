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
/// <c>cmpOrFundCode</c>, <b>comma-split into tokens</b> (trimmed, ordinal-ignore-case), carries the
/// bound symbol — the comma-coding convention TKT-foundation-046 fixed for the VYK directory, applied
/// to this source's field (TKT-foundation-091). An exact single-token code is the degenerate case of
/// that rule. A hit for another company is not a match, and where <b>several distinct company
/// candidates</b> carry the token the answer is <b>ambiguous, never picked</b> — the source's answer
/// is a search result list, not a directory of record, so no first-wins/last-wins tie-break is
/// applied; an ambiguity stays unresolved, named and counted, until a rule is evidenced;</item>
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
    /// Matches one symbol's hits to exactly one public oid, or names why it cannot.
    /// <para>
    /// <b>Candidacy is a token rule, not an equality rule (v1.5.65 §10.9.19, TKT-foundation-091).</b>
    /// The site serves several companies' codes <b>comma-joined</b> in one field (<c>"garan,tgb"</c>
    /// — the same convention TKT-foundation-046 fixed for the VYK directory), so a hit is a candidate
    /// when the bound symbol is one of its <b>comma-split tokens</b> (trimmed, ordinal-ignore-case).
    /// An exact single-token code is simply the degenerate case of that rule, so nothing 080 pinned
    /// changes. Only company hits (<c>searchType</c> <c>C</c>) are ever candidates: a fund hit that
    /// spells the same token never binds a stock instrument.
    /// </para>
    /// <para>
    /// <b>Several distinct company candidates sharing the token is an honest ambiguity — never a
    /// pick.</b> This deliberately diverges from 046's VYK <i>last-wins</i> rule: that list is a
    /// <b>directory of record</b> (one row per member, so a tie is a data defect to break
    /// deterministically), whereas this source's answer is a <b>search result list</b> (several real
    /// companies legitimately carry the token), so an ambiguity stays unresolved and <b>named</b>
    /// until a disambiguation rule is evidenced — never smuggled in as a first-wins, a last-wins or a
    /// "closest title".
    /// </para>
    /// <para>
    /// <b>Recorded residue, not fixed here (TKT-foundation-091's DR-3):</b> a malformed
    /// <c>memberOrFundOid</c> is rejected as <c>COMPANY_IDENTITY_UNRESOLVED</c> before ambiguity is
    /// counted, so an answer carrying two company candidates where one oid is malformed reports
    /// <i>unresolved</i> rather than <c>COMPANY_IDENTITY_AMBIGUOUS</c>. Nothing is guessed and
    /// nothing is stored either way — only the reason code is imperfect in that edge — and the
    /// widened candidate set made a pre-existing branch reachable, so changing it is a
    /// classification rule this lane does not smuggle in; it is routed to the user.
    /// </para>
    /// </summary>
    private static Classification Classify(string symbol, IReadOnlyList<KapPublicCompanyHit> hits)
    {
        var companyHits = hits
            .Where(hit => string.Equals(hit.SearchType, KapPublicIdentityReader.CompanySearchType, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var candidates = companyHits
            .Where(hit => CarriesToken(hit.CmpOrFundCode, symbol))
            .ToList();

        if (candidates.Count == 0)
        {
            return Classification.Unresolved(
                hits.Count == 0
                    ? "the identity answer carried no company hit for the symbol"
                    : $"the identity answer carried {hits.Count} company-or-fund hit(s) but none whose comma-split code carries this symbol as a token");
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
            // Name what was refused, so the operator can see the choice the resolver declined to
            // make. Bounded: the detail is a log/stat value, not a dump of the answer.
            var named = string.Join(
                ", ",
                candidates
                    .Select(candidate => candidate.CmpOrFundCode)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Take(5));
            return Classification.Ambiguous(
                $"the identity answer carried {distinct.Count} distinct company candidates whose code carries this symbol as a token" +
                (named.Length > 0 ? $" ({named})" : string.Empty) +
                " — refusing to pick one");
        }

        return new Classification(distinct[0], null, null);
    }

    /// <summary>
    /// Whether one <c>cmpOrFundCode</c> value carries <paramref name="symbol"/> as a
    /// <b>comma-separated token</b> (trimmed, Turkish-dotless-ı folded, ordinal-ignore-case) — the
    /// 046 rule class applied to this source's field. A code that merely <i>contains</i> the symbol
    /// inside a longer token is not a match, and neither is an empty token.
    /// <para>
    /// <b>The Turkish dotless-ı fold (v1.5.75 §10.9.19, TKT-foundation-099) is a comparison-time
    /// rule, never a data mutation.</b> The site spells several companies' codes with the Turkish
    /// dotless <c>ı</c> (U+0131) — BIMAS <c>'bımas'</c>, SISE <c>'sıse'</c>, ENKAI <c>'enkaı'</c>,
    /// ISCTR <c>'ısatr,ısbtr,ısctr,ıskur,tıb'</c> — and <c>OrdinalIgnoreCase</c> folds ASCII case
    /// but <b>not</b> <c>ı</c>↔<c>i</c>: they are distinct Unicode letters and invariant casing maps
    /// neither onto the other (<c>'ı'</c> uppercases to itself; <c>'I'</c> lowercases to <c>'i'</c>).
    /// Both sides are therefore folded (<c>ı</c>→<c>i</c>, and the uppercase dotted <c>İ</c>→<c>i</c>)
    /// before the token equality. The stored mapping, the answer body and the symbol keep their
    /// original spelling — the fold decides a <b>match</b>, it never rewrites anything.
    /// </para>
    /// <para>
    /// The fold is not a general transcription rule and never a tie-break: a token that differs by
    /// any other letter (<c>'ş'</c>, an extra part, a suffix) is still not a match, and a dotless
    /// token shared by several companies is still <c>COMPANY_IDENTITY_AMBIGUOUS</c> — the fold only
    /// lets a candidate be <i>counted</i>, it never chooses between candidates.
    /// </para>
    /// </summary>
    private static bool CarriesToken(string? cmpOrFundCode, string symbol)
    {
        if (string.IsNullOrWhiteSpace(cmpOrFundCode))
        {
            return false;
        }

        var foldedSymbol = FoldTurkishDotlessI(symbol);
        foreach (var token in cmpOrFundCode.Split(','))
        {
            var trimmed = token.Trim();
            if (trimmed.Length > 0
                && string.Equals(FoldTurkishDotlessI(trimmed), foldedSymbol, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Folds the two Turkish i letters that <see cref="StringComparison.OrdinalIgnoreCase"/> does not
    /// equate with ASCII <c>i</c>: the dotless <c>ı</c> (U+0131) and the uppercase dotted <c>İ</c>
    /// (U+0130) both become <c>i</c>. Only the two letters that break this source's codes are
    /// folded — this is not a culture-wide Turkish text rule, it is one comparison's normalization
    /// (TKT-foundation-099), applied to both sides of a token equality and to nothing else.
    /// </summary>
    private static string FoldTurkishDotlessI(string value)
    {
        if (value.AsSpan().IndexOfAny('ı', 'İ') < 0)
        {
            return value;
        }

        return string.Create(value.Length, value, static (folded, source) =>
        {
            for (var index = 0; index < source.Length; index++)
            {
                var character = source[index];
                folded[index] = character is 'ı' or 'İ' ? 'i' : character;
            }
        });
    }
}
