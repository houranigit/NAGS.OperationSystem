using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Notifications.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Notifications_AddIsArchived : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notifications_Recipient_IsRead_CreatedAt",
                schema: "notifications",
                table: "Notifications");

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                schema: "notifications",
                table: "Notifications",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                schema: "notifications",
                table: "Notifications",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Recipient_IsRead_CreatedAt",
                schema: "notifications",
                table: "Notifications",
                columns: new[] { "RecipientUserId", "IsRead", "CreatedAt" },
                filter: "[IsArchived] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notifications_Recipient_IsRead_CreatedAt",
                schema: "notifications",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                schema: "notifications",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                schema: "notifications",
                table: "Notifications");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Recipient_IsRead_CreatedAt",
                schema: "notifications",
                table: "Notifications",
                columns: new[] { "RecipientUserId", "IsRead", "CreatedAt" });
        }
    }
}
