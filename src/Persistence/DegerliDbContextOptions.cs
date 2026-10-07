using Microsoft.EntityFrameworkCore;

namespace Degerli.Persistence;

/// <summary>
/// Central place that builds the Npgsql options for the platform context so the
/// runtime registration and the design-time factory can never drift. Applies the
/// snake_case naming convention (02 header convention) and pins the migrations
/// assembly to this project.
/// </summary>
public static class DegerliDbContextOptions
{
    public const string MigrationsAssembly = "Degerli.Persistence";

    /// <summary>
    /// Applies the platform Npgsql options to an existing builder. Callers that
    /// need to compose on top of the shared configuration (e.g. the DI registration)
    /// must go through this method rather than repeating the chain.
    /// </summary>
    public static void Configure(DbContextOptionsBuilder builder, string connectionString)
    {
        builder
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(MigrationsAssembly))
            .UseSnakeCaseNamingConvention();
    }

    public static DbContextOptions<DegerliDbContext> Build(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<DegerliDbContext>();
        Configure(builder, connectionString);
        return builder.Options;
    }
}
