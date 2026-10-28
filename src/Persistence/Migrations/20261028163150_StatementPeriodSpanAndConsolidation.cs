using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Degerli.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// <b>Additive and forward-only, and it writes no row</b> (<c>02</c> §1.1/§3.1,
    /// TKT-foundation-094). <c>financial_statements</c> gains the three nullable columns that
    /// carry a filing's own declared evidence onto the stored fact: the column header's range
    /// start and duration (<c>period_start_date</c>, <c>duration_months</c> — NULL for an
    /// instant statement such as a balance sheet, and NULL on every pre-migration row, so
    /// nothing is back-filled or inferred) and the filing's <c>Finansal Tablo Niteliği</c>
    /// declaration (<c>consolidated</c> — NULL is honest absence, never a guessed false).
    /// Existing rows keep NULL on all three: the serving surface degrades to today's labels and
    /// suppresses the badges rather than fabricating a span or a Konsolide/Solo claim.
    /// </remarks>
    public partial class StatementPeriodSpanAndConsolidation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "consolidated",
                table: "financial_statements",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "duration_months",
                table: "financial_statements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "period_start_date",
                table: "financial_statements",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "consolidated",
                table: "financial_statements");

            migrationBuilder.DropColumn(
                name: "duration_months",
                table: "financial_statements");

            migrationBuilder.DropColumn(
                name: "period_start_date",
                table: "financial_statements");
        }
    }
}
