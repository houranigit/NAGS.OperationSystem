using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Operations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Operations_ReturnToRampOccurrences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReturnToRampId",
                schema: "operations",
                table: "work_order_tasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReturnToRampId",
                schema: "operations",
                table: "work_order_service_lines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "work_order_return_to_ramps",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ToUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RecordedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_order_return_to_ramps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_work_order_return_to_ramps_work_orders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalSchema: "operations",
                        principalTable: "work_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // The legacy schema recorded only one Boolean on each activity. It did not contain an
            // occurrence id, event window, description, or mutation linkage, so multiple historical
            // occurrences cannot be reconstructed safely. Preserve every flagged row under one
            // clearly labelled record per work order instead of inventing group boundaries.
            migrationBuilder.Sql(
                """
                ;WITH LegacyActivities AS
                (
                    SELECT WorkOrderId, FromUtc, ToUtc
                    FROM operations.work_order_service_lines
                    WHERE IsReturnToRamp = CAST(1 AS bit)
                    UNION ALL
                    SELECT WorkOrderId, FromUtc, ToUtc
                    FROM operations.work_order_tasks
                    WHERE IsReturnToRamp = CAST(1 AS bit)
                ), LegacyWindows AS
                (
                    SELECT WorkOrderId, MIN(FromUtc) AS FromUtc, MAX(ToUtc) AS ToUtc
                    FROM LegacyActivities
                    GROUP BY WorkOrderId
                )
                INSERT INTO operations.work_order_return_to_ramps
                    (Id, WorkOrderId, FromUtc, ToUtc, Description, RecordedByUserId, CreatedAtUtc)
                SELECT
                    NEWID(),
                    window.WorkOrderId,
                    window.FromUtc,
                    window.ToUtc,
                    N'Migrated legacy RTR activities; original occurrence grouping unavailable.',
                    workOrder.OwnerUserId,
                    COALESCE(workOrder.UpdatedAtUtc, workOrder.CreatedAtUtc)
                FROM LegacyWindows AS window
                INNER JOIN operations.work_orders AS workOrder ON workOrder.Id = window.WorkOrderId;

                UPDATE serviceLine
                SET ReturnToRampId = occurrence.Id
                FROM operations.work_order_service_lines AS serviceLine
                INNER JOIN operations.work_order_return_to_ramps AS occurrence
                    ON occurrence.WorkOrderId = serviceLine.WorkOrderId
                WHERE serviceLine.IsReturnToRamp = CAST(1 AS bit);

                UPDATE task
                SET ReturnToRampId = occurrence.Id
                FROM operations.work_order_tasks AS task
                INNER JOIN operations.work_order_return_to_ramps AS occurrence
                    ON occurrence.WorkOrderId = task.WorkOrderId
                WHERE task.IsReturnToRamp = CAST(1 AS bit);
                """);

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

            migrationBuilder.CreateIndex(
                name: "IX_work_order_return_to_ramps_WorkOrderId",
                schema: "operations",
                table: "work_order_return_to_ramps",
                column: "WorkOrderId");

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

            migrationBuilder.DropColumn(
                name: "IsReturnToRamp",
                schema: "operations",
                table: "work_order_tasks");

            migrationBuilder.DropColumn(
                name: "IsReturnToRamp",
                schema: "operations",
                table: "work_order_service_lines");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsReturnToRamp",
                schema: "operations",
                table: "work_order_tasks",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsReturnToRamp",
                schema: "operations",
                table: "work_order_service_lines",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                """
                UPDATE operations.work_order_tasks
                SET IsReturnToRamp = CAST(1 AS bit)
                WHERE ReturnToRampId IS NOT NULL;

                UPDATE operations.work_order_service_lines
                SET IsReturnToRamp = CAST(1 AS bit)
                WHERE ReturnToRampId IS NOT NULL;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_work_order_service_lines_work_order_return_to_ramps_ReturnToRampId",
                schema: "operations",
                table: "work_order_service_lines");

            migrationBuilder.DropForeignKey(
                name: "FK_work_order_tasks_work_order_return_to_ramps_ReturnToRampId",
                schema: "operations",
                table: "work_order_tasks");

            migrationBuilder.DropTable(
                name: "work_order_return_to_ramps",
                schema: "operations");

            migrationBuilder.DropIndex(
                name: "IX_work_order_tasks_ReturnToRampId",
                schema: "operations",
                table: "work_order_tasks");

            migrationBuilder.DropIndex(
                name: "IX_work_order_service_lines_ReturnToRampId",
                schema: "operations",
                table: "work_order_service_lines");

            migrationBuilder.DropColumn(
                name: "ReturnToRampId",
                schema: "operations",
                table: "work_order_tasks");

            migrationBuilder.DropColumn(
                name: "ReturnToRampId",
                schema: "operations",
                table: "work_order_service_lines");

        }
    }
}
