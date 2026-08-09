using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Operations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Operations_ResourceUsageCalculationTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                schema: "operations",
                table: "work_order_task_tools",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AddColumn<int>(
                name: "CalculationType",
                schema: "operations",
                table: "work_order_task_tools",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FromUtc",
                schema: "operations",
                table: "work_order_task_tools",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ToUtc",
                schema: "operations",
                table: "work_order_task_tools",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                schema: "operations",
                table: "work_order_task_materials",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AddColumn<int>(
                name: "CalculationType",
                schema: "operations",
                table: "work_order_task_materials",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FromUtc",
                schema: "operations",
                table: "work_order_task_materials",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ToUtc",
                schema: "operations",
                table: "work_order_task_materials",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                schema: "operations",
                table: "work_order_task_general_supports",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AddColumn<int>(
                name: "CalculationType",
                schema: "operations",
                table: "work_order_task_general_supports",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FromUtc",
                schema: "operations",
                table: "work_order_task_general_supports",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ToUtc",
                schema: "operations",
                table: "work_order_task_general_supports",
                type: "datetimeoffset",
                nullable: true);

            // Existing tools predate calculation setup. Tools are duration by default, and the
            // owning task's closed window is the only reliable historical usage interval.
            migrationBuilder.Sql(
                """
                UPDATE resource
                SET resource.[CalculationType] = 1,
                    resource.[FromUtc] = task.[FromUtc],
                    resource.[ToUtc] = task.[ToUtc],
                    resource.[Quantity] = NULL
                FROM [operations].[work_order_task_tools] AS resource
                INNER JOIN [operations].[work_order_tasks] AS task
                    ON task.[Id] = resource.[WorkOrderTaskId];
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_work_order_task_tools_ResourceUsage",
                schema: "operations",
                table: "work_order_task_tools",
                sql: "([CalculationType] = 0 AND [Quantity] IS NOT NULL AND [Quantity] > 0 AND [FromUtc] IS NULL AND [ToUtc] IS NULL) OR ([CalculationType] = 1 AND [Quantity] IS NULL AND [FromUtc] IS NOT NULL AND ([ToUtc] IS NULL OR [ToUtc] >= [FromUtc]))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_work_order_task_materials_ResourceUsage",
                schema: "operations",
                table: "work_order_task_materials",
                sql: "([CalculationType] = 0 AND [Quantity] IS NOT NULL AND [Quantity] > 0 AND [FromUtc] IS NULL AND [ToUtc] IS NULL) OR ([CalculationType] = 1 AND [Quantity] IS NULL AND [FromUtc] IS NOT NULL AND ([ToUtc] IS NULL OR [ToUtc] >= [FromUtc]))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_work_order_task_general_supports_ResourceUsage",
                schema: "operations",
                table: "work_order_task_general_supports",
                sql: "([CalculationType] = 0 AND [Quantity] IS NOT NULL AND [Quantity] > 0 AND [FromUtc] IS NULL AND [ToUtc] IS NULL) OR ([CalculationType] = 1 AND [Quantity] IS NULL AND [FromUtc] IS NOT NULL AND ([ToUtc] IS NULL OR [ToUtc] >= [FromUtc]))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_work_order_task_tools_ResourceUsage",
                schema: "operations",
                table: "work_order_task_tools");

            migrationBuilder.DropCheckConstraint(
                name: "CK_work_order_task_materials_ResourceUsage",
                schema: "operations",
                table: "work_order_task_materials");

            migrationBuilder.DropCheckConstraint(
                name: "CK_work_order_task_general_supports_ResourceUsage",
                schema: "operations",
                table: "work_order_task_general_supports");

            migrationBuilder.DropColumn(
                name: "CalculationType",
                schema: "operations",
                table: "work_order_task_tools");

            migrationBuilder.DropColumn(
                name: "FromUtc",
                schema: "operations",
                table: "work_order_task_tools");

            migrationBuilder.DropColumn(
                name: "ToUtc",
                schema: "operations",
                table: "work_order_task_tools");

            migrationBuilder.DropColumn(
                name: "CalculationType",
                schema: "operations",
                table: "work_order_task_materials");

            migrationBuilder.DropColumn(
                name: "FromUtc",
                schema: "operations",
                table: "work_order_task_materials");

            migrationBuilder.DropColumn(
                name: "ToUtc",
                schema: "operations",
                table: "work_order_task_materials");

            migrationBuilder.DropColumn(
                name: "CalculationType",
                schema: "operations",
                table: "work_order_task_general_supports");

            migrationBuilder.DropColumn(
                name: "FromUtc",
                schema: "operations",
                table: "work_order_task_general_supports");

            migrationBuilder.DropColumn(
                name: "ToUtc",
                schema: "operations",
                table: "work_order_task_general_supports");

            // A duration has no legacy quantity representation. Use the former UI default so a
            // rollback can restore the old non-null column without losing resource rows.
            migrationBuilder.Sql(
                """
                UPDATE [operations].[work_order_task_tools] SET [Quantity] = 1 WHERE [Quantity] IS NULL;
                UPDATE [operations].[work_order_task_materials] SET [Quantity] = 1 WHERE [Quantity] IS NULL;
                UPDATE [operations].[work_order_task_general_supports] SET [Quantity] = 1 WHERE [Quantity] IS NULL;
                """);

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                schema: "operations",
                table: "work_order_task_tools",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                schema: "operations",
                table: "work_order_task_materials",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                schema: "operations",
                table: "work_order_task_general_supports",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);
        }
    }
}
