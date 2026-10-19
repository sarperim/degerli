using System.Text;
using System.Text.Json;
using Degerli.Ingestion.Sources;

namespace Degerli.Ingestion.Kap;

/// <summary>A KAP payload carrying the provenance reference stored with every fact it
/// produces (FR-MDF-008).</summary>
public interface IKapPayload
{
    string? SourceRef { get; }
}

/// <summary>Parses a KAP payload and surfaces malformed bodies as
/// <see cref="SourcePayloadException"/> so the job can quarantine them
/// (<c>UNPARSEABLE_PAYLOAD</c>).</summary>
public static class KapPayload
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static SourcePayload<TPayload> Parse<TPayload>(string raw) where TPayload : class, IKapPayload
    {
        try
        {
            var payload = JsonSerializer.Deserialize<TPayload>(raw, JsonOptions)
                ?? throw new JsonException("KAP payload deserialized to null.");
            return new SourcePayload<TPayload>(payload.SourceRef, payload, raw);
        }
        catch (JsonException exception)
        {
            throw new SourcePayloadException(raw, exception);
        }
    }
}

/// <summary>
/// HTTP seam for the KAP-sourced payloads — financial statements, dividends, corporate
/// actions and disclosures (FR-MDF-002..005). KAP has its own base address, so the
/// adapters read through this client rather than the prices-bound <see cref="ISourceClient"/>
/// (TKT-mdf-002). L2 tests point it at the WireMock source double.
/// </summary>
public interface IKapSourceClient
{
    /// <summary>Fetches a KAP resource as a raw string; throws on a non-success response.</summary>
    Task<string> GetStringAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>
/// Base+path composition for the KAP gateways (01 v1.5 §10.9.11). A configured base such as
/// <c>https://apigwdev.mkk.com.tr/api/vyk</c> carries a <b>path prefix</b>, and a request path
/// such as <c>/members</c> is <b>root-relative</b> to .NET's URI resolution — so the naive
/// <c>HttpClient</c> join silently drops <c>/api/vyk</c> and every real-gateway request 404s.
/// Both halves are required: the base must end with a directory separator <b>and</b> the path's
/// leading slashes must be trimmed, so the path always resolves inside the base's directory.
/// </summary>
internal static class KapUri
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

/// <summary>Default <see cref="IKapSourceClient"/>. Mirrors <see cref="HttpSourceClient"/>:
/// a non-success status surfaces as <see cref="SourceFetchException"/> so the job can treat
/// it as a source failure. The VYK gateway requires an HTTP <c>Basic</c> credential, which
/// is attached to the <see cref="HttpClient"/> at construction (a header, never in the URL),
/// so no secret ever travels in <paramref name="path"/>; <see cref="Redact"/> is the belt-and-
/// braces guard for any message that would echo a configured secret.</summary>
public sealed class KapHttpSourceClient : IKapSourceClient
{
    private readonly HttpClient _http;
    private readonly IReadOnlyList<string> _secrets;

    public KapHttpSourceClient(HttpClient http, IEnumerable<string>? secrets = null)
    {
        _http = http;
        _secrets = (secrets ?? []).Where(s => !string.IsNullOrEmpty(s)).ToArray();

        // Half one of the prefix-preserving join (§10.9.11): the base carries a directory
        // separator, so the configured path prefix (`/api/vyk`) survives resolution.
        if (_http.BaseAddress is not null)
        {
            _http.BaseAddress = KapUri.WithDirectorySeparator(_http.BaseAddress);
        }
    }

