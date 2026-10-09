using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Prices;
using Degerli.Ingestion.Quarantine;
using Degerli.Ingestion.Sources;
using Degerli.Ingestion.Validation;
using Degerli.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Degerli.Ingestion;

/// <summary>
/// Composition root for the data platform's ingestion workers (C3a). The API host calls
/// <see cref="AddMarketDataIngestion"/> once; every job registers itself here so its
/// <see cref="IIngestJob.JobCode"/> is discoverable by the runner, the scheduler
/// (TKT-mdf-005) and the admin trigger (TKT-mdf-010) — the per-job registration
/// convention later ingestion tickets follow.
/// </summary>
public static class IngestionServiceCollectionExtensions
{
    /// <summary>
    /// Registers the ingestion framework, the fact-storage services and the <c>prices</c>
    /// job. Persistence is registered here too so the worker composition is
    /// self-contained. L2 tests compose this same method against a Testcontainers
    /// database and the WireMock source double.
    /// </summary>
    public static IServiceCollection AddMarketDataIngestion(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // In a host the configuration is already registered; in a bare test composition
        // this makes the worker registration self-contained.
        services.TryAddSingleton(configuration);

        // The DbContext is registered lazily: the connection string is read when the
        // context is first resolved, not at composition time. That keeps the worker
        // composition and the WebApplicationFactory test host (which injects its
        // connection string after `Program` runs) on the same code path.
        services.AddDbContext<DegerliDbContext>((provider, options) =>
        {
            var connectionString = provider.GetRequiredService<IConfiguration>().GetConnectionString("Default")
                ?? throw new InvalidOperationException(
                    "ConnectionStrings:Default is not configured (architecture §10.4).");
            DegerliDbContextOptions.Configure(options, connectionString);
        });

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IIngestionAlerter, LoggingIngestionAlerter>();

        services.Configure<PricesSourceOptions>(configuration.GetSection(PricesSourceOptions.SectionName));

        services.AddSingleton<ISourceClient>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<PricesSourceOptions>>().Value;
            var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl) ? "http://localhost" : options.BaseUrl;
            return new HttpSourceClient(new HttpClient
            {
                BaseAddress = new Uri(baseUrl, UriKind.Absolute),
                Timeout = TimeSpan.FromSeconds(30),
            });
        });

        services.AddScoped<IQuarantineWriter, QuarantineWriter>();
        services.AddScoped<IQuarantineService, QuarantineService>();
        services.AddSingleton<IPayloadSchemaValidator, JsonPayloadSchemaValidator>();
        services.AddScoped<ISourceAdapter<PricesPayload>, PricesSourceAdapter>();
        services.AddScoped<DailyPriceStore>();

        // Per-job registrations (the convention): add one line per job as it lands.
        services.AddScoped<IIngestJob, PricesJob>();

        services.AddScoped<IIngestionJobRunner, IngestionJobRunner>();

        return services;
    }
}
