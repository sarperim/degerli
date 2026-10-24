using System.Text.RegularExpressions;

namespace Degerli.Api.Diagnostics;

/// <summary>
/// Scrubs credentials from crash evidence before it is written (`01` §13.1 v1.5.26).
/// Two layers, matching the repo's existing client-failure posture: every configured
/// secret is replaced literally (the KAP <c>KapSourceOptions.Redact</c> convention), and
/// credential-shaped patterns are masked so an unconfigured value — an
/// <c>Authorization</c> header in a stack trace, a <c>password=</c> fragment in a log
/// line — never survives either.
/// </summary>
public sealed partial class CrashEvidenceRedactor
{
    private const string Mask = "***";

    // Secrets shorter than this are ignored: replacing a two-character value would shred
    // the artifact into noise without protecting anything meaningful.
    private const int MinimumSecretLength = 4;

    private readonly string[] _secrets;

    public CrashEvidenceRedactor(IEnumerable<string>? secrets = null)
    {
        _secrets = (secrets ?? Array.Empty<string>())
            .Where(secret => !string.IsNullOrWhiteSpace(secret) && secret.Length >= MinimumSecretLength)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(secret => secret.Length)
            .ToArray();
    }

    /// <summary>Returns <paramref name="text"/> with every configured secret and credential shape masked.</summary>
    public string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var redacted = text;
        foreach (var secret in _secrets)
        {
            redacted = redacted
                .Replace(secret, Mask, StringComparison.Ordinal)
                .Replace(Uri.EscapeDataString(secret), Mask, StringComparison.Ordinal);
        }

        redacted = CredentialAssignmentPattern().Replace(redacted, match => $"{match.Groups[1].Value}={Mask}");
        redacted = BearerTokenPattern().Replace(redacted, $"Bearer {Mask}");
        redacted = BasicTokenPattern().Replace(redacted, $"Basic {Mask}");

        return redacted;
    }

    /// <summary><c>password=…</c>, <c>api_key: …</c>, <c>token=…</c> and their siblings.</summary>
    [GeneratedRegex(
        @"\b(password|passwd|pwd|secret|api[_-]?key|apisecret|access[_-]?token|refresh[_-]?token|token)\b\s*[=:]\s*[^\s;&""',]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CredentialAssignmentPattern();

    /// <summary>An <c>Authorization: Bearer …</c> token echoed into a message or a stack trace.</summary>
    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.CultureInvariant)]
    private static partial Regex BearerTokenPattern();

    /// <summary>A <c>Authorization: Basic …</c> (base64 user:secret) token.</summary>
    [GeneratedRegex(@"Basic\s+[A-Za-z0-9+/]{8,}={0,2}", RegexOptions.CultureInvariant)]
    private static partial Regex BasicTokenPattern();
}
