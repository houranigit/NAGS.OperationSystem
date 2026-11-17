using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Operations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Operations_FlightShares : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "flight_shares",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlightId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorStaffMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorFullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AuthorEmployeeId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flight_shares", x => x.Id);
                    table.ForeignKey(
                        name: "FK_flight_shares_flights_FlightId",
                        column: x => x.FlightId,
                        principalSchema: "operations",
                        principalTable: "flights",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "flight_share_recipients",
                schema: "operations",
                columns: table => new
                {
                    ShareId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EmployeeId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flight_share_recipients", x => new { x.ShareId, x.StaffMemberId });
                    table.ForeignKey(
                        name: "FK_flight_share_recipients_flight_shares_ShareId",
                        column: x => x.ShareId,
                        principalSchema: "operations",
                        principalTable: "flight_shares",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_flight_share_recipients_StaffMemberId_ShareId",
                schema: "operations",
                table: "flight_share_recipients",
                columns: new[] { "StaffMemberId", "ShareId" });

            migrationBuilder.CreateIndex(
                name: "IX_flight_shares_AuthorUserId_StationId_CreatedAtUtc_Id",
                schema: "operations",
                table: "flight_shares",
                columns: new[] { "AuthorUserId", "StationId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_flight_shares_FlightId",
                schema: "operations",
                table: "flight_shares",
                column: "FlightId");

            migrationBuilder.CreateIndex(
                name: "IX_flight_shares_StationId_CreatedAtUtc_Id",
                schema: "operations",
                table: "flight_shares",
                columns: new[] { "StationId", "CreatedAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "flight_share_recipients",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "flight_shares",
                schema: "operations");
        }
    }
}
