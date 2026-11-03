using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Operations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Operations_FlightSchedulingBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Keep the retained invitation contract, platform audit events, and processed envelopes.
            // The other retired Operations contracts can no longer be rehydrated.
            migrationBuilder.Sql("""
                DELETE FROM [operations].[outbox_messages]
                WHERE [ProcessedOnUtc] IS NULL AND [Type] LIKE N'Operations.Contracts.%'
                  AND [Type] NOT LIKE N'Operations.Contracts.FlightEmployeeAssigned,%';
                """);

            migrationBuilder.DropTable(
                name: "flight_reminder_schedules",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "flight_timeline_entries",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "Operations_MobileMutations",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "work_order_service_line_attachments",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "work_order_service_line_performers",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "work_order_task_attachments",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "work_order_task_employees",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "work_order_task_general_supports",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "work_order_task_materials",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "work_order_task_tools",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "work_order_timeline_entries",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "work_order_service_lines",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "work_order_tasks",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "work_order_return_to_ramps",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "work_orders",
                schema: "operations");

            migrationBuilder.DropIndex(
                name: "IX_flights_OriginalFlightNumber",
                schema: "operations",
                table: "flights");

            migrationBuilder.DropIndex(
                name: "IX_flights_Status",
                schema: "operations",
                table: "flights");

            // The reminder migration added this index outside the EF model snapshot.
            migrationBuilder.DropIndex(
                name: "IX_flights_Status_ScheduledArrivalUtc",
                schema: "operations",
                table: "flights");

            migrationBuilder.DropColumn(
                name: "ContractId",
                schema: "operations",
                table: "flights");

            migrationBuilder.DropColumn(
                name: "ContractNumber",
                schema: "operations",
                table: "flights");

            migrationBuilder.DropColumn(
                name: "MergedIntoFlightId",
                schema: "operations",
                table: "flights");

            migrationBuilder.DropColumn(
                name: "OriginalFlightNumber",
                schema: "operations",
                table: "flights");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "operations",
                table: "flights");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "The flight scheduling reset removes operational history. Restore the database backup to roll back this migration.");
        }
    }
}
