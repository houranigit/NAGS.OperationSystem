using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class ResourceUsageMigrationTests
{
    private const string PreviousMigration = "20260808211603_Operations_ReturnToRampOccurrences";
    private const string ResourceUsageMigration = "20260808213653_Operations_ResourceUsageCalculationTypes";

    [Fact]
    public void Up_backfills_legacy_tools_from_task_window_before_enforcing_usage_checks()
    {
        using var db = CreateContext();
        db.Database.GetMigrations().ShouldContain(ResourceUsageMigration);

        var script = db.GetService<IMigrator>().GenerateScript(PreviousMigration, ResourceUsageMigration);

        script.ShouldContain("UPDATE resource");
        script.ShouldContain("resource.[FromUtc] = task.[FromUtc]");
        script.ShouldContain("resource.[ToUtc] = task.[ToUtc]");
        script.ShouldContain("resource.[Quantity] = NULL");
        script.ShouldContain("work_order_task_materials");
        script.ShouldContain("work_order_task_general_supports");
        script.ShouldContain("CK_work_order_task_tools_ResourceUsage");
        script.ShouldContain("CK_work_order_task_materials_ResourceUsage");
        script.ShouldContain("CK_work_order_task_general_supports_ResourceUsage");
        script.ShouldContain("[ToUtc] IS NULL OR [ToUtc] >= [FromUtc]");

        var backfill = script.IndexOf("UPDATE resource", StringComparison.Ordinal);
        var firstConstraint = script.IndexOf("CK_work_order_task_tools_ResourceUsage", StringComparison.Ordinal);
        backfill.ShouldBeGreaterThanOrEqualTo(0);
        firstConstraint.ShouldBeGreaterThan(backfill);
    }

    [Fact]
    public void Down_repairs_null_quantities_in_every_resource_table_before_restoring_non_null_columns()
    {
        using var db = CreateContext();
        var script = db.GetService<IMigrator>().GenerateScript(ResourceUsageMigration, PreviousMigration);

        script.ShouldContain(
            "UPDATE [operations].[work_order_task_tools] SET [Quantity] = 1 WHERE [Quantity] IS NULL;");
        script.ShouldContain(
            "UPDATE [operations].[work_order_task_materials] SET [Quantity] = 1 WHERE [Quantity] IS NULL;");
        script.ShouldContain(
            "UPDATE [operations].[work_order_task_general_supports] SET [Quantity] = 1 WHERE [Quantity] IS NULL;");

        var repair = script.IndexOf(
            "UPDATE [operations].[work_order_task_tools] SET [Quantity] = 1",
            StringComparison.Ordinal);
        var nonNullQuantity = script.IndexOf("ALTER COLUMN [Quantity] decimal(18,2) NOT NULL", StringComparison.Ordinal);
        repair.ShouldBeGreaterThanOrEqualTo(0);
        nonNullQuantity.ShouldBeGreaterThan(repair);
    }

    private static OperationsDbContext CreateContext() => new(
        new DbContextOptionsBuilder<OperationsDbContext>()
            .UseSqlServer(
                "Server=localhost;Database=operations-resource-usage-script;User Id=sa;Password=NotUsed1!;TrustServerCertificate=true")
            .Options);
}
