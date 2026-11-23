using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Operations.Infrastructure.Persistence.Migrations;

public partial class Operations_AdministratorFlightActuals
{
    public override IReadOnlyList<MigrationOperation> DownOperations =>
    [
        new SqlOperation
        {
            Sql = """
                IF EXISTS (SELECT 1 FROM [operations].[flight_arrival_submissions] WHERE [SubmittedByStaffMemberId] IS NULL)
                   OR EXISTS (SELECT 1 FROM [operations].[flight_departure_submissions] WHERE [SubmittedByStaffMemberId] IS NULL)
                    THROW 51000, 'Cannot downgrade while administrator-entered flight actuals exist because the previous schema requires an employee submitter.', 1;
                """
        },
        .. base.DownOperations
    ];
}
