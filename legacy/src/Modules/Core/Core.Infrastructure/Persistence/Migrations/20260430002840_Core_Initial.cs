using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Core_Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "core");

            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "AircraftTypes",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Manufacturer = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AircraftTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditTrails",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Action = table.Column<int>(type: "int", nullable: false),
                    OldValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditTrails", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Countries",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Countries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Currencies",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Currencies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Customers",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IataCode = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    IcaoCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CountryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OfficialEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    OfficialPhone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Logo = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    Address_Line1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Address_Line2 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Address_City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Address_PostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Address_CountryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Employees",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Logo = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    ManpowerTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Contract_From = table.Column<DateOnly>(type: "date", nullable: false),
                    Contract_To = table.Column<DateOnly>(type: "date", nullable: true),
                    WorkingSchedule = table.Column<int>(type: "int", nullable: false),
                    LinkedUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InboxMessages",
                schema: "core",
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
                name: "Licenses",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Licenses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ManpowerPricePlans",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ManpowerTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Basis = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManpowerPricePlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ManpowerTypes",
                schema: "core",
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
                    table.PrimaryKey("PK_ManpowerTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OperationTypes",
                schema: "core",
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
                    table.PrimaryKey("PK_OperationTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "core",
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
                name: "ServicePricePlans",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AircraftTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Basis = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicePricePlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Services",
                schema: "core",
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
                    table.PrimaryKey("PK_Services", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Stations",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IataCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CountryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExchangeRates",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToCurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExchangeRates_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalSchema: "core",
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomerContacts",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    JobTitle = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    LinkedUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerContacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerContacts_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "core",
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeLicenses",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LicenseNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeLicenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeLicenses_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalSchema: "core",
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ManpowerPricePlanBrackets",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MinMinutes = table.Column<int>(type: "int", nullable: false),
                    MaxMinutes = table.Column<int>(type: "int", nullable: true),
                    BlockSize = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    BillingMode = table.Column<int>(type: "int", nullable: false),
                    ManpowerPricePlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManpowerPricePlanBrackets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManpowerPricePlanBrackets_ManpowerPricePlans_ManpowerPricePlanId",
                        column: x => x.ManpowerPricePlanId,
                        principalSchema: "core",
                        principalTable: "ManpowerPricePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ServicePricePlanBrackets",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MinMinutes = table.Column<int>(type: "int", nullable: false),
                    MaxMinutes = table.Column<int>(type: "int", nullable: true),
                    BlockSize = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    BillingMode = table.Column<int>(type: "int", nullable: false),
                    ServicePricePlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicePricePlanBrackets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServicePricePlanBrackets_ServicePricePlans_ServicePricePlanId",
                        column: x => x.ServicePricePlanId,
                        principalSchema: "core",
                        principalTable: "ServicePricePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AircraftTypes_CreatedAt",
                schema: "core",
                table: "AircraftTypes",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AircraftTypes_IsActive",
                schema: "core",
                table: "AircraftTypes",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_AircraftTypes_Manufacturer_Model",
                schema: "core",
                table: "AircraftTypes",
                columns: new[] { "Manufacturer", "Model" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AircraftTypes_UpdatedAt",
                schema: "core",
                table: "AircraftTypes",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Countries_Code",
                schema: "core",
                table: "Countries",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Countries_CreatedAt",
                schema: "core",
                table: "Countries",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Countries_Name",
                schema: "core",
                table: "Countries",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Currencies_Code",
                schema: "core",
                table: "Currencies",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Currencies_CreatedAt",
                schema: "core",
                table: "Currencies",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Currencies_IsActive",
                schema: "core",
                table: "Currencies",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Currencies_Name",
                schema: "core",
                table: "Currencies",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Currencies_UpdatedAt",
                schema: "core",
                table: "Currencies",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerContacts_CreatedAt",
                schema: "core",
                table: "CustomerContacts",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerContacts_CustomerId",
                schema: "core",
                table: "CustomerContacts",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerContacts_CustomerId_Email",
                schema: "core",
                table: "CustomerContacts",
                columns: new[] { "CustomerId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerContacts_Email",
                schema: "core",
                table: "CustomerContacts",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerContacts_Name",
                schema: "core",
                table: "CustomerContacts",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CountryId",
                schema: "core",
                table: "Customers",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CreatedAt",
                schema: "core",
                table: "Customers",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_IataCode",
                schema: "core",
                table: "Customers",
                column: "IataCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_IcaoCode",
                schema: "core",
                table: "Customers",
                column: "IcaoCode");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_IsActive",
                schema: "core",
                table: "Customers",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_Name",
                schema: "core",
                table: "Customers",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_UpdatedAt",
                schema: "core",
                table: "Customers",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeLicenses_EmployeeId_LicenseId",
                schema: "core",
                table: "EmployeeLicenses",
                columns: new[] { "EmployeeId", "LicenseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeLicenses_LicenseNumber",
                schema: "core",
                table: "EmployeeLicenses",
                column: "LicenseNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_CreatedAt",
                schema: "core",
                table: "Employees",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_Email",
                schema: "core",
                table: "Employees",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Employees_FullName",
                schema: "core",
                table: "Employees",
                column: "FullName");

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_CreatedAt",
                schema: "core",
                table: "ExchangeRates",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_CurrencyId_ToCurrencyId",
                schema: "core",
                table: "ExchangeRates",
                columns: new[] { "CurrencyId", "ToCurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_ToCurrencyId",
                schema: "core",
                table: "ExchangeRates",
                column: "ToCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_UpdatedAt",
                schema: "core",
                table: "ExchangeRates",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Licenses_Code",
                schema: "core",
                table: "Licenses",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Licenses_CreatedAt",
                schema: "core",
                table: "Licenses",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Licenses_Name",
                schema: "core",
                table: "Licenses",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_ManpowerPricePlanBrackets_ManpowerPricePlanId",
                schema: "core",
                table: "ManpowerPricePlanBrackets",
                column: "ManpowerPricePlanId");

            migrationBuilder.CreateIndex(
                name: "IX_ManpowerPricePlans_CreatedAt",
                schema: "core",
                table: "ManpowerPricePlans",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ManpowerPricePlans_CurrencyId",
                schema: "core",
                table: "ManpowerPricePlans",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_ManpowerPricePlans_ManpowerTypeId_OperationTypeId",
                schema: "core",
                table: "ManpowerPricePlans",
                columns: new[] { "ManpowerTypeId", "OperationTypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManpowerPricePlans_UpdatedAt",
                schema: "core",
                table: "ManpowerPricePlans",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ManpowerTypes_CreatedAt",
                schema: "core",
                table: "ManpowerTypes",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ManpowerTypes_IsActive",
                schema: "core",
                table: "ManpowerTypes",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_ManpowerTypes_Name",
                schema: "core",
                table: "ManpowerTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManpowerTypes_UpdatedAt",
                schema: "core",
                table: "ManpowerTypes",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_OperationTypes_CreatedAt",
                schema: "core",
                table: "OperationTypes",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_OperationTypes_IsActive",
                schema: "core",
                table: "OperationTypes",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_OperationTypes_Name",
                schema: "core",
                table: "OperationTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperationTypes_UpdatedAt",
                schema: "core",
                table: "OperationTypes",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ServicePricePlanBrackets_ServicePricePlanId",
                schema: "core",
                table: "ServicePricePlanBrackets",
                column: "ServicePricePlanId");

            migrationBuilder.CreateIndex(
                name: "IX_ServicePricePlans_AircraftTypeId",
                schema: "core",
                table: "ServicePricePlans",
                column: "AircraftTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ServicePricePlans_CreatedAt",
                schema: "core",
                table: "ServicePricePlans",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ServicePricePlans_CurrencyId",
                schema: "core",
                table: "ServicePricePlans",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_ServicePricePlans_Service_Operation_Aircraft",
                schema: "core",
                table: "ServicePricePlans",
                columns: new[] { "ServiceId", "OperationTypeId", "AircraftTypeId" },
                unique: true,
                filter: "[AircraftTypeId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ServicePricePlans_Service_Operation_NullAircraft",
                schema: "core",
                table: "ServicePricePlans",
                columns: new[] { "ServiceId", "OperationTypeId" },
                unique: true,
                filter: "[AircraftTypeId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ServicePricePlans_UpdatedAt",
                schema: "core",
                table: "ServicePricePlans",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Services_CreatedAt",
                schema: "core",
                table: "Services",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Services_IsActive",
                schema: "core",
                table: "Services",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Services_Name",
                schema: "core",
                table: "Services",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Services_UpdatedAt",
                schema: "core",
                table: "Services",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Stations_City",
                schema: "core",
                table: "Stations",
                column: "City");

            migrationBuilder.CreateIndex(
                name: "IX_Stations_CountryId",
                schema: "core",
                table: "Stations",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_Stations_CreatedAt",
                schema: "core",
                table: "Stations",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Stations_IataCode",
                schema: "core",
                table: "Stations",
                column: "IataCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stations_IsActive",
                schema: "core",
                table: "Stations",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Stations_Name",
                schema: "core",
                table: "Stations",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Stations_UpdatedAt",
                schema: "core",
                table: "Stations",
                column: "UpdatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AircraftTypes",
                schema: "core");

            migrationBuilder.DropTable(
                name: "AuditTrails",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "Countries",
                schema: "core");

            migrationBuilder.DropTable(
                name: "CustomerContacts",
                schema: "core");

            migrationBuilder.DropTable(
                name: "EmployeeLicenses",
                schema: "core");

            migrationBuilder.DropTable(
                name: "ExchangeRates",
                schema: "core");

            migrationBuilder.DropTable(
                name: "InboxMessages",
                schema: "core");

            migrationBuilder.DropTable(
                name: "Licenses",
                schema: "core");

            migrationBuilder.DropTable(
                name: "ManpowerPricePlanBrackets",
                schema: "core");

            migrationBuilder.DropTable(
                name: "ManpowerTypes",
                schema: "core");

            migrationBuilder.DropTable(
                name: "OperationTypes",
                schema: "core");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "core");

            migrationBuilder.DropTable(
                name: "ServicePricePlanBrackets",
                schema: "core");

            migrationBuilder.DropTable(
                name: "Services",
                schema: "core");

            migrationBuilder.DropTable(
                name: "Stations",
                schema: "core");

            migrationBuilder.DropTable(
                name: "Customers",
                schema: "core");

            migrationBuilder.DropTable(
                name: "Employees",
                schema: "core");

            migrationBuilder.DropTable(
                name: "Currencies",
                schema: "core");

            migrationBuilder.DropTable(
                name: "ManpowerPricePlans",
                schema: "core");

            migrationBuilder.DropTable(
                name: "ServicePricePlans",
                schema: "core");
        }
    }
}
