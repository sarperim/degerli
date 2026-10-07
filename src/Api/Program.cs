using Degerli.Api.Infrastructure;
using Degerli.Ingestion;
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

// Data platform workers run in-process in this container (AD-04).
builder.Services.AddHostedService<IngestionHostedService>();

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseApiInfrastructure();

// Liveness + database check used by the deploy smoke-check and UptimeRobot (§10.3).
app.MapHealthChecks("/health");

// OpenAPI document is anonymous and lives under the versioned prefix (`03` header).
app.MapOpenApi("/api/v1/openapi.json");

var api = app.MapGroup("/api/v1");

// Auth plumbing: the antiforgery token endpoint is anonymous; every unsafe method
// under /api/v1 must present the token in the X-CSRF-Token header.
var auth = api.MapGroup("/auth").RequireRateLimiting(RateLimitingSetup.AuthPolicy);
auth.MapGet("/csrf-token", CsrfToken).AllowAnonymous();

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
