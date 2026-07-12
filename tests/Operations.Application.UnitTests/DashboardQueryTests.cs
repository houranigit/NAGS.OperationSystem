using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Authorization;
using Operations.Application.Features.Dashboard;
using Operations.Application.Features.Flights;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class DashboardQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Dashboard_ReturnsEveryOperationalFlightStatus()
    {
        await using var db = NewDb();
        var scheduled = CreateFlight("100");
        var inProgress = CreateFlight("200");
        var completed = CreateFlight("300");
        var canceled = CreateFlight("400");

        inProgress.OnWorkOrderSubmitted(Now).IsSuccess.ShouldBeTrue();
        completed.OnWorkOrderSubmitted(Now).IsSuccess.ShouldBeTrue();
        completed.SettleCompleted(Now).IsSuccess.ShouldBeTrue();
        canceled.OnWorkOrderSubmitted(Now).IsSuccess.ShouldBeTrue();
        canceled.SettleCanceled(Now).IsSuccess.ShouldBeTrue();

        db.Flights.AddRange(scheduled, inProgress, completed, canceled);
        await db.SaveChangesAsync();

        var handler = new GetOperationsDashboardQueryHandler(
            db,
            new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null)));

        var result = await handler.Handle(new GetOperationsDashboardQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ScheduledFlights.ShouldBe(1);
        result.Value.InProgressFlights.ShouldBe(1);
        result.Value.CompletedFlights.ShouldBe(1);
        result.Value.CanceledFlights.ShouldBe(1);
    }

    [Fact]
    public async Task Dashboard_UsesTheSameAssignedAndPerLandingVisibilityAsFlightLists()
    {
        await using var db = NewDb();
        var stationId = Guid.NewGuid();
        var otherStationId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var assignedStaff = new StaffMemberSnapshot(staffId, "Scoped Engineer", "EMP-10");

        var assigned = CreateFlight("100", stationId, assignedEmployees: [assignedStaff]);
        var perLanding = CreateFlight(
            "200",
            stationId,
            plannedServices: [new ServiceSnapshot(WellKnownMasterDataIds.AircraftPerLandingService, "Aircraft Per Landing")]);
        var unassigned = CreateFlight("300", stationId);
        var otherStation = CreateFlight(
            "400",
            otherStationId,
            assignedEmployees: [new StaffMemberSnapshot(staffId, "Scoped Engineer", "EMP-10")]);

        db.Flights.AddRange(assigned, perLanding, unassigned, otherStation);
        await db.SaveChangesAsync();

        var scope = new StaticScope(new OperationsScopeContext(UserType.StationStaff, stationId, staffId));
        var handler = new GetOperationsDashboardQueryHandler(db, scope);

        var result = await handler.Handle(new GetOperationsDashboardQuery(), CancellationToken.None);
        var listResult = await new GetFlightsQueryHandler(db, scope).Handle(
            new GetFlightsQuery(PageSize: 100),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        listResult.IsSuccess.ShouldBeTrue();
        listResult.Value.Items.Select(flight => flight.FlightNumber).ShouldBe(["100", "200"], ignoreOrder: true);
        result.Value.ScheduledFlights.ShouldBe(2);
        result.Value.InProgressFlights.ShouldBe(0);
        result.Value.CompletedFlights.ShouldBe(0);
        result.Value.CanceledFlights.ShouldBe(0);
    }

    private static OperationsDbContext NewDb() =>
        new(new DbContextOptionsBuilder<OperationsDbContext>()
            .UseInMemoryDatabase($"dashboard-{Guid.NewGuid()}")
            .Options);

    private static Flight CreateFlight(
        string flightNumber,
        Guid? stationId = null,
        IReadOnlyList<StaffMemberSnapshot>? assignedEmployees = null,
        IReadOnlyList<ServiceSnapshot>? plannedServices = null) =>
        Flight.ScheduleNew(
            new CustomerSnapshot(Guid.NewGuid(), "RJ", "Royal Jordanian"),
            new StationSnapshot(stationId ?? Guid.NewGuid(), "ORD", "Chicago O'Hare"),
            new OperationTypeSnapshot(Guid.NewGuid(), "Transit"),
            FlightNumber.Create(flightNumber).Value,
            ScheduledTime.Create(Now, Now.AddHours(1)).Value,
            aircraftType: null,
            plannedServices: plannedServices ?? [new ServiceSnapshot(Guid.NewGuid(), "Marshalling")],
            assignedEmployees: assignedEmployees ?? [],
            contractId: null,
            contractNumber: null,
            createdByUserId: Guid.NewGuid(),
            now: Now).Value;

    private sealed class StaticScope(OperationsScopeContext context) : IOperationsScope
    {
        public Task<Result<OperationsScopeContext>> ResolveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(context));
    }
}
