using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Operations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Operations_ReturnToRampNumbersAndSignatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LastReturnToRampSequence",
                schema: "operations",
                table: "work_orders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CustomerSignatureContentType",
                schema: "operations",
                table: "work_order_return_to_ramps",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerSignatureFileName",
                schema: "operations",
                table: "work_order_return_to_ramps",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerSignatureReference",
                schema: "operations",
                table: "work_order_return_to_ramps",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CustomerSignatureSize",
                schema: "operations",
                table: "work_order_return_to_ramps",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CustomerSignedAtUtc",
                schema: "operations",
                table: "work_order_return_to_ramps",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Sequence",
                schema: "operations",
                table: "work_order_return_to_ramps",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Existing occurrences receive stable numbers in their original creation order.
            migrationBuilder.Sql("""
                WITH NumberedOccurrences AS (
                    SELECT Id, ROW_NUMBER() OVER (PARTITION BY WorkOrderId ORDER BY CreatedAtUtc, FromUtc, Id) AS Sequence
                    FROM [operations].[work_order_return_to_ramps]
                )
                UPDATE occurrence SET Sequence = numbered.Sequence
                FROM [operations].[work_order_return_to_ramps] occurrence
                INNER JOIN NumberedOccurrences numbered ON occurrence.Id = numbered.Id;

                UPDATE workOrder SET LastReturnToRampSequence = numbered.LastSequence
                FROM [operations].[work_orders] workOrder
                INNER JOIN (
                    SELECT WorkOrderId, MAX(Sequence) AS LastSequence
                    FROM [operations].[work_order_return_to_ramps]
                    GROUP BY WorkOrderId
                ) numbered ON workOrder.Id = numbered.WorkOrderId;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_work_order_return_to_ramps_WorkOrderId_Sequence",
                schema: "operations",
                table: "work_order_return_to_ramps",
                columns: new[] { "WorkOrderId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_work_order_return_to_ramps_WorkOrderId_Sequence",
                schema: "operations",
                table: "work_order_return_to_ramps");

            migrationBuilder.DropColumn(
                name: "LastReturnToRampSequence",
                schema: "operations",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "CustomerSignatureContentType",
                schema: "operations",
                table: "work_order_return_to_ramps");

            migrationBuilder.DropColumn(
                name: "CustomerSignatureFileName",
                schema: "operations",
                table: "work_order_return_to_ramps");

            migrationBuilder.DropColumn(
                name: "CustomerSignatureReference",
                schema: "operations",
                table: "work_order_return_to_ramps");

            migrationBuilder.DropColumn(
                name: "CustomerSignatureSize",
                schema: "operations",
                table: "work_order_return_to_ramps");

            migrationBuilder.DropColumn(
                name: "CustomerSignedAtUtc",
                schema: "operations",
                table: "work_order_return_to_ramps");

            migrationBuilder.DropColumn(
                name: "Sequence",
                schema: "operations",
                table: "work_order_return_to_ramps");
        }
    }
}
