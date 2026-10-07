using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// Boots the API in-process for L2 integration tests. The database health check is
/// replaced with a deterministic stub so mechanism tests (CSRF, rate limits, headers,
/// caching, OpenAPI) do not require a live PostgreSQL; the real check is exercised by
/// the compose/e2e stack (TC-XC-016).
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly IDictionary<string, string?> _overrides;
    private readonly HealthStatus _dbStatus;

    public ApiFactory()
        : this(new Dictionary<string, string?>(), HealthStatus.Healthy)
    {
    }

    internal ApiFactory(IDictionary<string, string?> overrides, HealthStatus dbStatus)
    {
        _overrides = overrides;
        _dbStatus = dbStatus;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Host=127.0.0.1;Port=5432;Database=degerli;Username=degerli;Password=degerli;Timeout=1",
            };
            foreach (var pair in _overrides)
            {
                settings[pair.Key] = pair.Value;
            }

            configuration.AddInMemoryCollection(settings);
        });

        builder.ConfigureTestServices(services =>
        {
            services.Configure<HealthCheckServiceOptions>(options =>
            {
                options.Registrations.Clear();
                options.Registrations.Add(new HealthCheckRegistration(
                    "database",
                    _ => new StubHealthCheck(_dbStatus),
                    failureStatus: null,
                    tags: new[] { "ready" }));
            });
        });
    }

    private sealed class StubHealthCheck : IHealthCheck
    {
        private readonly HealthStatus _status;

        public StubHealthCheck(HealthStatus status) => _status = status;

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(_status == HealthStatus.Healthy
                ? HealthCheckResult.Healthy("stubbed database is reachable")
                : HealthCheckResult.Unhealthy("stubbed database is down"));
    }
}
