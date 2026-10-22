using System.Text.Json;
using Degerli.Ingestion.Sources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.Kap;

/// <summary>
/// The per-run snapshot of the VYK company directory (Şirket Listesi) and the global
/// disclosure anchor (01 v1.5 §10.9.11). <see cref="BySymbol"/> maps a BIST stock code to the
/// gateway's <c>companyId</c>. <see cref="Failed"/> records a transport failure of the members
/// fetch so the caller degrades honestly (marks every company failed) instead of fabricating an
/// empty/partial mapping.
/// </summary>
public sealed record VykDirectorySnapshot(
    IReadOnlyDictionary<string, string> BySymbol,
    string? LastDisclosureIndex,
    bool Failed,
    string? FailureReason)
{
    public static readonly VykDirectorySnapshot Empty = new(
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), null, false, null);
}

/// <summary>
/// The identity-isolation channel of a <b>company-scoped</b> VYK adapter (01 v1.5 §10.9.15; the
/// §10.9.10/§10.9.11 per-company isolation doctrine, FR-MDF-012). The three events families own
/// their company loop inside the adapter, so they also own the isolation: a bound symbol the
/// directory cannot map is <b>skipped and named here</b> (the loop continues), and the job turns
/// that list into the run's per-symbol stats — never a whole-run fail-stop.
/// </summary>
public interface IVykIdentityIsolationReporter
{
    /// <summary>
    /// The bound symbols the most recent walk isolated as
    /// <see cref="VykCompanyDirectory.CompanyIdentityUnresolved"/>. Empty when every bound symbol
    /// resolved (or when the run took the legacy single-resource branch).
    /// </summary>
    IReadOnlyList<string> UnresolvedSymbols { get; }
}

/// <summary>
/// Resolves a BIST symbol to the VYK gateway's <c>companyId</c> through <b>one</b> paced
/// <c>/members</c> fetch per run, cached in memory for the whole company-scoped loop
/// (01 v1.5 §10.9.11). <c>MemberInfo.stockCode</c> carries the ticker directly, so no
/// <c>/memberSecurities</c> consult is needed. A member may carry <b>several</b> tickers in one
/// comma-joined code (<c>"GARAN, TGB"</c>), so the map registers every part as its own key
/// (01 v1.5 §10.9.15); literal duplicate entries are last-wins with a logged warning when the
/// ids differ. The fetch is lazy — the members/anchor calls
/// happen only when the VYK company-scoped source is actually enabled and used, so the
/// canonical/XBRL L2 doubles never reach them. <c>/memberSecurities</c>
/// (<c>borsaKodu</c>/<c>isin</c>) remains an unneeded fallback: <c>stockCode</c> is unambiguous
/// for the bound members.
/// </summary>
public sealed class VykCompanyDirectory
{
    /// <summary>
    /// The named reason for a bound symbol the directory cannot map to a <c>companyId</c>
    /// (FR-MDF-008/FR-MDF-012, 01 v1.5 §10.9.11) — the same code the <c>statements</c> lane
    /// records (<c>StatementsJob.CompanyIdentityUnresolved</c>).
    /// </summary>
    public const string CompanyIdentityUnresolved = "COMPANY_IDENTITY_UNRESOLVED";

    private readonly IKapSourceClient _client;
    private readonly KapSourceOptions _options;
    private readonly KapRequestPacer _pacer;
    private readonly ILogger<VykCompanyDirectory> _logger;
    private VykDirectorySnapshot? _snapshot;

    public VykCompanyDirectory(
        IKapSourceClient client,
        IOptions<KapSourceOptions> options,
        KapRequestPacer pacer,
        ILogger<VykCompanyDirectory> logger)
    {
        _client = client;
        _options = options.Value;
        _pacer = pacer;
        _logger = logger;
    }

    /// <summary>True when the VYK company-scoped source is enabled (a members route is configured).</summary>
    public bool Enabled => _options.HasMembersPath;

