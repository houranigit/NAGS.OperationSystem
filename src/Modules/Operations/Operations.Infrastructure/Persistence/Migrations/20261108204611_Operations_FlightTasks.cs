using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Operations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Operations_FlightTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Notes",
                schema: "operations",
                table: "flight_services",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CustomerReferenceNumber",
                schema: "operations",
                table: "flight_services",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "StorageKey",
                schema: "operations",
                table: "flight_service_attachments",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "FileName",
                schema: "operations",
                table: "flight_service_attachments",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "ContentType",
                schema: "operations",
                table: "flight_service_attachments",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                schema: "operations",
                table: "flight_service_attachments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "flight_tasks",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlightId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskType = table.Column<int>(type: "int", nullable: false),
                    AtaChapterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AtaChapterCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    AtaChapterTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CustomerReferenceNumber = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FromUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ToUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flight_tasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_flight_tasks_flights_FlightId",
                        column: x => x.FlightId,
                        principalSchema: "operations",
                        principalTable: "flights",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "flight_task_attachments",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlightTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flight_task_attachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_flight_task_attachments_flight_tasks_FlightTaskId",
                        column: x => x.FlightTaskId,
                        principalSchema: "operations",
                        principalTable: "flight_tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "flight_task_general_supports",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlightTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GeneralSupportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GeneralSupportName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CalculationType = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    Quantity = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    FromUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ToUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flight_task_general_supports", x => x.Id);
                    table.CheckConstraint("CK_flight_task_general_supports_ResourceUsage", "([CalculationType] = 0 AND [Quantity] IS NOT NULL AND [Quantity] > 0 AND [FromUtc] IS NULL AND [ToUtc] IS NULL) OR ([CalculationType] = 1 AND [Quantity] IS NULL AND [FromUtc] IS NOT NULL AND ([ToUtc] IS NULL OR [ToUtc] >= [FromUtc]))");
                    table.ForeignKey(
                        name: "FK_flight_task_general_supports_flight_tasks_FlightTaskId",
                        column: x => x.FlightTaskId,
                        principalSchema: "operations",
                        principalTable: "flight_tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "flight_task_materials",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlightTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MaterialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MaterialName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CalculationType = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    Quantity = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    FromUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ToUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flight_task_materials", x => x.Id);
                    table.CheckConstraint("CK_flight_task_materials_ResourceUsage", "([CalculationType] = 0 AND [Quantity] IS NOT NULL AND [Quantity] > 0 AND [FromUtc] IS NULL AND [ToUtc] IS NULL) OR ([CalculationType] = 1 AND [Quantity] IS NULL AND [FromUtc] IS NOT NULL AND ([ToUtc] IS NULL OR [ToUtc] >= [FromUtc]))");
                    table.ForeignKey(
                        name: "FK_flight_task_materials_flight_tasks_FlightTaskId",
                        column: x => x.FlightTaskId,
                        principalSchema: "operations",
                        principalTable: "flight_tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "flight_task_performers",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlightTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffFullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StaffEmployeeId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FromUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ToUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flight_task_performers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_flight_task_performers_flight_tasks_FlightTaskId",
                        column: x => x.FlightTaskId,
                        principalSchema: "operations",
                        principalTable: "flight_tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "flight_task_tools",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlightTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToolId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToolName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CalculationType = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    Quantity = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    FromUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ToUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flight_task_tools", x => x.Id);
                    table.CheckConstraint("CK_flight_task_tools_ResourceUsage", "([CalculationType] = 0 AND [Quantity] IS NOT NULL AND [Quantity] > 0 AND [FromUtc] IS NULL AND [ToUtc] IS NULL) OR ([CalculationType] = 1 AND [Quantity] IS NULL AND [FromUtc] IS NOT NULL AND ([ToUtc] IS NULL OR [ToUtc] >= [FromUtc]))");
                    table.ForeignKey(
                        name: "FK_flight_task_tools_flight_tasks_FlightTaskId",
                        column: x => x.FlightTaskId,
                        principalSchema: "operations",
                        principalTable: "flight_tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_flight_service_performers_FlightServiceId_StaffMemberId",
                schema: "operations",
                table: "flight_service_performers",
                columns: new[] { "FlightServiceId", "StaffMemberId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_flight_task_attachments_FlightTaskId",
                schema: "operations",
                table: "flight_task_attachments",
                column: "FlightTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_flight_task_general_supports_FlightTaskId",
                schema: "operations",
                table: "flight_task_general_supports",
                column: "FlightTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_flight_task_general_supports_FlightTaskId_GeneralSupportId",
                schema: "operations",
                table: "flight_task_general_supports",
                columns: new[] { "FlightTaskId", "GeneralSupportId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_flight_task_materials_FlightTaskId",
                schema: "operations",
                table: "flight_task_materials",
                column: "FlightTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_flight_task_materials_FlightTaskId_MaterialId",
                schema: "operations",
                table: "flight_task_materials",
                columns: new[] { "FlightTaskId", "MaterialId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_flight_task_performers_FlightTaskId",
                schema: "operations",
                table: "flight_task_performers",
                column: "FlightTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_flight_task_performers_FlightTaskId_StaffMemberId",
                schema: "operations",
                table: "flight_task_performers",
                columns: new[] { "FlightTaskId", "StaffMemberId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_flight_task_tools_FlightTaskId",
                schema: "operations",
                table: "flight_task_tools",
                column: "FlightTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_flight_task_tools_FlightTaskId_ToolId",
                schema: "operations",
                table: "flight_task_tools",
                columns: new[] { "FlightTaskId", "ToolId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_flight_tasks_FlightId",
                schema: "operations",
                table: "flight_tasks",
                column: "FlightId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "flight_task_attachments",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "flight_task_general_supports",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "flight_task_materials",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "flight_task_performers",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "flight_task_tools",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "flight_tasks",
                schema: "operations");

            migrationBuilder.DropIndex(
                name: "IX_flight_service_performers_FlightServiceId_StaffMemberId",
                schema: "operations",
                table: "flight_service_performers");

            migrationBuilder.DropColumn(
                name: "Kind",
                schema: "operations",
                table: "flight_service_attachments");

            migrationBuilder.AlterColumn<string>(
                name: "Notes",
                schema: "operations",
                table: "flight_services",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CustomerReferenceNumber",
                schema: "operations",
                table: "flight_services",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "StorageKey",
                schema: "operations",
                table: "flight_service_attachments",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "FileName",
                schema: "operations",
                table: "flight_service_attachments",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "ContentType",
                schema: "operations",
                table: "flight_service_attachments",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);
        }
    }
}
