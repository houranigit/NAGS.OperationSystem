namespace Core.Contracts.Features.Customer;

public sealed record CustomerDto(
    Guid Id,
    string IataCode,
    string? IcaoCode,
    string Name,
    string? OfficialEmail,
    string? OfficialPhone,
    AddressDto? Address,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    IReadOnlyList<CustomerContactDto> Contacts,
    byte[]? Logo = null);
