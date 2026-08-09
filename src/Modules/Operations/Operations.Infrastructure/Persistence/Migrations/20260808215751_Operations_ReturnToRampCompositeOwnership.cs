using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Operations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Operations_ReturnToRampCompositeOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_work_order_service_lines_work_order_return_to_ramps_ReturnToRampId",
                schema: "operations",
                table: "work_order_service_lines");

            migrationBuilder.DropForeignKey(
                name: "FK_work_order_tasks_work_order_return_to_ramps_ReturnToRampId",
                schema: "operations",
                table: "work_order_tasks");

            migrationBuilder.DropIndex(
                name: "IX_work_order_tasks_ReturnToRampId",
                schema: "operations",
                table: "work_order_tasks");

            migrationBuilder.DropIndex(
                name: "IX_work_order_service_lines_ReturnToRampId",
                schema: "operations",
                table: "work_order_service_lines");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_work_order_return_to_ramps_Id_WorkOrderId",
                schema: "operations",
                table: "work_order_return_to_ramps",
                columns: new[] { "Id", "WorkOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_work_order_tasks_ReturnToRampId_WorkOrderId",
                schema: "operations",
                table: "work_order_tasks",
                columns: new[] { "ReturnToRampId", "WorkOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_work_order_service_lines_ReturnToRampId_WorkOrderId",
                schema: "operations",
                table: "work_order_service_lines",
                columns: new[] { "ReturnToRampId", "WorkOrderId" });

            migrationBuilder.AddForeignKey(
                name: "FK_work_order_service_lines_work_order_return_to_ramps_ReturnToRampId_WorkOrderId",
                schema: "operations",
                table: "work_order_service_lines",
                columns: new[] { "ReturnToRampId", "WorkOrderId" },
                principalSchema: "operations",
                principalTable: "work_order_return_to_ramps",
                principalColumns: new[] { "Id", "WorkOrderId" });

            migrationBuilder.AddForeignKey(
                name: "FK_work_order_tasks_work_order_return_to_ramps_ReturnToRampId_WorkOrderId",
                schema: "operations",
                table: "work_order_tasks",
                columns: new[] { "ReturnToRampId", "WorkOrderId" },
                principalSchema: "operations",
                principalTable: "work_order_return_to_ramps",
                principalColumns: new[] { "Id", "WorkOrderId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_work_order_service_lines_work_order_return_to_ramps_ReturnToRampId_WorkOrderId",
                schema: "operations",
                table: "work_order_service_lines");

            migrationBuilder.DropForeignKey(
                name: "FK_work_order_tasks_work_order_return_to_ramps_ReturnToRampId_WorkOrderId",
                schema: "operations",
                table: "work_order_tasks");

            migrationBuilder.DropIndex(
                name: "IX_work_order_tasks_ReturnToRampId_WorkOrderId",
                schema: "operations",
                table: "work_order_tasks");

            migrationBuilder.DropIndex(
                name: "IX_work_order_service_lines_ReturnToRampId_WorkOrderId",
                schema: "operations",
                table: "work_order_service_lines");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_work_order_return_to_ramps_Id_WorkOrderId",
                schema: "operations",
                table: "work_order_return_to_ramps");

            migrationBuilder.CreateIndex(
                name: "IX_work_order_tasks_ReturnToRampId",
                schema: "operations",
                table: "work_order_tasks",
                column: "ReturnToRampId");

            migrationBuilder.CreateIndex(
                name: "IX_work_order_service_lines_ReturnToRampId",
                schema: "operations",
                table: "work_order_service_lines",
                column: "ReturnToRampId");

            migrationBuilder.AddForeignKey(
                name: "FK_work_order_service_lines_work_order_return_to_ramps_ReturnToRampId",
                schema: "operations",
                table: "work_order_service_lines",
                column: "ReturnToRampId",
                principalSchema: "operations",
                principalTable: "work_order_return_to_ramps",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_work_order_tasks_work_order_return_to_ramps_ReturnToRampId",
                schema: "operations",
                table: "work_order_tasks",
                column: "ReturnToRampId",
                principalSchema: "operations",
                principalTable: "work_order_return_to_ramps",
                principalColumn: "Id");
        }
    }
}
