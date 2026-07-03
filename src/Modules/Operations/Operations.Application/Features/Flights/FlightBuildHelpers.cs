using BuildingBlocks.Domain.Results;
using Operations.Application.Common;
using Operations.Domain.ValueObjects;

namespace Operations.Application.Features.Flights;

/// <summary>Resolves and validates the common set of flight references and value objects.</summary>
internal static class FlightBuildHelpers
{
    internal sealed record BuiltFlight(
        CustomerSnapshot Customer,
        StationSnapshot Station,
        OperationTypeSnapshot OperationType,
        FlightNumber FlightNumber,
        ScheduledTime Schedule,
        AircraftTypeSnapshot? AircraftType,
        IReadOnlyList<ServiceSnapshot> PlannedServices);

    internal static async Task<Result<BuiltFlight>> BuildAsync(
        MasterDataResolver resolver,
        Guid customerId,
        Guid stationId,
        Guid operationTypeId,
        Guid? aircraftTypeId,
        string flightNumber,
        DateTimeOffset scheduledArrivalUtc,
        DateTimeOffset scheduledDepartureUtc,
        IReadOnlyList<Guid> plannedServiceIds,
        CancellationToken cancellationToken)
    {
        var customer = await resolver.CustomerAsync(customerId, cancellationToken);
        if (customer.IsFailure)
            return customer.Error;

        var station = await resolver.StationAsync(stationId, cancellationToken);
        if (station.IsFailure)
            return station.Error;

        var operationType = await resolver.OperationTypeAsync(operationTypeId, cancellationToken);
        if (operationType.IsFailure)
            return operationType.Error;

        var aircraft = await resolver.AircraftTypeAsync(aircraftTypeId, cancellationToken);
        if (aircraft.IsFailure)
            return aircraft.Error;

        var number = FlightNumber.Create(flightNumber);
        if (number.IsFailure)
            return number.Error;

        var schedule = ScheduledTime.Create(scheduledArrivalUtc, scheduledDepartureUtc);
        if (schedule.IsFailure)
            return schedule.Error;

        var services = await resolver.ServicesAsync(plannedServiceIds, cancellationToken);
        if (services.IsFailure)
            return services.Error;

        return new BuiltFlight(customer.Value, station.Value, operationType.Value, number.Value, schedule.Value, aircraft.Value, services.Value);
    }
}
