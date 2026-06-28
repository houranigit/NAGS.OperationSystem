using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterData.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MasterData_StaffMemberEmployeeId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmployeeId",
                schema: "masterdata",
                table: "staff_members",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE [masterdata].[staff_members]
                SET [EmployeeId] = 'LEGACY-' + REPLACE(CONVERT(varchar(36), [Id]), '-', '')
                WHERE [EmployeeId] IS NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "EmployeeId",
                schema: "masterdata",
                table: "staff_members",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_staff_members_EmployeeId",
                schema: "masterdata",
                table: "staff_members",
                column: "EmployeeId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_staff_members_EmployeeId",
                schema: "masterdata",
                table: "staff_members");

            migrationBuilder.DropColumn(
                name: "EmployeeId",
                schema: "masterdata",
                table: "staff_members");
        }
    }
}
