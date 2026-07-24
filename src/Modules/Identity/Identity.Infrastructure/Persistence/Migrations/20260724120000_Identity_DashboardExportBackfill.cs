using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Identity.Infrastructure.Persistence.Migrations;

/// <summary>
/// Preserves the pre-existing dashboard export capability when export authorization is separated
/// from analytics page access. Only roles that already had analytics access are changed, and their
/// users' security stamps are rotated and active sessions are revoked so the next login receives
/// the new permission claim.
/// </summary>
[DbContext(typeof(IdentityDbContext))]
[Migration("20260724120000_Identity_DashboardExportBackfill")]
public sealed class Identity_DashboardExportBackfill : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @AffectedRoles TABLE
            (
                [Id] uniqueidentifier NOT NULL PRIMARY KEY
            );

            UPDATE [identity].[roles]
            SET [Permissions] = JSON_MODIFY(
                    [Permissions],
                    'append $',
                    N'operations.dashboard.export'),
                [UpdatedAtUtc] = SYSUTCDATETIME()
            OUTPUT inserted.[Id] INTO @AffectedRoles ([Id])
            WHERE ISJSON([Permissions]) = 1
              AND EXISTS
              (
                  SELECT 1
                  FROM OPENJSON([Permissions]) AS [permission]
                  WHERE [permission].[value] = N'operations.dashboard.view-analytics'
              )
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM OPENJSON([Permissions]) AS [permission]
                  WHERE [permission].[value] = N'operations.dashboard.export'
              );

            UPDATE [user]
            SET [SecurityStamp] = NEWID(),
                [UpdatedAtUtc] = SYSUTCDATETIME()
            FROM [identity].[users] AS [user]
            INNER JOIN @AffectedRoles AS [role]
                ON [role].[Id] = [user].[RoleId];

            UPDATE [session]
            SET [RevokedAtUtc] = SYSUTCDATETIME()
            FROM [identity].[user_sessions] AS [session]
            INNER JOIN [identity].[users] AS [user]
                ON [user].[Id] = [session].[UserId]
            INNER JOIN @AffectedRoles AS [role]
                ON [role].[Id] = [user].[RoleId]
            WHERE [session].[RevokedAtUtc] IS NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Intentionally irreversible: after deployment the new export permission may have been
        // granted explicitly, and session revocation cannot be safely undone.
    }
}
