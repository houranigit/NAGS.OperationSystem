using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Commands;
using Core.Contracts.Features.AircraftType;
using Core.Contracts.Features.Customer;
using Core.Contracts.Features.Employee;
using Core.Contracts.Features.OperationType;
using Core.Contracts.Features.Station;
using Identity.Domain.Authorization;

namespace Operations.Application.Features.Flight.Commands.BatchCreateFlights;

/// <summary>
/// Same payload shape as <see cref="CreateFlight.CreateFlightCommand"/> per row; validates every item before inserting any (all-or-nothing).
/// Inserts only scheduled flights with no work order data, identical to single create semantics.
/// </summary>
public sealed record BatchCreateFlightsCommand(IReadOnlyList<BatchCreateFlightItem> Flights)
    : ICommand<IReadOnlyList<Guid>>, IRequirePermission
{
    public string RequiredPermission => Permissions.Flights.Create;
}

/// <inheritdoc cref="CreateFlight.CreateFlightCommand" />
public sealed record BatchCreateFlightItem(
    CustomerSnapshot CustomerSnapshot,
    StationSnapshot StationSnapshot,
    OperationTypeSnapshot OperationTypeSnapshot,
    AircraftTypeSnapshot? AircraftTypeId,
    string FlightNumber,
    DateTimeOffset Sta,
    DateTimeOffset Std,
    IReadOnlyList<EmployeeSnapshot> AssignedEmployees);
