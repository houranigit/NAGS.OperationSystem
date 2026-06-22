using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterData.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MasterData_Customers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customers",
                schema: "masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IataCode = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    IcaoCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CountryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OfficialEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    OfficialPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    LogoFileReference = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "customer_addresses",
                schema: "masterdata",
                columns: table => new
                {
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Line1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Line2 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Region = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_addresses", x => x.CustomerId);
                    table.ForeignKey(
                        name: "FK_customer_addresses_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "masterdata",
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "customer_contacts",
                schema: "masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    JobTitle = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    LinkedUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_contacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_customer_contacts_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "masterdata",
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_customer_contacts_CustomerId_Email",
                schema: "masterdata",
                table: "customer_contacts",
                columns: new[] { "CustomerId", "Email" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_customers_CountryId",
                schema: "masterdata",
                table: "customers",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_customers_IataCode",
                schema: "masterdata",
                table: "customers",
                column: "IataCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customers_IcaoCode",
                schema: "masterdata",
                table: "customers",
                column: "IcaoCode",
                unique: true,
                filter: "[IcaoCode] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_addresses",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "customer_contacts",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "customers",
                schema: "masterdata");
        }
    }
}
