using System.Text.Json;
using Degerli.Ingestion.Kap;
using Degerli.Ingestion.Sources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.Shares;

/// <summary>
/// The VYK <c>/memberSecurities</c> adapter (01 v1.5.36 §10.9.11, FR-MDF-002/003's lane):
/// <b>one paced gateway call per run</b> (the 046 <c>/members</c> precedent — a single directory
/// snapshot, decoded in one pass; share capital changes rarely, so there is no per-instrument
/// loop and no cursor), decoded into per-member security lines. The run's every gateway call goes
/// through the scoped <see cref="KapRequestPacer"/>, so the free plan's throttle is never crossed.
/// <para>
/// <b>Failure posture:</b> a transport failure, a non-success HTTP status or a gateway fault is a
/// <see cref="SourceFetchException"/> (the run is recorded <c>failed</c>, never a fabricated empty
/// success); a body that is not the <c>/memberSecurities</c> list shape is a
/// <see cref="SourcePayloadException"/> (the job quarantines <c>UNPARSEABLE_PAYLOAD</c>). An
/// <b>empty array</b> is an honest empty list, not a failure.
/// </para>
/// </summary>
public sealed class SharesSourceAdapter : ISourceAdapter<SharesPayload>
{
    private readonly IKapSourceClient _client;
    private readonly KapSourceOptions _options;
    private readonly KapRequestPacer? _pacer;
    private readonly ILogger<SharesSourceAdapter>? _logger;

    public SharesSourceAdapter(IKapSourceClient client, IOptions<KapSourceOptions> options)
        : this(client, options, pacer: null, logger: null)
    {
    }

    public SharesSourceAdapter(
        IKapSourceClient client,
        IOptions<KapSourceOptions> options,
        KapRequestPacer? pacer,
        ILogger<SharesSourceAdapter>? logger)
    {
        _client = client;
        _options = options.Value;
        _pacer = pacer;
        _logger = logger;
    }

    /// <inheritdoc />
    public string SourceName => "kap";

    /// <inheritdoc />
    public async Task<SourcePayload<SharesPayload>> FetchAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        if (!_options.HasMemberSecuritiesPath)
        {
            throw new SourceFetchException(
                "Ingestion:Kap:MemberSecuritiesPath is not configured; the shares lane cannot fetch a share-count snapshot.");
        }

        if (_pacer is not null)
        {
            await _pacer.PaceAsync(cancellationToken).ConfigureAwait(false);
        }

        var raw = await _client.GetStringAsync(_options.MemberSecuritiesPath, cancellationToken).ConfigureAwait(false);
        var payload = Decode(raw);

        _logger?.LogInformation(
            "shares: decoded {Members} member(s), {Lines} security line(s), {Unreadable} line(s) without currentCapital",
            payload.Members.Count,
            payload.Members.Sum(member => member.Lines.Count),
            payload.Members.Sum(member => member.Lines.Count(line => line.CurrentCapital is null)));

        return new SourcePayload<SharesPayload>(SourceName, payload, raw);
    }

    /// <summary>Decodes one <c>/memberSecurities</c> body (tolerant of the array/object wrapping).</summary>
    internal SharesPayload Decode(string raw)
    {
        using var document = ParseDocument(raw);
        var root = document.RootElement;

        var fault = VykFault.TryRead(root);
        if (fault is not null)
        {
            throw new SourceFetchException($"VYK /memberSecurities returned a fault ({fault.Code}): {Redact(fault.Message)}");
        }

        if (!IsMemberSecuritiesShape(root))
        {
            throw new SourcePayloadException(
                raw,
                new JsonException("VYK /memberSecurities body is neither a member list nor a member object."));
        }

        var members = new List<ShareMember>();
        foreach (var element in MemberElements(root))
        {
            members.Add(ShareMember.From(MemberSecuritiesInfo.From(element)));
        }

        return new SharesPayload(members);
    }

    /// <summary>
    /// The real shape is a root array of <c>{member, securities[]}</c> entries; the schema's
    /// declared single-object shape is accepted too. A root object that itself carries
    /// <c>member</c>/<c>securities</c> is <b>one member</b> — it must never be unwrapped by its
    /// <c>securities</c> array, or every security line would be read as a member (empty members,
    /// zero rows, a fabricated empty success). A single-array wrapper is unwrapped only when its
    /// array is member-shaped (or empty); anything else — notably an empty object, which carries
    /// no member at all — is not this resource.
    /// </summary>
    private static IEnumerable<JsonElement> MemberElements(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in root.EnumerateArray())
            {
                yield return element;
            }

            yield break;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        if (IsMemberShaped(root))
        {
            yield return root;
            yield break;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            if (property.Value.EnumerateArray().Any(element => !IsMemberShaped(element)))
            {
                continue;
            }

            foreach (var element in property.Value.EnumerateArray())
            {
                yield return element;
            }

            yield break;
        }
    }

    /// <summary>True when an element carries the member wrapper or its security lines.</summary>
    private static bool IsMemberShaped(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object
        && (element.TryGetProperty("member", out _) || element.TryGetProperty("securities", out _));

    /// <summary>
    /// The real shape is a root array of <c>{member, securities[]}</c> entries; the schema's
    /// declared single-object shape (and a single-array wrapper) is accepted too. Anything else —
    /// notably an empty object, which carries no member at all — is not this resource and must
    /// not decode to a fabricated empty success.
    /// </summary>
    private static bool IsMemberSecuritiesShape(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (IsMemberShaped(root))
        {
            return true;
        }

        // A single-array wrapper (`{ "<something>": [ … ] }`) — the list-shaped VYK convention —
        // but only when its elements are member-shaped (an empty array is an honest empty list).
        return root.EnumerateObject().Any(property =>
            property.Value.ValueKind == JsonValueKind.Array
            && !property.Value.EnumerateArray().Any(element => !IsMemberShaped(element)));
    }

    private string Redact(string? message) => _options.Redact(message);

    private static JsonDocument ParseDocument(string raw)
    {
        try
        {
            return JsonDocument.Parse(raw);
        }
        catch (JsonException exception)
        {
            throw new SourcePayloadException(raw, exception);
        }
    }
}
