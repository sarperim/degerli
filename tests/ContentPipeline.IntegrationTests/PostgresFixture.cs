using Degerli.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Degerli.ContentPipeline.IntegrationTests;

/// <summary>
/// A fresh, migrated PostgreSQL 17 container for the content pipeline tests. Mirrors
/// the foundation-006 harness: the database is never doubled (test strategy §7), and
/// the migrations apply the platform seed so the fixture universe can be seeded on top.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("degerli_content_test")
        .WithUsername("degerli")
        .WithPassword("degerli")
        .Build();

    public DbContextOptions<DegerliDbContext> Options { get; private set; } = null!;

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        // The fixture universe seeds a repository-known password and fails closed unless the
        // process declares Development/Test (CWE-798). Declare Test before migrations/seed.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Test");

        await _container.StartAsync();
        Options = DegerliDbContextOptions.Build(_container.GetConnectionString());

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public DegerliDbContext CreateContext() => new(Options);
}
