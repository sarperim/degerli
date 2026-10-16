using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Degerli.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountingProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "accounting_profile",
                table: "instruments",
                type: "text",
                nullable: false,
                defaultValue: "industrial");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "accounting_profile",
                table: "instruments");
        }
    }
}
