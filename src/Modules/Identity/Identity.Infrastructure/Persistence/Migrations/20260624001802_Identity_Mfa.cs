using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Identity_Mfa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MfaEnabled",
                schema: "identity",
                table: "users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MfaRecoveryCodeHashes",
                schema: "identity",
                table: "users",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "MfaSecret",
                schema: "identity",
                table: "users",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MfaEnabled",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "MfaRecoveryCodeHashes",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "MfaSecret",
                schema: "identity",
                table: "users");
        }
    }
}
