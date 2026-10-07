using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Degerli.Api.Infrastructure;

/// <summary>
/// Database dependency check for <c>GET /health</c> (`01` §10.3): opens a connection
/// on the configured <c>ConnectionStrings:Default</c> and runs <c>SELECT 1</c>.
/// </summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly string? _connectionString;

    public DatabaseHealthCheck(IConfiguration configuration)
        => _connectionString = configuration.GetConnectionString("Default");

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            return HealthCheckResult.Unhealthy("Database connection string is not configured.");
        }

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);

            return HealthCheckResult.Healthy("Database reachable.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Database unreachable.", exception);
        }
    }
}
