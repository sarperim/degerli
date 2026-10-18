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
///   through the single <see cref="FixtureSeeder"/> code path shared with L2;</item>
///   <item>run the derived-data jobs (<c>metrics-recompute</c> then <c>market-snapshot</c>)
///   at the anchor through <see cref="FixtureDerivedData"/>, so the stack serves populated
///   market/derived reads (TKT-foundation-013). This step belongs to the L4 entrypoint only:
///   the L2 variant keeps seeding facts and running the jobs itself.</item>
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

        var timeProvider = clock ?? TimeProvider.System;
        var anchor = FixtureAnchor.NowAnchored(timeProvider);
        var set = FixtureUniverse.Build(anchor);

        await using var db = new DegerliDbContext(DegerliDbContextOptions.Build(connectionString));
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        await DegerliDbSeeder.ApplyAsync(db, cancellationToken).ConfigureAwait(false);
        await FixtureSeeder.ApplyAsync(db, set, cancellationToken).ConfigureAwait(false);

        // Honesty guard (01 v1.5 §10.9.9, TKT-foundation-023): the FU universe is a synthetic
        // world. Seeding it into a database that already holds real rows (or that a browsing
        // surface will read) yields a mixed universe — the exact condition that produced the
        // 116 = 100 real + 16 fixture stock list. This is a log line only: exit code,
        // idempotency and the L4 contract are unchanged. Real-only stacks use the README
        // "Dev-stack data" reset path instead.
        await log.WriteLineAsync(
            "fixture-universe: WARNING — seeding the synthetic FU universe into this database. " +
            "FU instruments/memberships coexist with any real rows already stored, so the " +
            "browsing surface will mix synthetic and real data. Run this entrypoint only against " +
            "the L2/L4 CI database or an opt-in dev/demo database — never against a database " +
            "meant to browse real data (see README \"Dev-stack data\").")
            .ConfigureAwait(false);

        await log.WriteLineAsync(
            $"fixture-universe: seeded L4 now-anchored variant; T={anchor.T:yyyy-MM-dd}; " +
            $"instruments={set.Instruments.Count}; prices={set.Prices.Count}; statements={set.Statements.Count}.")
            .ConfigureAwait(false);

        // Derived data only after every fact is seeded, at the same anchor/clock used to
        // build the fixture set (FU §8.3). Idempotent on the jobs' keys, so a re-run
        // upserts in place.
        await FixtureDerivedData.ApplyAsync(connectionString, anchor, log, timeProvider, cancellationToken)
            .ConfigureAwait(false);

        return 0;
    }
}
