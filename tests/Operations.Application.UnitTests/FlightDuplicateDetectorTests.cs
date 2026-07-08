using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Authorization;
using Operations.Application.Features.Dashboard;
using Operations.Application.Features.Flights;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class FlightDuplicateDetectorTests
{
    private static OperationsDbContext NewDb() =>
        new(new DbContextOptionsBuilder<OperationsDbContext>()
            .UseInMemoryDatabase($"ops-{Guid.NewGuid()}")
            .Options);

    private static readonly DateTimeOffset Now = new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Detects_SameCustomerStationStaAndStd_AsCandidate()
    {
        var customerId = Guid.NewGuid();
        var stationId = Guid.NewGuid();

        await using var db = NewDb();
        var existing = CreateExistingFlight(customerId, stationId);
        db.Flights.Add(existing);
        await db.SaveChangesAsync();

        var detector = new FlightDuplicateDetector(db);
        var candidates = await detector.FindAsync(customerId, stationId, Now, Now.AddHours(1), excludeFlightId: null, CancellationToken.None);

        candidates.ShouldNotBeEmpty();
        candidates[0].Score.ShouldBe(FlightDuplicateDetector.StrongMatchThreshold);
    }

    [Fact]
    public async Task DifferentSta_ProducesNoCandidates()
    {
        var customerId = Guid.NewGuid();
        var stationId = Guid.NewGuid();

        await using var db = NewDb();
        db.Flights.Add(CreateExistingFlight(customerId, stationId));
        await db.SaveChangesAsync();

        var detector = new FlightDuplicateDetector(db);
        var candidates = await detector.FindAsync(customerId, stationId, Now.AddMinutes(10), Now.AddHours(1), excludeFlightId: null, CancellationToken.None);

        candidates.ShouldBeEmpty();
    }

    [Fact]
    public async Task DifferentCustomer_ProducesNoCandidates()
    {
        var stationId = Guid.NewGuid();

        await using var db = NewDb();
        db.Flights.Add(CreateExistingFlight(Guid.NewGuid(), stationId));
        await db.SaveChangesAsync();

        var detector = new FlightDuplicateDetector(db);
        var candidates = await detector.FindAsync(Guid.NewGuid(), stationId, Now, Now.AddHours(1), excludeFlightId: null, CancellationToken.None);

        candidates.ShouldBeEmpty();
    }

    [Fact]
    public async Task DifferentStation_ProducesNoCandidates()
    {
        var customerId = Guid.NewGuid();

        await using var db = NewDb();
        db.Flights.Add(CreateExistingFlight(customerId, Guid.NewGuid()));
        await db.SaveChangesAsync();

        var detector = new FlightDuplicateDetector(db);
        var candidates = await detector.FindAsync(customerId, Guid.NewGuid(), Now, Now.AddHours(1), excludeFlightId: null, CancellationToken.None);

        candidates.ShouldBeEmpty();
    }

    [Fact]
    public async Task DifferentStd_ProducesNoCandidates()
    {
        var customerId = Guid.NewGuid();
        var stationId = Guid.NewGuid();

        await using var db = NewDb();
        db.Flights.Add(CreateExistingFlight(customerId, stationId));
        await db.SaveChangesAsync();

        var detector = new FlightDuplicateDetector(db);
        var candidates = await detector.FindAsync(customerId, stationId, Now, Now.AddHours(2), excludeFlightId: null, CancellationToken.None);

        candidates.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExcludedFlight_IsNotReturned()
    {
        var customerId = Guid.NewGuid();
        var stationId = Guid.NewGuid();

        await using var db = NewDb();
        var existing = CreateExistingFlight(customerId, stationId);
        db.Flights.Add(existing);
        await db.SaveChangesAsync();

        var detector = new FlightDuplicateDetector(db);
        var candidates = await detector.FindAsync(customerId, stationId, Now, Now.AddHours(1), existing.Id, CancellationToken.None);

        candidates.ShouldBeEmpty();
    }

    [Fact]
    public async Task DuplicateQuery_AllowsAdministrator_WhenStationIsProvided()
    {
        var customerId = Guid.NewGuid();
        var stationId = Guid.NewGuid();

        await using var db = NewDb();
        db.Flights.Add(CreateExistingFlight(customerId, stationId));
        await db.SaveChangesAsync();

        var handler = new FindDuplicateCandidatesQueryHandler(
            new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null)),
            new FlightDuplicateDetector(db));

        var result = await handler.Handle(new FindDuplicateCandidatesQuery(customerId, stationId, Now, Now.AddHours(1)), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task DuplicateQuery_RequiresStation_ForAdministrator()
    {
        var customerId = Guid.NewGuid();

        await using var db = NewDb();
        var handler = new FindDuplicateCandidatesQueryHandler(
            new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null)),
            new FlightDuplicateDetector(db));

        var result = await handler.Handle(new FindDuplicateCandidatesQuery(customerId, null, Now, Now.AddHours(1)), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Operations.Flight.DuplicateCheckStationRequired");
    }

    private static Flight CreateExistingFlight(Guid customerId, Guid stationId) =>
        Flight.ScheduleNew(
            new CustomerSnapshot(customerId, "SV", "Saudia"),
            new StationSnapshot(stationId, "RUH", "Riyadh"),
            new OperationTypeSnapshot(Guid.NewGuid(), "Transit"),
            FlightNumber.Create("SV1020").Value,
            ScheduledTime.Create(Now, Now.AddHours(1)).Value,
            aircraftType: null,
            plannedServices: [new ServiceSnapshot(Guid.NewGuid(), "Marshalling")],
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
}
