using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Operations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Operations_WorkOrderReturnToRampProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsReturnToRamp",
                schema: "operations",
                table: "work_order_tasks",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsReturnToRamp",
                schema: "operations",
                table: "work_order_service_lines",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsReturnToRamp",
                schema: "operations",
                table: "work_order_tasks");

            migrationBuilder.DropColumn(
                name: "IsReturnToRamp",
                schema: "operations",
                table: "work_order_service_lines");
        }
    }
}
