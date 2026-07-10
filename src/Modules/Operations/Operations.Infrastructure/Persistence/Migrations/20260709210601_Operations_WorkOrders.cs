using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Operations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Operations_WorkOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[operations].[work_order_service_line_employees]', N'U') IS NOT NULL
                    DROP TABLE [operations].[work_order_service_line_employees];

                IF OBJECT_ID(N'[operations].[work_order_task_attachments]', N'U') IS NOT NULL
                    DROP TABLE [operations].[work_order_task_attachments];

                IF OBJECT_ID(N'[operations].[work_order_task_employees]', N'U') IS NOT NULL
                    DROP TABLE [operations].[work_order_task_employees];

                IF OBJECT_ID(N'[operations].[work_order_task_general_supports]', N'U') IS NOT NULL
                    DROP TABLE [operations].[work_order_task_general_supports];

                IF OBJECT_ID(N'[operations].[work_order_task_materials]', N'U') IS NOT NULL
                    DROP TABLE [operations].[work_order_task_materials];

                IF OBJECT_ID(N'[operations].[work_order_task_tools]', N'U') IS NOT NULL
                    DROP TABLE [operations].[work_order_task_tools];

                IF OBJECT_ID(N'[operations].[work_order_timeline_entries]', N'U') IS NOT NULL
                    DROP TABLE [operations].[work_order_timeline_entries];

                IF OBJECT_ID(N'[operations].[work_order_service_lines]', N'U') IS NOT NULL
                    DROP TABLE [operations].[work_order_service_lines];

                IF OBJECT_ID(N'[operations].[work_order_tasks]', N'U') IS NOT NULL
                    DROP TABLE [operations].[work_order_tasks];

                IF OBJECT_ID(N'[operations].[work_orders]', N'U') IS NOT NULL
                    DROP TABLE [operations].[work_orders];

                IF OBJECT_ID(N'[operations].[station_work_order_sequences]', N'U') IS NOT NULL
                    DROP TABLE [operations].[station_work_order_sequences];
                """);

            migrationBuilder.CreateTable(
                name: "work_order_timeline_entries",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Details = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_order_timeline_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "work_orders",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlightId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MergedIntoWorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IsMergeGenerated = table.Column<bool>(type: "bit", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerStaffMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OwnerFullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OwnerEmployeeId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerIataCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    CustomerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StationIataCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    StationName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    OperationTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationTypeName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PlannedFlightNumber = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    ScheduledArrivalUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ScheduledDepartureUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActualFlightNumber = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    AircraftTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AircraftManufacturer = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AircraftModel = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    AircraftTailNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ActualArrivalUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ActualDepartureUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CanceledAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ApprovalSequence = table.Column<int>(type: "int", nullable: true),
                    ApprovalNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_work_orders_flights_FlightId",
                        column: x => x.FlightId,
                        principalSchema: "operations",
                        principalTable: "flights",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "work_order_service_lines",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PerformedByStaffMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PerformedByFullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PerformedByEmployeeId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FromUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ToUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_order_service_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_work_order_service_lines_work_orders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalSchema: "operations",
                        principalTable: "work_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "work_order_tasks",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskType = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    FromUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ToUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_order_tasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_work_order_tasks_work_orders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalSchema: "operations",
                        principalTable: "work_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "work_order_task_attachments",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    StorageReference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_order_task_attachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_work_order_task_attachments_work_order_tasks_WorkOrderTaskId",
                        column: x => x.WorkOrderTaskId,
                        principalSchema: "operations",
                        principalTable: "work_order_tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "work_order_task_employees",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffFullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StaffEmployeeId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_order_task_employees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_work_order_task_employees_work_order_tasks_WorkOrderTaskId",
                        column: x => x.WorkOrderTaskId,
                        principalSchema: "operations",
                        principalTable: "work_order_tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "work_order_task_general_supports",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GeneralSupportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GeneralSupportName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_order_task_general_supports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_work_order_task_general_supports_work_order_tasks_WorkOrderTaskId",
                        column: x => x.WorkOrderTaskId,
                        principalSchema: "operations",
                        principalTable: "work_order_tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "work_order_task_materials",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MaterialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MaterialName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_order_task_materials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_work_order_task_materials_work_order_tasks_WorkOrderTaskId",
                        column: x => x.WorkOrderTaskId,
                        principalSchema: "operations",
                        principalTable: "work_order_tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "work_order_task_tools",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToolId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToolName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_order_task_tools", x => x.Id);
                    table.ForeignKey(
                        name: "FK_work_order_task_tools_work_order_tasks_WorkOrderTaskId",
                        column: x => x.WorkOrderTaskId,
                        principalSchema: "operations",
                        principalTable: "work_order_tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_work_order_service_lines_WorkOrderId",
                schema: "operations",
                table: "work_order_service_lines",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_work_order_task_attachments_WorkOrderTaskId",
                schema: "operations",
                table: "work_order_task_attachments",
                column: "WorkOrderTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_work_order_task_employees_WorkOrderTaskId",
                schema: "operations",
                table: "work_order_task_employees",
                column: "WorkOrderTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_work_order_task_general_supports_WorkOrderTaskId",
                schema: "operations",
                table: "work_order_task_general_supports",
                column: "WorkOrderTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_work_order_task_materials_WorkOrderTaskId",
                schema: "operations",
                table: "work_order_task_materials",
                column: "WorkOrderTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_work_order_task_tools_WorkOrderTaskId",
                schema: "operations",
                table: "work_order_task_tools",
                column: "WorkOrderTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_work_order_tasks_WorkOrderId",
                schema: "operations",
                table: "work_order_tasks",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_work_order_timeline_entries_WorkOrderId_OccurredAtUtc",
                schema: "operations",
                table: "work_order_timeline_entries",
                columns: new[] { "WorkOrderId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_work_orders_FlightId",
                schema: "operations",
                table: "work_orders",
                column: "FlightId",
                unique: true,
                filter: "[Status] = 2");

            migrationBuilder.CreateIndex(
                name: "IX_work_orders_FlightId_OwnerUserId",
                schema: "operations",
                table: "work_orders",
                columns: new[] { "FlightId", "OwnerUserId" },
                unique: true,
                filter: "[Status] IN (0, 1, 2) AND [IsMergeGenerated] = CAST(0 AS bit)");

            migrationBuilder.CreateIndex(
                name: "IX_work_orders_OwnerUserId",
                schema: "operations",
                table: "work_orders",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_work_orders_StationId_ApprovalSequence",
                schema: "operations",
                table: "work_orders",
                columns: new[] { "StationId", "ApprovalSequence" },
                unique: true,
                filter: "[ApprovalSequence] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_work_orders_Status",
                schema: "operations",
                table: "work_orders",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "work_order_service_lines",
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
                name: "work_order_tasks",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "work_orders",
                schema: "operations");
        }
    }
}
