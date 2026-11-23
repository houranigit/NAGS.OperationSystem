using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Operations.Infrastructure.Persistence.Migrations;

public partial class Operations_AdministratorFlightCancellation
{
    public override IReadOnlyList<MigrationOperation> DownOperations =>
    [
        new SqlOperation
        {
            Sql = """
                IF EXISTS (SELECT 1 FROM [operations].[flight_cancellation_submissions] WHERE [SubmittedByStaffMemberId] IS NULL)
                    THROW 51000, 'Cannot downgrade while administrator-entered flight cancellations exist because the previous schema requires an employee submitter.', 1;
                """
        },
        .. base.DownOperations
    ];
}
