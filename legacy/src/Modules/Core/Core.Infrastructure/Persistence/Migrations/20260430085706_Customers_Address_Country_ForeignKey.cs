using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Customers_Address_Country_ForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Customers_Address_CountryId",
                schema: "core",
                table: "Customers",
                column: "Address_CountryId");

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_Countries_Address_CountryId",
                schema: "core",
                table: "Customers",
                column: "Address_CountryId",
                principalSchema: "core",
                principalTable: "Countries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Customers_Countries_Address_CountryId",
                schema: "core",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Customers_Address_CountryId",
                schema: "core",
                table: "Customers");
        }
    }
}
