using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Authorization;
using Operations.Application.Common;
using Operations.Application.Features.Flights;
using Operations.Domain.Authorization;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class FlightSchedulingTests
{
    [Fact]
    public async Task SingleFlight_PersistsItsPlanningAndCanBeReadFromTheGrid()
    {
        await using var db = NewDb();
        var reader = new SchedulingMasterDataReader();
        var now = new DateTimeOffset(2026, 7, 3, 10, 0, 0, TimeSpan.Zero);
        var result = await new ScheduleFlightCommandHandler(db, AdminScope(), new MasterDataResolver(reader), new TestUser(), TimeProvider.System, NoopMobileSyncBroadcaster.Instance)
            .Handle(new ScheduleFlightCommand(Guid.NewGuid(), reader.StationId, Guid.NewGuid(), "123",
                now, now.AddHours(1), null, [reader.ServiceId], []), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        db.ChangeTracker.Clear();
        var flight = await db.Flights.Include(f => f.PlannedServices).SingleAsync();
        flight.PlannedServices.ShouldHaveSingleItem().Service.ServiceId.ShouldBe(reader.ServiceId);
        var page = await new GetFlightsQueryHandler(db, AdminScope(), NoopCustomerLogoReader.Instance)
            .Handle(new GetFlightsQuery(Search: "SV-123"), CancellationToken.None);
        page.Value.Items.ShouldHaveSingleItem().Id.ShouldBe(result.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("A310")]
    public async Task FlightRegister_IncludesTheOptionalAircraftModel(string? aircraftModel)
    {
        await using var db = NewDb();
        var aircraft = aircraftModel is null ? null : new AircraftTypeSnapshot(Guid.NewGuid(), "Airbus", aircraftModel);
        var flight = NewFlight(Guid.NewGuid(), [], aircraftType: aircraft);
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new GetFlightsQueryHandler(db, AdminScope(), NoopCustomerLogoReader.Instance)
            .Handle(new GetFlightsQuery(), CancellationToken.None);

        var item = result.Value.Items.ShouldHaveSingleItem();
        item.AircraftTypeModel.ShouldBe(aircraftModel);
        item.CustomerId.ShouldBe(flight.Customer.CustomerId);
    }

    [Fact]
    public async Task GridStationSort_UsesTheColumnPropertyName()
    {
        await using var db = NewDb();
        var first = NewFlight(Guid.NewGuid(), [], stationIata: "AAA", arrivalDay: 1);
        var second = NewFlight(Guid.NewGuid(), [], stationIata: "ZZZ", arrivalDay: 2);
        db.Flights.AddRange(first, second);
        await db.SaveChangesAsync();

        var result = await new GetFlightsQueryHandler(db, AdminScope(), NoopCustomerLogoReader.Instance)
            .Handle(new GetFlightsQuery(Sort: "StationIataCode:asc"), CancellationToken.None);

        result.Value.Items.Select(flight => flight.Id).ShouldBe([first.Id, second.Id]);
    }

    [Fact]
    public async Task BatchSchedule_DeduplicatesDatesAndPreservesOvernightLocalTimes()
    {
        await using var db = NewDb();
        var reader = new SchedulingMasterDataReader();
        var firstDate = new DateOnly(2026, 7, 3);
        var result = await new ScheduleFlightsCommandHandler(db, AdminScope(), new MasterDataResolver(reader), new TestUser(), TimeProvider.System, NoopMobileSyncBroadcaster.Instance)
            .Handle(new ScheduleFlightsCommand(Guid.NewGuid(), reader.StationId, Guid.NewGuid(), "123",
                new TimeOnly(23, 30), new TimeOnly(1, 30), [firstDate.AddDays(1), firstDate, firstDate],
                null, [reader.ServiceId], [], "Asia/Riyadh"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(2);
        db.ChangeTracker.Clear();
        var flights = await db.Flights.Include(f => f.PlannedServices).OrderBy(f => f.Schedule.Sta).ToListAsync();
        flights[0].Schedule.Sta.ShouldBe(new DateTimeOffset(2026, 7, 3, 20, 30, 0, TimeSpan.Zero));
        flights[0].Schedule.Std.ShouldBe(new DateTimeOffset(2026, 7, 3, 22, 30, 0, TimeSpan.Zero));
        flights[1].Schedule.Sta.ShouldBe(flights[0].Schedule.Sta.AddDays(1));
        flights.All(flight => flight.PlannedServices.Count == 1).ShouldBeTrue();
        flights[0].PlannedServices[0].Id.ShouldNotBe(flights[1].PlannedServices[0].Id);
    }

    [Fact]
    public async Task SchedulingWithAssignedStaff_RequiresAssignmentPermission()
    {
        await using var db = NewDb();
        var reader = new SchedulingMasterDataReader();
        var result = await new ScheduleFlightCommandHandler(db, AdminScope(), new MasterDataResolver(reader), new TestUser(false), TimeProvider.System, NoopMobileSyncBroadcaster.Instance)
            .Handle(new ScheduleFlightCommand(Guid.NewGuid(), reader.StationId, Guid.NewGuid(), "123",
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), null, [reader.ServiceId], [Guid.NewGuid()]), CancellationToken.None);

        result.Error.Code.ShouldBe("Operations.Flight.AssignForbidden");
        (await db.Flights.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task SchedulingOutsideStationScope_IsDenied()
    {
        await using var db = NewDb();
        var scope = new StaticScope(new OperationsScopeContext(UserType.StationStaff, Guid.NewGuid(), Guid.NewGuid()));
        var reader = new SchedulingMasterDataReader();
        var result = await new ScheduleFlightCommandHandler(db, scope, new MasterDataResolver(reader), new TestUser(), TimeProvider.System, NoopMobileSyncBroadcaster.Instance)
            .Handle(new ScheduleFlightCommand(Guid.NewGuid(), reader.StationId, Guid.NewGuid(), "123",
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), null, [reader.ServiceId], []), CancellationToken.None);

        result.Error.Code.ShouldBe("Operations.Scope.Forbidden");
        (await db.Flights.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task StationStaff_SeeOnlyAssignedOrPerLandingFlightsAtTheirStation()
    {
        await using var db = NewDb();
        var stationId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var assigned = NewFlight(stationId, [new StaffMemberSnapshot(staffId, "Assigned staff", "E1")]);
        var perLanding = NewFlight(stationId, [], perLanding: true);
        db.Flights.AddRange(assigned, perLanding, NewFlight(stationId, []), NewFlight(Guid.NewGuid(), [], perLanding: true));
        await db.SaveChangesAsync();

        var result = await new GetFlightsQueryHandler(db, new StaticScope(new OperationsScopeContext(UserType.StationStaff, stationId, staffId)), NoopCustomerLogoReader.Instance)
            .Handle(new GetFlightsQuery(), CancellationToken.None);

        result.Value.Items.Select(flight => flight.Id).ShouldBe([assigned.Id, perLanding.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task StationDispatcher_SeesAllFlightsAtTheirStation()
    {
        await using var db = NewDb();
        var stationId = Guid.NewGuid();
        var flight = NewFlight(stationId, []);
        db.Flights.AddRange(flight, NewFlight(Guid.NewGuid(), []));
        await db.SaveChangesAsync();

        var result = await new GetFlightsQueryHandler(db, new StaticScope(new OperationsScopeContext(UserType.StationStaff, stationId, Guid.NewGuid(), CanViewStationWide: true)), NoopCustomerLogoReader.Instance)
            .Handle(new GetFlightsQuery(), CancellationToken.None);

        result.Value.Items.ShouldHaveSingleItem().Id.ShouldBe(flight.Id);
    }

    [Fact]
    public async Task ScheduleEdit_CanClearRosterAndChangeToPerLandingTogether()
    {
        await using var db = NewDb();
        var reader = new SchedulingMasterDataReader();
        var staff = new StaffMemberSnapshot(Guid.NewGuid(), "Assigned staff", "E1");
        var flight = NewFlight(reader.StationId, [staff]);
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        var arrival = flight.Schedule.Sta.AddHours(2);

        var result = await new UpdateScheduledFlightCommandHandler(db, AdminScope(), new MasterDataResolver(reader), new TestUser(), TimeProvider.System, NoopMobileSyncBroadcaster.Instance)
            .Handle(new UpdateScheduledFlightCommand(flight.Id, flight.Customer.CustomerId, reader.StationId,
                flight.OperationType.OperationTypeId, arrival, arrival.AddHours(1), null,
                [WellKnownMasterDataIds.AircraftPerLandingService], flight.RowVersion, []), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        db.ChangeTracker.Clear();
        var saved = await db.Flights.Include(item => item.PlannedServices).Include(item => item.AssignedEmployees).SingleAsync();
        saved.IsPerLanding.ShouldBeTrue();
        saved.AssignedEmployees.ShouldBeEmpty();
        saved.Schedule.Sta.ShouldBe(arrival);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScheduleEdit_WithoutAssignmentPermission_PreservesAnOmittedOrUnchangedRoster(bool includeRoster)
    {
        await using var db = NewDb();
        var reader = new SchedulingMasterDataReader();
        var staff = new StaffMemberSnapshot(Guid.NewGuid(), "Assigned staff", "E1");
        var flight = NewFlight(reader.StationId, [staff]);
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        var assignmentId = flight.AssignedEmployees[0].Id;
        var arrival = flight.Schedule.Sta.AddHours(2);

        var result = await new UpdateScheduledFlightCommandHandler(db, AdminScope(), new MasterDataResolver(reader), new TestUser(false), TimeProvider.System, NoopMobileSyncBroadcaster.Instance)
            .Handle(new UpdateScheduledFlightCommand(flight.Id, flight.Customer.CustomerId, reader.StationId,
                flight.OperationType.OperationTypeId, arrival, arrival.AddHours(1), null,
                [reader.ServiceId], flight.RowVersion, includeRoster ? [staff.StaffMemberId] : null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        db.ChangeTracker.Clear();
        var saved = await db.Flights.Include(item => item.AssignedEmployees).SingleAsync();
        saved.AssignedEmployees.ShouldHaveSingleItem().Id.ShouldBe(assignmentId);
        saved.Schedule.Sta.ShouldBe(arrival);
    }

    [Fact]
    public async Task ScheduleEdit_RejectsUnauthorizedRosterChangeBeforeChangingPlanning()
    {
        await using var db = NewDb();
        var reader = new SchedulingMasterDataReader();
        var staff = new StaffMemberSnapshot(Guid.NewGuid(), "Assigned staff", "E1");
        var flight = NewFlight(reader.StationId, [staff]);
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        var arrival = flight.Schedule.Sta;
        var serviceId = flight.PlannedServices[0].Service.ServiceId;

        var result = await new UpdateScheduledFlightCommandHandler(db, AdminScope(), new MasterDataResolver(reader), new TestUser(false), TimeProvider.System, NoopMobileSyncBroadcaster.Instance)
            .Handle(new UpdateScheduledFlightCommand(flight.Id, flight.Customer.CustomerId, reader.StationId,
                flight.OperationType.OperationTypeId, arrival.AddHours(2), arrival.AddHours(3), null,
                [WellKnownMasterDataIds.AircraftPerLandingService], flight.RowVersion, []), CancellationToken.None);

        result.Error.Code.ShouldBe("Operations.Flight.AssignForbidden");
        flight.Schedule.Sta.ShouldBe(arrival);
        flight.PlannedServices.ShouldHaveSingleItem().Service.ServiceId.ShouldBe(serviceId);
        flight.AssignedEmployees.ShouldHaveSingleItem().Employee.StaffMemberId.ShouldBe(staff.StaffMemberId);
        db.ChangeTracker.HasChanges().ShouldBeFalse();
    }

    private static Flight NewFlight(Guid stationId, IReadOnlyList<StaffMemberSnapshot> staff, bool perLanding = false, string stationIata = "RUH", int arrivalDay = 1, AircraftTypeSnapshot? aircraftType = null) =>
        Flight.ScheduleNew(new CustomerSnapshot(Guid.NewGuid(), "SV", "Saudia"),
            new StationSnapshot(stationId, stationIata, "Station"), new OperationTypeSnapshot(Guid.NewGuid(), "Transit"),
            FlightNumber.Create("123").Value, ScheduledTime.Create(new DateTimeOffset(2026, 7, arrivalDay, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, arrivalDay, 10, 0, 0, TimeSpan.Zero)).Value,
            aircraftType, [new ServiceSnapshot(perLanding ? WellKnownMasterDataIds.AircraftPerLandingService : Guid.NewGuid(), "Service")],
            staff, Guid.NewGuid(), DateTimeOffset.UtcNow).Value;

    private static OperationsDbContext NewDb() => new(new DbContextOptionsBuilder<OperationsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static StaticScope AdminScope() => new(new OperationsScopeContext(UserType.SystemAdministrator, null, null));

    private sealed class StaticScope(OperationsScopeContext context) : IOperationsScope
    {
        public Task<Result<OperationsScopeContext>> ResolveAsync(CancellationToken cancellationToken) => Task.FromResult(Result.Success(context));
    }

    private sealed class TestUser(bool canAssign = true) : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; } = Guid.NewGuid();
        public UserType? UserType => BuildingBlocks.Contracts.Authorization.UserType.SystemAdministrator;
        public Guid? ExternalReferenceId => null;
        public bool HasPermission(string permission) => canAssign && permission == OperationsPermissions.Flights.Assign;
    }
}
