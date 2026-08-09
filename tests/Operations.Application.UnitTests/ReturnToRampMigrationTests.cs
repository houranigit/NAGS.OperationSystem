using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class ReturnToRampMigrationTests
{
    private const string ResourceUsageMigration = "20260808213653_Operations_ResourceUsageCalculationTypes";
    private const string CompositeOwnershipMigration = "20260808215751_Operations_ReturnToRampCompositeOwnership";

    [Fact]
    public void Migration_backfills_one_occurrence_per_legacy_work_order_before_dropping_flags()
    {
        using var db = new OperationsDbContext(
            new DbContextOptionsBuilder<OperationsDbContext>()
                .UseSqlServer(
                    "Server=localhost;Database=operations-migration-script;User Id=sa;Password=NotUsed1!;TrustServerCertificate=true")
                .Options);

        const string migrationId = "20260808211603_Operations_ReturnToRampOccurrences";
        db.Database.GetMigrations().ShouldContain(migrationId);
        var script = db.GetService<IMigrator>().GenerateScript(
            "20260723214508_Operations_WorkOrderServiceLineAttachments",
            migrationId);

        script.ShouldContain("work_order_return_to_ramps");
        script.ShouldContain("LegacyWindows");
        script.ShouldContain("GROUP BY WorkOrderId");
        script.ShouldContain("Migrated legacy RTR activities; original occurrence grouping unavailable.");
        script.ShouldContain("SET ReturnToRampId = occurrence.Id");

        var backfill = script.IndexOf("LegacyActivities", StringComparison.Ordinal);
        var dropFlag = script.LastIndexOf("DROP COLUMN [IsReturnToRamp]", StringComparison.Ordinal);
        backfill.ShouldBeGreaterThanOrEqualTo(0);
        dropFlag.ShouldBeGreaterThan(backfill);
    }

    [Fact]
    public void Composite_ownership_migration_keys_children_by_occurrence_and_work_order()
    {
        using var db = new OperationsDbContext(
            new DbContextOptionsBuilder<OperationsDbContext>()
                .UseSqlServer(
                    "Server=localhost;Database=operations-migration-script;User Id=sa;Password=NotUsed1!;TrustServerCertificate=true")
                .Options);

        db.Database.GetMigrations().ShouldContain(CompositeOwnershipMigration);
        var script = db.GetService<IMigrator>().GenerateScript(
            ResourceUsageMigration,
            CompositeOwnershipMigration);

        script.ShouldContain("AK_work_order_return_to_ramps_Id_WorkOrderId");
        script.ShouldContain("FK_work_order_service_lines_work_order_return_to_ramps_ReturnToRampId_WorkOrderId");
        script.ShouldContain("FK_work_order_tasks_work_order_return_to_ramps_ReturnToRampId_WorkOrderId");
        script.ShouldContain("FOREIGN KEY ([ReturnToRampId], [WorkOrderId])");
        script.ShouldContain("REFERENCES [operations].[work_order_return_to_ramps] ([Id], [WorkOrderId])");
    }
}
