using BuildingBlocks.Application.Abstractions.Queries;
using Core.Contracts.Features.Currency;

namespace Core.Application.Features.Currency.Queries.GetExchangeRatesForCurrency;

/// <summary>
/// Returns all exchange rates that have <paramref name="CurrencyId"/> as either the from or
/// to side. The contract wizard uses this to build a "plan currency → contract currency"
/// rate map and convert default plan prices to the contract's currency.
/// </summary>
public sealed record GetExchangeRatesForCurrencyQuery(Guid CurrencyId)
    : IQuery<IReadOnlyList<ExchangeRateRowDto>>;
