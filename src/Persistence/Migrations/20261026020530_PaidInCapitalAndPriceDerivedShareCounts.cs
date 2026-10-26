using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Degerli.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PaidInCapitalAndPriceDerivedShareCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "shares_outstanding",
                table: "share_counts",
                newName: "paid_in_capital");

            migrationBuilder.CreateTable(
                name: "price_derived_share_counts",
                columns: table => new
                {
                    instrument_id = table.Column<long>(type: "bigint", nullable: false),
                    shares_outstanding = table.Column<long>(type: "bigint", nullable: false),
                    market_cap_raw = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    close_raw = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    price_date = table.Column<DateOnly>(type: "date", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    source_ref = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_price_derived_share_counts", x => x.instrument_id);
                    table.ForeignKey(
                        name: "fk_price_derived_share_counts_instruments_instrument_id",
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
                name: "price_derived_share_counts");

            migrationBuilder.RenameColumn(
                name: "paid_in_capital",
                table: "share_counts",
                newName: "shares_outstanding");
        }
    }
}
