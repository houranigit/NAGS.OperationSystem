using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Operations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Operations_WorkOrderTimelineAndSubmittedLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "work_order_timeline_entries",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlightId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    WorkOrderNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Details = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_order_timeline_entries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_work_order_timeline_entries_WorkOrderId_OccurredAtUtc",
                schema: "operations",
                table: "work_order_timeline_entries",
                columns: new[] { "WorkOrderId", "OccurredAtUtc" });

            migrationBuilder.Sql("""
                INSERT INTO operations.work_order_timeline_entries
                    (Id, WorkOrderId, FlightId, EventType, OccurredAtUtc, ActorUserId, ActorName, WorkOrderNumber, Details)
                SELECT NEWID(), Id, FlightId, 0, CreatedAtUtc, CreatedByUserId, NULL, WorkOrderNumber, NULL
                FROM operations.work_orders
                """);

            migrationBuilder.Sql("""
                INSERT INTO operations.work_order_timeline_entries
                    (Id, WorkOrderId, FlightId, EventType, OccurredAtUtc, ActorUserId, ActorName, WorkOrderNumber, Details)
                SELECT NEWID(), Id, FlightId, 2, ApprovedAtUtc, COALESCE(ApprovedByUserId, CreatedByUserId), NULL, WorkOrderNumber, NULL
                FROM operations.work_orders
                WHERE Status = 2 AND ApprovedAtUtc IS NOT NULL
                """);

            migrationBuilder.Sql("""
                INSERT INTO operations.work_order_timeline_entries
                    (Id, WorkOrderId, FlightId, EventType, OccurredAtUtc, ActorUserId, ActorName, WorkOrderNumber, Details)
                SELECT NEWID(), Id, FlightId, 3, COALESCE(UpdatedAtUtc, CreatedAtUtc), CreatedByUserId, NULL, WorkOrderNumber, NULL
                FROM operations.work_orders
                WHERE Status IN (3, 4)
                """);

            migrationBuilder.Sql("""
                INSERT INTO operations.work_order_timeline_entries
                    (Id, WorkOrderId, FlightId, EventType, OccurredAtUtc, ActorUserId, ActorName, WorkOrderNumber, Details)
                SELECT NEWID(), Id, FlightId, 4, COALESCE(UpdatedAtUtc, CreatedAtUtc), CreatedByUserId, NULL, WorkOrderNumber, 'Merged into ' + CONVERT(nvarchar(36), SupersededByWorkOrderId) + '.'
                FROM operations.work_orders
                WHERE Status = 5 OR SupersededByWorkOrderId IS NOT NULL
                """);

            migrationBuilder.Sql("""
                UPDATE operations.work_orders
                SET Status = 1
                WHERE Status IN (0, 3, 4, 5)
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE operations.work_orders
                SET Status = 5
                WHERE SupersededByWorkOrderId IS NOT NULL AND Status = 1
                """);

            migrationBuilder.DropTable(
                name: "work_order_timeline_entries",
                schema: "operations");
        }
    }
}
