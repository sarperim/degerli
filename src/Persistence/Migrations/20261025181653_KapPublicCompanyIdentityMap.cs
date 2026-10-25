using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Degerli.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class KapPublicCompanyIdentityMap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "kap_public_company_map",
                columns: table => new
                {
                    instrument_id = table.Column<long>(type: "bigint", nullable: false),
                    kap_public_oid = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    kap_public_oid_source = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    kap_public_oid_as_of = table.Column<DateOnly>(type: "date", nullable: false),
                    vyk_company_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    vyk_company_id_source = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    vyk_company_id_as_of = table.Column<DateOnly>(type: "date", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_kap_public_company_map", x => x.instrument_id);
                    table.CheckConstraint("ck_kap_public_company_map_oid_shape", "kap_public_oid ~ '^[0-9a-f]{32}$'");
                    table.CheckConstraint("ck_kap_public_company_map_vyk_evidence", "(vyk_company_id IS NULL AND vyk_company_id_source IS NULL AND vyk_company_id_as_of IS NULL) OR (vyk_company_id IS NOT NULL AND vyk_company_id_source IS NOT NULL AND vyk_company_id_as_of IS NOT NULL)");
                    table.CheckConstraint("ck_kap_public_company_map_vyk_shape", "vyk_company_id IS NULL OR vyk_company_id ~ '^[0-9]{1,12}$'");
                    table.ForeignKey(
                        name: "fk_kap_public_company_map_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "kap_public_company_map");
        }
    }
}
