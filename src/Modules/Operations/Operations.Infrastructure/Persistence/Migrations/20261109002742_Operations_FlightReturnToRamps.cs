using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Operations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Operations_FlightReturnToRamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LastReturnToRampSequence",
                schema: "operations",
                table: "flights",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ReturnToRampId",
                schema: "operations",
                table: "flight_tasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReturnToRampId",
                schema: "operations",
                table: "flight_services",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "flight_return_to_ramps",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlightId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    RtrArrivalUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ArrivalReceivedByStaffMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ArrivalReceivedByStaffFullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ArrivalReceivedByStaffEmployeeId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ArrivalRemarks = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RtrDepartureUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CustomerSignatureFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    CustomerSignatureStorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CustomerSignatureContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CustomerSignatureSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    CustomerSignedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DepartureHandledByStaffMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DepartureHandledByStaffFullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DepartureHandledByStaffEmployeeId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DepartureRemarks = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flight_return_to_ramps", x => x.Id);
                    table.UniqueConstraint("AK_flight_return_to_ramps_Id_FlightId", x => new { x.Id, x.FlightId });
                    table.CheckConstraint("CK_flight_return_to_ramps_Sequence", "[Sequence] > 0");
                    table.CheckConstraint("CK_flight_return_to_ramps_Window", "[RtrDepartureUtc] IS NULL OR [RtrDepartureUtc] >= [RtrArrivalUtc]");
                    table.ForeignKey(
                        name: "FK_flight_return_to_ramps_flights_FlightId",
                        column: x => x.FlightId,
                        principalSchema: "operations",
                        principalTable: "flights",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_flight_tasks_ReturnToRampId_FlightId",
                schema: "operations",
                table: "flight_tasks",
                columns: new[] { "ReturnToRampId", "FlightId" });

            migrationBuilder.CreateIndex(
                name: "IX_flight_services_ReturnToRampId_FlightId",
                schema: "operations",
                table: "flight_services",
                columns: new[] { "ReturnToRampId", "FlightId" });

            migrationBuilder.CreateIndex(
                name: "IX_flight_return_to_ramps_FlightId_RtrDepartureUtc",
                schema: "operations",
                table: "flight_return_to_ramps",
                columns: new[] { "FlightId", "RtrDepartureUtc" },
                unique: true,
                filter: "[RtrDepartureUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_flight_return_to_ramps_FlightId_Sequence",
                schema: "operations",
                table: "flight_return_to_ramps",
                columns: new[] { "FlightId", "Sequence" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_flight_services_flight_return_to_ramps_ReturnToRampId_FlightId",
                schema: "operations",
                table: "flight_services",
                columns: new[] { "ReturnToRampId", "FlightId" },
                principalSchema: "operations",
                principalTable: "flight_return_to_ramps",
                principalColumns: new[] { "Id", "FlightId" });

            migrationBuilder.AddForeignKey(
                name: "FK_flight_tasks_flight_return_to_ramps_ReturnToRampId_FlightId",
                schema: "operations",
                table: "flight_tasks",
                columns: new[] { "ReturnToRampId", "FlightId" },
                principalSchema: "operations",
                principalTable: "flight_return_to_ramps",
                principalColumns: new[] { "Id", "FlightId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_flight_services_flight_return_to_ramps_ReturnToRampId_FlightId",
                schema: "operations",
                table: "flight_services");

            migrationBuilder.DropForeignKey(
                name: "FK_flight_tasks_flight_return_to_ramps_ReturnToRampId_FlightId",
                schema: "operations",
                table: "flight_tasks");

            migrationBuilder.DropTable(
                name: "flight_return_to_ramps",
                schema: "operations");

            migrationBuilder.DropIndex(
                name: "IX_flight_tasks_ReturnToRampId_FlightId",
                schema: "operations",
                table: "flight_tasks");

            migrationBuilder.DropIndex(
                name: "IX_flight_services_ReturnToRampId_FlightId",
                schema: "operations",
                table: "flight_services");

            migrationBuilder.DropColumn(
                name: "LastReturnToRampSequence",
                schema: "operations",
                table: "flights");

            migrationBuilder.DropColumn(
                name: "ReturnToRampId",
                schema: "operations",
                table: "flight_tasks");

            migrationBuilder.DropColumn(
                name: "ReturnToRampId",
                schema: "operations",
                table: "flight_services");
        }
    }
}
