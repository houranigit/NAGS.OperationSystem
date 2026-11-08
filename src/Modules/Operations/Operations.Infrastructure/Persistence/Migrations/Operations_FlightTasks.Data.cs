using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Operations.Infrastructure.Persistence.Migrations;

public partial class Operations_FlightTasks
{
    // Keep data preparation separate from the EF-generated schema operations.
    public override IReadOnlyList<MigrationOperation> UpOperations =>
    [
        new SqlOperation
        {
            Sql = """
                IF EXISTS (SELECT 1 FROM [operations].[flight_services]
                    WHERE DATALENGTH([CustomerReferenceNumber]) > 400 OR DATALENGTH([Notes]) > 4000)
                    THROW 51000, 'FlightServices parity: resolve references over 200 characters or notes over 2000 before migrating; existing data was not truncated.', 1;

                IF EXISTS (SELECT 1 FROM [operations].[flight_services]
                    WHERE [ServiceId] = '40000000-0000-0000-0000-000000000001')
                    THROW 51000, 'FlightServices parity: resolve Aircraft Per Landing records stored as actual services before migrating.', 1;

                IF EXISTS (SELECT 1 FROM [operations].[flight_services]
                    WHERE [ServiceId] = '40000000-0000-0000-0000-000000000003'
                      AND NULLIF(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE([Notes], CHAR(9), ''), CHAR(10), ''), CHAR(13), ''))), '') IS NULL)
                    THROW 51000, 'FlightServices parity: add descriptions to existing Unknown service records before migrating.', 1;

                IF EXISTS (SELECT 1 FROM [operations].[flight_service_performers]
                    GROUP BY [FlightServiceId], [StaffMemberId] HAVING COUNT(*) > 1)
                    THROW 51000, 'FlightServices parity: resolve duplicate employee periods before migrating; no employee records were deleted.', 1;

                IF EXISTS (SELECT 1 FROM [operations].[flight_service_performers] AS p
                    JOIN [operations].[flight_services] AS s ON s.[Id] = p.[FlightServiceId]
                    WHERE p.[FromUtc] < s.[FromUtc]
                       OR (s.[ToUtc] IS NOT NULL AND (p.[FromUtc] > s.[ToUtc] OR p.[ToUtc] > s.[ToUtc])))
                    THROW 51000, 'FlightServices parity: resolve employee periods outside their service before migrating.', 1;

                IF EXISTS (SELECT 1 FROM [operations].[flight_service_attachments]
                    GROUP BY [FlightServiceId] HAVING COUNT(*) > 10)
                    THROW 51000, 'FlightServices parity: resolve services with more than 10 attachments before migrating; no attachments were deleted.', 1;

                IF EXISTS (SELECT 1 FROM [operations].[flight_service_attachments]
                    WHERE DATALENGTH([StorageKey]) > 1000 OR DATALENGTH([ContentType]) > 200)
                    THROW 51000, 'FlightServices parity: resolve storage keys over 500 characters or content types over 100 before migrating; file references were not truncated.', 1;

                -- Normalize display names only; keep storage keys and file content unchanged.
                IF EXISTS (SELECT 1 FROM [operations].[flight_service_attachments] AS a
                    CROSS APPLY (SELECT REPLACE(LTRIM(RTRIM(a.[FileName])), N'\', N'/') AS [Path]) AS p
                    WHERE RIGHT(p.[Path], CHARINDEX(N'/', REVERSE(p.[Path]) + N'/') - 1) = N'')
                    THROW 51000, 'FlightServices parity: resolve attachment names without a file basename before migrating.', 1;

                UPDATE a SET [FileName] = LEFT(RIGHT(p.[Path], CHARINDEX(N'/', REVERSE(p.[Path]) + N'/') - 1), 255)
                FROM [operations].[flight_service_attachments] AS a
                CROSS APPLY (SELECT REPLACE(LTRIM(RTRIM(a.[FileName])), N'\', N'/') AS [Path]) AS p;
                """
        },
        .. base.UpOperations,
        new SqlOperation
        {
            Sql = """
                -- Existing metadata predates Kind; classify without replacing any attachment.
                UPDATE [operations].[flight_service_attachments]
                SET [Kind] = CASE
                    WHEN LOWER(LTRIM(RTRIM([ContentType]))) LIKE N'image/%' THEN 0
                    WHEN LOWER(LTRIM(RTRIM([ContentType]))) LIKE N'audio/%' THEN 1
                    ELSE 2
                END;
                """
        }
    ];
}
