using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Operations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Operations_WorkOrderReferenceNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WorkPackageNumber",
                schema: "operations",
                table: "work_orders",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerReferenceNumber",
                schema: "operations",
                table: "work_order_tasks",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerReferenceNumber",
                schema: "operations",
                table: "work_order_service_lines",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WorkPackageNumber",
                schema: "operations",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "CustomerReferenceNumber",
                schema: "operations",
                table: "work_order_tasks");

            migrationBuilder.DropColumn(
                name: "CustomerReferenceNumber",
                schema: "operations",
                table: "work_order_service_lines");
        }
    }
}
