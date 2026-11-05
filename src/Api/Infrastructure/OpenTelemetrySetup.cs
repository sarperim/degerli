using System.Diagnostics;
using System.Diagnostics.Metrics;
using Degerli.Ingestion.Scheduling;
using Degerli.Ingestion.Telemetry;
using Degerli.Persistence;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Degerli.Api.Infrastructure;

/// <summary>
/// OpenTelemetry composition for the API host (SLICE-014).
/// <para>
/// <b>The gating rule:</b> nothing is registered unless <c>OpenTelemetry:Enabled</c> is
/// <c>true</c>. Instrumentation in the ingestion lane is unconditional
/// (<see cref="DegerliIngestionMetrics"/> carries no-ops until something listens), so this
/// method is purely the listener/exporter decision: off by default in production, on in the
/// dev stack by flipping one <c>.env</c> value. Tests never call this method — they attach an
/// in-process <see cref="MeterListener"/> instead, so no test exports over OTLP
/// (zero-live-calls invariant).
/// </para>
/// <para>
/// When enabled it subscribes AspNetCore + HttpClient + System.Runtime instrumentation, the
/// <c>Degerli.Ingestion</c> meter, an EOD-session freshness gauge and the OTLP exporter
/// pointed at <c>OpenTelemetry:OtlpEndpoint</c> (the dev Aspire dashboard's OTLP listener).
/// </para>
/// </summary>
public static class OpenTelemetrySetup
{
    /// <summary>The configuration key that turns the whole pipeline on.</summary>
    public const string EnabledKey = "OpenTelemetry:Enabled";

    /// <summary>The OTLP endpoint the exporter ships to (the dev Aspire dashboard's listener).</summary>
    public const string OtlpEndpointKey = "OpenTelemetry:OtlpEndpoint";

    /// <summary>The gauge's instrument name: the EOD display lag, in seconds.</summary>
    public const string EodSessionLagInstrument = "degerli.market.eod_session_lag_seconds";

    /// <summary>
    /// Registers the OpenTelemetry metrics/traces pipeline when <c>OpenTelemetry:Enabled</c>
    /// is true; registers nothing at all otherwise.
    /// </summary>
    public static IServiceCollection AddDegerliOpenTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (!configuration.GetValue<bool>(EnabledKey))
        {
            // Off (the production default): not a single OTel service is registered, so the
            // host behaves exactly as the pre-slice stack.
            return services;
        }

        var otlpEndpoint = configuration[OtlpEndpointKey];

        services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("Degerli.Api"))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation())
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    // The ingestion lane's meter is always recorded; only its export is gated.
                    .AddMeter(DegerliIngestionMetrics.MeterName);

                metrics.AddOtlpExporter(options =>
                {
                    if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    {
                        options.Endpoint = new Uri(otlpEndpoint, UriKind.Absolute);
                    }
                });
            });

        // The EOD freshness gauge observes on scrape; it is created at host start because an
        // ObservableGauge must be held by something that outlives the registration.
        services.AddHostedService<EodSessionLagGaugeHostedService>();

        return services;
    }
}

/// <summary>
/// Owns the EOD-session display-lag gauge (SLICE-014): an
/// <see cref="ObservableGauge{T}"/> on the <c>Degerli.Ingestion</c> meter named
/// <c>degerli.market.eod_session_lag_seconds</c>.
/// <para>
/// The callback opens a scope and calls the <see cref="EodSessionFreshness"/> seam the
/// stock/screener surfaces already use, publishing the lag in seconds. It must never throw
/// into the exporter — a down database, a missing calendar or a canceled read skips the
/// sample instead of failing the scrape.
/// </para>
/// </summary>
public sealed class EodSessionLagGaugeHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private ObservableGauge<double>? _gauge;

    public EodSessionLagGaugeHostedService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _gauge = DegerliIngestionMetrics.Meter.CreateObservableGauge(
            OpenTelemetrySetup.EodSessionLagInstrument,
            ObserveLag,
            unit: "s",
            description: "Seconds the freshest stored daily price trails the completed session.");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // The meter owns the instrument; dropping our reference is enough (the options
        // binding keeps the meter alive for the host's lifetime).
        _gauge = null;
        return Task.CompletedTask;
    }

    private IEnumerable<Measurement<double>> ObserveLag()
    {
        if (TryMeasureLagSeconds(out var seconds))
        {
            yield return new Measurement<double>(seconds);
        }
    }

    private bool TryMeasureLagSeconds(out double seconds)
    {
        seconds = 0;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DegerliDbContext>();
            var calendar = scope.ServiceProvider.GetRequiredService<IIngestionCalendar>();
            var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();

            var (asOf, stale) = EodSessionFreshness
                .FromDailyPricesAsync(db, calendar, clock, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            if (stale)
            {
                var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
                seconds = TimeSpan.FromDays(Math.Max(0, today.DayNumber - asOf.DayNumber)).TotalSeconds;
            }

            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The exporter must never see a scrape failure.
            Debug.WriteLine($"EOD session lag gauge skipped: {exception.GetType().Name}");
            return false;
        }
    }
}
