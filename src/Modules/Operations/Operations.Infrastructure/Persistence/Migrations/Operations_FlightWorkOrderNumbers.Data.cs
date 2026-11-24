using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Operations.Infrastructure.Persistence.Migrations;

public partial class Operations_FlightWorkOrderNumbers
{
    private const string ApprovalSequenceIndex = "UX_flights_StationId_ApprovalSequence";

    public override IReadOnlyList<MigrationOperation> UpOperations =>
    [
        .. base.UpOperations,
        new SqlOperation
        {
            // Defer binding the new columns until this migration runs in an idempotent script.
            Sql = """
                EXEC(N'
                    WITH [NumberedFlights] AS
                    (
                        SELECT [Id], ROW_NUMBER() OVER (PARTITION BY [StationId] ORDER BY [ApprovedAtUtc], [Id]) AS [Sequence]
                        FROM [operations].[flights]
                        WHERE [ApprovalStatus] IN (1, 3)
                    )
                    UPDATE [flight]
                    SET [ApprovalSequence] = [numbered].[Sequence],
                        [ApprovalNumber] = UPPER(LTRIM(RTRIM([flight].[StationIataCode]))) + N''-''
                            + CASE WHEN [numbered].[Sequence] < 1000
                                THEN RIGHT(N''0000'' + CONVERT(nvarchar(10), [numbered].[Sequence]), 4)
                                ELSE CONVERT(nvarchar(10), [numbered].[Sequence])
                              END
                    FROM [operations].[flights] AS [flight]
                    INNER JOIN [NumberedFlights] AS [numbered] ON [numbered].[Id] = [flight].[Id];
                ');
                """
        },
        // The station ID is mapped through the table-split station snapshot.
        new CreateIndexOperation
        {
            Name = ApprovalSequenceIndex,
            Schema = "operations",
            Table = "flights",
            Columns = ["StationId", "ApprovalSequence"],
            IsUnique = true,
            Filter = "[ApprovalSequence] IS NOT NULL"
        }
    ];

    public override IReadOnlyList<MigrationOperation> DownOperations =>
    [
        new DropIndexOperation { Name = ApprovalSequenceIndex, Schema = "operations", Table = "flights" },
        .. base.DownOperations
    ];
}
