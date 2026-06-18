namespace Core.Contracts.Features.Employee;

public sealed record EmployeeSelectOption(
    Guid Id,
    string FullName,
    string Email,
    string ManpowerTypeName);
