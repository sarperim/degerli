using System.Globalization;
using System.Text.Json;
using Degerli.Ingestion.Sources;
using Microsoft.Extensions.Options;

namespace Degerli.Ingestion.Macro;

/// <summary>
/// The consolidated macro source adapter (FR-MOV-014, OQ-MOV-005) — the <b>TCMB EVDS 3.0</b>
/// migration (01 v1.5.6 §10.9.12). One adapter carries the five official series (TÜİK CPI,
/// CBRT repo, USD/TRY, EUR/TRY, GOLD), replacing the dead EVDS v2 REST wiring: the v2
/// <c>/service/evds</c> path and its <c>TP.APIFON1</c>/<c>TP.MK.ALTIN</c> series are gone
/// (the whole host now 302-redirects to <c>evds3.tcmb.gov.tr</c>).
///
/// <para>
/// <b>Two transports, one adapter.</b> When the EVDS 3.0 discovery routes are configured
/// (<see cref="MacroSourceOptions.UsesV3Discovery"/>) the adapter drives the new platform:
/// it discovers the series from the anonymous catalog + serieList
/// (<c>GET /igmevdsms-dis/…</c>), then issues the platform's <c>POST /igmevdsms-dis/fe</c>
/// values request — <b>anonymous</b> (no key, no login — live-verified 2026-10-18). The CPI
/// index is converted to the canonical YoY measure deterministically. When discovery is
/// <em>not</em> configured the adapter keeps the legacy single-GET fetch and parses by shape:
/// the real column <c>items</c>/<c>Tarih</c> envelope (via <see cref="EvdsMacroDecoder"/>) and
/// the canonical <c>sourceRef</c>/<c>series</c> source-double envelope (the L2 golden
/// contract, §10.9.7) — detection by shape, never by config.
/// </para>
///
/// <para>
/// Persistence, validation and alerting stay in the jobs. A non-success response surfaces as
/// <see cref="SourceFetchException"/> (the unreachable class) and a malformed body as
/// <see cref="SourcePayloadException"/> (the invalid class).
/// </para>
/// </summary>
public sealed class MacroSourceAdapter : ISourceAdapter<MacroPayload>
{
    /// <summary>The concrete source name recorded for EVDS-sourced values (03 §1).</summary>
    public const string EvdsSource = "evds";

    /// <summary>
    /// The bounded value-date lookback (days) the FX/gold/repo request spans (DR-2): EVDS
    /// filters by <em>observation</em> date, so a run-date-only window would never return the
    /// per-release CBRT repo (dated its decision date). 90 days safely covers the longest
    /// cadence (monthly) plus its publication lag while keeping the fetch bounded.
    /// </summary>
    private const int LookbackDays = 90;

