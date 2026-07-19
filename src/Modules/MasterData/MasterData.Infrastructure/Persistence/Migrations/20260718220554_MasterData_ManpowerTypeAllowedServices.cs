using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterData.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MasterData_ManpowerTypeAllowedServices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "manpower_type_allowed_services",
                schema: "masterdata",
                columns: table => new
                {
                    ManpowerTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_manpower_type_allowed_services", x => new { x.ManpowerTypeId, x.ServiceId });
                    table.ForeignKey(
                        name: "FK_manpower_type_allowed_services_manpower_types_ManpowerTypeId",
                        column: x => x.ManpowerTypeId,
                        principalSchema: "masterdata",
                        principalTable: "manpower_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_manpower_type_allowed_services_services_ServiceId",
                        column: x => x.ServiceId,
                        principalSchema: "masterdata",
                        principalTable: "services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Preserve pre-feature behavior for every record that exists at deployment time.
            // New services and manpower types intentionally receive no automatic allowances.
            migrationBuilder.Sql(
                """
                INSERT INTO [masterdata].[manpower_type_allowed_services] ([ManpowerTypeId], [ServiceId])
                SELECT [m].[Id], [s].[Id]
                FROM [masterdata].[manpower_types] AS [m]
                CROSS JOIN [masterdata].[services] AS [s]
                WHERE [s].[Id] <> '40000000-0000-0000-0000-000000000001';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_manpower_type_allowed_services_ServiceId",
                schema: "masterdata",
                table: "manpower_type_allowed_services",
                column: "ServiceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "manpower_type_allowed_services",
                schema: "masterdata");
        }
    }
}
