using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Degerli.Persistence.Migrations
{
    /// <summary>
    /// One-time forward migration of <c>financial_statements.version</c> from the old
    /// <c>text</c> declaration (<c>as_reported</c>/<c>restated</c>) to the integer
    /// restatement sequence (<c>0</c> = as-reported, <c>1</c> = first restatement) — 02
    /// §1 principle 5, §3.1, §8. Existing values are remapped in place; no fact row is
    /// invented or dropped. Wiring the payload declaration to the sequence is the store's
    /// responsibility from here on (BR-MDF-011).
    /// </summary>
    public partial class StatementVersionSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Remap the existing declarations, then change the column type in one
            // statement so the USING expression covers any row the UPDATE did not reach.
            migrationBuilder.Sql("""
                ALTER TABLE financial_statements
                    ALTER COLUMN version TYPE integer
                    USING (CASE version
                        WHEN 'as_reported' THEN 0
                        WHEN 'restated' THEN 1
                        ELSE version::integer
                    END);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE financial_statements
                    ALTER COLUMN version TYPE text
                    USING (CASE version
                        WHEN 0 THEN 'as_reported'
                        ELSE 'restated'
                    END);
                """);
        }
    }
}
