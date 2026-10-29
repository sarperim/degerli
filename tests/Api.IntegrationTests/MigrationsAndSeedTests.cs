using Xunit;
using System.Data;
using System.Data.Common;
using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Degerli.Persistence.Seed;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// Migrations apply to a fresh container, a re-run is a no-op (forward-only), and
/// the checked-in seed is idempotent/re-runnable (02 §8).
/// </summary>
public sealed class MigrationsAndSeedTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public MigrationsAndSeedTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Migrations_apply_to_a_fresh_container_and_rerun_is_a_noop()
    {
        await using var db = _fixture.CreateContext();

        var appliedAfterFirstRun = await CountAsync(db, "\"__EFMigrationsHistory\"");
        Assert.True(appliedAfterFirstRun >= 1, "No migrations were applied.");

        // Re-running `database update` must not change the applied set.
        await db.Database.MigrateAsync();

        var appliedAfterSecondRun = await CountAsync(db, "\"__EFMigrationsHistory\"");
        Assert.Equal(appliedAfterFirstRun, appliedAfterSecondRun);
    }

    [Fact]
    public async Task Seed_is_idempotent_and_rerunnable()
    {
        await using var db = _fixture.CreateContext();

        // The initial migration already applied the seed; apply it twice more.
        await DegerliDbSeeder.ApplyAsync(db);
        await DegerliDbSeeder.ApplyAsync(db);

        // The catalog is 32 rows: 18 through v1; the key-value & moat panel's 14 additions
        // land it at 32 — TKT-foundation-096, `02` §6.2 v2.
        Assert.Equal(32, await CountAsync(db, "metric_catalog"));
        Assert.Equal(6, await CountAsync(db, "macro_series"));
        Assert.Equal(1, await ScalarIntAsync(db,
            "SELECT count(*)::int FROM asp_net_roles WHERE normalized_name = 'BUILDER'"));
        Assert.Equal(1, await ScalarIntAsync(db,
            "SELECT count(*)::int FROM asp_net_users WHERE normalized_email = 'BUILDER@DEGERLI.LOCAL'"));
        Assert.Equal(1, await ScalarIntAsync(db,
            "SELECT count(*)::int FROM asp_net_user_roles"));
    }

    [Fact]
    public async Task Builder_account_is_seeded_with_the_expected_password_and_language()
    {
        await using var db = _fixture.CreateContext();

        var row = await QueryAsync(db, """
            SELECT password_hash, language_pref, email_confirmed
            FROM asp_net_users
            WHERE normalized_email = 'BUILDER@DEGERLI.LOCAL'
            """);
        var account = Assert.Single(row);

        var hasher = new PasswordHasher<ApplicationUser>();
        var result = hasher.VerifyHashedPassword(
            new ApplicationUser { UserName = DegerliSeed.BuilderEmail },
            (string)account[0]!,
            DegerliSeed.BuilderPassword);

        Assert.Equal(PasswordVerificationResult.Success, result);
        Assert.Equal("tr", (string)account[1]!);
        Assert.True((bool)account[2]!);
    }

    [Fact]
    public async Task Accounting_profile_column_defaults_to_industrial_and_the_seed_is_checked_in()
    {
        await using var db = _fixture.CreateContext();

        // 02 §3.1 — the instruments.accounting_profile column defaults to `industrial`.
        var columnDefault = await ScalarStringAsync(db, """
            SELECT column_default
            FROM information_schema.columns
            WHERE table_name = 'instruments' AND column_name = 'accounting_profile'
            """);
        Assert.NotNull(columnDefault);
        Assert.Contains("industrial", columnDefault!);

        // The checked-in builder-curated classification resolves BIST financial members and
        // falls back to `industrial` for everything else (02 §3.1).
        Assert.Equal(AccountingProfiles.Bank, AccountingProfiles.GetOrDefault("GARAN"));
        Assert.Equal(AccountingProfiles.Insurance, AccountingProfiles.GetOrDefault("ANSGR"));
        Assert.Equal(AccountingProfiles.Reit, AccountingProfiles.GetOrDefault("EKGYO"));
        Assert.Equal(AccountingProfiles.Industrial, AccountingProfiles.GetOrDefault("ALFA"));
        Assert.Equal(AccountingProfiles.Industrial, AccountingProfiles.GetOrDefault(null));
    }

    private static async Task<string?> ScalarStringAsync(DegerliDbContext db, string sql)
    {
        await using var command = await CommandAsync(db, sql);
        return (string?)(await command.ExecuteScalarAsync());
    }

    private static async Task<int> CountAsync(DegerliDbContext db, string table) =>
        await ScalarIntAsync(db, $"SELECT count(*)::int FROM {table}");

    private static async Task<int> ScalarIntAsync(DegerliDbContext db, string sql)
    {
        await using var command = await CommandAsync(db, sql);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<List<object?[]>> QueryAsync(DegerliDbContext db, string sql)
    {
        await using var command = await CommandAsync(db, sql);
        var rows = new List<object?[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var values = new object[reader.FieldCount];
            reader.GetValues(values);
            rows.Add(values);
        }

        return rows;
    }

    private static async Task<DbCommand> CommandAsync(DegerliDbContext db, string sql)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var command = connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }
}
