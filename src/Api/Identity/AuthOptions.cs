namespace Degerli.Api.Identity;

/// <summary>
/// Identity-module configuration (`01` §10.4). Bound from the <c>Auth</c> section;
/// <c>APP_URL</c> overrides <see cref="AppUrl"/> at deploy time so e-mail links point
/// at the public site.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// Privacy-notice versions currently served. Registration consent must reference
    /// one of these; an unknown version is rejected (I-ACC-3, `03` §6).
    /// </summary>
    public IList<string> NoticeVersions { get; set; } = new List<string> { "2026-10" };

    /// <summary>
    /// Salt for the anonymized consent identifier (<c>user_ref_hash</c>, `02` §5.4).
    /// Not a credential — override per deployment; the default is dev/CI.
    /// </summary>
    public string UserRefHashKey { get; set; } = "degerli-dev-user-ref-hash-key";

    /// <summary>Public base URL used to build the verification link.</summary>
    public string AppUrl { get; set; } = "http://localhost:5173";

    /// <summary>Verification-token lifetime (`01` §10.1: 48h single-use).</summary>
    public TimeSpan VerificationTokenLifespan { get; set; } = TimeSpan.FromHours(48);

    /// <summary>Reset-token lifetime (`01` §10.1: 2h single-use).</summary>
    public TimeSpan ResetTokenLifespan { get; set; } = TimeSpan.FromHours(2);
}
