using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Identity_SessionRotationHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FamilyId",
                schema: "identity",
                table: "user_sessions",
                type: "uniqueidentifier",
                nullable: true);

            // Every pre-existing session is the root of its own sign-in lineage. This preserves
            // current refresh tokens and avoids grouping unrelated devices under Guid.Empty.
            migrationBuilder.Sql(
                """
                UPDATE [identity].[user_sessions]
                SET [FamilyId] = [Id]
                WHERE [FamilyId] IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "FamilyId",
                schema: "identity",
                table: "user_sessions",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                schema: "identity",
                table: "user_sessions",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateIndex(
                name: "IX_user_sessions_FamilyId",
                schema: "identity",
                table: "user_sessions",
                column: "FamilyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_user_sessions_FamilyId",
                schema: "identity",
                table: "user_sessions");

            migrationBuilder.DropColumn(
                name: "FamilyId",
                schema: "identity",
                table: "user_sessions");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                schema: "identity",
                table: "user_sessions");
        }
    }
}
