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
/// a fabricated line.
/// </summary>
public sealed record ShareMember(string? MemberId, string? Title, IReadOnlyList<ShareSecurityLine> Lines)
{
    public static ShareMember From(MemberSecuritiesInfo member) => new(
        member.MemberId,
        member.Title,
        member.Securities.Select(ShareSecurityLine.From).ToList());
}

/// <summary>
/// The decoded <c>/memberSecurities</c> payload for one <c>shares</c> run.
/// <see cref="SkippedLines"/> counts the security lines the decode could not use because they
/// carry no <c>currentCapital</c> — the run reports the skip (never a zero fill, never a silent
/// drop).
/// </summary>
public sealed record SharesPayload(IReadOnlyList<ShareMember> Members, int SkippedLines);
