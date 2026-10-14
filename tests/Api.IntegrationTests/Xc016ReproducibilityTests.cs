using System.Net;
using Degerli.Api.IntegrationTests.Harness;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TC-XC-016 — reproducibility: a fresh stack from the repo comes up healthy before
/// anything runs (NFR-MDF-005, SC-007, `01` §6). The executable CI form is the e2e
/// job's <c>docker compose -f compose.ci.yml up</c>; here the API + real (migrated,
/// Testcontainers) PostgreSQL half is exercised end-to-end — <c>GET /health</c> must
/// return 200 driven by the live database check, not a stub — and the compose
/// declaration is asserted so the CI bootstrap cannot silently drift.
/// </summary>
public sealed class Xc016ReproducibilityTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _postgres;

    public Xc016ReproducibilityTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task Fresh_migrated_api_serves_health_with_the_live_database_check()
    {
        await using var factory = new DegerliAppFactory(_postgres.ConnectionString);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public void Compose_ci_declares_the_reproducible_stack_and_its_health_gate()
    {
        var composePath = Path.Combine(FindRepositoryRoot(), "compose.ci.yml");
        Assert.True(File.Exists(composePath), $"compose.ci.yml not found at {composePath}");
        var compose = File.ReadAllText(composePath);

        // The stack the reproducible quickstart promises: API + PostgreSQL + seeded
        // fixtures + MailPit (test strategy §7).
        Assert.Contains("postgres:", compose, StringComparison.Ordinal);
        Assert.Contains("mailpit:", compose, StringComparison.Ordinal);
        Assert.Contains("fixtures:", compose, StringComparison.Ordinal);
        Assert.Contains("api:", compose, StringComparison.Ordinal);

        // The API only starts after the one-shot fixtures service completes…
        Assert.Contains("service_completed_successfully", compose, StringComparison.Ordinal);
        // …and its healthcheck gates readiness on the same /health endpoint the tests wait for.
        Assert.Contains("/health", compose, StringComparison.Ordinal);
    }

    /// <summary>Walks up from the test bin directory to the checkout that holds compose.ci.yml.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "compose.ci.yml")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root (compose.ci.yml).");
    }
}
