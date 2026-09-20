using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Operations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Operations_WorkOrderTaskAtaChapters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AtaChapterCode",
                schema: "operations",
                table: "work_order_tasks",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AtaChapterId",
                schema: "operations",
                table: "work_order_tasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AtaChapterTitle",
                schema: "operations",
                table: "work_order_tasks",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AtaChapterCode",
                schema: "operations",
                table: "work_order_tasks");

            migrationBuilder.DropColumn(
                name: "AtaChapterId",
                schema: "operations",
                table: "work_order_tasks");

            migrationBuilder.DropColumn(
                name: "AtaChapterTitle",
                schema: "operations",
                table: "work_order_tasks");
        }
    }
}
