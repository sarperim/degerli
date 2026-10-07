using System;
using Degerli.Persistence.Seed;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Degerli.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "asp_net_roles",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "asp_net_users",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    language_pref = table.Column<string>(type: "text", nullable: false, defaultValue: "tr"),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "coverage_metadata",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    scope = table.Column<string>(type: "text", nullable: false),
                    instrument_id = table.Column<long>(type: "bigint", nullable: true),
                    fund_id = table.Column<string>(type: "text", nullable: true),
                    data_type = table.Column<string>(type: "text", nullable: false),
                    available_from = table.Column<DateOnly>(type: "date", nullable: true),
                    available_to = table.Column<DateOnly>(type: "date", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_coverage_metadata", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "funds",
                columns: table => new
                {
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    fund_type = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: true),
                    source_ref = table.Column<string>(type: "text", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_funds", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "indices",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "text", nullable: false),
                    name_tr = table.Column<string>(type: "text", nullable: false),
                    name_en = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_indices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ingest_runs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_code = table.Column<string>(type: "text", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    status = table.Column<string>(type: "text", nullable: true),
                    stats_json = table.Column<string>(type: "jsonb", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ingest_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "macro_series",
                columns: table => new
                {
                    code = table.Column<string>(type: "text", nullable: false),
                    name_tr = table.Column<string>(type: "text", nullable: false),
                    name_en = table.Column<string>(type: "text", nullable: false),
                    unit = table.Column<string>(type: "text", nullable: true),
                    source_name = table.Column<string>(type: "text", nullable: true),
                    cadence = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_macro_series", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "market_snapshots",
                columns: table => new
                {
                    snapshot_date = table.Column<DateOnly>(type: "date", nullable: false),
                    xu100_level = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    xu100_change_pct = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    xu30_level = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    xu30_change_pct = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    breadth_advancing = table.Column<int>(type: "integer", nullable: true),
                    breadth_declining = table.Column<int>(type: "integer", nullable: true),
                    breadth_unchanged = table.Column<int>(type: "integer", nullable: true),
                    volume_total = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    market_pe = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    market_pe_excluded_count = table.Column<int>(type: "integer", nullable: true),
                    market_div_yield = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    gainers_json = table.Column<string>(type: "jsonb", nullable: true),
                    losers_json = table.Column<string>(type: "jsonb", nullable: true),
                    sector_perf_json = table.Column<string>(type: "jsonb", nullable: true),
                    as_of_trading_date = table.Column<DateOnly>(type: "date", nullable: false),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_market_snapshots", x => x.snapshot_date);
                });

            migrationBuilder.CreateTable(
                name: "metric_catalog",
                columns: table => new
                {
                    metric_code = table.Column<string>(type: "text", nullable: false),
                    family = table.Column<string>(type: "text", nullable: false),
                    unit = table.Column<string>(type: "text", nullable: true),
                    label_tr = table.Column<string>(type: "text", nullable: false),
                    label_en = table.Column<string>(type: "text", nullable: false),
                    description_tr = table.Column<string>(type: "text", nullable: true),
                    description_en = table.Column<string>(type: "text", nullable: true),
                    is_screenable = table.Column<bool>(type: "boolean", nullable: false),
                    is_growth_cagr = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_metric_catalog", x => x.metric_code);
                });

            migrationBuilder.CreateTable(
                name: "quarantined_facts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_code = table.Column<string>(type: "text", nullable: false),
                    source_ref = table.Column<string>(type: "text", nullable: true),
                    payload_json = table.Column<string>(type: "jsonb", nullable: true),
                    reason_code = table.Column<string>(type: "text", nullable: false),
                    quarantined_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    resolution_note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quarantined_facts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sectors",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "text", nullable: false),
                    name_tr = table.Column<string>(type: "text", nullable: false),
                    name_en = table.Column<string>(type: "text", nullable: false),
                    parent_sector_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sectors", x => x.id);
                    table.ForeignKey(
                        name: "fk_sectors_sectors_parent_sector_id",
                        column: x => x.parent_sector_id,
                        principalTable: "sectors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "asp_net_role_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    role_id = table.Column<long>(type: "bigint", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_role_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_asp_net_role_claims_asp_net_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "asp_net_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "asp_net_user_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_user_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_asp_net_user_claims_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "asp_net_user_logins",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_user_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_asp_net_user_logins_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "asp_net_user_roles",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    role_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_user_roles", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_asp_net_user_roles_asp_net_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "asp_net_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_asp_net_user_roles_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "asp_net_user_tokens",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_asp_net_user_tokens_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "consent_records",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: true),
                    user_ref_hash = table.Column<string>(type: "text", nullable: false),
                    notice_version = table.Column<string>(type: "text", nullable: false),
                    consented_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consent_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_consent_records_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "saved_screens",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    criteria_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saved_screens", x => x.id);
                    table.ForeignKey(
                        name: "fk_saved_screens_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fund_navs",
                columns: table => new
                {
                    fund_id = table.Column<string>(type: "text", nullable: false),
                    nav_date = table.Column<DateOnly>(type: "date", nullable: false),
                    nav_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    source_ref = table.Column<string>(type: "text", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fund_navs", x => new { x.fund_id, x.nav_date });
                    table.ForeignKey(
                        name: "fk_fund_navs_funds_fund_id",
                        column: x => x.fund_id,
                        principalTable: "funds",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fund_performances",
                columns: table => new
                {
                    fund_id = table.Column<string>(type: "text", nullable: false),
                    period = table.Column<string>(type: "text", nullable: false),
                    as_of_date = table.Column<DateOnly>(type: "date", nullable: false),
                    return_value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    source_ref = table.Column<string>(type: "text", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fund_performances", x => new { x.fund_id, x.period, x.as_of_date });
                    table.ForeignKey(
                        name: "fk_fund_performances_funds_fund_id",
                        column: x => x.fund_id,
                        principalTable: "funds",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "index_levels",
                columns: table => new
                {
                    index_id = table.Column<long>(type: "bigint", nullable: false),
                    level_date = table.Column<DateOnly>(type: "date", nullable: false),
                    close = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    source_ref = table.Column<string>(type: "text", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_index_levels", x => new { x.index_id, x.level_date });
                    table.ForeignKey(
                        name: "fk_index_levels_indices_index_id",
                        column: x => x.index_id,
                        principalTable: "indices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "macro_values",
                columns: table => new
                {
                    series_code = table.Column<string>(type: "text", nullable: false),
                    value_date = table.Column<DateOnly>(type: "date", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    source_ref = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_macro_values", x => new { x.series_code, x.value_date, x.recorded_at });
                    table.ForeignKey(
                        name: "fk_macro_values_macro_series_series_code",
                        column: x => x.series_code,
                        principalTable: "macro_series",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "instruments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    symbol = table.Column<string>(type: "text", nullable: false),
                    isin = table.Column<string>(type: "text", nullable: true),
                    name = table.Column<string>(type: "text", nullable: false),
                    sector_id = table.Column<long>(type: "bigint", nullable: true),
                    listing_date = table.Column<DateOnly>(type: "date", nullable: true),
                    delisting_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "text", nullable: true),
                    source_ref = table.Column<string>(type: "text", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_instruments", x => x.id);
                    table.ForeignKey(
                        name: "fk_instruments_sectors_sector_id",
                        column: x => x.sector_id,
                        principalTable: "sectors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sector_metric_medians",
                columns: table => new
                {
                    sector_id = table.Column<long>(type: "bigint", nullable: false),
                    metric_code = table.Column<string>(type: "text", nullable: false),
                    as_of_date = table.Column<DateOnly>(type: "date", nullable: false),
                    median_value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    peer_count = table.Column<int>(type: "integer", nullable: true),
                    excluded_count = table.Column<int>(type: "integer", nullable: true),
                    is_adjusted = table.Column<bool>(type: "boolean", nullable: false),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sector_metric_medians", x => new { x.sector_id, x.metric_code, x.as_of_date });
                    table.ForeignKey(
                        name: "fk_sector_metric_medians_sectors_sector_id",
                        column: x => x.sector_id,
                        principalTable: "sectors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "business_descriptions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    instrument_id = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    text_tr = table.Column<string>(type: "text", nullable: true),
                    text_en = table.Column<string>(type: "text", nullable: true),
                    source_refs_json = table.Column<string>(type: "jsonb", nullable: true),
                    last_reviewed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_descriptions", x => x.id);
                    table.CheckConstraint("ck_business_descriptions_published_texts", "status <> 'published' OR (text_tr IS NOT NULL AND text_en IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_business_descriptions_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "corporate_actions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    instrument_id = table.Column<long>(type: "bigint", nullable: false),
                    action_type = table.Column<string>(type: "text", nullable: false),
                    action_date = table.Column<DateOnly>(type: "date", nullable: false),
                    terms_json = table.Column<string>(type: "jsonb", nullable: true),
                    source_ref = table.Column<string>(type: "text", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_corporate_actions", x => x.id);
                    table.ForeignKey(
                        name: "fk_corporate_actions_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "daily_prices",
                columns: table => new
                {
                    instrument_id = table.Column<long>(type: "bigint", nullable: false),
                    price_date = table.Column<DateOnly>(type: "date", nullable: false),
                    open = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    high = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    low = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    close_raw = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    close_adjusted = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    volume = table.Column<long>(type: "bigint", nullable: true),
                    source_ref = table.Column<string>(type: "text", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_prices", x => new { x.instrument_id, x.price_date });
                    table.ForeignKey(
                        name: "fk_daily_prices_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dcf_baselines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    instrument_id = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    params_json = table.Column<string>(type: "jsonb", nullable: false),
                    build_date = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dcf_baselines", x => x.id);
                    table.ForeignKey(
                        name: "fk_dcf_baselines_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dcf_scenarios",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    instrument_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    params_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dcf_scenarios", x => x.id);
                    table.ForeignKey(
                        name: "fk_dcf_scenarios_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_dcf_scenarios_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "derived_metrics",
                columns: table => new
                {
                    instrument_id = table.Column<long>(type: "bigint", nullable: false),
                    metric_code = table.Column<string>(type: "text", nullable: false),
                    as_of_date = table.Column<DateOnly>(type: "date", nullable: false),
                    value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    window_years = table.Column<int>(type: "integer", nullable: true),
                    is_adjusted = table.Column<bool>(type: "boolean", nullable: false),
                    is_rested = table.Column<bool>(type: "boolean", nullable: false),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_derived_metrics", x => new { x.instrument_id, x.metric_code, x.as_of_date });
                    table.ForeignKey(
                        name: "fk_derived_metrics_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dividends",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    instrument_id = table.Column<long>(type: "bigint", nullable: false),
                    ex_date = table.Column<DateOnly>(type: "date", nullable: false),
                    pay_date = table.Column<DateOnly>(type: "date", nullable: true),
                    amount_per_share = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "text", nullable: true),
                    source_ref = table.Column<string>(type: "text", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dividends", x => x.id);
                    table.ForeignKey(
                        name: "fk_dividends_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "financial_statements",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    instrument_id = table.Column<long>(type: "bigint", nullable: false),
                    period_type = table.Column<string>(type: "text", nullable: false),
                    period_end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    fiscal_year = table.Column<int>(type: "integer", nullable: true),
                    statement_type = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<string>(type: "text", nullable: false),
                    restatement_date = table.Column<DateOnly>(type: "date", nullable: true),
                    published_at = table.Column<DateOnly>(type: "date", nullable: true),
                    source_ref = table.Column<string>(type: "text", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_financial_statements", x => x.id);
                    table.ForeignKey(
                        name: "fk_financial_statements_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fund_holdings",
                columns: table => new
                {
                    fund_id = table.Column<string>(type: "text", nullable: false),
                    as_of_date = table.Column<DateOnly>(type: "date", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    instrument_id = table.Column<long>(type: "bigint", nullable: true),
                    name_raw = table.Column<string>(type: "text", nullable: true),
                    weight = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    units = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    source_ref = table.Column<string>(type: "text", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fund_holdings", x => new { x.fund_id, x.as_of_date, x.line_no });
                    table.ForeignKey(
                        name: "fk_fund_holdings_funds_fund_id",
                        column: x => x.fund_id,
                        principalTable: "funds",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_fund_holdings_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "index_constituents",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    index_id = table.Column<long>(type: "bigint", nullable: false),
                    instrument_id = table.Column<long>(type: "bigint", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    source_ref = table.Column<string>(type: "text", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_index_constituents", x => x.id);
                    table.ForeignKey(
                        name: "fk_index_constituents_indices_index_id",
                        column: x => x.index_id,
                        principalTable: "indices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_index_constituents_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "kap_disclosures",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    instrument_id = table.Column<long>(type: "bigint", nullable: false),
                    disclosure_type = table.Column<string>(type: "text", nullable: true),
                    publish_date = table.Column<DateOnly>(type: "date", nullable: true),
                    title = table.Column<string>(type: "text", nullable: true),
                    source_url = table.Column<string>(type: "text", nullable: true),
                    document_path = table.Column<string>(type: "text", nullable: true),
                    source_ref = table.Column<string>(type: "text", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_kap_disclosures", x => x.id);
                    table.ForeignKey(
                        name: "fk_kap_disclosures_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fin_line_items",
                columns: table => new
                {
                    statement_id = table.Column<long>(type: "bigint", nullable: false),
                    item_code = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fin_line_items", x => new { x.statement_id, x.item_code });
                    table.ForeignKey(
                        name: "fk_fin_line_items_financial_statements_statement_id",
                        column: x => x.statement_id,
                        principalTable: "financial_statements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_role_claims_role_id",
                table: "asp_net_role_claims",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "asp_net_roles",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_user_claims_user_id",
                table: "asp_net_user_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_user_logins_user_id",
                table: "asp_net_user_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_user_roles_role_id",
                table: "asp_net_user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "asp_net_users",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "asp_net_users",
                column: "normalized_user_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_business_descriptions_instrument_id",
                table: "business_descriptions",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "ix_consent_records_user_id",
                table: "consent_records",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_corporate_actions_instrument_id",
                table: "corporate_actions",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "ix_dcf_baselines_instrument_id",
                table: "dcf_baselines",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "ix_dcf_scenarios_instrument_id",
                table: "dcf_scenarios",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "ix_dcf_scenarios_user_id_instrument_id_name",
                table: "dcf_scenarios",
                columns: new[] { "user_id", "instrument_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dividends_instrument_id",
                table: "dividends",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "ix_financial_statements_instrument_id_period_type_period_end_d",
                table: "financial_statements",
                columns: new[] { "instrument_id", "period_type", "period_end_date", "statement_type", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fund_holdings_instrument_id",
                table: "fund_holdings",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "ix_index_constituents_index_id",
                table: "index_constituents",
                column: "index_id");

            migrationBuilder.CreateIndex(
                name: "ix_index_constituents_instrument_id",
                table: "index_constituents",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "ix_indices_code",
                table: "indices",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_instruments_sector_id",
                table: "instruments",
                column: "sector_id");

            migrationBuilder.CreateIndex(
                name: "ix_instruments_symbol",
                table: "instruments",
                column: "symbol",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_kap_disclosures_instrument_id",
                table: "kap_disclosures",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "ix_saved_screens_user_id_name",
                table: "saved_screens",
                columns: new[] { "user_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sectors_code",
                table: "sectors",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sectors_parent_sector_id",
                table: "sectors",
                column: "parent_sector_id");

            // Read-only views defined by the data model (02 §3.1, §3.2).
            migrationBuilder.Sql(DegerliViews.CurrentUniverse);
            migrationBuilder.Sql(DegerliViews.DataFreshness);

            // Checked-in, idempotent seed data (02 §8): 18 metric-catalog rows,
            // 6 macro series, and the builder role + bootstrap account.
            foreach (var seedStatement in DegerliSeed.Statements())
            {
                migrationBuilder.Sql(seedStatement);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS v_data_freshness;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS v_current_universe;");

            migrationBuilder.DropTable(
                name: "asp_net_role_claims");

            migrationBuilder.DropTable(
                name: "asp_net_user_claims");

            migrationBuilder.DropTable(
                name: "asp_net_user_logins");

            migrationBuilder.DropTable(
                name: "asp_net_user_roles");

            migrationBuilder.DropTable(
                name: "asp_net_user_tokens");

            migrationBuilder.DropTable(
                name: "business_descriptions");

            migrationBuilder.DropTable(
                name: "consent_records");

            migrationBuilder.DropTable(
                name: "corporate_actions");

            migrationBuilder.DropTable(
                name: "coverage_metadata");

            migrationBuilder.DropTable(
                name: "daily_prices");

            migrationBuilder.DropTable(
                name: "dcf_baselines");

            migrationBuilder.DropTable(
                name: "dcf_scenarios");

            migrationBuilder.DropTable(
                name: "derived_metrics");

            migrationBuilder.DropTable(
                name: "dividends");

            migrationBuilder.DropTable(
                name: "fin_line_items");

            migrationBuilder.DropTable(
                name: "fund_holdings");

            migrationBuilder.DropTable(
                name: "fund_navs");

            migrationBuilder.DropTable(
                name: "fund_performances");

            migrationBuilder.DropTable(
                name: "index_constituents");

            migrationBuilder.DropTable(
                name: "index_levels");

            migrationBuilder.DropTable(
                name: "ingest_runs");

            migrationBuilder.DropTable(
                name: "kap_disclosures");

            migrationBuilder.DropTable(
                name: "macro_values");

            migrationBuilder.DropTable(
                name: "market_snapshots");

            migrationBuilder.DropTable(
                name: "metric_catalog");

            migrationBuilder.DropTable(
                name: "quarantined_facts");

            migrationBuilder.DropTable(
                name: "saved_screens");

            migrationBuilder.DropTable(
                name: "sector_metric_medians");

            migrationBuilder.DropTable(
                name: "asp_net_roles");

            migrationBuilder.DropTable(
                name: "financial_statements");

            migrationBuilder.DropTable(
                name: "funds");

            migrationBuilder.DropTable(
                name: "indices");

            migrationBuilder.DropTable(
                name: "macro_series");

            migrationBuilder.DropTable(
                name: "asp_net_users");

            migrationBuilder.DropTable(
                name: "instruments");

            migrationBuilder.DropTable(
                name: "sectors");
        }
    }
}
