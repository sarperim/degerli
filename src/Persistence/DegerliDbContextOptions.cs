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

    public static DbContextOptions<DegerliDbContext> Build(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<DegerliDbContext>();
        builder
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(MigrationsAssembly))
            .UseSnakeCaseNamingConvention();
        return builder.Options;
    }
}
