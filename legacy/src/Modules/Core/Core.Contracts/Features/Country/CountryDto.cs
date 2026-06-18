namespace Core.Contracts.Features.Country;

public sealed record CountryDto(
    Guid Id,
    string Code,
    string Name,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
