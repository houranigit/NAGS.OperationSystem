using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Operations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Operations_RestoreFlightNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "flight_reminder_schedules",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlightId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeadTimeMinutes = table.Column<int>(type: "int", nullable: false),
                    ScheduledArrivalUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DueAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DispatchedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SkippedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SkipReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flight_reminder_schedules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_flight_reminder_schedules_FlightId_StaffMemberId_LeadTimeMinutes_ScheduledArrivalUtc",
                schema: "operations",
                table: "flight_reminder_schedules",
                columns: new[] { "FlightId", "StaffMemberId", "LeadTimeMinutes", "ScheduledArrivalUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_flight_reminder_schedules_State_DispatchedAtUtc",
                schema: "operations",
                table: "flight_reminder_schedules",
                columns: new[] { "State", "DispatchedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_flight_reminder_schedules_State_DueAtUtc",
                schema: "operations",
                table: "flight_reminder_schedules",
                columns: new[] { "State", "DueAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_flight_reminder_schedules_State_SkippedAtUtc",
                schema: "operations",
                table: "flight_reminder_schedules",
                columns: new[] { "State", "SkippedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "flight_reminder_schedules",
                schema: "operations");
        }
    }
}
