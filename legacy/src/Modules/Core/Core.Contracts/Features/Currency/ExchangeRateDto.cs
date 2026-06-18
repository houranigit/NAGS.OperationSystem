namespace Core.Contracts.Features.Currency;

public sealed record ExchangeRateDto(
    Guid Id,
    Guid ToCurrencyId,
    string ToCurrencyCode,
    string ToCurrencyName,
    decimal Rate,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
