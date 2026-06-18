using Core.Contracts.Features.ManpowerType;
using Core.Contracts.Features.Station;
using Identity.Domain.Enumerations;

namespace Core.Contracts.Features.Employee;

public sealed record EmployeeDto(
    Guid Id,
    string FullName,
    string Email,
    ManpowerTypeSnapshot ManpowerTypeSnapshot,
    StationSnapshot StationSnapshot,
    DateOnly? ContractFrom,
    DateOnly? ContractTo,
    IReadOnlyList<DayOfWeek> WorkingDays,
    Guid? LinkedUserId,
    bool IsActive,
    UserType UserType,
    UserStatus UserStatus,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    IReadOnlyList<EmployeeLicenseDto> Licenses);