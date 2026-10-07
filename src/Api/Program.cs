using Degerli.Ingestion;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Structured JSON logging: rolling files + stdout, driven by appsettings.json so the
// same configuration applies locally and in the container (architecture §10.3).
builder.Host.UseSerilog((context, services, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.AddHealthChecks();

// Data platform workers run in-process in this container (AD-04).
builder.Services.AddHostedService<IngestionHostedService>();

var app = builder.Build();

app.UseSerilogRequestLogging();

// Liveness endpoint used by the deploy smoke-check and UptimeRobot (architecture §10.3).
app.MapHealthChecks("/health");

app.Run();

// Exposed so integration tests can host the app with WebApplicationFactory.
public partial class Program { }
