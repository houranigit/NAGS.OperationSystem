using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterData.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MasterData_ResourceCalculationTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The default values are also the deterministic backfill for every row that predates
            // calculation setup: tools were historically time-based, while materials and general
            // support were recorded as consumed quantities.
            migrationBuilder.AddColumn<int>(
                name: "CalculationType",
                schema: "masterdata",
                table: "tools",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CalculationType",
                schema: "masterdata",
                table: "materials",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CalculationType",
                schema: "masterdata",
                table: "general_supports",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "CK_tools_CalculationType",
                schema: "masterdata",
                table: "tools",
                sql: "[CalculationType] IN (0, 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_materials_CalculationType",
                schema: "masterdata",
                table: "materials",
                sql: "[CalculationType] IN (0, 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_general_supports_CalculationType",
                schema: "masterdata",
                table: "general_supports",
                sql: "[CalculationType] IN (0, 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_tools_CalculationType",
                schema: "masterdata",
                table: "tools");

            migrationBuilder.DropCheckConstraint(
                name: "CK_materials_CalculationType",
                schema: "masterdata",
                table: "materials");

            migrationBuilder.DropCheckConstraint(
                name: "CK_general_supports_CalculationType",
                schema: "masterdata",
                table: "general_supports");

            migrationBuilder.DropColumn(
                name: "CalculationType",
                schema: "masterdata",
                table: "tools");

            migrationBuilder.DropColumn(
                name: "CalculationType",
                schema: "masterdata",
                table: "materials");

            migrationBuilder.DropColumn(
                name: "CalculationType",
                schema: "masterdata",
                table: "general_supports");
        }
    }
}
