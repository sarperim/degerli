using Degerli.Ingestion.Kap;

namespace Degerli.Ingestion.Shares;

/// <summary>
/// One decoded VYK security line, reduced to the facts the share-count store needs
/// (01 v1.5.36 §10.9.11). <see cref="CurrentCapital"/> is <c>null</c> when the gateway line
/// carries no value — the line is then <b>skipped and counted</b>, never treated as a zero.
/// </summary>
public sealed record ShareSecurityLine(
    string? Isin,
    string? BorsaKodu,
    string? TertipGroup,
    decimal? CurrentCapital,
    bool TradingOpen)
{
    public static ShareSecurityLine From(SecurityLineInfo line) => new(
        Trimmed(line.Isin),
        Trimmed(line.BorsaKodu),
        Trimmed(line.TertipGroup),
        line.CurrentCapital,
        line.TradingOpen);

    /// <summary>Codes are trimmed: the real payload pads some tertip groups (<c>" A  "</c>).</summary>
    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// One VYK member and its decoded security lines. A member with no securities decodes to an
/// empty <see cref="Lines"/> list (the live payload's codeless financing/auditor members) — never
/// a fabricated line. The member's own identity is not carried: every line's <c>borsaKodu</c> /
/// <c>isin</c> resolves the instrument, so no decoded field rides unused.
/// </summary>
public sealed record ShareMember(IReadOnlyList<ShareSecurityLine> Lines)
{
    public static ShareMember From(MemberSecuritiesInfo member) => new(
        member.Securities.Select(ShareSecurityLine.From).ToList());
}

/// <summary>
/// The decoded <c>/memberSecurities</c> payload for one <c>shares</c> run. The payload carries the
/// decoded lines only; the run's <c>skipped</c> count is computed by
/// <see cref="ShareCountStore"/> against <b>our</b> instruments, so a line the source carries for a
/// member we do not cover is out of scope, not a skip.
/// </summary>
public sealed record SharesPayload(IReadOnlyList<ShareMember> Members);
