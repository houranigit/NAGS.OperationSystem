using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterData.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MasterData_StaffMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "staff_members",
                schema: "masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    StationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ManpowerTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmploymentStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EmploymentEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    WorkingScheduleMask = table.Column<int>(type: "int", nullable: true),
                    LinkedUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff_members", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "staff_member_licenses",
                schema: "masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LicenseNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff_member_licenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_staff_member_licenses_staff_members_StaffMemberId",
                        column: x => x.StaffMemberId,
                        principalSchema: "masterdata",
                        principalTable: "staff_members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_staff_member_licenses_LicenseId",
                schema: "masterdata",
                table: "staff_member_licenses",
                column: "LicenseId");

            migrationBuilder.CreateIndex(
                name: "IX_staff_member_licenses_StaffMemberId_LicenseId",
                schema: "masterdata",
                table: "staff_member_licenses",
                columns: new[] { "StaffMemberId", "LicenseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_staff_members_Email",
                schema: "masterdata",
                table: "staff_members",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_staff_members_ManpowerTypeId",
                schema: "masterdata",
                table: "staff_members",
                column: "ManpowerTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_staff_members_StationId",
                schema: "masterdata",
                table: "staff_members",
                column: "StationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "staff_member_licenses",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "staff_members",
                schema: "masterdata");
        }
    }
}
