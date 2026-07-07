using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Authorization;
using Operations.Application.Features.Flights;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class FlightQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("RJ-1")]
    [InlineData("RJ1")]
    public async Task GetFlights_SearchMatchesDisplayedCustomerIataFlightNumber(string search)
    {
        await using var db = NewDb();
        var flight = CreateScheduledFlight(customerIata: "RJ", customerName: "Royal Jordanian", flightNumber: "1");
        db.Flights.Add(flight);
        db.Flights.Add(CreateScheduledFlight(customerIata: "SV", customerName: "Saudia", flightNumber: "2"));
        await db.SaveChangesAsync();

        var handler = new GetFlightsQueryHandler(
            db,
            new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null)));

        var result = await handler.Handle(new GetFlightsQuery(Search: search), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Select(f => f.Id).ShouldBe([flight.Id]);
    }

    private static OperationsDbContext NewDb() =>
        new(new DbContextOptionsBuilder<OperationsDbContext>()
            .UseInMemoryDatabase($"ops-{Guid.NewGuid()}")
            .Options);

    private static Flight CreateScheduledFlight(string customerIata, string customerName, string flightNumber) =>
        Flight.ScheduleNew(
            new CustomerSnapshot(Guid.NewGuid(), customerIata, customerName),
            new StationSnapshot(Guid.NewGuid(), "DMM", "Dammam"),
            new OperationTypeSnapshot(Guid.NewGuid(), "Transit"),
            FlightNumber.Create(flightNumber).Value,
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
