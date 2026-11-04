using System.Text.Json;
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Auditing;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Readers;
using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Authorization;
using Operations.Application.Common;
using Operations.Application.Features.Flights;
using Operations.Contracts;
using Operations.Domain.Authorization;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class FlightInvitationTests
{
    [Fact]
    public async Task Options_ContainOnlyActiveUnassignedStaffAtTheFlightStation()
    {
        await using var db = NewDb();
        var reader = new SchedulingMasterDataReader();
        var assigned = Staff(reader.StationId, "Assigned");
        var available = Staff(reader.StationId, "Available");
        var inactive = Staff(reader.StationId, "Inactive", active: false);
        var otherStation = Staff(Guid.NewGuid(), "Other station");
        foreach (var staff in new[] { assigned, available, inactive, otherStation })
            reader.StaffMembers.Add(staff.Id, staff);
        var flight = FlightFor(reader.StationId, [Snapshot(assigned)]);
        db.Flights.Add(flight);
        await db.SaveChangesAsync();

        var result = await new GetFlightInvitationOptionsQueryHandler(db, AdminScope(), reader)
            .Handle(new GetFlightInvitationOptionsQuery(flight.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldHaveSingleItem().StaffMemberId.ShouldBe(available.Id);
    }

    [Fact]
    public async Task Invitation_AddsDistinctStaffWithoutReplacingExistingAssignments_AndQueuesOneInviteEvent()
    {
        await using var db = NewDb();
        var reader = new SchedulingMasterDataReader();
        var assigned = Staff(reader.StationId, "Assigned");
        var invited = Staff(reader.StationId, "Invited");
        reader.StaffMembers.Add(invited.Id, invited);
        var flight = FlightFor(reader.StationId, [Snapshot(assigned)]);
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        var assignmentId = flight.AssignedEmployees[0].Id;
        var handler = Handler(db, reader, AdminScope());

        var result = await handler.Handle(new InviteEmployeesToFlightCommand(
            flight.Id, [assigned.Id, invited.Id, invited.Id], flight.RowVersion), CancellationToken.None);
        var repeated = await handler.Handle(new InviteEmployeesToFlightCommand(
            flight.Id, [invited.Id], flight.RowVersion), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        repeated.IsSuccess.ShouldBeTrue();
        flight.AssignedEmployees.Count.ShouldBe(2);
        flight.AssignedEmployees.Single(employee => employee.Employee.StaffMemberId == assigned.Id).Id.ShouldBe(assignmentId);
        var envelope = await db.OutboxMessages.SingleAsync();
        var invitation = JsonSerializer.Deserialize<FlightEmployeeAssigned>(envelope.Content)!;
        invitation.Source.ShouldBe(FlightAssignmentSource.Invite);
        invitation.StaffMemberId.ShouldBe(invited.Id);
        invitation.FlightId.ShouldBe(flight.Id);
        invitation.AssignedByDisplayName.ShouldBe("Inviter");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidInvitationTarget_DoesNotChangeRosterOrQueueEvents(bool wrongStation)
    {
        await using var db = NewDb();
        var reader = new SchedulingMasterDataReader();
        var valid = Staff(reader.StationId, "Valid");
        var invalid = Staff(wrongStation ? Guid.NewGuid() : reader.StationId, "Invalid", active: wrongStation);
        reader.StaffMembers.Add(valid.Id, valid);
        reader.StaffMembers.Add(invalid.Id, invalid);
        var flight = FlightFor(reader.StationId, []);
        db.Flights.Add(flight);
        await db.SaveChangesAsync();

        var result = await Handler(db, reader, AdminScope()).Handle(new InviteEmployeesToFlightCommand(
            flight.Id, [valid.Id, invalid.Id], flight.RowVersion), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        flight.AssignedEmployees.ShouldBeEmpty();
        (await db.OutboxMessages.CountAsync()).ShouldBe(0);
        db.ChangeTracker.HasChanges().ShouldBeFalse();
    }

    [Fact]
    public async Task PerLanding_RejectsBothOptionsAndInvitation()
    {
        await using var db = NewDb();
        var reader = new SchedulingMasterDataReader();
        var flight = FlightFor(reader.StationId, [], perLanding: true);
        db.Flights.Add(flight);
        await db.SaveChangesAsync();

        var options = await new GetFlightInvitationOptionsQueryHandler(db, AdminScope(), reader)
            .Handle(new GetFlightInvitationOptionsQuery(flight.Id), CancellationToken.None);
        var invitation = await Handler(db, reader, AdminScope()).Handle(new InviteEmployeesToFlightCommand(
            flight.Id, [Guid.NewGuid()], flight.RowVersion), CancellationToken.None);

        options.Error.Code.ShouldBe("Operations.PerLanding.AssignmentNotAllowed");
        invitation.Error.Code.ShouldBe("Operations.PerLanding.AssignmentNotAllowed");
        flight.AssignedEmployees.ShouldBeEmpty();
        (await db.OutboxMessages.CountAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData(UserType.ViewerOnly)]
    [InlineData(UserType.StationStaff)]
    public async Task ViewerOrOutOfScopeStaff_CannotReadOptionsOrInvite(UserType userType)
    {
        await using var db = NewDb();
        var reader = new SchedulingMasterDataReader();
        var flight = FlightFor(reader.StationId, []);
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        var scope = new StaticScope(new OperationsScopeContext(userType, Guid.NewGuid(), Guid.NewGuid()));

        var options = await new GetFlightInvitationOptionsQueryHandler(db, scope, reader)
            .Handle(new GetFlightInvitationOptionsQuery(flight.Id), CancellationToken.None);
        var invitation = await Handler(db, reader, scope).Handle(new InviteEmployeesToFlightCommand(
            flight.Id, [Guid.NewGuid()], flight.RowVersion), CancellationToken.None);

        options.IsFailure.ShouldBeTrue();
        invitation.IsFailure.ShouldBeTrue();
        flight.AssignedEmployees.ShouldBeEmpty();
        (await db.OutboxMessages.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public void InvitationPermission_IsAvailableOnlyToAdministratorsAndStationStaff()
    {
        var permission = new OperationsPermissionCatalog().Permissions.Single(item => item.Code == OperationsPermissions.Flights.Invite);
        permission.IsCompatibleWith(UserType.SystemAdministrator).ShouldBeTrue();
        permission.IsCompatibleWith(UserType.StationStaff).ShouldBeTrue();
        permission.IsCompatibleWith(UserType.ViewerOnly).ShouldBeFalse();
        permission.IsCompatibleWith(UserType.CustomerContact).ShouldBeFalse();
    }

    private static InviteEmployeesToFlightCommandHandler Handler(OperationsDbContext db, SchedulingMasterDataReader reader, IOperationsScope scope) =>
        new(db, scope, new MasterDataResolver(reader), new Actor(), new AuditActor(), TimeProvider.System, NoopMobileSyncBroadcaster.Instance);

    private static StaffMemberReadSnapshot Staff(Guid stationId, string name, bool active = true) =>
        new(Guid.NewGuid(), name, name, stationId, Guid.NewGuid(), active);

    private static StaffMemberSnapshot Snapshot(StaffMemberReadSnapshot staff) => new(staff.Id, staff.FullName, staff.EmployeeId);

    private static Flight FlightFor(Guid stationId, IReadOnlyList<StaffMemberSnapshot> staff, bool perLanding = false) =>
        Flight.ScheduleNew(new CustomerSnapshot(Guid.NewGuid(), "SV", "Saudia"),
            new StationSnapshot(stationId, "RUH", "Riyadh"), new OperationTypeSnapshot(Guid.NewGuid(), "Transit"),
            FlightNumber.Create("SV200").Value,
            ScheduledTime.Create(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1)).Value,
            null, [new ServiceSnapshot(perLanding ? WellKnownMasterDataIds.AircraftPerLandingService : Guid.NewGuid(), "Planned service")],
            staff, Guid.NewGuid(), DateTimeOffset.UtcNow).Value;

    private static OperationsDbContext NewDb() => new(new DbContextOptionsBuilder<OperationsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static StaticScope AdminScope() => new(new OperationsScopeContext(UserType.SystemAdministrator, null, null));

    private sealed class StaticScope(OperationsScopeContext context) : IOperationsScope
    {
        public Task<Result<OperationsScopeContext>> ResolveAsync(CancellationToken cancellationToken) => Task.FromResult(Result.Success(context));
    }

    private sealed class Actor : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; } = Guid.NewGuid();
        public UserType? UserType => BuildingBlocks.Contracts.Authorization.UserType.SystemAdministrator;
        public Guid? ExternalReferenceId => null;
        public bool HasPermission(string permission) => permission == OperationsPermissions.Flights.Invite;
    }

    private sealed class AuditActor : IAuditContext
    {
        public Guid? ActorId => null;
        public string? ActorDisplayName => "Inviter";
        public bool IsSystemActor => false;
        public string? CorrelationId => null;
    }
}