    /// <summary>
    /// The wider CPI-index request window (days, ~13 months). The canonical TÜİK CPI measure
    /// is the annual <b>% change</b>, which the EVDS 3.0 platform does not publish directly
    /// (verified: the <c>bie_tukfiy2025</c> datagroup carries only index levels, 01 v1.5.6
    /// §10.9.12), so the adapter fetches the index over a year-plus window and pairs each
    /// month with the year-prior month to compute YoY deterministically. Only months inside
    /// the standard 90-day output window are emitted.
    /// </summary>
    private const int CpiIndexLookbackDays = 400;

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
        return _options.UsesV3Discovery
            ? await FetchV3Async(date, cancellationToken).ConfigureAwait(false)
            : await FetchLegacyAsync(date, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The legacy single-GET fetch (the L2 golden lane, §10.9.7): one resource at the
    /// configured <see cref="MacroSourceOptions.Path"/>, parsed by shape — the real column
    /// envelope (<c>items</c>/<c>Tarih</c>) or the canonical <c>sourceRef</c>/<c>series</c>
    /// double.
    /// </summary>
    private async Task<SourcePayload<MacroPayload>> FetchLegacyAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var raw = await FetchStringAsync(BuildLegacyRequestPath(date), cancellationToken).ConfigureAwait(false);

        using var document = ParseDocument(raw);
        if (EvdsMacroDecoder.IsEnvelope(document))
        {
            // The real envelope carries no provenance field; the adapter stamps the
            // concrete EVDS source (03 §1) and keeps only the bounded value-date window.
            var realItems = EvdsMacroDecoder.DecodeRows(document, date.AddDays(-LookbackDays), date);
            return new SourcePayload<MacroPayload>(EvdsSource, new MacroPayload(EvdsSource, realItems), raw);
        }

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
    /// The EVDS 3.0 transport (01 v1.5.6 §10.9.12): discover the series from the anonymous
    /// catalog + serieList, then issue the anonymous <c>POST /fe</c> values request. The
    /// FX/gold/repo series ride the bounded 90-day request window (DR-2); the CPI index rides
    /// a year-plus window and is converted to the canonical YoY measure.
    /// </summary>
    private async Task<SourcePayload<MacroPayload>> FetchV3Async(DateOnly date, CancellationToken cancellationToken)
    {
        var resolved = await DiscoverSeriesAsync(cancellationToken).ConfigureAwait(false);

        var items = new List<MacroItem>();
        var raw = string.Empty;

        var dailySeries = resolved.Where(r => !string.Equals(r.Definition.CanonicalCode, MacroSeries.TuikCpi, StringComparison.Ordinal)).ToList();
        if (dailySeries.Count > 0)
        {
            var body = BuildV3Body(dailySeries, date.AddDays(-LookbackDays), date);
            raw = await PostValuesAsync(body, cancellationToken).ConfigureAwait(false);
            using var document = ParseDocument(raw);
            items.AddRange(EvdsMacroDecoder.DecodeRows(
                document,
                date.AddDays(-LookbackDays),
                date,
                dailySeries.Select(r => r.Definition.CanonicalCode).ToHashSet(StringComparer.Ordinal)));
        }

        var cpi = resolved.FirstOrDefault(r => string.Equals(r.Definition.CanonicalCode, MacroSeries.TuikCpi, StringComparison.Ordinal));
        if (cpi is not null)
        {
            var body = BuildV3Body([cpi], date.AddDays(-CpiIndexLookbackDays), date);
            var cpiRaw = await PostValuesAsync(body, cancellationToken).ConfigureAwait(false);
            raw = cpiRaw;
            using var document = ParseDocument(cpiRaw);
            var levels = EvdsMacroDecoder.DecodeRows(
                document,
                date.AddDays(-CpiIndexLookbackDays),
                date,
                new HashSet<string>(StringComparer.Ordinal) { MacroSeries.TuikCpi });
            items.AddRange(EvdsCpiYoY.Compute(levels, date.AddDays(-LookbackDays), date));
        }

        return new SourcePayload<MacroPayload>(EvdsSource, new MacroPayload(EvdsSource, items), raw);
    }

    /// <summary>
    /// Resolves the official series from the anonymous catalog + serieList (01 v1.5.6
    /// §10.9.12). The catalog fetch confirms each target datagroup still exists; the serieList
    /// fetch resolves the concrete series code and its default aggregation method. A datagroup
    /// or series absent from a live discovery response is <b>skipped</b> (never guessed): the
    /// job then marks that series LATE and the last-known value keeps being served.
    /// </summary>
    private async Task<IReadOnlyList<ResolvedSeries>> DiscoverSeriesAsync(CancellationToken cancellationToken)
    {
        var catalogRaw = await FetchStringAsync(_options.CatalogPath, cancellationToken).ConfigureAwait(false);
        var datagroups = ParseDatagroups(catalogRaw);

        var resolved = new List<ResolvedSeries>();
        foreach (var definition in EvdsSeries.Requested)
        {
            if (!datagroups.Contains(definition.Datagroup))
            {
                continue;
            }

            var serieListRaw = await FetchStringAsync(_options.SerieListRoute(definition.Datagroup), cancellationToken).ConfigureAwait(false);
            var entry = ParseSerieList(serieListRaw).FirstOrDefault(s =>
                string.Equals(s.DatagroupCode, definition.Datagroup, StringComparison.Ordinal)
                && string.Equals(s.SerieCode, definition.SerieCode, StringComparison.Ordinal));

            if (entry is null)
            {
                continue;
            }

            var agg = string.IsNullOrWhiteSpace(entry.DefaultAggMethod) ? "avg" : entry.DefaultAggMethod!;
            resolved.Add(new ResolvedSeries(definition, agg));
        }

        return resolved;
    }

    /// <summary>Parses the catalog and collects the datagroup codes it publishes. A malformed
    /// catalog is an invalid (payload) failure so the job quarantines it.</summary>
    private static HashSet<string> ParseDatagroups(string raw)
    {
        using var document = ParseDocument(raw);
        var codes = new HashSet<string>(StringComparer.Ordinal);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return codes;
        }

        foreach (var category in document.RootElement.EnumerateArray())
        {
            if (!category.TryGetProperty("DATAGROUPS", out var groups) || groups.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var group in groups.EnumerateArray())
            {
                if (group.TryGetProperty("DATAGROUP_CODE", out var code) && code.ValueKind == JsonValueKind.String)
                {
                    codes.Add(code.GetString()!);
                }
            }
        }

        return codes;
    }

