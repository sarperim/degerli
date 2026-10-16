using System.Globalization;
using System.Text.Json;
using Degerli.Ingestion.Sources;
using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.Macro;

/// <summary>
/// The consolidated macro source adapter (FR-MOV-014, OQ-MOV-005): one TCMB EVDS request
/// carries the five official series (TÜİK CPI, CBRT repo, USD/TRY, EUR/TRY, GOLD), replacing
/// the earlier scattered per-source plan. It fetches the EVDS envelope through the
/// macro-bound <see cref="ISourceClient"/> and maps each observation's EVDS
/// <c>seriesCode</c> back to its canonical macro code so the jobs, the store and the strip
/// are unchanged. Persistence, validation and alerting stay in the jobs. A non-success
/// response surfaces as <see cref="SourceFetchException"/> (the unreachable class) and a
/// malformed body as <see cref="SourcePayloadException"/> (the invalid class).
/// </summary>
public sealed class MacroSourceAdapter : ISourceAdapter<MacroPayload>
{
    /// <summary>The concrete source name recorded for EVDS-sourced values (03 §1).</summary>
    public const string EvdsSource = "evds";

    /// <summary>
    /// The bounded value-date lookback (days) the EVDS request spans (DR-2): EVDS filters by
    /// <em>observation</em> date, so a run-date-only window would never return the monthly
    /// TÜİK CPI (dated month-start) or the per-release CBRT repo (dated its decision date).
    /// 90 days safely covers the longest cadence (monthly) plus its publication lag while
    /// keeping the fetch bounded; re-reads are idempotent in the store.
    /// </summary>
    private const int LookbackDays = 90;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISourceClient _client;
    private readonly MacroSourceOptions _options;

    public MacroSourceAdapter(ISourceClient client, IOptions<MacroSourceOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    /// <inheritdoc />
    public string SourceName => EvdsSource;

    /// <inheritdoc />
    public async Task<SourcePayload<MacroPayload>> FetchAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var raw = await FetchStringAsync(BuildRequestPath(date), cancellationToken).ConfigureAwait(false);

        EvdsMacroPayload? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<EvdsMacroPayload>(raw, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new SourcePayloadException(raw, exception);
        }

        if (envelope is null)
        {
            throw new SourceFetchException("Macro payload deserialized to null.");
        }

        var items = (envelope.Series ?? [])
            .Select(ToMacroItem)
            .Where(item => item is not null)
            .Select(item => item!)
            .ToList();

        var sourceRef = string.IsNullOrWhiteSpace(envelope.SourceRef) ? EvdsSource : envelope.SourceRef;
        return new SourcePayload<MacroPayload>(sourceRef, new MacroPayload(sourceRef, items), raw);
    }

    /// <summary>
    /// Builds the EVDS multi-series request: the five official series, a bounded value-date
    /// window ending at the requested run date, the reviewer-facing JSON type, and the
    /// configured API key. EVDS separates series identifiers with <c>-</c> and dates are
    /// <c>dd-MM-yyyy</c>. The window is a lookback (not a single day) because EVDS filters by
    /// observation date and the periodic series are not dated the run date (DR-2).
    /// </summary>
    private string BuildRequestPath(DateOnly date)
    {
        var series = string.Join('-', EvdsSeries.RequestOrder);
        var end = date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        var start = date.AddDays(-LookbackDays).ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        var query = string.Create(
            CultureInfo.InvariantCulture,
            $"?series={Uri.EscapeDataString(series)}&startDate={start}&endDate={end}&type=json&key={Uri.EscapeDataString(_options.EvdsApiKey)}");
        return _options.Path + query;
    }

    /// <summary>
    /// Fetches through the source client, stripping the EVDS API key from any failure surface
    /// (SEC-1, CWE-598). The key must travel in the query string — the source-client seam has
    /// no header channel — so a non-success response would otherwise produce a
    /// <see cref="SourceFetchException"/> whose message echoes the live secret.
    /// </summary>
    private async Task<string> FetchStringAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.GetStringAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (SourceFetchException exception)
        {
            throw RedactClientFailure(exception);
        }
    }

    private SourceFetchException RedactClientFailure(SourceFetchException exception)
    {
        var message = exception.Message;
        if (!string.IsNullOrEmpty(_options.EvdsApiKey))
        {
            message = message
                .Replace(_options.EvdsApiKey, "***", StringComparison.Ordinal)
                .Replace(Uri.EscapeDataString(_options.EvdsApiKey), "***", StringComparison.Ordinal);
        }

        return string.Equals(message, exception.Message, StringComparison.Ordinal)
            ? exception
            : new SourceFetchException(message);
    }

    /// <summary>
    /// Maps one EVDS observation to a canonical macro item. The EVDS identifier wins when it
    /// is one of the five official series; otherwise the observation's canonical code is used
    /// (the fixture-only independent measure). An observation naming neither is dropped — it
    /// is not a known V1 series and could not be stored.
    /// </summary>
    private static MacroItem? ToMacroItem(EvdsObservation observation)
    {
        var code = ResolveCode(observation);
        return code is null
            ? null
            : new MacroItem(code, observation.ValueDate, observation.Value, observation.Unit, observation.RecordedAt);
    }

    private static string? ResolveCode(EvdsObservation observation)
    {
        if (!string.IsNullOrWhiteSpace(observation.SeriesCode)
            && EvdsSeries.CanonicalByEvdsCode.TryGetValue(observation.SeriesCode, out var canonical))
        {
            return canonical;
        }

        return string.IsNullOrWhiteSpace(observation.Code) ? null : observation.Code;
    }
}
