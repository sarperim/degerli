namespace Degerli.Ingestion.KapPublic;

/// <summary>
/// The lane-local URI join for the public-site client (01 v1.5.53 §10.9.19). Deliberately a
/// copy of the two-line join the VYK client needs, not a reference to it: the public-site lane
/// shares <b>no</b> type with <c>Ingestion:Kap</c>, so the two sources stay separable
/// and the VYK adapter is never a dependency of this one. The semantics are the same because
/// the requirement is the same: a configured path prefix (a reverse proxy in front of a
/// replayed capture) must survive the join.
/// </summary>
internal static class KapPublicUri
{
    /// <summary>
    /// Ensures <paramref name="baseAddress"/> ends with a directory separator, so a relative
    /// request path is appended to (never substitutes) the configured path prefix. Idempotent;
    /// any query/fragment on the configured base is preserved.
    /// </summary>
    public static Uri WithDirectorySeparator(Uri baseAddress)
    {
        var builder = new UriBuilder(baseAddress);
        if (!builder.Path.EndsWith('/'))
        {
            builder.Path += "/";
        }

        return builder.Uri;
    }

    /// <summary>
    /// Strips the leading slashes from a configured request path so it stays a
    /// <b>relative</b> URI — a leading slash would make it root-relative (host-root) and drop
    /// the base's path prefix.
    /// </summary>
    public static string ToRelativePath(string path) => path.TrimStart('/');
}
