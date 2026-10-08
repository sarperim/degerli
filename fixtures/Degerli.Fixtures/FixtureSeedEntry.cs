using Degerli.Persistence;
using Degerli.Persistence.Seed;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Fixtures;

/// <summary>
/// The L4 compose.ci seeding entrypoint (test strategy §5.4, §8.3). Brings a
/// target database to the now-anchored fixture universe in one deterministic,
/// idempotent pass:
/// <list type="number">
///   <item>apply the forward-only EF migrations (which also run the application
///   seed — metric catalog, macro series, builder role/account: `02` §8);</item>
///   <item>re-run the idempotent application seed (safe no-op on a migrated DB);</item>
///   <item>apply <see cref="FixtureUniverse.Build"/> for <see cref="FixtureAnchor.NowAnchored"/>
///   through the single <see cref="FixtureSeeder"/> code path shared with L2.</item>
/// </list>
/// <para>
/// The anchor is <c>T = the seed (container-start) day</c>; fresh rows sit on
/// <c>T</c> and stale rows are offsets of it, so L4 tests assert flags/offsets
/// and never wall-clock dates (FU §8.3).
/// </para>
/// </summary>
public static class FixtureSeedEntry
{
    /// <summary>Environment key read when no connection string argument is supplied.</summary>
    public const string ConnectionStringEnvironmentKey = "ConnectionStrings__Default";

    /// <summary>Parses the connection string from an argument, falling back to the environment.</summary>
    public static string? ResolveConnectionString(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        for (var i = 0; i < args.Count; i++)
        {
            const string flag = "--connection-string";
            var arg = args[i];
            if (arg.StartsWith($"{flag}=", StringComparison.Ordinal))
            {
                return arg[(flag.Length + 1)..];
            }

            if (arg == flag && i + 1 < args.Count)
            {
                return args[i + 1];
            }

            if (!arg.StartsWith('-'))
            {
                return arg;
            }
        }

        return Environment.GetEnvironmentVariable(ConnectionStringEnvironmentKey);
    }

    /// <summary>
    /// Migrates and seeds the database. Returns a process exit code (0 on success).
    /// </summary>
    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextWriter log,
        TimeProvider? clock = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(log);

        var connectionString = ResolveConnectionString(args);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            await log.WriteLineAsync(
                $"fixture-universe: no connection string (pass --connection-string or set {ConnectionStringEnvironmentKey}).")
                .ConfigureAwait(false);
            return 2;
        }

        var anchor = FixtureAnchor.NowAnchored(clock ?? TimeProvider.System);
        var set = FixtureUniverse.Build(anchor);

        await using var db = new DegerliDbContext(DegerliDbContextOptions.Build(connectionString));
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        await DegerliDbSeeder.ApplyAsync(db, cancellationToken).ConfigureAwait(false);
        await FixtureSeeder.ApplyAsync(db, set, cancellationToken).ConfigureAwait(false);

        await log.WriteLineAsync(
            $"fixture-universe: seeded L4 now-anchored variant; T={anchor.T:yyyy-MM-dd}; " +
            $"instruments={set.Instruments.Count}; prices={set.Prices.Count}; statements={set.Statements.Count}.")
            .ConfigureAwait(false);
        return 0;
    }
}
