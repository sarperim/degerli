using Microsoft.EntityFrameworkCore.Design;

namespace Degerli.Persistence;

/// <summary>
/// Design-time factory so <c>dotnet ef migrations add</c> / <c>dotnet ef database
/// update</c> work without booting the API host (Program.cs is not required to know
/// about persistence). Reads the same environment key the runtime uses.
/// </summary>
public class DegerliDbContextFactory : IDesignTimeDbContextFactory<DegerliDbContext>
{
    private const string FallbackConnectionString =
        "Host=localhost;Port=5432;Database=degerli;Username=degerli;Password=degerli";

    public DegerliDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? FallbackConnectionString;

        return new DegerliDbContext(DegerliDbContextOptions.Build(connectionString));
    }
}
