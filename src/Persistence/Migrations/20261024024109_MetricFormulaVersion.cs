using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Degerli.Persistence.Migrations
{
    /// <summary>
    /// Definition-version provenance for the derived-fact lanes (<c>01</c> §10.9.13,
    /// TKT-foundation-070; <c>02</c> §3.1/§8): <c>formula_version</c> on
    /// <c>derived_metrics</c> and <c>sector_metric_medians</c>, the definition that produced
    /// each row.
    /// <para>
    /// <b>Additive and forward-only, with an honest (empty) backfill.</b> Both columns are
    /// nullable and this migration writes no row: every row that exists at deploy time was
    /// produced by the then-current definition — which, in production, is the definition this
    /// release ships (<c>CanonicalMetrics.Version</c> = 1) — but there is no per-row record of
    /// which definition wrote each value, and inventing one would be a fabricated provenance
    /// claim. <c>NULL</c> therefore means exactly "produced before versioning existed", which
    /// is true. The next recompute of a day rewrites those rows (values included, unchanged)
    /// and stamps the current version, so the gap closes from evidence, never from a guess.
    /// </para>
    /// </summary>
    public partial class MetricFormulaVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "formula_version",
                table: "sector_metric_medians",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "formula_version",
                table: "derived_metrics",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_sector_metric_medians_formula_version",
                table: "sector_metric_medians",
                sql: "formula_version IS NULL OR formula_version >= 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_derived_metrics_formula_version",
                table: "derived_metrics",
                sql: "formula_version IS NULL OR formula_version >= 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_sector_metric_medians_formula_version",
                table: "sector_metric_medians");

            migrationBuilder.DropCheckConstraint(
                name: "ck_derived_metrics_formula_version",
                table: "derived_metrics");

            migrationBuilder.DropColumn(
                name: "formula_version",
                table: "sector_metric_medians");

            migrationBuilder.DropColumn(
                name: "formula_version",
                table: "derived_metrics");
        }
    }
}
