using System.Net.Http.Headers;
using System.Text;
using Degerli.Ingestion.Alerting;
using Degerli.Ingestion.CorporateActions;
using Degerli.Ingestion.Coverage;
using Degerli.Ingestion.Disclosures;
using Degerli.Ingestion.Dividends;
using Degerli.Ingestion.Funds;
using Degerli.Ingestion.Intraday;
using Degerli.Ingestion.Jobs;
using Degerli.Ingestion.Kap;
using Degerli.Ingestion.KapPublic;
using Degerli.Ingestion.Macro;
using Degerli.Ingestion.Metrics;
using Degerli.Ingestion.Prices;
using Degerli.Ingestion.Quarantine;
using Degerli.Ingestion.Scheduling;
using Degerli.Ingestion.Shares;
using Degerli.Ingestion.Sources;
using Degerli.Ingestion.Statements;
using Degerli.Ingestion.Universe;
using Degerli.Ingestion.Validation;
using Degerli.Ingestion.Valuation;
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
        // Charset at the source client (01 v1.5 §10.9.4, AD-14): the .NET code-pages
        // provider is registered once at product startup so the legacy Turkish code pages
        // (windows-1254 / ISO-8859-9) that real İş Yatırım/KAP artifacts arrive in decode
        // faithfully through ISourceClient. Registration is process-global and idempotent.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

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
            var http = new HttpClient
            {
                // The VYK base URL carries a path prefix (`/api/vyk`) and ships without a
                // trailing slash; the join must preserve it (01 v1.5 §10.9.11).
                BaseAddress = KapUri.WithDirectorySeparator(new Uri(baseUrl, UriKind.Absolute)),
                Timeout = TimeSpan.FromSeconds(30),
            };

            // The VYK gateway authenticates with an HTTP Basic credential (01 v1.5
            // §10.9.11): ApiKey as the username, ApiSecret as the password, attached as a
            // request header — never in the URL — and only when a credential is actually
            // configured (empty in local/test wiring, where the WireMock double ignores it).
            // The secret list feeds the client's redaction guard so no configured credential
            // can leak into a thrown message.
            var secrets = options.Secrets.ToList();
            if (options.HasCredential)
            {
                var (username, password) = options.BasicCredential();
                var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}"));
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
                // The base64 token placed on the wire is itself redaction surface (SEC-2).
                secrets.Add(basic);
            }

            return new KapHttpSourceClient(http, secrets);
        });

        // The KAP **public-site** source (01 v1.5.53 §10.9.19, TKT-foundation-079) is a
        // separate source from the VYK gateway above — its own base address, its own
        // single-flight client, its own per-run pacer and its own options section. The VYK
        // wiring is untouched: selecting the public site for a lane is configuration
        // (TKT-foundation-082/084), never a rewrite, and switching back is configuration too.
        services.Configure<KapPublicSourceOptions>(configuration.GetSection(KapPublicSourceOptions.SectionName));
        services.AddKeyedSingleton<HttpClient>(KapPublicSourceOptions.ClientKey, (provider, _) =>
        {
            var options = provider.GetRequiredService<IOptions<KapPublicSourceOptions>>().Value;
            var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl) ? "http://localhost" : options.BaseUrl;
            return new HttpClient
            {
                BaseAddress = KapUri.WithDirectorySeparator(new Uri(baseUrl, UriKind.Absolute)),
                // Every request carries its own bound (the per-item detail timeout is the
                // shorter one): a stalled body can never outlive the bound its request set.
                Timeout = Timeout.InfiniteTimeSpan,
            };
        });
        services.AddScoped<KapPublicRequestPacer>();
        services.AddScoped<IKapPublicSourceClient>(provider => new KapPublicHttpSourceClient(
            provider.GetRequiredKeyedService<HttpClient>(KapPublicSourceOptions.ClientKey),
            provider.GetRequiredService<IOptions<KapPublicSourceOptions>>(),
            provider.GetRequiredService<KapPublicRequestPacer>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddScoped<KapPublicSourceReader>();

        services.Configure<StatementsSourceOptions>(configuration.GetSection(StatementsSourceOptions.SectionName));
        services.Configure<DividendsSourceOptions>(configuration.GetSection(DividendsSourceOptions.SectionName));
        services.Configure<CorporateActionsSourceOptions>(configuration.GetSection(CorporateActionsSourceOptions.SectionName));
        services.Configure<DisclosuresSourceOptions>(configuration.GetSection(DisclosuresSourceOptions.SectionName));

        services.AddScoped<IQuarantineWriter, QuarantineWriter>();
        services.AddScoped<IQuarantineService, QuarantineService>();
        services.AddSingleton<IPayloadSchemaValidator, JsonPayloadSchemaValidator>();
        // The price adapter is both the incremental and the backfill source (TKT-mdf-007);
        // one instance is registered and exposed through both seams.
        services.AddScoped<PricesSourceAdapter>();
        services.AddScoped<ISourceAdapter<PricesPayload>>(provider => provider.GetRequiredService<PricesSourceAdapter>());
        services.AddScoped<IBackfillSourceAdapter<PricesHistoryPayload>>(provider => provider.GetRequiredService<PricesSourceAdapter>());
        services.AddScoped<DailyPriceStore>();
        services.AddScoped<CoverageRecorder>();
        services.AddScoped<FundCoverageRecorder>();

        // The intraday refresh lane (TKT-foundation-047, 01 §10.9.18): its own source section
        // mirroring `Ingestion:Prices` (the probe-gated İş/Yahoo choice is config, never a
        // code fork), its own keyed client (a repoint at the Yahoo contingency must not move
        // the EOD lane), the intraday fact store and the served price-state resolver.
        services.Configure<IntradayQuoteSourceOptions>(configuration.GetSection(IntradayQuoteSourceOptions.SectionName));
        services.AddKeyedSingleton<ISourceClient>(IntradayQuoteSourceOptions.ClientKey, (provider, _) =>
        {
            var options = provider.GetRequiredService<IOptions<IntradayQuoteSourceOptions>>().Value;
            var baseUrl = options.BaseUrl;
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                baseUrl = provider.GetRequiredService<IOptions<PricesSourceOptions>>().Value.BaseUrl;
            }

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                baseUrl = "http://localhost";
            }

            return new HttpSourceClient(new HttpClient
            {
                BaseAddress = new Uri(baseUrl, UriKind.Absolute),
                Timeout = TimeSpan.FromSeconds(30),
            });
        });
        services.AddScoped(provider => new IntradayQuoteSourceAdapter(
            provider.GetRequiredKeyedService<ISourceClient>(IntradayQuoteSourceOptions.ClientKey),
            provider.GetRequiredService<IOptions<IntradayQuoteSourceOptions>>()));
        services.AddScoped<IntradayQuoteStore>();
        services.AddScoped<PriceStateService>();

        // Universe, sector and index-levels sync (FR-MDF-006/007). The universe source has
        // its own keyed source client bound to its base address (01 v1.5 §10.9.8), so the
        // universe adapters never share the prices client. The real universe resource is a
        // legacy ASP.NET AJAX POST handler, reachable through the client's POST seam; the
        // index-levels resource is a plain GET on the same host. The base-address fallback
        // (Universe:BaseUrl → Prices:BaseUrl → http://localhost) keeps the canonical L2
        // harness composing over its own stubbed paths unchanged.
        services.AddKeyedSingleton<ISourceClient>(UniverseSourceOptions.ClientKey, (provider, _) =>
        {
            var universe = provider.GetRequiredService<IOptions<UniverseSourceOptions>>().Value;
            var baseUrl = universe.BaseUrl;
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                baseUrl = provider.GetRequiredService<IOptions<PricesSourceOptions>>().Value.BaseUrl;
            }

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                baseUrl = "http://localhost";
            }

            return new HttpSourceClient(new HttpClient
            {
                BaseAddress = new Uri(baseUrl, UriKind.Absolute),
                Timeout = TimeSpan.FromSeconds(30),
            });
        });
        services.AddScoped<ISourceAdapter<UniversePayload>>(provider =>
            new UniverseSourceAdapter(
                provider.GetRequiredKeyedService<ISourceClient>(UniverseSourceOptions.ClientKey),
                provider.GetRequiredService<IOptions<UniverseSourceOptions>>()));
        services.AddScoped<ISourceAdapter<IndexLevelsPayload>>(provider =>
            new IndexLevelsSourceAdapter(
                provider.GetRequiredKeyedService<ISourceClient>(UniverseSourceOptions.ClientKey),
                provider.GetRequiredService<IOptions<UniverseSourceOptions>>()));
        services.AddScoped<UniverseSyncStore>();

        // The VYK company-scoped source (01 v1.5 §10.9.11): one per-run directory snapshot,
        // a run-wide request pacer and the per-company disclosure cursor store. Each is
        // scoped to one run so the read-only statements pacing never leaks onto the other
        // KAP jobs sharing the client.
        services.AddScoped<KapRequestPacer>();
        services.AddScoped<VykCompanyDirectory>();
        services.AddScoped<KapDisclosureCursorStore>();
        services.AddScoped<StatementsSourceAdapter>();
        services.AddScoped<ISourceAdapter<StatementsPayload>>(provider => provider.GetRequiredService<StatementsSourceAdapter>());
        services.AddScoped<FinancialStatementStore>();

        services.AddScoped<ISourceAdapter<DividendsPayload>, DividendsSourceAdapter>();
        services.AddScoped<DividendStore>();

        // The shares lane (01 v1.5.36 §10.9.11): one paced /memberSecurities snapshot per run
        // through the shared KAP client, decoded into the per-instrument share-count store.
        services.AddScoped<ISourceAdapter<SharesPayload>, SharesSourceAdapter>();
        services.AddScoped<ShareCountStore>();

        services.AddScoped<ISourceAdapter<CorporateActionsPayload>, CorporateActionsSourceAdapter>();
        services.AddScoped<CorporateActionStore>();

        services.AddScoped<ISourceAdapter<DisclosuresPayload>, DisclosuresSourceAdapter>();
        services.AddScoped<DisclosureStore>();

        // C3b Metrics Engine (FR-MDF-013/014): canonical metric computation + storage.
        services.AddScoped<MetricsEngine>();
        services.AddScoped<DerivedMetricStore>();

        // C3b sector medians (FR-RES-007/SD-002): the vs.-sector strip's data source.
        services.AddScoped<SectorMedianEngine>();
        services.AddScoped<SectorMedianStore>();

        // C3b DCF baseline generation (02 §5.8, UC-VAL-003): builder default rules +
        // the job that derives dcf_baselines rows from canonical facts.
        services.Configure<DcfBaselineOptions>(configuration.GetSection(DcfBaselineOptions.SectionName));
        services.AddScoped<DcfBaselineGenerationJob>();

        // C3b market snapshot (FR-MOV-001..006/021): nightly market aggregates + storage.
        services.Configure<MarketSnapshotOptions>(configuration.GetSection(MarketSnapshotOptions.SectionName));
        services.AddScoped<MarketSnapshotEngine>();
        services.AddScoped<MarketSnapshotStore>();

        // TEFAS fund ingestion (FR-FDF-001..003): its own source client, bound to the
        // funds base address, so the fund adapter never shares the prices client.
        services.Configure<TefasSourceOptions>(configuration.GetSection(TefasSourceOptions.SectionName));
        services.AddKeyedSingleton<ISourceClient>(TefasSourceOptions.ClientKey, (provider, _) =>
        {
            var options = provider.GetRequiredService<IOptions<TefasSourceOptions>>().Value;
            var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl) ? "http://localhost" : options.BaseUrl;
            return new HttpSourceClient(new HttpClient
            {
                BaseAddress = new Uri(baseUrl, UriKind.Absolute),
                Timeout = TimeSpan.FromSeconds(30),
            });
        });
        services.AddScoped<ISourceAdapter<TefasUniversePayload>>(provider =>
            new TefasSourceAdapter(
                provider.GetRequiredKeyedService<ISourceClient>(TefasSourceOptions.ClientKey),
                provider.GetRequiredService<IOptions<TefasSourceOptions>>()));
        services.AddScoped<TefasFundStore>();

        // Macro ingestion (FR-MOV-014..016): its own source client, bound to the macro
        // base address, so the macro adapter never shares the prices/TEFAS client.
        services.Configure<MacroSourceOptions>(configuration.GetSection(MacroSourceOptions.SectionName));
        services.AddKeyedSingleton<ISourceClient>(MacroSourceOptions.ClientKey, (provider, _) =>
        {
            var options = provider.GetRequiredService<IOptions<MacroSourceOptions>>().Value;
            var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl) ? "http://localhost" : options.BaseUrl;
            return new HttpSourceClient(new HttpClient
            {
                BaseAddress = new Uri(baseUrl, UriKind.Absolute),
                Timeout = TimeSpan.FromSeconds(30),
            });
        });
        services.AddScoped<ISourceAdapter<MacroPayload>>(provider =>
            new MacroSourceAdapter(
                provider.GetRequiredKeyedService<ISourceClient>(MacroSourceOptions.ClientKey),
                provider.GetRequiredService<IOptions<MacroSourceOptions>>()));
        services.AddScoped<MacroStore>();
        services.AddScoped<MacroFreshnessService>();

        // Per-job registrations (the convention): add one line per job as it lands.
        services.AddScoped<IIngestJob, PricesJob>();
        services.AddScoped<IIngestJob, IntradayRefreshJob>();
        services.AddScoped<IIngestJob, UniverseSyncJob>();
        services.AddScoped<IIngestJob, StatementsJob>();
        services.AddScoped<IIngestJob, DividendsJob>();
        services.AddScoped<IIngestJob, SharesJob>();
        services.AddScoped<IIngestJob, CorporateActionsJob>();
        services.AddScoped<IIngestJob, DisclosuresJob>();
        services.AddScoped<IIngestJob, MetricsRecomputeJob>();
        // C3b sector medians (FR-RES-007/SD-002): the vs.-sector strip's data source.
        services.AddScoped<IIngestJob, SectorMediansJob>();
        services.AddScoped<IIngestJob, DcfBaselineGenerationJob>();
        services.AddScoped<IIngestJob, MarketSnapshotJob>();
        services.AddScoped<IIngestJob, FundNavJob>();
        services.AddScoped<IIngestJob, FundPerformanceJob>();
        services.AddScoped<IIngestJob, FundHoldingsJob>();
        services.AddScoped<IIngestJob, MacroDailyJob>();
        services.AddScoped<IIngestJob, MacroCpiJob>();

        services.AddScoped<IIngestionJobRunner, IngestionJobRunner>();

        return services;
    }
}
