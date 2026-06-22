using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterData.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MasterData_Stations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stations",
                schema: "masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IataCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    IcaoCode = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CountryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stations_CountryId",
                schema: "masterdata",
                table: "stations",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_stations_IataCode",
                schema: "masterdata",
                table: "stations",
                column: "IataCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stations_IcaoCode",
                schema: "masterdata",
                table: "stations",
                column: "IcaoCode",
                unique: true,
                filter: "[IcaoCode] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stations",
                schema: "masterdata");
        }
    }
}
