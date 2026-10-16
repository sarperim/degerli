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
        var raw = await _client.GetStringAsync(BuildRequestPath(date), cancellationToken).ConfigureAwait(false);

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
    /// Builds the EVDS multi-series request: the five official series, the requested value
    /// date window and the reviewer-facing JSON type, with the configured API key. EVDS
    /// separates series identifiers with <c>-</c> and dates are <c>dd-MM-yyyy</c>.
    /// </summary>
    private string BuildRequestPath(DateOnly date)
    {
        var series = string.Join('-', EvdsSeries.RequestOrder);
        var window = date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        var query = string.Create(
            CultureInfo.InvariantCulture,
            $"?series={Uri.EscapeDataString(series)}&startDate={window}&endDate={window}&type=json&key={Uri.EscapeDataString(_options.EvdsApiKey)}");
        return _options.Path + query;
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
