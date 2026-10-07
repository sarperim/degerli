using Xunit;
using System.Data;
using System.Data.Common;
using System.Globalization;
using Degerli.Persistence;
using Degerli.Persistence.Seed;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// TC-MDF-009 — schema enforces the data-model invariants. Structural conformance
/// against a freshly migrated container, plus focused behavioral checks for the
/// publish gate. Migrations/seed behavior is covered in
/// <see cref="MigrationsAndSeedTests"/>.
/// </summary>
public sealed class SchemaConformanceTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public SchemaConformanceTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Every_data_model_table_and_view_exists()
    {
        await using var db = _fixture.CreateContext();

        var tables = (await QueryAsync(db,
                "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'"))
            .Select(r => (string)r[0]!)
            .ToHashSet(StringComparer.Ordinal);

        string[] expectedTables =
        [
            "asp_net_users", "asp_net_roles", "asp_net_user_roles", "asp_net_user_claims",
            "asp_net_user_logins", "asp_net_user_tokens", "asp_net_role_claims",
            "sectors", "instruments", "indices", "index_constituents", "daily_prices", "index_levels",
            "financial_statements", "fin_line_items", "dividends", "corporate_actions", "kap_disclosures",
            "derived_metrics", "coverage_metadata", "ingest_runs", "quarantined_facts",
            "macro_series", "macro_values", "market_snapshots",
            "metric_catalog", "saved_screens",
            "business_descriptions", "sector_metric_medians",
            "dcf_baselines", "dcf_scenarios",
            "consent_records",
            "funds", "fund_navs", "fund_performances", "fund_holdings",
        ];
        Assert.Empty(expectedTables.Except(tables));

        var views = (await QueryAsync(db,
                "SELECT viewname FROM pg_views WHERE schemaname = 'public'"))
            .Select(r => (string)r[0]!)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("v_current_universe", views);
        Assert.Contains("v_data_freshness", views);
    }

    [Fact]
    public async Task Fact_tables_carry_not_null_source_ref_and_recorded_at()
    {
        await using var db = _fixture.CreateContext();

        string[] factTables =
        [
            "instruments", "index_constituents", "daily_prices", "index_levels",
            "financial_statements", "dividends", "corporate_actions", "kap_disclosures",
            "macro_values", "funds", "fund_navs", "fund_performances", "fund_holdings",
        ];

        foreach (var table in factTables)
        {
            var rows = await QueryAsync(db, $"""
                SELECT column_name, is_nullable
                FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = '{table}'
                  AND column_name IN ('source_ref', 'recorded_at')
                """);

            var nullability = rows.ToDictionary(r => (string)r[0]!, r => (string)r[1]!);
            Assert.True(nullability.ContainsKey("source_ref"), $"{table}.source_ref is missing");
            Assert.True(nullability.ContainsKey("recorded_at"), $"{table}.recorded_at is missing");
            Assert.Equal("NO", nullability["source_ref"]);
            Assert.Equal("NO", nullability["recorded_at"]);
        }
    }

    [Theory]
    [InlineData("financial_statements", new[] { "instrument_id", "period_type", "period_end_date", "statement_type", "version" })]
    [InlineData("daily_prices", new[] { "instrument_id", "price_date" })]
    [InlineData("saved_screens", new[] { "user_id", "name" })]
    [InlineData("dcf_scenarios", new[] { "user_id", "instrument_id", "name" })]
    public async Task Required_uniqueness_is_enforced(string table, string[] columns)
    {
        await using var db = _fixture.CreateContext();

        var indexDefs = (await QueryAsync(db,
                $"SELECT indexdef FROM pg_indexes WHERE schemaname = 'public' AND tablename = '{table}'"))
            .Select(r => (string)r[0]!)
            .ToList();

        var match = indexDefs.Any(def =>
            (def.Contains("PRIMARY KEY", StringComparison.OrdinalIgnoreCase)
             || def.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase))
            && columns.All(c => def.Contains(c, StringComparison.OrdinalIgnoreCase)));

        Assert.True(match,
            $"No unique/primary index on {table} covering ({string.Join(", ", columns)}). Found: {string.Join(" | ", indexDefs)}");
    }

    [Fact]
    public async Task Published_business_description_requires_both_texts()
    {
        await using var db = _fixture.CreateContext();
        var instrumentId = await InsertInstrumentAsync(db, "TSTPUB");

        // Draft may be incomplete.
        await ExecAsync(db,
            $"INSERT INTO business_descriptions (instrument_id, version, status) VALUES ({instrumentId}, 1, 'draft')");

        // Published with a missing English text is rejected by the CHECK constraint.
        var ex = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => ExecAsync(db,
            $"INSERT INTO business_descriptions (instrument_id, version, status, text_tr) VALUES ({instrumentId}, 2, 'published', 'metin')"));
        Assert.Equal("23514", ex.SqlState);

        // Published with both texts is accepted.
        await ExecAsync(db,
            $"INSERT INTO business_descriptions (instrument_id, version, status, text_tr, text_en) VALUES ({instrumentId}, 3, 'published', 'metin', 'text')");
    }

    [Fact]
    public async Task Current_universe_view_returns_only_active_xu100_members()
    {
        await using var db = _fixture.CreateContext();

        var sectorId = await ScalarLongAsync(db,
            "INSERT INTO sectors (code, name_tr, name_en) VALUES ('TSTSEC', 'Test', 'Test') RETURNING id");
        var memberId = await InsertInstrumentAsync(db, "TSTMEM", sectorId);
        var nonMemberId = await InsertInstrumentAsync(db, "TSTNON", sectorId);
        var exMemberId = await InsertInstrumentAsync(db, "TSTEX", sectorId);

        var xu100 = await ScalarLongAsync(db,
            "INSERT INTO indices (code, name_tr, name_en) VALUES ('XU100', 'BIST 100', 'BIST 100') RETURNING id");
        var xu30 = await ScalarLongAsync(db,
            "INSERT INTO indices (code, name_tr, name_en) VALUES ('XU30', 'BIST 30', 'BIST 30') RETURNING id");

        await ExecAsync(db, $"INSERT INTO index_constituents (index_id, instrument_id, effective_from, source_ref, recorded_at) VALUES ({xu100}, {memberId}, DATE '2020-01-01', 'seed', now())");
        await ExecAsync(db, $"INSERT INTO index_constituents (index_id, instrument_id, effective_from, source_ref, recorded_at) VALUES ({xu30}, {nonMemberId}, DATE '2020-01-01', 'seed', now())");
        await ExecAsync(db, $"INSERT INTO index_constituents (index_id, instrument_id, effective_from, effective_to, source_ref, recorded_at) VALUES ({xu100}, {exMemberId}, DATE '2020-01-01', DATE '2021-01-01', 'seed', now())");

        var symbols = (await QueryAsync(db, "SELECT symbol FROM v_current_universe"))
            .Select(r => (string)r[0]!)
            .ToList();

        Assert.Contains("TSTMEM", symbols);
        Assert.DoesNotContain("TSTNON", symbols);
        Assert.DoesNotContain("TSTEX", symbols);
    }

    [Fact]
    public async Task Data_freshness_view_exposes_last_success_per_job()
    {
        await using var db = _fixture.CreateContext();

        await ExecAsync(db, """
            INSERT INTO ingest_runs (job_code, started_at, finished_at, status, stats_json)
            VALUES
                ('tst_job', now() - interval '3 hours', now() - interval '2 hours', 'failed', '{}'::jsonb),
                ('tst_job', now() - interval '2 hours', now() - interval '1 hour', 'succeeded', '{}'::jsonb)
            """);

        var rows = await QueryAsync(db,
            "SELECT last_success_at, last_failure_at FROM v_data_freshness WHERE job_code = 'tst_job'");

        var row = Assert.Single(rows);
        Assert.NotEqual(DBNull.Value, row[0]);
        Assert.NotEqual(DBNull.Value, row[1]);
    }

    private static async Task<long> InsertInstrumentAsync(DegerliDbContext db, string symbol, long? sectorId = null)
    {
        var sector = sectorId is null ? "NULL" : sectorId.Value.ToString(CultureInfo.InvariantCulture);
        return await ScalarLongAsync(db, $"""
            INSERT INTO instruments (symbol, name, sector_id, source_ref, recorded_at)
            VALUES ('{symbol}', 'Test {symbol}', {sector}, 'seed', now())
            RETURNING id
            """);
    }

    private static async Task<List<object?[]>> QueryAsync(DegerliDbContext db, string sql)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

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

    private static async Task<int> ExecAsync(DegerliDbContext db, string sql)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarLongAsync(DegerliDbContext db, string sql)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
