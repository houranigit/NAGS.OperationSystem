using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Operations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Operations_MobileMutations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Operations_MobileMutations",
                schema: "operations",
                columns: table => new
                {
                    ClientMutationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FlightId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClientFlightId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Operations_MobileMutations", x => x.ClientMutationId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Operations_MobileMutations_ClientFlightId",
                schema: "operations",
                table: "Operations_MobileMutations",
                column: "ClientFlightId",
                unique: true,
                filter: "[ClientFlightId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Operations_MobileMutations_OwnerUserId",
                schema: "operations",
                table: "Operations_MobileMutations",
                column: "OwnerUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Operations_MobileMutations",
                schema: "operations");
        }
    }
}
