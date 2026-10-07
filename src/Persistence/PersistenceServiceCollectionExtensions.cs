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
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Default is not configured (architecture §10.4).");

        services.AddDbContext<DegerliDbContext>(options =>
            DegerliDbContextOptions.Configure(options, connectionString));

        return services;
    }
}
