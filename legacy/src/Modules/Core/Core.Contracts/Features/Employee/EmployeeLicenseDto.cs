using Core.Contracts.Features.License;

namespace Core.Contracts.Features.Employee;

public sealed record EmployeeLicenseDto(
    EmployeeSnapshot EmployeeSnapshot,
    LicenseSnapshot LicenseSnapshot,
    string LicenseNumber);
