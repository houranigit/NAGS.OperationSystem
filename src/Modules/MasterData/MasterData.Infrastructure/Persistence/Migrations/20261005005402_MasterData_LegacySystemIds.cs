using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterData.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MasterData_LegacySystemIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LegacySystemId",
                schema: "masterdata",
                table: "tools",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegacySystemId",
                schema: "masterdata",
                table: "stations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegacySystemId",
                schema: "masterdata",
                table: "staff_members",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegacySystemId",
                schema: "masterdata",
                table: "services",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegacySystemId",
                schema: "masterdata",
                table: "operation_types",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegacySystemId",
                schema: "masterdata",
                table: "materials",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegacySystemId",
                schema: "masterdata",
                table: "manpower_types",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegacySystemId",
                schema: "masterdata",
                table: "general_supports",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegacySystemId",
                schema: "masterdata",
                table: "customers",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegacySystemId",
                schema: "masterdata",
                table: "aircraft_types",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LegacySystemId",
                schema: "masterdata",
                table: "tools");

            migrationBuilder.DropColumn(
                name: "LegacySystemId",
                schema: "masterdata",
                table: "stations");

            migrationBuilder.DropColumn(
                name: "LegacySystemId",
                schema: "masterdata",
                table: "staff_members");

            migrationBuilder.DropColumn(
                name: "LegacySystemId",
                schema: "masterdata",
                table: "services");

            migrationBuilder.DropColumn(
                name: "LegacySystemId",
                schema: "masterdata",
                table: "operation_types");

            migrationBuilder.DropColumn(
                name: "LegacySystemId",
                schema: "masterdata",
                table: "materials");

            migrationBuilder.DropColumn(
                name: "LegacySystemId",
                schema: "masterdata",
                table: "manpower_types");

            migrationBuilder.DropColumn(
                name: "LegacySystemId",
                schema: "masterdata",
                table: "general_supports");

            migrationBuilder.DropColumn(
                name: "LegacySystemId",
                schema: "masterdata",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "LegacySystemId",
                schema: "masterdata",
                table: "aircraft_types");
        }
    }
}
