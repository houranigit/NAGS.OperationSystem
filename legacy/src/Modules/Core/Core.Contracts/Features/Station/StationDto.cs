using Core.Contracts.Features.Employee;

namespace Core.Contracts.Features.Station;

public sealed record StationDto(
    Guid Id,
    string IataCode,
    string? IcaoCode,
    string Name,
    string? City,
    bool IsActive,
    IReadOnlyList<EmployeeDto> AssignedEmployees,
    DateTime CreatedAt,
    DateTime? UpdatedAt);