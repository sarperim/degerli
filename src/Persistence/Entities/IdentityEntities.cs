using Microsoft.AspNetCore.Identity;

namespace Degerli.Persistence.Entities;

/// <summary>
/// Application user. Uses ASP.NET Core Identity's standard schema (prefixed
/// <c>asp_net_*</c>, long/bigint keys) extended with the account language
/// preference (FR-ACC-004). Email verification is Identity's
/// <see cref="IdentityUser{TKey}.EmailConfirmed"/> — there is no separate
/// <c>is_verified</c> column (02-data-model §3.6).
/// </summary>
public class ApplicationUser : IdentityUser<long>
{
    /// <summary>Preferred UI language; TR by default (FR-ACC-004).</summary>
    public string LanguagePref { get; set; } = "tr";
}

/// <summary>Application role (Identity standard schema). The <c>builder</c> role is seeded.</summary>
public class ApplicationRole : IdentityRole<long>
{
    public ApplicationRole() { }

    public ApplicationRole(string roleName) : base(roleName) { }
}

/// <summary>
/// KVKK consent record (ACC §7; NFR-ACC-001/004). One per account per notice
/// version. <see cref="UserId"/> is nullable and has no cascade delete so the
/// evidence survives account deletion, identified only by
/// <see cref="UserRefHash"/> (02-data-model §5.4).
/// </summary>
public class ConsentRecord
{
    public long Id { get; set; }

    /// <summary>FK to <c>asp_net_users</c>; set to NULL on account deletion.</summary>
    public long? UserId { get; set; }

    /// <summary>Anonymized identifier retained after deletion.</summary>
    public string UserRefHash { get; set; } = null!;

    public string NoticeVersion { get; set; } = null!;

    public DateTimeOffset ConsentedAt { get; set; }

    /// <summary>Recorded action; V1 only has <c>register</c>.</summary>
    public string Action { get; set; } = null!;
}
