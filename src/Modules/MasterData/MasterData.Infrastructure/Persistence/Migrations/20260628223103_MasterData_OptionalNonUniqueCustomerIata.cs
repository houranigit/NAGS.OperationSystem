using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterData.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MasterData_OptionalNonUniqueCustomerIata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_customers_IataCode",
                schema: "masterdata",
                table: "customers");

            migrationBuilder.AlterColumn<string>(
                name: "IataCode",
                schema: "masterdata",
                table: "customers",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(2)",
                oldMaxLength: 2);

            migrationBuilder.CreateIndex(
                name: "IX_customers_IataCode",
                schema: "masterdata",
                table: "customers",
                column: "IataCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_customers_IataCode",
                schema: "masterdata",
                table: "customers");

            migrationBuilder.AlterColumn<string>(
                name: "IataCode",
                schema: "masterdata",
                table: "customers",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(2)",
                oldMaxLength: 2,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_customers_IataCode",
                schema: "masterdata",
                table: "customers",
                column: "IataCode",
                unique: true);
        }
    }
}
