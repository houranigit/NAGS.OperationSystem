using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Operations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Operations_WorkOrderSignatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomerSignatureContentType",
                schema: "operations",
                table: "work_orders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerSignatureFileName",
                schema: "operations",
                table: "work_orders",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerSignatureReference",
                schema: "operations",
                table: "work_orders",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CustomerSignatureSize",
                schema: "operations",
                table: "work_orders",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CustomerSignedAtUtc",
                schema: "operations",
                table: "work_orders",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomerSignatureContentType",
                schema: "operations",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "CustomerSignatureFileName",
                schema: "operations",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "CustomerSignatureReference",
                schema: "operations",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "CustomerSignatureSize",
                schema: "operations",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "CustomerSignedAtUtc",
                schema: "operations",
                table: "work_orders");
        }
    }
}
