using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Operations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Operations_RepeatedPerformerPeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_flight_task_performers_FlightTaskId_StaffMemberId",
                schema: "operations",
                table: "flight_task_performers");

            migrationBuilder.DropIndex(
                name: "IX_flight_service_performers_FlightServiceId_StaffMemberId",
                schema: "operations",
                table: "flight_service_performers");

            migrationBuilder.CreateIndex(
                name: "IX_flight_task_performers_FlightTaskId_StaffMemberId",
                schema: "operations",
                table: "flight_task_performers",
                columns: new[] { "FlightTaskId", "StaffMemberId" });

            migrationBuilder.CreateIndex(
                name: "IX_flight_service_performers_FlightServiceId_StaffMemberId",
                schema: "operations",
                table: "flight_service_performers",
                columns: new[] { "FlightServiceId", "StaffMemberId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_flight_task_performers_FlightTaskId_StaffMemberId",
                schema: "operations",
                table: "flight_task_performers");

            migrationBuilder.DropIndex(
                name: "IX_flight_service_performers_FlightServiceId_StaffMemberId",
                schema: "operations",
                table: "flight_service_performers");

            migrationBuilder.CreateIndex(
                name: "IX_flight_task_performers_FlightTaskId_StaffMemberId",
                schema: "operations",
                table: "flight_task_performers",
                columns: new[] { "FlightTaskId", "StaffMemberId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_flight_service_performers_FlightServiceId_StaffMemberId",
                schema: "operations",
                table: "flight_service_performers",
                columns: new[] { "FlightServiceId", "StaffMemberId" },
                unique: true);
        }
    }
}
