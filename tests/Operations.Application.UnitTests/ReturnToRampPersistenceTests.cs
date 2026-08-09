using Microsoft.EntityFrameworkCore;
using Operations.Application.Features.WorkOrders;
using Operations.Domain.Enumerations;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Domain.WorkOrders;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class ReturnToRampPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Loader_round_trips_multiple_occurrences_with_their_nested_rows()
    {
        await using var db = new OperationsDbContext(
            new DbContextOptionsBuilder<OperationsDbContext>()
                .UseInMemoryDatabase($"rtr-roundtrip-{Guid.NewGuid()}")
                .Options);
        var flight = CreateFlight();
        var workOrder = WorkOrder.SubmitNew(
            flight,
            WorkOrderType.Completion,
            Guid.NewGuid(),
            owner: null,
            actualFlightNumber: null,
            aircraftType: null,
            aircraftTailNumber: null,
            actuals: null,
            cancellation: null,
            remarks: null,
            serviceLines: [],
            tasks: [],
            returnToRamps: [Occurrence(0, "First"), Occurrence(60, "Second")],
            now: Now).Value;

        db.Flights.Add(flight);
        db.WorkOrders.Add(workOrder);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloaded = await WorkOrderLoader.ForMutation(db.WorkOrders.AsNoTracking())
            .SingleAsync(item => item.Id == workOrder.Id);

        reloaded.ReturnToRamps.Count.ShouldBe(2);
        reloaded.ReturnToRamps.Select(item => item.Description).ShouldBe(["First", "Second"]);
        foreach (var occurrence in reloaded.ReturnToRamps)
        {
            occurrence.ServiceLines.ShouldHaveSingleItem().ReturnToRampId.ShouldBe(occurrence.Id);
            occurrence.Tasks.ShouldHaveSingleItem().ReturnToRampId.ShouldBe(occurrence.Id);
        }
        reloaded.ServiceLines.Count(item => item.IsReturnToRamp).ShouldBe(2);
        reloaded.Tasks.Count(item => item.IsReturnToRamp).ShouldBe(2);
    }

    [Fact]
    public void Merge_cloner_preserves_each_occurrence_without_recloning_flattened_children()
    {
        var flight = CreateFlight();
        var first = CreateWorkOrder(
            flight,
            [Occurrence(0, "First"), Occurrence(60, "Second")]);
        var second = CreateWorkOrder(
            flight,
            [Occurrence(120, "Third")]);

        // Domain compatibility lists expose these same children at the work-order level.
        first.ServiceLines.Count(item => item.IsReturnToRamp).ShouldBe(2);
        first.Tasks.Count(item => item.IsReturnToRamp).ShouldBe(2);

        var cloned = WorkOrderReturnToRampCloner.Clone([first, second]);

        cloned.Select(item => item.Description).ShouldBe(["First", "Second", "Third"]);
        cloned.SelectMany(item => item.ServiceLines).Count().ShouldBe(3);
        cloned.SelectMany(item => item.Tasks).Count().ShouldBe(3);
        cloned.ShouldAllBe(item => item.Id == null);
        cloned.SelectMany(item => item.ServiceLines)
            .ShouldAllBe(line => line.Id == null && !line.IsReturnToRamp);
        cloned.SelectMany(item => item.Tasks)
            .ShouldAllBe(task => task.Id == null && !task.IsReturnToRamp);
    }

    [Fact]
    public void Loader_uses_split_queries_for_the_relational_work_order_graph()
    {
        using var db = new OperationsDbContext(
            new DbContextOptionsBuilder<OperationsDbContext>()
                .UseSqlServer("Server=localhost;Database=operations-query-shape;Integrated Security=true;TrustServerCertificate=true")
                .Options);

        var sql = WorkOrderLoader.ForMutation(db.WorkOrders.AsNoTracking())
            .Where(item => item.Id == Guid.NewGuid())
            .ToQueryString();

        sql.ShouldContain("split-query mode", Case.Insensitive);
    }

    [Fact]
    public void Model_enforces_return_to_ramp_and_work_order_ownership_together()
    {
        using var db = new OperationsDbContext(
            new DbContextOptionsBuilder<OperationsDbContext>()
                .UseSqlServer("Server=localhost;Database=operations-model;Integrated Security=true;TrustServerCertificate=true")
                .Options);

        AssertCompositeOwnershipForeignKey<WorkOrderServiceLine>(db);
        AssertCompositeOwnershipForeignKey<WorkOrderTask>(db);
    }

    private static Flight CreateFlight() => Flight.ScheduleNew(
        new CustomerSnapshot(Guid.NewGuid(), "SV", "Saudia"),
        new StationSnapshot(Guid.NewGuid(), "RUH", "Riyadh"),
        new OperationTypeSnapshot(Guid.NewGuid(), "Transit"),
        FlightNumber.Create("SV101").Value,
        ScheduledTime.Create(Now, Now.AddHours(1)).Value,
        aircraftType: null,
        plannedServices: [new ServiceSnapshot(Guid.NewGuid(), "Marshalling")],
        assignedEmployees: [],
        contractId: null,
        contractNumber: null,
        createdByUserId: Guid.NewGuid(),
        now: Now).Value;

    private static WorkOrder CreateWorkOrder(
        Flight flight,
        IReadOnlyList<WorkOrderReturnToRampInput> returnToRamps) =>
        WorkOrder.SubmitNew(
            flight,
            WorkOrderType.Completion,
            Guid.NewGuid(),
            owner: null,
            actualFlightNumber: null,
            aircraftType: null,
            aircraftTailNumber: null,
            actuals: null,
            cancellation: null,
            remarks: null,
            serviceLines: [],
            tasks: [],
            returnToRamps,
            now: Now).Value;

    private static void AssertCompositeOwnershipForeignKey<TChild>(OperationsDbContext db)
    {
        var entity = db.Model.FindEntityType(typeof(TChild)).ShouldNotBeNull();
        var foreignKey = entity.GetForeignKeys()
            .Single(item => item.PrincipalEntityType.ClrType == typeof(WorkOrderReturnToRamp));

        foreignKey.Properties.Select(property => property.Name)
            .ShouldBe(["ReturnToRampId", "WorkOrderId"]);
        foreignKey.PrincipalKey.Properties.Select(property => property.Name)
            .ShouldBe(["Id", "WorkOrderId"]);
        foreignKey.DeleteBehavior.ShouldBe(DeleteBehavior.NoAction);
    }

    private static WorkOrderReturnToRampInput Occurrence(int offsetMinutes, string description)
    {
        var from = Now.AddMinutes(offsetMinutes);
        var window = TimeWindow.Create(from, from.AddMinutes(40)).Value;
        var staff = new StaffMemberSnapshot(Guid.NewGuid(), $"{description} Agent", $"{offsetMinutes}");
        return new WorkOrderReturnToRampInput(
            null,
            window,
            description,
            [new WorkOrderServiceLineInput(
                new ServiceSnapshot(Guid.NewGuid(), $"{description} Service"),
                [staff],
                TimeWindow.Create(from.AddMinutes(2), from.AddMinutes(20)).Value,
                null)],
            [new WorkOrderTaskInput(
                null,
                TaskType.Minor,
                $"{description} Task",
                TimeWindow.Create(from.AddMinutes(5), from.AddMinutes(30)).Value,
                [staff],
                [],
                [],
                [])]);
    }
}
