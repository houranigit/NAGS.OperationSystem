namespace Core.Contracts.Features.Currency;

/// <summary>
/// Single exchange-rate row exposed to the contract wizard so it can convert plan prices
/// from the plan's currency into the contract's currency.
/// </summary>
public sealed record ExchangeRateRowDto(
    Guid FromCurrencyId,
    Guid ToCurrencyId,
    decimal Rate,
    DateTime EffectiveAt);
