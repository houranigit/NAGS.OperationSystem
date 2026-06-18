using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Commands;
using Core.Contracts.Features.AircraftType;
using Core.Contracts.Features.Customer;
using Core.Contracts.Features.Employee;
using Core.Contracts.Features.OperationType;
using Core.Contracts.Features.Station;
using Identity.Domain.Authorization;

namespace Operations.Application.Features.Flight.Commands.CreateFlight;

/// <summary>
/// Flight identity and scheduling snapshot as on <see cref="Operations.Contracts.Flight.FlightDto"/> (customer through STD) plus crew assignment.
/// Status is always <see cref="Operations.Domain.Enumerations.FlightStatus.Scheduled"/>; <c>AcceptedWorkOrderSnapshot</c> and <c>AttachedWorkOrders</c> stay empty for new rows.
/// This command must not set or mutate work order or lifecycle state beyond that — other commands own those concerns.
/// </summary>
public sealed record CreateFlightCommand(
    CustomerSnapshot CustomerSnapshot,
    StationSnapshot StationSnapshot,
    OperationTypeSnapshot OperationTypeSnapshot,
    AircraftTypeSnapshot? AircraftTypeId,
    string FlightNumber,
    DateTimeOffset Sta,
    DateTimeOffset Std,
    IReadOnlyList<EmployeeSnapshot> AssignedEmployees) : ICommand<Guid>, IRequirePermission
{
    public string RequiredPermission => Permissions.Flights.Create;
}
