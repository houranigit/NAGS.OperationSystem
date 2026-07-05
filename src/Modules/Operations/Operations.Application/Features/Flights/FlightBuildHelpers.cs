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

    internal sealed record BuiltFlightReferences(
        CustomerSnapshot Customer,
        StationSnapshot Station,
        OperationTypeSnapshot OperationType,
        FlightNumber FlightNumber,
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
        var references = await BuildReferencesAsync(resolver, customerId, stationId, operationTypeId,
            aircraftTypeId, flightNumber, plannedServiceIds, cancellationToken);
        if (references.IsFailure)
            return references.Error;

        return BuildWithSchedule(references.Value, scheduledArrivalUtc, scheduledDepartureUtc);
    }

    internal static async Task<Result<BuiltFlightReferences>> BuildReferencesAsync(
        MasterDataResolver resolver,
        Guid customerId,
        Guid stationId,
        Guid operationTypeId,
        Guid? aircraftTypeId,
        string flightNumber,
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

        var services = await resolver.ServicesAsync(plannedServiceIds, cancellationToken);
        if (services.IsFailure)
            return services.Error;

        return new BuiltFlightReferences(customer.Value, station.Value, operationType.Value, number.Value, aircraft.Value, services.Value);
    }

    internal static Result<BuiltFlight> BuildWithSchedule(
        BuiltFlightReferences references,
        DateTimeOffset scheduledArrivalUtc,
        DateTimeOffset scheduledDepartureUtc)
    {
        var schedule = ScheduledTime.Create(scheduledArrivalUtc, scheduledDepartureUtc);
        if (schedule.IsFailure)
            return schedule.Error;

        return new BuiltFlight(
            Copy(references.Customer),
            Copy(references.Station),
            Copy(references.OperationType),
            FlightNumber.Create(references.FlightNumber.Value).Value,
            schedule.Value,
            references.AircraftType is null ? null : Copy(references.AircraftType),
            CopyServices(references.PlannedServices));
    }

    internal static IReadOnlyList<StaffMemberSnapshot> CopyStaffMembers(IReadOnlyList<StaffMemberSnapshot> employees) =>
        employees.Select(e => new StaffMemberSnapshot(e.StaffMemberId, e.FullName, e.EmployeeId)).ToList();

    private static CustomerSnapshot Copy(CustomerSnapshot snapshot) =>
        new(snapshot.CustomerId, snapshot.IataCode, snapshot.Name);

    private static StationSnapshot Copy(StationSnapshot snapshot) =>
        new(snapshot.StationId, snapshot.IataCode, snapshot.Name);

    private static OperationTypeSnapshot Copy(OperationTypeSnapshot snapshot) =>
        new(snapshot.OperationTypeId, snapshot.Name);

    private static AircraftTypeSnapshot Copy(AircraftTypeSnapshot snapshot) =>
        new(snapshot.AircraftTypeId, snapshot.Manufacturer, snapshot.Model);

    private static IReadOnlyList<ServiceSnapshot> CopyServices(IReadOnlyList<ServiceSnapshot> services)
    {
        var copies = new List<ServiceSnapshot>(services.Count);
        foreach (var service in services)
            copies.Add(new ServiceSnapshot(service.ServiceId, service.Name));
        return copies;
    }
}
