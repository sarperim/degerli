using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Degerli.Api.Identity;

/// <summary>
/// Email-confirmation token provider used by the Identity module. It is byte-compatible
/// with the framework's <see cref="DataProtectorTokenProvider{TUser}"/> token format
/// (creation-ticks, user id, purpose, security stamp — all data-protected), but it
/// sources its clock from the injected <see cref="TimeProvider"/> instead of
/// <c>DateTimeOffset.UtcNow</c>. That lets the fake clock drive the 48h
/// verification-token expiry exactly as the ACC test plan requires
/// (`.pipeline/testing/user-accounts.md` TC-ACC-012) with no wall-clock wait.
///
/// It is registered under a dedicated provider name
/// (<see cref="ProviderName"/>) so the framework default that password-reset tokens use
/// is left untouched.
/// </summary>
public sealed class DegerliDataProtectorTokenProvider<TUser> : DataProtectorTokenProvider<TUser>
    where TUser : class
{
    /// <summary>Token-provider name wired to <c>Tokens.EmailConfirmationTokenProvider</c>.</summary>
    public const string ProviderName = "DegerliEmailConfirmation";

    private static readonly UTF8Encoding TokenEncoding = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly TimeProvider _clock;

    public DegerliDataProtectorTokenProvider(
        IDataProtectionProvider dataProtectionProvider,
        IOptions<DataProtectionTokenProviderOptions> options,
        ILogger<DataProtectorTokenProvider<TUser>> logger,
        TimeProvider clock)
        : base(dataProtectionProvider, options, logger)
        => _clock = clock;

    public override async Task<string> GenerateAsync(string purpose, UserManager<TUser> manager, TUser user)
    {
        var userId = await manager.GetUserIdAsync(user);
        string? stamp = null;
        if (manager.SupportsUserSecurityStamp)
        {
            stamp = await manager.GetSecurityStampAsync(user);
        }

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, TokenEncoding, leaveOpen: true))
        {
            writer.Write(_clock.GetUtcNow().UtcTicks);
            writer.Write(userId);
            writer.Write(purpose ?? string.Empty);
            writer.Write(stamp ?? string.Empty);
        }

        return Convert.ToBase64String(Protector.Protect(stream.ToArray()));
    }

    public override async Task<bool> ValidateAsync(string purpose, string token, UserManager<TUser> manager, TUser user)
    {
        try
        {
            var bytes = Protector.Unprotect(Convert.FromBase64String(token));
            using var stream = new MemoryStream(bytes);
            using var reader = new BinaryReader(stream, TokenEncoding, leaveOpen: true);

            var createdTicks = reader.ReadInt64();
            if (new DateTimeOffset(createdTicks, TimeSpan.Zero) + Options.TokenLifespan < _clock.GetUtcNow())
            {
                return false;
            }

            if (!string.Equals(reader.ReadString(), await manager.GetUserIdAsync(user), StringComparison.Ordinal))
            {
                return false;
            }

            if (!string.Equals(reader.ReadString(), purpose, StringComparison.Ordinal))
            {
                return false;
            }

            var stamp = reader.ReadString();
            if (reader.BaseStream.Position != reader.BaseStream.Length)
            {
                return false;
            }

            if (manager.SupportsUserSecurityStamp)
            {
                return string.Equals(stamp, await manager.GetSecurityStampAsync(user), StringComparison.Ordinal);
            }

            return stamp.Length == 0;
        }
        catch
        {
            return false;
        }
    }
}
