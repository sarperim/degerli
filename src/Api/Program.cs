using Degerli.Api.Admin;
using Degerli.Api.Identity;
using Degerli.Api.Infrastructure;
using Degerli.Api.Market;
using Degerli.Api.Screener;
using Degerli.Api.Stocks;
using Degerli.Api.Valuation;
using Degerli.Ingestion;
using Degerli.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Serilog;

// Forward-only migrator entrypoint (architecture §6.2; `02-data-model.md` §8). The
// deploy job runs `docker compose run --rm api migrate` before `up -d`; the trailing
// `migrate` argument reaches this branch. It applies the pending EF migrations against
// the configured connection string and exits without starting the web host, so the API
// never serves an un-migrated schema. Checked before the host builder so the bare
// `migrate` token never reaches the command-line configuration provider.
if (args.Contains("migrate", StringComparer.OrdinalIgnoreCase))
{
    var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        Console.Error.WriteLine("migrate: no connection string (set ConnectionStrings__Default).");
        return 2;
    }

    await using var migrationDb = new DegerliDbContext(DegerliDbContextOptions.Build(connectionString));
    await migrationDb.Database.MigrateAsync();
    Console.WriteLine("migrate: database schema is up to date.");
    return 0;
}

var builder = WebApplication.CreateBuilder(args);

// Structured JSON logging: rolling files + stdout, driven by appsettings.json so the
// same configuration applies locally and in the container (architecture §10.3).
builder.Host.UseSerilog((context, services, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

// Shared cross-cutting API infrastructure (architecture §10): ProblemDetails/errors,
// honest-data envelope types, antiforgery, health checks, rate limits, security
// headers, caching policy and the OpenAPI document.
builder.Services.AddApiInfrastructure(builder.Configuration, builder.Environment);

// Single EF Core model + the Identity/auth module (registration, consent, session).
builder.Services.AddDegerliPersistence(builder.Configuration);
builder.Services.AddDegerliIdentity(builder.Configuration);
builder.Services.AddDegerliAuthSessionRevocation();

// Data platform ingestion (C3a): source adapters, fact storage and the per-job
// registration convention (TKT-mdf-002). Later ingestion tickets add their jobs there.
builder.Services.AddMarketDataIngestion(builder.Configuration);

// Data platform workers run in-process in this container (AD-04).
builder.Services.AddHostedService<IngestionHostedService>();
builder.Services.AddHostedService<Degerli.Ingestion.Scheduling.IngestionSchedulerHostedService>();

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseApiInfrastructure();
app.UseAuthorization();

// Liveness + database check used by the deploy smoke-check and UptimeRobot (§10.3).
app.MapHealthChecks("/health");

// OpenAPI document is anonymous and lives under the versioned prefix (`03` header).
app.MapOpenApi("/api/v1/openapi.json");

var api = app.MapGroup("/api/v1");

// Auth plumbing: the antiforgery token endpoint is anonymous; every unsafe method
// under /api/v1 must present the token in the X-CSRF-Token header.
var auth = api.MapGroup("/auth").RequireRateLimiting(RateLimitingSetup.AuthPolicy);
auth.MapGet("/csrf-token", CsrfToken).AllowAnonymous();

// Identity module endpoints (TKT-acc-002): registration + session; the /me read
// asserts the minimal-data posture. Later ACC tickets extend these groups.
api.MapDegerliIdentityEndpoints();
api.MapDegerliAuthSessionEndpoints();
api.MapDegerliMeEndpoints();

// Valuation module (TKT-val-004): the pure, anonymous, stateless DCF compute endpoint.
api.MapDegerliDcfEndpoints();

// Valuation module (TKT-val-003): the anonymous DCF baseline read (`GET …/dcf`).
api.MapDegerliDcfBaselineEndpoints();

// Valuation module (TKT-val-005): per-account DCF scenario CRUD (`/me/scenarios`).
api.MapDegerliDcfScenarioEndpoints();

// Stocks module (TKT-res-002): the current-universe list/filter/search and the
// lightweight header typeahead (FR-RES-001..004).
api.MapDegerliStockEndpoints();

// Stocks module (TKT-res-003): the Overview (published description / preparing) and
// Valuation (current multiples + adjusted history + vs.-sector strip) sections.
api.MapDegerliStockOverviewAndValuationEndpoints();

// Stocks module (TKT-res-004): the Financials, Profitability, Growth, Balance-sheet
// and Dividends sections (FR-RES-008..012/014..016/027).
api.MapDegerliStockFundamentalsEndpoints();

// Screener module (TKT-scr-002): the visible metric catalog and the core ad-hoc run
// (FR-SCR-001..006/014/015; UC-SCR-001). Saved screens/admin visibility follow later.
api.MapDegerliScreenerEndpoints();

// Screener module (TKT-scr-004): per-account saved-screen CRUD (FR-SCR-007..011;
// UC-SCR-002/003). Mutations require a verified e-mail; reads require auth only.
api.MapDegerliSavedScreenEndpoints();

// Market module (TKT-mov-004): the public dashboard reads (FR-MOV-001..010/021;
// UC-MOV-001/003) — equity blocks and the macro strip as separate endpoints.
api.MapDegerliMarketEndpoints();

var admin = api.MapDegerliAdminEndpoints();

// Non-production probes let the infrastructure tests exercise the mechanisms without
// waiting for domain endpoints. They are never mapped in production.
if (!app.Environment.IsProduction())
{
    api.MapGet("/_probe/rate", () => Results.Ok(new { ok = true })).AllowAnonymous();
    api.MapGet("/_probe/throw", ThrowProbe);
    api.MapGet("/stocks/_probe", () => Results.Ok(new { ok = true })).AllowAnonymous();
    api.MapGet("/market/_probe", () => Results.Ok(new { ok = true })).AllowAnonymous();
    api.MapGet("/screener/metrics/_probe", () => Results.Ok(new { ok = true })).AllowAnonymous();
    api.MapGet("/me/_probe", () => Results.Ok(new { ok = true })).AllowAnonymous();
    auth.MapPost("/_probe", () => Results.NoContent()).AllowAnonymous();
    admin.MapGet("/_probe", () => Results.Ok(new { ok = true })).AllowAnonymous();
}

app.Run();
return 0;

static IResult CsrfToken(HttpContext context, IAntiforgery antiforgery)
{
    var tokens = antiforgery.GetAndStoreTokens(context);
    return Results.Ok(new { token = tokens.RequestToken, headerName = "X-CSRF-Token" });
}

static IResult ThrowProbe()
    => throw new InvalidOperationException("probe: unhandled exception for infrastructure verification.");

// Exposed so integration tests can host the app with WebApplicationFactory.
public partial class Program { }
