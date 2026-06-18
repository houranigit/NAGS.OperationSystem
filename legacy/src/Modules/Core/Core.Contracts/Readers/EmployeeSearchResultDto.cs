namespace Core.Contracts.Readers;

public sealed record EmployeeSearchResultDto(
    Guid EmployeeId,
    string FullName,
    string Email);
