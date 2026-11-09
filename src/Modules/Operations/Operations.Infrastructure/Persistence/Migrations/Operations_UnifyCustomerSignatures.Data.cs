using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Operations.Infrastructure.Persistence.Migrations;

public partial class Operations_UnifyCustomerSignatures
{
    // Keep legacy data preparation separate from the generated schema operations.
    public override IReadOnlyList<MigrationOperation> UpOperations =>
    [
        new SqlOperation
        {
            Sql = """
                IF EXISTS (SELECT 1 FROM [operations].[flight_departure_submissions]
                    WHERE DATALENGTH([CustomerSignatureStorageKey]) > 1000
                       OR DATALENGTH([CustomerSignatureContentType]) > 200)
                    THROW 51000, 'CustomerSignature unification: resolve storage keys over 500 characters or content types over 100 before migrating; existing file references were not truncated.', 1;

                IF EXISTS (SELECT 1 FROM [operations].[flight_departure_submissions]
                    WHERE [CreatedAtUtc] = CONVERT(datetimeoffset, N'0001-01-01T00:00:00+00:00'))
                    THROW 51000, 'CustomerSignature unification: resolve default submission timestamps before migrating; a valid signing-time fallback is required.', 1;
                """
        },
        .. base.UpOperations,
        new SqlOperation
        {
            Sql = """
                -- The old normal-departure schema did not capture either of these values.
                -- Use the approved display-name and submission-recording-time fallbacks;
                -- the actual historical filename and signing time cannot be recovered.
                UPDATE d
                SET [CustomerSignatureFileName] = COALESCE(
                        NULLIF(LEFT(RIGHT(p.[Path], CHARINDEX(N'/', REVERSE(p.[Path]) + N'/') - 1), 255), N''),
                        N'customer-signature'),
                    [CustomerSignedAtUtc] = SWITCHOFFSET(d.[CreatedAtUtc], '+00:00')
                FROM [operations].[flight_departure_submissions] AS d
                CROSS APPLY (SELECT REPLACE(LTRIM(RTRIM(d.[CustomerSignatureStorageKey])), N'\', N'/') AS [Path]) AS p;
                """
        }
    ];
}