    /// <summary>Parses one datagroup's serieList into the fields the adapter reads.</summary>
    private static IReadOnlyList<EvdsSerieInfo> ParseSerieList(string raw)
    {
        try
        {
            return JsonSerializer.Deserialize<List<EvdsSerieInfo>>(raw, JsonOptions) ?? [];
        }
        catch (JsonException exception)
        {
            throw new SourcePayloadException(raw, exception);
        }
    }

    /// <summary>Builds one EVDS 3.0 <c>/fe</c> request body from the resolved series.</summary>
    private static EvdsV3Request BuildV3Body(IReadOnlyList<ResolvedSeries> series, DateOnly start, DateOnly end)
    {
        var codes = series.Select(s => s.Definition.SerieCode).ToList();
        return new EvdsV3Request(
            Type: "json",
            Series: string.Join('-', codes),
            AggregationTypes: string.Join('-', series.Select(s => s.Agg)),
            Formulas: string.Join('-', codes.Select(_ => "0")),
            StartDate: start.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
            EndDate: end.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
            Frequency: "2",
            DecimalSeperator: ".",
            Decimal: "2",
            DateFormat: "0",
            Lang: "tr",
            Yon: "0",
            Sira: "0",
            OzelFormuller: [],
            GroupSeperator: true,
            IsRaporSayfasi: false);
    }

    /// <summary>
    /// Posts one values request through the POST seam. The endpoint is anonymous (no key),
    /// so no credential travels in the body or the URL; a failure is redacted anyway (the
    /// optional configured key is never echoed).
    /// </summary>
    private async Task<string> PostValuesAsync(EvdsV3Request body, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        try
        {
            return await _client.PostJsonAsync(_options.Path, json, cancellationToken).ConfigureAwait(false);
        }
        catch (SourceFetchException exception)
        {
            throw RedactClientFailure(exception);
        }
    }

    /// <summary>
    /// Parses the raw body to a <see cref="JsonDocument"/>, surfacing a malformed body as
    /// an invalid (payload) failure so the job quarantines it (<c>UNPARSEABLE_PAYLOAD</c>).
    /// </summary>
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

    /// <summary>
    /// Builds the legacy macro GET request: the official series, a bounded value-date window
    /// ending at the requested run date, the reviewer-facing JSON type, and the configured
    /// API key. The window is a lookback (not a single day) because EVDS filters by observation
    /// date and the periodic series are not dated the run date (DR-2). Only used by the legacy
    /// canonical-double lane (production uses the v3 POST).
    /// </summary>
    private string BuildLegacyRequestPath(DateOnly date)
    {
        var series = string.Join('-', EvdsSeries.RequestOrder.Append(EvdsSeries.CpiIndex));
        var end = date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        var start = date.AddDays(-LookbackDays).ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        var query = string.Create(
            CultureInfo.InvariantCulture,
            $"?series={Uri.EscapeDataString(series)}&startDate={start}&endDate={end}&type=json&key={Uri.EscapeDataString(_options.EvdsApiKey)}");
        return _options.Path + query;
    }

    /// <summary>
    /// Fetches through the source client, stripping the configured EVDS API key from any
    /// failure surface (SEC-1, CWE-598). The legacy GET carries the key in the query string
    /// (the source-client seam has no header channel), so a non-success response would
    /// otherwise produce a <see cref="SourceFetchException"/> whose message echoes it.
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
    /// Maps one EVDS observation to a canonical macro item, resolving the canonical code
    /// <em>only</em> from the EVDS identifier. Only the five official series resolve (FU v1.4
    /// §7/§10, TC-MOV-027): the independent CPI measure is entered through the admin endpoint
    /// and never arrives from the macro adapter, so an observation that names no official
    /// series is dropped — it is not a V1 series the adapter may store.
    /// </summary>
    private static MacroItem? ToMacroItem(EvdsObservation observation)
    {
        if (string.IsNullOrWhiteSpace(observation.SeriesCode)
            || !EvdsSeries.CanonicalByEvdsCode.TryGetValue(observation.SeriesCode, out var canonical))
        {
            return null;
        }

        return new MacroItem(canonical, observation.ValueDate, observation.Value, observation.Unit, observation.RecordedAt);
    }

    /// <summary>One discovered series: its definition and the aggregation method the
    /// datagroup's serieList publishes.</summary>
    private sealed record ResolvedSeries(EvdsSeries.Definition Definition, string Agg);
}
