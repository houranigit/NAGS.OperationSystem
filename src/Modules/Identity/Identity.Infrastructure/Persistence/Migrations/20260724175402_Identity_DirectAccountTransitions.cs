using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Identity_DirectAccountTransitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                schema: "identity",
                table: "users",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<Guid>(
                name: "SecurityStamp",
                schema: "identity",
                table: "user_sessions",
                type: "uniqueidentifier",
                nullable: true);

            // Preserve valid sessions by binding them to the owning user's current authorization
            // generation. A historical orphan cannot be trusted, so give it an unguessable stamp
            // and revoke it before making the column required.
            migrationBuilder.Sql(
                """
                UPDATE [session]
                SET [SecurityStamp] = [user].[SecurityStamp]
                FROM [identity].[user_sessions] AS [session]
                INNER JOIN [identity].[users] AS [user]
                    ON [user].[Id] = [session].[UserId];

                UPDATE [identity].[user_sessions]
                SET [SecurityStamp] = NEWID(),
                    [RevokedAtUtc] = COALESCE([RevokedAtUtc], SYSUTCDATETIME())
                WHERE [SecurityStamp] IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "SecurityStamp",
                schema: "identity",
                table: "user_sessions",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_users_UserType_ExternalReference",
                schema: "identity",
                table: "users",
                sql: "([UserType] IN ('SystemAdministrator', 'ViewerOnly') AND [ExternalReferenceId] IS NULL)\nOR\n([UserType] IN ('StationStaff', 'CustomerContact')\n    AND ([ExternalReferenceId] IS NOT NULL OR [LoginEmailReleased] = 1))");

            migrationBuilder.AddForeignKey(
                name: "FK_users_roles_RoleId",
                schema: "identity",
                table: "users",
                column: "RoleId",
                principalSchema: "identity",
                principalTable: "roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_users_roles_RoleId",
                schema: "identity",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_users_UserType_ExternalReference",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                schema: "identity",
                table: "user_sessions");
        }
    }
}
