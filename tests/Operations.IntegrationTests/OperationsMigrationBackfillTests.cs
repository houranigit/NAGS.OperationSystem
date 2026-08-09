using MasterData.Contracts.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Operations.Domain.Enumerations;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Domain.WorkOrders;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.IntegrationTests;

public sealed class OperationsMigrationBackfillTests(OperationsApiFactory factory)
    : IClassFixture<OperationsApiFactory>
{
    private const string LegacySchemaMigration =
        "20260723214508_Operations_WorkOrderServiceLineAttachments";
    private static readonly DateTimeOffset Now =
        new(2026, 8, 8, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Legacy_rtr_and_resource_rows_are_backfilled_by_real_sql_migrations()
    {
        // Starting the host creates and migrates this test class's isolated SQL Server database.
        _ = factory.Services;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var migrator = db.Database.GetService<IMigrator>();
        var flight = CreateFlight();
        var workOrder = CreateWorkOrder(flight);
        db.Flights.Add(flight);
        db.WorkOrders.Add(workOrder);
        await db.SaveChangesAsync();
        var workOrderId = workOrder.Id;
        db.ChangeTracker.Clear();

        await migrator.MigrateAsync(LegacySchemaMigration);
        try
        {
            (await ScalarIntAsync(db,
                "SELECT COUNT(*) FROM [operations].[work_order_service_lines] WHERE [IsReturnToRamp] = 1"))
                .ShouldBe(2);
            (await ScalarIntAsync(db,
                "SELECT COUNT(*) FROM [operations].[work_order_tasks] WHERE [IsReturnToRamp] = 1"))
                .ShouldBe(2);

            // Give each legacy resource kind a distinctive quantity before upgrading. The tool
            // quantity is intentionally discarded because the new default for tools is Duration.
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE [operations].[work_order_task_tools] SET [Quantity] = 7");
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE [operations].[work_order_task_materials] SET [Quantity] = 2");
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE [operations].[work_order_task_general_supports] SET [Quantity] = 3");

            await db.Database.MigrateAsync();
            db.ChangeTracker.Clear();

            var migrated = await db.WorkOrders
                .AsNoTracking()
                .AsSplitQuery()
                .Include(item => item.ReturnToRamps)
                    .ThenInclude(item => item.ServiceLines)
                .Include(item => item.ReturnToRamps)
                    .ThenInclude(item => item.Tasks)
                        .ThenInclude(item => item.Tools)
                .Include(item => item.ReturnToRamps)
                    .ThenInclude(item => item.Tasks)
                        .ThenInclude(item => item.Materials)
                .Include(item => item.ReturnToRamps)
                    .ThenInclude(item => item.Tasks)
                        .ThenInclude(item => item.GeneralSupports)
                .Include(item => item.ServiceLines)
                .Include(item => item.Tasks)
                .SingleAsync(item => item.Id == workOrderId);
            var occurrence = migrated.ReturnToRamps.ShouldHaveSingleItem();

            occurrence.Description.ShouldBe(
                "Migrated legacy RTR activities; original occurrence grouping unavailable.");
            occurrence.Window.From.ShouldBe(Now.AddHours(1).AddMinutes(2));
            occurrence.Window.To.ShouldBe(Now.AddHours(2).AddMinutes(55));
            occurrence.ServiceLines.Count.ShouldBe(2);
            occurrence.Tasks.Count.ShouldBe(2);
            occurrence.ServiceLines.ShouldAllBe(line => line.ReturnToRampId == occurrence.Id);
            occurrence.Tasks.ShouldAllBe(task => task.ReturnToRampId == occurrence.Id);
            migrated.ServiceLines.Count(line => line.ReturnToRampId == null).ShouldBe(1);
            migrated.Tasks.Count(task => task.ReturnToRampId == null).ShouldBe(1);

            var migratedTask = occurrence.Tasks.Single(task => task.Description == "First legacy task");
            var tool = migratedTask.Tools.ShouldHaveSingleItem();
            tool.Usage.CalculationType.ShouldBe(ResourceCalculationType.Duration);
            tool.Usage.Quantity.ShouldBeNull();
            tool.Usage.FromUtc.ShouldBe(migratedTask.Window.From);
            tool.Usage.ToUtc.ShouldBe(migratedTask.Window.To);

            var material = migratedTask.Materials.ShouldHaveSingleItem();
            material.Usage.CalculationType.ShouldBe(ResourceCalculationType.Quantity);
            material.Usage.Quantity.ShouldBe(2m);
            material.Usage.FromUtc.ShouldBeNull();
            material.Usage.ToUtc.ShouldBeNull();

            var support = migratedTask.GeneralSupports.ShouldHaveSingleItem();
            support.Usage.CalculationType.ShouldBe(ResourceCalculationType.Quantity);
            support.Usage.Quantity.ShouldBe(3m);
            support.Usage.FromUtc.ShouldBeNull();
            support.Usage.ToUtc.ShouldBeNull();
        }
        finally
        {
            await db.Database.MigrateAsync();
        }
    }

    private static Flight CreateFlight() => Flight.ScheduleNew(
        new CustomerSnapshot(Guid.NewGuid(), "SV", "Saudia"),
        new StationSnapshot(Guid.NewGuid(), "RUH", "Riyadh"),
        new OperationTypeSnapshot(Guid.NewGuid(), "Transit"),
        FlightNumber.Create("SV101").Value,
        ScheduledTime.Create(Now, Now.AddHours(2)).Value,
        aircraftType: null,
        plannedServices: [new ServiceSnapshot(Guid.NewGuid(), "Marshalling")],
        assignedEmployees: [],
        contractId: null,
        contractNumber: null,
        createdByUserId: Guid.NewGuid(),
        now: Now).Value;

    private static WorkOrder CreateWorkOrder(Flight flight)
    {
        var standardWindow = TimeWindow.Create(Now.AddMinutes(5), Now.AddMinutes(25)).Value;
        return WorkOrder.SubmitNew(
            flight,
            WorkOrderType.Completion,
            Guid.NewGuid(),
            Staff(),
            FlightNumber.Create("SV101").Value,
            aircraftType: null,
            aircraftTailNumber: "HZ-LEGACY",
            ActualTime.Create(Now, Now.AddHours(2)).Value,
            cancellation: null,
            remarks: "Migration fixture",
            serviceLines:
            [
                new WorkOrderServiceLineInput(
                    new ServiceSnapshot(Guid.NewGuid(), "Standard service"),
                    [Staff()],
                    standardWindow,
                    null)
            ],
            tasks:
            [
                new WorkOrderTaskInput(
                    null,
                    TaskType.Minor,
                    "Standard task",
                    standardWindow,
                    [Staff()],
                    [],
                    [],
                    [])
            ],
            returnToRamps:
            [
                Occurrence(60, "First legacy task", includeResources: true),
                Occurrence(150, "Second legacy task", includeResources: false)
            ],
            now: Now).Value;
    }

    private static WorkOrderReturnToRampInput Occurrence(
        int offsetMinutes,
        string taskDescription,
        bool includeResources)
    {
        var from = Now.AddMinutes(offsetMinutes);
        var taskWindow = TimeWindow.Create(from.AddMinutes(5), from.AddMinutes(25)).Value;
        return new WorkOrderReturnToRampInput(
            null,
            TimeWindow.Create(from, from.AddMinutes(30)).Value,
            taskDescription,
            [new WorkOrderServiceLineInput(
                new ServiceSnapshot(Guid.NewGuid(), $"Service {offsetMinutes}"),
                [Staff()],
                TimeWindow.Create(from.AddMinutes(2), from.AddMinutes(20)).Value,
                null)],
            [new WorkOrderTaskInput(
                null,
                TaskType.Minor,
                taskDescription,
                taskWindow,
                [Staff()],
                includeResources
                    ? [new WorkOrderTaskToolInput(
                        new ToolSnapshot(Guid.NewGuid(), "Legacy tool"),
                        ResourceUsage.Create(
                            ResourceCalculationType.Duration,
                            null,
                            from.AddMinutes(7),
                            from.AddMinutes(18)).Value)]
                    : [],
                includeResources
                    ? [new WorkOrderTaskMaterialInput(
                        new MaterialSnapshot(Guid.NewGuid(), "Legacy material"),
                        ResourceUsage.Create(ResourceCalculationType.Quantity, 1m, null, null).Value)]
                    : [],
                includeResources
                    ? [new WorkOrderTaskGeneralSupportInput(
                        new GeneralSupportSnapshot(Guid.NewGuid(), "Legacy support"),
                        ResourceUsage.Create(ResourceCalculationType.Quantity, 1m, null, null).Value)]
                    : [])]);
    }

    private static StaffMemberSnapshot Staff() =>
        new(Guid.NewGuid(), "Legacy Agent", "E-100");

    private static async Task<int> ScalarIntAsync(OperationsDbContext db, string sql)
    {
        var connection = db.Database.GetDbConnection();
        var closeWhenDone = connection.State != System.Data.ConnectionState.Open;
        if (closeWhenDone)
            await connection.OpenAsync();

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }
        finally
        {
            if (closeWhenDone)
                await connection.CloseAsync();
        }
    }
}
