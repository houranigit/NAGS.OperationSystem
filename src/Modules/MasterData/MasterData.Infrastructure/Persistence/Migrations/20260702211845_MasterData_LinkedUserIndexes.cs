using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterData.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MasterData_LinkedUserIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_staff_members_LinkedUserId",
                schema: "masterdata",
                table: "staff_members",
                column: "LinkedUserId",
                unique: true,
                filter: "[LinkedUserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_customer_contacts_LinkedUserId",
                schema: "masterdata",
                table: "customer_contacts",
                column: "LinkedUserId",
                unique: true,
                filter: "[LinkedUserId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_staff_members_LinkedUserId",
                schema: "masterdata",
                table: "staff_members");

            migrationBuilder.DropIndex(
                name: "IX_customer_contacts_LinkedUserId",
                schema: "masterdata",
                table: "customer_contacts");
        }
    }
}
