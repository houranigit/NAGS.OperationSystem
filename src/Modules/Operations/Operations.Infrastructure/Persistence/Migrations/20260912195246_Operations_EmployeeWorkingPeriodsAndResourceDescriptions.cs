using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Operations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Operations_EmployeeWorkingPeriodsAndResourceDescriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "operations",
                table: "work_order_task_tools",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "operations",
                table: "work_order_task_materials",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "operations",
                table: "work_order_task_general_supports",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FromUtc",
                schema: "operations",
                table: "work_order_task_employees",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ToUtc",
                schema: "operations",
                table: "work_order_task_employees",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FromUtc",
                schema: "operations",
                table: "work_order_service_line_performers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ToUtc",
                schema: "operations",
                table: "work_order_service_line_performers",
                type: "datetimeoffset",
                nullable: true);

            // Historical assignments represented participation for the complete service/task.
            migrationBuilder.Sql("""
                UPDATE employee
                SET employee.FromUtc = task.FromUtc, employee.ToUtc = task.ToUtc
                FROM operations.work_order_task_employees employee
                INNER JOIN operations.work_order_tasks task ON task.Id = employee.WorkOrderTaskId;

                UPDATE performer
                SET performer.FromUtc = service.FromUtc, performer.ToUtc = service.ToUtc
                FROM operations.work_order_service_line_performers performer
                INNER JOIN operations.work_order_service_lines service ON service.Id = performer.WorkOrderServiceLineId;
                """);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "FromUtc",
                schema: "operations",
                table: "work_order_task_employees",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ToUtc",
                schema: "operations",
                table: "work_order_task_employees",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "FromUtc",
                schema: "operations",
                table: "work_order_service_line_performers",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ToUtc",
                schema: "operations",
                table: "work_order_service_line_performers",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldNullable: true);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                schema: "operations",
                table: "work_order_task_tools");

            migrationBuilder.DropColumn(
                name: "Description",
                schema: "operations",
                table: "work_order_task_materials");

            migrationBuilder.DropColumn(
                name: "Description",
                schema: "operations",
                table: "work_order_task_general_supports");

            migrationBuilder.DropColumn(
                name: "FromUtc",
                schema: "operations",
                table: "work_order_task_employees");

            migrationBuilder.DropColumn(
                name: "ToUtc",
                schema: "operations",
                table: "work_order_task_employees");

            migrationBuilder.DropColumn(
                name: "FromUtc",
                schema: "operations",
                table: "work_order_service_line_performers");

            migrationBuilder.DropColumn(
                name: "ToUtc",
                schema: "operations",
                table: "work_order_service_line_performers");
        }
    }
}
