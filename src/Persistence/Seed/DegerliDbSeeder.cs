using Microsoft.EntityFrameworkCore;

namespace Degerli.Persistence.Seed;

/// <summary>
/// Re-runnable application of <see cref="DegerliSeed"/>. The same statement list is
/// executed by the initial migration; this entry point exists so seeding can be
/// invoked again (e.g. tests, or a future migrate step) without duplicating rows.
/// </summary>
public static class DegerliDbSeeder
{
    public static async Task ApplyAsync(
        DegerliDbContext context,
        CancellationToken cancellationToken = default)
    {
        foreach (var statement in DegerliSeed.Statements())
        {
            await context.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }
    }
}
