namespace Degerli.SourceProbes.Reporting;

/// <summary>
/// Credential redaction for everything the probe emits (TKT-foundation-068 acceptance iii).
/// Hostnames and paths are fine to record — they explain which endpoint drifted — but a
/// query value or header value that authenticates must never survive into a report, a log
/// line or an exception message.
/// </summary>
public static class Redactor
{
    /// <summary>The placeholder substituted for a secret.</summary>
    public const string Mask = "<redacted>";

    /// <summary>
    /// Masks the value of every query parameter named in <paramref name="secretParameters"/>
    /// (case-insensitive), any URL userinfo, and — when the query cannot be parsed — the whole
    /// query string. The scheme, host, path and the non-secret parameter names survive.
    /// </summary>
    public static string RedactUrl(string? url, IEnumerable<string> secretParameters)
    {
        if (string.IsNullOrEmpty(url))
        {
            return string.Empty;
        }

        var secrets = new HashSet<string>(secretParameters, StringComparer.OrdinalIgnoreCase);
        var queryStart = url.IndexOf('?', StringComparison.Ordinal);
        if (queryStart < 0)
        {
            return RedactUserInfo(url);
        }

        var prefix = RedactUserInfo(url[..queryStart]);
        var query = url[(queryStart + 1)..];
        var rendered = new List<string>();
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator < 0)
            {
                rendered.Add(pair);
                continue;
            }

            var name = pair[..separator];
            rendered.Add(secrets.Contains(name) ? name + "=" + Mask : pair);
        }

        return prefix + "?" + string.Join('&', rendered);
    }

    /// <summary>Masks a header value when the header authenticates; returns the name otherwise.</summary>
    /// <remarks>
    /// The probe never records header values at all: it reports the <i>names</i> of the
    /// headers it sent so an operator can see that a credential was attached, without the
    /// value leaving the process.
    /// </remarks>
    public static bool IsSecretHeader(string name) =>
        name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("X-Api-Key", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Api-Key", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Cookie", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Scrubs known secret <b>values</b> out of free text (an exception message, a detail
    /// note) so a transport error that echoes the URL cannot leak the credential.
    /// </summary>
    public static string RedactSecrets(string? text, IEnumerable<string?> secrets)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var rendered = text;
        foreach (var secret in secrets)
        {
            if (!string.IsNullOrWhiteSpace(secret) && secret.Length >= 4)
            {
                rendered = rendered.Replace(secret, Mask, StringComparison.Ordinal);
            }
        }

        return rendered;
    }

    private static string RedactUserInfo(string url)
    {
        var scheme = url.IndexOf("://", StringComparison.Ordinal);
        if (scheme < 0)
        {
            return url;
        }

        var authorityStart = scheme + 3;
        var authorityEnd = url.IndexOfAny(['/', '?', '#'], authorityStart);
        var authority = authorityEnd < 0 ? url[authorityStart..] : url[authorityStart..authorityEnd];
        var at = authority.LastIndexOf('@');
        if (at < 0)
        {
            return url;
        }

        var maskedAuthority = Mask + authority[at..];
        return url[..authorityStart] + maskedAuthority + (authorityEnd < 0 ? string.Empty : url[authorityEnd..]);
    }
}
