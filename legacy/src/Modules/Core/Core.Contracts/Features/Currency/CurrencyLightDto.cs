namespace Core.Contracts.Features.Currency;

public sealed record CurrencyLightDto(
    Guid Id,
    string Code,
    string Name,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);