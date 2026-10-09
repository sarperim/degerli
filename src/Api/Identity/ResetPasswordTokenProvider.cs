using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Degerli.Persistence.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Degerli.Api.Identity;

/// <summary>
/// Identity password-reset token provider (`01` §10.1: 2h single-use; FR-ACC-009,
/// UC-ACC-004). Deliberately a <see cref="IUserTwoFactorTokenProvider{TUser}"/> over
/// the platform <see cref="TimeProvider"/> rather than the default data-protector
/// provider, so the 2h expiry is deterministic under the test fake clock (test plan:
/// "Time is faked (TimeProvider) for reset-token expiry (2h)"). The opaque token binds
/// the user id, issue time and security stamp; a successful reset rotates the security
/// stamp (via <c>UserManager.ResetPasswordAsync</c>), which invalidates the token —
/// that is how single-use is enforced.
/// </summary>
public sealed class ResetPasswordTokenProvider : IUserTwoFactorTokenProvider<ApplicationUser>
{
    private const char Separator = '\u001f';

    private readonly IDataProtector _protector;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _lifespan;

    public ResetPasswordTokenProvider(
        IDataProtectionProvider dataProtectionProvider,
        TimeProvider clock,
        IOptions<AuthOptions> options)
    {
        _protector = dataProtectionProvider.CreateProtector("Degerli.Identity.ResetPassword");
        _clock = clock;
        _lifespan = options.Value.ResetTokenLifespan;
    }

    public async Task<string> GenerateAsync(string purpose, UserManager<ApplicationUser> manager, ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(user);

        var stamp = await manager.GetSecurityStampAsync(user);
        var payload = string.Join(
            Separator,
            user.Id.ToString(CultureInfo.InvariantCulture),
            _clock.GetUtcNow().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            stamp ?? string.Empty,
            purpose ?? string.Empty);

        return WebEncoders.Base64UrlEncode(_protector.Protect(Encoding.UTF8.GetBytes(payload)));
    }

    public async Task<bool> ValidateAsync(string purpose, string token, UserManager<ApplicationUser> manager, ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(user);

        if (!TryUnprotect(token, out var parts))
        {
            return false;
        }

        if (!string.Equals(parts[3], purpose, StringComparison.Ordinal))
        {
            return false;
        }

        if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tokenUserId)
            || tokenUserId != user.Id)
        {
            return false;
        }

        if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var createdMs))
        {
            return false;
        }

        var createdAt = DateTimeOffset.FromUnixTimeMilliseconds(createdMs);
        if (createdAt + _lifespan < _clock.GetUtcNow())
        {
            return false;
        }

        var stamp = await manager.GetSecurityStampAsync(user);
        return string.Equals(parts[2], stamp, StringComparison.Ordinal);
    }

    public Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<ApplicationUser> manager, ApplicationUser user)
        => Task.FromResult(false);

    /// <summary>
    /// Reads the user id embedded in a reset token without validating expiry/stamp, so
    /// the reset endpoint can resolve the target account. The subsequent
    /// <c>UserManager.ResetPasswordAsync</c> performs the full validation.
    /// </summary>
    public bool TryReadUserId(string? token, out long userId)
    {
        userId = 0;
        return TryUnprotect(token, out var parts)
            && long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out userId);
    }

    private bool TryUnprotect(string? token, out string[] parts)
    {
        parts = Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            var bytes = _protector.Unprotect(WebEncoders.Base64UrlDecode(token));
            var split = Encoding.UTF8.GetString(bytes).Split(Separator);
            if (split.Length != 4)
            {
                return false;
            }

            parts = split;
            return true;
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            return false;
        }
    }
}
