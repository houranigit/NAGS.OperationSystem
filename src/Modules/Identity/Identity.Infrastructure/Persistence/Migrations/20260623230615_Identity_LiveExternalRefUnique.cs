using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Identity_LiveExternalRefUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_users_UserType_ExternalReferenceId",
                schema: "identity",
                table: "users",
                columns: new[] { "UserType", "ExternalReferenceId" },
                unique: true,
                filter: "[ExternalReferenceId] IS NOT NULL AND [LoginEmailReleased] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_UserType_ExternalReferenceId",
                schema: "identity",
                table: "users");
        }
    }
}
