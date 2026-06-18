namespace Core.Contracts.Features.Currency;

public sealed record CurrencyDto(
    Guid Id,
    string Code,
    string Name,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    IReadOnlyList<ExchangeRateDto> ExchangeRates);
