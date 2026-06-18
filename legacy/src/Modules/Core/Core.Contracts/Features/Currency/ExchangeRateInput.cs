namespace Core.Contracts.Features.Currency;

/// <summary>
/// Exchange rate row for create/update currency commands. <see cref="Id"/> is set when updating an existing rate; null on create.
/// </summary>
public sealed record ExchangeRateInput(
    Guid? Id,
    Guid ToCurrencyId,
    decimal Rate);