    /// <inheritdoc />
    public async Task<string> GetStringAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // Half two: a leading-slash path is root-relative to URI resolution and would drop the
        // base's path prefix — always resolve inside the base's directory instead.
        using var response = await _http.GetAsync(KapUri.ToRelativePath(path), cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new SourceFetchException(
                Redact($"KAP source '{_http.BaseAddress}' returned HTTP {(int)response.StatusCode} for '{path}'."));
        }

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces every configured secret with <c>***</c> in a message that could echo it.</summary>
    private string Redact(string message)
    {
        var redacted = message;
        foreach (var secret in _secrets)
        {
            redacted = redacted
                .Replace(secret, "***", StringComparison.Ordinal)
                .Replace(Uri.EscapeDataString(secret), "***", StringComparison.Ordinal);
        }

        return redacted;
    }
}

/// <summary>
/// Shared KAP source configuration (the VYK gateway base address and its credential). The
/// production base address is the MKK "KAP Data Dissemination Services" (KAP VYK) Apinizer
/// gateway (01 v1.5 §10.9.11); the credential is the free-plan <b>API Key / API Secret pair</b>,
/// supplied through the environment (<c>Ingestion__Kap__ApiKey</c>/<c>Ingestion__Kap__ApiSecret</c>)
/// and never committed. <see cref="MembersPath"/>
/// enables the VYK company-scoped statements source: the adapter resolves a BIST symbol to
/// the gateway's <c>companyId</c> through one paced <c>/members</c> fetch per run. When it is
/// unset the adapter keeps the single-resource per-company behavior the canonical/XBRL L2
/// doubles use (01 v1.5 §10.9.10), so no golden changes.
/// </summary>
public sealed class KapSourceOptions
{
    public const string SectionName = "Ingestion:Kap";

    /// <summary>KAP (or source-double) base address.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// The free-plan <b>API Key</b> (OQ-026) — the gateway's HTTP Basic <em>username</em>
    /// (live-verified 2026-10-18). Empty in local/test wiring, where the WireMock double
    /// ignores it. Never committed — supplied through <c>Ingestion__Kap__ApiKey</c>.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The free-plan <b>API Secret</b> — the gateway's HTTP Basic <em>password</em> paired with
    /// <see cref="ApiKey"/> (live-verified 2026-10-18). Empty in local/test wiring. Never
    /// committed — supplied through <c>Ingestion__Kap__ApiSecret</c>.
    /// </summary>
    public string ApiSecret { get; set; } = string.Empty;

    /// <summary>
    /// The <c>/members</c> route (Şirket Listesi) that resolves symbol → <c>companyId</c>.
    /// Empty disables the VYK company-scoped statements source (canonical/XBRL doubles only).
    /// </summary>
    public string MembersPath { get; set; } = string.Empty;

    /// <summary>The <c>/lastDisclosureIndex</c> route — the run's global disclosure anchor.</summary>
    public string LastDisclosureIndexPath { get; set; } = "/lastDisclosureIndex";

    /// <summary>True when a Basic credential can be attached (an API key or secret is present).</summary>
    public bool HasCredential => !string.IsNullOrWhiteSpace(ApiKey) || !string.IsNullOrWhiteSpace(ApiSecret);

    /// <summary>True when the VYK company-scoped source is enabled (a members route is configured).</summary>
    public bool HasMembersPath => !string.IsNullOrWhiteSpace(MembersPath);

    /// <summary>Every configured secret (for redaction / never-echo guarantees).</summary>
    public IEnumerable<string> Secrets =>
        new[] { ApiKey, ApiSecret }.Where(s => !string.IsNullOrEmpty(s));

    /// <summary>
    /// The Basic-auth (username, password) pair for the gateway
    /// (<c>ApiKey</c>:<c>ApiSecret</c>, live-verified 2026-10-18): the API key is the username,
    /// the API secret the password. Both travel only in the <c>Authorization</c> header.
    /// </summary>
    public (string Username, string Password) BasicCredential() => (ApiKey, ApiSecret);

    /// <summary>Replaces every configured secret in <paramref name="message"/> with <c>***</c>.</summary>
    public string Redact(string? message)
    {
        var redacted = message ?? string.Empty;
        foreach (var secret in Secrets)
        {
            redacted = redacted
                .Replace(secret, "***", StringComparison.Ordinal)
                .Replace(Uri.EscapeDataString(secret), "***", StringComparison.Ordinal);
        }

        // Defence in depth: the HTTP Basic token actually placed on the wire is the base64 of
        // "ApiKey:ApiSecret" (see IngestionServiceCollectionExtensions) — a gateway fault
        // echoing the Authorization header must not survive either. ASCII matches the encoder
        // the client uses for the header.
        if (HasCredential)
        {
            var (username, password) = BasicCredential();
            var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}"));
            redacted = redacted.Replace(basic, "***", StringComparison.Ordinal);
        }

        return redacted;
    }
}
