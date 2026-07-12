using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Mobile;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Authorization;
using Operations.Application.Common;
using Operations.Application.Features.WorkOrders;
using Operations.Domain.Enumerations;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Domain.WorkOrders;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class PerLandingApprovalCommandTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ApprovePerLandingFlights_ApprovesIncompleteSystemWorkOrderAndCompletesFlight()
    {
        await using var db = NewDb();
        var flight = CreatePerLandingFlight();
        flight.OnWorkOrderSubmitted(Now).IsSuccess.ShouldBeTrue();
        var workOrder = WorkOrder.SubmitNew(
            flight,
            WorkOrderType.Completion,
            Guid.Empty,
            owner: null,
            actualFlightNumber: null,
            aircraftType: null,
            aircraftTailNumber: null,
            actuals: null,
            cancellation: null,
            remarks: "Per Landing review",
            serviceLines: [],
            tasks: [],
            Now).Value;
        db.Flights.Add(flight);
        db.WorkOrders.Add(workOrder);
        await db.SaveChangesAsync();

        var userId = Guid.NewGuid();
        var handler = new ApprovePerLandingFlightsCommandHandler(
            db,
            new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null)),
            new NoopWorkOrderTimelineWriter(),
            new NoopFlightTimelineWriter(),
            new NoopMobileSyncBroadcaster(),
            new TestUserContext(userId),
            TimeProvider.System);

        var result = await handler.Handle(
            new ApprovePerLandingFlightsCommand([
                new PerLandingApprovalSelection(flight.Id, workOrder.Id, workOrder.RowVersion)
            ]),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(1);
        workOrder.Status.ShouldBe(WorkOrderStatus.Approved);
        workOrder.ApprovalNumber.ShouldBe("RUH-0001");
        flight.Status.ShouldBe(FlightStatus.Completed);
    }

    private static OperationsDbContext NewDb() =>
        new(new DbContextOptionsBuilder<OperationsDbContext>()
            .UseInMemoryDatabase($"ops-{Guid.NewGuid()}")
            .Options);

    private static Flight CreatePerLandingFlight() =>
        Flight.ScheduleNew(
            new CustomerSnapshot(Guid.NewGuid(), "SV", "Saudia"),
            new StationSnapshot(Guid.NewGuid(), "RUH", "Riyadh"),
            new OperationTypeSnapshot(Guid.NewGuid(), "Transit"),
            FlightNumber.Create("1020").Value,
            ScheduledTime.Create(Now, Now.AddHours(1)).Value,
            aircraftType: null,
            plannedServices: [new ServiceSnapshot(WellKnownMasterDataIds.AircraftPerLandingService, "Aircraft Per Landing")],
            assignedEmployees: [],
            contractId: null,
            contractNumber: null,
            createdByUserId: Guid.NewGuid(),
            now: Now).Value;

    private sealed class StaticScope(OperationsScopeContext context) : IOperationsScope
    {
        public Task<Result<OperationsScopeContext>> ResolveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(context));
    }

    private sealed class TestUserContext(Guid userId) : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => userId;
        public UserType? UserType => BuildingBlocks.Contracts.Authorization.UserType.SystemAdministrator;
        public Guid? ExternalReferenceId => null;
        public bool HasPermission(string permission) => true;
    }

    private sealed class NoopWorkOrderTimelineWriter : IWorkOrderTimelineWriter
    {
        public Task AppendAsync(
            Guid workOrderId,
            WorkOrderTimelineEventType eventType,
            DateTimeOffset occurredAtUtc,
            string? details = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoopFlightTimelineWriter : IFlightTimelineWriter
    {
        public Task AppendAsync(
            Guid flightId,
            FlightTimelineEventType eventType,
            DateTimeOffset occurredAtUtc,
            string? details = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoopMobileSyncBroadcaster : IMobileSyncBroadcaster
    {
        public void Enqueue(MobileSyncChange change) { }
        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task BroadcastNowAsync(MobileSyncChange change, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
