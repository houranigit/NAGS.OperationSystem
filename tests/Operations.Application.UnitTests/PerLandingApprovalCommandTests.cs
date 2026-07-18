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
        var tasksOnlyWorkOrder = CreateWorkOrder(
            flight,
            includeService: false,
            tasks: [TaskInput()]);
        db.Flights.Add(flight);
        db.WorkOrders.AddRange(workOrder, tasksOnlyWorkOrder);
        await db.SaveChangesAsync();

        var handler = CreateHandler(db);

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

    [Theory]
    [InlineData(WorkOrderStatus.Submitted, WorkOrderType.Completion, false)]
    [InlineData(WorkOrderStatus.Returned, WorkOrderType.Completion, false)]
    [InlineData(WorkOrderStatus.Approved, WorkOrderType.Completion, false)]
    [InlineData(WorkOrderStatus.Submitted, WorkOrderType.Cancellation, false)]
    [InlineData(WorkOrderStatus.Submitted, WorkOrderType.Completion, true)]
    public async Task ApprovePerLandingFlights_RejectsAnyNonMergedWorkOrderWithPerformedService(
        WorkOrderStatus status,
        WorkOrderType type,
        bool mergeGenerated)
    {
        await using var db = NewDb();
        var flight = CreatePerLandingFlight();
        flight.OnWorkOrderSubmitted(Now).IsSuccess.ShouldBeTrue();
        var selected = CreateWorkOrder(flight, includeService: false);
        var performed = CreateWorkOrder(
            flight,
            includeService: true,
            type: type,
            mergeGenerated: mergeGenerated);
        TransitionWorkOrder(performed, status);
        db.Flights.Add(flight);
        db.WorkOrders.AddRange(selected, performed);
        await db.SaveChangesAsync();

        var result = await CreateHandler(db).Handle(
            new ApprovePerLandingFlightsCommand([
                new PerLandingApprovalSelection(flight.Id, selected.Id, selected.RowVersion)
            ]),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Operations.PerLanding.Ineligible");
        selected.Status.ShouldBe(WorkOrderStatus.Submitted);
        flight.Status.ShouldBe(FlightStatus.InProgress);
    }

    [Fact]
    public async Task ApprovePerLandingFlights_IgnoresMergedWorkOrderWithPerformedService()
    {
        await using var db = NewDb();
        var flight = CreatePerLandingFlight();
        flight.OnWorkOrderSubmitted(Now).IsSuccess.ShouldBeTrue();
        var selected = CreateWorkOrder(flight, includeService: false, mergeGenerated: true);
        var source = CreateWorkOrder(flight, includeService: true);
        source.MarkMergedInto(selected.Id, Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        db.Flights.Add(flight);
        db.WorkOrders.AddRange(selected, source);
        await db.SaveChangesAsync();

        var result = await CreateHandler(db).Handle(
            new ApprovePerLandingFlightsCommand([
                new PerLandingApprovalSelection(flight.Id, selected.Id, selected.RowVersion)
            ]),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        selected.Status.ShouldBe(WorkOrderStatus.Approved);
        flight.Status.ShouldBe(FlightStatus.Completed);
    }

    private static OperationsDbContext NewDb() =>
        new(new DbContextOptionsBuilder<OperationsDbContext>()
            .UseInMemoryDatabase($"ops-{Guid.NewGuid()}")
            .Options);

    private static ApprovePerLandingFlightsCommandHandler CreateHandler(OperationsDbContext db) =>
        new(
            db,
            new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null)),
            new NoopWorkOrderTimelineWriter(),
            new NoopFlightTimelineWriter(),
            new NoopMobileSyncBroadcaster(),
            new TestUserContext(Guid.NewGuid()),
            TimeProvider.System);

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

    private static WorkOrder CreateWorkOrder(
        Flight flight,
        bool includeService,
        WorkOrderType type = WorkOrderType.Completion,
        IReadOnlyList<WorkOrderTaskInput>? tasks = null,
        bool mergeGenerated = false)
    {
        var employee = new StaffMemberSnapshot(Guid.NewGuid(), "Ramp Agent", "EMP-1");
        WorkOrderServiceLineInput[] serviceLines = includeService
            ? [new WorkOrderServiceLineInput(
                new ServiceSnapshot(Guid.NewGuid(), "Marshalling"),
                employee,
                TimeWindow.Create(Now, Now.AddMinutes(15)).Value,
                null)]
            : [];
        var cancellation = type == WorkOrderType.Cancellation
            ? CancellationDetails.Create(Now, "Customer canceled").Value
            : null;
        var actualNumber = FlightNumber.Create("1020").Value;
        var aircraftType = new AircraftTypeSnapshot(Guid.NewGuid(), "Airbus", "A320");
        var actuals = ActualTime.Create(Now, Now.AddHours(1)).Value;
        var result = mergeGenerated
            ? WorkOrder.SubmitMerged(
                flight, type, Guid.NewGuid(), employee, actualNumber, aircraftType, "HZ-ABC", actuals,
                cancellation, "Per Landing", serviceLines, tasks ?? [], Now)
            : WorkOrder.SubmitNew(
                flight, type, Guid.NewGuid(), employee, actualNumber, aircraftType, "HZ-ABC", actuals,
                cancellation, "Per Landing", serviceLines, tasks ?? [], Now);

        return result.Value;
    }

    private static WorkOrderTaskInput TaskInput() =>
        new(
            Id: null,
            TaskType.Minor,
            "Task without service",
            TimeWindow.Create(Now, Now.AddMinutes(15)).Value,
            Employees: [],
            Tools: [],
            Materials: [],
            GeneralSupports: []);

    private static void TransitionWorkOrder(WorkOrder workOrder, WorkOrderStatus status)
    {
        switch (status)
        {
            case WorkOrderStatus.Submitted:
                return;
            case WorkOrderStatus.Returned:
                workOrder.Approve(1, "RUH-0001", Guid.NewGuid(), Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
                workOrder.Return(Guid.NewGuid(), "Correction required", Now.AddMinutes(2)).IsSuccess.ShouldBeTrue();
                return;
            case WorkOrderStatus.Approved:
                workOrder.Approve(1, "RUH-0001", Guid.NewGuid(), Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, null);
        }
    }

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
