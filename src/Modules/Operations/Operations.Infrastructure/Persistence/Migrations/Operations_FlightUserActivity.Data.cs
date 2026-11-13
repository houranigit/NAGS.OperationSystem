using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Operations.Infrastructure.Persistence.Migrations;

public partial class Operations_FlightUserActivity
{
    // Retained creator/latest-updater evidence cannot reconstruct deleted records or intermediate actors.
    // EndedByUserId has no separate recording timestamp; operational End/ATD instants are not interaction times.
    public override IReadOnlyList<MigrationOperation> UpOperations =>
    [
        .. base.UpOperations,
        new SqlOperation
        {
            Sql = """
                WITH [Interactions] AS
                (
                    SELECT [CreatedByUserId] AS [UserId], [FlightId], [CreatedAtUtc] AS [OccurredAtUtc]
                    FROM [operations].[flight_arrival_submissions]
                    UNION ALL SELECT [UpdatedByUserId], [FlightId], [UpdatedAtUtc] FROM [operations].[flight_arrival_submissions]
                    UNION ALL SELECT [CreatedByUserId], [FlightId], [CreatedAtUtc] FROM [operations].[flight_departure_submissions]
                    UNION ALL SELECT [UpdatedByUserId], [FlightId], [UpdatedAtUtc] FROM [operations].[flight_departure_submissions]
                    UNION ALL SELECT [CreatedByUserId], [FlightId], [CreatedAtUtc] FROM [operations].[flight_services]
                    UNION ALL SELECT [UpdatedByUserId], [FlightId], [UpdatedAtUtc] FROM [operations].[flight_services]
                    UNION ALL SELECT [CreatedByUserId], [FlightId], [CreatedAtUtc] FROM [operations].[flight_tasks]
                    UNION ALL SELECT [UpdatedByUserId], [FlightId], [UpdatedAtUtc] FROM [operations].[flight_tasks]
                    UNION ALL SELECT [CreatedByUserId], [FlightId], [CreatedAtUtc] FROM [operations].[flight_cancellation_submissions]
                    UNION ALL SELECT [UpdatedByUserId], [FlightId], [UpdatedAtUtc] FROM [operations].[flight_cancellation_submissions]
                    UNION ALL SELECT [CreatedByUserId], [FlightId], [CreatedAtUtc] FROM [operations].[flight_return_to_ramps]
                    UNION ALL SELECT [UpdatedByUserId], [FlightId], [UpdatedAtUtc] FROM [operations].[flight_return_to_ramps]
                    UNION ALL SELECT [CreatedByUserId], [Id], [CreatedAtUtc] FROM [operations].[flights]
                        WHERE [OperationTypeId] = '30000000-0000-0000-0000-000000000001'
                )
                INSERT INTO [operations].[flight_user_activities] ([UserId], [FlightId], [LastInteractionAtUtc], [UsedSearch])
                SELECT [UserId], [FlightId], MAX(SWITCHOFFSET([OccurredAtUtc], '+00:00')), CAST(0 AS bit)
                FROM [Interactions]
                WHERE [UserId] IS NOT NULL AND [UserId] <> '00000000-0000-0000-0000-000000000000'
                    AND [OccurredAtUtc] IS NOT NULL
                    AND [OccurredAtUtc] > CONVERT(datetimeoffset, N'0001-01-01T00:00:00+00:00')
                GROUP BY [UserId], [FlightId];
                """
        }
    ];
}
