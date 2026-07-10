using Microsoft.EntityFrameworkCore;
using Operations.Application.Features.WorkOrders;
using Operations.Domain.Enumerations;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Domain.WorkOrders;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class WorkOrderNumberAllocatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AllocateAsync_ReturnsLowestFreeStationSequence()
    {
        await using var db = NewDb();
        var station = new StationSnapshot(Guid.NewGuid(), "RUH", "Riyadh");
        db.WorkOrders.Add(CreateApprovedWorkOrder(station, 1));
        db.WorkOrders.Add(CreateApprovedWorkOrder(station, 3));
        db.WorkOrders.Add(CreateApprovedWorkOrder(new StationSnapshot(Guid.NewGuid(), "JED", "Jeddah"), 2));
        await db.SaveChangesAsync();

        var allocator = new WorkOrderNumberAllocator(db);

        var result = await allocator.AllocateAsync(station, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Sequence.ShouldBe(2);
        result.Value.Number.ShouldBe("RUH-0002");
    }

    private static OperationsDbContext NewDb() =>
        new(new DbContextOptionsBuilder<OperationsDbContext>()
            .UseInMemoryDatabase($"ops-{Guid.NewGuid()}")
            .Options);

    private static WorkOrder CreateApprovedWorkOrder(StationSnapshot station, int sequence)
    {
        var flight = Flight.ScheduleNew(
            new CustomerSnapshot(Guid.NewGuid(), "SV", "Saudia"),
            station,
            new OperationTypeSnapshot(Guid.NewGuid(), "Transit"),
            FlightNumber.Create($"SV{sequence:000}").Value,
            ScheduledTime.Create(Now.AddHours(sequence), Now.AddHours(sequence + 1)).Value,
            aircraftType: null,
            plannedServices: [new ServiceSnapshot(Guid.NewGuid(), "Marshalling")],
            assignedEmployees: [],
            contractId: null,
            contractNumber: null,
            createdByUserId: Guid.NewGuid(),
            now: Now).Value;

        var workOrder = WorkOrder.SubmitNew(
            flight,
            WorkOrderType.Completion,
            Guid.NewGuid(),
            new StaffMemberSnapshot(Guid.NewGuid(), "Ahmed Ali", "E1001"),
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            [],
            Now).Value;

        workOrder.UpdateDetails(
            WorkOrderType.Completion,
            workOrder.ActualFlightNumber,
            new AircraftTypeSnapshot(Guid.NewGuid(), "Airbus", "A320"),
            null,
            ActualTime.Create(Now, Now.AddHours(1)).Value,
            null,
            null,
            [],
            [],
            Now).IsSuccess.ShouldBeTrue();

        workOrder.Approve(sequence, WorkOrderNumber.Format(station.IataCode, sequence).Value, Guid.NewGuid(), Now).IsSuccess.ShouldBeTrue();
        return workOrder;
    }
}
