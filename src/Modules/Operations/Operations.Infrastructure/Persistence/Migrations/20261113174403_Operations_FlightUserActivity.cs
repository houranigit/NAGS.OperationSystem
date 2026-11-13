using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Operations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Operations_FlightUserActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "flight_user_activities",
                schema: "operations",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlightId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastInteractionAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UsedSearch = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flight_user_activities", x => new { x.UserId, x.FlightId });
                    table.ForeignKey(
                        name: "FK_flight_user_activities_flights_FlightId",
                        column: x => x.FlightId,
                        principalSchema: "operations",
                        principalTable: "flights",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_flight_user_activities_FlightId",
                schema: "operations",
                table: "flight_user_activities",
                column: "FlightId");

            migrationBuilder.CreateIndex(
                name: "IX_flight_user_activities_UserId_LastInteractionAtUtc_FlightId",
                schema: "operations",
                table: "flight_user_activities",
                columns: new[] { "UserId", "LastInteractionAtUtc", "FlightId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "flight_user_activities",
                schema: "operations");
        }
    }
}
