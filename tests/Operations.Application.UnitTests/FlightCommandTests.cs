using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using System.Text.Json;
using MasterData.Contracts.Readers;
using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Operations.Contracts;
using Operations.Application.Authorization;
using Operations.Application.Common;
using Operations.Application.Features.Flights;
using Operations.Domain.Enumerations;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class FlightCommandTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AssignEmployeesCommand_RejectsPerLandingFlight()
    {
        await using var db = NewDb();
        var flight = CreateScheduledFlight(new ServiceSnapshot(WellKnownMasterDataIds.AircraftPerLandingService, "Aircraft Per Landing"));
        db.Flights.Add(flight);
        await db.SaveChangesAsync();

        var handler = new AssignEmployeesCommandHandler(
            db,
            new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null)),
            new MasterDataResolver(new ThrowingMasterDataReader()),
            new NoopTimelineWriter(),
            new NoopMobileSyncBroadcaster(),
            new StaticUserContext(),
            new StaticAuditContext(),
            TimeProvider.System);

        var result = await handler.Handle(
            new AssignEmployeesCommand(flight.Id, [Guid.NewGuid()], [1]),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Operations.PerLanding.AssignmentNotAllowed");
    }

    [Fact]
    public async Task ScheduleFlightCommand_Enqueues_one_assignment_event_per_new_recipient()
    {
        await using var db = NewDb();
        var stationId = Guid.NewGuid();
        var staff = Staff(Guid.NewGuid(), stationId, "Assigned");
        var reader = new ThrowingMasterDataReader([StaffRead(staff, stationId)]);
        var handler = new ScheduleFlightCommandHandler(
            db,
            new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null)),
            new MasterDataResolver(reader),
            new NoopTimelineWriter(),
            new NoopMobileSyncBroadcaster(),
            new StaticUserContext(),
            new StaticAuditContext(),
            TimeProvider.System);

        var result = await handler.Handle(new ScheduleFlightCommand(
            Guid.NewGuid(),
            stationId,
            Guid.NewGuid(),
            "SV300",
            Now,
            Now.AddHours(1),
            null,
            [Guid.NewGuid()],
            [staff.StaffMemberId]), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var message = await db.OutboxMessages.SingleAsync();
        message.Content.ShouldContain(staff.StaffMemberId.ToString());
        message.Content.ShouldContain("SV300");
    }

    [Fact]
    public async Task ScheduleFlightsCommand_Enqueues_one_schedule_update_event_per_recipient()
    {
        await using var db = NewDb();
        var stationId = Guid.NewGuid();
        var firstStaff = Staff(Guid.NewGuid(), stationId, "First assigned");
        var secondStaff = Staff(Guid.NewGuid(), stationId, "Second assigned");
        var mobileSync = new NoopMobileSyncBroadcaster();
        var handler = new ScheduleFlightsCommandHandler(
            db,
            new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null)),
            new MasterDataResolver(new ThrowingMasterDataReader(
                [StaffRead(firstStaff, stationId), StaffRead(secondStaff, stationId)])),
            new NoopTimelineWriter(),
            mobileSync,
            new StaticUserContext(),
            new StaticAuditContext(),
            TimeProvider.System);

        var result = await handler.Handle(new ScheduleFlightsCommand(
            Guid.NewGuid(),
            stationId,
            Guid.NewGuid(),
            "SV301",
            new TimeOnly(10, 0),
            new TimeOnly(11, 0),
            [new DateOnly(2026, 7, 13), new DateOnly(2026, 7, 14)],
            null,
            [Guid.NewGuid()],
            [firstStaff.StaffMemberId, secondStaff.StaffMemberId]), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var messages = await db.OutboxMessages.ToListAsync();
        messages.Count.ShouldBe(2);
        messages.ShouldAllBe(message => message.Type.Contains(nameof(FlightScheduleUpdated), StringComparison.Ordinal));

        var events = messages
            .Select(message => JsonSerializer.Deserialize<FlightScheduleUpdated>(message.Content)!)
            .ToList();
        events.ShouldAllBe(integrationEvent => integrationEvent.FlightCount == 2);
        events.Select(integrationEvent => integrationEvent.StaffMemberId)
            .ToHashSet()
            .SetEquals([firstStaff.StaffMemberId, secondStaff.StaffMemberId])
            .ShouldBeTrue();
        mobileSync.Changes.Count.ShouldBe(2);
        mobileSync.Changes.ShouldAllBe(change =>
            change.Table == BuildingBlocks.Application.Mobile.MobileSyncTables.Flights &&
            change.Op == BuildingBlocks.Application.Mobile.MobileSyncOps.Refresh &&
            change.EntityId == null);
        mobileSync.Changes.Select(change => change.Audience).ToHashSet().SetEquals(
            [
                BuildingBlocks.Application.Mobile.MobileSyncAudience.Employee(firstStaff.StaffMemberId),
                BuildingBlocks.Application.Mobile.MobileSyncAudience.Employee(secondStaff.StaffMemberId)
            ]).ShouldBeTrue();
    }

    [Fact]
    public async Task ScheduleFlightsCommand_When_dates_collapse_to_one_flight_keeps_detailed_assignment_event()
    {
        await using var db = NewDb();
        var stationId = Guid.NewGuid();
        var staff = Staff(Guid.NewGuid(), stationId, "Assigned");
        var handler = new ScheduleFlightsCommandHandler(
            db,
            new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null)),
            new MasterDataResolver(new ThrowingMasterDataReader([StaffRead(staff, stationId)])),
            new NoopTimelineWriter(),
            new NoopMobileSyncBroadcaster(),
            new StaticUserContext(),
            new StaticAuditContext(),
            TimeProvider.System);

        var selectedDate = new DateOnly(2026, 7, 13);
        var result = await handler.Handle(new ScheduleFlightsCommand(
            Guid.NewGuid(),
            stationId,
            Guid.NewGuid(),
            "SV302",
            new TimeOnly(10, 0),
            new TimeOnly(11, 0),
            [selectedDate, selectedDate],
            null,
            [Guid.NewGuid()],
            [staff.StaffMemberId]), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(1);
        var message = await db.OutboxMessages.SingleAsync();
        message.Type.ShouldContain(nameof(FlightEmployeeAssigned));
        var integrationEvent = JsonSerializer.Deserialize<FlightEmployeeAssigned>(message.Content)!;
        integrationEvent.FlightId.ShouldBe(result.Value[0]);
        integrationEvent.FlightNumber.ShouldBe("SV302");
    }

    [Fact]
    public async Task AssignEmployeesCommand_Enqueues_events_only_for_roster_additions()
    {
        await using var db = NewDb();
        var stationId = Guid.NewGuid();
        var existing = Staff(Guid.NewGuid(), stationId, "Existing");
        var added = Staff(Guid.NewGuid(), stationId, "Added");
        var flight = CreateScheduledFlight(assignedEmployees: [existing], stationId: stationId);
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        var user = new StaticUserContext();
        var handler = new AssignEmployeesCommandHandler(
            db,
            new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null)),
            new MasterDataResolver(new ThrowingMasterDataReader([StaffRead(existing, stationId), StaffRead(added, stationId)])),
            new NoopTimelineWriter(),
            new NoopMobileSyncBroadcaster(),
            user,
            new StaticAuditContext(),
            TimeProvider.System);

        var result = await handler.Handle(
            new AssignEmployeesCommand(flight.Id, [existing.StaffMemberId, added.StaffMemberId], flight.RowVersion),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var message = await db.OutboxMessages.SingleAsync();
        message.Type.ShouldContain(nameof(FlightEmployeeAssigned));
        message.Content.ShouldContain(added.StaffMemberId.ToString());
        message.Content.ShouldNotContain(existing.StaffMemberId.ToString());
    }

    [Fact]
    public async Task AssignEmployeesCommand_Does_not_enqueue_for_unassign()
    {
        await using var db = NewDb();
        var stationId = Guid.NewGuid();
        var retained = Staff(Guid.NewGuid(), stationId, "Retained");
        var removed = Staff(Guid.NewGuid(), stationId, "Removed");
        var flight = CreateScheduledFlight(assignedEmployees: [retained, removed], stationId: stationId);
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        var handler = new AssignEmployeesCommandHandler(
            db,
            new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null)),
            new MasterDataResolver(new ThrowingMasterDataReader([StaffRead(retained, stationId)])),
            new NoopTimelineWriter(),
            new NoopMobileSyncBroadcaster(),
            new StaticUserContext(),
            new StaticAuditContext(),
            TimeProvider.System);

        var result = await handler.Handle(
            new AssignEmployeesCommand(flight.Id, [retained.StaffMemberId], flight.RowVersion),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        (await db.OutboxMessages.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task InviteEmployeesCommand_Ignores_existing_and_enqueues_only_new_recipients()
    {
        await using var db = NewDb();
        var stationId = Guid.NewGuid();
        var existing = Staff(Guid.NewGuid(), stationId, "Existing");
        var added = Staff(Guid.NewGuid(), stationId, "Added");
        var flight = CreateScheduledFlight(assignedEmployees: [existing], stationId: stationId);
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        var handler = new InviteEmployeesToFlightCommandHandler(
            db,
            new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null)),
            new MasterDataResolver(new ThrowingMasterDataReader([StaffRead(added, stationId)])),
            new NoopTimelineWriter(),
            new NoopMobileSyncBroadcaster(),
            new StaticUserContext(),
            new StaticAuditContext(),
            TimeProvider.System);

        var result = await handler.Handle(
            new InviteEmployeesToFlightCommand(flight.Id, [existing.StaffMemberId, added.StaffMemberId], flight.RowVersion),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        flight.AssignedEmployees.Count.ShouldBe(2);
        var message = await db.OutboxMessages.SingleAsync();
        message.Content.ShouldContain(added.StaffMemberId.ToString());
        message.Content.ShouldNotContain(existing.StaffMemberId.ToString());
        JsonSerializer.Deserialize<FlightEmployeeAssigned>(message.Content)!.Source
            .ShouldBe(FlightAssignmentSource.Invite);
    }

    private static OperationsDbContext NewDb() =>
        new(new DbContextOptionsBuilder<OperationsDbContext>()
            .UseInMemoryDatabase($"ops-{Guid.NewGuid()}")
            .Options);

    private static Flight CreateScheduledFlight(
        ServiceSnapshot? plannedService = null,
        IReadOnlyList<StaffMemberSnapshot>? assignedEmployees = null,
        Guid? stationId = null) =>
        Flight.ScheduleNew(
            new CustomerSnapshot(Guid.NewGuid(), "SV", "Saudia"),
            new StationSnapshot(stationId ?? Guid.NewGuid(), "RUH", "Riyadh"),
            new OperationTypeSnapshot(Guid.NewGuid(), "Transit"),
            FlightNumber.Create("SV1020").Value,
            ScheduledTime.Create(Now, Now.AddHours(1)).Value,
            aircraftType: null,
            plannedServices: [plannedService ?? new ServiceSnapshot(Guid.NewGuid(), "Marshalling")],
            assignedEmployees: assignedEmployees ?? [],
            contractId: null,
            contractNumber: null,
            createdByUserId: Guid.NewGuid(),
            now: Now).Value;

    private static StaffMemberSnapshot Staff(Guid id, Guid stationId, string name) =>
        new(id, name, $"EMP-{id:N}"[..12]);

    private static StaffMemberReadSnapshot StaffRead(StaffMemberSnapshot staff, Guid stationId) =>
        new(staff.StaffMemberId, staff.FullName, staff.EmployeeId, stationId, Guid.NewGuid(), true);

    private sealed class StaticScope(OperationsScopeContext context) : IOperationsScope
    {
        public Task<Result<OperationsScopeContext>> ResolveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(context));
    }

    private sealed class NoopTimelineWriter : IFlightTimelineWriter
    {
        public Task AppendAsync(
            Guid flightId,
            FlightTimelineEventType eventType,
            DateTimeOffset occurredAtUtc,
            string? details = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NoopMobileSyncBroadcaster : BuildingBlocks.Application.Mobile.IMobileSyncBroadcaster
    {
        public List<BuildingBlocks.Application.Mobile.MobileSyncChange> Changes { get; } = [];

        public void Enqueue(BuildingBlocks.Application.Mobile.MobileSyncChange change)
        {
            Changes.Add(change);
        }

        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task BroadcastNowAsync(BuildingBlocks.Application.Mobile.MobileSyncChange change, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class StaticUserContext : BuildingBlocks.Application.Abstractions.IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; } = Guid.NewGuid();
        public UserType? UserType => BuildingBlocks.Contracts.Authorization.UserType.SystemAdministrator;
        public Guid? ExternalReferenceId => null;
        public bool HasPermission(string permission) => true;
    }

    private sealed class StaticAuditContext : BuildingBlocks.Application.Auditing.IAuditContext
    {
        public Guid? ActorId => Guid.NewGuid();
        public string? ActorDisplayName => "Test Dispatcher";
        public bool IsSystemActor => false;
        public string? CorrelationId => null;
    }

    private sealed class ThrowingMasterDataReader(IReadOnlyList<StaffMemberReadSnapshot>? staff = null) : IMasterDataReader
    {
        public Task<CustomerReadSnapshot?> GetCustomerAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<CustomerReadSnapshot?>(new CustomerReadSnapshot(id, "SV", null, "Saudia", true));

        public Task<StationReadSnapshot?> GetStationAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<StationReadSnapshot?>(new StationReadSnapshot(id, "RUH", null, "Riyadh", true));

        public Task<OperationTypeReadSnapshot?> GetOperationTypeAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<OperationTypeReadSnapshot?>(new OperationTypeReadSnapshot(id, "Transit", true));

        public Task<AircraftTypeReadSnapshot?> GetAircraftTypeAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<ServiceReadSnapshot?> GetServiceAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<ServiceReadSnapshot>> GetServicesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ServiceReadSnapshot>>(ids.Select(id => new ServiceReadSnapshot(id, "Marshalling", true)).ToList());

        public Task<StaffMemberReadSnapshot?> GetStaffMemberAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<StaffMemberReadSnapshot>> GetStaffMembersAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StaffMemberReadSnapshot>>((staff ?? [])
                .Where(item => ids.Contains(item.Id))
                .ToList());

        public Task<IReadOnlyList<StaffMemberReadSnapshot>> GetActiveStaffMembersForStationAsync(Guid stationId, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<ToolReadSnapshot?> GetToolAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<MaterialReadSnapshot?> GetMaterialAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<GeneralSupportReadSnapshot?> GetGeneralSupportAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<ManpowerTypeReadSnapshot?> GetManpowerTypeAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<ServiceReadSnapshot>> GetActiveServicesAsync(CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<ToolReadSnapshot>> GetActiveToolsAsync(CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<MaterialReadSnapshot>> GetActiveMaterialsAsync(CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<GeneralSupportReadSnapshot>> GetActiveGeneralSupportsAsync(CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<CustomerReadSnapshot>> GetActiveCustomersAsync(CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<AircraftTypeReadSnapshot>> GetActiveAircraftTypesAsync(CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }
}
