using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contracts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Contracts_Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "contracts");

            migrationBuilder.CreateTable(
                name: "Contracts",
                schema: "contracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Customer_CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Customer_IataCode = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    Customer_Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Currency_CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Currency_Code = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Period_StartDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Period_ExpiryDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Period_ExpiryAlertDays = table.Column<int>(type: "int", nullable: false),
                    Period_ExpiryAlertInterval = table.Column<int>(type: "int", nullable: true),
                    PaymentTerms = table.Column<int>(type: "int", nullable: false),
                    ApplyVat = table.Column<bool>(type: "bit", nullable: false),
                    DebriefRequired = table.Column<bool>(type: "bit", nullable: false),
                    Attachment = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    Fees_Admin_Type = table.Column<int>(type: "int", nullable: false),
                    Fees_Admin_Value = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Fees_Disbursement_Type = table.Column<int>(type: "int", nullable: false),
                    Fees_Disbursement_Value = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Fees_Holiday_Type = table.Column<int>(type: "int", nullable: false),
                    Fees_Holiday_Value = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Fees_Night_Type = table.Column<int>(type: "int", nullable: false),
                    Fees_Night_Value = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Fees_ReturnToRamp_Type = table.Column<int>(type: "int", nullable: false),
                    Fees_ReturnToRamp_Value = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Fees_Other_Type = table.Column<int>(type: "int", nullable: false),
                    Fees_Other_Value = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CancellationBasis = table.Column<int>(type: "int", nullable: false),
                    CancellationChargeType = table.Column<int>(type: "int", nullable: false),
                    DelayBasis = table.Column<int>(type: "int", nullable: false),
                    DelayChargeType = table.Column<int>(type: "int", nullable: false),
                    DelayType = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Termination_Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Termination_AtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Termination_ByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastExpiringSoonNotificationAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contracts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InboxMessages",
                schema: "contracts",
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
                name: "OutboxMessages",
                schema: "contracts",
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
                name: "CancellationBrackets",
                schema: "contracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MinMinutes = table.Column<int>(type: "int", nullable: false),
                    MaxMinutes = table.Column<int>(type: "int", nullable: true),
                    Value = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CancellationBrackets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CancellationBrackets_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalSchema: "contracts",
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContractAdvancePayments",
                schema: "contracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationType_OperationTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationType_Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FlightsCount = table.Column<int>(type: "int", nullable: false),
                    FlightCost_Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Balance_Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Deposit_Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RemainingBalance_Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RemainingDeposit_Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractAdvancePayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractAdvancePayments_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalSchema: "contracts",
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContractGeneralSupports",
                schema: "contracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationType_OperationTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationType_Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    GeneralSupport_GeneralSupportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GeneralSupport_Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Basis = table.Column<int>(type: "int", nullable: false),
                    PackagePaidBalance_Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    PackageRemainingBalance_Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractGeneralSupports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractGeneralSupports_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalSchema: "contracts",
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContractManpowers",
                schema: "contracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationType_OperationTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationType_Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ManpowerType_ManpowerTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ManpowerType_Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Basis = table.Column<int>(type: "int", nullable: false),
                    PackagePaidBalance_Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    PackageRemainingBalance_Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractManpowers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractManpowers_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalSchema: "contracts",
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContractMaterials",
                schema: "contracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationType_OperationTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationType_Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Material_MaterialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Material_Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Basis = table.Column<int>(type: "int", nullable: false),
                    PackagePaidBalance_Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    PackageRemainingBalance_Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractMaterials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractMaterials_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalSchema: "contracts",
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContractOperationTypes",
                schema: "contracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationType_OperationTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationType_Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractOperationTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractOperationTypes_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalSchema: "contracts",
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContractServices",
                schema: "contracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationType_OperationTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationType_Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Service_ServiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Service_Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Service_IsAog = table.Column<bool>(type: "bit", nullable: false),
                    AircraftType_AircraftTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AircraftType_Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Basis = table.Column<int>(type: "int", nullable: false),
                    PackagePaidBalance_Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    PackageRemainingBalance_Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractServices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractServices_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalSchema: "contracts",
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContractStations",
                schema: "contracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Station_StationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Station_IataCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Station_Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractStations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractStations_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalSchema: "contracts",
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContractTools",
                schema: "contracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationType_OperationTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationType_Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Tool_ToolId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tool_Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AircraftType_AircraftTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AircraftType_Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Basis = table.Column<int>(type: "int", nullable: false),
                    PackagePaidBalance_Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    PackageRemainingBalance_Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractTools", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractTools_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalSchema: "contracts",
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DelayBrackets",
                schema: "contracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MinMinutes = table.Column<int>(type: "int", nullable: false),
                    MaxMinutes = table.Column<int>(type: "int", nullable: true),
                    Value = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DelayBrackets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DelayBrackets_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalSchema: "contracts",
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContractGeneralSupportBrackets",
                schema: "contracts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MinMinutes = table.Column<int>(type: "int", nullable: false),
                    MaxMinutes = table.Column<int>(type: "int", nullable: true),
                    BlockSize = table.Column<int>(type: "int", nullable: false),
                    PriceValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PackagePriceValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    BillingMode = table.Column<int>(type: "int", nullable: false),
                    ContractGeneralSupportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractGeneralSupportBrackets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractGeneralSupportBrackets_ContractGeneralSupports_ContractGeneralSupportId",
                        column: x => x.ContractGeneralSupportId,
                        principalSchema: "contracts",
                        principalTable: "ContractGeneralSupports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContractManpowerBrackets",
                schema: "contracts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MinMinutes = table.Column<int>(type: "int", nullable: false),
                    MaxMinutes = table.Column<int>(type: "int", nullable: true),
                    BlockSize = table.Column<int>(type: "int", nullable: false),
                    PriceValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PackagePriceValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    BillingMode = table.Column<int>(type: "int", nullable: false),
                    ContractManpowerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractManpowerBrackets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractManpowerBrackets_ContractManpowers_ContractManpowerId",
                        column: x => x.ContractManpowerId,
                        principalSchema: "contracts",
                        principalTable: "ContractManpowers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContractMaterialBrackets",
                schema: "contracts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MinMinutes = table.Column<int>(type: "int", nullable: false),
                    MaxMinutes = table.Column<int>(type: "int", nullable: true),
                    BlockSize = table.Column<int>(type: "int", nullable: false),
                    PriceValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PackagePriceValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    BillingMode = table.Column<int>(type: "int", nullable: false),
                    ContractMaterialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractMaterialBrackets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractMaterialBrackets_ContractMaterials_ContractMaterialId",
                        column: x => x.ContractMaterialId,
                        principalSchema: "contracts",
                        principalTable: "ContractMaterials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContractOperationTypeServices",
                schema: "contracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsAog = table.Column<bool>(type: "bit", nullable: false),
                    ContractOperationTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractOperationTypeServices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractOperationTypeServices_ContractOperationTypes_ContractOperationTypeId",
                        column: x => x.ContractOperationTypeId,
                        principalSchema: "contracts",
                        principalTable: "ContractOperationTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContractServiceBrackets",
                schema: "contracts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MinMinutes = table.Column<int>(type: "int", nullable: false),
                    MaxMinutes = table.Column<int>(type: "int", nullable: true),
                    BlockSize = table.Column<int>(type: "int", nullable: false),
                    PriceValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PackagePriceValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    BillingMode = table.Column<int>(type: "int", nullable: false),
                    ContractServiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractServiceBrackets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractServiceBrackets_ContractServices_ContractServiceId",
                        column: x => x.ContractServiceId,
                        principalSchema: "contracts",
                        principalTable: "ContractServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContractToolBrackets",
                schema: "contracts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MinMinutes = table.Column<int>(type: "int", nullable: false),
                    MaxMinutes = table.Column<int>(type: "int", nullable: true),
                    BlockSize = table.Column<int>(type: "int", nullable: false),
                    PriceValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PackagePriceValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    BillingMode = table.Column<int>(type: "int", nullable: false),
                    ContractToolId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractToolBrackets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractToolBrackets_ContractTools_ContractToolId",
                        column: x => x.ContractToolId,
                        principalSchema: "contracts",
                        principalTable: "ContractTools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CancellationBrackets_ContractId_SortOrder",
                schema: "contracts",
                table: "CancellationBrackets",
                columns: new[] { "ContractId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "UX_ContractAdvancePayments_Contract_OperationType",
                schema: "contracts",
                table: "ContractAdvancePayments",
                columns: new[] { "ContractId", "OperationTypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractGeneralSupportBrackets_ContractGeneralSupportId",
                schema: "contracts",
                table: "ContractGeneralSupportBrackets",
                column: "ContractGeneralSupportId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractGeneralSupports_ContractId_OperationTypeId",
                schema: "contracts",
                table: "ContractGeneralSupports",
                columns: new[] { "ContractId", "OperationTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContractManpowerBrackets_ContractManpowerId",
                schema: "contracts",
                table: "ContractManpowerBrackets",
                column: "ContractManpowerId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractManpowers_ContractId_OperationTypeId",
                schema: "contracts",
                table: "ContractManpowers",
                columns: new[] { "ContractId", "OperationTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContractMaterialBrackets_ContractMaterialId",
                schema: "contracts",
                table: "ContractMaterialBrackets",
                column: "ContractMaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractMaterials_ContractId_OperationTypeId",
                schema: "contracts",
                table: "ContractMaterials",
                columns: new[] { "ContractId", "OperationTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContractOperationTypes_ContractId",
                schema: "contracts",
                table: "ContractOperationTypes",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractOperationTypeServices_ContractOperationTypeId",
                schema: "contracts",
                table: "ContractOperationTypeServices",
                column: "ContractOperationTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_ContractNo",
                schema: "contracts",
                table: "Contracts",
                column: "ContractNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_CreatedAt",
                schema: "contracts",
                table: "Contracts",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_CurrencyId",
                schema: "contracts",
                table: "Contracts",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_CustomerId",
                schema: "contracts",
                table: "Contracts",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_Status",
                schema: "contracts",
                table: "Contracts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_UpdatedAt",
                schema: "contracts",
                table: "Contracts",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ContractServiceBrackets_ContractServiceId",
                schema: "contracts",
                table: "ContractServiceBrackets",
                column: "ContractServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractServices_ContractId_OperationTypeId",
                schema: "contracts",
                table: "ContractServices",
                columns: new[] { "ContractId", "OperationTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContractStations_ContractId",
                schema: "contracts",
                table: "ContractStations",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractToolBrackets_ContractToolId",
                schema: "contracts",
                table: "ContractToolBrackets",
                column: "ContractToolId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractTools_ContractId_OperationTypeId",
                schema: "contracts",
                table: "ContractTools",
                columns: new[] { "ContractId", "OperationTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_DelayBrackets_ContractId_SortOrder",
                schema: "contracts",
                table: "DelayBrackets",
                columns: new[] { "ContractId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CancellationBrackets",
                schema: "contracts");

            migrationBuilder.DropTable(
                name: "ContractAdvancePayments",
                schema: "contracts");

            migrationBuilder.DropTable(
                name: "ContractGeneralSupportBrackets",
                schema: "contracts");

            migrationBuilder.DropTable(
                name: "ContractManpowerBrackets",
                schema: "contracts");

            migrationBuilder.DropTable(
                name: "ContractMaterialBrackets",
                schema: "contracts");

            migrationBuilder.DropTable(
                name: "ContractOperationTypeServices",
                schema: "contracts");

            migrationBuilder.DropTable(
                name: "ContractServiceBrackets",
                schema: "contracts");

            migrationBuilder.DropTable(
                name: "ContractStations",
                schema: "contracts");

            migrationBuilder.DropTable(
                name: "ContractToolBrackets",
                schema: "contracts");

            migrationBuilder.DropTable(
                name: "DelayBrackets",
                schema: "contracts");

            migrationBuilder.DropTable(
                name: "InboxMessages",
                schema: "contracts");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "contracts");

            migrationBuilder.DropTable(
                name: "ContractGeneralSupports",
                schema: "contracts");

            migrationBuilder.DropTable(
                name: "ContractManpowers",
                schema: "contracts");

            migrationBuilder.DropTable(
                name: "ContractMaterials",
                schema: "contracts");

            migrationBuilder.DropTable(
                name: "ContractOperationTypes",
                schema: "contracts");

            migrationBuilder.DropTable(
                name: "ContractServices",
                schema: "contracts");

            migrationBuilder.DropTable(
                name: "ContractTools",
                schema: "contracts");

            migrationBuilder.DropTable(
                name: "Contracts",
                schema: "contracts");
        }
    }
}
