using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Identity_PortalAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_Email",
                schema: "identity",
                table: "users");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EmailChangeExpiresAtUtc",
                schema: "identity",
                table: "users",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EmailChangeToken",
                schema: "identity",
                table: "users",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExternalReferenceId",
                schema: "identity",
                table: "users",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LoginEmailReleased",
                schema: "identity",
                table: "users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PendingEmail",
                schema: "identity",
                table: "users",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserType",
                schema: "identity",
                table: "users",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CompatibleUserType",
                schema: "identity",
                table: "roles",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "identity",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Consumer = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ProcessedOnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_messages", x => new { x.MessageId, x.Consumer });
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredOnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProcessedOnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_users_Email",
                schema: "identity",
                table: "users",
                column: "Email",
                unique: true,
                filter: "[LoginEmailReleased] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_users_ExternalReferenceId",
                schema: "identity",
                table: "users",
                column: "ExternalReferenceId");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_ProcessedOnUtc",
                schema: "identity",
                table: "outbox_messages",
                column: "ProcessedOnUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "identity");

            migrationBuilder.DropIndex(
                name: "IX_users_Email",
                schema: "identity",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_users_ExternalReferenceId",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "EmailChangeExpiresAtUtc",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "EmailChangeToken",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "ExternalReferenceId",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "LoginEmailReleased",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "PendingEmail",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "UserType",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "CompatibleUserType",
                schema: "identity",
                table: "roles");

            migrationBuilder.CreateIndex(
                name: "IX_users_Email",
                schema: "identity",
                table: "users",
                column: "Email",
                unique: true);
        }
    }
}
