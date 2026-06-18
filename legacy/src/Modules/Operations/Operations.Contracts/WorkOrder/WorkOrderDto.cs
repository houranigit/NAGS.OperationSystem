using Core.Contracts.Features.AircraftType;
using Core.Contracts.Features.Customer;
using Core.Contracts.Features.OperationType;
using Core.Contracts.Features.Station;
using Operations.Contracts.Flight;
using Operations.Domain.Enumerations;

namespace Operations.Contracts.WorkOrder;

public sealed record WorkOrderDto(
    Guid Id,
    string? WorkOrderNo,
    FlightSnapshot? FlightSnapshot,
    CustomerSnapshot CustomerSnapshot,
    StationSnapshot StationSnapshot,
    OperationTypeSnapshot OperationTypeSnapshot,
    AircraftTypeSnapshot? AircraftTypeId,
    string? AircraftTailNumber,
    string FlightNumber,
    DateTimeOffset Sta,
    DateTimeOffset Std,
    DateTimeOffset Ata,
    DateTimeOffset Atd,
    bool IsCanceled,
    DateTimeOffset? CanceledAt,
    WorkOrderStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    byte[]? CustomerSignature = null);
