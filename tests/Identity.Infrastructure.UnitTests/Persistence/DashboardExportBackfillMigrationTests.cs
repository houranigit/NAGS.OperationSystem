using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;

namespace Identity.Infrastructure.UnitTests.Persistence;

public sealed class DashboardExportBackfillMigrationTests
{
    [Fact]
    public void Migration_is_discoverable_and_backfills_roles_before_revoking_their_sessions()
    {
        using var db = new IdentityDbContext(
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseSqlServer(
                    "Server=localhost;Database=identity-migration-script;User Id=sa;Password=NotUsed1!;TrustServerCertificate=true")
                .Options);

        const string migrationId = "20260724120000_Identity_DashboardExportBackfill";
        db.Database.GetMigrations().ShouldContain(migrationId);

        var script = db.GetService<IMigrator>().GenerateScript(
            "20260702182654_Identity_UserTokenIndexes",
            migrationId);

        script.ShouldContain("operations.dashboard.view-analytics");
        script.ShouldContain("operations.dashboard.export");
        script.ShouldContain("OPENJSON");
        script.ShouldContain("[SecurityStamp]");
        script.ShouldContain("NEWID()");
        script.ShouldContain("[identity].[user_sessions]");
        script.ShouldContain("[RevokedAtUtc]");
    }
}
