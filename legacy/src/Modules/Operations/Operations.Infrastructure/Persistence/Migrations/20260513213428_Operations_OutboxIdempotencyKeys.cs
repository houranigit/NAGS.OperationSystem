using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Operations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Operations_OutboxIdempotencyKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClientMutationId",
                schema: "operations",
                table: "WorkOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClientFlightId",
                schema: "operations",
                table: "Flights",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_ClientMutationId",
                schema: "operations",
                table: "WorkOrders",
                column: "ClientMutationId",
                unique: true,
                filter: "[ClientMutationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Flights_ClientFlightId",
                schema: "operations",
                table: "Flights",
                column: "ClientFlightId",
                unique: true,
                filter: "[ClientFlightId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_ClientMutationId",
                schema: "operations",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_Flights_ClientFlightId",
                schema: "operations",
                table: "Flights");

            migrationBuilder.DropColumn(
                name: "ClientMutationId",
                schema: "operations",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "ClientFlightId",
                schema: "operations",
                table: "Flights");
        }
    }
}
