using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Degerli.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ShareCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "share_counts",
                columns: table => new
                {
                    instrument_id = table.Column<long>(type: "bigint", nullable: false),
                    shares_outstanding = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    source_ref = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_share_counts", x => x.instrument_id);
                    table.ForeignKey(
                        name: "fk_share_counts_instruments_instrument_id",
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
                name: "share_counts");
        }
    }
}
