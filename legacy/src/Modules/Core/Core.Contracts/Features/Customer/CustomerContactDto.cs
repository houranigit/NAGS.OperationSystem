namespace Core.Contracts.Features.Customer;

public sealed record CustomerContactDto(
    Guid Id,
    string Name,
    string Email,
    string? Phone,
    Guid? LinkedUserId,
    bool IsActive);
