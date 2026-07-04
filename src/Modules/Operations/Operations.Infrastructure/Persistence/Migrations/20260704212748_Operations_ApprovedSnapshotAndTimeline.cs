using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Operations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Operations_ApprovedSnapshotAndTimeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OwnerEmployeeId",
                schema: "operations",
                table: "work_orders",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerFullName",
                schema: "operations",
                table: "work_orders",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerSnapshotStaffMemberId",
                schema: "operations",
                table: "work_orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerStaffMemberId",
                schema: "operations",
                table: "work_orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedActualAircraftManufacturer",
                schema: "operations",
                table: "flights",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedActualAircraftModel",
                schema: "operations",
                table: "flights",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedActualAircraftTypeId",
                schema: "operations",
                table: "flights",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedActualArrivalUtc",
                schema: "operations",
                table: "flights",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedActualDepartureUtc",
                schema: "operations",
                table: "flights",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedActualFlightNumber",
                schema: "operations",
                table: "flights",
                type: "nvarchar(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedAircraftTailNumber",
                schema: "operations",
                table: "flights",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAtUtc",
                schema: "operations",
                table: "flights",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedByUserId",
                schema: "operations",
                table: "flights",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedCanceledAtUtc",
                schema: "operations",
                table: "flights",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedCanceledByUserId",
                schema: "operations",
                table: "flights",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedCancellationReason",
                schema: "operations",
                table: "flights",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedCustomerSignatureReference",
                schema: "operations",
                table: "flights",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedRemarks",
                schema: "operations",
                table: "flights",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedWorkOrderId",
                schema: "operations",
                table: "flights",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedWorkOrderNumber",
                schema: "operations",
                table: "flights",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ApprovedWorkOrderType",
                schema: "operations",
                table: "flights",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "flight_timeline_entries",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlightId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WorkOrderNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Details = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flight_timeline_entries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_work_orders_OwnerStaffMemberId",
                schema: "operations",
                table: "work_orders",
                column: "OwnerStaffMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_flight_timeline_entries_FlightId_OccurredAtUtc",
                schema: "operations",
                table: "flight_timeline_entries",
                columns: new[] { "FlightId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "flight_timeline_entries",
                schema: "operations");

            migrationBuilder.DropIndex(
                name: "IX_work_orders_OwnerStaffMemberId",
                schema: "operations",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "OwnerEmployeeId",
                schema: "operations",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "OwnerFullName",
                schema: "operations",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "OwnerSnapshotStaffMemberId",
                schema: "operations",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "OwnerStaffMemberId",
                schema: "operations",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "ApprovedActualAircraftManufacturer",
                schema: "operations",
                table: "flights");

            migrationBuilder.DropColumn(
                name: "ApprovedActualAircraftModel",
                schema: "operations",
                table: "flights");

            migrationBuilder.DropColumn(
                name: "ApprovedActualAircraftTypeId",
                schema: "operations",
                table: "flights");

            migrationBuilder.DropColumn(
                name: "ApprovedActualArrivalUtc",
                schema: "operations",
                table: "flights");

            migrationBuilder.DropColumn(
                name: "ApprovedActualDepartureUtc",
                schema: "operations",
                table: "flights");

            migrationBuilder.DropColumn(
                name: "ApprovedActualFlightNumber",
                schema: "operations",
                table: "flights");

            migrationBuilder.DropColumn(
                name: "ApprovedAircraftTailNumber",
                schema: "operations",
                table: "flights");

            migrationBuilder.DropColumn(
                name: "ApprovedAtUtc",
                schema: "operations",
                table: "flights");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                schema: "operations",
                table: "flights");

            migrationBuilder.DropColumn(
                name: "ApprovedCanceledAtUtc",
                schema: "operations",
                table: "flights");

            migrationBuilder.DropColumn(
                name: "ApprovedCanceledByUserId",
                schema: "operations",
                table: "flights");

            migrationBuilder.DropColumn(
                name: "ApprovedCancellationReason",
                schema: "operations",
                table: "flights");

            migrationBuilder.DropColumn(
                name: "ApprovedCustomerSignatureReference",
                schema: "operations",
                table: "flights");

            migrationBuilder.DropColumn(
                name: "ApprovedRemarks",
                schema: "operations",
                table: "flights");

            migrationBuilder.DropColumn(
                name: "ApprovedWorkOrderId",
                schema: "operations",
                table: "flights");

            migrationBuilder.DropColumn(
                name: "ApprovedWorkOrderNumber",
                schema: "operations",
                table: "flights");

            migrationBuilder.DropColumn(
                name: "ApprovedWorkOrderType",
                schema: "operations",
                table: "flights");
        }
    }
}
