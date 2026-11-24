using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Operations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Operations_FlightApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ApprovalStatus",
                schema: "operations",
                table: "flights",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAtUtc",
                schema: "operations",
                table: "flights",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedByDisplayName",
                schema: "operations",
                table: "flights",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedByUserId",
                schema: "operations",
                table: "flights",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ApprovedReturnToRampSequence",
                schema: "operations",
                table: "flights",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovalStatus",
                schema: "operations",
                table: "flights");

            migrationBuilder.DropColumn(
                name: "ApprovedAtUtc",
                schema: "operations",
                table: "flights");

            migrationBuilder.DropColumn(
                name: "ApprovedByDisplayName",
                schema: "operations",
                table: "flights");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                schema: "operations",
                table: "flights");

            migrationBuilder.DropColumn(
                name: "ApprovedReturnToRampSequence",
                schema: "operations",
                table: "flights");
        }
    }
}
