using Degerli.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
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
        var hasher = new PasswordHasher<ApplicationUser>();
        var passwordHash = hasher.HashPassword(
            new ApplicationUser { UserName = DegerliSeed.BuilderEmail, Email = DegerliSeed.BuilderEmail },
            DegerliSeed.BuilderPassword);

        foreach (var statement in DegerliSeed.Statements(passwordHash))
        {
            await context.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }
    }
}
