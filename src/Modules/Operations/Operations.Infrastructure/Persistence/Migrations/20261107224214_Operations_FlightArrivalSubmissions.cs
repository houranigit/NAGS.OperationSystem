using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Operations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Operations_FlightArrivalSubmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "flight_arrival_submissions",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlightId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmittedByStaffMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmittedByStaffFullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SubmittedByStaffEmployeeId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CustomerWorkReferenceNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ActualArrivalUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AircraftTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AircraftManufacturer = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AircraftModel = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AircraftTailNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReceivedByStaffMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceivedByStaffFullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ReceivedByStaffEmployeeId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Remarks = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsAccepted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flight_arrival_submissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_flight_arrival_submissions_flights_FlightId",
                        column: x => x.FlightId,
                        principalSchema: "operations",
                        principalTable: "flights",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_flight_arrival_submissions_FlightId",
                schema: "operations",
                table: "flight_arrival_submissions",
                column: "FlightId",
                unique: true,
                filter: "[IsAccepted] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_flight_arrival_submissions_FlightId_SubmittedByStaffMemberId",
                schema: "operations",
                table: "flight_arrival_submissions",
                columns: new[] { "FlightId", "SubmittedByStaffMemberId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "flight_arrival_submissions",
                schema: "operations");
        }
    }
}
