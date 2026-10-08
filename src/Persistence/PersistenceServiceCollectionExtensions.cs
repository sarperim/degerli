using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Degerli.Persistence;

/// <summary>
/// Persistence registration lives in this extension file (not in Program.cs): the
/// API host calls <see cref="AddDegerliPersistence"/> from its composition setup.
/// </summary>
public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the platform <see cref="DegerliDbContext"/> against the
    /// <c>ConnectionStrings:Default</c> connection string (architecture §10.4).
    /// </summary>
    public static IServiceCollection AddDegerliPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Resolve the connection string from the built service provider rather than
        // eagerly from the configuration snapshot: under WebApplicationFactory the
        // test host's configuration overrides are applied after this registration
        // runs, so an eager read would miss them.
        services.AddDbContext<DegerliDbContext>((serviceProvider, options) =>
        {
            var resolved = serviceProvider.GetRequiredService<IConfiguration>();
            var connectionString = resolved.GetConnectionString("Default")
                ?? configuration.GetConnectionString("Default")
                ?? throw new InvalidOperationException(
                    "ConnectionStrings:Default is not configured (architecture §10.4).");

            DegerliDbContextOptions.Configure(options, connectionString);
        });

        return services;
    }
}
