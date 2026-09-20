using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterData.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MasterData_AtaChapters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ata_chapter_categories",
                schema: "masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ata_chapter_categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ata_chapters",
                schema: "masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ata_chapters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ata_chapters_ata_chapter_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalSchema: "masterdata",
                        principalTable: "ata_chapter_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ata_chapter_categories_Name",
                schema: "masterdata",
                table: "ata_chapter_categories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ata_chapters_CategoryId",
                schema: "masterdata",
                table: "ata_chapters",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ata_chapters_Code",
                schema: "masterdata",
                table: "ata_chapters",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ata_chapters",
                schema: "masterdata");

            migrationBuilder.DropTable(
                name: "ata_chapter_categories",
                schema: "masterdata");
        }
    }
}
