using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Identity_UserTokenIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_users_EmailChangeToken",
                schema: "identity",
                table: "users",
                column: "EmailChangeToken",
                unique: true,
                filter: "[EmailChangeToken] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_users_PasswordResetToken",
                schema: "identity",
                table: "users",
                column: "PasswordResetToken",
                unique: true,
                filter: "[PasswordResetToken] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_EmailChangeToken",
                schema: "identity",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_users_PasswordResetToken",
                schema: "identity",
                table: "users");
        }
    }
}
