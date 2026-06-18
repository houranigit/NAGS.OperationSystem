namespace Core.Contracts.Features.Employee;

public sealed record EmployeeLicenseInput(
    Guid? Id,
    Guid LicenseId,
    string LicenseNumber);
