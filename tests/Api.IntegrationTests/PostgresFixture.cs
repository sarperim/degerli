using Degerli.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// A fresh, migrated PostgreSQL 17 container shared by one test class. Migrations
/// are applied by the harness so every test runs against the real schema — the DB
/// is never doubled (test strategy §7). The container image is pinned to match the
/// compose stack.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("degerli_test")
        .WithUsername("degerli")
        .WithPassword("degerli")
        .Build();

    public DbContextOptions<DegerliDbContext> Options { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // The seed's builder-password guard fails closed: the checked-in dev/CI
        // default is only selectable in an explicitly declared Development/Test
        // environment. Declare Test before migrations (which run the seed) so the
        // harness exercises the intended dev/CI path (CWE-798; 02 §8).
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Test");

        await _container.StartAsync();
        Options = DegerliDbContextOptions.Build(_container.GetConnectionString());

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public DegerliDbContext CreateContext() => new(Options);
}
