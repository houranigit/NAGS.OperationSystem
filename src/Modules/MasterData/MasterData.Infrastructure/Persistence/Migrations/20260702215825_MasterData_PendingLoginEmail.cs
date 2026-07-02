using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterData.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MasterData_PendingLoginEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LoginEmailChangeFailureReason",
                schema: "masterdata",
                table: "staff_members",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PendingLoginEmail",
                schema: "masterdata",
                table: "staff_members",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LoginEmailChangeFailureReason",
                schema: "masterdata",
                table: "customer_contacts",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PendingLoginEmail",
                schema: "masterdata",
                table: "customer_contacts",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LoginEmailChangeFailureReason",
                schema: "masterdata",
                table: "staff_members");

            migrationBuilder.DropColumn(
                name: "PendingLoginEmail",
                schema: "masterdata",
                table: "staff_members");

            migrationBuilder.DropColumn(
                name: "LoginEmailChangeFailureReason",
                schema: "masterdata",
                table: "customer_contacts");

            migrationBuilder.DropColumn(
                name: "PendingLoginEmail",
                schema: "masterdata",
                table: "customer_contacts");
        }
    }
}
