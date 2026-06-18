using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Store.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Store_Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "store");

            migrationBuilder.CreateTable(
                name: "GeneralSupportPricePlans",
                schema: "store",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GeneralSupportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Basis = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneralSupportPricePlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GeneralSupports",
                schema: "store",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDuration = table.Column<bool>(type: "bit", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneralSupports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InboxMessages",
                schema: "store",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MaterialPricePlans",
                schema: "store",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MaterialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Basis = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialPricePlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Materials",
                schema: "store",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Materials", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "store",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ToolPricePlans",
                schema: "store",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToolId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Basis = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToolPricePlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tools",
                schema: "store",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tools", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Units",
                schema: "store",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Units", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GeneralSupportPricePlanBrackets",
                schema: "store",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MinMinutes = table.Column<int>(type: "int", nullable: false),
                    MaxMinutes = table.Column<int>(type: "int", nullable: true),
                    BlockSize = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    BillingMode = table.Column<int>(type: "int", nullable: false),
                    GeneralSupportPricePlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneralSupportPricePlanBrackets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GeneralSupportPricePlanBrackets_GeneralSupportPricePlans_GeneralSupportPricePlanId",
                        column: x => x.GeneralSupportPricePlanId,
                        principalSchema: "store",
                        principalTable: "GeneralSupportPricePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MaterialPricePlanBrackets",
                schema: "store",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MinMinutes = table.Column<int>(type: "int", nullable: false),
                    MaxMinutes = table.Column<int>(type: "int", nullable: true),
                    BlockSize = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    BillingMode = table.Column<int>(type: "int", nullable: false),
                    MaterialPricePlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialPricePlanBrackets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaterialPricePlanBrackets_MaterialPricePlans_MaterialPricePlanId",
                        column: x => x.MaterialPricePlanId,
                        principalSchema: "store",
                        principalTable: "MaterialPricePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ToolPricePlanBrackets",
                schema: "store",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MinMinutes = table.Column<int>(type: "int", nullable: false),
                    MaxMinutes = table.Column<int>(type: "int", nullable: true),
                    BlockSize = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    BillingMode = table.Column<int>(type: "int", nullable: false),
                    ToolPricePlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToolPricePlanBrackets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ToolPricePlanBrackets_ToolPricePlans_ToolPricePlanId",
                        column: x => x.ToolPricePlanId,
                        principalSchema: "store",
                        principalTable: "ToolPricePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Equipments",
                schema: "store",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToolId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FactoryId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SerialId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CalibrationDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Equipments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Equipments_Tools_ToolId",
                        column: x => x.ToolId,
                        principalSchema: "store",
                        principalTable: "Tools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Equipments_FactoryId",
                schema: "store",
                table: "Equipments",
                column: "FactoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Equipments_SerialId",
                schema: "store",
                table: "Equipments",
                column: "SerialId");

            migrationBuilder.CreateIndex(
                name: "IX_Equipments_ToolId_FactoryId_SerialId",
                schema: "store",
                table: "Equipments",
                columns: new[] { "ToolId", "FactoryId", "SerialId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GeneralSupportPricePlanBrackets_GeneralSupportPricePlanId",
                schema: "store",
                table: "GeneralSupportPricePlanBrackets",
                column: "GeneralSupportPricePlanId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralSupportPricePlans_CreatedAt",
                schema: "store",
                table: "GeneralSupportPricePlans",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralSupportPricePlans_CurrencyId",
                schema: "store",
                table: "GeneralSupportPricePlans",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralSupportPricePlans_GeneralSupportId",
                schema: "store",
                table: "GeneralSupportPricePlans",
                column: "GeneralSupportId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GeneralSupportPricePlans_UpdatedAt",
                schema: "store",
                table: "GeneralSupportPricePlans",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralSupports_CreatedAt",
                schema: "store",
                table: "GeneralSupports",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralSupports_IsActive",
                schema: "store",
                table: "GeneralSupports",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralSupports_IsDuration",
                schema: "store",
                table: "GeneralSupports",
                column: "IsDuration");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralSupports_Name",
                schema: "store",
                table: "GeneralSupports",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GeneralSupports_UnitId",
                schema: "store",
                table: "GeneralSupports",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralSupports_UpdatedAt",
                schema: "store",
                table: "GeneralSupports",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialPricePlanBrackets_MaterialPricePlanId",
                schema: "store",
                table: "MaterialPricePlanBrackets",
                column: "MaterialPricePlanId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialPricePlans_CreatedAt",
                schema: "store",
                table: "MaterialPricePlans",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialPricePlans_CurrencyId",
                schema: "store",
                table: "MaterialPricePlans",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialPricePlans_MaterialId",
                schema: "store",
                table: "MaterialPricePlans",
                column: "MaterialId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaterialPricePlans_UpdatedAt",
                schema: "store",
                table: "MaterialPricePlans",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Materials_CreatedAt",
                schema: "store",
                table: "Materials",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Materials_IsActive",
                schema: "store",
                table: "Materials",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Materials_Name",
                schema: "store",
                table: "Materials",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Materials_UnitId",
                schema: "store",
                table: "Materials",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_Materials_UpdatedAt",
                schema: "store",
                table: "Materials",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ToolPricePlanBrackets_ToolPricePlanId",
                schema: "store",
                table: "ToolPricePlanBrackets",
                column: "ToolPricePlanId");

            migrationBuilder.CreateIndex(
                name: "IX_ToolPricePlans_CreatedAt",
                schema: "store",
                table: "ToolPricePlans",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ToolPricePlans_CurrencyId",
                schema: "store",
                table: "ToolPricePlans",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_ToolPricePlans_ToolId",
                schema: "store",
                table: "ToolPricePlans",
                column: "ToolId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ToolPricePlans_UpdatedAt",
                schema: "store",
                table: "ToolPricePlans",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Tools_CreatedAt",
                schema: "store",
                table: "Tools",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Tools_IsActive",
                schema: "store",
                table: "Tools",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Tools_Name",
                schema: "store",
                table: "Tools",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tools_UpdatedAt",
                schema: "store",
                table: "Tools",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Units_Code",
                schema: "store",
                table: "Units",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Units_CreatedAt",
                schema: "store",
                table: "Units",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Units_IsActive",
                schema: "store",
                table: "Units",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Units_UpdatedAt",
                schema: "store",
                table: "Units",
                column: "UpdatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Equipments",
                schema: "store");

            migrationBuilder.DropTable(
                name: "GeneralSupportPricePlanBrackets",
                schema: "store");

            migrationBuilder.DropTable(
                name: "GeneralSupports",
                schema: "store");

            migrationBuilder.DropTable(
                name: "InboxMessages",
                schema: "store");

            migrationBuilder.DropTable(
                name: "MaterialPricePlanBrackets",
                schema: "store");

            migrationBuilder.DropTable(
                name: "Materials",
                schema: "store");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "store");

            migrationBuilder.DropTable(
                name: "ToolPricePlanBrackets",
                schema: "store");

            migrationBuilder.DropTable(
                name: "Units",
                schema: "store");

            migrationBuilder.DropTable(
                name: "Tools",
                schema: "store");

            migrationBuilder.DropTable(
                name: "GeneralSupportPricePlans",
                schema: "store");

            migrationBuilder.DropTable(
                name: "MaterialPricePlans",
                schema: "store");

            migrationBuilder.DropTable(
                name: "ToolPricePlans",
                schema: "store");
        }
    }
}
