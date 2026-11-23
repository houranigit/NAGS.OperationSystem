using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Operations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Operations_AdministratorFlightCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_flight_cancellation_submissions_FlightId_SubmittedByStaffMemberId",
                schema: "operations",
                table: "flight_cancellation_submissions");

            migrationBuilder.AlterColumn<Guid>(
                name: "SubmittedByStaffMemberId",
                schema: "operations",
                table: "flight_cancellation_submissions",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<string>(
                name: "SubmittedByStaffFullName",
                schema: "operations",
                table: "flight_cancellation_submissions",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "SubmittedByStaffEmployeeId",
                schema: "operations",
                table: "flight_cancellation_submissions",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.CreateIndex(
                name: "IX_flight_cancellation_submissions_FlightId_SubmittedByStaffMemberId",
                schema: "operations",
                table: "flight_cancellation_submissions",
                columns: new[] { "FlightId", "SubmittedByStaffMemberId" },
                unique: true,
                filter: "[SubmittedByStaffMemberId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_flight_cancellation_submissions_FlightId_Unlinked",
                schema: "operations",
                table: "flight_cancellation_submissions",
                column: "FlightId",
                unique: true,
                filter: "[SubmittedByStaffMemberId] IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_flight_cancellation_submissions_SubmitterSnapshot",
                schema: "operations",
                table: "flight_cancellation_submissions",
                sql: "([SubmittedByStaffMemberId] IS NULL AND [SubmittedByStaffFullName] IS NULL AND [SubmittedByStaffEmployeeId] IS NULL) OR ([SubmittedByStaffMemberId] IS NOT NULL AND [SubmittedByStaffFullName] IS NOT NULL AND [SubmittedByStaffEmployeeId] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_flight_cancellation_submissions_FlightId_SubmittedByStaffMemberId",
                schema: "operations",
                table: "flight_cancellation_submissions");

            migrationBuilder.DropIndex(
                name: "IX_flight_cancellation_submissions_FlightId_Unlinked",
                schema: "operations",
                table: "flight_cancellation_submissions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_flight_cancellation_submissions_SubmitterSnapshot",
                schema: "operations",
                table: "flight_cancellation_submissions");

            migrationBuilder.AlterColumn<Guid>(
                name: "SubmittedByStaffMemberId",
                schema: "operations",
                table: "flight_cancellation_submissions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "SubmittedByStaffFullName",
                schema: "operations",
                table: "flight_cancellation_submissions",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "SubmittedByStaffEmployeeId",
                schema: "operations",
                table: "flight_cancellation_submissions",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_flight_cancellation_submissions_FlightId_SubmittedByStaffMemberId",
                schema: "operations",
                table: "flight_cancellation_submissions",
                columns: new[] { "FlightId", "SubmittedByStaffMemberId" },
                unique: true);
        }
    }
}
