using Degerli.Api.Identity;
using Degerli.Api.Infrastructure;
using Degerli.Api.Stocks;
using Degerli.Api.Valuation;
using Degerli.Ingestion;
using Degerli.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;

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

// Valuation module (TKT-val-005): per-account DCF scenario CRUD (`/me/scenarios`).
api.MapDegerliDcfScenarioEndpoints();

// Stocks module (TKT-res-002): the current-universe list/filter/search and the
// lightweight header typeahead (FR-RES-001..004).
api.MapDegerliStockEndpoints();

var admin = api.MapGroup("/admin").RequireRateLimiting(RateLimitingSetup.AdminPolicy);

// Non-production probes let the infrastructure tests exercise the mechanisms without
// waiting for domain endpoints. They are never mapped in production.
if (!app.Environment.IsProduction())
{
    api.MapGet("/_probe/rate", () => Results.Ok(new { ok = true })).AllowAnonymous();
    api.MapGet("/_probe/throw", ThrowProbe);
    api.MapGet("/stocks/_probe", () => Results.Ok(new { ok = true })).AllowAnonymous();
    api.MapGet("/me/_probe", () => Results.Ok(new { ok = true })).AllowAnonymous();
    auth.MapPost("/_probe", () => Results.NoContent()).AllowAnonymous();
    admin.MapGet("/_probe", () => Results.Ok(new { ok = true })).AllowAnonymous();
}

app.Run();

static IResult CsrfToken(HttpContext context, IAntiforgery antiforgery)
{
    var tokens = antiforgery.GetAndStoreTokens(context);
    return Results.Ok(new { token = tokens.RequestToken, headerName = "X-CSRF-Token" });
}

static IResult ThrowProbe()
    => throw new InvalidOperationException("probe: unhandled exception for infrastructure verification.");

// Exposed so integration tests can host the app with WebApplicationFactory.
public partial class Program { }
