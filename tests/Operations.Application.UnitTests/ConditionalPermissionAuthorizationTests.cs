using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using Operations.Application.Authorization;
using Operations.Application.Features.Flights;
using Operations.Application.Features.WorkOrders;
using Operations.Domain.Authorization;
using Operations.Domain.Enumerations;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Domain.WorkOrders;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class ConditionalPermissionAuthorizationTests
{
    [Fact]
    public async Task Scheduling_a_flight_with_staff_requires_assign_permission()
    {
        var handler = ScheduleHandler(UserWithoutConditionalPermissions(), scope: null!);

        var result = await handler.Handle(ScheduleFlight([Guid.NewGuid()]), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Operations.Flight.AssignForbidden");
    }

    [Fact]
    public async Task Bulk_scheduling_with_staff_requires_assign_permission()
    {
        var handler = BulkScheduleHandler(UserWithoutConditionalPermissions(), scope: null!);

        var result = await handler.Handle(ScheduleFlights([Guid.NewGuid()]), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Operations.Flight.AssignForbidden");
    }

    [Fact]
    public async Task Scheduling_without_staff_does_not_require_assign_permission()
    {
        var handler = ScheduleHandler(
            UserWithoutConditionalPermissions(),
            new RejectingScope("Test.ScopeReached"));

        var result = await handler.Handle(ScheduleFlight([]), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Test.ScopeReached");
    }

    [Fact]
    public async Task Scheduling_is_denied_by_write_scope_for_ViewerOnly_even_with_a_forged_schedule_claim()
    {
        var handler = ScheduleHandler(
            UserWithoutConditionalPermissions(),
            new StaticScope(new OperationsScopeContext(UserType.ViewerOnly, null, null)));

        var result = await handler.Handle(ScheduleFlight([]), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Operations.Scope.ReadOnly");
    }

    [Fact]
    public async Task Immediate_merge_approval_requires_approve_permission()
    {
        var handler = MergeHandler(UserWithoutConditionalPermissions());

        var result = await handler.Handle(MergeCommand(approveImmediately: true), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Operations.WorkOrder.ApproveForbidden");
    }

    [Fact]
    public async Task Merge_without_immediate_approval_does_not_require_approve_permission()
    {
        var handler = MergeHandler(UserWithoutConditionalPermissions());

        var result = await handler.Handle(MergeCommand(approveImmediately: false), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Operations.WorkOrder.MergeSourceCount");
    }

    [Fact]
    public void Delete_others_does_not_grant_edit_access_to_another_owners_work_order()
    {
        var workOrder = CreateWorkOrder(ownerUserId: Guid.NewGuid());
        var user = new TestUserContext(
            new HashSet<string>(StringComparer.Ordinal) { OperationsPermissions.WorkOrders.DeleteOthers });

        WorkOrderAuthorization.EnsureManageAccess(workOrder, user).IsFailure.ShouldBeTrue();
        WorkOrderAuthorization.EnsureDeleteAccess(workOrder, user).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Manage_others_does_not_grant_delete_access_to_another_owners_work_order()
    {
        var workOrder = CreateWorkOrder(ownerUserId: Guid.NewGuid());
        var user = new TestUserContext(
            new HashSet<string>(StringComparer.Ordinal) { OperationsPermissions.WorkOrders.ManageOthers });

        WorkOrderAuthorization.EnsureManageAccess(workOrder, user).IsSuccess.ShouldBeTrue();
        WorkOrderAuthorization.EnsureDeleteAccess(workOrder, user).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Owner_can_manage_and_delete_their_own_work_order_without_elevated_permissions()
    {
        var ownerUserId = Guid.NewGuid();
        var workOrder = CreateWorkOrder(ownerUserId);
        var user = new TestUserContext(new HashSet<string>(StringComparer.Ordinal), ownerUserId);

        WorkOrderAuthorization.EnsureManageAccess(workOrder, user).IsSuccess.ShouldBeTrue();
        WorkOrderAuthorization.EnsureDeleteAccess(workOrder, user).IsSuccess.ShouldBeTrue();
    }

    private static ScheduleFlightCommandHandler ScheduleHandler(IUserContext user, IOperationsScope scope) =>
        new(
            db: null!,
            scope,
            resolver: null!,
            timeline: null!,
            mobileSync: null!,
            user,
            auditContext: null!,
            timeProvider: TimeProvider.System);

    private static ScheduleFlightsCommandHandler BulkScheduleHandler(IUserContext user, IOperationsScope scope) =>
        new(
            db: null!,
            scope,
            resolver: null!,
            timeline: null!,
            mobileSync: null!,
            user,
            auditContext: null!,
            timeProvider: TimeProvider.System);

    private static MergeWorkOrdersCommandHandler MergeHandler(IUserContext user) =>
        new(
            db: null!,
            scope: null!,
            inputBuilder: null!,
            resolver: null!,
            allocator: null!,
            workOrderTimeline: null!,
            flightTimeline: null!,
            mobileSync: null!,
            user,
            timeProvider: TimeProvider.System);

    private static ScheduleFlightCommand ScheduleFlight(IReadOnlyList<Guid> assignedStaffMemberIds) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "OS100",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(2),
            AircraftTypeId: null,
            PlannedServiceIds: [Guid.NewGuid()],
            assignedStaffMemberIds);

    private static ScheduleFlightsCommand ScheduleFlights(IReadOnlyList<Guid> assignedStaffMemberIds) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "OS100",
            new TimeOnly(10, 0),
            new TimeOnly(12, 0),
            SelectedDates: [DateOnly.FromDateTime(DateTime.UtcNow)],
            AircraftTypeId: null,
            PlannedServiceIds: [Guid.NewGuid()],
            assignedStaffMemberIds);

    private static MergeWorkOrdersCommand MergeCommand(bool approveImmediately) =>
        new(
            Guid.NewGuid(),
            SourceWorkOrderIds: [Guid.NewGuid()],
            WorkOrderType.Completion,
            new WorkOrderEditableCommandPayload(
                ActualFlightNumber: null,
                AircraftTypeId: null,
                AircraftTailNumber: null,
                ActualArrivalUtc: null,
                ActualDepartureUtc: null,
                CanceledAtUtc: null,
                CancellationReason: null,
                Remarks: null,
                ServiceLines: [],
                Tasks: []),
            approveImmediately);

    private static WorkOrder CreateWorkOrder(Guid ownerUserId)
    {
        var now = DateTimeOffset.UtcNow;
        var flight = Flight.ScheduleNew(
            new CustomerSnapshot(Guid.NewGuid(), "OS", "Example customer"),
            new StationSnapshot(Guid.NewGuid(), "ORD", "Chicago"),
            new OperationTypeSnapshot(Guid.NewGuid(), "Turnaround"),
            FlightNumber.Create("OS100").Value,
            ScheduledTime.Create(now, now.AddHours(2)).Value,
            aircraftType: null,
            plannedServices: [],
            assignedEmployees: [],
            contractId: null,
            contractNumber: null,
            createdByUserId: Guid.NewGuid(),
            now,
            allowEmptyPlannedServices: true).Value;

        return WorkOrder.SubmitNew(
            flight,
            WorkOrderType.Completion,
            ownerUserId,
            owner: null,
            actualFlightNumber: null,
            aircraftType: null,
            aircraftTailNumber: null,
            actuals: null,
            cancellation: null,
            remarks: null,
            serviceLines: [],
            tasks: [],
            now).Value;
    }

    private static IUserContext UserWithoutConditionalPermissions() =>
        new TestUserContext(new HashSet<string>(StringComparer.Ordinal)
        {
            OperationsPermissions.Flights.Schedule,
            OperationsPermissions.WorkOrders.Merge
        });

    private sealed class RejectingScope(string code) : IOperationsScope
    {
        public Task<Result<OperationsScopeContext>> ResolveAsync(CancellationToken cancellationToken) =>
            Task.FromResult<Result<OperationsScopeContext>>(Error.Forbidden("Scope reached.", code));
    }

    private sealed class StaticScope(OperationsScopeContext context) : IOperationsScope
    {
        public Task<Result<OperationsScopeContext>> ResolveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(context));
    }

    private sealed class TestUserContext(IReadOnlySet<string> permissions, Guid? userId = null) : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; } = userId ?? Guid.NewGuid();
        public UserType? UserType => BuildingBlocks.Contracts.Authorization.UserType.SystemAdministrator;
        public Guid? ExternalReferenceId => null;
        public bool HasPermission(string permission) => permissions.Contains(permission);
    }
}