    /// <summary>
    /// Returns the run's directory snapshot, fetching <c>/members</c> once and caching the
    /// result (including a failure) for the rest of the run.
    /// </summary>
    public async Task<VykDirectorySnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_snapshot is not null)
        {
            return _snapshot;
        }

        if (!_options.HasMembersPath)
        {
            return _snapshot = VykDirectorySnapshot.Empty;
        }

        try
        {
            var members = await FetchMembersAsync(cancellationToken).ConfigureAwait(false);
            var anchor = await FetchAnchorAsync(cancellationToken).ConfigureAwait(false);
            return _snapshot = new VykDirectorySnapshot(members, anchor, Failed: false, FailureReason: null);
        }
        catch (Exception exception) when (exception is SourceFetchException or SourcePayloadException)
        {
            // A source failure (or a body that is not the members shape at all) is a
            // source-down, not a per-company parse event (01 v1.5 §10.9.10): the caller
            // marks every company failed and writes no fabricated mapping.
            _logger.LogWarning(exception, "VYK members directory fetch failed (SOURCE_FETCH_FAILED)");
            return _snapshot = new VykDirectorySnapshot(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                null,
                Failed: true,
                FailureReason: _options.Redact(exception.Message));
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> FetchMembersAsync(CancellationToken cancellationToken)
    {
        await _pacer.PaceAsync(cancellationToken).ConfigureAwait(false);
        var raw = await _client.GetStringAsync(_options.MembersPath, cancellationToken).ConfigureAwait(false);

        using var document = ParseOrThrow(raw);
        var root = document.RootElement;
        var fault = VykFault.TryRead(root);
        if (fault is not null)
        {
            throw new SourceFetchException($"VYK /members returned a fault ({fault.Code}): {_options.Redact(fault.Message)}");
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in VykEnvelopes.Elements(root).ToList())
        {
            var member = MemberInfo.From(element);
            if (string.IsNullOrWhiteSpace(member.StockCode) || string.IsNullOrWhiteSpace(member.Id))
            {
                // Codeless members (auditors: FK/IGS/IGMS/YK memberTypes) carry no ticker and
                // therefore no identity to resolve — skipped, never a fabricated mapping.
                continue;
            }

            var id = member.Id.Trim();
            foreach (var part in member.StockCode.Split(','))
            {
                // A member may carry several tickers in one comma-joined code (live-verified
                // 2026-10-20: Türkiye Garanti Bankası = "GARAN, TGB", 77 of 1037 dev-gateway
                // members). Registering the joined string as one key made every such symbol
                // unresolvable, so each part is its own key (01 v1.5 §10.9.15).
                var code = part.Trim();
                if (code.Length == 0)
                {
                    continue;
                }

                if (map.TryGetValue(code, out var claimed)
                    && !string.Equals(claimed, id, StringComparison.Ordinal))
                {
                    // Two member entries claim the same ticker with different ids (the live
                    // payload carries literal duplicates too): last-wins, never silent.
                    _logger.LogWarning(
                        "VYK members directory: stock code {Code} is claimed by two members ({ExistingId} and {Id}); last-wins",
                        code,
                        claimed,
                        id);
                }

                map[code] = id;
            }
        }

        return map;
    }

    private async Task<string?> FetchAnchorAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.LastDisclosureIndexPath))
        {
            return null;
        }

        try
        {
            await _pacer.PaceAsync(cancellationToken).ConfigureAwait(false);
            var raw = await _client.GetStringAsync(_options.LastDisclosureIndexPath, cancellationToken).ConfigureAwait(false);
            using var document = ParseOrThrow(raw);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("lastDisclosureIndex", out var value)
                && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
        }
        catch (Exception exception) when (exception is SourceFetchException or SourcePayloadException)
        {
            // The anchor is a secondary read; its unavailability — a transport failure *or* a
            // body that is not the anchor shape at all — must not fail the directory
            // (01 v1.5 §10.9.11). A malformed anchor body must never mark every company failed.
            _logger.LogWarning(exception, "VYK lastDisclosureIndex anchor fetch failed (degraded)");
            return null;
        }
    }

    private static JsonDocument ParseOrThrow(string raw)
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
