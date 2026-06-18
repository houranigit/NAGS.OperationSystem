using Core.Contracts.Features.AircraftType;
using Core.Contracts.Features.Customer;
using Core.Contracts.Features.OperationType;
using Core.Contracts.Features.Station;
using Operations.Domain.Enumerations;

namespace Operations.Contracts.Flight;

public sealed record FlightLightDto(
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
    DateTimeOffset? CanceledAt);
