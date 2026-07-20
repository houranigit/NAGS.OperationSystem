using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Operations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Operations_WorkOrderServiceLinePerformers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "work_order_service_line_performers",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderServiceLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffFullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StaffEmployeeId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_order_service_line_performers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_work_order_service_line_performers_work_order_service_lines_WorkOrderServiceLineId",
                        column: x => x.WorkOrderServiceLineId,
                        principalSchema: "operations",
                        principalTable: "work_order_service_lines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_work_order_service_line_performers_WorkOrderServiceLineId",
                schema: "operations",
                table: "work_order_service_line_performers",
                column: "WorkOrderServiceLineId");

            migrationBuilder.Sql(
                """
                INSERT INTO [operations].[work_order_service_line_performers]
                    ([Id], [WorkOrderId], [WorkOrderServiceLineId], [StaffMemberId], [StaffFullName], [StaffEmployeeId])
                SELECT NEWID(), [WorkOrderId], [Id], [PerformedByStaffMemberId], [PerformedByFullName], [PerformedByEmployeeId]
                FROM [operations].[work_order_service_lines];
                """);

            migrationBuilder.DropColumn(
                name: "PerformedByEmployeeId",
                schema: "operations",
                table: "work_order_service_lines");

            migrationBuilder.DropColumn(
                name: "PerformedByFullName",
                schema: "operations",
                table: "work_order_service_lines");

            migrationBuilder.DropColumn(
                name: "PerformedByStaffMemberId",
                schema: "operations",
                table: "work_order_service_lines");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PerformedByEmployeeId",
                schema: "operations",
                table: "work_order_service_lines",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PerformedByFullName",
                schema: "operations",
                table: "work_order_service_lines",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "PerformedByStaffMemberId",
                schema: "operations",
                table: "work_order_service_lines",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.Sql(
                """
                UPDATE service_line
                SET [PerformedByStaffMemberId] = performer.[StaffMemberId],
                    [PerformedByFullName] = performer.[StaffFullName],
                    [PerformedByEmployeeId] = performer.[StaffEmployeeId]
                FROM [operations].[work_order_service_lines] AS service_line
                CROSS APPLY
                (
                    SELECT TOP (1) [StaffMemberId], [StaffFullName], [StaffEmployeeId]
                    FROM [operations].[work_order_service_line_performers]
                    WHERE [WorkOrderServiceLineId] = service_line.[Id]
                    ORDER BY [Id]
                ) AS performer;
                """);

            migrationBuilder.DropTable(
                name: "work_order_service_line_performers",
                schema: "operations");
        }
    }
}
