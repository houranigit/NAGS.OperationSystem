using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterData.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MasterData_PortalAccessState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PortalCorrelationId",
                schema: "masterdata",
                table: "staff_members",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PortalFailureReason",
                schema: "masterdata",
                table: "staff_members",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PortalState",
                schema: "masterdata",
                table: "staff_members",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "PortalCorrelationId",
                schema: "masterdata",
                table: "customer_contacts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PortalFailureReason",
                schema: "masterdata",
                table: "customer_contacts",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PortalState",
                schema: "masterdata",
                table: "customer_contacts",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PortalCorrelationId",
                schema: "masterdata",
                table: "staff_members");

            migrationBuilder.DropColumn(
                name: "PortalFailureReason",
                schema: "masterdata",
                table: "staff_members");

            migrationBuilder.DropColumn(
                name: "PortalState",
                schema: "masterdata",
                table: "staff_members");

            migrationBuilder.DropColumn(
                name: "PortalCorrelationId",
                schema: "masterdata",
                table: "customer_contacts");

            migrationBuilder.DropColumn(
                name: "PortalFailureReason",
                schema: "masterdata",
                table: "customer_contacts");

            migrationBuilder.DropColumn(
                name: "PortalState",
                schema: "masterdata",
                table: "customer_contacts");
        }
    }
}
