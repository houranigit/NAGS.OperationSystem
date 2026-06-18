using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Commands;
using Core.Contracts.Features.AircraftType;
using Core.Contracts.Features.Customer;
using Core.Contracts.Features.Employee;
using Core.Contracts.Features.OperationType;
using Core.Contracts.Features.Station;
using Identity.Domain.Authorization;

namespace Operations.Application.Features.Flight.Commands.UpdateFlight;

/// <summary>
/// Updates operational snapshot (customer through STD), aircraft type, and assigned employees only.
/// Does not change <see cref="Operations.Contracts.Flight.FlightDto.Status"/>, cancellation, <see cref="Operations.Contracts.Flight.FlightDto.AcceptedWorkOrderSnapshot"/>, or <see cref="Operations.Contracts.Flight.FlightDto.AttachedWorkOrders"/> — those have separate application logic.
/// </summary>
public sealed record UpdateFlightCommand(
    Guid Id,
    CustomerSnapshot CustomerSnapshot,
    StationSnapshot StationSnapshot,
    OperationTypeSnapshot OperationTypeSnapshot,
    AircraftTypeSnapshot? AircraftTypeId,
    string FlightNumber,
    DateTimeOffset Sta,
    DateTimeOffset Std,
    IReadOnlyList<EmployeeSnapshot> AssignedEmployees) : ICommand, IRequirePermission
{
    public string RequiredPermission => Permissions.Flights.Update;
}
