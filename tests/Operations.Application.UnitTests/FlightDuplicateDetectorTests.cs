using Microsoft.EntityFrameworkCore;
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
    public async Task Detects_SameCustomerStationAndTime_AsStrongCandidate()
    {
        var customerId = Guid.NewGuid();
        var stationId = Guid.NewGuid();

        await using var db = NewDb();
        var existing = Flight.CreateAdHoc(
            new CustomerSnapshot(customerId, "SV", "Saudia"),
            new StationSnapshot(stationId, "RUH", "Riyadh"),
            new OperationTypeSnapshot(Guid.NewGuid(), "Transit"),
            FlightNumber.Create("SV1020").Value,
            ScheduledTime.Create(Now, Now.AddHours(1)).Value,
            aircraftType: null,
            plannedServices: [new ServiceSnapshot(Guid.NewGuid(), "Marshalling")],
            creator: new StaffMemberSnapshot(Guid.NewGuid(), "Ahmed", "E1"),
            createdByUserId: Guid.NewGuid(),
            now: Now).Value;
        db.Flights.Add(existing);
        await db.SaveChangesAsync();

        var detector = new FlightDuplicateDetector(db);
        var candidates = await detector.FindAsync(customerId, stationId, "SV1020", Now.AddMinutes(10), CancellationToken.None);

        candidates.ShouldNotBeEmpty();
        candidates[0].Score.ShouldBeGreaterThanOrEqualTo(FlightDuplicateDetector.StrongMatchThreshold);
    }

    [Fact]
    public async Task DifferentStation_ProducesNoCandidates()
    {
        var customerId = Guid.NewGuid();

        await using var db = NewDb();
        var existing = Flight.CreateAdHoc(
            new CustomerSnapshot(customerId, "SV", "Saudia"),
            new StationSnapshot(Guid.NewGuid(), "JED", "Jeddah"),
            new OperationTypeSnapshot(Guid.NewGuid(), "Transit"),
            FlightNumber.Create("SV1020").Value,
            ScheduledTime.Create(Now, Now.AddHours(1)).Value,
            null, [new ServiceSnapshot(Guid.NewGuid(), "Marshalling")], new StaffMemberSnapshot(Guid.NewGuid(), "Ahmed", "E1"),
            Guid.NewGuid(), Now).Value;
        db.Flights.Add(existing);
        await db.SaveChangesAsync();

        var detector = new FlightDuplicateDetector(db);
        var candidates = await detector.FindAsync(customerId, Guid.NewGuid(), "SV1020", Now, CancellationToken.None);

        candidates.ShouldBeEmpty();
    }
}
