using Core.Domain.Enumerations;

namespace Core.Contracts.Features.AircraftType;

public sealed record AircraftTypeDto(
    Guid Id,
    Manufacturer Manufacturer,
    string Model,
    string? Notes,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
