using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.CorporateActions;
using Degerli.Ingestion.Disclosures;
using Degerli.Ingestion.Dividends;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Kap;
using Degerli.Ingestion.Prices;
using Degerli.Ingestion.Quarantine;
using Degerli.Ingestion.Scheduling;
using Degerli.Ingestion.Sources;
using Degerli.Ingestion.Statements;
using Degerli.Ingestion.Universe;
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
        services.Configure<UniverseSourceOptions>(configuration.GetSection(UniverseSourceOptions.SectionName));

        // C3c scheduler: Cronos cron + trading calendar + the run-ledger retry ladder.
        // The hosted loop is added by the API host; the engine is resolvable on its own
        // so tests and admin triggers share one scheduling rule.
        services.Configure<IngestionSchedulerOptions>(configuration.GetSection(IngestionSchedulerOptions.SectionName));
        services.Configure<TradingCalendarOptions>(configuration.GetSection(TradingCalendarOptions.SectionName));
        services.TryAddSingleton<IIngestionCalendar, ConfiguredTradingCalendar>();
        services.TryAddSingleton<IngestionScheduler>();

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

        // KAP has its own base address (statements/dividends/actions/disclosures), separate
        // from the prices source, so its adapters read through a dedicated client.
        services.Configure<KapSourceOptions>(configuration.GetSection(KapSourceOptions.SectionName));
        services.AddSingleton<IKapSourceClient>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<KapSourceOptions>>().Value;
            var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl) ? "http://localhost" : options.BaseUrl;
            return new KapHttpSourceClient(new HttpClient
            {
                BaseAddress = new Uri(baseUrl, UriKind.Absolute),
                Timeout = TimeSpan.FromSeconds(30),
            });
        });

        services.Configure<StatementsSourceOptions>(configuration.GetSection(StatementsSourceOptions.SectionName));
        services.Configure<DividendsSourceOptions>(configuration.GetSection(DividendsSourceOptions.SectionName));
        services.Configure<CorporateActionsSourceOptions>(configuration.GetSection(CorporateActionsSourceOptions.SectionName));
        services.Configure<DisclosuresSourceOptions>(configuration.GetSection(DisclosuresSourceOptions.SectionName));

        services.AddScoped<IQuarantineWriter, QuarantineWriter>();
        services.AddScoped<IQuarantineService, QuarantineService>();
        services.AddSingleton<IPayloadSchemaValidator, JsonPayloadSchemaValidator>();
        services.AddScoped<ISourceAdapter<PricesPayload>, PricesSourceAdapter>();
        services.AddScoped<DailyPriceStore>();

        // Universe, sector and index-levels sync (FR-MDF-006/007).
        services.AddScoped<ISourceAdapter<UniversePayload>, UniverseSourceAdapter>();
        services.AddScoped<ISourceAdapter<IndexLevelsPayload>, IndexLevelsSourceAdapter>();
        services.AddScoped<UniverseSyncStore>();

        services.AddScoped<ISourceAdapter<StatementsPayload>, StatementsSourceAdapter>();
        services.AddScoped<FinancialStatementStore>();

        services.AddScoped<ISourceAdapter<DividendsPayload>, DividendsSourceAdapter>();
        services.AddScoped<DividendStore>();

        services.AddScoped<ISourceAdapter<CorporateActionsPayload>, CorporateActionsSourceAdapter>();
        services.AddScoped<CorporateActionStore>();

        services.AddScoped<ISourceAdapter<DisclosuresPayload>, DisclosuresSourceAdapter>();
        services.AddScoped<DisclosureStore>();

        // Per-job registrations (the convention): add one line per job as it lands.
        services.AddScoped<IIngestJob, PricesJob>();
        services.AddScoped<IIngestJob, UniverseSyncJob>();
        services.AddScoped<IIngestJob, StatementsJob>();
        services.AddScoped<IIngestJob, DividendsJob>();
        services.AddScoped<IIngestJob, CorporateActionsJob>();
        services.AddScoped<IIngestJob, DisclosuresJob>();

        services.AddScoped<IIngestionJobRunner, IngestionJobRunner>();

        return services;
    }
}
