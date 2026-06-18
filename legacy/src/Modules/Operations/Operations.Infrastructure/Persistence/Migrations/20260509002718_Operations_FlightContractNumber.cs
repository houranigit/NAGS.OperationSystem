using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Operations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Operations_FlightContractNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContractNumber",
                schema: "operations",
                table: "Flights",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            // Backfill: every existing scheduled flight already carries a ContractId
            // pointing at contracts.Contracts; copy the contract's ContractNo onto the
            // flight so the new column is populated end-to-end before the application
            // starts writing it. Ad-hoc flights have ContractId = NULL and stay NULL.
            // Idempotent — running the migration twice is a no-op once values are set.
            migrationBuilder.Sql(@"
                UPDATE f
                   SET f.ContractNumber = c.ContractNo
                  FROM operations.Flights f
                  INNER JOIN contracts.Contracts c ON c.Id = f.ContractId
                 WHERE f.ContractId IS NOT NULL
                   AND f.ContractNumber IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContractNumber",
                schema: "operations",
                table: "Flights");
        }
    }
}
