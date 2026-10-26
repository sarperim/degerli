using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Degerli.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// <b>Forward-only, and it rewrites no fact.</b> The statement store's natural key gains the
    /// source (<c>01</c> v1.5.57 §10.9.19, TC-MDF-111; TKT-foundation-082's recorded blocker): two
    /// sources — the VYK lane's <c>kap</c> and the public-site lane's <c>kap-public</c> — may each
    /// hold a fact for the same period, and <c>version</c> stays the restatement sequence
    /// <em>within one source's own filing series</em> (<c>02</c> §3.1). Every row already written
    /// keeps its identity, its provenance and its version; the index only stops the store treating
    /// one source's fact as another's twin.
    /// </remarks>
    public partial class StatementSourceInNaturalKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_financial_statements_instrument_id_period_type_period_end_d",
                table: "financial_statements");

            migrationBuilder.CreateIndex(
                name: "ix_financial_statements_instrument_id_period_type_period_end_d",
                table: "financial_statements",
                columns: new[] { "instrument_id", "period_type", "period_end_date", "statement_type", "source_ref", "version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_financial_statements_instrument_id_period_type_period_end_d",
                table: "financial_statements");

            migrationBuilder.CreateIndex(
                name: "ix_financial_statements_instrument_id_period_type_period_end_d",
                table: "financial_statements",
                columns: new[] { "instrument_id", "period_type", "period_end_date", "statement_type", "version" },
                unique: true);
        }
    }
}
