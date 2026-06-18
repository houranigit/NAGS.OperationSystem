using Operations.Domain.Enumerations;
using Operations.Contracts.WorkOrder;
using Core.Contracts.Features.Customer;
using Core.Contracts.Features.Station;
using Core.Contracts.Features.OperationType;
using Core.Contracts.Features.AircraftType;
using Core.Contracts.Features.Employee;
using Core.Contracts.Features.Service;

namespace Operations.Contracts.Flight;

public sealed record FlightDto(
    Guid Id,
    Guid? ContractId,
    string? ContractNumber,
    CustomerSnapshot CustomerSnapshot,
    StationSnapshot StationSnapshot,
    OperationTypeSnapshot OperationTypeSnapshot,
    AircraftTypeSnapshot? AircraftTypeId,
    string FlightNumber,
    DateTimeOffset Sta,
    DateTimeOffset Std,
    FlightStatus Status,
    DateTimeOffset? CanceledAt,
    WorkOrderSnapshot? AcceptedWorkOrderSnapshot,
    IReadOnlyList<EmployeeSnapshot> AssignedEmployees,
    IReadOnlyList<ServiceSnapshot> Services,
    IReadOnlyList<WorkOrderLightDto> AttachedWorkOrders,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
