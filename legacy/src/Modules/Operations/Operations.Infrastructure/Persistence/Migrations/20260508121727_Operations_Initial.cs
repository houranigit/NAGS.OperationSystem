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
                name: "Flights",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerIataCode = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StationName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    StationIataCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    OperationTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationTypeName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    FlightNumber = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Sta = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Std = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AircraftTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AircraftTypeModel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CanceledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AcceptedWorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AcceptedWorkOrderNumber = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flights", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InboxMessages",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StationWorkOrderCounters",
                schema: "operations",
                columns: table => new
                {
                    StationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StationWorkOrderCounters", x => x.StationId);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrders",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlightId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConfirmedFlightId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WorkOrderNo = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerIataCode = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StationName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    StationIataCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    OperationTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationTypeName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    FlightNumber = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    AircraftTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AircraftTypeModel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AircraftTailNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Sta = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Std = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Ata = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Atd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsCanceled = table.Column<bool>(type: "bit", nullable: false),
                    CanceledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Remarks = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedByEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CustomerSignature = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    MarkedForDeletionAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FlightAssignments",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlightId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeFullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EmployeeStationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeStationName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    EmployeeStationIataCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    EmployeeManpowerTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeManpowerTypeName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlightAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FlightAssignments_Flights_FlightId",
                        column: x => x.FlightId,
                        principalSchema: "operations",
                        principalTable: "Flights",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FlightServices",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlightId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    IsAog = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlightServices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FlightServices_Flights_FlightId",
                        column: x => x.FlightId,
                        principalSchema: "operations",
                        principalTable: "Flights",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FlightWorkOrderAttachments",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlightId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlightWorkOrderAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FlightWorkOrderAttachments_Flights_FlightId",
                        column: x => x.FlightId,
                        principalSchema: "operations",
                        principalTable: "Flights",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderServiceLines",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Service_IsAog = table.Column<bool>(type: "bit", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeFullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EmployeeStationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeStationName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    EmployeeStationIataCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    EmployeeManpowerTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeManpowerTypeName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    From = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    To = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ReturnToRamp = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderServiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrderServiceLines_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalSchema: "operations",
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderTasks",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskType = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    From = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    To = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReturnToRamp = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrderTasks_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalSchema: "operations",
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderTaskAttachments",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Bytes = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    SizeBytes = table.Column<int>(type: "int", nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderTaskAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrderTaskAttachments_WorkOrderTasks_TaskId",
                        column: x => x.TaskId,
                        principalSchema: "operations",
                        principalTable: "WorkOrderTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderTaskEmployees",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeFullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EmployeeStationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeStationName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    EmployeeStationIataCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    EmployeeManpowerTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeManpowerTypeName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderTaskEmployees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrderTaskEmployees_WorkOrderTasks_TaskId",
                        column: x => x.TaskId,
                        principalSchema: "operations",
                        principalTable: "WorkOrderTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderTaskGeneralSupports",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GeneralSupportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GeneralSupportName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderTaskGeneralSupports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrderTaskGeneralSupports_WorkOrderTasks_TaskId",
                        column: x => x.TaskId,
                        principalSchema: "operations",
                        principalTable: "WorkOrderTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderTaskMaterials",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MaterialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MaterialName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderTaskMaterials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrderTaskMaterials_WorkOrderTasks_TaskId",
                        column: x => x.TaskId,
                        principalSchema: "operations",
                        principalTable: "WorkOrderTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderTaskTools",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToolId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToolName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderTaskTools", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrderTaskTools_WorkOrderTasks_TaskId",
                        column: x => x.TaskId,
                        principalSchema: "operations",
                        principalTable: "WorkOrderTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FlightAssignments_FlightId",
                schema: "operations",
                table: "FlightAssignments",
                column: "FlightId");

            migrationBuilder.CreateIndex(
                name: "IX_Flights_ContractId",
                schema: "operations",
                table: "Flights",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_Flights_CreatedAt",
                schema: "operations",
                table: "Flights",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Flights_Status",
                schema: "operations",
                table: "Flights",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Flights_Status_UpdatedAt",
                schema: "operations",
                table: "Flights",
                columns: new[] { "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Flights_UpdatedAt",
                schema: "operations",
                table: "Flights",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_FlightServices_FlightId",
                schema: "operations",
                table: "FlightServices",
                column: "FlightId");

            migrationBuilder.CreateIndex(
                name: "IX_FlightWorkOrderAttachments_FlightId",
                schema: "operations",
                table: "FlightWorkOrderAttachments",
                column: "FlightId");

            migrationBuilder.CreateIndex(
                name: "IX_FlightWorkOrderAttachments_FlightId_WorkOrderId",
                schema: "operations",
                table: "FlightWorkOrderAttachments",
                columns: new[] { "FlightId", "WorkOrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FlightWorkOrderAttachments_WorkOrderId",
                schema: "operations",
                table: "FlightWorkOrderAttachments",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_CreatedAt",
                schema: "operations",
                table: "WorkOrders",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_FlightId",
                schema: "operations",
                table: "WorkOrders",
                column: "FlightId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_FlightId_CreatedByEmployeeId_Status",
                schema: "operations",
                table: "WorkOrders",
                columns: new[] { "FlightId", "CreatedByEmployeeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_FlightId_Status",
                schema: "operations",
                table: "WorkOrders",
                columns: new[] { "FlightId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_Status",
                schema: "operations",
                table: "WorkOrders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_Status_MarkedForDeletionAt",
                schema: "operations",
                table: "WorkOrders",
                columns: new[] { "Status", "MarkedForDeletionAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_Status_UpdatedAt",
                schema: "operations",
                table: "WorkOrders",
                columns: new[] { "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_UpdatedAt",
                schema: "operations",
                table: "WorkOrders",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderServiceLines_WorkOrderId",
                schema: "operations",
                table: "WorkOrderServiceLines",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderServiceLines_WorkOrderId_From",
                schema: "operations",
                table: "WorkOrderServiceLines",
                columns: new[] { "WorkOrderId", "From" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderTaskAttachments_TaskId",
                schema: "operations",
                table: "WorkOrderTaskAttachments",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderTaskEmployees_TaskId",
                schema: "operations",
                table: "WorkOrderTaskEmployees",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderTaskGeneralSupports_TaskId",
                schema: "operations",
                table: "WorkOrderTaskGeneralSupports",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderTaskMaterials_TaskId",
                schema: "operations",
                table: "WorkOrderTaskMaterials",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderTasks_WorkOrderId",
                schema: "operations",
                table: "WorkOrderTasks",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderTaskTools_TaskId",
                schema: "operations",
                table: "WorkOrderTaskTools",
                column: "TaskId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FlightAssignments",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "FlightServices",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "FlightWorkOrderAttachments",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "InboxMessages",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "StationWorkOrderCounters",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "WorkOrderServiceLines",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "WorkOrderTaskAttachments",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "WorkOrderTaskEmployees",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "WorkOrderTaskGeneralSupports",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "WorkOrderTaskMaterials",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "WorkOrderTaskTools",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "Flights",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "WorkOrderTasks",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "WorkOrders",
                schema: "operations");
        }
    }
}
