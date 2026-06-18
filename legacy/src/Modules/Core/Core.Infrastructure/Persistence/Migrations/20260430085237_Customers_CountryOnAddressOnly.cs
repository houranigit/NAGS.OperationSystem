using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Customers_CountryOnAddressOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE [core].[Customers]
                SET [Address_CountryId] = [CountryId]
                WHERE [Address_CountryId] IS NULL;
                """);

            migrationBuilder.DropIndex(
                name: "IX_Customers_CountryId",
                schema: "core",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "CountryId",
                schema: "core",
                table: "Customers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CountryId",
                schema: "core",
                table: "Customers",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CountryId",
                schema: "core",
                table: "Customers",
                column: "CountryId");
        }
    }
}
