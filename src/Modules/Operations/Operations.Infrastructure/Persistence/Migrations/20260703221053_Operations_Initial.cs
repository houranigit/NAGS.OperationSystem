using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Operations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Operations_Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "operations");

            migrationBuilder.CreateTable(
                name: "flights",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerIataCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    CustomerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StationIataCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    StationName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    OperationTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationTypeName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FlightNumber = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    OriginalFlightNumber = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    ScheduledArrivalUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ScheduledDepartureUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AircraftTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AircraftManufacturer = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AircraftModel = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ContractNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    MergedIntoFlightId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PotentialDuplicateOfFlightId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flights", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "operations",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Consumer = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ProcessedOnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_messages", x => new { x.MessageId, x.Consumer });
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredOnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProcessedOnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "station_work_order_sequences",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StationIata = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    LastValue = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_station_work_order_sequences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "work_orders",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlightId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    WorkOrderNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerIataCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    CustomerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StationIataCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    StationName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    OperationTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationTypeName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FlightNumber = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    AircraftTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AircraftManufacturer = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AircraftModel = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    AircraftTailNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ScheduledArrivalUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ScheduledDepartureUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActualArrivalUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ActualDepartureUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CanceledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CanceledAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CustomerSignatureReference = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SupersededByWorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_orders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "flight_assigned_employees",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlightId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffFullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StaffEmployeeId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flight_assigned_employees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_flight_assigned_employees_flights_FlightId",
                        column: x => x.FlightId,
                        principalSchema: "operations",
                        principalTable: "flights",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "flight_planned_services",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlightId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flight_planned_services", x => x.Id);
                    table.ForeignKey(
                        name: "FK_flight_planned_services_flights_FlightId",
                        column: x => x.FlightId,
                        principalSchema: "operations",
                        principalTable: "flights",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                    Origin = table.Column<int>(type: "int", nullable: false),
                    FromUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ToUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReturnToRamp = table.Column<bool>(type: "bit", nullable: false)
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
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    FromUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ToUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReturnToRamp = table.Column<bool>(type: "bit", nullable: false)
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
                name: "work_order_service_line_employees",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffFullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StaffEmployeeId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_order_service_line_employees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_work_order_service_line_employees_work_order_service_lines_ServiceLineId",
                        column: x => x.ServiceLineId,
                        principalSchema: "operations",
                        principalTable: "work_order_service_lines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "work_order_task_attachments",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StorageReference = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CapturedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_order_task_attachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_work_order_task_attachments_work_order_tasks_TaskId",
                        column: x => x.TaskId,
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
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffFullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StaffEmployeeId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_order_task_employees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_work_order_task_employees_work_order_tasks_TaskId",
                        column: x => x.TaskId,
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
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GeneralSupportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GeneralSupportName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_order_task_general_supports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_work_order_task_general_supports_work_order_tasks_TaskId",
                        column: x => x.TaskId,
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
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MaterialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MaterialName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_order_task_materials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_work_order_task_materials_work_order_tasks_TaskId",
                        column: x => x.TaskId,
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
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToolId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToolName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_order_task_tools", x => x.Id);
                    table.ForeignKey(
                        name: "FK_work_order_task_tools_work_order_tasks_TaskId",
                        column: x => x.TaskId,
                        principalSchema: "operations",
                        principalTable: "work_order_tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_flight_assigned_employees_FlightId",
                schema: "operations",
                table: "flight_assigned_employees",
                column: "FlightId");

            migrationBuilder.CreateIndex(
                name: "IX_flight_planned_services_FlightId",
                schema: "operations",
                table: "flight_planned_services",
                column: "FlightId");

            migrationBuilder.CreateIndex(
                name: "IX_flights_OriginalFlightNumber",
                schema: "operations",
                table: "flights",
                column: "OriginalFlightNumber");

            migrationBuilder.CreateIndex(
                name: "IX_flights_Status",
                schema: "operations",
                table: "flights",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_ProcessedOnUtc",
                schema: "operations",
                table: "outbox_messages",
                column: "ProcessedOnUtc");

            migrationBuilder.CreateIndex(
                name: "IX_work_order_service_line_employees_ServiceLineId",
                schema: "operations",
                table: "work_order_service_line_employees",
                column: "ServiceLineId");

            migrationBuilder.CreateIndex(
                name: "IX_work_order_service_lines_WorkOrderId",
                schema: "operations",
                table: "work_order_service_lines",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_work_order_task_attachments_TaskId",
                schema: "operations",
                table: "work_order_task_attachments",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_work_order_task_employees_TaskId",
                schema: "operations",
                table: "work_order_task_employees",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_work_order_task_general_supports_TaskId",
                schema: "operations",
                table: "work_order_task_general_supports",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_work_order_task_materials_TaskId",
                schema: "operations",
                table: "work_order_task_materials",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_work_order_task_tools_TaskId",
                schema: "operations",
                table: "work_order_task_tools",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_work_order_tasks_WorkOrderId",
                schema: "operations",
                table: "work_order_tasks",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_work_orders_FlightId",
                schema: "operations",
                table: "work_orders",
                column: "FlightId");

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
                name: "flight_assigned_employees",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "flight_planned_services",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "station_work_order_sequences",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "work_order_service_line_employees",
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
                name: "flights",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "work_order_service_lines",
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
