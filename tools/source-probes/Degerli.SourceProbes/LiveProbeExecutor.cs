using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Degerli.SourceProbes.Contracts;
using Degerli.SourceProbes.Probing;
using Degerli.SourceProbes.Reporting;

namespace Degerli.SourceProbes;

/// <summary>
/// Executes one bounded, paced, single-request live probe and turns whatever came back into a
/// <see cref="ProbeResult"/>. It never retries, never follows a redirect chain, never reads a
/// second resource, and never reports an unreadable answer as clean.
/// </summary>
/// <remarks>
/// Credentials come from the environment only (<c>Ingestion__Kap__ApiKey</c> /
/// <c>Ingestion__Kap__ApiSecret</c>) and are redacted out of the endpoint recorded in the
/// report. The EVDS 3.0 values route is anonymous (<c>01</c> §10.9.12) — no credential is
/// read or sent for it.
/// </remarks>
public sealed class LiveProbeExecutor
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    private readonly HttpClient _http;
    private readonly string? _symbolOverride;

    /// <summary>Creates the executor.</summary>
    public LiveProbeExecutor(HttpClient? http = null, string? symbolOverride = null)
    {
        _http = http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        _http.Timeout = RequestTimeout;
        _symbolOverride = symbolOverride;
    }

    /// <summary>Probes one source.</summary>
    public async Task<ProbeResult> ProbeAsync(
        SourceProbe probe,
        ProbeContract contract,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(contract);

        if (!probe.HasEndpoint)
        {
            return ProbeResult.Unreachable(probe.Key, string.Empty, probe.UnconfiguredReason ?? "no endpoint.", now);
        }

        var secrets = new List<string?>();
        var url = Expand(probe.UrlTemplate, now, _symbolOverride);
        var redacted = Redactor.RedactUrl(url, probe.SecretQueryParameters);

        using var request = new HttpRequestMessage(new HttpMethod(probe.Method), url);
        if (!string.IsNullOrEmpty(probe.RequestBody))
        {
            // A POST body carries the same {placeholders} as the URL, so a request is built
            // from one clock reading and never from a literal captured date.
            var body = Expand(probe.RequestBody, now, _symbolOverride);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        switch (probe.SourceRef.ToLowerInvariant())
        {
            case "kap":
                var key = Environment.GetEnvironmentVariable("Ingestion__Kap__ApiKey");
                var secret = Environment.GetEnvironmentVariable("Ingestion__Kap__ApiSecret");
                secrets.Add(key);
                secrets.Add(secret);
                if (string.IsNullOrWhiteSpace(key) && string.IsNullOrWhiteSpace(secret))
                {
                    return ProbeResult.Unreachable(
                        probe.Key,
                        redacted,
                        "no KAP credential in the environment (Ingestion__Kap__ApiKey/ApiSecret); the VYK gateway "
                        + "answers unauthenticated requests, so this is not a shape observation.",
                        now);
                }

                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Basic",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes($"{key}:{secret}")));
                break;

            default:
                // EVDS 3.0 (§10.9.12) and every public source are anonymous: a credential is
                // neither required nor sent. Requiring one here would make an anonymous route
                // permanently "unreachable" and hide its shape.
                break;
        }

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            var detail = status == (int)HttpStatusCode.OK
                ? $"compared against {contract.Double.CatalogEntry} ({contract.Double.Provenance})"
                : $"HTTP {status} — the double serves {contract.ExpectedStatus}; observed envelope shape only";

            return ProbeRunner.Evaluate(probe.Key, redacted, contract, body, ProbeMode.Live, status, detail, now);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ProbeResult.Unreachable(
                probe.Key,
                redacted,
                "transport failure: " + Redactor.RedactSecrets(ex.Message, secrets),
                now);
        }
    }

    /// <summary>
    /// Expands the <c>{placeholders}</c> a probe's URL template (or POST body) carries. Bounded
    /// and deterministic: yesterday's date for the date-range sources, the captured disclosure /
    /// company identifiers for the KAP list, and a single EVDS series.
    /// </summary>
    internal static string Expand(string template, DateTimeOffset now, string? symbolOverride)
    {
        var yesterday = now.ToUniversalTime().Date.AddDays(-1);
        var date = yesterday.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        return template
            .Replace("{symbol}", symbolOverride ?? "ASELS", StringComparison.Ordinal)
            .Replace("{date}", date, StringComparison.Ordinal)
            .Replace("{from}", date, StringComparison.Ordinal)
            .Replace("{to}", date, StringComparison.Ordinal)
            .Replace("{startDate}", date, StringComparison.Ordinal)
            .Replace("{endDate}", date, StringComparison.Ordinal)
            .Replace("{index}", "XU100", StringComparison.Ordinal)
            .Replace("{disclosureIndex}", "1226255", StringComparison.Ordinal)
            .Replace("{companyId}", "1001", StringComparison.Ordinal)
            .Replace("{series}", "TP.DK.USD.S.YTL", StringComparison.Ordinal);
    }
}
