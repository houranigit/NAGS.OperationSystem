using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Operations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Operations_WorkOrderServiceLineAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "work_order_service_line_attachments",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderServiceLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    StorageReference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_order_service_line_attachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_work_order_service_line_attachments_work_order_service_lines_WorkOrderServiceLineId",
                        column: x => x.WorkOrderServiceLineId,
                        principalSchema: "operations",
                        principalTable: "work_order_service_lines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_work_order_service_line_attachments_WorkOrderServiceLineId",
                schema: "operations",
                table: "work_order_service_line_attachments",
                column: "WorkOrderServiceLineId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "work_order_service_line_attachments",
                schema: "operations");
        }
    }
}
