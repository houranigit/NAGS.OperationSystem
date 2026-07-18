using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterData.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MasterData_RetireOnCallService : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "masterdata",
                table: "services",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000002"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "masterdata",
                table: "services",
                columns: new[] { "Id", "Name", "Description", "IsActive", "CreatedAtUtc", "UpdatedAtUtc" },
                values: new object[]
                {
                    new Guid("40000000-0000-0000-0000-000000000002"),
                    "On Call",
                    "On-call standby technical support billed per hour.",
                    true,
                    new DateTimeOffset(2026, 7, 3, 0, 0, 0, TimeSpan.Zero),
                    null
                });
        }
    }
}
